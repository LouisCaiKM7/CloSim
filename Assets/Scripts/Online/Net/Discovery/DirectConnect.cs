// CloSim Online Multiplayer — direct IP:port connect (+ start-host-on-port helper).
// Module: LAN discovery + direct-IP connect (A2 Netcode Foundation, discovery helper).
// Namespace: Online.Net.Discovery. Additive only; consumes Online.Contracts DTOs.
//
// WHAT THIS IS
//   The "connect to your own host by IP" path (architecture.md §4.2 (b)): point the active Mirror
//   transport at a port, set NetworkManager.singleton.networkAddress, and StartClient(). Also the
//   symmetric host helper (start a listen-server on a chosen port). This is transport-agnostic:
//   KCP (default) and Telepathy both derive from Mirror.PortTransport, so we set the port through
//   that single seam without caring which transport the core agent selected.
//
// PORT-FORWARDING NOTE
//   LAN play needs NO port forwarding — direct-IP and CloSimNetworkDiscovery work on the local subnet
//   out of the box. For INTERNET play against a public host, making the host reachable (port forward /
//   UPnP) is the HOST's responsibility. There is no relay in v1 (architecture.md §10 Q1).
//
// RESULT SEMANTICS (important for the core agent)
//   Mirror connects asynchronously — success/rejection is not known when StartClient() returns. The
//   ConnectResult returned here is the *synchronous launch* result: Success = the attempt was started;
//   TransportError = it could not even be started (no NetworkManager / no port-transport / already
//   running / blank address). The DEFINITIVE outcome (Success / Rejected_BadToken / Timeout / ...)
//   arrives asynchronously through IOnlineConnection.OnClientConnected, driven by Mirror's client
//   connect/disconnect callbacks and the CloSimNetworkAuthenticator token handshake.

using Mirror;
using Online.Contracts;
using Online.Net;

namespace Online.Net.Discovery
{
    /// <summary>
    /// Static helpers for direct IP:port client connect and start-host-on-port. Thin, stateless
    /// wrappers over Mirror's NetworkManager/transport so UI (A3/A5) never touches Mirror directly.
    /// </summary>
    public static class DirectConnect
    {
        // ── INTEGRATION DECISION (A2 seam reconciliation) ─────────────────────────────────────────
        // The CANONICAL staging location for the client's join token/version is the
        // CloSimNetworkManager (PendingJoinToken / PendingClientVersion) — that is what
        // CloSimNetworkAuthenticator reads at handshake time. The static properties below are kept as
        // a convenience MIRROR for callers that connect via DirectConnect directly (not through
        // OnlineConnection); Join(ConnectEndpoint) writes BOTH the manager (canonical) and these.

        /// <summary>
        /// Mirror of the join token to present on the NEXT client connect. The authoritative value the
        /// authenticator reads is <c>CloSimNetworkManager.PendingJoinToken</c>; this static is kept in
        /// sync by <see cref="Join(ConnectEndpoint)"/> for direct callers. "" = no token.
        /// </summary>
        public static string PendingJoinToken { get; set; } = string.Empty;

        /// <summary>Mirror of the client protocol/version to present on the next connect. Authoritative
        /// value is <c>CloSimNetworkManager.PendingClientVersion</c>. "" = unspecified.</summary>
        public static string PendingVersion { get; set; } = string.Empty;

        /// <summary>
        /// Connect directly to a host by IP/hostname and port. Sets the active transport's port,
        /// sets <c>NetworkManager.singleton.networkAddress</c>, and calls StartClient().
        /// See the "RESULT SEMANTICS" note at the top of this file — the returned value reports whether
        /// the attempt LAUNCHED; the final outcome arrives via IOnlineConnection events.
        /// </summary>
        /// <param name="ip">Host IP or hostname (e.g. "127.0.0.1" for your own host, or a LAN address).</param>
        /// <param name="port">Transport port. 0 = leave the transport's configured port unchanged.</param>
        public static ConnectResult Join(string ip, ushort port)
        {
            NetworkManager nm = NetworkManager.singleton;
            if (nm == null)
                return ConnectResult.TransportError;                 // core agent hasn't spawned the manager yet.
            if (string.IsNullOrWhiteSpace(ip))
                return ConnectResult.TransportError;                 // nothing to connect to.
            if (NetworkClient.active || NetworkServer.active)
                return ConnectResult.TransportError;                 // already hosting/connected — stop first.

            if (port != 0 && !SetTransportPort(port))
                return ConnectResult.TransportError;                 // active transport has no settable port.

            nm.networkAddress = ip.Trim();
            nm.StartClient();
            return ConnectResult.Success;                            // attempt launched; await async result.
        }

        /// <summary>
        /// Connect using a full <see cref="ConnectEndpoint"/> (address/port/token/version). Stages the
        /// token + version into the CANONICAL manager location (and the static mirror), then delegates
        /// to <see cref="Join(string, ushort)"/>.
        /// </summary>
        public static ConnectResult Join(ConnectEndpoint endpoint)
        {
            string token = endpoint.joinToken ?? string.Empty;
            string version = endpoint.version ?? string.Empty;

            // Static mirror (for direct callers) …
            PendingJoinToken = token;
            PendingVersion = version;

            // … and the CANONICAL location the authenticator actually reads.
            if (NetworkManager.singleton is CloSimNetworkManager m)
            {
                m.PendingJoinToken = token;
                if (!string.IsNullOrEmpty(version))
                    m.PendingClientVersion = version;
            }

            return Join(endpoint.address, (ushort)endpoint.port);
        }

        /// <summary>
        /// Start a listen-server (host) on a given port — the other half of the "join your own host"
        /// flow. Sets the active transport's port then calls StartHost(). Returns false if there is no
        /// NetworkManager, the transport port cannot be set, or a server/client is already running.
        /// </summary>
        /// <param name="port">Transport port to host on. 0 = leave the transport's configured port unchanged.</param>
        public static bool StartHostOnPort(ushort port)
        {
            NetworkManager nm = NetworkManager.singleton;
            if (nm == null)
                return false;
            if (NetworkServer.active || NetworkClient.active)
                return false;
            if (port != 0 && !SetTransportPort(port))
                return false;

            nm.StartHost();
            return true;
        }

        /// <summary>
        /// Set the active Mirror transport's port in a transport-agnostic way. Works for any transport
        /// deriving from <see cref="PortTransport"/> (KCP, Telepathy, ...). Returns false if the active
        /// transport does not expose a settable port.
        /// </summary>
        public static bool SetTransportPort(ushort port)
        {
            Transport active = Transport.active;
            if (active is PortTransport portTransport)
            {
                portTransport.Port = port;
                return true;
            }
            return false;
        }
    }
}
