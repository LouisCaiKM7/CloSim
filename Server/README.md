# CloSim Master Server

A lightweight **backend room-directory API** for CloSim's online multiplayer. It lets player-hosted
public listen-servers advertise themselves so other players can find and join them from the in-game
**Server List**.

> **Client-only. No web UI.** The *only* client of this service is the game executable
> (`CloSim.exe`). There is **no web frontend, no HTML page, no admin browser console** — every route
> returns JSON, and the directory is gated by a client API key so it isn't casually browsable. `GET /`
> is a JSON `404`, not a landing page.

- **Scope:** register / heartbeat / deregister / list public rooms. That's it. Gameplay is
  server-authoritative on the Mirror listen-server (handled elsewhere); this service only tracks the
  room **directory**, never game state.
- **No join tokens here.** Token gating for private/token rooms happens in the Mirror auth handshake,
  not this API. `RoomInfo` only exposes `requiresToken` (a boolean) — the token itself is never sent
  to or stored by the directory.

---

## Stack choice: Node.js + Express

Chosen over ASP.NET minimal API because:

- **Lightweight & zero-build** — plain ESM JavaScript, one dependency (Express). Nothing to compile.
- **Tiny container** — `node:20-alpine` image, fast cold starts, ideal for a stateless directory that
  scales to zero when no one is hosting.
- **Ubiquitous ops** — trivial to run locally (`npm start`) and to deploy as a container on AWS.

State is **in-memory with TTL** (no database). Public rooms are ephemeral — they exist only while a
listen-server is up and heartbeating — so a durable store is unnecessary for v1. The `RoomStore`
class is the seam to swap for **DynamoDB (native item TTL)** or **Redis (`EXPIRE`)** if the directory
ever needs to scale horizontally across instances.

---

## Layout

```
Server/
├─ src/
│  ├─ server.js      # entrypoint: boots Express, starts the TTL reaper, graceful shutdown
│  ├─ app.js         # Express app factory — the HTTP wire contract (routes)
│  ├─ auth.js        # X-Api-Key gating middleware (constant-time key compare)
│  ├─ roomStore.js   # in-memory room directory + TTL expiry
│  └─ config.js      # env-driven config + boot validation (refuses to run ungated)
├─ test/
│  └─ wire.test.js   # contract + TTL tests (node --test, no external deps)
├─ deploy/           # AWS Terraform IaC (all endpoints/secrets are blank placeholders)
├─ Dockerfile        # container image (config injected via env at runtime)
├─ .env.example      # BLANK config template — copy to .env and fill in
├─ package.json
└─ README.md         # this file
```

---

## Run locally

Requires Node.js >= 18.

```bash
cd Server
npm install

# Option A — with a real (fake) dev key (recommended, mirrors production gating):
CLIENT_API_KEYS=dev-key-123 npm start

# Option B — ungated, local dev only (the server refuses this unless the flag is explicit):
ALLOW_INSECURE_NO_AUTH=true npm start
```

Or copy the template and edit it, then load it (Node 20+ supports `--env-file`):

```bash
cp .env.example .env          # fill in CLIENT_API_KEYS at minimum
node --env-file=.env src/server.js
```

The service listens on `http://localhost:8080` by default (override with `PORT` / `BIND_HOST`).

**Smoke test:**

```bash
curl localhost:8080/healthz                       # -> {"ok":true,...}  (no key needed)
curl localhost:8080/rooms                          # -> 401 (no key)
curl -H "X-Api-Key: dev-key-123" localhost:8080/rooms   # -> {"rooms":[]}
```

**Run the tests:**

```bash
npm test        # node --test — HTTP contract, auth, filters, TTL
```

### Testing register → list → expire against the game client

In the Unity editor **only**, point the client at your local server (never commit this):
`new MasterServerClient("http://localhost:8080", "dev-key-123")`. Then host a public room and it will
`POST /rooms`; open the Server List and it will `GET /rooms`. Stop heartbeating (close the host) and
the room drops from the list after `ROOM_TTL_SECONDS` (default 45s).

