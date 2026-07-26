// CloSim master server — replay METADATA store (pluggable).
//
// Replay metadata is the small, queryable record for each uploaded replay (who, which game, scores,
// duration, size, when). The opaque replay BLOB itself lives in S3 (see blobStore.js) — this store
// only holds the JSON metadata and the pointer.
//
// Two interchangeable implementations behind one interface (mirrors the roomStore seam):
//
//   • InMemoryReplayMetaStore — DEFAULT. Local dev / tests, no database, no AWS. Bounded by a cap;
//                               oldest entries are evicted when full. Volatile (per process).
//   • DynamoReplayMetaStore    — production scale path. A DynamoDB table (partition key = replayId)
//                               gives durable, horizontally-shared metadata. The AWS SDK is imported
//                               LAZILY and ONLY when REPLAY_TABLE is set, so default boot needs no AWS.
//
// Interface (all async so callers are backend-agnostic):
//   create(meta)                      -> stored meta
//   get(id)                           -> meta | null
//   list({ userId, gameId, roomId, limit }) -> meta[]  (newest first)
//   remove(id)                        -> boolean (existed)
//   size (getter)                     -> number | undefined  (undefined = not cheaply known)
//
// The stored `meta` is the exact client-facing metadata shape (see replays.js normalizeMeta).

/**
 * Select the metadata store from config. Blank REPLAY_TABLE => in-memory (default).
 * @param {object} cfg
 * @param {{ dynamoClient?: object }} [inject]  test seam
 */
export function createReplayMetaStore(cfg = {}, inject = {}) {
  const table = str(cfg.replayTable);
  if (table === '') {
    return new InMemoryReplayMetaStore({ maxReplays: cfg.maxReplays });
  }
  return new DynamoReplayMetaStore({
    table,
    region: str(cfg.replayS3Region),
    inject,
  });
}

// ---- In-memory store (default) ------------------------------------------------------------------

export class InMemoryReplayMetaStore {
  constructor({ maxReplays } = {}) {
    this.maxReplays = Number.isFinite(maxReplays) && maxReplays > 0 ? maxReplays : 10000;
    /** @type {Map<string, { meta: object, seq: number }>} */
    this._byId = new Map();
    this._seq = 0;
  }

  // eslint-disable-next-line require-await
  async create(meta) {
    // Evict the oldest entries when at capacity (dev-only bound; production uses DynamoDB).
    while (this._byId.size >= this.maxReplays) {
      const oldest = this._oldestId();
      if (oldest == null) break;
      this._byId.delete(oldest);
    }
    this._byId.set(meta.replayId, { meta, seq: ++this._seq });
    return meta;
  }

  // eslint-disable-next-line require-await
  async get(id) {
    const entry = this._byId.get(id);
    return entry ? entry.meta : null;
  }

  // eslint-disable-next-line require-await
  async list({ userId, gameId, roomId, limit } = {}) {
    const uId = str(userId);
    const gId = str(gameId);
    const rId = str(roomId);
    const cap = clampLimit(limit);

    const rows = [];
    for (const entry of this._byId.values()) {
      const m = entry.meta;
      if (uId !== '' && str(m.userId) !== uId) continue;
      if (gId !== '' && str(m.gameId) !== gId) continue;
      if (rId !== '' && str(m.roomId) !== rId) continue;
      rows.push(entry);
    }
    // Newest first (higher seq = created later). createdAt ties broken deterministically by seq.
    rows.sort((a, b) => b.seq - a.seq);
    return rows.slice(0, cap).map((e) => e.meta);
  }

  // eslint-disable-next-line require-await
  async remove(id) {
    return this._byId.delete(id);
  }

  get size() {
    return this._byId.size;
  }

  _oldestId() {
    let oldestId = null;
    let oldestSeq = Infinity;
    for (const [id, entry] of this._byId) {
      if (entry.seq < oldestSeq) {
        oldestSeq = entry.seq;
        oldestId = id;
      }
    }
    return oldestId;
  }
}

// ---- DynamoDB store (production scale path) ------------------------------------------------------
//
// Table shape (provisioned by deploy/replays.tf):
//   partition key: replayId (S)
//   attributes:    the full metadata JSON + createdAtEpoch (N) for ordering.
//   For efficient per-user / per-game listing at scale, add GSIs (userId-index, gameId-index) with
//   createdAtEpoch as the sort key and Query them here. This adapter uses a bounded Scan+filter as a
//   simple, correct default; swap to Query on a GSI when volume warrants (documented, not required).

export class DynamoReplayMetaStore {
  constructor({ table, region, inject = {} }) {
    this.table = table;
    this.region = region;
    this._inject = inject;
    this._doc = inject.dynamoClient || null;
    this._cmds = null;
  }

  async _ensure() {
    if (this._doc && this._cmds) return;
    const ddb = await import('@aws-sdk/client-dynamodb');
    const lib = await import('@aws-sdk/lib-dynamodb');
    this._cmds = {
      Put: lib.PutCommand,
      Get: lib.GetCommand,
      Delete: lib.DeleteCommand,
      Scan: lib.ScanCommand,
    };
    if (!this._doc) {
      const base = new ddb.DynamoDBClient(this.region ? { region: this.region } : {});
      this._doc = lib.DynamoDBDocumentClient.from(base);
    }
  }

  async create(meta) {
    await this._ensure();
    const item = { ...meta, createdAtEpoch: Date.parse(meta.createdAt) || Date.now() };
    await this._doc.send(new this._cmds.Put({ TableName: this.table, Item: item }));
    return meta;
  }

  async get(id) {
    await this._ensure();
    const out = await this._doc.send(new this._cmds.Get({ TableName: this.table, Key: { replayId: id } }));
    if (!out.Item) return null;
    // eslint-disable-next-line no-unused-vars
    const { createdAtEpoch, ...meta } = out.Item;
    return meta;
  }

  async list({ userId, gameId, roomId, limit } = {}) {
    await this._ensure();
    const cap = clampLimit(limit);
    const names = {};
    const values = {};
    const filters = [];
    for (const [field, val] of [['userId', userId], ['gameId', gameId], ['roomId', roomId]]) {
      const v = str(val);
      if (v !== '') {
        names[`#${field}`] = field;
        values[`:${field}`] = v;
        filters.push(`#${field} = :${field}`);
      }
    }
    const params = { TableName: this.table };
    if (filters.length > 0) {
      params.FilterExpression = filters.join(' AND ');
      params.ExpressionAttributeNames = names;
      params.ExpressionAttributeValues = values;
    }
    const out = await this._doc.send(new this._cmds.Scan(params));
    const rows = (out.Items || []).sort((a, b) => (b.createdAtEpoch || 0) - (a.createdAtEpoch || 0));
    return rows.slice(0, cap).map(({ createdAtEpoch, ...meta }) => meta); // eslint-disable-line no-unused-vars
  }

  async remove(id) {
    await this._ensure();
    // ReturnValues ALL_OLD lets us report whether it existed (best-effort).
    const out = await this._doc.send(
      new this._cmds.Delete({ TableName: this.table, Key: { replayId: id }, ReturnValues: 'ALL_OLD' })
    );
    return Boolean(out.Attributes);
  }

  // Not cheaply known for a DynamoDB table without a counter — surfaced as undefined in /healthz.
  get size() {
    return undefined;
  }
}

function clampLimit(limit) {
  const n = Number.parseInt(limit, 10);
  if (!Number.isFinite(n) || n <= 0) return 50; // default page
  return Math.min(n, 200); // hard cap
}

function str(v) {
  return typeof v === 'string' ? v.trim() : '';
}
