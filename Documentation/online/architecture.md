# CloSim Online Multiplayer — Architecture & Interface Contracts

> **This document is the single source of truth for the online-multiplayer effort.**
> Agents A2–A5 build against the data models and C# interface signatures defined here.
> If code and this document disagree, this document wins until A1 (Architect) amends it via PR.
>
> **Status:** Phase 0 (planning) — authored by A1. No implementation code exists yet.

---

## 1. Goal

Add **online multiplayer** to CloSim (currently local split-screen only) so that:

- Any player can **host a listen-server** from inside the client (player-hosted, no dedicated server required).
- Public rooms **register with an AWS master server** and are **browsable** by any client on the internet.
- Players can also **connect directly by IP/port**, and run **LAN / private rooms gated by a join token/password** (not publicly listed).
- Rooms hold **up to 6 players, max 3 per alliance**, supporting versus modes **1v1, 2v1, 2v2, 3v1, 3v2, 3v3** (asymmetric allowed) plus the existing offline same-alliance / co-op modes.
- **Zero changes to game content** — robot mechanics, drivetrain physics, field/scoring rules, game-piece behavior, and assets are untouched. Work is strictly additive plus the minimal plumbing refactors required to support >4 players and network ownership.

### Client-only, in-game constraint (hard rule)

- The **entire room system** — the in-game **Server List** (browse), Create Room, Join, room lobby, and mode selection — lives **inside the game executable as native Unity UI**. It is **never** a web page or a browser-accessed frontend.
- The AWS master server is a **backend directory API only**. Its **sole client is `CloSim.exe`**. There is **no web UI, no HTML frontend, no admin browser console** — nothing a web browser is meant to reach.
- The directory API is treated as **game-client-only** and **can be gated** (client API key / client token / signed requests) so it is not a casually browsable public web endpoint, even though it is HTTP under the hood.
- The term **"Server List"** is used everywhere (not "Server Browser") to avoid the web-browser connotation.

## 2. Netcode stack decision

**Mirror** (MIT). Chosen over Unity NGO / Photon because:

- MIT license is clean against CloSim's **GPLv3**.
- Self-hostable listen-servers — "anyone runs their own host".
- Ships LAN discovery + a list-server pattern we can point at our own AWS master server.
- Mature `NetworkTransform`, `NetworkBehaviour`, `[SyncVar]`, `[Command]`/`[ClientRpc]` primitives that map cleanly onto CloSim's existing MonoBehaviour robots.

Transport: **KCP (kcp2k)** as default (UDP, reliable+unreliable channels, good for realtime robot movement). Telepathy (TCP) selectable as a fallback. A2 owns the final transport wiring.

---

## 3. Target architecture

