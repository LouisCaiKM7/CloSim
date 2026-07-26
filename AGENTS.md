# AGENTS.md — CloSim Online Multiplayer Team Roster & Working Agreement

This is the working agreement for the core 5-agent team building online multiplayer for CloSim, plus the
additive agents that have since joined the effort (replay feature, docs sync).
Rules live in `CLAUDE.md`. Interface contracts (the source of truth) live in `Documentation/online/architecture.md`.
Each core agent also has a focused brief in `Documentation/online/agents/`.

---

## Team & branches

| Agent | Role | Branch | Brief | Status |
|-------|------|--------|-------|--------|
| **A1** | Architect / Tech Lead | `feature/online-multiplayer` (integration) | this file + `CLAUDE.md` + `architecture.md` | ongoing |
| **A2** | Netcode Foundation | `feat/netcode-foundation` | `Documentation/online/agents/A2-netcode-foundation.md` | ✅ merged (landed as `Online.Net.*`, not `Online.Netcode`) |
| **A3** | Rooms & Modes | `feat/rooms-lobby` | `Documentation/online/agents/A3-rooms-modes.md` | not yet merged |
| **A4** | Gameplay Sync | `feat/gameplay-sync` | `Documentation/online/agents/A4-gameplay-sync.md` | not yet merged |
| **A5** | Master Server + Server List | `feat/master-server` | `Documentation/online/agents/A5-master-server.md` | backend ✅ merged (`Server/`); Server List UI not yet merged |
| **replay-design** | Replay feature architect (design/contracts) | `feat/replay-design` | `Documentation/online/architecture.md` (owns the replay design/contracts section) | in progress |
| **replay-backend** | Replay storage backend (AWS S3-backed) | `feat/replay-backend` | design doc above | in progress |
| **docs-sync** | Keeps `CLAUDE.md` / `README.md` / `AGENTS.md` accurate vs. merged reality | `feat/docs-sync` | this file | recurring/as-needed |

All feature branches are cut from `feature/online-multiplayer`. Merges go back through **A1** (PR-style review). `main` is never touched in this effort. The replay agents and the docs-sync agent are **additive** to the original 4-phase plan — they don't change A2–A5's ownership below except where noted (new `Online.Replay` rows).

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

> **Namespace note:** the netcode foundation landed under **`Online.Net.*`** (`Online.Net`, `Online.Net.Auth`,
> `Online.Net.Discovery`, `Online.Net.MasterClient`) — earlier planning referred to this as `Online.Netcode`;
> that name is superseded. Rooms use `Online.Rooms` / `Online.UI.Lobby`; gameplay sync uses `Online.Sync`;
> the Server List UI uses `Online.UI.ServerList`; the new replay feature uses `Online.Replay`. Update any
> stale `Online.Netcode` reference you find to `Online.Net.*`.

