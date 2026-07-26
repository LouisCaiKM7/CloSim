# CLAUDE.md — CloSim Online Multiplayer Development Rulebook

This file governs the **online multiplayer** extension of CloSim. Read it fully before touching code.
It is the plan + rules; the **interface contracts** live in `Documentation/online/architecture.md` (the source of truth for types and signatures), and the **team roster / ownership** lives in `AGENTS.md`.

---

## Project goal

CloSim is a Unity (URP, Unity 2022/6-era, Input System 1.7.0) FRC robotics simulator, **GPLv3-licensed**, derived from MoSimBuilder. Today it is **local split-screen only** and has **no networking** (`Packages/manifest.json` has no netcode packages).

We are adding **online multiplayer**:

- **Player-hosted listen-servers** (a player opens a server from inside the client — no dedicated server needed).
- **Public rooms register with an AWS master server** (a backend directory API) and are **browsable in-game** via a native Unity **Server List** screen.
- **Direct IP/port connect** and **LAN / private rooms gated by a join token/password** (not publicly listed).
- **Rooms:** up to **6 players, ≤3 per alliance**; versus modes **1v1, 2v1, 2v2, 3v1, 3v2, 3v3** (asymmetric OK) plus the existing offline co-op/same-alliance modes.

The netcode stack is **Mirror** (MIT).

---

## The 7 golden rules (non-negotiable)

1. **DO NOT CHANGE GAME CONTENT.** No changes to robot mechanics, drivetrain/swerve physics, field scoring rules, game-piece behavior, assets, prefabs' gameplay tuning, or "the feel". Work is **strictly additive**, plus the *minimal* plumbing refactors required to (a) support >4 players and (b) add network ownership. If a change alters how the game plays offline, it is out of scope — stop and escalate to A1.

2. **Blank config everywhere.** The AWS master-server endpoint and any secret/credential are **blank placeholders**. Use exactly:
   ```csharp
   MASTER_SERVER_URL = "" // TODO: user provides hosting endpoint
   ```
   Never hardcode a real endpoint, IP, bucket, key, or ARN. The user supplies these later.

3. **The room system is 100% in-game native Unity UI.** Browse (Server List), create, join, room lobby, and mode selection are all **inside `CloSim.exe`**. **Never** a web page, HTML frontend, or browser-accessed console. The AWS master server is a **backend directory API whose only client is `CloSim.exe`** — no web UI, no admin browser console. The directory API can be **gated** (client API key / signed requests) so it is not casually browsable. Use the name **"Server List"**, never "Server Browser".

4. **Additive-only file discipline.** In this phase, prefer new files under `Assets/Scripts/Online/**` and top-level `Server/**`. Touch existing gameplay `.cs` files **only** for the de-hardcoding explicitly assigned to you (mainly `LoadMatch.cs` for A4), and keep those edits minimal, reviewed, and behavior-preserving for offline play.

5. **Server-authoritative.** The host/listen-server is authoritative for room state, robot spawn/ownership, match flow (`Fms`), and scoring (`FieldScorer`). Clients send intent (commands) and render replicated state. Never trust a client for score or match state.

6. **Contracts are the source of truth.** Code against the interfaces/DTOs in `Documentation/online/architecture.md` (`Online.Contracts`). Do not invent parallel types for the same concept. Signature changes go through A1 via PR that updates the doc in the same change.

7. **Backward compatibility for offline.** The existing `PlayMode` enum path and 4-slot local split-screen must keep working unchanged. Online uses the `(blueCount, redCount)` model; offline keeps the enum. Both resolve to the same internal count model at match start.

---

## Target architecture (summary)

See `Documentation/online/architecture.md` §3–§4 for the full ASCII architecture + connection/sequence diagrams. In brief:

```
  CloSim.exe (Host)  ── Mirror/KCP ──  CloSim.exe (Clients)
       │                                     ▲
       │ register/heartbeat (public only)    │ list rooms
       ▼                                     │
  AWS Master Server (backend directory API, client = CloSim.exe only, config BLANK)
```