```
                                   ┌──────────────────────────────────────────┐
                                   │        AWS MASTER SERVER (Server/)        │
                                   │  Backend directory API — A5              │
                                   │  ONLY client = CloSim.exe (no web UI)     │
                                   │  REST: register / heartbeat / deregister  │
                                   │        list / issue-token                 │
                                   │  Gated by client API key / signed reqs    │
                                   │  Config endpoint = BLANK (user provides)  │
                                   └───────▲───────────────────────┬───────────┘
                register/heartbeat │       (HTTPS)                 │ list/browse
             (host only, public)   │                              │  (any client)
                                   │                              │
        ┌──────────────────────────┴───┐              ┌───────────▼───────────────┐
        │   HOST CLIENT (listen-server)│              │        JOINING CLIENT     │
        │  ┌────────────────────────┐  │   Mirror     │  ┌──────────────────────┐ │
        │  │ CloSimNetworkManager   │◄─┼──transport───┼─►│ CloSimNetworkManager │ │
        │  │  (Mirror) — A2         │  │  (KCP/UDP)   │  │  (Mirror) — A2       │ │
        │  ├────────────────────────┤  │              │  ├──────────────────────┤ │
        │  │ RoomService (host auth)│  │  room state  │  │ RoomService (client) │ │
        │  │  6-cap / ≤3 per team   │◄─┼──SyncVars────┼─►│  ready / team pick   │ │
        │  │  host-picks-mode  — A3 │  │  + Rpc/Cmd   │  │  lobby UI       — A3 │ │
        │  ├────────────────────────┤  │              │  ├──────────────────────┤ │
        │  │ LoadMatch (N-slot)     │  │ robot/piece  │  │ LoadMatch (N-slot)   │ │
        │  │ server-auth spawn      │◄─┼──NetworkTx───┼─►│ single-view camera   │ │
        │  │ FMS / scoring    — A4  │  │  + scoring   │  │                — A4  │ │
        │  └────────────────────────┘  │  SyncVars    │  └──────────────────────┘ │
        └──────────────────────────────┘              │  ┌──────────────────────┐ │
                                                       │  │ Server List UI       │ │
                                                       │  │  (native Unity, in-  │ │
                                                       │  │   game only, no web) │ │
                                                       │  │  list public rooms   │ │
                                                       │  │  direct-IP / token   │ │
                                                       │  │                — A5  │ │
                                                       └──┴──────────────────────┘─┘

LAN / PRIVATE (token-gated) path: host does NOT register with master server.
Client discovers via Mirror LAN discovery OR enters host IP:port + join token directly.
```

### Layering / dependency rule

```
  A2 Netcode Foundation   (IOnlineConnection, IMasterServerClient stubs)   ← depends on nothing internal
        ▲              ▲
        │              │
  A3 Rooms & Modes   A4 Gameplay Sync      ← both depend on A2 + these contracts
        ▲              ▲
        └──────┬───────┘
               │
  A5 Master Server + Browser  ← backend is standalone; browser UI depends on A2 (connect) + A1 RoomInfo model
```

A2 must not reference A3/A4/A5 types. A3/A4 talk to A2 only through the interfaces below. A5's backend is language-agnostic and independent; A5's in-game **Server List** (native Unity UI) depends only on `IMasterServerClient` (A2 provides the concrete HTTP client) and the `RoomInfo` DTO. **The master server has no UI of its own** — its only client is `CloSim.exe`.

---

## 4. Connection & discovery flows

### 4.1 Host creates a PUBLIC room → client browses → client joins

```
Host UI        CloSimNetworkManager   RoomService(host)   IMasterServerClient   MasterServer(AWS)   Browser UI(client)   Joining client
  │  Create Public Room  │                  │                    │                   │                  │                    │
  ├─────────────────────►│ StartHost()      │                    │                   │                  │                    │
  │                      ├─ listen-server up │                    │                   │                  │                    │
  │                      ├─ create room ────►│ build RoomInfo     │                   │                  │                    │
  │                      │                   ├─ Register(RoomInfo)─►│  POST /rooms ────►│ store + token    │                    │
  │                      │                   │◄── roomId + token ──┤◄── 200 ───────────┤                  │                    │
  │                      │                   ├─ Heartbeat(roomId) every N s ─► POST /rooms/{id}/heartbeat  │                    │
  │                      │                   │                    │                   │                  │                    │
  │                      │                   │                    │                   │  GET /rooms ◄─────┤ browse             │
  │                      │                   │                    │                   ├─ list RoomInfo[] ─►│ show list          │
  │                      │                   │                    │                   │                  │  pick room         │
  │                      │                   │                    │                   │                  ├─ Connect(endpoint)─►│ StartClient()
  │                      │◄════════ Mirror connect (KCP) ═════════════════════════════════════════════════════════════════════┤
  │                      ├─ OnServerConnect ►│ TryAddMember(slot) │  (enforce 6-cap / ≤3 team)            │                    │
  │                      │                   ├─ SyncVar RoomInfo ─────────────► replicated to all clients ►│ lobby shows member │
  │  host picks mode     ├──────────────────►│ SetMatchConfig(blueN,redN)  → SyncVar                       │                    │
  │  all ready → Launch  ├──────────────────►│ ServerStartMatch() → LoadMatch spawns N robots (A4)         │                    │
  │                      │                   ├─ Deregister(roomId) once match locked/full ─► DELETE /rooms/{id} (optional)      │
```

