// CloSim master server — replay upload/list/download routes.
//
// This is the EXACT wire contract the in-game replay recorder/uploader speaks. Unlike rooms (which
// are ephemeral and in-memory), replays PERSIST: metadata in a pluggable store, the opaque blob in
// S3. Large blobs never stream through this API — the client uploads/downloads them DIRECTLY via
// short-lived PRESIGNED URLs (the preferred two-step "create metadata -> presigned PUT" flow).
//
//   POST   /replays          body = replay metadata (JSON)  -> { ok, replayId, upload:{url,method,headers,expiresInSec} }
//   GET    /replays?userId&gameId&roomId&limit               -> { replays: [<metadata>...] }  (newest first)
//   GET    /replays/{id}                                     -> { ok, replay:<metadata>, download:{url,method,expiresInSec} }
//   DELETE /replays/{id}                                     -> { ok }  (idempotent; owner-gated)
//
// Memory-mode ONLY (dev/tests), mounted UNAUTHENTICATED before the X-Api-Key gate because the URL
// carries its own signature (exactly like a real S3 presigned URL, which S3 serves, not this API):
//   PUT    /replays/blob/{id}?op=put&exp&sig                 (opaque bytes in)
//   GET    /replays/blob/{id}?op=get&exp&sig                 (opaque bytes out)
//
// Metadata shape (aligned with the client contract):
//   { replayId, userId, gameId, mode:{blue,red}, sceneName, durationSec,
//     finalScore:{blue,red}, sizeBytes, createdAt, schemaVersion, roomId? }

import express from 'express';
import { randomUUID } from 'node:crypto';

const OCTET = 'application/octet-stream';
const DEFAULT_SCHEMA_VERSION = 1;

/**
 * Mount the memory-mode blob endpoints (dev/tests). No-op unless the blob store is in-memory.
 * MUST be mounted BEFORE the global JSON body parser (raw bytes) and BEFORE the auth gate.
 * @param {import('express').Express} app
 * @param {import('./blobStore.js').MemoryBlobStore} blobStore
 * @param {object} cfg
 */
export function mountMemoryBlobRoutes(app, blobStore, cfg = {}) {
  if (!blobStore || blobStore.mode !== 'memory') return;
  const limit = clampInt(cfg.replayMaxSizeBytes, 50 * 1024 * 1024);

  // Upload: opaque bytes straight into the dev blob store, gated only by the URL signature.
  app.put('/replays/blob/:id', rawParser(limit), (req, res) => {
    const { id } = req.params;
    if (!blobStore.verify(id, 'put', req.query.exp, req.query.sig)) {
      return res.status(403).json({ ok: false, error: 'invalid or expired upload signature' });
    }
    const body = Buffer.isBuffer(req.body) ? req.body : Buffer.alloc(0);
    if (limit && body.length > limit) {
      return res.status(413).json({ ok: false, error: 'replay blob too large' });
    }
    blobStore.put(id, body, req.get('content-type') || OCTET);
    return res.status(200).json({ ok: true, sizeBytes: body.length });
  });

  // Download: serve the opaque bytes back, gated only by the URL signature (never X-Api-Key).
  app.get('/replays/blob/:id', (req, res) => {
    const { id } = req.params;
    if (!blobStore.verify(id, 'get', req.query.exp, req.query.sig)) {
      return res.status(403).json({ ok: false, error: 'invalid or expired download signature' });
    }
    const blob = blobStore.get(id);
    if (!blob) return res.status(404).json({ ok: false, error: 'replay blob not found' });
    res.setHeader('Content-Type', blob.contentType || OCTET);
    return res.status(200).send(blob.body);
  });

  // Raw body parser for the opaque blob bytes (any content-type).
  function rawParser(max) {
    return express.raw({ type: () => true, limit: max });
  }
}

/**
 * Register the authenticated replay routes on the app. Must be mounted AFTER the X-Api-Key gate.
 * @param {import('express').Express} app
 * @param {{ metaStore: object, blobStore: object, config: object }} deps
 */
