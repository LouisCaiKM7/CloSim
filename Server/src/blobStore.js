// CloSim master server — replay BLOB storage (pluggable).
//
// Replay blobs are OPAQUE bytes (the format is owned by the in-game recorder/uploader; this service
// never parses them). Unlike ephemeral rooms, replays must PERSIST — so blobs live in Amazon S3.
//
// Two interchangeable implementations behind one interface:
//
//   • S3BlobStore     — production. Blobs go straight to/from S3 via short-lived PRESIGNED URLs, so
//                       large payloads never stream through this API. The AWS SDK is imported LAZILY
//                       (dynamic import) and ONLY when a bucket is configured, so local dev / tests
//                       never need the SDK installed and the process boots with no AWS at all.
//   • MemoryBlobStore — local dev / tests. Keeps blobs in memory and issues self-signed "presigned"
//                       URLs that point back at this API's own (unauthenticated, signature-gated)
//                       /replays/blob/:id endpoints — mirroring the S3 direct-PUT / direct-GET flow
//                       without any cloud dependency. NOT for production (single-process, volatile).
//
// Interface (all async so callers are storage-agnostic):
//   mode                              -> 's3' | 'memory'
//   presignPut(id, opts)  -> { url, method:'PUT', headers, expiresInSec }
//   presignGet(id, opts)  -> { url, method:'GET', expiresInSec }
//   remove(id)                        -> void (best-effort; idempotent)
// MemoryBlobStore additionally exposes put/get/verify used by the local blob endpoints.

import { createHmac, randomBytes, timingSafeEqual } from 'node:crypto';

const OCTET = 'application/octet-stream';

/**
 * Select the blob store from config. Blank bucket => in-memory dev store (graceful degrade — the
 * rest of the service still runs without AWS).
 * @param {object} cfg  runtime config (see config.js)
 * @param {{ s3Client?: object, presigner?: (client:object, command:object, opts:object)=>Promise<string>,
 *           commands?: object }} [inject]  test seam for a mocked S3
 */
export function createBlobStore(cfg = {}, inject = {}) {
  const bucket = str(cfg.replayS3Bucket);
  if (bucket === '') {
    return new MemoryBlobStore({ maxBytes: cfg.replayMaxSizeBytes });
  }
  return new S3BlobStore({
    bucket,
    region: str(cfg.replayS3Region),
    prefix: str(cfg.replayS3Prefix) || 'replays/',
    inject,
  });
}

// ---- In-memory dev store ------------------------------------------------------------------------

export class MemoryBlobStore {
  constructor({ maxBytes } = {}) {
    this.mode = 'memory';
    this.maxBytes = Number.isFinite(maxBytes) && maxBytes > 0 ? maxBytes : 0;
    /** @type {Map<string, { body: Buffer, contentType: string }>} */
    this._blobs = new Map();
    // Per-process signing secret for the self-signed URLs (rotates every boot — dev only).
    this._secret = randomBytes(32);
  }

  _sign(id, op, exp) {
    return createHmac('sha256', this._secret).update(`${id}\n${op}\n${exp}`).digest('hex');
  }

  /** Constant-time verification of a self-signed blob URL. */
  verify(id, op, exp, sig) {
    const expNum = Number.parseInt(exp, 10);
    if (!Number.isFinite(expNum) || expNum * 1000 < Date.now()) return false;
    const expected = this._sign(id, op, String(exp));
    const a = Buffer.from(String(sig || ''), 'utf8');
    const b = Buffer.from(expected, 'utf8');
    return a.length === b.length && timingSafeEqual(a, b);
  }

  _url(baseUrl, id, op, expiresInSec) {
    const exp = Math.floor(Date.now() / 1000) + expiresInSec;
    const sig = this._sign(id, op, String(exp));
    const base = str(baseUrl).replace(/\/+$/, '');
    return `${base}/replays/blob/${encodeURIComponent(id)}?op=${op}&exp=${exp}&sig=${sig}`;
  }

  // eslint-disable-next-line require-await
  async presignPut(id, { baseUrl, contentType = OCTET, expiresInSec = 900 } = {}) {
    return {
      url: this._url(baseUrl, id, 'put', expiresInSec),
      method: 'PUT',
      headers: { 'Content-Type': contentType },
      expiresInSec,
    };
  }

  // eslint-disable-next-line require-await
  async presignGet(id, { baseUrl, expiresInSec = 900 } = {}) {
    return {
      url: this._url(baseUrl, id, 'get', expiresInSec),
      method: 'GET',
      expiresInSec,
    };
  }

  put(id, body, contentType = OCTET) {
    this._blobs.set(id, { body: Buffer.from(body), contentType: contentType || OCTET });
  }

  get(id) {
    return this._blobs.get(id) || null;
  }

  // eslint-disable-next-line require-await
  async remove(id) {
    this._blobs.delete(id);
  }
}

// ---- S3 store (production) ----------------------------------------------------------------------

export class S3BlobStore {
  constructor({ bucket, region, prefix, inject = {} }) {
    this.mode = 's3';
    this.bucket = bucket;
    this.region = region;
    this.prefix = prefix;
    this._inject = inject;
    this._client = inject.s3Client || null;
    this._presigner = inject.presigner || null;
    this._commands = inject.commands || null;
  }

  _key(id) {
    return `${this.prefix}${id}.bin`;
  }

  // Lazily wire the AWS SDK. Imported here (not at module load) so a blank-bucket deploy never needs
  // the SDK and the process boots with no AWS. Injected client/presigner (tests) short-circuit this.
  async _ensure() {
    if (this._client && this._presigner && this._commands) return;
    const s3 = await import('@aws-sdk/client-s3');
    const presign = await import('@aws-sdk/s3-request-presigner');
    this._commands = this._commands || {
      Put: s3.PutObjectCommand,
      Get: s3.GetObjectCommand,
      Delete: s3.DeleteObjectCommand,
    };
    this._client = this._client || new s3.S3Client(this.region ? { region: this.region } : {});
    this._presigner = this._presigner || presign.getSignedUrl;
  }

  async presignPut(id, { contentType = OCTET, expiresInSec = 900 } = {}) {
    await this._ensure();
    const cmd = new this._commands.Put({ Bucket: this.bucket, Key: this._key(id), ContentType: contentType });
    const url = await this._presigner(this._client, cmd, { expiresIn: expiresInSec });
    return { url, method: 'PUT', headers: { 'Content-Type': contentType }, expiresInSec };
  }

  async presignGet(id, { expiresInSec = 900 } = {}) {
    await this._ensure();
    const cmd = new this._commands.Get({ Bucket: this.bucket, Key: this._key(id) });
    const url = await this._presigner(this._client, cmd, { expiresIn: expiresInSec });
    return { url, method: 'GET', expiresInSec };
  }

  async remove(id) {
    await this._ensure();
    const cmd = new this._commands.Delete({ Bucket: this.bucket, Key: this._key(id) });
    await this._client.send(cmd);
  }
}

function str(v) {
  return typeof v === 'string' ? v.trim() : '';
}
