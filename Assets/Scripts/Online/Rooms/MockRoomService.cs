// CloSim Online Multiplayer — offline in-memory room (A3 Rooms & Modes).
// Namespace: Online.Rooms. A NON-networked ILobbyRoom used for editor UI iteration and automated
// UI tests: no Mirror, no connection, all mutations apply immediately and raise events synchronously.
// It reuses the SAME RoomValidation rules as the networked RoomService, so cap behaviour matches.
//
// The local player is always the "host" (connectionId 0). You can inject fake members via
// AddFakeMember(...) to exercise the roster UI (alliance chips, ready states, host crown, caps).

using System;
using System.Collections.Generic;
using Online.Contracts;
using UnityEngine;

namespace Online.Rooms
{
    [AddComponentMenu("CloSim/Rooms/Mock Room Service (offline)")]
    public class MockRoomService : MonoBehaviour, ILobbyRoom
    {
        private const int LocalId = 0;

        private RoomInfo _room = new RoomInfo
        {
            roomId = "mock",
            name = "Practice Room",
            hostName = "You",
            address = "127.0.0.1",
            port = 7777,
            gameId = "Rebuilt",
            region = "",
            playerCount = 0,
            capacity = RoomValidation.MaxMembers,
            visibility = RoomVisibility.Private,
            requiresToken = false,
            state = RoomState.Lobby,
            version = ""
        };

        private NetworkMatchConfig _config = new NetworkMatchConfig
        {
            blueCount = 1, redCount = 1, gameId = "Rebuilt", sceneName = "Game_Rebuilt",
            humanPlayerType = 0, allowSpectators = true
        };

        private RoomState _state = RoomState.Lobby;
        private readonly List<RoomMemberSlot> _members = new();
        private int _nextFakeId = 1;

        public RoomInfo CurrentRoom => _room;
        public IReadOnlyList<RoomMemberSlot> Members => _members;
        public NetworkMatchConfig MatchConfig => _config;
        public RoomState State => _state;

        public event Action<RoomInfo> OnRoomChanged;
        public event Action<IReadOnlyList<RoomMemberSlot>> OnMembersChanged;
        public event Action<NetworkMatchConfig> OnMatchConfigChanged;
        public event Action OnMatchStarting;

        public int LocalConnectionId => LocalId;
        public bool IsLocalHost => true;

        private void Awake()
        {
            SeatHostIfMissing();
        }

        public bool TryGetLocalMember(out RoomMemberSlot slot)
        {
            int idx = RoomValidation.IndexOf(_members, LocalId);
            if (idx >= 0) { slot = _members[idx]; return true; }
            slot = default;
            return false;
        }

        private void SeatHostIfMissing()
        {
            if (RoomValidation.IndexOf(_members, LocalId) >= 0) return;
            _members.Add(new RoomMemberSlot
            {
                connectionId = LocalId, slotIndex = 0, displayName = "You",
                alliance = RoomAlliance.Blue, role = MemberRole.Player,
                isReady = false, isHost = true, robotIndex = 0
            });
            RaiseAll();
        }

        // --- IRoomService host authority ---

        public bool TryAddMember(int connectionId, string displayName, out RoomMemberSlot slot, out string rejectReason)
        {
            if (RoomValidation.IndexOf(_members, connectionId) >= 0)
            {
                slot = _members[RoomValidation.IndexOf(_members, connectionId)];
                rejectReason = "";
                return true;
            }
            if (!RoomValidation.CanSeatNewMember(_members, out rejectReason)) { slot = default; return false; }

            slot = new RoomMemberSlot
            {
                connectionId = connectionId, slotIndex = _members.Count, displayName = displayName,
                alliance = RoomAlliance.Unassigned, role = MemberRole.Player,
                isReady = false, isHost = false, robotIndex = 0
            };
            _members.Add(slot);
            RefreshCounts();
            RaiseAll();
            return true;
        }

        public void RemoveMember(int connectionId)
        {
            int idx = RoomValidation.IndexOf(_members, connectionId);
            if (idx < 0) return;
            _members.RemoveAt(idx);
            RefreshCounts();
            RaiseAll();
        }

