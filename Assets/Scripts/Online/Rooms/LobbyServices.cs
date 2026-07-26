// CloSim Online Multiplayer — lobby service locator (A3 Rooms & Modes).
// Namespace: Online.Rooms. The single seam through which the lobby UI reaches the netcode layer:
//   * Connection  — A2's IOnlineConnection (host/join/LAN), created on demand.
//   * Master      — A2's IMasterServerClient (Server List browse), BLANK config by default.
//   * Room        — the live ILobbyRoom (networked RoomService.Instance, or an injected offline mock).
//
// It also carries the "pending room seed" the host fills on Create Room and that RoomNetworkBootstrap
// consumes the moment the listen-server comes up (to seed the replicated RoomInfo / NetworkMatchConfig).
//
// Additive only. It NEVER touches Mirror directly — everything goes through the A2 façade.

using System;
using Online.Contracts;
using Online.Net;
using Online.Net.MasterClient;
using UnityEngine;

namespace Online.Rooms
{
    /// <summary>Seed data a host supplies on Create Room, applied to the RoomService once hosting starts.</summary>
    public struct RoomSeed
    {
        public RoomInfo info;
        public NetworkMatchConfig config;
        public string hostDisplayName;
        public bool valid;
    }

    [AddComponentMenu("CloSim/Rooms/Lobby Services")]
    [DisallowMultipleComponent]
    public class LobbyServices : MonoBehaviour
    {
        private static LobbyServices _instance;

        public static LobbyServices Instance => _instance;

        /// <summary>Gets (or lazily creates) the persistent LobbyServices singleton.</summary>
        public static LobbyServices EnsureExists()
        {
            if (_instance != null) return _instance;
            var go = new GameObject(nameof(LobbyServices));
            _instance = go.AddComponent<LobbyServices>();
            return _instance;
        }

        [Tooltip("Display name presented to other players. Editable from the lobby.")]
        [SerializeField] private string _localPlayerName = "Player";

        private IOnlineConnection _connection;
        private IMasterServerClient _master;
        private ILobbyRoom _roomOverride; // set to a MockRoomService for offline UI iteration

        private RoomSeed _pendingSeed;

        public string LocalPlayerName
        {
            get => string.IsNullOrWhiteSpace(_localPlayerName) ? "Player" : _localPlayerName;
            set => _localPlayerName = value;
        }

        /// <summary>A2 connection façade. Lazily attaches an OnlineConnection component to this object.</summary>
        public IOnlineConnection Connection
        {
            get
            {
                if (_connection == null)
                    _connection = GetComponent<OnlineConnection>() ?? gameObject.AddComponent<OnlineConnection>();
                return _connection;
            }
        }

        /// <summary>A2 master-server HTTP client (blank config → IsConfigured false, no calls made).</summary>
        public IMasterServerClient Master => _master ??= new MasterServerClient();

        /// <summary>
        /// The active room. Prefers a manually injected mock (offline UI testing); otherwise the live
        /// networked <see cref="RoomService.Instance"/>. May be null before a room is created/joined.
        /// </summary>
        public ILobbyRoom Room => _roomOverride ?? RoomService.Instance;

        /// <summary>Injects an offline mock (or clears it with null) for editor UI iteration.</summary>
        public void SetRoomOverride(ILobbyRoom room) => _roomOverride = room;

        public bool HasPendingSeed => _pendingSeed.valid;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---------------------------------------------------------------- host / join flows

        /// <summary>
        /// Opens a listen-server for a new room. Stores the seed so RoomNetworkBootstrap can populate the
        /// replicated room once <see cref="IOnlineConnection.OnHostStarted"/> fires.
        /// </summary>
        public void HostRoom(RoomInfo info, NetworkMatchConfig config, string joinToken)
        {
            _pendingSeed = new RoomSeed
            {
                info = info,
                config = config,
                hostDisplayName = LocalPlayerName,
                valid = true
            };

            Connection.StartHost(new HostStartOptions
            {
                port = info.port,
                joinToken = joinToken ?? "",
                visibility = info.visibility,
                roomName = info.name,
                gameId = info.gameId,
                version = info.version
            });
        }

        /// <summary>Direct-connect to a host by address/port (+ optional token).</summary>
        public void JoinDirect(string address, int port, string joinToken)
        {
            Connection.StartClient(new ConnectEndpoint
            {
                address = address,
                port = port,
                joinToken = joinToken ?? "",
                version = ""
            });
        }

        /// <summary>Join a listed/discovered room via its RoomInfo (used by the Server List).</summary>
        public void JoinRoom(RoomInfo room, string joinToken) => JoinDirect(room.address, room.port, joinToken);

        /// <summary>Consumed once by RoomNetworkBootstrap right after the host starts.</summary>
        public RoomSeed ConsumePendingSeed()
        {
            RoomSeed seed = _pendingSeed;
            _pendingSeed = default;
            return seed;
        }

        public void Disconnect()
        {
            if (Connection.IsHost) Connection.StopHost();
            else if (Connection.IsClient) Connection.StopClient();
        }
    }
}
