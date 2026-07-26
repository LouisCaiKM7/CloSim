// CloSim master server — HTTP directory API (Express app factory).
//
// This is the EXACT wire contract A2's in-game MasterServerClient (CloSim.exe) speaks. Do not change
// shapes/paths/headers without updating that client + the A1 contract in lockstep.
//
//   POST   /rooms                  body = JSON of RoomInfo            -> { ok, roomId, error }
//   POST   /rooms/{id}/heartbeat   body = {}                         -> { ok } (refreshes TTL)
//   DELETE /rooms/{id}                                               -> { ok } (idempotent)
//   GET    /rooms?gameId&region&hideFull&hidePrivate                 -> { rooms: [RoomInfo, ...] }
//   GET    /healthz                (no auth)                         -> { ok, ... } JSON health
//
// Auth: every route except /healthz requires a valid X-Api-Key (see auth.js).
// There is intentionally NO web UI / HTML route: the only client is the game exe. GET / is 404 JSON.

import express from 'express';
import { makeApiKeyAuth } from './auth.js';

const SERVICE_NAME = 'closim-master-server';

/**
 * @param {{ store: import('./roomStore.js').RoomStore, config: object, startedAt?: number }} deps
 */
export function createApp({ store, config, startedAt = Date.now() }) {
  const app = express();
  app.disable('x-powered-by');
  app.disable('etag');

  // Parse JSON bodies (RoomInfo register payload, {} heartbeat). Small cap — rooms are tiny.
  app.use(
    express.json({
      limit: '16kb',
      // Tolerate an empty body on POSTs that don't need one (heartbeat sometimes sends "{}").
      type: ['application/json', 'application/*+json', 'text/json'],
    })
  );

  // ---- Health check (UNAUTHENTICATED, JSON only — never HTML) ---------------------------------
  app.get('/healthz', (_req, res) => {
    res.json({
      ok: true,
      service: SERVICE_NAME,
      status: 'healthy',
      rooms: store.size,
      region: config.regionLabel,
      roomTtlSeconds: config.roomTtlSeconds,
      heartbeatSeconds: config.heartbeatSeconds,
      uptimeSeconds: Math.round((Date.now() - startedAt) / 1000),
    });
  });

  // ---- Auth gate: everything below requires the client credential -----------------------------
  const auth = makeApiKeyAuth(config);
  app.use(auth);

  // ---- Register (host, public rooms only) -----------------------------------------------------
  // Body = JsonUtility.ToJson(RoomInfo). Enums (visibility/state) arrive as ints; we echo them back.
  app.post('/rooms', (req, res) => {
    const result = store.register(req.body);
    if (!result.ok) {
      const code = result.error === 'directory full' ? 503 : 400;
      return res.status(code).json(result);
    }
    return res.status(200).json(result); // { ok, roomId, error } — matches RegisterResponseDto
  });

  // ---- Heartbeat (host) -> refresh TTL --------------------------------------------------------
  app.post('/rooms/:id/heartbeat', (req, res) => {
    const alive = store.heartbeat(req.params.id);
    if (!alive) {
      // Room unknown or already expired. Client ignores the body but 404 lets a host re-register.
      return res.status(404).json({ ok: false, error: 'unknown or expired room' });
    }
    return res.status(200).json({ ok: true });
  });

  // ---- Deregister (host) — idempotent ---------------------------------------------------------
  app.delete('/rooms/:id', (req, res) => {
    store.deregister(req.params.id);
    return res.status(200).json({ ok: true });
  });

  // ---- List / browse (in-game Server List) ----------------------------------------------------
  // NOTE: version is deliberately NOT a filter — version-mismatched rooms are still returned so the
  // Server List can grey them out (resolved design decision).
  app.get('/rooms', (req, res) => {
    const query = {
      gameId: typeof req.query.gameId === 'string' ? req.query.gameId : '',
      region: typeof req.query.region === 'string' ? req.query.region : '',
      hideFull: req.query.hideFull === 'true',
      // hidePrivate defaults to true when absent; explicit "false" opts in to private (never listed anyway).
      hidePrivate: req.query.hidePrivate === undefined ? true : req.query.hidePrivate !== 'false',
    };
    const rooms = store.list(query);
    return res.status(200).json({ rooms }); // object form (client also tolerates a bare array)
  });

  // ---- No web UI: unknown routes and GET / are JSON 404, never an HTML page --------------------
  app.use((req, res) => {
    res.status(404).json({ ok: false, error: 'not found' });
  });

  // ---- Error handler: malformed JSON etc. -> JSON 400, never an HTML error page ---------------
  // eslint-disable-next-line no-unused-vars
  app.use((err, req, res, _next) => {
    const status = err.status || err.statusCode || 400;
    res.status(status).json({ ok: false, error: err.message || 'bad request' });
  });

  return app;
}

export { SERVICE_NAME };
