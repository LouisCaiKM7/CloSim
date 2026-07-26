// CloSim Online Multiplayer — UI-facing room facade (A3 Rooms & Modes).
// Namespace: Online.Rooms. Extends the A1 contract IRoomService with the LOCAL-player convenience the
// lobby UI needs, so screens bind to one interface and work against both the networked RoomService
// and the offline MockRoomService (used for editor UI iteration without a live connection).

using Online.Contracts;

namespace Online.Rooms
{
    /// <summary>
    /// Everything the lobby UI needs from a room: the replicated <see cref="IRoomService"/> state plus
    /// intent helpers scoped to the LOCAL player. On the networked implementation these route through
    /// host-validated commands; host-only actions no-op on non-host clients.
    /// </summary>
    public interface ILobbyRoom : IRoomService
    {
        /// <summary>Local player's connectionId (0 host, client id otherwise, -1 offline mock/none).</summary>
        int LocalConnectionId { get; }

        /// <summary>True on the instance that owns the room (the host / the mock).</summary>
        bool IsLocalHost { get; }

        /// <summary>The local member's slot, if seated.</summary>
        bool TryGetLocalMember(out RoomMemberSlot slot);

        void JoinAsLocalMember(string displayName);
        void LocalSetAlliance(RoomAlliance alliance);
        void LocalSetRole(MemberRole role);
        void LocalSetReady(bool ready);
        void LocalSetRobot(int robotIndex);

        /// <summary>Host-only: change the match mode. No-op for non-hosts.</summary>
        void HostSetMatchConfig(NetworkMatchConfig config);

        /// <summary>Host-only: start the match. No-op for non-hosts.</summary>
        void HostStartMatch();
    }
}