---

## HTTP API reference

This is the **exact wire** the game client (`Assets/Scripts/Online/Net/MasterClient/`) speaks. Do not
change paths/headers/shapes without updating that client and the A1 contract in lockstep.

**Auth:** every route **except `/healthz`** requires the header `X-Api-Key: <client key>`. A missing or
unrecognized key returns `401 {"ok":false,"error":"..."}`. Multiple keys are supported (rotation).

**Content type:** requests and responses are `application/json`. Enums in `RoomInfo` (`visibility`,
`state`) are serialized as **integers** (Unity `JsonUtility` convention): `RoomVisibility` Public=0,
Private=1; `RoomState` Lobby=0, Starting=1, InMatch=2, Ended=3.

### `POST /rooms` — register a public room (host)

Body = JSON of a `RoomInfo` (`JsonUtility.ToJson(room)`). If `roomId` is blank the server mints one;
if present, it's honored (so re-registering updates in place). Registering also refreshes the TTL.

```jsonc
// request body (RoomInfo)
{ "roomId":"", "name":"Alice's Room", "hostName":"Alice", "address":"203.0.113.5", "port":7777,
  "gameId":"Reefscape", "region":"us-east", "playerCount":1, "capacity":6,
  "visibility":0, "requiresToken":false, "state":0, "version":"1.0.0" }
```

```jsonc
// 200 response  (matches the client's RegisterResponseDto)
{ "ok": true, "roomId": "b1c2...", "error": "" }
```

Failure: `400` (invalid body) or `503` (directory full), both `{ "ok":false, "roomId":"", "error":"..." }`.

### `POST /rooms/{id}/heartbeat` — keep-alive (host)

Body `{}`. Refreshes the room's TTL. `200 {"ok":true}` if live; `404 {"ok":false,...}` if unknown or
already expired (a host can then re-register). The client sends this every `HeartbeatSeconds` (15s) and
ignores the response body.

### `DELETE /rooms/{id}` — deregister (host)

Idempotent. Always `200 {"ok":true}`. If the host crashes without calling this, the room still drops
via TTL after missed heartbeats.

### `GET /rooms` — list public rooms (in-game Server List)

Query params (all optional), matching `RoomQuery`:

| Param | Meaning |
|-------|---------|
| `gameId` | exact match; omit/blank = all games |
| `region` | exact match; omit/blank = all regions |
| `hideFull` | `true` excludes rooms where `playerCount >= capacity` |
| `hidePrivate` | defaults `true`; private rooms are never listed anyway |

```jsonc
// 200 response — object form (the client also tolerates a bare array)
{ "rooms": [ { /* RoomInfo */ }, ... ] }
```

> **Version is never a filter.** The client deliberately does not send `version`, and this server
> never drops version-mismatched rooms — the Server List shows them greyed out rather than hiding
> them, so `RoomInfo.version` is always returned as-is.

### `GET /healthz` — health check (unauthenticated, JSON)

```jsonc
{ "ok":true, "service":"closim-master-server", "status":"healthy",
  "rooms":3, "region":"", "roomTtlSeconds":45, "heartbeatSeconds":15, "uptimeSeconds":1234 }
```

---

## Auth model

The directory is **game-client-only**, gated by a client API key sent as `X-Api-Key`. Keys are
configured via `CLIENT_API_KEYS` (comma-separated; multiple allowed for rotation / per-build keys),
sourced from **AWS Secrets Manager** in production. Comparison is constant-time.

The server **refuses to boot** if no keys are set, unless `ALLOW_INSECURE_NO_AUTH=true` is explicitly
provided (local dev only) — so a real deployment can never accidentally run open. The key value is
**blank** everywhere in this repo; the user supplies it.

## TTL / heartbeat model

In-memory store, per-room TTL. Values match the game client's cadence
(`MasterServerConfig`: `HeartbeatSeconds=15`, `RoomTtlSeconds=45` ≈ 3 missed beats):