### 4.2 LAN / PRIVATE (token-gated) room — NOT publicly listed

```
Host UI        CloSimNetworkManager   RoomService(host)         Joining client
  │ Create Private Room  │                  │                        │
  ├─────────────────────►│ StartHost()      │                        │
  │  set joinToken="xyz" ├─ create room ───►│ RoomInfo.visibility=Private, joinToken set
  │                      │  (NO master-server Register)               │
  │                      │                  │                        │
  │  --- discovery is one of: ---           │                        │
  │  (a) Mirror LAN discovery broadcast ◄───┼──── UDP broadcast ─────► client finds host on LAN
  │  (b) host shares IP:port + token out-of-band                     │
  │                      │                  │                        ├─ Connect(endpoint, token="xyz") ─► StartClient()
  │                      │◄═══ Mirror connect + AuthRequest{token} ═══╡
  │                      ├─ OnServerAuth ───►│ ValidateJoin(token)    │
  │                      │                   ├─ token OK → accept + TryAddMember
  │                      │                   ├─ token bad → Reject(reason) ─► client shows "wrong password"
```

**Token gating applies to BOTH public and private rooms** when a token/password is set. Public simply means "listed in the master directory"; private means "not listed". A public room MAY still require a token if the host sets one.

---

## 5. Shared data models (DTOs)

These are C# types in namespace `Online.Contracts`. They are plain serializable data (Mirror-serializable + JSON-serializable for the master server). Keep them dependency-free (no `UnityEngine` types beyond primitives where avoidable; use `int`/`string`/enums).

```csharp
namespace Online.Contracts
{
    /// Room visibility in the discovery system.
    public enum RoomVisibility { Public, Private }   // Public = listed on master server; Private = not listed.

    /// A network member's role within a room.
    public enum MemberRole { Player, Spectator }

    /// Alliance selection, mirrors Core.AllianceColor but kept here to avoid a hard dep from the backend DTO.
    public enum RoomAlliance { Unassigned, Blue, Red }

    /// Lifecycle state of a room.
    public enum RoomState { Lobby, Starting, InMatch, Ended }

    /// The count-based match shape that REPLACES rigid PlayMode for online.
    /// blueCount + redCount must be within [1..3] each and total <= 6.
    [System.Serializable]
    public struct NetworkMatchConfig
    {
        public int blueCount;      // 0..3
        public int redCount;       // 0..3
        public string gameId;      // "Rebuilt" | "Reefscape" (matches scene/game display name)
        public string sceneName;   // scene to load for the match
        public int humanPlayerType;// cast of Core.HumanPlayerType; kept int to stay Core-independent
        public bool allowSpectators;

        public int TotalPlayers => blueCount + redCount;
        public bool IsValid => blueCount is >= 0 and <= 3
                            && redCount  is >= 0 and <= 3
                            && TotalPlayers is >= 1 and <= 6;
    }

    /// One occupied slot in a room (a connected member).
    [System.Serializable]
    public struct RoomMemberSlot
    {
        public int connectionId;   // Mirror connection id (server-assigned); 0 = host on some transports
        public int slotIndex;      // 0..5 stable index within the room
        public string displayName;
        public RoomAlliance alliance;
        public MemberRole role;
        public bool isReady;
        public bool isHost;
        public int robotIndex;     // index into the game's robot catalog (per-member robot choice)
    }

    /// Public-facing room summary. This is what the master server stores and what the
    /// in-game Server List shows. Must be JSON-serializable for the backend.
    [System.Serializable]
    public struct RoomInfo
    {
        public string roomId;          // server-assigned unique id
        public string name;            // host-chosen display name
        public string hostName;
        public string address;         // public IP or hostname of the listen-server
        public int    port;            // transport port
        public string gameId;          // "Rebuilt" | "Reefscape"
        public string region;          // optional, free-form ("us-east", "eu", "") 
        public int    playerCount;     // current occupied player slots
        public int    capacity;        // always 6 for now
        public RoomVisibility visibility;
        public bool   requiresToken;   // true if a join token/password is set (never send the token itself)
        public RoomState state;
        public string version;         // client build/protocol version for compatibility filtering
    }

    /// Result of a connection attempt, returned to UI.
    public enum ConnectResult { Success, Rejected_BadToken, Rejected_Full, Rejected_Version, Timeout, TransportError }
}
```

