// CloSim Online Multiplayer — host-authoritative networked room/lobby (A3 Rooms & Modes).
// Namespace: Online.Rooms. A Mirror NetworkBehaviour that owns the replicated room truth.
//
// AUTHORITY MODEL (golden rule 5): the HOST is authoritative. Clients only ever REQUEST via
// [Command]s; the host validates every request (RoomValidation) and applies it. Replication is via
// [SyncVar] scalars + a SyncList<RoomMemberSlot>. To stay robust across Mirror versions this class
// does NOT subscribe to per-op SyncVar/SyncList callbacks (whose delegate signatures have drifted
// between releases); instead the server bumps a monotonic [SyncVar] revision on every mutation and
// each instance re-raises the IRoomService events when the revision advances. One-frame latency in a
// lobby is irrelevant.
//
// SPAWNING: this is a singleton networked object. RoomNetworkBootstrap spawns it host-side and
// registers its prefab so it replicates to clients. Instance is discoverable via RoomService.Instance.
//
// MATCH HANDOFF: ServerStartMatch() calls IMatchLauncher.LaunchNetworkedMatch(config, roster) — the
// A4 seam. We call it by INTERFACE only and never spawn robots or touch LoadMatch ourselves.

using System;
using System.Collections.Generic;
using Mirror;
using Online.Contracts;
using UnityEngine;

namespace Online.Rooms
{
    [AddComponentMenu("CloSim/Rooms/Room Service")]
    [DisallowMultipleComponent]
    public class RoomService : NetworkBehaviour, IRoomService, ILobbyRoom
    {
        /// <summary>The host's Mirror connectionId. In host mode the local connection is always 0.</summary>
        public const int HostConnectionId = 0;

        /// <summary>Live singleton for the current room object (server or client). Null when no room exists.</summary>
        public static RoomService Instance { get; private set; }

        /// <summary>
        /// The match launcher (A4). Assigned by A4's bootstrap. When null, ServerStartMatch scans the
        /// scene for any MonoBehaviour implementing IMatchLauncher; if still none, it raises the room
        /// events but performs no spawn (so the lobby flow is testable without A4).
        /// </summary>
        public static IMatchLauncher Launcher { get; set; }

        // ---------------------------------------------------------------- replicated state

        [SyncVar] private RoomInfo _room;
        [SyncVar] private NetworkMatchConfig _config;
        [SyncVar] private RoomState _state = RoomState.Lobby;
        [SyncVar] private int _hostConnectionId = HostConnectionId;
        [SyncVar] private int _revision;

        private readonly SyncList<RoomMemberSlot> _members = new();

        // ---------------------------------------------------------------- change detection / caches

        private int _lastRevision = int.MinValue;
        private RoomState _lastState = (RoomState)(-1);
        private float _nextReconcileTime;

        // ---------------------------------------------------------------- IRoomService surface

        public RoomInfo CurrentRoom => _room;
        public IReadOnlyList<RoomMemberSlot> Members => _members;
        public NetworkMatchConfig MatchConfig => _config;
        public RoomState State => _state;

        public event Action<RoomInfo> OnRoomChanged;
        public event Action<IReadOnlyList<RoomMemberSlot>> OnMembersChanged;
        public event Action<NetworkMatchConfig> OnMatchConfigChanged;
        public event Action OnMatchStarting;

        /// <summary>The local player's connectionId (0 for host, the client's id otherwise, -1 if offline).</summary>
        public int LocalConnectionId
        {
            get
            {
                if (NetworkServer.active) return HostConnectionId;                 // host
                if (NetworkClient.active && NetworkClient.connection != null)
                    return NetworkClient.connection.connectionId;                  // remote client
                return -1;
            }
        }

        /// <summary>The local member's slot, if seated.</summary>
        public bool TryGetLocalMember(out RoomMemberSlot slot)
        {
            int id = LocalConnectionId;
            int idx = RoomValidation.IndexOf(_members, id);
            if (idx >= 0) { slot = _members[idx]; return true; }
            slot = default;
            return false;
        }

        // ---------------------------------------------------------------- Unity / Mirror lifecycle

        private void Awake() => Instance = this;

