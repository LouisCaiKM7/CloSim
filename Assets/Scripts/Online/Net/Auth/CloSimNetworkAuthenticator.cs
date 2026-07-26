// CloSim Online Multiplayer — network join authenticator (A2 Netcode Foundation).
// Module: token/password gating + protocol-version reject, on top of Mirror's
// request/response NetworkAuthenticator pattern.
//
// Owned by the A2 swarm "auth" helper. Additive only — this file lives entirely under
// Assets/Scripts/Online/Net/Auth/ and touches nothing else.
//
// Contract references (read-only): Online.Contracts.ConnectResult, HostStartOptions.joinToken,
// ConnectEndpoint.joinToken — see Documentation/online/architecture.md §4.2 and §5.
//
// The core netcode agent (CloSimNetworkManager) wires an instance of this component into
// NetworkManager.authenticator and configures it before StartHost / StartClient.

using System;
using System.Collections;
using Mirror;
using Online.Contracts;
using UnityEngine;

namespace Online.Net.Auth
{
    /// <summary>
    /// Mirror authenticator for CloSim rooms. Gates connecting clients on two independent checks,
    /// evaluated server-side, both of which HARD-REJECT on failure:
    ///
    /// 1. <b>Protocol/version compatibility.</b> The client sends the protocol version it was built
    ///    against; the server rejects any client whose version != the server's
    ///    (see <see cref="ProtocolVersion"/>). Maps to <see cref="ConnectResult.Rejected_Version"/>.
    ///
    /// 2. <b>Join token / password.</b> The host sets <see cref="ExpectedToken"/>:
    ///    <list type="bullet">
    ///      <item>blank/empty/whitespace  => <i>public room</i>: any token is accepted (including none);</item>
    ///      <item>non-blank               => <i>private / token-gated</i>: the client's
    ///            <see cref="ClientToken"/> must match exactly (ordinal), else reject.</item>
    ///    </list>
    ///    Token gating applies to BOTH public-listed and private rooms — "public" only means the
    ///    room is listed on the master directory, not that it is un-gated. Maps to
    ///    <see cref="ConnectResult.Rejected_BadToken"/>.
    ///
    /// The version check runs before the token check so a stale build never leaks a wrong-password
    /// signal. The token itself is only ever validated host-side and is never placed in RoomInfo.
    ///
    /// Wiring (done by the core agent):
    /// <code>
    ///   var auth = gameObject.AddComponent&lt;CloSimNetworkAuthenticator&gt;();
    ///   networkManager.authenticator = auth;
    ///   // Host, from HostStartOptions.joinToken:
    ///   auth.ConfigureAsHost(options.joinToken);
    ///   // Client, from ConnectEndpoint.joinToken:
    ///   auth.ConfigureAsClient(endpoint.joinToken);
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CloSim/Online/CloSim Network Authenticator")]
    public class CloSimNetworkAuthenticator : NetworkAuthenticator
    {
        // ---------------------------------------------------------------------
        // Response codes carried in AuthResponseMessage.code. The core connection
        // wrapper maps these to Online.Contracts.ConnectResult (see MapToConnectResult).
        // ---------------------------------------------------------------------

        /// <summary>Auth succeeded; the connection is accepted.</summary>
        public const byte CodeSuccess = 100;

        /// <summary>Join token / password did not match the host's expected token.</summary>
        public const byte CodeBadToken = 200;

        /// <summary>Client protocol version does not match the server's.</summary>
        public const byte CodeVersionMismatch = 210;

        /// <summary>Server-side error while processing the request (malformed / unexpected).</summary>
        public const byte CodeServerError = 220;

        // ---------------------------------------------------------------------
        // Configuration — set by the core agent before StartHost / StartClient.
        // ---------------------------------------------------------------------

        [Header("Host configuration")]
        [Tooltip("Expected join token/password for THIS host. Blank/empty/whitespace => public room " +
                 "(accept any client token). Non-blank => clients must present a matching token. " +
                 "Set from HostStartOptions.joinToken. Never placed in RoomInfo.")]
        public string ExpectedToken = "";

