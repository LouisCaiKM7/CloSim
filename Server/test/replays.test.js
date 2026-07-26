// CloSim master server — replay route tests (node --test, no external deps beyond express).
//
// Covers: the exact HTTP wire the in-game recorder/uploader speaks, X-Api-Key gating, metadata
// validation + server-authoritative fields, the full presigned upload/download flow against the
// in-memory dev blob store, list filters + newest-first ordering, idempotent + owner-gated delete,
// and the presign flow against a MOCKED S3 (injected client/presigner) — no AWS required.

import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { RoomStore } from '../src/roomStore.js';
import { createApp } from '../src/app.js';
import { createBlobStore, S3BlobStore } from '../src/blobStore.js';
import { createReplayMetaStore, DynamoReplayMetaStore } from '../src/replayStore.js';

const API_KEY = 'test-key-123';

const CONFIG = {
  clientApiKeys: [API_KEY],
  allowInsecureNoAuth: false,
  regionLabel: 'test',
  roomTtlSeconds: 45,
  heartbeatSeconds: 15,
  // Replay config: blank bucket/table => in-memory dev stores.
  replayS3Bucket: '',
  replayTable: '',
  replayUploadUrlTtlSeconds: 900,
  replayDownloadUrlTtlSeconds: 900,
  replayMaxSizeBytes: 50 * 1024 * 1024,
  maxReplays: 10000,
};

function meta(overrides = {}) {
  return {
    userId: 'user-alice',
    gameId: 'Reefscape',
    mode: { blue: 3, red: 3 },
    sceneName: 'Reefscape',
    durationSec: 150,
    finalScore: { blue: 88, red: 74 },
    sizeBytes: 4096,
    schemaVersion: 1,
    ...overrides,
  };
}

// ---- In-memory (dev) app -----------------------------------------------------------------------

let server;
let base;

before(async () => {
  const store = new RoomStore({ roomTtlSeconds: 45, maxRooms: 100 });
  const app = createApp({
    store,
    config: CONFIG,
    replayMetaStore: createReplayMetaStore(CONFIG),
    blobStore: createBlobStore(CONFIG),
  });
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
function post(body, extra = {}) {
  return req('/replays', { method: 'POST', headers: auth(extra), body: JSON.stringify(body) });
}

test('replay routes require X-Api-Key (401 without it)', async () => {
  assert.equal((await req('/replays')).status, 401);
  assert.equal((await req('/replays', { method: 'POST', body: '{}' })).status, 401);
});

test('POST /replays validates required fields (400)', async () => {
  const noUser = await post(meta({ userId: '' }));
  assert.equal(noUser.status, 400);
  const noGame = await post(meta({ gameId: '' }));
  assert.equal(noGame.status, 400);
});

test('POST /replays rejects a blob larger than the cap (400)', async () => {
  const res = await post(meta({ sizeBytes: 999 * 1024 * 1024 }));
  assert.equal(res.status, 400);
  assert.match((await res.json()).error, /sizeBytes/);
});

test('POST /replays returns { ok, replayId, upload } with a presigned PUT', async () => {
  const res = await post(meta());
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.ok, true);
  assert.ok(body.replayId && body.replayId.length > 0);
  assert.ok(body.upload && typeof body.upload.url === 'string');
  assert.equal(body.upload.method, 'PUT');
  assert.ok(body.upload.expiresInSec > 0);
});

test('server is authoritative for replayId / createdAt (ignores client-supplied)', async () => {
  const res = await post(meta({ replayId: 'client-forged-id', createdAt: '1999-01-01T00:00:00Z' }));
  const { replayId } = await res.json();
  assert.notEqual(replayId, 'client-forged-id');
  const got = await (await req(`/replays/${replayId}`, { headers: auth() })).json();
  assert.equal(got.replay.replayId, replayId);
  assert.notEqual(got.replay.createdAt, '1999-01-01T00:00:00Z');
  assert.ok(Date.parse(got.replay.createdAt) > Date.parse('2020-01-01'));
});

test('full presigned upload -> get -> download round-trips the opaque blob', async () => {
  // 1) create metadata + get an upload URL
  const created = await (await post(meta({ userId: 'roundtrip', sizeBytes: 11 }))).json();
  const replayId = created.replayId;

  // 2) PUT the opaque blob straight to the presigned URL (no X-Api-Key — signature-gated)
  const payload = Buffer.from('REPLAY_BYTES');
  const put = await fetch(created.upload.url, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/octet-stream' },
    body: payload,
  });
  assert.equal(put.status, 200);

  // 3) GET the replay -> metadata + presigned download URL
  const detail = await (await req(`/replays/${replayId}`, { headers: auth() })).json();
  assert.equal(detail.ok, true);
  assert.equal(detail.replay.replayId, replayId);
  assert.equal(detail.download.method, 'GET');

  // 4) download the blob directly and compare bytes
  const dl = await fetch(detail.download.url);
  assert.equal(dl.status, 200);
  assert.match(dl.headers.get('content-type'), /application\/octet-stream/);
  const bytes = Buffer.from(await dl.arrayBuffer());
  assert.equal(bytes.toString(), 'REPLAY_BYTES');
});

