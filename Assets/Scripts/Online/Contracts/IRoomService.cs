// CloSim Online Multiplayer — room/lobby contract (stub).
// SOURCE OF TRUTH: Documentation/online/architecture.md §6.2.
// Owned by A1 (Architect). IMPLEMENTED BY A3 (feat/rooms-lobby), host-authoritative.
// Enforces 6-cap and <=3 per alliance. Clients send intent; the host validates and applies.

using System;
using System.Collections.Generic;

namespace Online.Contracts
{
    public interface IRoomService
    {
        RoomInfo CurrentRoom { get; }
        IReadOnlyList<RoomMemberSlot> Members { get; }
        NetworkMatchConfig MatchConfig { get; }
        RoomState State { get; }

        // --- Host authority ---
        bool TryAddMember(int connectionId, string displayName, out RoomMemberSlot slot, out string rejectReason);
        void RemoveMember(int connectionId);
        void SetMatchConfig(NetworkMatchConfig config);   // host picks mode (blueCount, redCount, ...)
        bool CanStartMatch(out string reason);            // all players ready + valid config
        void ServerStartMatch();                          // hands off to A4 via IMatchLauncher

        // --- Member intent (client -> host commands; host validates) ---
        void RequestAlliance(int connectionId, RoomAlliance alliance); // rejected if that alliance already has 3
        void RequestRole(int connectionId, MemberRole role);
        void RequestReady(int connectionId, bool ready);
        void RequestRobot(int connectionId, int robotIndex);

        // --- Events ---
        event Action<RoomInfo> OnRoomChanged;
        event Action<IReadOnlyList<RoomMemberSlot>> OnMembersChanged;
        event Action<NetworkMatchConfig> OnMatchConfigChanged;
        event Action OnMatchStarting;
    }
}