        [Header("Client configuration")]
        [Tooltip("Join token/password THIS client presents to the host. Set from ConnectEndpoint.joinToken. " +
                 "Leave blank when joining a public (un-gated) room.")]
        public string ClientToken = "";

        [Header("Diagnostics")]
        [Tooltip("Log accept/reject decisions to the Unity console.")]
        public bool logAuthDecisions = false;

        /// <summary>Seconds to keep a rejected connection alive so the reject reason reaches the client
        /// before the transport disconnects it. Mirror's recommended graceful-reject delay.</summary>
        [Tooltip("Grace period (seconds) before disconnecting a rejected client so it can read the reason.")]
        public float rejectDisconnectDelay = 1f;

        // ---------------------------------------------------------------------
        // Client-side result surface — the core wrapper reads these (or subscribes to the event)
        // to raise IOnlineConnection.OnClientConnected(ConnectResult).
        // ---------------------------------------------------------------------

        /// <summary>Code from the most recent server auth response received by this client.</summary>
        public byte LastClientResponseCode { get; private set; }

        /// <summary>Human-readable message from the most recent server auth response.</summary>
        public string LastClientResponseMessage { get; private set; }

        /// <summary>Raised on the client when a server auth response arrives (code, message).
        /// Fires for both accept and reject. The core wrapper maps the code via
        /// <see cref="MapToConnectResult"/>.</summary>
        public event Action<byte, string> OnClientAuthResponse;

        // ---------------------------------------------------------------------
        // Protocol version source.
        // ---------------------------------------------------------------------

        /// <summary>
        /// The protocol version this build speaks. Both host and client read the same value, so two
        /// builds from the same source agree and a mismatched build is rejected.
        ///
        /// INTEGRATION: this references <c>Online.Net.Core.CloSimProtocol.Version</c>, defined by the
        /// core netcode agent under Assets/Scripts/Online/Net/Core/. It is assumed to be an
        /// <see cref="int"/> constant. If the core defines it with a different name or type (e.g. a
        /// string build id), reconcile by changing this ONE property — it is the only reference.
        /// Until the core file lands in this branch, this symbol is intentionally unresolved (the whole
        /// module also depends on Mirror, which the core agent adds), and resolves on integration.
        /// </summary>
        protected virtual int ProtocolVersion => Online.Net.Core.CloSimProtocol.Version;

        // ---------------------------------------------------------------------
        // Auth handshake messages.
        // ---------------------------------------------------------------------

        /// <summary>Client -> server: the credential + version presented at connect time.
        /// Kept intentionally tiny and versioned (architecture.md §"Risks / watch-outs").</summary>
        public struct AuthRequestMessage : NetworkMessage
        {
            public string token;
            public int protocolVersion;
        }

        /// <summary>Server -> client: accept/reject verdict with a machine code + reason string.</summary>
        public struct AuthResponseMessage : NetworkMessage
        {
            public byte code;
            public string message;
        }

        // =====================================================================
        // Server side
        // =====================================================================