        public void SetMatchConfig(NetworkMatchConfig config)
        {
            _config = config;
            RefreshCounts();
            OnMatchConfigChanged?.Invoke(_config);
            OnRoomChanged?.Invoke(_room);
        }

        public bool CanStartMatch(out string reason) => RoomValidation.CanStart(_members, _config, out reason);

        public void ServerStartMatch()
        {
            if (!CanStartMatch(out _)) return;
            _state = RoomState.Starting;
            var r = _room; r.state = RoomState.Starting; _room = r;
            OnRoomChanged?.Invoke(_room);
            OnMatchStarting?.Invoke();
        }

        public void RequestAlliance(int connectionId, RoomAlliance alliance)
        {
            int idx = RoomValidation.IndexOf(_members, connectionId);
            if (idx < 0) return;
            if (!RoomValidation.CanUseAlliance(_members, connectionId, alliance, out _)) return;
            var m = _members[idx];
            m.alliance = alliance;
            m.isReady = false;
            _members[idx] = m;
            RaiseAll();
        }

        public void RequestRole(int connectionId, MemberRole role)
        {
            int idx = RoomValidation.IndexOf(_members, connectionId);
            if (idx < 0) return;
            var m = _members[idx];
            if (role == MemberRole.Player)
            {
                if (!RoomValidation.CanBecomePlayer(_members, connectionId, out _)) return;
                m.role = MemberRole.Player;
                m.alliance = RoomAlliance.Unassigned;
                m.isReady = false;
            }
            else
            {
                m.role = MemberRole.Spectator;
                m.alliance = RoomAlliance.Unassigned;
                m.isReady = false;
                m.robotIndex = 0;
            }
            _members[idx] = m;
            RefreshCounts();
            RaiseAll();
        }

        public void RequestReady(int connectionId, bool ready)
        {
            int idx = RoomValidation.IndexOf(_members, connectionId);
            if (idx < 0) return;
            var m = _members[idx];
            if (m.role != MemberRole.Player) return;
            if (ready && m.alliance == RoomAlliance.Unassigned) return;
            m.isReady = ready;
            _members[idx] = m;
            RaiseAll();
        }

        public void RequestRobot(int connectionId, int robotIndex)
        {
            int idx = RoomValidation.IndexOf(_members, connectionId);
            if (idx < 0) return;
            var m = _members[idx];
            if (m.role != MemberRole.Player) return;
            m.robotIndex = Mathf.Max(0, robotIndex);
            _members[idx] = m;
            RaiseAll();
        }

        // --- ILobbyRoom local convenience ---

        public void JoinAsLocalMember(string displayName) => SeatHostIfMissing();
        public void LocalSetAlliance(RoomAlliance alliance) => RequestAlliance(LocalId, alliance);
        public void LocalSetRole(MemberRole role) => RequestRole(LocalId, role);
        public void LocalSetReady(bool ready) => RequestReady(LocalId, ready);
        public void LocalSetRobot(int robotIndex) => RequestRobot(LocalId, robotIndex);
        public void HostSetMatchConfig(NetworkMatchConfig config) => SetMatchConfig(config);
        public void HostStartMatch() => ServerStartMatch();

        // --- test helpers ---

        /// <summary>Seats a fake remote member so the roster UI can be exercised offline.</summary>
        public RoomMemberSlot AddFakeMember(string name, RoomAlliance alliance = RoomAlliance.Unassigned,
            bool ready = false, MemberRole role = MemberRole.Player)
        {
            int id = _nextFakeId++;
            TryAddMember(id, name, out RoomMemberSlot slot, out _);
            if (role == MemberRole.Spectator) RequestRole(id, MemberRole.Spectator);
            else if (alliance != RoomAlliance.Unassigned) RequestAlliance(id, alliance);
            if (ready) RequestReady(id, true);
            RoomValidation.IndexOf(_members, id);
            return slot;
        }

        private void RefreshCounts()
        {
            var r = _room;
            r.playerCount = RoomValidation.CountPlayers(_members);
            r.capacity = RoomValidation.MaxMembers;
            r.state = _state;
            _room = r;
        }

        private void RaiseAll()
        {
            OnMembersChanged?.Invoke(_members);
            OnRoomChanged?.Invoke(_room);
        }
    }
}