        public override void OnStartServer()
        {
            base.OnStartServer();
            Instance = this;
            // Seed defaults if the host bootstrap hasn't initialised yet.
            if (string.IsNullOrEmpty(_room.name) && string.IsNullOrEmpty(_room.gameId))
                _room = DefaultRoomInfo();
            SeatHostIfMissing();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Instance = this;
            _lastRevision = int.MinValue; // force a fresh event burst so the UI populates
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (Instance == this) Instance = null;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (isServer)
                ServerReconcileDisconnected();

            if (_revision != _lastRevision)
            {
                _lastRevision = _revision;
                RaiseAll();
            }

            if (_state != _lastState)
            {
                _lastState = _state;
                if (_state == RoomState.Starting)
                    OnMatchStarting?.Invoke();
            }
        }

        private void RaiseAll()
        {
            OnMembersChanged?.Invoke(_members);
            OnRoomChanged?.Invoke(_room);
            OnMatchConfigChanged?.Invoke(_config);
        }

        // ================================================================ HOST AUTHORITY (server) ===

        /// <summary>
        /// Host-side room seeding, called by RoomNetworkBootstrap immediately after spawn. Fills the
        /// public RoomInfo and initial config, and stamps the host's display name onto its own slot.
        /// </summary>
        [Server]
        public void ServerInitialize(RoomInfo info, NetworkMatchConfig config, string hostDisplayName)
        {
            _room = info;
            _config = config;
            _state = RoomState.Lobby;
            _hostConnectionId = HostConnectionId;
            SeatHostIfMissing(hostDisplayName);
            RefreshRoomCounts();
            ServerTouch();
        }

        [Server]
        private void SeatHostIfMissing(string hostDisplayName = "Host")
        {
            if (RoomValidation.IndexOf(_members, HostConnectionId) >= 0)
                return;

            _members.Add(new RoomMemberSlot
            {
                connectionId = HostConnectionId,
                slotIndex = NextFreeSlotIndex(),
                displayName = string.IsNullOrWhiteSpace(hostDisplayName) ? "Host" : hostDisplayName,
                alliance = RoomAlliance.Blue,
                role = MemberRole.Player,
                isReady = false,
                isHost = true,
                robotIndex = 0
            });
        }

        /// <inheritdoc/>
        [Server]
        public bool TryAddMember(int connectionId, string displayName, out RoomMemberSlot slot, out string rejectReason)
        {
            int existing = RoomValidation.IndexOf(_members, connectionId);
            if (existing >= 0)
            {
                slot = _members[existing];
                rejectReason = "";
                return true; // idempotent re-join
            }

            if (!RoomValidation.CanSeatNewMember(_members, out rejectReason))
            {
                slot = default;
                return false;
            }

            // New members join as an unassigned Player when a player slot is free; otherwise, if the
            // room allows spectators, they join as a pure-observer Spectator.
            bool asPlayer = RoomValidation.CanBecomePlayer(_members, connectionId, out _);
            MemberRole role = asPlayer ? MemberRole.Player
                : (_config.allowSpectators ? MemberRole.Spectator : MemberRole.Player);

            if (!asPlayer && !_config.allowSpectators)
            {
                slot = default;
                rejectReason = "Player slots are full and spectators are disabled.";
                return false;
            }

            slot = new RoomMemberSlot
            {
                connectionId = connectionId,
                slotIndex = NextFreeSlotIndex(),
                displayName = string.IsNullOrWhiteSpace(displayName) ? $"Player {connectionId}" : displayName,
                alliance = RoomAlliance.Unassigned,
                role = role,
                isReady = false,
                isHost = connectionId == _hostConnectionId,
                robotIndex = 0
            };
            _members.Add(slot);
            RefreshRoomCounts();
            ServerTouch();
            return true;
        }

        /// <inheritdoc/>
        [Server]
        public void RemoveMember(int connectionId)
        {
            int idx = RoomValidation.IndexOf(_members, connectionId);
            if (idx < 0) return;
            _members.RemoveAt(idx);
            RefreshRoomCounts();
            ServerTouch();
        }

        /// <inheritdoc/>
        [Server]
        public void SetMatchConfig(NetworkMatchConfig config)
        {
            _config = config;
            // If the mode no longer allows spectators, demote nobody automatically; just clamp readiness.
            RefreshRoomCounts();
            ServerTouch();
        }

        /// <inheritdoc/>
        public bool CanStartMatch(out string reason) => RoomValidation.CanStart(_members, _config, out reason);

