// CloSim Online Multiplayer — connection lifecycle contract (stub).
// SOURCE OF TRUTH: Documentation/online/architecture.md §6.1.
// Owned by A1 (Architect). IMPLEMENTED BY A2 (feat/netcode-foundation).
// A3/A4/A5 consume this — they must NEVER touch Mirror's NetworkManager directly.

using System;

namespace Online.Contracts
{
    /// <summary>
    /// Façade over the Mirror listen-server / client lifecycle. The single seam through which
    /// rooms (A3), gameplay (A4), and the Server List (A5) start/stop and observe connections.
    /// </summary>
    public interface IOnlineConnection
    {
        // --- State ---
        bool IsHost { get; }
        bool IsClient { get; }
        bool IsConnected { get; }
        ConnectionRole Role { get; }
        string LocalEndpointAddress { get; } // best-effort public/LAN address of this host

        // --- Host lifecycle ---
        void StartHost(HostStartOptions options);
        void StopHost();

        // --- Client lifecycle ---
        void StartClient(ConnectEndpoint endpoint);
        void StopClient();

        // --- LAN discovery ---
        void StartLanDiscovery();
        void StopLanDiscovery();

        // --- Events ---
        event Action OnHostStarted;
        event Action OnHostStopped;
        event Action<ConnectResult> OnClientConnected;   // Success or a rejection reason
        event Action OnClientDisconnected;
        event Action<RoomInfo> OnLanRoomDiscovered;      // one per discovered LAN host
    }
}