### Model rules

- `NetworkMatchConfig` is the **online** representation of match shape. It is mapped **to/from** the legacy `Core.PlayMode` by A3's adapter (see §7). Offline play keeps using `PlayMode` unchanged.
- `capacity` is fixed at **6**. Per-alliance cap **3** is enforced by `RoomService`, not encoded structurally, so asymmetric shapes (e.g. 3v1) work.
- The **join token is never placed in `RoomInfo`**. Only `requiresToken` is exposed. The token travels in the Mirror auth handshake and is validated host-side.

---

## 6. C# interface contracts

All interfaces live in namespace `Online.Contracts`. A2 provides the concrete implementations (except the backend). A3/A4/A5's in-game code consume these interfaces — never concrete Mirror types directly, so the layers stay swappable and testable.

> A1 ships tiny compile-safe **stub** interface files under `Assets/Scripts/Online/Contracts/` so downstream code has something to reference. A2 owns turning them into working implementations and MAY refine signatures **via PR to A1** (update this doc in the same PR).

### 6.1 `IOnlineConnection` — owned/implemented by A2

The connection lifecycle façade. Rooms (A3), Gameplay (A4), and the Browser (A5) call this; none of them call Mirror's `NetworkManager` directly.

```csharp
namespace Online.Contracts
{
    public interface IOnlineConnection
    {
        // --- State ---
        bool IsHost { get; }
        bool IsClient { get; }
        bool IsConnected { get; }
        ConnectionRole Role { get; }          // None | Host | Client
        string LocalEndpointAddress { get; }  // best-effort public/LAN address of this host

        // --- Host lifecycle ---
        void StartHost(HostStartOptions options);   // opens listen-server (transport, port, token, visibility)
        void StopHost();

        // --- Client lifecycle ---
        void StartClient(ConnectEndpoint endpoint);  // direct IP/port (+ optional token)
        void StopClient();

        // --- LAN discovery ---
        void StartLanDiscovery();
        void StopLanDiscovery();

        // --- Events (UI / services subscribe) ---
        event System.Action OnHostStarted;
        event System.Action OnHostStopped;
        event System.Action<ConnectResult> OnClientConnected;   // Success or a rejection reason
        event System.Action OnClientDisconnected;
        event System.Action<RoomInfo> OnLanRoomDiscovered;      // one per discovered LAN host
    }

    public enum ConnectionRole { None, Host, Client }

    public struct HostStartOptions
    {
        public int port;                 // 0 => transport default
        public string joinToken;         // "" => no token gating
        public RoomVisibility visibility;
        public string roomName;
        public string gameId;
        public string version;
    }

    public struct ConnectEndpoint
    {
        public string address;
        public int port;
        public string joinToken;         // "" if none
        public string version;
    }
}
```

### 6.2 `IRoomService` — contract by A1, implemented by A3 (host-authoritative)

The networked room/lobby state machine. Host instance is authoritative; client instances are read-mostly mirrors that send intent via commands. Enforces 6-cap and ≤3/alliance.

