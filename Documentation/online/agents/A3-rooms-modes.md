# A3 — Rooms & Modes

**Branch:** `feat/rooms-lobby` (off `feature/online-multiplayer`)
**Depends on:** A2 (`IOnlineConnection`) + A1 contracts. Coordinates with A4 on the roster ↔ `LoadMatch` slot mapping.
**Namespace roots:** `Online.Rooms`, `Online.UI.Lobby`
**Read first:** `CLAUDE.md`, `AGENTS.md`, `Documentation/online/architecture.md` (esp. §4, §5, §6.2, §7).

---

## Context

Today CloSim has **no room/lobby concept and no mode-selection UI**. `GameSetupController.PlaySelectedGame()` just ships a default `new MatchSettings()` (PlayMode.OneVsZero) into the scene. Offline modes come from the rigid `Core.PlayMode` enum, which **caps at 2v2** (`OneVsZero, TwoVsZero, OneVsOne, ThreeVsZero, TwoVsTwo`) and drives `LoadMatch.GetPlayerCount()` / `IsPlayerBlue()`.

You build the **networked room/lobby** system and a **flexible `(blueCount, redCount)` mode model** for online, while keeping the offline enum path working.

## Scope

- **`IRoomService`** — host-authoritative networked room model (Mirror `NetworkBehaviour` on a room manager object):
  - Capacity **6**, **≤3 per alliance** (enforced server-side; asymmetric shapes allowed).
  - Team assignment (blue/red), **ready-up**, **host-picks-mode**, optional **spectator** slots for remaining capacity.
  - Members replicate via SyncVars/SyncList; clients send intent via `[Command]`, host validates and applies.
- **`NetworkMatchConfig`** (`blueCount`, `redCount`, `gameId`, `sceneName`, `humanPlayerType`, `allowSpectators`) as the online mode representation. Supported: 1v1, 2v1, 2v2, 3v1, 3v2, 3v3 + reverses + co-op (Xv0).
- **`PlayMode ↔ NetworkMatchConfig` adapter** (§7 table): offline enum stays canonical for offline; counts are canonical for online. Provide a pure mapping class in `Online.Rooms`.
- **In-game lobby UI** (native Unity, `Online.UI.Lobby`): Create Room (public/private, name, token, mode), Join (from list / by IP / LAN), and the Room screen (member list, team pick, ready toggle, host mode picker, start button). Uses A2's `IOnlineConnection` for connect and A5's Server List for browse (don't rebuild the list — link to it).

## Files you own

- `Assets/Scripts/Online/Rooms/**` (room service, adapter)
- `Assets/Scripts/Online/UI/Lobby/**` (create/join/room screens)
- New lobby scenes/prefabs under `Assets/**/Online/Lobby/` (keep separate; never co-edit another agent's scene)

**Read-only:** `Online/Contracts/**` (A1), `Online/Netcode/**` (A2), `LoadMatch.cs` (A4). Do **not** edit `LoadMatch.cs`.

## Interface contract you must honor

- Implement `Online.Contracts.IRoomService` (§6.2) host-authoritatively.
- Consume `Online.Contracts.IOnlineConnection` (A2) — never Mirror `NetworkManager` directly.
- Produce/consume `RoomInfo`, `RoomMemberSlot`, `NetworkMatchConfig`, `RoomVisibility`, `MemberRole`, `RoomAlliance`, `RoomState` (§5).
- Hand off to A4 via `IMatchLauncher.LaunchNetworkedMatch(config, roster)` when the host starts the match — **do not spawn robots yourself** (that's A4).

Signature changes → PR to A1 (update doc + stub together).

## Coordination with A4

The `RoomMemberSlot` roster (slotIndex, alliance, robotIndex, role) is the bridge to `LoadMatch`'s per-slot model (`PlayerMatchSettings`). Agree with A4 on: slot ordering (blue slots first, then red? or interleaved?), how `robotIndex` maps to the shared catalog, and where spectators sit. Settle this early via A1.

## Definition of Done

- Host creates a room; a second instance joins; member list replicates.
- 6-cap and ≤3/alliance enforced server-side (join rejected / alliance switch rejected when full).
- Ready-up + host mode pick replicate; start gated on all players ready + valid config.
- Spectators fill remaining capacity when allowed.
- Adapter round-trips the 5 legacy `PlayMode` values and represents the online-only shapes (2v1/3v1/3v2/3v3) purely as counts.
- **Offline play unchanged** — the enum path still drives local split-screen.
- Lobby UI is 100% native Unity in-game. Written editor test steps included.

## Testing notes

- Two instances (ParrelSync/build). Try to seat a 4th player on an alliance that already has 3 → rejected. Fill to 6 → 7th join rejected (or seated as spectator if allowed). Toggle ready on all → start enabled.
- Verify a private room requires the token (A2's auth) and a public room appears in A5's Server List.

## Constraints

- **No game-content changes.** Rooms/UI/adapter only.
- **In-game only.** All room UI is native Unity; no web.
- **Blank config** everywhere the master server is referenced.
- **Server-authoritative:** the host owns room truth; clients only request.

## Risks / watch-outs

- Keep the offline `PlayMode` path untouched — the adapter is additive; don't rewrite `LoadMatch`'s enum switches (that's A4's careful refactor).
- Human-player type (Bucket/Dumper) is alliance-slot-bound; decide with A4 whether spectators get any human-player control (default: pure observers — see architecture §10 Q4).
