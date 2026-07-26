// CloSim Online Multiplayer — IOnlineConnection façade (CORE, A2-owned).
// Namespace: Online.Net. File under Assets/Scripts/Online/Net/Core/.
//
// The single seam through which Rooms (A3), Gameplay (A4) and the Server List (A5) start/stop and
// observe connections. It wraps CloSimNetworkManager and never leaks Mirror types to callers.
//
// SWARM INTEGRATION SEAMS (helper classes on sibling branches; merged into feat/netcode-foundation later):
//   * `CloSimNetworkAuthenticator` — assigned to the manager's `authenticator`; it reads the handshake
//     state we set on CloSimNetworkManager (ExpectedJoinToken/ExpectedVersion/PendingJoinToken/
//     PendingClientVersion) and calls manager.ReportClientConnectResult(...) for reject reasons.
//   * `CloSimNetworkDiscovery` — LAN advertise/find. Assumed API (verify at merge):
//         void AdvertiseServer(Online.Contracts.RoomInfo room);
//         void StopAdvertising();
//         void StartDiscovery();          // client search
//         void StopDiscovery();
//         event System.Action<Online.Contracts.RoomInfo> OnServerFound;
//   * `DirectConnect` — direct-IP client connect. Assumed API (verify at merge):
//         static void Join(string ip, ushort port);   // sets networkAddress + port, calls StartClient()

using System;
using System.Net;
using System.Net.Sockets;
using Mirror;
using Online.Contracts;
using UnityEngine;

namespace Online.Net
{
    /// <summary>Concrete <see cref="IOnlineConnection"/> over the CloSim Mirror stack.</summary>
    [AddComponentMenu("CloSim/Netcode/Online Connection")]
    public class OnlineConnection : MonoBehaviour, IOnlineConnection
    {
        private CloSimNetworkManager _manager;
        private CloSimNetworkDiscovery _discovery; // INTEGRATION: helper type (sibling branch)

        // Client connect-attempt state machine.
        private bool _clientAttempt;
        private bool _resultDelivered;
        private bool _transportErrored;

        // Cached for LocalEndpointAddress.
        private string _cachedLocalAddress;

        // ---------------------------------------------------------------- IOnlineConnection state

        public bool IsHost => NetworkServer.active && NetworkClient.active;
        public bool IsClient => NetworkClient.active && !NetworkServer.active;
        public bool IsConnected => NetworkServer.active ? NetworkServer.active : NetworkClient.isConnected;

        public ConnectionRole Role =>
            NetworkServer.active ? ConnectionRole.Host :
            NetworkClient.active ? ConnectionRole.Client :
            ConnectionRole.None;

        public string LocalEndpointAddress => _cachedLocalAddress ??= ResolveLocalAddress();

        // ---------------------------------------------------------------- IOnlineConnection events

        public event Action OnHostStarted;
        public event Action OnHostStopped;
        public event Action<ConnectResult> OnClientConnected;
        public event Action OnClientDisconnected;
        public event Action<RoomInfo> OnLanRoomDiscovered;

        // ---------------------------------------------------------------- Unity lifecycle

