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
| **1 — Netcode Foundation** | **A2** | `feat/netcode-foundation` | Mirror in `manifest.json`; `CloSimNetworkManager`; connection lifecycle (StartHost/StartClient/Stop); transport; LAN discovery; token/password auth gating; direct-IP connect; concrete `IOnlineConnection` + `IMasterServerClient` HTTP client | A1 contracts |
| **2 — Rooms & Modes** | **A3** | `feat/rooms-lobby` | Networked room/lobby model (`IRoomService`, 6-cap/≤3-team/ready-up/host-picks-mode/spectators); `PlayMode ↔ NetworkMatchConfig` adapter; in-game lobby UI (create/join/room) | A2 + A1 |
| **3 — Gameplay Sync** | **A4** | `feat/gameplay-sync` | De-hardcode `LoadMatch` 4→N; server-auth robot spawn + per-client ownership; `NetworkTransform`/sync on robots, pieces, `FieldScorer`, `Fms`; online single-view camera; `IMatchLauncher` | A2 + A1; coordinates with A3 on player model |
| **4 — Master Server + Server List** | **A5** | `feat/master-server` | Standalone backend directory service in `Server/` (register/heartbeat/deregister/list/token, config blank, gated, no web UI); AWS IaC (blank placeholders); in-game **Server List** UI (native Unity) using A2's `IMasterServerClient` | A1 `RoomInfo` + A2 connect API; backend is independent |

**Ordering:** A2 unblocks A3 and A4 (they need the connection API). A3 and A4 run largely in parallel, syncing on the player/roster model (`RoomMemberSlot` ↔ `LoadMatch` slots) via A1. A5's **backend** can start immediately (independent); A5's **Server List UI** needs A2's `IMasterServerClient` + connect. A1 integrates each branch back to `feature/online-multiplayer` via PR-style review merges.

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

- **Namespaces:** existing roots are `Core`, `Audio`, `InputController`, `CameraControls`, `Utilities`, `UI.*`, `Field.*`, `Robot.*`. New online code uses a new **`Online.*`** root: `Online.Contracts` (A1), `Online.Netcode` (A2), `Online.Rooms` (A3), `Online.Sync` (A4), `Online.Master` / `Online.UI.ServerList` (A5). Backend service is non-C#-in-`Assets`; it lives in top-level `Server/`.
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
- Contract stub interfaces: **`Assets/Scripts/Online/Contracts/*.cs`** (namespace `Online.Contracts`)
- Backend directory service (A5): **`Server/`** (top-level, outside `Assets/`)
