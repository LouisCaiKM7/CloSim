// CloSim Online Multiplayer — CloSim-specific Mirror NetworkManager (CORE, A2-owned).
// Namespace: Online.Net. File under Assets/Scripts/Online/Net/Core/.
//
// This is the bootstrap that wires Mirror for CloSim: it guarantees a KCP transport, hosts the
// shared auth-handshake state that the helper `CloSimNetworkAuthenticator` reads, and re-raises
// Mirror's lifecycle callbacks as plain C# events that OnlineConnection (the IOnlineConnection
// façade) consumes. Nothing outside Online.Net.* should touch Mirror directly.
//
// SWARM INTEGRATION SEAMS (helpers on their own branches; not present in this worktree — merged later):
//   * `CloSimNetworkAuthenticator : Mirror.NetworkAuthenticator` — assigned to `authenticator`.
//     It reads the host expectations (ExpectedJoinToken / ExpectedVersion / maxConnections) and the
//     client handshake values (PendingJoinToken / PendingClientVersion) from THIS class, and calls
//     ReportClientConnectResult(...) on the client with the server's verdict.
//   * `CloSimNetworkDiscovery` — LAN advertise/find (owned by OnlineConnection's façade wiring).
//   * `DirectConnect.Join(string ip, ushort port)` — direct-IP client connect helper.

using System;
using Mirror;
using Online.Contracts;
using UnityEngine;
using kcp2k;

namespace Online.Net
{
    /// <summary>Transport backends CloSim can run on. KCP (UDP) is the default for realtime robot sync.</summary>
    public enum NetTransportKind
    {
        Kcp,
        Telepathy
    }

    [AddComponentMenu("CloSim/Netcode/CloSim Network Manager")]
    public class CloSimNetworkManager : NetworkManager
    {
        [Header("CloSim transport")]
        [Tooltip("Primary transport. KCP (kcp2k) is the CloSim default; Telepathy (TCP) is a fallback.")]
        [SerializeField] private NetTransportKind _transportKind = NetTransportKind.Kcp;

        [Tooltip("Default game port used when a host/connect request passes port 0.")]
        [SerializeField] private ushort _defaultPort = 7777;

        // ---- Shared auth-handshake state (written by OnlineConnection, read by the auth helper) ----

        /// <summary>Host: expected join token/password. "" => open room (no token gating).</summary>
        [NonSerialized] public string ExpectedJoinToken = "";

        /// <summary>Host: protocol/build version this listen-server accepts.</summary>
        [NonSerialized] public string ExpectedVersion = NetcodeProtocol.Version;

        /// <summary>Client: join token/password to present on connect. "" => none.</summary>
        [NonSerialized] public string PendingJoinToken = "";

        /// <summary>Client: protocol/build version to present on connect.</summary>
        [NonSerialized] public string PendingClientVersion = NetcodeProtocol.Version;

        // ---------------------------------------------------------------- Public events (façade wires these)

        public event Action HostStarted;
        public event Action HostStopped;

        /// <summary>Fires on the local client once it is fully connected (post-auth).</summary>
        public event Action ClientConnected;

        /// <summary>Fires on the local client when it disconnects for any reason.</summary>
        public event Action ClientDisconnected;

        /// <summary>Fires on the local client with a transport-level error reason (best-effort).</summary>
        public event Action<string> ClientTransportError;

        /// <summary>
        /// The connect verdict surfaced to UI. The auth helper calls <see cref="ReportClientConnectResult"/>
        /// with a rejection reason; the happy path is reported as Success from OnClientConnect below.
        /// </summary>
        public event Action<ConnectResult> ClientConnectResult;

        /// <summary>Local protocol/build version. Exposed so A3/A5 can label mismatched rooms.</summary>
        public string ProtocolVersion => NetcodeProtocol.Version;

        // ---------------------------------------------------------------- Bootstrap

        public override void Awake()
        {
            EnsureTransport();
            base.Awake();
        }

        /// <summary>Guarantees a transport component exists and is assigned before Mirror initialises.</summary>
        private void EnsureTransport()
        {
            if (transport != null)
                return;

            switch (_transportKind)
            {
                case NetTransportKind.Telepathy:
                    transport = GetComponent<TelepathyTransport>() ?? gameObject.AddComponent<TelepathyTransport>();
                    break;
                default:
                    transport = GetComponent<KcpTransport>() ?? gameObject.AddComponent<KcpTransport>();
                    break;
            }

            ConfigureTransportPort(_defaultPort);
        }

        /// <summary>
        /// Sets the active transport's listen/connect port. Passing 0 keeps the CloSim default port.
        /// Used host-side by OnlineConnection and (client-side) by the DirectConnect helper.
        /// </summary>
        public void ConfigureTransportPort(ushort port)
        {
            ushort effective = port == 0 ? _defaultPort : port;

            switch (transport)
            {
                case KcpTransport kcp:
                    kcp.Port = effective;
                    break;
                case TelepathyTransport tcp:
                    tcp.port = effective;
                    break;
            }
        }

