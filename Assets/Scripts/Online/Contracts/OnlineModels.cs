// CloSim Online Multiplayer — shared data models / DTOs (contract stubs).
// SOURCE OF TRUTH: Documentation/online/architecture.md §5.
// Owned by A1 (Architect). Plain serializable data; keep dependency-free and JSON/Mirror friendly.

using System;

namespace Online.Contracts
{
    /// <summary>
    /// The count-based match shape that REPLACES rigid Core.PlayMode for online play.
    /// A3's adapter maps this to/from PlayMode; offline keeps using PlayMode unchanged.
    /// </summary>
    [Serializable]
    public struct NetworkMatchConfig
    {
        public int blueCount;       // 0..3
        public int redCount;        // 0..3
        public string gameId;       // "Rebuilt" | "Reefscape"
        public string sceneName;    // scene to load for the match
        public int humanPlayerType; // cast of Core.HumanPlayerType; int keeps this Core-independent
        public bool allowSpectators;

        public int TotalPlayers => blueCount + redCount;

        public bool IsValid =>
            blueCount is >= 0 and <= 3 &&
            redCount is >= 0 and <= 3 &&
            TotalPlayers is >= 1 and <= 6;
    }

    /// <summary>One occupied slot in a room (a connected member).</summary>
    [Serializable]
    public struct RoomMemberSlot
    {
        public int connectionId;   // Mirror connection id (server-assigned)
        public int slotIndex;      // 0..5 stable index within the room
        public string displayName;
        public RoomAlliance alliance;
        public MemberRole role;
        public bool isReady;
        public bool isHost;
        public int robotIndex;     // index into the game's robot catalog (network-safe key)
    }

    /// <summary>
    /// Public-facing room summary. Stored by the master server and shown by the in-game Server List.
    /// Must be JSON-serializable for the backend. NEVER carries the join token — only 'requiresToken'.
    /// </summary>
    [Serializable]
    public struct RoomInfo
    {
        public string roomId;
        public string name;
        public string hostName;
        public string address;         // public IP or hostname of the listen-server
        public int port;
        public string gameId;          // "Rebuilt" | "Reefscape"
        public string region;          // optional, free-form
        public int playerCount;        // current occupied player slots
        public int capacity;           // always 6 for now
        public RoomVisibility visibility;
        public bool requiresToken;     // true if a join token/password is set (token itself never sent)
        public RoomState state;
        public string version;         // client build/protocol version for compatibility filtering
    }

    /// <summary>Options for opening a listen-server. See IOnlineConnection.StartHost.</summary>
    [Serializable]
    public struct HostStartOptions
    {
        public int port;               // 0 => transport default
        public string joinToken;       // "" => no token gating
        public RoomVisibility visibility;
        public string roomName;
        public string gameId;
        public string version;
    }

    /// <summary>Target for a direct client connect. See IOnlineConnection.StartClient.</summary>
    [Serializable]
    public struct ConnectEndpoint
    {
        public string address;
        public int port;
        public string joinToken;       // "" if none
        public string version;
    }
}
