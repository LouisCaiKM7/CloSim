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
| **2 — Rooms & Modes** ✅ merged | **A3** | `feat/rooms-lobby` | Networked room/lobby model (`IRoomService`, 6-cap/≤3-team/ready-up/host-picks-mode/spectators); `PlayMode ↔ NetworkMatchConfig` adapter; in-game lobby UI (create/join/room). Namespaces `Online.Rooms` / `Online.UI.Lobby`. | A2 + A1 |
| **3 — Gameplay Sync** ✅ merged | **A4** | `feat/gameplay-sync` | De-hardcode `LoadMatch` 4→N; server-auth robot spawn + per-client ownership; `NetworkTransform`/sync on robots, pieces, `FieldScorer`, `Fms`; online single-view camera; `IMatchLauncher`. Namespace `Online.Sync`. | A2 + A1; coordinates with A3 on player model |
| **4 — Master Server + Server List** ✅ merged (backend + UI) | **A5** | `feat/master-server` | Standalone backend directory service in `Server/` (register/heartbeat/deregister/list/healthz, config blank, `X-Api-Key` gated, no web UI). In-game **Server List** UI (native Unity) using A2's `IMasterServerClient`, plus AWS Terraform IaC (blank placeholders). Landed under `Online.UI.Lobby` rather than the originally-planned `Online.UI.ServerList` — see the naming note in "What's built so far". | A1 `RoomInfo` + A2 connect API; backend is independent |
| **5 — Replay (new)** ✅ merged | replay-design + replay-backend agents | `feat/replay-design`, `feat/replay-backend` | Deterministic **state-snapshot** match replays (not video): in-game recorder, AWS S3-backed storage via the backend, in-game Replays list/viewer. Full design/contracts in `Documentation/online/architecture.md`. S3 config blank until user-supplied. | A2 connection/session hooks; A4 gameplay state; reuses A5's backend-hosting pattern |

**Ordering:** A2 unblocks A3 and A4 (they need the connection API). A3 and A4 run largely in parallel, syncing on the player/roster model (`RoomMemberSlot` ↔ `LoadMatch` slots) via A1. A5's **backend** can start immediately (independent); A5's **Server List UI** needs A2's `IMasterServerClient` + connect. A1 integrates each branch back to `feature/online-multiplayer` via PR-style review merges.

> **Status update:** All five phases are merged into `feature/online-multiplayer`: netcode foundation (A2), rooms & lobby incl. the native Server List UI (A3), gameplay sync (A4), the master-server backend + IaC (A5), and the replay feature end-to-end (contracts, backend, service client, codec, recorder, and in-game Replays list/viewer). A build/QA harness is merged alongside. See "What's built so far" below for the current, living status and known gaps.

---

## What's built so far (living status)

> Keep this section in sync with `feature/online-multiplayer`. Update it whenever a branch merges.