        public override void OnStartServer()
        {
            // requireAuthentication:false — this handler IS the authentication step, so the
            // connection is not yet authenticated when it arrives.
            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequestMessage, false);
        }

        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<AuthRequestMessage>();
        }

        /// <summary>Called by Mirror when a client connects. We do nothing here and instead wait for the
        /// client's <see cref="AuthRequestMessage"/> (request/response pattern).</summary>
        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            // Intentionally empty — decision happens in OnAuthRequestMessage.
        }

        void OnAuthRequestMessage(NetworkConnectionToClient conn, AuthRequestMessage msg)
        {
            // Guard against a duplicate/late request from an already-decided connection.
            if (conn.isAuthenticated)
                return;

            // 1) Protocol/version compatibility — checked first so an outdated build never gets a
            //    misleading "wrong password" result.
            int serverVersion = ProtocolVersion;
            if (msg.protocolVersion != serverVersion)
            {
                RejectConnection(conn, CodeVersionMismatch,
                    $"Version mismatch (server {serverVersion}, client {msg.protocolVersion}).");
                return;
            }

            // 2) Join token / password.
            if (!TokenMatches(msg.token))
            {
                RejectConnection(conn, CodeBadToken, "Invalid join token / password.");
                return;
            }

            // Accept.
            if (logAuthDecisions)
                Debug.Log($"[CloSimAuth] Accept conn {conn.connectionId} (v{msg.protocolVersion}).");

            conn.Send(new AuthResponseMessage { code = CodeSuccess, message = "Success" });
            ServerAccept(conn);
        }

        /// <summary>Token policy: blank expected token => public room, accept any token; otherwise the
        /// client token must match exactly (ordinal, case-sensitive).</summary>
        bool TokenMatches(string clientToken)
        {
            if (string.IsNullOrWhiteSpace(ExpectedToken))
                return true; // public / un-gated room

            return string.Equals(ExpectedToken, clientToken ?? string.Empty, StringComparison.Ordinal);
        }

        void RejectConnection(NetworkConnectionToClient conn, byte code, string reason)
        {
            if (logAuthDecisions)
                Debug.Log($"[CloSimAuth] Reject conn {conn.connectionId}: {code} {reason}");

            // Tell the client why, then disconnect after a short grace period so the message is
            // actually delivered before the transport tears the connection down.
            conn.Send(new AuthResponseMessage { code = code, message = reason });
            conn.isAuthenticated = false;
            StartCoroutine(DelayedReject(conn));
        }

        IEnumerator DelayedReject(NetworkConnectionToClient conn)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, rejectDisconnectDelay));

            // Only reject if the connection still exists (the client may have already dropped).
            if (conn != null)
                ServerReject(conn);
        }

        // =====================================================================
        // Client side
        // =====================================================================

        public override void OnStartClient()
        {
            LastClientResponseCode = 0;
            LastClientResponseMessage = null;
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponseMessage, false);
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<AuthResponseMessage>();
        }

        /// <summary>Called by Mirror on the client once connected; we send our credential + version.</summary>
        public override void OnClientAuthenticate()
        {
            NetworkClient.Send(new AuthRequestMessage
            {
                token = ClientToken ?? string.Empty,
                protocolVersion = ProtocolVersion
            });
        }

        void OnAuthResponseMessage(AuthResponseMessage msg)
        {
            LastClientResponseCode = msg.code;
            LastClientResponseMessage = msg.message;
            OnClientAuthResponse?.Invoke(msg.code, msg.message);

            if (msg.code == CodeSuccess)
            {
                if (logAuthDecisions)
                    Debug.Log("[CloSimAuth] Server accepted this client.");
                ClientAccept();
            }
            else
            {
                if (logAuthDecisions)
                    Debug.Log($"[CloSimAuth] Server rejected this client: {msg.code} {msg.message}");
                ClientReject();
            }
        }

        // =====================================================================
        // Configuration helpers + result mapping (called by the core agent)
        // =====================================================================

        /// <summary>Configure this authenticator on the host. Pass <c>HostStartOptions.joinToken</c>
        /// ("" / null => public, un-gated room).</summary>
        public void ConfigureAsHost(string expectedToken)
        {
            ExpectedToken = expectedToken ?? string.Empty;
        }

        /// <summary>Configure this authenticator on the client. Pass <c>ConnectEndpoint.joinToken</c>
        /// ("" / null when joining a public room).</summary>
        public void ConfigureAsClient(string token)
        {
            ClientToken = token ?? string.Empty;
        }

        /// <summary>Map an auth response code to the UI-facing <see cref="ConnectResult"/>. The core
        /// connection wrapper uses this when raising <c>IOnlineConnection.OnClientConnected</c>.</summary>
        public static ConnectResult MapToConnectResult(byte code)
        {
            switch (code)
            {
                case CodeSuccess:         return ConnectResult.Success;
                case CodeBadToken:        return ConnectResult.Rejected_BadToken;
                case CodeVersionMismatch: return ConnectResult.Rejected_Version;
                default:                  return ConnectResult.TransportError;
            }
        }
    }
}
