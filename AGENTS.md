# AGENTS.md — CloSim Online Multiplayer Team Roster & Working Agreement

This is the working agreement for the 5-agent team building online multiplayer for CloSim.
Rules live in `CLAUDE.md`. Interface contracts (the source of truth) live in `Documentation/online/architecture.md`.
Each agent also has a focused brief in `Documentation/online/agents/`.

---

## Team & branches

| Agent | Role | Branch | Brief |
|-------|------|--------|-------|
| **A1** | Architect / Tech Lead | `feature/online-multiplayer` (integration) | this file + `CLAUDE.md` + `architecture.md` |
| **A2** | Netcode Foundation | `feat/netcode-foundation` | `Documentation/online/agents/A2-netcode-foundation.md` |
| **A3** | Rooms & Modes | `feat/rooms-lobby` | `Documentation/online/agents/A3-rooms-modes.md` |
| **A4** | Gameplay Sync | `feat/gameplay-sync` | `Documentation/online/agents/A4-gameplay-sync.md` |
| **A5** | Master Server + Server List | `feat/master-server` | `Documentation/online/agents/A5-master-server.md` |

All feature branches are cut from `feature/online-multiplayer`. Merges go back through **A1** (PR-style review). `main` is never touched in this effort.

---

## Dependency graph

```
        A2 (netcode foundation)
       ╱   ╲
      ▼     ▼
     A3     A4        A3↔A4 coordinate on the player/roster model
      ╲     ╱
       ▼   ▼
        A1 integrates
     A5 backend: independent (start now) · A5 Server List UI: needs A2
```

- **A2 depends on:** A1 contracts only. Must NOT reference A3/A4/A5.
- **A3 depends on:** A2 (`IOnlineConnection`) + A1 contracts. Coordinates with A4 on `RoomMemberSlot` ↔ `LoadMatch` slot mapping.
- **A4 depends on:** A2 (`IOnlineConnection`) + A1 contracts (`IMatchLauncher`, `NetworkMatchConfig`). Coordinates with A3 on the roster.
- **A5 depends on:** A1 `RoomInfo` DTO (backend) + A2 `IMasterServerClient` and connect API (Server List UI). Backend is otherwise standalone and can start immediately.

---

## File / directory ownership (to minimize merge conflicts)

> Rule: **only touch files your row lists.** Anything under `Assets/Scripts/Online/Contracts/` is **A1-owned** — request changes via A1, don't edit in place. Shared existing files (esp. `LoadMatch.cs`) have a single owner for edits (A4) to avoid conflicts.

| Agent | Owns (create/edit here) | May read (do not edit) |
|-------|--------------------------|------------------------|
| **A1** | `CLAUDE.md`, `AGENTS.md`, `Documentation/online/**`, `Assets/Scripts/Online/Contracts/**`, `Packages/manifest.json` (conflict-arbiter), integration merges | everything |
| **A2** | `Assets/Scripts/Online/Netcode/**`, `Packages/manifest.json` (add Mirror — coordinate with A1), a test bootstrap scene under `Assets/Scenes/Online/` | `Online/Contracts/**`, existing gameplay |
| **A3** | `Assets/Scripts/Online/Rooms/**`, `Assets/Scripts/Online/UI/Lobby/**`, lobby scenes/prefabs under `Assets/**/Online/Lobby/` | `Online/Contracts/**`, `Online/Netcode/**`, `LoadMatch.cs` |
| **A4** | `Assets/Scripts/Online/Sync/**`, **`Assets/Scripts/Core/LoadMatch.cs`** (de-hardcode 4→N — sole editor), minimal touches to `MatchSceneBootstrap.cs` / `Fms.cs` / `FieldScorer.cs` for sync hooks (additive; coordinate via A1) | `Online/Contracts/**`, `Online/Netcode/**`, `Online/Rooms/**` |
| **A5** | `Server/**` (backend + IaC), `Assets/Scripts/Online/Master/**`, `Assets/Scripts/Online/UI/ServerList/**`, Server List scenes/prefabs | `Online/Contracts/**`, `Online/Netcode/**` |

**Conflict-sensitive files:**
- `Packages/manifest.json` — A2 adds Mirror; any other package change routes through A1.
- `Assets/Scripts/Core/LoadMatch.cs` — **A4 only**. A3/others read it, never edit.
- `Assets/Scripts/Online/Contracts/**` — **A1 only**. Signature changes are PRs to A1 that also update `architecture.md`.
- Scenes/prefabs (`.unity`, `.prefab`) — Unity YAML merges badly; keep new online scenes/prefabs separate per agent, never co-edit one scene.

---

## Definition of Done (per agent)

**A1 (this phase):** docs + contracts + git graph committed to `feature/online-multiplayer`; stub interfaces compile-safe; open questions surfaced to coordinator. Later: integrate A2–A5 branches with contract conformance + golden-rule review.

**A2:** Mirror resolves in `manifest.json`; `CloSimNetworkManager` bootstraps; host/client start-stop works; transport selectable; LAN discovery emits `RoomInfo`; token/password auth gate accepts/rejects on connect; direct-IP connect works; concrete `IOnlineConnection` + `IMasterServerClient` HTTP client (config blank) implemented and match the doc; a test bootstrap scene + written editor test steps; no dependency on A3/A4/A5; offline unaffected.

**A3:** `IRoomService` host-authoritative model enforces 6-cap + ≤3/alliance; team assignment, ready-up, host-picks-mode, optional spectators; `PlayMode ↔ NetworkMatchConfig` adapter with offline enum path preserved; in-game create/join/room lobby UI (native Unity) replicating state across clients; written editor test steps; offline unaffected.

**A4:** `LoadMatch` de-hardcoded 4→N (up to 6) with offline 4-default preserved; server-authoritative robot spawn from the shared catalog by `robotIndex` with per-client ownership; `NetworkTransform`/sync on robots + game pieces; `FieldScorer` + `Fms` server-authoritative (reuse `Fms` scheduled server-time start hook); online single-view camera, offline split-screen intact; `IMatchLauncher` implemented; **no game-rule changes**; offline regression passes; written editor test steps.

**A5:** `Server/` backend implements register/heartbeat/deregister/list/token with **config blank** and **no web UI** (game-client-only, gated by client credential); AWS IaC with blank placeholders; in-game **Server List** UI (native Unity) lists public rooms and connects via A2's API + direct-IP/token; written run + editor test steps; endpoint never hardcoded.

---

## Coordination protocol

1. **Contracts are law.** Build against `Online.Contracts` from `architecture.md`. If you need a signature changed, open a PR to A1 that edits both the code stub and `architecture.md` in the same change; A1 ratifies, then downstream picks it up.
2. **Stay in your lane.** Only edit files your ownership row lists. Read others freely.
3. **Sync often.** Rebase/merge `feature/online-multiplayer` into your branch regularly so you get merged siblings + contract updates.
4. **Merge through A1.** PR-style; A1 reviews for golden rules (no game-content change, blank config, in-game-only room system, server-authority), contract conformance, and conflict surface.
5. **Golden rules override tickets.** If a task seems to require changing game content, an in-game→web move, or a hardcoded endpoint, **stop and escalate to A1** — do not proceed.
6. **Document your tests.** Since Unity can't compile here, every merge includes explicit editor test steps (see `CLAUDE.md` build/test section).
7. **No secrets, ever.** Endpoints/keys/ARNs stay blank placeholders in committed code.