        /// <inheritdoc/>
        [Server]
        public void ServerStartMatch()
        {
            if (!CanStartMatch(out string reason))
            {
                Debug.LogWarning($"[RoomService] ServerStartMatch blocked: {reason}");
                return;
            }

            _state = RoomState.Starting;
            var updated = _room;
            updated.state = RoomState.Starting;
            _room = updated;
            ServerTouch();

            IMatchLauncher launcher = ResolveLauncher();
            var roster = new List<RoomMemberSlot>(_members);
            if (launcher != null)
            {
                try { launcher.LaunchNetworkedMatch(_config, roster); }
                catch (Exception e) { Debug.LogError($"[RoomService] Match launcher threw: {e}"); }
            }
            else
            {
                Debug.LogWarning("[RoomService] No IMatchLauncher available (A4). Room marked Starting; " +
                                 "no robots spawned. This is expected when testing the lobby standalone.");
            }
        }

        // ---------------------------------------------------------------- member intent (server-applied)

        /// <inheritdoc/>
        [Server]
        public void RequestAlliance(int connectionId, RoomAlliance alliance)
        {
            int idx = RoomValidation.IndexOf(_members, connectionId);
            if (idx < 0) return;

            RoomMemberSlot m = _members[idx];
            if (m.role == MemberRole.Spectator && alliance != RoomAlliance.Unassigned)
                return; // spectators have no alliance; they must first become a Player via RequestRole

            if (!RoomValidation.CanUseAlliance(_members, connectionId, alliance, out string reason))
            {
                Debug.Log($"[RoomService] Alliance change rejected for {connectionId}: {reason}");
                return;
            }

            m.alliance = alliance;
            m.isReady = false; // changing alliance clears ready
            _members[idx] = m;
            ServerTouch();
        }

        /// <inheritdoc/>
        [Server]
        public void RequestRole(int connectionId, MemberRole role)
        {
            int idx = RoomValidation.IndexOf(_members, connectionId);
            if (idx < 0) return;

            RoomMemberSlot m = _members[idx];
            if (m.role == role) return;

            if (role == MemberRole.Player)
            {
                if (!RoomValidation.CanBecomePlayer(_members, connectionId, out string reason))
                {
                    Debug.Log($"[RoomService] Role change rejected for {connectionId}: {reason}");
                    return;
                }
                m.role = MemberRole.Player;
                m.alliance = RoomAlliance.Unassigned;
            }
            else // Spectator: pure observer, drop alliance/robot/ready
            {
                m.role = MemberRole.Spectator;
                m.alliance = RoomAlliance.Unassigned;
                m.isReady = false;
                m.robotIndex = 0;
            }

            m.isReady = role == MemberRole.Player ? false : m.isReady;
            _members[idx] = m;
            RefreshRoomCounts();
            ServerTouch();
        }

        /// <inheritdoc/>
        [Server]
        public void RequestReady(int connectionId, bool ready)
        {
            int idx = RoomValidation.IndexOf(_members, connectionId);
            if (idx < 0) return;

            RoomMemberSlot m = _members[idx];
            if (m.role != MemberRole.Player) return;                  // spectators can't ready
            if (ready && m.alliance == RoomAlliance.Unassigned) return; // must pick a side first
            if (m.isReady == ready) return;

            m.isReady = ready;
            _members[idx] = m;
            ServerTouch();
        }

        /// <inheritdoc/>
        [Server]
        public void RequestRobot(int connectionId, int robotIndex)
        {
            int idx = RoomValidation.IndexOf(_members, connectionId);
            if (idx < 0) return;

            RoomMemberSlot m = _members[idx];
            if (m.role != MemberRole.Player) return;
            if (robotIndex < 0) robotIndex = 0;
            if (m.robotIndex == robotIndex) return;

            m.robotIndex = robotIndex;
            _members[idx] = m;
            ServerTouch();
        }

        // ================================================================ CLIENT INTENT (commands) ===
        // requiresAuthority = false: RoomService is a server-owned singleton with no client authority,
        // so every client→host call uses the authenticated `sender` and IGNORES any client-supplied id.