```csharp
namespace Online.Contracts
{
    public interface IRoomService
    {
        RoomInfo CurrentRoom { get; }
        System.Collections.Generic.IReadOnlyList<RoomMemberSlot> Members { get; }
        NetworkMatchConfig MatchConfig { get; }
        RoomState State { get; }

        // --- Host authority ---
        bool TryAddMember(int connectionId, string displayName, out RoomMemberSlot slot, out string rejectReason);
        void RemoveMember(int connectionId);
        void SetMatchConfig(NetworkMatchConfig config);          // host picks mode (blueCount,redCount,...)
        bool CanStartMatch(out string reason);                  // all players ready + valid config
        void ServerStartMatch();                                // hands off to A4 spawn (see IMatchLauncher)

        // --- Member intent (client → host commands, host validates) ---
        void RequestAlliance(int connectionId, RoomAlliance alliance);  // rejected if that alliance already has 3
        void RequestRole(int connectionId, MemberRole role);
        void RequestReady(int connectionId, bool ready);
        void RequestRobot(int connectionId, int robotIndex);

        // --- Events ---
        event System.Action<RoomInfo> OnRoomChanged;
        event System.Action<System.Collections.Generic.IReadOnlyList<RoomMemberSlot>> OnMembersChanged;
        event System.Action<NetworkMatchConfig> OnMatchConfigChanged;
        event System.Action OnMatchStarting;
    }
}
```

### 6.3 `IMasterServerClient` — contract by A1, HTTP client by A2, backend by A5

In-game client for the AWS master directory. **All endpoints are BLANK placeholders** until the user provides them.

```csharp
namespace Online.Contracts
{
    public interface IMasterServerClient
    {
        // The ONLY caller of this is the game client (CloSim.exe). The master server exposes
        // NO web UI. Requests SHOULD carry a client credential (API key / signed request) so the
        // directory is game-client-only, not a casually browsable public web endpoint.
        // Endpoint + credential are injected/config-driven. See MasterServerConfig — MUST default to "".
        // MASTER_SERVER_URL = ""  // TODO: user provides hosting endpoint

        // Host side (public rooms only):
        System.Threading.Tasks.Task<RegisterResult> RegisterAsync(RoomInfo room);
        System.Threading.Tasks.Task HeartbeatAsync(string roomId);
        System.Threading.Tasks.Task DeregisterAsync(string roomId);

        // Client side (browser):
        System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<RoomInfo>> ListRoomsAsync(RoomQuery query);

        bool IsConfigured { get; }   // false while MASTER_SERVER_URL == "" — UI shows "master server not configured"
    }

    public struct RegisterResult { public bool ok; public string roomId; public string error; }

    public struct RoomQuery
    {
        public string gameId;    // "" => all
        public string region;    // "" => all
        public bool hideFull;
        public bool hidePrivate; // default true — private rooms are never listed anyway
        public string version;   // "" => all
    }

    /// Config holder. Concrete value supplied by the user later; NEVER hardcode a real endpoint.
    public static class MasterServerConfig
    {
        public const string MasterServerUrl = ""; // TODO: user provides hosting endpoint (AWS)
        public const string ClientApiKey    = ""; // TODO: user provides client credential (gates the directory API)
        public const int HeartbeatSeconds = 15;
        public const int RoomTtlSeconds = 45;      // master server drops rooms after 3 missed heartbeats
    }
}
```

### 6.4 `IMatchLauncher` — contract by A1, implemented by A4 (bridges room → LoadMatch)

The seam between the lobby (A3) and the match spawn (A4). Keeps A3 ignorant of `LoadMatch` internals and keeps A4 ignorant of room UI.

```csharp
namespace Online.Contracts
{
    public interface IMatchLauncher
    {
        // Called host-side by RoomService.ServerStartMatch(). Translates the room roster + config
        // into an N-slot networked match: loads the scene, spawns N server-authoritative robots,
        // assigns per-client ownership, sets up single-view cameras online.
        void LaunchNetworkedMatch(NetworkMatchConfig config,
                                  System.Collections.Generic.IReadOnlyList<RoomMemberSlot> roster);

        bool IsMatchActive { get; }
        event System.Action OnMatchSpawned;   // all robots spawned + owned
    }
}
```

---

## 7. Legacy `PlayMode` ↔ `NetworkMatchConfig` mapping (A3 owns the adapter)

