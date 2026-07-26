// CloSim Online Multiplayer — base class + keys for lobby screens (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. A screen owns a full-stretch RectTransform and builds its own native UI
// in BuildUi(). Screens navigate to one another by STRING KEY (LobbyScreenKeys) so they stay decoupled.

using Online.Rooms;
using UnityEngine;

namespace Online.UI.Lobby
{
    /// <summary>Well-known screen keys used with <see cref="LobbyNavigator"/>. Keeps screens decoupled.</summary>
    public static class LobbyScreenKeys
    {
        public const string Home = "home";
        public const string Create = "create";
        public const string Join = "join";
        public const string Room = "room";
        public const string ServerList = "serverlist";
    }

    /// <summary>
    /// Base class for every lobby screen. The entry controller instantiates each screen on a stretched
    /// child of the lobby canvas, calls <see cref="Initialize"/>, and registers it with the navigator.
    /// </summary>
    public abstract class LobbyScreen : MonoBehaviour
    {
        protected LobbyNavigator Navigator { get; private set; }
        protected LobbyServices Services => LobbyServices.EnsureExists();

        /// <summary>Room facade (networked RoomService or offline mock). May be null before create/join.</summary>
        protected ILobbyRoom Room => Services.Room;

        /// <summary>The control focused first when this screen appears (for gamepad nav). Set in BuildUi.</summary>
        public GameObject FirstSelected { get; protected set; }

        /// <summary>Unique key for this screen (see <see cref="LobbyScreenKeys"/>).</summary>
        public abstract string Key { get; }

        private bool _built;

        public void Initialize(LobbyNavigator navigator)
        {
            Navigator = navigator;
            EnsureRoot();
            if (!_built)
            {
                BuildUi();
                _built = true;
            }
        }

        /// <summary>Ensures this screen sits on a full-stretch RectTransform under its parent canvas.</summary>
        private void EnsureRoot()
        {
            var rt = transform as RectTransform;
            if (rt != null) LobbyUiKit.Stretch(rt);
        }

        /// <summary>Build the native UI hierarchy under this transform. Called once.</summary>
        protected abstract void BuildUi();

        /// <summary>Called each time the screen becomes visible — refresh dynamic content here.</summary>
        public virtual void OnShow() { }

        /// <summary>Called when the screen is hidden.</summary>
        public virtual void OnHide() { }

        // Convenience navigation for subclasses.
        protected void Go(string key) => Navigator?.Show(key);
        protected void Back() => Navigator?.Back();
    }
}