- **Mirror** listen-server; transport **KCP** (default) / Telepathy (fallback).
- **AWS master server** = public room registry (`register`/`heartbeat`/`deregister`/`list`/`issue-token`). Backend only; no web UI; gated by client credential; config blank.
- **Own-host / LAN / token** = Mirror LAN discovery + direct IP:port + join-token auth handshake for private rooms.
- **Rooms** = 6-cap, ≤3/alliance, host-picks-mode, ready-up, optional spectators.

---

## Phased implementation roadmap

| Phase | Owner | Branch | Deliverable | Depends on |
|-------|-------|--------|-------------|------------|
| **0 — Planning & scaffolding** ✅ | **A1** | `feature/online-multiplayer` | This rulebook, `AGENTS.md`, `README` online section, `Documentation/online/**` (architecture + contracts + per-agent briefs), git branch graph, `Assets/Scripts/Online/Contracts/**` stub interfaces | — |
| **1 — Netcode Foundation** ✅ merged | **A2** | `feat/netcode-foundation` | Mirror in `manifest.json` (OpenUPM `com.mirrornetworking.mirror` 96.6.4); `CloSimNetworkManager`; connection lifecycle (StartHost/StartClient/Stop); transport; LAN discovery; token/password auth gating; direct-IP connect; concrete `IOnlineConnection` + `IMasterServerClient` HTTP client. Landed under `Online.Net.*` (see Coding Conventions). | A1 contracts |
| **2 — Rooms & Modes** (not yet merged) | **A3** | `feat/rooms-lobby` | Networked room/lobby model (`IRoomService`, 6-cap/≤3-team/ready-up/host-picks-mode/spectators); `PlayMode ↔ NetworkMatchConfig` adapter; in-game lobby UI (create/join/room). Namespaces `Online.Rooms` / `Online.UI.Lobby`. | A2 + A1 |
| **3 — Gameplay Sync** (not yet merged) | **A4** | `feat/gameplay-sync` | De-hardcode `LoadMatch` 4→N; server-auth robot spawn + per-client ownership; `NetworkTransform`/sync on robots, pieces, `FieldScorer`, `Fms`; online single-view camera; `IMatchLauncher`. Namespace `Online.Sync`. | A2 + A1; coordinates with A3 on player model |
| **4 — Master Server + Server List** (backend ✅ merged; UI not yet merged) | **A5** | `feat/master-server` | Standalone backend directory service in `Server/` (register/heartbeat/deregister/list/healthz, config blank, `X-Api-Key` gated, no web UI) — **merged**, includes AWS Terraform IaC (blank placeholders). In-game **Server List** UI (native Unity, namespace `Online.UI.ServerList`) using A2's `IMasterServerClient` — **not yet merged**. | A1 `RoomInfo` + A2 connect API; backend is independent |
| **5 — Replay (new)** | replay-design + replay-backend agents | `feat/replay-design`, `feat/replay-backend` | Deterministic **state-snapshot** match replays (not video): in-game recorder, AWS S3-backed storage via the backend, in-game Replays browser/viewer. Full design/contracts in `Documentation/online/architecture.md` (owned by the replay-design agent this round — see it for details). S3 config blank until user-supplied. | A2 connection/session hooks; A4 gameplay state; reuses A5's backend-hosting pattern |

**Ordering:** A2 unblocks A3 and A4 (they need the connection API). A3 and A4 run largely in parallel, syncing on the player/roster model (`RoomMemberSlot` ↔ `LoadMatch` slots) via A1. A5's **backend** can start immediately (independent); A5's **Server List UI** needs A2's `IMasterServerClient` + connect. A1 integrates each branch back to `feature/online-multiplayer` via PR-style review merges.