- Register / heartbeat sets the room's expiry to `now + ROOM_TTL_SECONDS` (default **45s**).
- A background reaper sweeps expired rooms every `SWEEP_INTERVAL_SECONDS` (default 10s); listing also
  skips any lapsed room immediately.
- Stop heartbeating (host closes/crashes) → the room disappears from `GET /rooms` within the TTL.

## Where the config lives (all blank)

Every endpoint / secret / region is a **blank placeholder** — nothing is hardcoded:

- **[`.env.example`](./.env.example)** — the runtime env template (`PORT`, `BIND_HOST`,
  `CLIENT_API_KEYS`, `ROOM_TTL_SECONDS`, `HEARTBEAT_SECONDS`, `MAX_ROOMS`, `REGION_LABEL`, `LOG_LEVEL`).
  Copy to `.env` and fill in. `.env` is gitignored; only `.env.example` is tracked.
- **[`deploy/terraform.tfvars.example`](./deploy/terraform.tfvars.example)** — the AWS deploy inputs
  (region, image URI, domain, etc.), all `# TODO: user provides`.
- The client-side endpoint/key live in the Unity contract `MasterServerConfig` (`MasterServerUrl=""`,
  `ClientApiKey=""`) — also blank; the user fills them after deploying.

---

<!-- BEGIN deploy section (AWS Terraform under Server/deploy/) -->
## Deploy to AWS

The master server ships as a container and runs on **AWS App Runner** (managed HTTPS + autoscaling for
a single stateless HTTP service), pulling its image from **ECR**, with the client API key held in
**AWS Secrets Manager**. All infrastructure is Terraform under [`Server/deploy/`](./deploy/) — see
[`deploy/README.md`](./deploy/README.md) for the full walkthrough.

### Prerequisites
- Terraform >= 1.5, AWS CLI v2 (authenticated), Docker
- AWS permissions for ECR, App Runner, IAM, and Secrets Manager

### Steps
1. **Configure** — from `Server/deploy/`, `cp terraform.tfvars.example terraform.tfvars` and fill each
   `# TODO: user provides` (region, image URI later, region label, optional domain). Leave the real
   API key out of tfvars.
2. **Create the ECR repo**
   ```bash
   cd Server/deploy
   terraform init
   terraform apply -target=aws_ecr_repository.this
   terraform output -raw ecr_repository_url
   ```
3. **Build + push the image** — from `Server/` (Dockerfile is `Server/Dockerfile`):
   ```bash
   aws ecr get-login-password --region <aws_region> \
     | docker login --username AWS --password-stdin <account_id>.dkr.ecr.<aws_region>.amazonaws.com
   docker build -t <ecr_repository_url>:latest .
   docker push <ecr_repository_url>:latest
   ```
   Then set `ecr_image_uri = "<ecr_repository_url>:latest"` in `terraform.tfvars`.
4. **Apply the stack** — `terraform plan && terraform apply`.
5. **Set the client API key** (out-of-band; never in tfvars):
   ```bash
   aws secretsmanager put-secret-value \
     --secret-id "$(terraform output -raw secret_arn)" \
     --secret-string "your-key-1,your-key-2"
   ```
6. **Get the URL** — `terraform output -raw service_url`. That HTTPS endpoint is what the game client
   uses (with the key in `X-Api-Key`). Verify with `curl "$(terraform output -raw service_url)/healthz"`.

The service is configured entirely through env vars injected by Terraform (`PORT`, `BIND_HOST`,
`ROOM_TTL_SECONDS`, `HEARTBEAT_SECONDS`, `MAX_ROOMS`, `REGION_LABEL`, `LOG_LEVEL`) plus
`CLIENT_API_KEYS` from Secrets Manager. Rotate the key by updating the secret and triggering a new App
Runner deployment. Tear down with `terraform destroy`.
<!-- END deploy section -->