test('blob endpoints reject a tampered/invalid signature (403)', async () => {
  const created = await (await post(meta())).json();
  const badUrl = created.upload.url.replace(/sig=[a-f0-9]+/, 'sig=deadbeef');
  const put = await fetch(badUrl, { method: 'PUT', body: Buffer.from('x') });
  assert.equal(put.status, 403);
});

test('GET /replays/{id} is 404 for an unknown id', async () => {
  const res = await req('/replays/does-not-exist', { headers: auth() });
  assert.equal(res.status, 404);
  assert.match(res.headers.get('content-type'), /application\/json/);
});

test('GET /replays lists newest-first and honors userId/gameId/roomId/limit filters', async () => {
  await post(meta({ userId: 'filt', gameId: 'Reefscape', roomId: 'room-1' }));
  await post(meta({ userId: 'filt', gameId: 'Rebuilt', roomId: 'room-2' }));
  const lastRes = await (await post(meta({ userId: 'filt', gameId: 'Reefscape', roomId: 'room-1' }))).json();

  const byUser = await (await req('/replays?userId=filt', { headers: auth() })).json();
  assert.ok(Array.isArray(byUser.replays));
  assert.ok(byUser.replays.every((r) => r.userId === 'filt'));
  // newest first: the most recently created 'filt' replay is first
  assert.equal(byUser.replays[0].replayId, lastRes.replayId);

  const byGame = await (await req('/replays?userId=filt&gameId=Rebuilt', { headers: auth() })).json();
  assert.ok(byGame.replays.every((r) => r.gameId === 'Rebuilt'));

  const byRoom = await (await req('/replays?userId=filt&roomId=room-2', { headers: auth() })).json();
  assert.ok(byRoom.replays.every((r) => r.roomId === 'room-2'));

  const limited = await (await req('/replays?userId=filt&limit=1', { headers: auth() })).json();
  assert.equal(limited.replays.length, 1);
});

test('DELETE /replays/{id} is owner-gated and idempotent', async () => {
  const created = await (await post(meta({ userId: 'owner-bob' }))).json();
  const id = created.replayId;

  // wrong owner -> 403
  const wrong = await req(`/replays/${id}?userId=someone-else`, { method: 'DELETE', headers: auth() });
  assert.equal(wrong.status, 403);

  // missing owner claim -> 403
  const missing = await req(`/replays/${id}`, { method: 'DELETE', headers: auth() });
  assert.equal(missing.status, 403);

  // correct owner -> 200
  const ok = await req(`/replays/${id}?userId=owner-bob`, { method: 'DELETE', headers: auth() });
  assert.equal(ok.status, 200);

  // gone now
  assert.equal((await req(`/replays/${id}`, { headers: auth() })).status, 404);

  // idempotent: deleting again is still 200
  const again = await req(`/replays/${id}?userId=owner-bob`, { method: 'DELETE', headers: auth() });
  assert.equal(again.status, 200);
});

// ---- Presign flow against a MOCKED S3 (no AWS) --------------------------------------------------