| Agent | Owns (create/edit here) | May read (do not edit) |
|-------|--------------------------|------------------------|
| **A1** | `CLAUDE.md`, `AGENTS.md`, `Documentation/online/**`, `Assets/Scripts/Online/Contracts/**`, `Packages/manifest.json` (conflict-arbiter), integration merges | everything |
| **A2** | `Assets/Scripts/Online/Net/**` (`Online.Net`, `Online.Net.Auth`, `Online.Net.Discovery`, `Online.Net.MasterClient`) — **merged**, `Packages/manifest.json` (added Mirror via OpenUPM `com.mirrornetworking.mirror` 96.6.4 — coordinate with A1), a test bootstrap scene under `Assets/Scenes/Online/` | `Online/Contracts/**`, existing gameplay |
| **A3** | `Assets/Scripts/Online/Rooms/**`, `Assets/Scripts/Online/UI/Lobby/**`, lobby scenes/prefabs under `Assets/**/Online/Lobby/` | `Online/Contracts/**`, `Online/Net/**`, `LoadMatch.cs` |
| **A4** | `Assets/Scripts/Online/Sync/**`, **`Assets/Scripts/Core/LoadMatch.cs`** (de-hardcode 4→N — sole editor), minimal touches to `MatchSceneBootstrap.cs` / `Fms.cs` / `FieldScorer.cs` for sync hooks (additive; coordinate via A1) | `Online/Contracts/**`, `Online/Net/**`, `Online/Rooms/**` |
| **A5** | `Server/**` (backend + IaC) — **merged**, `Assets/Scripts/Online/UI/ServerList/**` (namespace `Online.UI.ServerList`) — not yet merged, Server List scenes/prefabs | `Online/Contracts/**`, `Online/Net/**` |
| **replay-design** | Replay design/contracts inside `Documentation/online/architecture.md` (its owned section) | everything (read-only outside its section); does not edit `CLAUDE.md`/`README.md`/`AGENTS.md` |
| **replay-backend** | `Assets/Scripts/Online/Replay/**` (namespaces `Online.Contracts.Replay`, `Online.Replay.Codec`, `Online.Replay.Service`, `Online.Replay.Recorder`, `Online.Replay.UI` — recorder + Replays list/viewer, never "browser" per golden rule 3's naming convention), replay storage additions under `Server/**` (AWS S3-backed) | `Online/Contracts/**`, `Online/Net/**`, architecture.md replay contracts |
| **docs-sync** | `CLAUDE.md`, `README.md`, `AGENTS.md` only | everything (read-only) |

**Conflict-sensitive files:**
- `Packages/manifest.json` — A2 adds Mirror; any other package change routes through A1.
- `Assets/Scripts/Core/LoadMatch.cs` — **A4 only**. A3/others read it, never edit.
- `Assets/Scripts/Online/Contracts/**` — **A1 only**. Signature changes are PRs to A1 that also update `architecture.md`.
- `Documentation/online/architecture.md` — **A1**, and for the replay section specifically, the **replay-design** agent. Other agents propose changes via PR rather than editing directly.
- Scenes/prefabs (`.unity`, `.prefab`) — Unity YAML merges badly; keep new online scenes/prefabs separate per agent, never co-edit one scene.

---

## Definition of Done (per agent)

**A1 (this phase):** docs + contracts + git graph committed to `feature/online-multiplayer`; stub interfaces compile-safe; open questions surfaced to coordinator. Later: integrate A2–A5 branches with contract conformance + golden-rule review.

**A2:** Mirror resolves in `manifest.json`; `CloSimNetworkManager` bootstraps; host/client start-stop works; transport selectable; LAN discovery emits `RoomInfo`; token/password auth gate accepts/rejects on connect; direct-IP connect works; concrete `IOnlineConnection` + `IMasterServerClient` HTTP client (config blank) implemented and match the doc; a test bootstrap scene + written editor test steps; no dependency on A3/A4/A5; offline unaffected.

**A3:** `IRoomService` host-authoritative model enforces 6-cap + ≤3/alliance; team assignment, ready-up, host-picks-mode, optional spectators; `PlayMode ↔ NetworkMatchConfig` adapter with offline enum path preserved; in-game create/join/room lobby UI (native Unity) replicating state across clients; written editor test steps; offline unaffected.

**A4:** `LoadMatch` de-hardcoded 4→N (up to 6) with offline 4-default preserved; server-authoritative robot spawn from the shared catalog by `robotIndex` with per-client ownership; `NetworkTransform`/sync on robots + game pieces; `FieldScorer` + `Fms` server-authoritative (reuse `Fms` scheduled server-time start hook); online single-view camera, offline split-screen intact; `IMatchLauncher` implemented; **no game-rule changes**; offline regression passes; written editor test steps.

**A5:** `Server/` backend implements register/heartbeat/deregister/list/token with **config blank** and **no web UI** (game-client-only, gated by client credential); AWS IaC with blank placeholders; in-game **Server List** UI (native Unity) lists public rooms and connects via A2's API + direct-IP/token; written run + editor test steps; endpoint never hardcoded. *(Backend half of this DoD is met — merged. Server List UI half is still outstanding.)*

**replay-design:** replay design/contracts documented in `Documentation/online/architecture.md` (deterministic state-snapshot model, not video; recorder + Replays list/viewer contracts; AWS S3 storage seam); reconciles with A2 connection/session lifecycle and A4 gameplay state without requiring game-content changes.

**replay-backend:** in-game recorder + Replays list/viewer under `Assets/Scripts/Online/Replay/**` (`Online.Contracts.Replay` / `Online.Replay.*`); AWS S3-backed storage integration (bucket/region/credentials **blank** — `// TODO: user provides`) added to `Server/**` following A5's existing config-blank + gated pattern; written editor test steps; offline unaffected. *(Merged: contracts, codec, service client, backend storage, recorder, and the in-game Replays list/playback UI are all landed.)*

**docs-sync:** `CLAUDE.md`, `README.md`, `AGENTS.md` kept in sync with merged reality (namespaces, resolved decisions, blank-config inventory) after each merge round; never edits `architecture.md`, code, or `Server/**`.

---

## Coordination protocol

1. **Contracts are law.** Build against `Online.Contracts` from `architecture.md`. If you need a signature changed, open a PR to A1 that edits both the code stub and `architecture.md` in the same change; A1 ratifies, then downstream picks it up.
2. **Stay in your lane.** Only edit files your ownership row lists. Read others freely.
3. **Sync often.** Rebase/merge `feature/online-multiplayer` into your branch regularly so you get merged siblings + contract updates.
4. **Merge through A1.** PR-style; A1 reviews for golden rules (no game-content change, blank config, in-game-only room system, server-authority), contract conformance, and conflict surface.
5. **Golden rules override tickets.** If a task seems to require changing game content, an in-game→web move, or a hardcoded endpoint, **stop and escalate to A1** — do not proceed.
6. **Document your tests.** Since Unity can't compile here, every merge includes explicit editor test steps (see `CLAUDE.md` build/test section).
7. **No secrets, ever.** Endpoints/keys/ARNs stay blank placeholders in committed code.
