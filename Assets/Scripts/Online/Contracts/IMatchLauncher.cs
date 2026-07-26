// CloSim Online Multiplayer — room -> match spawn seam (stub).
// SOURCE OF TRUTH: Documentation/online/architecture.md §6.4.
// Owned by A1 (Architect). IMPLEMENTED BY A4 (feat/gameplay-sync).
// Keeps A3 ignorant of LoadMatch internals and A4 ignorant of room UI.

using System;
using System.Collections.Generic;

namespace Online.Contracts
{
    public interface IMatchLauncher
    {
        /// <summary>
        /// Called host-side by IRoomService.ServerStartMatch(). Translates the room roster + config
        /// into an N-slot networked match: loads the scene, spawns N server-authoritative robots,
        /// assigns per-client ownership, and sets up single-view cameras for online play.
        /// Must NOT change any game rule, physics, scoring math, or game-piece behavior.
        /// </summary>
        void LaunchNetworkedMatch(NetworkMatchConfig config, IReadOnlyList<RoomMemberSlot> roster);

        bool IsMatchActive { get; }
        event Action OnMatchSpawned; // all robots spawned + owned
    }
}