export function registerReplayRoutes(app, { metaStore, blobStore, config = {} }) {
  const uploadTtl = clampInt(config.replayUploadUrlTtlSeconds, 900);
  const downloadTtl = clampInt(config.replayDownloadUrlTtlSeconds, 900);
  const maxSize = clampInt(config.replayMaxSizeBytes, 50 * 1024 * 1024);

  // ---- Create metadata + hand back a presigned upload URL (preferred two-step flow) -------------
  app.post('/replays', async (req, res, next) => {
    try {
      const parsed = normalizeMeta(req.body, { maxSize });
      if (!parsed.ok) return res.status(400).json({ ok: false, error: parsed.error });

      const meta = parsed.meta;
      await metaStore.create(meta);

      const upload = await blobStore.presignPut(meta.replayId, {
        baseUrl: publicBaseUrl(req),
        contentType: OCTET,
        expiresInSec: uploadTtl,
      });
      // { ok, replayId, upload } — the client PUTs the opaque blob straight to upload.url.
      return res.status(200).json({ ok: true, replayId: meta.replayId, upload });
    } catch (err) {
      return next(err);
    }
  });

  // ---- List (newest first), filterable ----------------------------------------------------------
  app.get('/replays', async (req, res, next) => {
    try {
      const replays = await metaStore.list({
        userId: req.query.userId,
        gameId: req.query.gameId,
        roomId: req.query.roomId,
        limit: req.query.limit,
      });
      return res.status(200).json({ replays }); // object wrapper, mirrors GET /rooms
    } catch (err) {
      return next(err);
    }
  });

  // ---- Get one: metadata + a short-lived presigned GET URL for the blob -------------------------
  app.get('/replays/:id', async (req, res, next) => {
    try {
      const meta = await metaStore.get(req.params.id);
      if (!meta) return res.status(404).json({ ok: false, error: 'replay not found' });
      const download = await blobStore.presignGet(meta.replayId, {
        baseUrl: publicBaseUrl(req),
        expiresInSec: downloadTtl,
      });
      return res.status(200).json({ ok: true, replay: meta, download });
    } catch (err) {
      return next(err);
    }
  });

  // ---- Delete: idempotent, owner-gated ----------------------------------------------------------
  // Ownership is asserted by the caller's userId (query `?userId=` or `X-User-Id` header) matching the
  // replay's recorded owner. Deleting a non-existent replay is a no-op success (idempotent).
  app.delete('/replays/:id', async (req, res, next) => {
    try {
      const id = req.params.id;
      const requester = str(req.query.userId) || str(req.get('x-user-id'));
      const existing = await metaStore.get(id);
      if (!existing) return res.status(200).json({ ok: true }); // idempotent: already gone

      if (str(existing.userId) !== '') {
        if (requester === '') {
          return res.status(403).json({ ok: false, error: 'owner-gated: provide userId (query or X-User-Id)' });
        }
        if (requester !== str(existing.userId)) {
          return res.status(403).json({ ok: false, error: 'forbidden: not the replay owner' });
        }
      }
      await blobStore.remove(id).catch(() => {}); // best-effort blob cleanup
      await metaStore.remove(id);
      return res.status(200).json({ ok: true });
    } catch (err) {
      return next(err);
    }
  });
}

// ---- Metadata validation / normalization --------------------------------------------------------
//
// The server is authoritative for replayId, createdAt, and schemaVersion defaults; everything else is
// coerced to the contract shape. Unknown extra fields are dropped so the stored record stays clean.
export function normalizeMeta(body, { maxSize } = {}) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) {
    return { ok: false, error: 'invalid metadata body' };
  }

  const userId = str(body.userId);
  const gameId = str(body.gameId);
  if (userId === '') return { ok: false, error: 'userId is required' };
  if (gameId === '') return { ok: false, error: 'gameId is required' };

  const sizeBytes = toInt(body.sizeBytes, 0);
  if (maxSize && sizeBytes > maxSize) {
    return { ok: false, error: `sizeBytes exceeds limit (${maxSize})` };
  }

  const meta = {
    replayId: randomUUID(), // authoritative — client-supplied ids are ignored
    userId,
    gameId,
    mode: {
      blue: toInt(body.mode && body.mode.blue, 0),
      red: toInt(body.mode && body.mode.red, 0),
    },
    sceneName: str(body.sceneName),
    durationSec: toNum(body.durationSec, 0),
    finalScore: {
      blue: toInt(body.finalScore && body.finalScore.blue, 0),
      red: toInt(body.finalScore && body.finalScore.red, 0),
    },
    sizeBytes,
    createdAt: new Date().toISOString(), // authoritative server timestamp
    schemaVersion: toInt(body.schemaVersion, DEFAULT_SCHEMA_VERSION),
  };
  // roomId is optional (not in the core shape) but supported as a list filter when present.
  const roomId = str(body.roomId);
  if (roomId !== '') meta.roomId = roomId;

  return { ok: true, meta };
}

function publicBaseUrl(req) {
  // Honor a proxy's forwarded proto/host when present, else the request's own.
  const proto = str(req.get('x-forwarded-proto')) || req.protocol || 'http';
  const host = str(req.get('x-forwarded-host')) || req.get('host') || 'localhost';
  return `${proto}://${host}`;
}

function clampInt(v, fallback) {
  const n = Number.parseInt(v, 10);
  return Number.isFinite(n) && n > 0 ? n : fallback;
}
function toInt(v, fallback) {
  const n = Number.parseInt(v, 10);
  return Number.isFinite(n) ? n : fallback;
}
function toNum(v, fallback) {
  const n = Number(v);
  return Number.isFinite(n) ? n : fallback;
}
function str(v) {
  return typeof v === 'string' ? v.trim() : '';
}