        [Command(requiresAuthority = false)]
        private void CmdJoin(string displayName, NetworkConnectionToClient sender = null)
        {
            if (sender == null) return;
            TryAddMember(sender.connectionId, displayName, out _, out _);
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestAlliance(RoomAlliance alliance, NetworkConnectionToClient sender = null)
        {
            if (sender != null) RequestAlliance(sender.connectionId, alliance);
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestRole(MemberRole role, NetworkConnectionToClient sender = null)
        {
            if (sender != null) RequestRole(sender.connectionId, role);
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestReady(bool ready, NetworkConnectionToClient sender = null)
        {
            if (sender != null) RequestReady(sender.connectionId, ready);
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestRobot(int robotIndex, NetworkConnectionToClient sender = null)
        {
            if (sender != null) RequestRobot(sender.connectionId, robotIndex);
        }

        [Command(requiresAuthority = false)]
        private void CmdSetMatchConfig(NetworkMatchConfig config, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender.connectionId != _hostConnectionId) return; // host-only
            SetMatchConfig(config);
        }

        [Command(requiresAuthority = false)]
        private void CmdStartMatch(NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender.connectionId != _hostConnectionId) return; // host-only
            ServerStartMatch();
        }

        // ================================================================ LOCAL (UI) convenience ===
        // The lobby UI calls these for the LOCAL player. They apply directly on the host and route
        // through a Command on a client. Host-only actions no-op on non-host clients.

        public void JoinAsLocalMember(string displayName)
        {
            if (isServer) TryAddMember(LocalConnectionId, displayName, out _, out _);
            else CmdJoin(displayName);
        }

        public void LocalSetAlliance(RoomAlliance alliance)
        {
            if (isServer) RequestAlliance(LocalConnectionId, alliance);
            else CmdRequestAlliance(alliance);
        }

        public void LocalSetRole(MemberRole role)
        {
            if (isServer) RequestRole(LocalConnectionId, role);
            else CmdRequestRole(role);
        }

        public void LocalSetReady(bool ready)
        {
            if (isServer) RequestReady(LocalConnectionId, ready);
            else CmdRequestReady(ready);
        }

        public void LocalSetRobot(int robotIndex)
        {
            if (isServer) RequestRobot(LocalConnectionId, robotIndex);
            else CmdRequestRobot(robotIndex);
        }

        /// <summary>True on the local instance that owns the room (the host).</summary>
        public bool IsLocalHost => isServer;

        /// <summary>Host-only: change the match mode. No-op on clients.</summary>
        public void HostSetMatchConfig(NetworkMatchConfig config)
        {
            if (isServer) SetMatchConfig(config);
            else CmdSetMatchConfig(config); // ignored server-side unless caller is host
        }

        /// <summary>Host-only: start the match. No-op on clients.</summary>
        public void HostStartMatch()
        {
            if (isServer) ServerStartMatch();
            else CmdStartMatch();
        }

        // ================================================================ helpers ===

        [Server]
        private void ServerReconcileDisconnected()
        {
            if (Time.unscaledTime < _nextReconcileTime) return;
            _nextReconcileTime = Time.unscaledTime + 0.5f;

            for (int i = _members.Count - 1; i >= 0; i--)
            {
                int id = _members[i].connectionId;
                if (id == HostConnectionId) continue; // host always present
                if (!NetworkServer.connections.ContainsKey(id))
                {
                    _members.RemoveAt(i);
                    RefreshRoomCounts();
                    ServerTouch();
                }
            }
        }

        [Server]
        private void ServerTouch() => _revision++;

        [Server]
        private void RefreshRoomCounts()
        {
            RoomInfo r = _room;
            r.playerCount = RoomValidation.CountPlayers(_members);
            r.capacity = RoomValidation.MaxMembers;
            r.state = _state;
            _room = r;
        }

        [Server]
        private int NextFreeSlotIndex()
        {
            for (int slot = 0; slot < RoomValidation.MaxMembers; slot++)
            {
                bool used = false;
                for (int i = 0; i < _members.Count; i++)
                {
                    if (_members[i].slotIndex == slot) { used = true; break; }
                }
                if (!used) return slot;
            }
            return _members.Count;
        }

        private static RoomInfo DefaultRoomInfo() => new RoomInfo
        {
            roomId = "",
            name = "CloSim Room",
            hostName = "Host",
            address = "",
            port = 0,
            gameId = "",
            region = "",
            playerCount = 0,
            capacity = RoomValidation.MaxMembers,
            visibility = RoomVisibility.Public,
            requiresToken = false,
            state = RoomState.Lobby,
            version = ""
        };

        private static IMatchLauncher ResolveLauncher()
        {
            if (Launcher != null) return Launcher;

            // Late-bound discovery so A3 never references A4's concrete type.
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (MonoBehaviour b in behaviours)
            {
                if (b is IMatchLauncher launcher)
                {
                    Launcher = launcher;
                    return launcher;
                }
            }
            return null;
        }
    }
}
