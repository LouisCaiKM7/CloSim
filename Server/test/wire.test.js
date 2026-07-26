// CloSim master server — contract + TTL tests (node --test, no external deps).
//
// Covers: the exact HTTP wire the game client speaks, X-Api-Key gating, RoomQuery filters,
// the "never filter by version" rule, and TTL expiry (via an injected clock on RoomStore).

import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { RoomStore } from '../src/roomStore.js';
import { createApp } from '../src/app.js';

const API_KEY = 'test-key-123';

const CONFIG = {
  clientApiKeys: [API_KEY],
  allowInsecureNoAuth: false,
  regionLabel: 'test',
  roomTtlSeconds: 45,
  heartbeatSeconds: 15,
};

// Minimal RoomInfo like Unity JsonUtility emits (enums as ints).
function roomInfo(overrides = {}) {
  return {
    roomId: '',
    name: 'Test Room',
    hostName: 'Alice',
    address: '203.0.113.5',
    port: 7777,
    gameId: 'Reefscape',
    region: 'us-east',
    playerCount: 1,
    capacity: 6,
    visibility: 0, // Public
    requiresToken: false,
    state: 0, // Lobby
    version: '1.0.0',
    ...overrides,
  };
}

let server;
let base;

before(async () => {
  const store = new RoomStore({ roomTtlSeconds: 45, maxRooms: 100 });
  const app = createApp({ store, config: CONFIG });
  await new Promise((resolve) => {
    server = app.listen(0, '127.0.0.1', resolve);
  });
  const { port } = server.address();
  base = `http://127.0.0.1:${port}`;
});

after(() => server && server.close());

function req(path, opts = {}) {
  const headers = { 'Content-Type': 'application/json', ...(opts.headers || {}) };
  return fetch(base + path, { ...opts, headers });
}
function auth(extra = {}) {
  return { 'X-Api-Key': API_KEY, ...extra };
}

test('healthz is unauthenticated JSON and never HTML', async () => {
  const res = await fetch(base + '/healthz');
  assert.equal(res.status, 200);
  assert.match(res.headers.get('content-type'), /application\/json/);
  const body = await res.json();
  assert.equal(body.ok, true);
  assert.equal(body.service, 'closim-master-server');
});

test('requests without X-Api-Key are rejected 401', async () => {
  const res = await req('/rooms');
  assert.equal(res.status, 401);
  const body = await res.json();
  assert.equal(body.ok, false);
});

test('register -> returns {ok, roomId, error} and mints an id when blank', async () => {
  const res = await req('/rooms', {
    method: 'POST',
    headers: auth(),
    body: JSON.stringify(roomInfo()),
  });
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.ok, true);
  assert.ok(body.roomId && body.roomId.length > 0);
  assert.equal(body.error, '');
});

test('register honors a host-supplied roomId', async () => {
  const res = await req('/rooms', {
    method: 'POST',
    headers: auth(),
    body: JSON.stringify(roomInfo({ roomId: 'host-abc' })),
  });
  const body = await res.json();
  assert.equal(body.roomId, 'host-abc');
});

test('list returns { rooms: [...] } wrapper and honors filters', async () => {
  // Register a few distinct rooms.
  await req('/rooms', { method: 'POST', headers: auth(), body: JSON.stringify(roomInfo({ roomId: 'r-reef', gameId: 'Reefscape' })) });
  await req('/rooms', { method: 'POST', headers: auth(), body: JSON.stringify(roomInfo({ roomId: 'r-rebuilt', gameId: 'Rebuilt' })) });
  await req('/rooms', { method: 'POST', headers: auth(), body: JSON.stringify(roomInfo({ roomId: 'r-full', gameId: 'Reefscape', playerCount: 6, capacity: 6 })) });

  const all = await (await req('/rooms', { headers: auth() })).json();
  assert.ok(Array.isArray(all.rooms));
  assert.ok(all.rooms.some((r) => r.roomId === 'r-reef'));

  const reef = await (await req('/rooms?gameId=Reefscape', { headers: auth() })).json();
  assert.ok(reef.rooms.every((r) => r.gameId === 'Reefscape'));
  assert.ok(!reef.rooms.some((r) => r.roomId === 'r-rebuilt'));

  const notFull = await (await req('/rooms?hideFull=true', { headers: auth() })).json();
  assert.ok(!notFull.rooms.some((r) => r.roomId === 'r-full'));
});

test('version-mismatched rooms are NOT dropped from the list', async () => {
  await req('/rooms', { method: 'POST', headers: auth(), body: JSON.stringify(roomInfo({ roomId: 'r-oldver', version: '0.0.1-old' })) });
  // Even if a client somehow passed version=, it must not filter.
  const res = await (await req('/rooms?version=9.9.9', { headers: auth() })).json();
  assert.ok(res.rooms.some((r) => r.roomId === 'r-oldver'), 'old-version room must remain listed');
});

test('heartbeat refreshes, deregister removes, unknown heartbeat is 404', async () => {
  await req('/rooms', { method: 'POST', headers: auth(), body: JSON.stringify(roomInfo({ roomId: 'r-hb' })) });
  const hb = await req('/rooms/r-hb/heartbeat', { method: 'POST', headers: auth(), body: '{}' });
  assert.equal(hb.status, 200);

  const del = await req('/rooms/r-hb', { method: 'DELETE', headers: auth() });
  assert.equal(del.status, 200);

  const gone = await (await req('/rooms', { headers: auth() })).json();
  assert.ok(!gone.rooms.some((r) => r.roomId === 'r-hb'));

  const miss = await req('/rooms/does-not-exist/heartbeat', { method: 'POST', headers: auth(), body: '{}' });
  assert.equal(miss.status, 404);
});

test('unknown route is JSON 404, not an HTML page (no web UI)', async () => {
  const res = await req('/', { headers: auth() });
  assert.equal(res.status, 404);
  assert.match(res.headers.get('content-type'), /application\/json/);
});

// ---- TTL logic (deterministic via injected clock) --------------------------------------------
test('RoomStore expires rooms after TTL with no heartbeat', () => {
  let t = 1_000_000;
  const store = new RoomStore({ roomTtlSeconds: 45, maxRooms: 10, now: () => t });
  store.register(roomInfo({ roomId: 'ttl-room' }));
  assert.equal(store.list().length, 1);

  t += 44_000; // still within TTL
  assert.equal(store.list().length, 1);

  t += 2_000; // now past 45s
  assert.equal(store.list().length, 0, 'room should be gone after TTL');
  assert.equal(store.sweep(), 1, 'reaper deletes the expired entry');
});

test('RoomStore heartbeat resets the TTL window', () => {
  let t = 0;
  const store = new RoomStore({ roomTtlSeconds: 45, maxRooms: 10, now: () => t });
  store.register(roomInfo({ roomId: 'hb-room' }));
  t += 40_000;
  assert.equal(store.heartbeat('hb-room'), true);
  t += 40_000; // 80s since register, but only 40s since heartbeat
  assert.equal(store.list().length, 1, 'heartbeat kept it alive');
});
