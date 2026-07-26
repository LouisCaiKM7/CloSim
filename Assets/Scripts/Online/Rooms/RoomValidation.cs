// CloSim Online Multiplayer — host-authoritative room rules (A3 Rooms & Modes).
// Namespace: Online.Rooms. Pure C#; NO Mirror / NO UnityEngine dependency so the caps logic is
// unit-testable and shared verbatim by BOTH the networked RoomService and the offline MockRoomService.
//
// The room is authoritative on the HOST. These are the rules the host enforces before mutating the
// replicated roster; clients only ever request. Never trust a client — every [Command] path runs
// these checks server-side.
//
//   * Capacity: max 6 members (connection cap; matches CloSimNetworkManager.maxConnections = 6).
//   * Players:  max 6, and max 3 per alliance.
//   * Spectators: pure observers (no robot, no alliance). They occupy a connection slot but are
//     excluded from the alliance cap — so they only "fill remaining capacity" left by players.

using System.Collections.Generic;
using Online.Contracts;

namespace Online.Rooms
{
    /// <summary>Stateless host-side validation for room capacity, alliance caps, and match-start gating.</summary>
    public static class RoomValidation
    {
        public const int MaxMembers = 6;       // total connections a room accepts
        public const int MaxPlayers = 6;       // players (non-spectators)
        public const int MaxPerAlliance = 3;   // players on a single alliance

        // ----------------------------------------------------------------- tallies

        public static int CountMembers(IReadOnlyList<RoomMemberSlot> members)
            => members?.Count ?? 0;

        public static int CountPlayers(IReadOnlyList<RoomMemberSlot> members)
        {
            if (members == null) return 0;
            int n = 0;
            for (int i = 0; i < members.Count; i++)
                if (members[i].role == MemberRole.Player) n++;
            return n;
        }

        public static int CountAlliance(IReadOnlyList<RoomMemberSlot> members, RoomAlliance alliance)
        {
            if (members == null) return 0;
            int n = 0;
            for (int i = 0; i < members.Count; i++)
                if (members[i].role == MemberRole.Player && members[i].alliance == alliance) n++;
            return n;
        }

        /// <summary>Index of the member with this connectionId, or -1.</summary>
        public static int IndexOf(IReadOnlyList<RoomMemberSlot> members, int connectionId)
        {
            if (members == null) return -1;
            for (int i = 0; i < members.Count; i++)
                if (members[i].connectionId == connectionId) return i;
            return -1;
        }

        // ----------------------------------------------------------------- join / role / alliance rules

        /// <summary>Can a brand-new connection be seated at all (connection capacity)?</summary>
        public static bool CanSeatNewMember(IReadOnlyList<RoomMemberSlot> members, out string reason)
        {
            if (CountMembers(members) >= MaxMembers)
            {
                reason = "Room is full.";
                return false;
            }
            reason = "";
            return true;
        }

        /// <summary>
        /// Can the member identified by <paramref name="connectionId"/> be a Player? Enforces the
        /// 6-player cap. Excludes the member itself so a member already counted as a player is not
        /// double-counted when merely re-confirming Player role.
        /// </summary>
        public static bool CanBecomePlayer(IReadOnlyList<RoomMemberSlot> members, int connectionId, out string reason)
        {
            int idx = IndexOf(members, connectionId);
            bool alreadyPlayer = idx >= 0 && members[idx].role == MemberRole.Player;

            int players = CountPlayers(members) - (alreadyPlayer ? 1 : 0);
            if (players >= MaxPlayers)
            {
                reason = "Player slots are full (max 6).";
                return false;
            }
            reason = "";
            return true;
        }

        /// <summary>
        /// Can the member move onto <paramref name="target"/> alliance? Unassigned is always allowed
        /// (it frees a slot). Blue/Red require the target alliance to have &lt; 3 players (excluding the
        /// mover) AND a free player slot overall.
        /// </summary>
        public static bool CanUseAlliance(
            IReadOnlyList<RoomMemberSlot> members, int connectionId, RoomAlliance target, out string reason)
        {
            if (target == RoomAlliance.Unassigned)
            {
                reason = "";
                return true;
            }

            if (!CanBecomePlayer(members, connectionId, out reason))
                return false;

            int idx = IndexOf(members, connectionId);
            bool alreadyOnTarget = idx >= 0
                                   && members[idx].role == MemberRole.Player
                                   && members[idx].alliance == target;

            int onTarget = CountAlliance(members, target) - (alreadyOnTarget ? 1 : 0);
            if (onTarget >= MaxPerAlliance)
            {
                reason = $"{target} alliance is full (max {MaxPerAlliance}).";
                return false;
            }

            reason = "";
            return true;
        }

        // ----------------------------------------------------------------- match-start gating

        /// <summary>
        /// True when the host may start: the config shape is valid, the roster fills it EXACTLY
        /// (blue players == blueCount, red players == redCount, no players left Unassigned), and every
        /// player is ready. Spectators are ignored entirely.
        /// </summary>
        public static bool CanStart(
            IReadOnlyList<RoomMemberSlot> members, NetworkMatchConfig config, out string reason)
        {
            if (!config.IsValid)
            {
                reason = "Selected mode is not a valid match shape.";
                return false;
            }

            int blue = CountAlliance(members, RoomAlliance.Blue);
            int red = CountAlliance(members, RoomAlliance.Red);
            int unassigned = CountUnassignedPlayers(members);

            if (unassigned > 0)
            {
                reason = "Everyone must pick an alliance.";
                return false;
            }

            if (blue != config.blueCount || red != config.redCount)
            {
                reason = $"Need {config.blueCount} blue and {config.redCount} red " +
                         $"(have {blue} blue, {red} red).";
                return false;
            }

            if (blue + red < 1)
            {
                reason = "Need at least one player.";
                return false;
            }

            if (!AllPlayersReady(members))
            {
                reason = "All players must be ready.";
                return false;
            }

            reason = "";
            return true;
        }

        public static int CountUnassignedPlayers(IReadOnlyList<RoomMemberSlot> members)
        {
            if (members == null) return 0;
            int n = 0;
            for (int i = 0; i < members.Count; i++)
                if (members[i].role == MemberRole.Player && members[i].alliance == RoomAlliance.Unassigned) n++;
            return n;
        }

        public static bool AllPlayersReady(IReadOnlyList<RoomMemberSlot> members)
        {
            if (members == null) return false;
            bool anyPlayer = false;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].role != MemberRole.Player) continue;
                anyPlayer = true;
                if (!members[i].isReady) return false;
            }
            return anyPlayer;
        }

        // ----------------------------------------------------------------- supported shapes (for UI)

        /// <summary>
        /// Every match shape the room supports: blue,red in [0..3], total in [1..6]. Ordered by total
        /// then blue. The lobby mode selector lists these; the host picks one.
        /// </summary>
        public static IReadOnlyList<(int blue, int red)> SupportedShapes()
        {
            var shapes = new List<(int blue, int red)>();
            for (int total = 1; total <= MaxPlayers; total++)
            {
                for (int blue = 0; blue <= MaxPerAlliance; blue++)
                {
                    int red = total - blue;
                    if (red < 0 || red > MaxPerAlliance) continue;
                    shapes.Add((blue, red));
                }
            }
            return shapes;
        }
    }
}
