// CloSim Online Multiplayer — Replays entry controller. Namespace: Online.Replay.UI.
// Mirrors Online.UI.Lobby.OnlineLobbyMenuController: assembles the whole in-game Replays overlay as a
// self-contained native-Unity canvas built entirely in code, so opening it needs no scene/prefab wiring
// — the main menu just calls OpenReplays(). See MainMenuController's multiplayerButton wiring for the
// exact same minimal-diff pattern this file's caller follows for the "Replays" button.
//
// Unlike the lobby overlay, this canvas is marked DontDestroyOnLoad: playing a replay loads the
// recorded match's field scene (UI.Transitions.SceneTransitionManager) and the playback transport
// controls (ReplayPlaybackScreen) must survive that scene swap and the return trip back to the menu.

using UnityEngine;

namespace Online.Replay.UI
{
    [AddComponentMenu("CloSim/Replay/Replays Menu Controller")]
    public class ReplaysMenuController : MonoBehaviour
    {
        private static ReplaysMenuController _instance;

        private Canvas _canvas;
        private Online.UI.Lobby.LobbyNavigator _navigator;
        private bool _built;
        private System.Action _onClosed;

        /// <summary>
        /// Opens (building on first use) the Replays overlay. <paramref name="onClosed"/> fires when the
        /// user backs out of the Replays list screen — the caller (main menu) restores itself there.
        /// </summary>
        public static void OpenReplays(System.Action onClosed = null)
        {
            if (_instance == null)
            {
                var go = new GameObject(nameof(ReplaysMenuController));
                _instance = go.AddComponent<ReplaysMenuController>();
            }

            _instance._onClosed = onClosed;
            _instance.Open();
        }

        private void Open()
        {
            if (!_built)
                Build();

            _canvas.gameObject.SetActive(true);
            _navigator.ShowRoot(ReplayScreenKeys.List);
        }

        private void Build()
        {
            _canvas = Online.UI.Lobby.LobbyUiKit.CreateOverlayCanvas("ReplaysCanvas");
            DontDestroyOnLoad(_canvas.gameObject); // survives the field-scene load/unload during playback
            DontDestroyOnLoad(gameObject);

            _navigator = _canvas.gameObject.AddComponent<Online.UI.Lobby.LobbyNavigator>();
            _navigator.OnExitRequested += CloseReplays;

            CreateScreen<ReplaysScreen>("ReplaysScreen");
            CreateScreen<ReplayPlaybackScreen>("ReplayPlaybackScreen");

            _built = true;
        }

        private void CreateScreen<T>(string objectName) where T : Online.UI.Lobby.LobbyScreen
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            Online.UI.Lobby.LobbyUiKit.Stretch((RectTransform)go.transform);

            var screen = go.AddComponent<T>();
            screen.Initialize(_navigator);   // builds the screen's UI once
            _navigator.Register(screen);     // registers by Key and deactivates it
        }

        private void CloseReplays()
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(false);

            _onClosed?.Invoke();
        }

        private void OnDestroy()
        {
            if (_navigator != null)
                _navigator.OnExitRequested -= CloseReplays;
            if (_instance == this)
                _instance = null;
        }
    }
}