Offline play is **unchanged**: `MatchSettings.playMode` (the `Core.PlayMode` enum) continues to drive `LoadMatch.GetPlayerCount()` / `IsPlayerBlue()`. Online play uses `NetworkMatchConfig(blueCount, redCount)`.

A3 provides a pure adapter (offline enum stays canonical for offline, counts are canonical for online):

| `PlayMode` (offline) | blueCount | redCount | Notes |
|----------------------|-----------|----------|-------|
| OneVsZero            | 1         | 0        | same-alliance solo |
| TwoVsZero            | 2         | 0        | co-op |
| ThreeVsZero          | 3         | 0        | co-op |
| OneVsOne             | 1         | 1        | versus |
| TwoVsTwo             | 2         | 2        | versus |

Online-only shapes with **no** `PlayMode` equivalent (must be represented purely as counts): **2v1, 3v1, 3v2, 3v3**, and the reverse asymmetric forms. `LoadMatch`'s hardcoded `PlayMode` switch statements (`GetPlayerCount`, `IsPlayerBlue`, `UsesFourWaySplit`) are refactored by A4 to accept a count-based source when running online, while preserving the enum path for offline. **Recommended:** introduce an internal `(int blueCount, int redCount)` resolved once at match start; both the enum path and the network path feed it, so downstream code stops switching on `PlayMode` directly.

---

## 8. Existing code seams (confirmed by reading the source)

These are the concrete integration points A3/A4 refactor. **Additive-first; minimal edits only where de-hardcoding is unavoidable.**

- `Assets/Scripts/Core/LoadMatch.cs`
  - `MatchSettings { List<PlayerMatchSettings> players; PlayMode playMode; bool useBlueAlliance; TrackingType trackingType; }` — `players` list and `Clone()`/`GetPlayer()` are **hard-padded/clamped to 4**. A4 must generalize to N (up to 6) without breaking the offline 4-default.
  - `PlayerMatchSettings { int robotIndex, blueSpawnIndex, redSpawnIndex; bool useVanityBumpers; StationNum driverStation; Cameras view; }` — per-slot state; maps onto `RoomMemberSlot`.
  - `private readonly GameObject[] _activeRobots = new GameObject[4];` (+ `_spawnedCameras[4]`, `_runtimeViews[4]`) — **the 4-slot arrays A4 de-hardcodes to N**.
  - `GetPlayerCount()` / `IsPlayerBlue(int)` / `UsesFourWaySplit()` — `PlayMode` switch statements to be fed by the count model online.
  - `GetViewportRect(int)` — split-screen rects; online path uses full-screen single view per client.
  - Robot spawn reads from `Resources` folder `robotResourceFolder` ("Robots/Rebuilt" etc.) → `_robotCatalog` (`RobotCatalogEntry`). Server-authoritative spawn (A4) must spawn from the same catalog so all clients agree on prefab by index.
  - `RobotCatalogEntry { int Index; GameObject Prefab; string DisplayName; int TeamNumber; Sprite TeamIcon, PreviewSprite; }` — robot index is the network-safe key (send `robotIndex`, not prefab refs).
- `Assets/Scripts/Core/GameSessionManager.cs` — `MatchLaunchData { string gameDisplayName, sceneName; MatchSettings matchSettings; HumanPlayerType humanPlayerType; }`, DontDestroyOnLoad singleton. Online path populates a `MatchLaunchData` equivalent from `NetworkMatchConfig` + roster before the networked scene load.
- `Assets/Scripts/Core/MatchSceneBootstrap.cs` — applies launch data to `LoadMatch` on scene start (`ApplySettings`, `SetHumanPlayerType`, `ResetField`). Online bootstrap must run **server-authoritatively** and defer robot ownership assignment until clients are present.
- `Assets/Scripts/Field/Core/FMS.cs` — class is **`Fms`** (namespace `Field.Core`), match flow/scoring. Match state lives in **`static` fields** (`MatchTimer`, `RobotState`, `MatchState { Auto, Teleop, Endgame, Finished }`); there is **no instance singleton** — coupling is via statics + `LoadMatch.SetFms(this)`. A4 makes match state server-authoritative; clients render, host decides.
  - **Key reuse hook (confirmed):** `Fms` already has a **scheduled server-time start path** — `HasScheduledMatch`, `ScheduledTeleopElapsedSeconds`, `ScheduledSecondsUntilEndgame`, private `ApplyScheduledState()` / `_scheduledServerTimeProvider : Func<double>`. A4 should drive networked, time-synced match start through this existing hook (host supplies a synchronized server clock) rather than adding a parallel timer.
