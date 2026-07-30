// CloSim Online Multiplayer — persistent-room UI restore. Namespace: Online.UI.Lobby.
//
// When a networked match returns to the lobby scene (RoomService.ServerReturnToLobby -> ServerChangeScene),
// the lobby scene reloads FRESH and would show the main menu — hiding the still-alive room. This listener
// detects that case (back on the lobby scene while still connected with a live RoomService) and re-opens the
// lobby overlay straight to the ROOM screen, so the room is visible and reusable for another match.
//
// Purely additive and online-only: no-ops offline and whenever not connected. The FIRST lobby load (from
// Splash) happens before any connection, so this never interferes with normal main-menu entry.

using Mirror;
using Online.Rooms;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Online.UI.Lobby
{
    public static class RoomReturnUiBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject(nameof(RoomReturnUiBootstrap));
            Object.DontDestroyOnLoad(go);
            go.AddComponent<RoomReturnUiListener>();
        }
    }

    /// <summary>Persistent listener: shows the Room screen when we re-enter the lobby scene mid-session.</summary>
    public sealed class RoomReturnUiListener : MonoBehaviour
    {
        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != RoomService.LobbySceneName)
                return;

            // Only when a networked room session is still live (returning from a match), not on first entry.
            if (!NetworkClient.active && !NetworkServer.active)
                return;
            if (RoomService.Instance == null)
                return;

            OnlineLobbyMenuController.OpenLobbyToRoom();
        }
    }
}