> **Status update:** Phase 1 (A2 netcode foundation) and the Phase 4 **backend** (A5's `Server/` directory API + AWS IaC) are merged, alongside a build/QA harness. Phases 2/3 (rooms, gameplay sync) and the Phase 4 **Server List UI** are not yet merged. See "What's built so far" below for the current, living status.

---

## What's built so far (living status)

> Keep this section in sync with `feature/online-multiplayer`. Update it whenever a branch merges.

- **Netcode foundation (Phase 1 / A2) — merged.** Mirror listen-server bootstrapped by `CloSimNetworkManager` (namespace `Online.Net`), the connection facade `OnlineConnection` implementing `IOnlineConnection`, LAN discovery + direct-IP connect (`Online.Net.Discovery`), a token/version-gated Mirror authenticator (`Online.Net.Auth.CloSimNetworkAuthenticator`), the protocol/version single-source-of-truth (`Online.Net.NetcodeProtocol`), and the master-server HTTP client (`Online.Net.MasterClient.MasterServerClient`). Code lives under `Assets/Scripts/Online/Net/**`.
- **Master-server backend + AWS IaC (Phase 4 backend / A5) — merged.** Node.js/Express directory API in `Server/` (`register` / `heartbeat` / `deregister` / `list` / `healthz`, `X-Api-Key` gating, in-memory + TTL room store), plus Terraform IaC under `Server/deploy/` (ECR + App Runner + Secrets Manager). All config blank; see `Server/README.md`. The in-game **Server List** UI itself (native Unity, consumes `IMasterServerClient`) has **not** landed yet.
- **Build/QA harness — merged.** Headless Windows build entry point (`Assets/Editor/Build/CloSimBuild.cs`, invoked by `Tools/build-windows.ps1` / `.sh`), EditMode contract smoke tests (`Assets/Tests/EditMode/ContractsSmokeTests.cs`) that assert the blank master-server config and DTO invariants, GitHub Actions CI (`.github/workflows/ci.yml`), and `Documentation/online/integration-checklist.md` (merge order, conflict hot-spots, verification steps, blank-config table).
- **Rooms & lobby (Phase 2 / A3)** and **gameplay sync (Phase 3 / A4)** — not yet merged into `feature/online-multiplayer`.
- **Replay feature (new — additive to the original 4-phase plan).** Deterministic **state-snapshot** match replays (not video): recorded in-game by a replay recorder, uploaded/stored via the backend on **AWS S3**, and played back through an in-game **Replays** browser/viewer. This is a new feature layered on top of the multiplayer effort, not a revision of Phases 1–4. Design/contracts are the responsibility of the replay-design agent and live in `Documentation/online/architecture.md` (do not duplicate the detailed design here — that file is the source of truth); backend storage work tracks in `feat/replay-backend`. **AWS S3 config (bucket/region/credentials) stays blank** until the user supplies it, per golden rule 2.

## Resolved decisions

These were open questions in earlier planning; they are now settled. Downstream agents should treat them as fixed:

1. **No relay in v1.** Player-hosted listen-servers on the public internet require the host to be reachable — the host is responsible for port-forwarding (or UPnP) the UDP game transport port. LAN play needs no port-forwarding. A relay is out of scope for v1 and may be revisited later as separate future work.
2. **Master-server auth model.** The AWS master server is a **backend directory API only**, gated by a client credential sent as the `X-Api-Key` header (`Server/src/auth.js`, constant-time compare). Every route except `/healthz` requires a valid key; the server **refuses to boot** with no keys configured unless `ALLOW_INSECURE_NO_AUTH=true` is explicitly set (local dev only, never in production).
3. **Protocol/version compatibility policy — hard reject at connect.** `Online.Net.Auth.CloSimNetworkAuthenticator`, backed by `Online.Net.NetcodeProtocol.Version`, **hard-rejects** any client whose protocol version doesn't match the host's, returning `ConnectResult.Rejected_Version`. This is independent of listing: the master server never filters `GET /rooms` on `version` — version-mismatched **public rooms are listed but shown greyed out** in the Server List, while an actual connect attempt against a mismatched host is always rejected.
4. **Spectators are pure observers.** `MemberRole.Spectator` slots get no human-player control, no alliance assignment, and no scoring interaction — view-only.
5. **Master directory persistence — in-memory + TTL.** No database for v1. `Server/src/roomStore.js` holds rooms in memory with TTL expiry: heartbeat every `HeartbeatSeconds` (15s), room dropped after `RoomTtlSeconds` (**~45s**, ≈3 missed beats) via a background reaper. `RoomStore` is the seam to later swap in DynamoDB/Redis if the directory needs to scale horizontally.
6. **Mirror package source.** Mirror is pulled via **OpenUPM** (`com.mirrornetworking.mirror`, version **96.6.4**) in `Packages/manifest.json`, with the Unity **Asset Store** package documented as a fallback if OpenUPM is unavailable in a given environment.

## Blank config inventory (`// TODO: user provides`)

Every placeholder below ships blank on purpose (golden rule 2 above). None of these are real endpoints/credentials — the user fills them in at deploy/setup time. This mirrors (and should stay in sync with) `Documentation/online/integration-checklist.md` §6.

| Placeholder | Where | What the user provides |
|-------------|-------|-------------------------|
| `MasterServerConfig.MasterServerUrl = ""` | `Assets/Scripts/Online/Contracts/IMasterServerClient.cs` | AWS master-server (directory API) HTTPS endpoint. Blank ⇒ `IsConfigured == false`, no calls made, Server List shows "master server not configured". |
| `MasterServerConfig.ClientApiKey = ""` | same file | Client credential sent as `X-Api-Key`, gating the directory API. |
| `CLIENT_API_KEYS` | `Server/.env.example` (local), AWS Secrets Manager (prod) | The matching API key(s) the backend accepts; the server won't boot without at least one unless the insecure-dev override is set. |
| AWS deploy inputs (region, ECR image URI, domain, etc.) | `Server/deploy/terraform.tfvars.example` | Terraform variables for the ECR + App Runner + Secrets Manager stack. All marked `# TODO: user provides`. |
| `UNITY_LICENSE` / `UNITY_EMAIL` / `UNITY_PASSWORD` (+ `UNITY_SERIAL` for Pro) | GitHub Actions repo secrets, consumed by `.github/workflows/ci.yml` | Unity CI activation so EditMode tests + the headless Windows build can run in CI. |
| `UNITY_PATH` (optional) | env var or `-UnityPath` arg to `Tools/build-windows.ps1` / `.sh` | Local path to `Unity.exe` if auto-detection misses it. |
| Replay S3 bucket / region / credentials | Replay backend + in-game replay recorder config (design in `Documentation/online/architecture.md`, owned by the replay-design agent) | AWS S3 bucket name, region, and access credentials for storing/retrieving replay snapshot files. Replay upload/download stays disabled until this is filled in. |

---

## Git workflow & branch strategy

```
main  (release; DO NOT TOUCH in this effort)
  └── feature/online-multiplayer          ← integration branch; all docs/contracts land here
        ├── feat/netcode-foundation   (A2)
        ├── feat/rooms-lobby          (A3)
        ├── feat/gameplay-sync        (A4)
        └── feat/master-server        (A5)
```

- All feature branches are cut from `feature/online-multiplayer`.
- Each agent works **only** on its branch and **only** in the files/dirs it owns (see `AGENTS.md`).
- Merge back to `feature/online-multiplayer` via **PR-style review through A1** (A1 reviews for the golden rules, contract conformance, and merge-conflict surface). No direct merges to `main`.
- Rebase/merge `feature/online-multiplayer` into your feature branch frequently to pick up contract updates and siblings' merged work.
- Commit messages: imperative, scoped, e.g. `netcode: add CloSimNetworkManager + KCP transport`. Reference the phase/agent.
- **Do not push** unless the user has set up remote auth. **Never** force-push shared branches.
- No secrets in commits (see golden rule 2).

---

## Coding conventions (match existing style)

- **Namespaces (standardized — matches merged code):** existing roots are `Core`, `Audio`, `InputController`, `CameraControls`, `Utilities`, `UI.*`, `Field.*`, `Robot.*`. New online code uses a new **`Online.*`** root:
  - `Online.Contracts` (A1) — DTOs/interfaces.
  - `Online.Net` (A2, merged) — netcode foundation: `Online.Net` (manager/connection/protocol core), `Online.Net.Auth` (authenticator), `Online.Net.Discovery` (LAN discovery + direct connect), `Online.Net.MasterClient` (master-server HTTP client). **Not** `Online.Netcode` — that name from earlier planning docs is superseded; use `Online.Net.*`.
  - `Online.Rooms` + `Online.UI.Lobby` (A3) — room/lobby service and its native-UI lobby screens.
  - `Online.Sync` (A4) — gameplay/state replication.
  - `Online.UI.ServerList` (A5) — in-game Server List UI. The master-server **backend** is non-C#, outside `Online.*`, and lives in top-level `Server/`.
  - `Online.Replay` (new replay feature) — in-game recorder + Replays browser/viewer; see `Documentation/online/architecture.md` for the full contract (owned by the replay-design agent).
- **Style:** 4-space indent; `_camelCase` private fields; PascalCase public members; `[SerializeField] private` for inspector fields; `[Header]`/`[Tooltip]` grouping as in `LoadMatch.cs`.
- **MyBox attributes** (`[Foldout]`, `[ReadOnly]`, `[Separator]`, `[MustBeAssigned]`) are used in the builder/scoring layer — use them for inspector-facing serialized fields where it improves clarity; the match-flow core files use stock Unity attributes only.
- **Input:** Unity Input System 1.7.0 only. Do not add a second input path. Remote players are driven by network state, not local devices.
- **No new game content:** do not add/modify robot prefabs' mechanics, field prefabs, scoring math, or piece behavior.
- **Assembly definitions:** if you add an `.asmdef` for `Online.*`, ensure it references the assemblies it needs (Mirror, existing gameplay asmdefs) and does **not** create a cycle. Coordinate asmdef changes with A1.

---

## Build / test procedure (Unity editor — for the user)

> **Constraint:** agents in this environment **cannot compile or run Unity** (no Unity CLI). Development is **review-based**: agents write code + document exactly how the user validates it in the editor. Every PR must include manual test steps.

General setup:

1. Open the project in **Unity Hub** with the matching editor version (URP, Unity 2022/6-era). Let it import.
2. After A2 adds Mirror to `Packages/manifest.json`, Unity resolves the package on next focus. If Mirror is added via Git URL or the Asset Store/OpenUPM, follow the note in the A2 brief.
3. Confirm the project compiles (Console has no errors) before testing a phase.

Per-phase validation:

- **Phase 1 (Netcode):** Enter Play mode, use a test bootstrap scene: Start Host, then a second editor/build instance Start Client to `127.0.0.1`. Verify connect/disconnect and that a bad token is rejected. Use **ParrelSync** or a standalone build for the second instance.
- **Phase 2 (Rooms):** Host creates a room; a second instance joins; verify 6-cap and ≤3/alliance enforcement, ready-up, host mode pick replicate across instances. All via in-game UI.
- **Phase 3 (Gameplay Sync):** Launch a networked match with N players (e.g. 3v3). Verify each client owns exactly its robot, sees others move (NetworkTransform), scoring/`Fms` timer identical across clients, single-view camera online, and that **offline split-screen still works unchanged**.
- **Phase 4 (Master Server + Server List):** Run the `Server/` backend locally, set `MasterServerConfig.MasterServerUrl` to the local URL **only for local testing** (never commit it). Host a public room → appears in the in-game Server List → another client joins from the list. Verify heartbeat/TTL drop.

**Offline regression check (every phase):** run the existing Rebuilt and Reefscape scenes in local split-screen (1–4 players) and confirm no behavioral change.

---

## Where things live

- Rules & plan: **`CLAUDE.md`** (this file)
- Team roster, ownership, DoD, coordination: **`AGENTS.md`**
- Architecture + interface contracts (source of truth): **`Documentation/online/architecture.md`**
- Per-agent briefs: **`Documentation/online/agents/A2..A5-*.md`**
- Merge order, conflict hot-spots, build/test/verification steps, blank-config table: **`Documentation/online/integration-checklist.md`**
- Contract stub interfaces: **`Assets/Scripts/Online/Contracts/*.cs`** (namespace `Online.Contracts`)
- Netcode foundation (merged): **`Assets/Scripts/Online/Net/**`** (namespaces `Online.Net`, `Online.Net.Auth`, `Online.Net.Discovery`, `Online.Net.MasterClient`)
- Backend directory service (A5, merged) + AWS IaC: **`Server/`** (top-level, outside `Assets/`) — see **`Server/README.md`**
- Build/QA harness: **`Assets/Editor/Build/CloSimBuild.cs`**, **`Tools/build-windows.ps1`/`.sh`**, **`Assets/Tests/EditMode/`**, **`.github/workflows/ci.yml`**
