// CloSim Online Multiplayer — selectable game/scene list for the lobby (A3 Rooms & Modes).
// Namespace: Online.Rooms. Plain data mapping a display gameId to the scene the match loads.
// This is ADDITIVE lobby metadata that merely references EXISTING scenes — it changes no game content.
// The Create Room screen exposes this list; the host's pick becomes RoomInfo.gameId + NetworkMatchConfig.sceneName.

using System.Collections.Generic;

namespace Online.Rooms
{
    /// <summary>One selectable FRC game in the lobby: a display/id string and the scene it loads.</summary>
    [System.Serializable]
    public struct LobbyGame
    {
        public string displayName; // shown in UI
        public string gameId;      // stored on RoomInfo.gameId ("Rebuilt" | "Reefscape" | ...)
        public string sceneName;   // scene loaded for the match

        public LobbyGame(string displayName, string gameId, string sceneName)
        {
            this.displayName = displayName;
            this.gameId = gameId;
            this.sceneName = sceneName;
        }
    }

    /// <summary>Default set of games, keyed to the existing CloSim scenes.</summary>
    public static class LobbyGameCatalog
    {
        /// <summary>
        /// The built-in games. Names map to existing scenes under Assets/Scenes/. If the project's
        /// scene names change, adjust here (or drive the Create Room screen from a serialized list).
        /// </summary>
        public static readonly IReadOnlyList<LobbyGame> Default = new List<LobbyGame>
        {
            new LobbyGame("Rebuilt",   "Rebuilt",   "Game_Rebuilt"),
            new LobbyGame("Reefscape", "Reefscape", "Game_Reefscape"),
            new LobbyGame("Unhinged",  "Unhinged",  "Game_Unhinged"),
        };

        public static LobbyGame ByGameId(string gameId)
        {
            foreach (LobbyGame g in Default)
                if (g.gameId == gameId) return g;
            return Default.Count > 0 ? Default[0] : new LobbyGame("Rebuilt", "Rebuilt", "Game_Rebuilt");
        }
    }
}