test('S3 mode: POST/GET/DELETE use the presigner + client (mocked)', async () => {
  const calls = { put: [], get: [], sent: [] };
  // Fake command classes just record their params.
  class Put { constructor(p) { this.p = p; calls.put.push(p); } }
  class Get { constructor(p) { this.p = p; calls.get.push(p); } }
  class Delete { constructor(p) { this.p = p; } }
  const commands = { Put, Get, Delete };
  const s3Client = { send: async (cmd) => { calls.sent.push(cmd.p); return {}; } };
  const presigner = async (_client, cmd, opts) => {
    const kind = cmd instanceof Put ? 'put' : 'get';
    return `https://s3.example/${cmd.p.Key}?kind=${kind}&exp=${opts.expiresIn}`;
  };

  const s3Config = { ...CONFIG, replayS3Bucket: 'closim-replays-test', replayS3Region: 'us-east-1' };
  const blobStore = new S3BlobStore({
    bucket: 'closim-replays-test',
    region: 'us-east-1',
    prefix: 'replays/',
    inject: { s3Client, presigner, commands },
  });

  const store = new RoomStore({ roomTtlSeconds: 45, maxRooms: 100 });
  const app = createApp({
    store,
    config: s3Config,
    replayMetaStore: createReplayMetaStore(s3Config),
    blobStore,
  });
  const srv = await new Promise((resolve) => {
    const s = app.listen(0, '127.0.0.1', () => resolve(s));
  });
  try {
    const { port } = srv.address();
    const b = `http://127.0.0.1:${port}`;
    const h = { 'Content-Type': 'application/json', 'X-Api-Key': API_KEY };

    // POST -> presigned S3 PUT URL (points at S3, not this API)
    const created = await (await fetch(`${b}/replays`, { method: 'POST', headers: h, body: JSON.stringify(meta()) })).json();
    assert.match(created.upload.url, /^https:\/\/s3\.example\/replays\/.*\.bin\?kind=put/);
    assert.equal(calls.put.length, 1);
    assert.equal(calls.put[0].Bucket, 'closim-replays-test');

    // GET -> presigned S3 GET URL
    const detail = await (await fetch(`${b}/replays/${created.replayId}`, { headers: h })).json();
    assert.match(detail.download.url, /kind=get/);
    assert.equal(calls.get.length, 1);

    // DELETE -> issues an S3 delete via client.send
    const del = await fetch(`${b}/replays/${created.replayId}?userId=user-alice`, { method: 'DELETE', headers: h });
    assert.equal(del.status, 200);
    assert.equal(calls.sent.length, 1);
    assert.equal(calls.sent[0].Bucket, 'closim-replays-test');
  } finally {
    srv.close();
  }
});

// ---- DynamoDB metadata adapter (injected fake DocumentClient, no AWS) --------------------------

test('DynamoReplayMetaStore builds correct commands and lists newest-first', async () => {
  const table = {}; // fake table keyed by replayId
  // Fake DynamoDBDocumentClient: dispatch on the real command class name + its .input.
  const doc = {
    async send(cmd) {
      const name = cmd.constructor.name;
      const input = cmd.input;
      if (name === 'PutCommand') {
        table[input.Item.replayId] = input.Item;
        return {};
      }
      if (name === 'GetCommand') {
        return { Item: table[input.Key.replayId] || undefined };
      }
      if (name === 'DeleteCommand') {
        const existed = table[input.Key.replayId];
        delete table[input.Key.replayId];
        return { Attributes: existed };
      }
      if (name === 'ScanCommand') {
        let items = Object.values(table);
        // honor a simple equality FilterExpression on userId/gameId/roomId
        if (input.FilterExpression) {
          for (const [k, v] of Object.entries(input.ExpressionAttributeValues || {})) {
            const field = k.slice(1); // ":userId" -> "userId"
            items = items.filter((it) => it[field] === v);
          }
        }
        return { Items: items };
      }
      throw new Error(`unexpected command ${name}`);
    },
  };

  const s = new DynamoReplayMetaStore({ table: 'closim-replays', region: 'us-east-1', inject: { dynamoClient: doc } });
  await s.create({ replayId: 'a', userId: 'u1', gameId: 'G', createdAt: '2026-01-01T00:00:00.000Z' });
  await s.create({ replayId: 'b', userId: 'u1', gameId: 'G', createdAt: '2026-02-01T00:00:00.000Z' });
  await s.create({ replayId: 'c', userId: 'u2', gameId: 'G', createdAt: '2026-03-01T00:00:00.000Z' });

  const got = await s.get('b');
  assert.equal(got.replayId, 'b');
  assert.equal(got.createdAtEpoch, undefined, 'internal sort key is stripped from returned metadata');

  const u1 = await s.list({ userId: 'u1' });
  assert.deepEqual(u1.map((r) => r.replayId), ['b', 'a'], 'newest first, filtered by userId');

  assert.equal(await s.remove('a'), true);
  assert.equal(await s.remove('a'), false);
  assert.equal(s.size, undefined);
});

test('/healthz surfaces replay storage mode', async () => {
  const body = await (await fetch(base + '/healthz')).json();
  assert.equal(body.ok, true);
  assert.equal(body.replayStorage, 'memory');
  assert.equal(typeof body.replays, 'number');
});
