# A5 — Master Server + In-Game Server List

**Branch:** `feat/master-server` (off `feature/online-multiplayer`)
**Depends on:** A1 `RoomInfo` DTO (backend) + A2 `IMasterServerClient` and connect API (Server List UI). The **backend is standalone** and can start immediately.
**Namespace roots:** `Online.Master`, `Online.UI.ServerList`; backend lives in top-level `Server/`.
**Read first:** `CLAUDE.md`, `AGENTS.md`, `Documentation/online/architecture.md` (esp. §1 client-only constraint, §4.1, §5, §6.3).

---

## Context

Public rooms need a **directory** so any client on the internet can find them. You build two things:

1. A **standalone backend directory service** (in `Server/`, outside `Assets/`) — the AWS master server.
2. The **in-game Server List** (native Unity UI) that lists public rooms and connects via A2's API.

## HARD constraint — client-only, no web UI

- The **entire room system is native in-game Unity UI**. The backend is a **directory API only**. Its **sole client is `CloSim.exe`**.
- **No web UI, no HTML frontend, no admin browser console** — nothing a web browser is meant to reach. Do not build a web dashboard, landing page, or `/` HTML route.
- Treat the directory API as **game-client-only**: gate it with a **client API key / client token / signed requests** so it isn't a casually browsable public web endpoint (even though it's HTTP under the hood). Document the gating; keep the key value **blank**.
- Use the name **"Server List"** everywhere — never "Server Browser".

## Scope — backend (`Server/`)

- **Directory API** (recommend **Node/Express** or **ASP.NET minimal API** — document your choice and why; keep it lightweight):
  - `POST /rooms` — **register** a public room (host only) → returns `roomId` (+ token issuance for private-room support if applicable).
  - `POST /rooms/{id}/heartbeat` — keep-alive; refresh TTL.
  - `DELETE /rooms/{id}` — **deregister**.
  - `GET /rooms` — **list** public rooms (supports `RoomQuery` filters: gameId, region, hideFull, version). Never returns join tokens (only `requiresToken`).
  - Optional token issuance endpoint for private rooms if the design needs server-issued tokens.
  - **Auth:** every request requires the **client credential** (API key / signed request). No unauthenticated browsing.
  - **Store:** recommend **in-memory + TTL** for v1 (rooms are ephemeral; drop after ~3 missed heartbeats, `RoomTtlSeconds`). Note DynamoDB/Redis as a scale-up option. Document the choice.
  - **No web UI route.** JSON only. Health check may be a JSON `GET /healthz` (not HTML).
- **AWS deployment IaC** (Terraform **or** AWS CDK — document choice): all endpoints, domains, secrets, ARNs, keys as **blank placeholders** with `// TODO: user provides` markers. The user supplies real values later. Never commit a real endpoint/secret.

## Scope — in-game Server List (`Assets/Scripts/Online/UI/ServerList/`)

- Native Unity UI screen listing public rooms from `IMasterServerClient.ListRoomsAsync(RoomQuery)`.
- Show `RoomInfo` fields (name, host, game, players/capacity, region, `requiresToken`, state, version). Filter/refresh.
- **Connect** via A2's `IOnlineConnection.StartClient(ConnectEndpoint)` (address/port from `RoomInfo`, prompt for token if `requiresToken`).
- **Direct-IP** entry (address + port + optional token) and a link to LAN discovery results (A2 emits `OnLanRoomDiscovered`).
- When `MasterServerConfig.MasterServerUrl == ""` (`IsConfigured == false`), show a clear "public server list not configured — use Direct IP / LAN" state. **Do not** hardcode an endpoint to make it work.

## Files you own

- `Server/**` (backend service + IaC + backend README)
- `Assets/Scripts/Online/Master/**` (any client-side glue not owned by A2's `IMasterServerClient` impl — coordinate so you don't duplicate A2's HTTP client)
- `Assets/Scripts/Online/UI/ServerList/**` + Server List scenes/prefabs (kept separate)

**Read-only:** `Online/Contracts/**` (A1), `Online/Netcode/**` (A2). Note: the concrete `IMasterServerClient` HTTP client is **A2's**; you consume it. If you need backend-shaped client helpers, coordinate with A2/A1 to avoid duplication.

## Interface contract you must honor

- Backend request/response shapes align with `RoomInfo` and `RoomQuery` (§5, §6.3) so A2's `IMasterServerClient` serializes cleanly.
- Server List UI consumes `IMasterServerClient` (A2) + `IOnlineConnection` (A2) — never Mirror directly, never a hardcoded endpoint.

Signature/shape changes → PR to A1 (update doc). The DTO is the contract between your backend JSON and A2's client.

## Definition of Done

- Backend implements register/heartbeat/deregister/list (+ optional token issuance), **config blank**, **gated by client credential**, **no web UI**, in-memory TTL store; runs locally with a documented command.
- AWS IaC present with **all endpoints/secrets blank placeholders**.
- In-game Server List (native Unity) lists rooms, supports direct-IP + LAN, connects via A2; graceful "not configured" state when blank.
- No hardcoded endpoint anywhere. Written run + editor test steps included.

## Testing notes

- Run the backend locally; in your editor **only**, set `MasterServerConfig.MasterServerUrl` to the local URL to test register → list → join (never commit it).
- Verify TTL: stop heartbeating a room → it drops from `GET /rooms` after the TTL.
- Verify auth: a request without the client credential is rejected.
- Verify no HTML/web route responds with a browser-facing page.

## Constraints

- **No game-content changes.**
- **In-game only / no web UI.** The only client of the master server is `CloSim.exe`.
- **Blank config:** `MASTER_SERVER_URL = "" // TODO: user provides hosting endpoint`, client API key blank, all IaC secrets blank.
- **Server-authoritative** game state is Mirror's job (A4) — the master server only tracks the room **directory**, never gameplay.

## Risks / watch-outs

- Master-server auth model (open register vs. required API key) is an open question (architecture §10 Q2) — default to a required client credential; confirm with A1.
- Version compatibility filtering policy (§10 Q3) — surface `RoomInfo.version` in the list; decide hard-block vs warn with A1.
- Don't let the "HTTP under the hood" tempt anyone into a web console — JSON API only, gated, game-client-only.