- **Netcode foundation (Phase 1 / A2) — merged.** Mirror listen-server bootstrapped by `CloSimNetworkManager` (namespace `Online.Net`), the connection facade `OnlineConnection` implementing `IOnlineConnection`, LAN discovery + direct-IP connect (`Online.Net.Discovery`), a token/version-gated Mirror authenticator (`Online.Net.Auth.CloSimNetworkAuthenticator`), the protocol/version single-source-of-truth (`Online.Net.NetcodeProtocol`), and the master-server HTTP client (`Online.Net.MasterClient.MasterServerClient`). Code lives under `Assets/Scripts/Online/Net/**`.
- **Rooms & lobby (Phase 2 / A3) — merged.** Networked room/lobby core (`Online.Rooms.RoomService` implementing `IRoomService`, 6-cap/≤3-per-alliance/ready-up/host-picks-mode/spectator enforcement), the `PlayMode ↔ NetworkMatchConfig` adapter, and the full native-Unity lobby UI (Create Room, Join, in-room lobby with mode selector, and the **Server List** screen) under `Online.UI.Lobby`. **Naming note:** the Server List UI landed as `Online.UI.Lobby.ServerListScreen` / `ServerListRowUI` rather than the originally-planned separate `Online.UI.ServerList` namespace — a harmless deviation from the Phase 4 row above; it is still the one and only in-game Server List (never a web page), just co-located with the rest of the lobby screens.
- **Gameplay sync (Phase 3 / A4) — merged.** `LoadMatch` de-hardcoded from a fixed 4 slots to the (blueCount, redCount) network model (offline `PlayMode` path byte-identical and untouched); server-authoritative robot spawn + per-connection ownership (`Online.Sync.MatchSpawnManager`, `RobotNetworkController`); match-flow and score replication (`Online.Sync.MatchFlowSync`, `ScoreSync`) driving the existing `Fms`/`FieldScorer`/`ScoreHolder` with no scoring-math changes; `IMatchLauncher` (`Online.Sync.MatchLauncher`) turning a room roster into a launched networked match. Host-authoritative game-piece network sync (`Online.Sync.Pieces.*`) also landed; a couple of known piece-sync/scoring edge-case bugs in that area are being tracked and fixed separately from this rulebook's day-to-day status.
- **Master-server backend + AWS IaC (Phase 4 backend / A5) — merged.** Node.js/Express directory API in `Server/` (`register` / `heartbeat` / `deregister` / `list` / `healthz`, `X-Api-Key` gating, in-memory + TTL room store), plus Terraform IaC under `Server/deploy/` (ECR + App Runner + Secrets Manager). All config blank; see `Server/README.md`.
- **Build/QA harness — merged.** Headless Windows build entry point (`Assets/Editor/Build/CloSimBuild.cs`, invoked by `Tools/build-windows.ps1` / `.sh`), EditMode contract smoke tests (`Assets/Tests/EditMode/ContractsSmokeTests.cs`, `ReplayCodecTests.cs`) that assert the blank master-server/replay config and DTO/codec invariants, GitHub Actions CI (`.github/workflows/ci.yml`), and `Documentation/online/integration-checklist.md` (merge order, conflict hot-spots, verification steps, blank-config table).
- **Replay feature (new — additive to the original 4-phase plan) — merged end-to-end.** Deterministic **state-snapshot** match replays (not video), all under `Online.Contracts.Replay` / `Online.Replay.*`:
  - **Contracts + wire format** (`Online.Contracts.Replay`: `ReplayFormat`, `ReplayModels`, `ReplayEnums`, `IReplayRecorder`, `IReplayService`) and a binary codec (`Online.Replay.Codec.ReplayWriter`/`ReplayReader`, gzip + fixed-point quantization, EditMode-tested).
  - **Backend + service client**: S3-backed blob storage plus presigned `/replays` endpoints on the master-server backend (`Server/src/replays.js`, `blobStore.js`), and the in-game HTTP client `Online.Replay.Service.ReplayServiceClient` implementing `IReplayService`.
  - **Recorder** (`Online.Replay.Recorder.ReplayRecorder` + `MatchReplayRecorderRunner`): host-only, hooked into `MatchSceneBootstrap`'s existing online branch (no-ops on clients); samples every robot's position/rotation at `ReplayFormat.DefaultTickRate`, keyframes every `DefaultKeyframeInterval` frames, and saves via `CompositeReplayService` on match end. Game-piece snapshots are deferred — see the file's header comment — so replays are robots-only for now.
  - **Offline / single-player recording (additive follow-up).** `Online.Replay.Recorder.OfflineMatchReplayRecorderRunner` is the offline sibling of `MatchReplayRecorderRunner`, hooked into `MatchSceneBootstrap`'s offline branch right after `ApplyLaunchData` applies the menu-selected settings. It reads robots straight off `LoadMatch`'s own slot arrays (no Mirror/room dependency), samples on the same tick/keyframe cadence, and saves through the same `CompositeReplayService` — so local split-screen / single-player matches produce a replay too, not just online-hosted ones.
  - **Local on-disk replay store (additive follow-up).** `Online.Replay.Service.LocalReplayService` implements `IReplayService` against `Application.persistentDataPath/Replays/` (one `.meta.json` + one `.replay` blob file per replay, reusing the existing `ReplayWriter`/`ReplayReader` wire format unchanged) and is always "configured" — no AWS setup required. `Online.Replay.Service.CompositeReplayService` unions it with the AWS-backed `ReplayServiceClient`: both recorders always save locally first (so every match — online or offline — gets a watchable replay), and additionally mirror to S3 when `ReplayServiceConfig` is configured. The Replays list/playback screens (`ReplaysScreen`, `ReplayPlaybackScreen`) now construct `CompositeReplayService` instead of `ReplayServiceClient` directly, so local replays show up and play back with zero backend.
  - **AWS S3 config (bucket/region/credentials) stays blank** until the user supplies it, per golden rule 2 — the remote half of `CompositeReplayService` is a graceful no-op until then, and the local store (and therefore "every match gets a replay") works regardless.

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
  - `Online.Rooms` + `Online.UI.Lobby` (A3, merged) — room/lobby service and its native-UI lobby screens, **including the Server List screen** (`Online.UI.Lobby.ServerListScreen`/`ServerListRowUI`). **Not** a separate `Online.UI.ServerList` — that name from earlier planning docs is superseded; the Server List UI lives under `Online.UI.Lobby` alongside Create/Join/Room.
  - `Online.Sync` (A4, merged) — gameplay/state replication (`Online.Sync.Match.*`, `Online.Sync.Flow.*`, `Online.Sync.Pieces.*`).
  - `Online.Contracts.Replay` (replay contracts, merged) — replay DTOs/enums/wire-format constants and the `IReplayRecorder`/`IReplayService` interfaces. Physically under `Assets/Scripts/Online/Replay/Contracts/**` but the C# namespace is `Online.Contracts.Replay`, not `Online.Replay.Contracts` — code against the namespace, not the folder name.
  - `Online.Replay.Codec` (merged) — binary wire codec (`ReplayWriter`/`ReplayReader`).
  - `Online.Replay.Service` (merged) — `ReplayServiceClient`, the in-game HTTP client for the replay backend.
  - `Online.Replay.Recorder` (merged) — host-only in-match recorder (`ReplayRecorder`, `MatchReplayRecorderRunner`).
  - `Online.Replay.UI` (merged) — in-game **Replays list** + playback screens (`ReplaysScreen`, `ReplayPlaybackScreen`, `ReplayRowUI`, `ReplaysMenuController`). Call this **"Replays list"**, never "Replays browser" — same "never browser" naming rule as golden rule 3's Server List.
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
- Rooms/lobby + Server List UI (merged): **`Assets/Scripts/Online/Rooms/**`** (namespace `Online.Rooms`), **`Assets/Scripts/Online/UI/Lobby/**`** (namespace `Online.UI.Lobby` — includes `ServerListScreen.cs`/`ServerListRowUI.cs`)
- Gameplay sync (merged): **`Assets/Scripts/Online/Sync/**`** (namespace `Online.Sync`, sub-namespaces `Online.Sync.Match`/`Online.Sync.Flow`/`Online.Sync.Pieces`)
- Replay feature (merged): **`Assets/Scripts/Online/Replay/**`** — `Contracts/` (namespace `Online.Contracts.Replay`), `Codec/` (`Online.Replay.Codec`), `Service/` (`Online.Replay.Service`), `Recorder/` (`Online.Replay.Recorder`), `UI/` (`Online.Replay.UI` — the in-game Replays list + playback screens)
- Backend directory service (A5, merged) + AWS IaC, incl. replay S3 blob storage: **`Server/`** (top-level, outside `Assets/`) — see **`Server/README.md`**
- Build/QA harness: **`Assets/Editor/Build/CloSimBuild.cs`**, **`Tools/build-windows.ps1`/`.sh`**, **`Assets/Tests/EditMode/`** (incl. `ReplayCodecTests.cs`), **`.github/workflows/ci.yml`**