        private void Awake()
        {
            EnsureManager();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void EnsureManager()
        {
            if (_manager != null)
                return;

            _manager = NetworkManager.singleton as CloSimNetworkManager;
            if (_manager == null)
            {
                var go = new GameObject(nameof(CloSimNetworkManager));
                _manager = go.AddComponent<CloSimNetworkManager>(); // Awake ensures KCP transport
            }

            // INTEGRATION: the auth helper gates every connect (token/password + protocol version).
            // Assign it if present; it reads the handshake state off CloSimNetworkManager.
            if (_manager.authenticator == null)
            {
                var auth = _manager.GetComponent<CloSimNetworkAuthenticator>();
                if (auth == null)
                    auth = _manager.gameObject.AddComponent<CloSimNetworkAuthenticator>();
                _manager.authenticator = auth;
            }

            // INTEGRATION: LAN discovery helper lives on the manager GameObject.
            _discovery = _manager.GetComponent<CloSimNetworkDiscovery>();
            if (_discovery == null)
                _discovery = _manager.gameObject.AddComponent<CloSimNetworkDiscovery>();

            Subscribe();
        }

        private void Subscribe()
        {
            _manager.HostStarted += HandleHostStarted;
            _manager.HostStopped += HandleHostStopped;
            _manager.ClientConnectResult += HandleClientConnectResult;
            _manager.ClientTransportError += HandleClientTransportError;
            _manager.ClientDisconnected += HandleClientDisconnected;

            if (_discovery != null)
                _discovery.OnServerFound += HandleServerFound; // INTEGRATION
        }

        private void Unsubscribe()
        {
            if (_manager != null)
            {
                _manager.HostStarted -= HandleHostStarted;
                _manager.HostStopped -= HandleHostStopped;
                _manager.ClientConnectResult -= HandleClientConnectResult;
                _manager.ClientTransportError -= HandleClientTransportError;
                _manager.ClientDisconnected -= HandleClientDisconnected;
            }

            if (_discovery != null)
                _discovery.OnServerFound -= HandleServerFound; // INTEGRATION
        }

        // ---------------------------------------------------------------- Host lifecycle

        public void StartHost(HostStartOptions options)
        {
            EnsureManager();

            _clientAttempt = false;

            // Host-side handshake expectations consumed by the auth helper.
            _manager.ExpectedJoinToken = options.joinToken ?? "";
            _manager.ExpectedVersion = NetcodeProtocol.Resolve(options.version);
            _manager.maxConnections = 6; // room capacity; A3 enforces 6-cap / <=3-per-alliance on top

            _manager.ConfigureTransportPort((ushort)Mathf.Max(0, options.port));

            _manager.StartHost();

            // Advertise on LAN for BOTH public and private rooms (private relies on it entirely).
            // INTEGRATION: CloSimNetworkDiscovery broadcasts this RoomInfo to LAN searchers.
            _discovery?.AdvertiseServer(BuildAdvertisedRoom(options));
        }

        public void StopHost()
        {
            _discovery?.StopAdvertising(); // INTEGRATION
            if (_manager != null)
                _manager.StopHost();
        }

        // ---------------------------------------------------------------- Client lifecycle

        public void StartClient(ConnectEndpoint endpoint)
        {
            EnsureManager();

            _clientAttempt = true;
            _resultDelivered = false;
            _transportErrored = false;

            // Client-side handshake values consumed by the auth helper.
            _manager.PendingJoinToken = endpoint.joinToken ?? "";
            _manager.PendingClientVersion = NetcodeProtocol.Resolve(endpoint.version);

            // INTEGRATION: DirectConnect sets networkAddress + transport port and calls StartClient().
            DirectConnect.Join(endpoint.address, (ushort)Mathf.Max(0, endpoint.port));
        }

        public void StopClient()
        {
            if (_manager != null)
                _manager.StopClient();
        }

        // ---------------------------------------------------------------- LAN discovery

        public void StartLanDiscovery()
        {
            EnsureManager();
            _discovery?.StartDiscovery(); // INTEGRATION
        }

        public void StopLanDiscovery()
        {
            _discovery?.StopDiscovery(); // INTEGRATION
        }

        // ---------------------------------------------------------------- Manager event handlers

        private void HandleHostStarted() => OnHostStarted?.Invoke();
        private void HandleHostStopped() => OnHostStopped?.Invoke();

        private void HandleClientConnectResult(ConnectResult result)
        {
            if (!_clientAttempt || _resultDelivered)
                return;

            _resultDelivered = true;
            OnClientConnected?.Invoke(result);
        }

        private void HandleClientTransportError(string reason)
        {
            _transportErrored = true;
        }

        private void HandleClientDisconnected()
        {
            if (!_clientAttempt)
                return; // host's local client — surfaced via OnHostStopped instead

            if (!_resultDelivered)
            {
                _resultDelivered = true;
                OnClientConnected?.Invoke(_transportErrored ? ConnectResult.TransportError : ConnectResult.Timeout);
            }

            _clientAttempt = false;
            _transportErrored = false;
            OnClientDisconnected?.Invoke();
        }

        private void HandleServerFound(RoomInfo room) // INTEGRATION
        {
            OnLanRoomDiscovered?.Invoke(room);
        }

        // ---------------------------------------------------------------- Helpers

        private RoomInfo BuildAdvertisedRoom(HostStartOptions options)
        {
            int port = options.port > 0 ? options.port : NetworkPort();
            return new RoomInfo
            {
                roomId = "",                    // master server assigns for public rooms (A5)
                name = options.roomName ?? "",
                hostName = "",                  // filled by the room layer (A3)
                address = LocalEndpointAddress,
                port = port,
                gameId = options.gameId ?? "",
                region = "",
                playerCount = NetworkServer.connections?.Count ?? 0,
                capacity = 6,
                visibility = options.visibility,
                requiresToken = !string.IsNullOrEmpty(options.joinToken),
                state = RoomState.Lobby,
                version = NetcodeProtocol.Resolve(options.version)
            };
        }

        private int NetworkPort()
        {
            // Best-effort read of the active transport's configured port via its Uri.
            try
            {
                Uri uri = Transport.active?.ServerUri();
                if (uri != null && uri.Port > 0)
                    return uri.Port;
            }
            catch (NotImplementedException)
            {
                // Some transports don't implement ServerUri(); fall through to default.
            }
            return 7777;
        }

        private static string ResolveLocalAddress()
        {
            try
            {
                foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch (Exception)
            {
                // DNS lookup can fail on locked-down platforms; fall back to loopback.
            }
            return "127.0.0.1";
        }
    }
}
