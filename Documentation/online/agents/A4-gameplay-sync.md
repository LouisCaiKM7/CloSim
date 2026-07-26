# A4 — Gameplay Sync

**Branch:** `feat/gameplay-sync` (off `feature/online-multiplayer`)
**Depends on:** A2 (`IOnlineConnection`) + A1 contracts (`IMatchLauncher`, `NetworkMatchConfig`). Coordinates with A3 on the roster.
**Namespace root:** `Online.Sync`
**Read first:** `CLAUDE.md`, `AGENTS.md`, `Documentation/online/architecture.md` (esp. §5, §6.4, §7, §8).

---

## Context

`Assets/Scripts/Core/LoadMatch.cs` (~2050 lines, namespace `Core`) is the central orchestrator and is **hardcoded to 4 local player slots**:

- `private readonly GameObject[] _activeRobots = new GameObject[4];` plus `_spawnedCameras[4]`, `_runtimeViews[4]`.
- `MatchSettings.players` initializes with exactly 4 entries; `Clone()` pads to 4; `GetPlayer(int)` does `players[Mathf.Clamp(index, 0, 3)]`.
- Player count + alliance come from `PlayMode` switches: `GetPlayerCount()`, `IsPlayerBlue(int)`, `UsesFourWaySplit()`.
- Robots spawn from a `Resources` folder (`robotResourceFolder`, e.g. `Robots/Rebuilt`) into a `_robotCatalog` of `RobotCatalogEntry` (robot **index** is the network-safe key).
- Cameras are **local split-screen** (`GetViewportRect`, per-index rects; single `AudioListener` on slot 0).
- Input (`PairInputs`, `Bind*`) is **local-device only** (`Gamepad.all`, `Keyboard.current`).
- Match flow is `Field.Core.Fms` (class `Fms`, **static** match state, **no instance singleton**); scoring is `Field.Scoring.FieldScorer` (**static**, alliance-keyed). The injection choke point is `MatchSceneBootstrap.Start()` → `LoadMatch.ApplySettings(MatchSettings)`.

You de-hardcode this to **N networked players (up to 6)** with server-authoritative spawn/ownership and sync — **without changing any game rule, physics, or piece behavior.**

## Scope

- **De-hardcode `LoadMatch` 4 → N (up to 6):** generalize `_activeRobots`/`_spawnedCameras`/`_runtimeViews` and the `GetPlayer`/clamp logic to a runtime count. **Preserve the offline 4-default and the `PlayMode` path exactly** — offline behavior must not change. Feed both the offline enum path and the online count path into one resolved `(blueCount, redCount)` (see §7) so downstream stops switching on `PlayMode` directly.
- **Server-authoritative robot spawn + ownership:** host spawns N robots from the shared catalog **by `robotIndex`** (so all clients agree on prefab); assign Mirror ownership so each client controls exactly its robot. Register robot prefabs with `CloSimNetworkManager` (A2 exposes the hook).
- **Sync:** `NetworkTransform` (or a tuned equivalent) on robots; sync **game pieces** (`Robot.Runtime.GamePiece` / `GamePieceManager`), **scoring** (`FieldScorer` static scores → host-authoritative replicated), and **match flow** (`Fms`).
- **Match flow via existing hook:** `Fms` already has a **scheduled server-time start path** (`HasScheduledMatch`, `ScheduledTeleopElapsedSeconds`, `ScheduledSecondsUntilEndgame`, `ApplyScheduledState`, `_scheduledServerTimeProvider`). Drive networked, time-synced match start through this existing hook — host supplies a synchronized server clock; **do not add a parallel timer.**
- **Cameras:** online = **per-client single full-screen view** (client renders only its owned robot's camera). Offline split-screen stays intact. Respect the single-`AudioListener` assumption (the local client's view owns the listener).
- **Input:** gate local input pairing to the **locally-owned** robot only; remote robots are driven by network state, not local devices.
- **`IMatchLauncher`:** implement it as the seam A3 calls (`LaunchNetworkedMatch(config, roster)`): load scene, spawn N owned robots, wire cameras.

## Files you own

- `Assets/Scripts/Online/Sync/**` (all new — network behaviours, spawn manager, launcher)
- **`Assets/Scripts/Core/LoadMatch.cs`** — sole editor for the 4→N de-hardcode. Keep edits minimal, additive where possible, behavior-preserving for offline.
- Minimal additive hooks in `MatchSceneBootstrap.cs`, `Field/Core/FMS.cs`, `Field/Scoring/FieldScorer.cs` — coordinate any edit to these with A1 (they are shared).

**Read-only:** `Online/Contracts/**` (A1), `Online/Netcode/**` (A2), `Online/Rooms/**` (A3).

## Interface contract you must honor

- Implement `Online.Contracts.IMatchLauncher` (§6.4).
- Consume `Online.Contracts.IOnlineConnection` (A2); read the roster as `IReadOnlyList<RoomMemberSlot>` from A3.
- Use `NetworkMatchConfig` for match shape; map to/from `PlayMode` via A3's adapter (don't duplicate it).

Signature changes → PR to A1.

## Coordination with A3

Agree early (via A1) on: slot ordering (blue-first vs interleaved) so `RoomMemberSlot.slotIndex` lines up with `LoadMatch` spawn slots and blue/red spawn lists; `robotIndex` → catalog mapping; spectator handling. A3 owns the roster; you own the spawn.

## Definition of Done

- `LoadMatch` runs 1–6 players online; **offline 1–4 split-screen unchanged** (regression pass on Rebuilt + Reefscape).
- Host spawns N robots; each client owns exactly one; movement replicates (NetworkTransform) with acceptable feel.
- Game pieces, `FieldScorer`, and `Fms` timer/state are identical across clients; clients cannot mutate score/state.
- Networked match start is time-synced via the existing `Fms` scheduled hook.
- Online single-view camera per client; offline split-screen intact; no duplicate `AudioListener`.
- **Zero game-rule / physics / piece-behavior changes.** Written editor test steps included.

## Testing notes

- Host a 3v3 across two+ instances; verify ownership, movement sync, scoring parity, timer parity, endgame transition parity.
- **Offline regression is mandatory every change:** run 1v0/2v0/3v0/1v1/2v2 local split-screen and confirm identical behavior.
- Watch determinism: spawn from catalog by index; never send prefab references.

## Constraints

- **No game-content changes** — this is the load-bearing rule for you. Physics, drivetrain, scoring math, piece behavior, assets: untouched. Only plumbing (slots, ownership, sync, camera routing).
- **Server-authoritative:** host decides spawn, score, match state; clients render + send input for owned robot.
- **Blank config / in-game only** — no master-server endpoints or web in your code.
- Keep `LoadMatch` edits reviewable and reversible; prefer adding methods/overloads over rewriting the enum switches.
