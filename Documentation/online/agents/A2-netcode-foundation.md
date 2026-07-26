# A2 — Netcode Foundation

**Branch:** `feat/netcode-foundation` (off `feature/online-multiplayer`)
**Depends on:** A1 contracts only. **Must NOT reference A3/A4/A5 types.**
**Namespace root:** `Online.Netcode`
**Read first:** `CLAUDE.md`, `AGENTS.md`, `Documentation/online/architecture.md` (esp. §4, §6.1, §6.3).

---

## Context

CloSim has **no networking today** — `Packages/manifest.json` contains only `com.unity.inputsystem` and standard modules. You are laying the foundation the rest of the team builds on. The stack is **Mirror** (MIT), chosen for GPLv3 fit and self-hostable player-hosted listen-servers. You provide a clean connection API (`IOnlineConnection`) and the master-server HTTP client (`IMasterServerClient`) so Rooms (A3), Gameplay (A4), and the Server List (A5) never touch Mirror directly.

## Scope

- Add **Mirror** to `Packages/manifest.json` (coordinate the package add with A1, since manifest is a conflict-sensitive file). Document the exact source used (OpenUPM / Git URL / Asset Store) in this branch's PR.
- Build **`CloSimNetworkManager`** (subclass Mirror's `NetworkManager`) as the CloSim-specific bootstrap: transport selection, spawnable prefab registration hooks (A4 fills the robot list), scene management hooks.
- **Connection lifecycle:** `StartHost` / `StartClient` / `StopHost` / `StopClient`, with clean state + events.
- **Transport:** wire **KCP (kcp2k)** as default; make **Telepathy** selectable. Expose port config.
- **LAN discovery:** Mirror `NetworkDiscovery` broadcasting/receiving a `RoomInfo` payload; emit `OnLanRoomDiscovered`.
- **Auth gating:** a Mirror `NetworkAuthenticator` that validates a **join token/password** on connect (both public-with-token and private rooms). Reject with a reason mapping to `ConnectResult`.
- **Direct-IP connect API:** `StartClient(ConnectEndpoint)` with address/port/token/version.
- **`IMasterServerClient` HTTP client** (concrete impl of the A1 interface) using `UnityWebRequest`: register/heartbeat/deregister/list, carrying the client credential. **Config blank** (`MasterServerConfig.MasterServerUrl == ""` → `IsConfigured == false`, no calls made).
- A minimal **test bootstrap scene** under `Assets/Scenes/Online/` with buttons to Start Host / Start Client (127.0.0.1) so the user can validate without A3/A4.

## Files you own

- `Assets/Scripts/Online/Netcode/**` (all new)
- `Packages/manifest.json` (add Mirror only — coordinate with A1)
- `Assets/Scenes/Online/NetcodeTest.unity` (+ any test prefabs, kept separate)

Do **not** edit `Assets/Scripts/Online/Contracts/**` (A1-owned) — implement against it. Do not edit `LoadMatch.cs` (A4).

## Interface contract you must honor

Implement, matching `architecture.md` §6.1 / §6.3 exactly:

- `Online.Contracts.IOnlineConnection` — concrete `CloSimOnlineConnection : MonoBehaviour, IOnlineConnection` (or similar) wrapping `CloSimNetworkManager`.
- `Online.Contracts.IMasterServerClient` — concrete `MasterServerHttpClient : IMasterServerClient`.
- Honor `HostStartOptions`, `ConnectEndpoint`, `ConnectResult`, `ConnectionRole`.

If you need a signature change, PR it to A1 (update the doc + the stub in the same change). Do not fork the types.

## Definition of Done

- Mirror resolves in the editor; project compiles.
- Host/client start-stop works to `127.0.0.1`; second instance (ParrelSync or standalone build) connects.
- Bad token → connection rejected with `ConnectResult.Rejected_BadToken`; good token → `Success`.
- LAN discovery emits `RoomInfo` for a host on the same subnet.
- `IMasterServerClient` makes no network calls while `MasterServerUrl == ""` and reports `IsConfigured == false`.
- No references to A3/A4/A5 code. Offline play unaffected.
- Written editor test steps included in the PR.

## Testing notes (Unity can't compile here)

- Use **ParrelSync** (clone the project) or a standalone build for a second instance.
- Test matrix: Host+Client localhost; Client to a LAN IP; bad/good token; StopHost while a client is connected (client should get `OnClientDisconnected`).
- To test `IMasterServerClient` locally, point `MasterServerUrl` at A5's local backend **only in your editor** — never commit a non-empty value.

## Constraints

- **No game-content changes.** You add netcode plumbing only.
- **Blank config.** `MASTER_SERVER_URL = "" // TODO: user provides hosting endpoint` and the client API key stay blank.
- **In-game only.** No web anything. The master client is HTTP under the hood but only ever called by the game.
- **Server-authoritative** posture: your API makes it natural for the host to be authoritative (A4/A3 enforce it).

## Risks / watch-outs

- **NAT/port-forwarding** for public hosting — Mirror has no relay. Document port-forward as a user responsibility for v1 (see architecture §10 Q1); don't build a relay unless the coordinator asks.
- Keep the auth handshake payload small and versioned (`ConnectEndpoint.version` / `HostStartOptions.version`).
- One `AudioListener` assumption exists in `LoadMatch` (slot 0) — not your problem to fix, but don't add a second listener in the test scene.