- `Assets/Scripts/Field/Scoring/FieldScorer.cs` — scores are **`static`, alliance-keyed** (`BlueFuel/RedFuel/BlueCoral/RedCoral/BlueAlgae/RedAlgae`), reset via `static ResetCounters()`. A4 replicates these host→client (host authoritative); do not let clients mutate score.
- **Injection choke point (confirmed):** `MatchSceneBootstrap.Start()` → `LoadMatch.ApplySettings(MatchSettings)` is the *single* place match settings enter a scene. Online path injects replicated settings here. Note `GameSetupController` currently always ships a **default** `new MatchSettings()` (PlayMode.OneVsZero) — there is today **no PlayMode-selection or ready-up UI**; A3 builds that fresh for online (and it may later be back-ported to offline, out of scope here).
- **Biggest rework area (confirmed):** `LoadMatch.PairInputs()` and all `Bind*` are **local-device only** (`Gamepad.all`, `Keyboard.current`). Remote players have no local device — A4 must gate local input pairing to the locally-owned robot only and drive remote robots from network state.

---

## 9. Namespaces & conventions (match existing)

- Top-level namespaces already in use: `Core`, `UI.*` (e.g. `UI.RobotSelection`, `UI.MainMenu`), `Field.Core`, `Field.Scoring`, `Field.SeasonSpecific.*`, `Robot.*` (`Robot.Runtime`, `Robot.Builders`), `CameraControls`, `Utilities`.
- **New online code** goes under a new `Online.*` root: `Online.Contracts` (interfaces/DTOs — this doc), `Online.Netcode` (A2), `Online.Rooms` (A3), `Online.Sync` (A4), `Online.Browser` / `Online.Master` (A5 client). Backend service lives in top-level `Server/` (outside `Assets/`).
- MyBox attributes (`[Foldout]`, `[ReadOnly]`, `[Separator]`, `[MustBeAssigned]`) are used in the project — use them for inspector-facing serialized fields to match style.
- Unity Input System 1.7.0 for input; do not introduce a second input path.
- Style: 4-space indent, `_camelCase` private fields, PascalCase public members, `[SerializeField] private` for inspector fields — mirror `LoadMatch.cs`.

---

## 10. Open questions for the coordinator/user

1. **NAT traversal / port-forwarding.** Player-hosted listen-servers on the public internet require the host to be reachable (port forward / UPnP / relay). Mirror has no built-in relay. Options: (a) document port-forwarding as a user responsibility for public hosting, (b) add a relay later. **Recommend (a) for v1**, note (b) as future work. Needs a decision before A2 finalizes transport.
2. **Master-server auth model.** Is room registration open, or does the host need an API key from the master server? Affects A5's backend and `IMasterServerClient`. Placeholder assumes open register + optional per-room token; confirm.
3. **Version/protocol compatibility policy.** `RoomInfo.version` filtering — hard block on mismatch, or warn? Affects browser UX (A5) and connect rejection (A2).
4. **Human player + spectators over network.** Existing `HumanPlayerType` (Bucket/Dumper) is tied to alliance slots. Do spectators get any human-player control, or are they pure observers? Assumed pure observers.
5. **Persistence of the master directory.** In-memory vs. durable store (DynamoDB/SQLite). Recommend a simple in-memory + TTL store for v1 since rooms are ephemeral; confirm with A5.
```
