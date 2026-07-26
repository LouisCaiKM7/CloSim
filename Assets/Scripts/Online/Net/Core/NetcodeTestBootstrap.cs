// CloSim Online Multiplayer — minimal dev test bootstrap (CORE, A2-owned).
// Namespace: Online.Net. File under Assets/Scripts/Online/Net/Core/.
//
// A tiny OnGUI harness so the netcode foundation can be validated WITHOUT A3/A4/A5:
// drop this component on an empty GameObject in a scratch scene, enter Play mode, then
// Start Host in one instance and Start Client (127.0.0.1) in a second instance
// (ParrelSync clone or a standalone build). It talks only to IOnlineConnection — never Mirror.
//
// This is a developer aid, not shipping UI. The real in-game flows are built by A3 (rooms)
// and A5 (Server List). No .unity scene is committed here to avoid Unity-YAML merge conflicts
// across the swarm; create a throwaway scene and add this component (one step).

using Online.Contracts;
using UnityEngine;

namespace Online.Net
{
    [AddComponentMenu("CloSim/Netcode/Netcode Test Bootstrap")]
    public class NetcodeTestBootstrap : MonoBehaviour
    {
        [SerializeField] private string _address = "127.0.0.1";
        [SerializeField] private int _port = 7777;
        [SerializeField] private string _joinToken = "";
        [SerializeField] private string _gameId = "Rebuilt";
        [SerializeField] private RoomVisibility _visibility = RoomVisibility.Public;

        private IOnlineConnection _connection;
        private string _status = "idle";

        private void Awake()
        {
            _connection = gameObject.AddComponent<OnlineConnection>();

            _connection.OnHostStarted += () => _status = "host started";
            _connection.OnHostStopped += () => _status = "host stopped";
            _connection.OnClientConnected += r => _status = $"client result: {r}";
            _connection.OnClientDisconnected += () => _status = "client disconnected";
            _connection.OnLanRoomDiscovered += room => _status = $"LAN room: {room.name} @ {room.address}:{room.port}";
        }

        private void OnGUI()
        {
            const int w = 260;
            GUILayout.BeginArea(new Rect(10, 10, w, 400), GUI.skin.box);
            GUILayout.Label("CloSim Netcode Test");
            GUILayout.Label($"Role: {_connection.Role}   Connected: {_connection.IsConnected}");
            GUILayout.Label($"Local addr: {_connection.LocalEndpointAddress}");
            GUILayout.Label($"Status: {_status}");

            GUILayout.Space(6);
            _address = GUILayout.TextField(_address);
            _joinToken = GUILayout.TextField(_joinToken);

            GUILayout.Space(6);
            if (!_connection.IsConnected)
            {
                if (GUILayout.Button("Start Host"))
                    _connection.StartHost(new HostStartOptions
                    {
                        port = _port,
                        joinToken = _joinToken,
                        visibility = _visibility,
                        roomName = "Test Room",
                        gameId = _gameId,
                        version = NetcodeProtocol.Version
                    });

                if (GUILayout.Button("Start Client"))
                    _connection.StartClient(new ConnectEndpoint
                    {
                        address = _address,
                        port = _port,
                        joinToken = _joinToken,
                        version = NetcodeProtocol.Version
                    });

                if (GUILayout.Button("Start LAN Discovery"))
                    _connection.StartLanDiscovery();
            }
            else
            {
                if (_connection.Role == ConnectionRole.Host && GUILayout.Button("Stop Host"))
                    _connection.StopHost();
                if (_connection.Role == ConnectionRole.Client && GUILayout.Button("Stop Client"))
                    _connection.StopClient();
            }

            GUILayout.EndArea();
        }
    }
}