        // ---------------------------------------------------------------- Verdict passthrough

        /// <summary>
        /// Called by the auth helper (client-side) once the server's verdict is known, so the façade can
        /// surface the precise <see cref="ConnectResult"/> (BadToken / Version / Full). Success is reported
        /// automatically from OnClientConnect.
        /// </summary>
        public void ReportClientConnectResult(ConnectResult result)
        {
            ClientConnectResult?.Invoke(result);
        }

        // ---------------------------------------------------------------- Mirror lifecycle overrides

        public override void OnStartHost()
        {
            base.OnStartHost();
            HostStarted?.Invoke();
        }

        public override void OnStopHost()
        {
            base.OnStopHost();
            HostStopped?.Invoke();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();

            // A client must be "ready" before the server sends it any spawned objects (the room, robots, …).
            // base.OnClientConnect only readies when !clientLoadedScene, which is not guaranteed for CloSim's
            // overlay-driven lobby flow, so ensure it explicitly. Without this a joined client connects but
            // receives zero replicated state (empty room).
            if (mode == NetworkManagerMode.ClientOnly && !NetworkClient.ready)
                NetworkClient.Ready();

            // Only surface Success for a genuine remote client. The host's local client also runs this.
            if (mode == NetworkManagerMode.ClientOnly)
                ReportClientConnectResult(ConnectResult.Success);

            ClientConnected?.Invoke();
        }

        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            base.OnServerReady(conn); // NetworkServer.SetClientReady(conn) -> conn.isReady = true

            // CloSim lobby/match connections have NO per-connection player object (there is no playerPrefab
            // and no AddPlayer step — robots and the RoomService are spawned server-side by our own managers).
            // Mirror's SetClientReady only calls SpawnObserversForConnection when conn.identity != null, so a
            // player-less client becomes "ready" yet observes ZERO already-spawned objects — the RoomService
            // (spawned at host start) is therefore never sent to a joining client, and the room stays empty.
            // Rebuild observers for every spawned identity so this now-ready connection receives them all.
            // Offline-safe: offline play has no remote ready server connections, so this is a no-op there.
            // AddObserver dedups on connectionId, so the host's local connection is never double-spawned.
            //
            // ROOT CAUSE OF THE SyncList-NOT-REPLICATING BUG (see CLAUDE.md task notes / Mirror 96.6.4):
            // NetworkClient.isSpawnFinished (Mirror/Assets/Mirror/Core/NetworkClient.cs:121) is a client-side
            // static flag that starts false and is only ever set true by OnObjectSpawnFinished
            // (NetworkClient.cs:1324-1355), which is only ever triggered by an ObjectSpawnFinishedMessage —
            // and the ONLY place Mirror sends that message is NetworkServer.SpawnObserversForConnection
            // (NetworkServer.cs:1432-1496), which SetClientReady only calls "if (conn.identity != null)"
            // (NetworkServer.cs:1420-1430). Because CloSim connections never get conn.identity (no
            // playerPrefab/AddPlayer step), that guard is never satisfied, so the server NEVER sends
            // ObjectSpawnStartedMessage/ObjectSpawnFinishedMessage to a CloSim client — isSpawnFinished stays
            // false on that client forever. With isSpawnFinished == false, NetworkClient.OnSpawn
            // (NetworkClient.cs:1543-1584) does NOT call ApplySpawnPayload (the method that actually
            // deserializes the SyncList/SyncVar payload and calls OnStartClient — NetworkClient.cs:1123-1180);
            // instead it deep-copies the message into a `pendingSpawns` dictionary that is only ever flushed
            // by OnObjectSpawnFinished. Since that message never arrives, the payload — the full
            // RoomService._members SyncList state included — is received over the wire but permanently
            // deferred and never applied. RoomService.Instance still becomes non-null because
            // NetworkIdentity/RoomService.Awake() runs at Instantiate() time, independent of
            // ApplySpawnPayload — which is why the identity "looked spawned" while Members stayed empty.
            //
            // FIX: bracket the manual RebuildObservers burst with the same ObjectSpawnStartedMessage /
            // ObjectSpawnFinishedMessage pair that SpawnObserversForConnection sends, so this connection's
            // client runs the normal deferred-apply-then-flush spawn sequence and isSpawnFinished correctly
            // flips true once. Both message types are public Mirror structs (Messages.cs) so this needs no
            // Mirror-internal access. Safe in host mode: NetworkClient registers these as no-ops for the
            // host's own local connection (RegisterMessageHandlers(hostMode: true), NetworkClient.cs:507-517).
            conn.Send(new ObjectSpawnStartedMessage());
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity != null)
                    NetworkServer.RebuildObservers(identity, true);
            }
            conn.Send(new ObjectSpawnFinishedMessage());
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            ClientDisconnected?.Invoke();
        }

        public override void OnClientError(TransportError error, string reason)
        {
            base.OnClientError(error, reason);
            ClientTransportError?.Invoke(reason);
        }
    }
}
