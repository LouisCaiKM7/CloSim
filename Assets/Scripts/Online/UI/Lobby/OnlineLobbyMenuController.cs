// CloSim Online Multiplayer — lobby entry controller (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. Assembles the whole in-game lobby as a self-contained native-Unity
// overlay canvas: it builds the LobbyNavigator + every screen (Home/Create/Join/Room/ServerList) in
// code, so opening the lobby needs NO scene/prefab wiring — the main menu just calls OpenLobby().
//
// This mirrors MainMenuController's root-swap navigation, but the lobby lives on its OWN overlay canvas
// (sortingOrder 100) drawn on top of the main menu; closing it simply hides the overlay and reveals the
// menu again (and invokes the onClosed callback so the menu can restore focus).

using UnityEngine;

namespace Online.UI.Lobby
{
    [AddComponentMenu("CloSim/Rooms/Online Lobby Menu Controller")]
    public class OnlineLobbyMenuController : MonoBehaviour
    {
        private static OnlineLobbyMenuController _instance;

        private Canvas _canvas;
        private LobbyNavigator _navigator;
        private bool _built;
        private System.Action _onClosed;

        /// <summary>
        /// Opens (building on first use) the online lobby overlay. <paramref name="onClosed"/> fires when
        /// the user backs out of the lobby's home screen — the caller (main menu) restores itself there.
        /// </summary>
        public static void OpenLobby(System.Action onClosed = null)
        {
            if (_instance == null)
            {
                var go = new GameObject(nameof(OnlineLobbyMenuController));
                _instance = go.AddComponent<OnlineLobbyMenuController>();
            }

            _instance._onClosed = onClosed;
            _instance.Open();
        }

        private void Open()
        {
            Online.Rooms.LobbyServices.EnsureExists();
            if (!_built)
                Build();

            _canvas.gameObject.SetActive(true);
            _navigator.ShowRoot(LobbyScreenKeys.Home);
        }

        private void Build()
        {
            _canvas = LobbyUiKit.CreateOverlayCanvas("OnlineLobbyCanvas");
            _navigator = _canvas.gameObject.AddComponent<LobbyNavigator>();
            _navigator.OnExitRequested += CloseLobby;

            CreateScreen<LobbyHomeScreen>("HomeScreen");
            CreateScreen<CreateRoomScreen>("CreateRoomScreen");
            CreateScreen<JoinScreen>("JoinScreen");
            CreateScreen<RoomScreen>("RoomScreen");
            CreateScreen<ServerListScreen>("ServerListScreen");

            _built = true;
        }

        private void CreateScreen<T>(string objectName) where T : LobbyScreen
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            LobbyUiKit.Stretch((RectTransform)go.transform);

            var screen = go.AddComponent<T>();
            screen.Initialize(_navigator);   // builds the screen's UI once
            _navigator.Register(screen);     // registers by Key and deactivates it
        }

        private void CloseLobby()
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(false);

            _onClosed?.Invoke();
        }

        private void OnDestroy()
        {
            if (_navigator != null)
                _navigator.OnExitRequested -= CloseLobby;
            if (_instance == this)
                _instance = null;
        }
    }
}
