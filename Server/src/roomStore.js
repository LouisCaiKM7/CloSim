// CloSim master server — in-memory room directory with TTL.
//
// v1 persistence is intentionally in-memory: public rooms are ephemeral (they exist only while a
// listen-server is up and heartbeating), so a durable DB is unnecessary. Rooms expire when
// heartbeats stop — after ROOM_TTL_SECONDS with no heartbeat, the reaper drops them and they
// disappear from GET /rooms.
//
// Scale-up path (documented, not built for v1): swap this class for a DynamoDB table with a TTL
// attribute (native item expiry) or a Redis keyspace with per-key EXPIRE. The public methods here
// (register/heartbeat/deregister/list) form the seam to reimplement.
//
// The stored object is the on-the-wire RoomInfo (enums serialized as ints by Unity JsonUtility).
// We keep it verbatim and echo it back, so A2's client deserializes cleanly.

import { randomUUID } from 'node:crypto';

// RoomVisibility enum (Online.Contracts): Public=0, Private=1.
const VISIBILITY_PRIVATE = 1;

export class RoomStore {
  /**
   * @param {{ roomTtlSeconds: number, maxRooms: number, now?: () => number }} opts
   */
  constructor({ roomTtlSeconds, maxRooms, now = Date.now }) {
    this.ttlMs = roomTtlSeconds * 1000;
    this.maxRooms = maxRooms;
    this._now = now;
    /** @type {Map<string, { room: object, expiresAt: number }>} */
    this._rooms = new Map();
  }

  /**
   * Register (or re-register / update) a public room. Honors the host-supplied roomId when present,
   * otherwise mints one. Refreshes TTL. Returns { ok, roomId, error }.
   * @param {object} room  the RoomInfo body sent by the host
   */
  register(room) {
    if (!room || typeof room !== 'object') {
      return { ok: false, roomId: '', error: 'invalid room body' };
    }

    const incomingId = typeof room.roomId === 'string' ? room.roomId.trim() : '';
    const isNew = incomingId === '' || !this._rooms.has(incomingId);

    // Enforce the directory cap only when adding a genuinely new room.
    if (isNew && this._rooms.size >= this.maxRooms) {
      // Try a sweep first in case the cap is stale.
      this.sweep();
      if (this._rooms.size >= this.maxRooms) {
        return { ok: false, roomId: incomingId, error: 'directory full' };
      }
    }

    const roomId = incomingId !== '' ? incomingId : randomUUID();

    // Normalize: keep the wire shape, force our authoritative roomId, coerce numeric fields.
    const stored = {
      ...room,
      roomId,
      port: toInt(room.port, 0),
      playerCount: toInt(room.playerCount, 0),
      capacity: toInt(room.capacity, 6),
      visibility: toInt(room.visibility, 0),
      state: toInt(room.state, 0),
      requiresToken: Boolean(room.requiresToken),
    };

    this._rooms.set(roomId, { room: stored, expiresAt: this._now() + this.ttlMs });
    return { ok: true, roomId, error: '' };
  }

  /**
   * Refresh a room's TTL. Returns true if the room existed (and was live), false otherwise.
   * @param {string} roomId
   */
  heartbeat(roomId) {
    const entry = this._rooms.get(roomId);
    if (!entry) return false;
    if (entry.expiresAt <= this._now()) {
      // Already lapsed but not yet swept — treat as gone.
      this._rooms.delete(roomId);
      return false;
    }
    entry.expiresAt = this._now() + this.ttlMs;
    return true;
  }

  /**
   * Remove a room. Returns true if it existed.
   * @param {string} roomId
   */
  deregister(roomId) {
    return this._rooms.delete(roomId);
  }

  /**
   * List live rooms matching a RoomQuery. IMPORTANT: never filters by version — version-mismatched
   * rooms MUST still be returned so the in-game Server List can grey them out (resolved decision).
   * @param {{ gameId?: string, region?: string, hideFull?: boolean, hidePrivate?: boolean }} query
   * @returns {object[]} array of RoomInfo
   */
  list(query = {}) {
    const now = this._now();
    const gameId = str(query.gameId);
    const region = str(query.region);
    const hideFull = Boolean(query.hideFull);
    // hidePrivate defaults to true (private rooms are never listed anyway).
    const hidePrivate = query.hidePrivate === undefined ? true : Boolean(query.hidePrivate);

    const out = [];
    for (const entry of this._rooms.values()) {
      if (entry.expiresAt <= now) continue; // lazily skip expired; reaper deletes them
      const r = entry.room;

      if (gameId !== '' && str(r.gameId) !== gameId) continue;
      if (region !== '' && str(r.region) !== region) continue;
      if (hideFull && toInt(r.playerCount, 0) >= toInt(r.capacity, 6)) continue;
      if (hidePrivate && toInt(r.visibility, 0) === VISIBILITY_PRIVATE) continue;

      out.push(r);
    }
    return out;
  }

  /** Delete all expired rooms. Returns the number reaped. */
  sweep() {
    const now = this._now();
    let reaped = 0;
    for (const [id, entry] of this._rooms) {
      if (entry.expiresAt <= now) {
        this._rooms.delete(id);
        reaped += 1;
      }
    }
    return reaped;
  }

  /** Live (non-expired) room count. */
  get size() {
    const now = this._now();
    let n = 0;
    for (const entry of this._rooms.values()) if (entry.expiresAt > now) n += 1;
    return n;
  }
}

function toInt(v, fallback) {
  const n = Number.parseInt(v, 10);
  return Number.isFinite(n) ? n : fallback;
}

function str(v) {
  return typeof v === 'string' ? v.trim() : '';
}
