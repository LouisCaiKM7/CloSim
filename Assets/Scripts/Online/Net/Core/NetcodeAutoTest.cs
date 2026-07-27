// CloSim Online Multiplayer — dev-only autonomous connection test (CORE). Namespace: Online.Net.
//
// Activated ONLY by command-line flags, so it has ZERO effect on a normal launch:
//   CloSim.exe -closim-autohost              -> starts a host (listen server) on port 7777
//   CloSim.exe -closim-autoclient [address]  -> connects a client to <address>:7777 (default 127.0.0.1)
//
// It drives the same IOnlineConnection facade the in-game lobby uses and logs every connection event with
// a [NetcodeAutoTest] prefix, so a host + client pair can be validated purely from the player logs without
// any UI interaction. This is a developer aid (like NetcodeTestBootstrap) — not shipping behaviour.

using System;
using Online.Contracts;
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
            var conn = go.AddComponent<OnlineConnection>();

            conn.OnHostStarted += () => Debug.Log("[NetcodeAutoTest] HOST STARTED (listening on 7777)");
            conn.OnHostStopped += () => Debug.Log("[NetcodeAutoTest] HOST STOPPED");
            conn.OnClientConnected += r => Debug.Log($"[NetcodeAutoTest] CLIENT RESULT: {r}");
            conn.OnClientDisconnected += () => Debug.Log("[NetcodeAutoTest] CLIENT DISCONNECTED");

            if (host)
            {
                Debug.Log("[NetcodeAutoTest] starting HOST on port 7777...");
                conn.StartHost(new HostStartOptions
                {
                    port = 7777,
                    joinToken = "",
                    visibility = RoomVisibility.Public,
                    roomName = "AutoTest",
                    gameId = "Rebuilt",
                    version = NetcodeProtocol.Version
                });
            }
            else
            {
                Debug.Log($"[NetcodeAutoTest] starting CLIENT to {address}:7777...");
                conn.StartClient(new ConnectEndpoint
                {
                    address = address,
                    port = 7777,
                    joinToken = "",
                    version = NetcodeProtocol.Version
                });
            }
        }
    }
}
