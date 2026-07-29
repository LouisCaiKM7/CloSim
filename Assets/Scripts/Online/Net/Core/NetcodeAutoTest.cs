// CloSim Online Multiplayer — dev-only autonomous connection/room test (CORE). Namespace: Online.Net.
//
// Activated ONLY by command-line flags, so it has ZERO effect on a normal launch:
//   CloSim.exe -closim-autohost              -> hosts a room via LobbyServices on port 7777
//   CloSim.exe -closim-autoclient [address]  -> joins <address>:7777 via LobbyServices (default 127.0.0.1)
//
// Unlike a raw StartHost/StartClient probe, this drives the SAME LobbyServices path the in-game lobby
// uses (Create Room / Join), so it exercises RoomNetworkBootstrap spawning the networked RoomService and
// the room replicating to the client. It logs connection events AND the live RoomService member count with
// a [NetcodeAutoTest] prefix, so host + client room replication can be validated purely from player logs.

using System;
using Mirror;
using Online.Contracts;
using Online.Rooms;
using UnityEngine;

namespace Online.Net
{
    public static class NetcodeAutoTest
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool host = false, client = false;
            string address = "127.0.0.1";

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-closim-autohost", StringComparison.OrdinalIgnoreCase))
                    host = true;
                else if (string.Equals(args[i], "-closim-autoclient", StringComparison.OrdinalIgnoreCase))
                {
                    client = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        address = args[i + 1];
                }
            }

            if (!host && !client)
                return;

            var go = new GameObject("NetcodeAutoTest");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<NetcodeAutoTestRunner>().Begin(host, address);
        }
    }

    /// <summary>MonoBehaviour half of NetcodeAutoTest — drives the flow and polls room state over time.</summary>
    public sealed class NetcodeAutoTestRunner : MonoBehaviour
    {
        private float _t;
        private string _lastSig = "";

        private bool _host2;
        private string _address2;
        private bool _started;

        public void Begin(bool host, string address)
        {
            // Defer the host/join until AFTER the Splash scene has advanced to Main_Menu, so the room is
            // created in the menu (exactly like the real UI) and is not destroyed by the Splash->Main_Menu
            // scene transition. This makes the test represent how a player actually hosts/joins.
            _host2 = host;
            _address2 = address;
        }

        private void StartFlow(bool host, string address)
        {
            LobbyServices svc = LobbyServices.EnsureExists();
            svc.Connection.OnHostStarted += () => Debug.Log("[NetcodeAutoTest] HOST STARTED (listening on 7777)");
            svc.Connection.OnClientConnected += r => Debug.Log($"[NetcodeAutoTest] CLIENT RESULT: {r}");

            if (host)
            {
                Debug.Log("[NetcodeAutoTest] hosting a room via LobbyServices...");
                var info = new RoomInfo
                {
                    name = "AutoTest",
                    gameId = "Rebuilt",
                    port = 7777,
                    capacity = 6,
                    visibility = RoomVisibility.Public,
                    state = RoomState.Lobby,
                    version = ""
                };
                var config = new NetworkMatchConfig { blueCount = 1, redCount = 1, allowSpectators = true };
                svc.HostRoom(info, config, "");
            }
            else
            {
                Debug.Log($"[NetcodeAutoTest] joining {address}:7777 via LobbyServices...");
                svc.JoinDirect(address, 7777, "");
            }
        }

        private void Update()
        {
            _t += Time.deltaTime;

            // Start the host/join only after the menu has loaded (past the Splash transition).
            if (!_started && _t > 12f)
            {
                _started = true;
                StartFlow(_host2, _address2);
            }

            RoomService room = RoomService.Instance;
            bool present = room != null;
            int members = present ? room.Members.Count : -1;

            bool cliConnected = NetworkClient.active && NetworkClient.isConnected;
            bool cliReady = NetworkClient.ready;
            int cliSpawned = NetworkClient.spawned != null ? NetworkClient.spawned.Count : -1;

            // Log a concise connection/room-state signature whenever it changes, so host + client room
            // replication can be validated from player logs.
            string sig = $"{present}|{members}|{cliConnected}|{cliReady}|{cliSpawned}";
            if (sig != _lastSig)
            {
                _lastSig = sig;
                Debug.Log($"[NetcodeAutoTest] t={_t:F0}s room={present} members={members} " +
                          $"cliConnected={cliConnected} cliReady={cliReady} cliSpawnedCount={cliSpawned}");
            }
        }
    }
}
