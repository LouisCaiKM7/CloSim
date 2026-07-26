// CloSim Online Multiplayer — master-server directory client contract (stub).
// SOURCE OF TRUTH: Documentation/online/architecture.md §6.3.
// Owned by A1 (Architect). HTTP CLIENT IMPLEMENTED BY A2; BACKEND BY A5.
//
// HARD CONSTRAINTS:
//  * The ONLY caller of this is the game client (CloSim.exe). The master server has NO web UI.
//  * The directory API SHOULD be gated (client API key / signed request) — not casually browsable.
//  * CONFIG IS BLANK. MasterServerUrl defaults to "" — the user provides it later.
//    While blank, IsConfigured == false and no network calls are made.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Online.Contracts
{
    public interface IMasterServerClient
    {
        // Host side (public rooms only):
        Task<RegisterResult> RegisterAsync(RoomInfo room);
        Task HeartbeatAsync(string roomId);
        Task DeregisterAsync(string roomId);

        // Client side (in-game Server List):
        Task<IReadOnlyList<RoomInfo>> ListRoomsAsync(RoomQuery query);

        bool IsConfigured { get; } // false while MasterServerConfig.MasterServerUrl == ""
    }

    public struct RegisterResult
    {
        public bool ok;
        public string roomId;
        public string error;
    }

    public struct RoomQuery
    {
        public string gameId;    // "" => all
        public string region;    // "" => all
        public bool hideFull;
        public bool hidePrivate; // default true — private rooms are never listed anyway
        public string version;   // "" => all
    }

    /// <summary>
    /// Config holder. Concrete values are supplied by the user later — NEVER hardcode a real endpoint/key.
    /// </summary>
    public static class MasterServerConfig
    {
        public const string MasterServerUrl = ""; // TODO: user provides hosting endpoint (AWS)
        public const string ClientApiKey = "";    // TODO: user provides client credential (gates the directory API)
        public const int HeartbeatSeconds = 15;
        public const int RoomTtlSeconds = 45;      // master server drops rooms after ~3 missed heartbeats
    }
}
