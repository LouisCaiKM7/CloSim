// CloSim Online Multiplayer — client-side master-server HTTP client (A2 Netcode Foundation).
// Concrete implementation of Online.Contracts.IMasterServerClient (architecture.md §6.3).
//
// HARD CONSTRAINTS honored here:
//  * The ONLY caller is the game client (CloSim.exe). The master server has NO web UI — this is
//    plain HTTP under the hood, but every request carries a client credential so the directory
//    is game-client-only, not a casually browsable public web endpoint.
//  * CONFIG IS BLANK. MasterServerUrl / ClientApiKey default to "" (the user supplies them later).
//    While the URL is blank, IsConfigured == false, NO network calls are made, and every operation
//    degrades gracefully (empty list / no-op success) and NEVER throws — so the game runs fine
//    before the AWS endpoint exists.
//  * Additive only. This file adds behavior; it does not modify the contract or any game content.
//
// Auth: the client API key is sent on every request as an HTTP header (X-Api-Key).
// JSON: Unity's JsonUtility (com.unity.modules.jsonserialize). No third-party JSON dependency.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Online.Contracts;

namespace Online.Net.MasterClient
{
    /// <summary>
    /// In-game HTTP client for the AWS master directory. Implements every member of
    /// <see cref="IMasterServerClient"/> using <see cref="UnityWebRequest"/>.
    ///
    /// Operations (per architecture.md §4.1 / §6.3):
    ///   host  → RegisterAsync / HeartbeatAsync / DeregisterAsync a PUBLIC room,
    ///   client→ ListRoomsAsync (browse the in-game Server List, filtered by RoomQuery).
    ///
    /// Note: join-token issuance/validation is NOT part of this contract. Token gating happens in
    /// the Mirror auth handshake (private and public-with-token rooms), which is a separate A2
    /// component — RoomInfo only ever exposes `requiresToken`, never the token itself.
    /// </summary>
    public sealed class MasterServerClient : IMasterServerClient
    {
        // --- CONFIG IS BLANK ------------------------------------------------------------------
        // The user provides these later (AWS endpoint + client credential). NEVER commit real values.
        private readonly string MasterServerUrl = ""; // TODO: user provides hosting endpoint
        private readonly string ClientApiKey    = ""; // TODO: user provides

        // --- Constants ------------------------------------------------------------------------
        private const string ApiKeyHeader = "X-Api-Key"; // client credential gates the directory API
        private const int RequestTimeoutSeconds = 10;    // per-request ceiling; keeps the UI responsive

        /// <summary>
        /// Default constructor: config stays blank (production default — user supplies the endpoint
        /// later via a configured instance, and until then the client no-ops gracefully).
        /// </summary>
        public MasterServerClient()
        {
        }

        /// <summary>
        /// Optional constructor for LOCAL TESTING ONLY (e.g. point at A5's local backend in the
        /// editor). Never commit a non-empty endpoint. Passing null leaves the blank default.
        /// </summary>
        public MasterServerClient(string masterServerUrl, string clientApiKey = "")
        {
            if (masterServerUrl != null) MasterServerUrl = masterServerUrl;
            if (clientApiKey != null) ClientApiKey = clientApiKey;
        }

        /// <inheritdoc/>
        /// <remarks>False while MasterServerUrl is blank — no network calls are made in that state.</remarks>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(MasterServerUrl);

        // --- Host side (public rooms only) ----------------------------------------------------

        /// <inheritdoc/>
        public async Task<RegisterResult> RegisterAsync(RoomInfo room)
        {
            // Not configured → no-op success. Hosting still works locally (Mirror listen-server);
            // the room simply isn't listed until the user supplies the directory endpoint.
            if (!IsConfigured)
                return new RegisterResult { ok = true, roomId = room.roomId, error = "" };

            try
            {
                string json = JsonUtility.ToJson(room);
                using UnityWebRequest req = MakePostJson(BuildUrl("rooms"), json);
                await UnityWebRequestAwaiter.SendAsync(req);

                if (!IsSuccess(req))
                    return new RegisterResult { ok = false, roomId = room.roomId, error = DescribeError(req) };

                RegisterResponseDto dto = ParseJson<RegisterResponseDto>(req.downloadHandler?.text);
                if (dto == null) // 200 with empty/unparseable body → treat as success, keep our id
                    return new RegisterResult { ok = true, roomId = room.roomId, error = "" };

                return new RegisterResult
                {
                    ok = dto.ok,
                    roomId = string.IsNullOrEmpty(dto.roomId) ? room.roomId : dto.roomId,
                    error = dto.error ?? ""
                };
            }
            catch (Exception e)
            {
                // Never throw to the caller — surface as a failed-but-safe result.
                return new RegisterResult { ok = false, roomId = room.roomId, error = e.Message };
            }
        }

        /// <inheritdoc/>
        public async Task HeartbeatAsync(string roomId)
        {
            if (!IsConfigured || string.IsNullOrEmpty(roomId))
                return; // no-op

            try
            {
                using UnityWebRequest req = MakePostJson(
                    BuildUrl($"rooms/{Uri.EscapeDataString(roomId)}/heartbeat"), "{}");
                await UnityWebRequestAwaiter.SendAsync(req);
                // Best-effort: a missed heartbeat just lets the room's TTL lapse server-side.
            }
            catch
            {
                // Swallow — heartbeat must never throw into the host's update loop.
            }
        }

        /// <inheritdoc/>
        public async Task DeregisterAsync(string roomId)
        {
            if (!IsConfigured || string.IsNullOrEmpty(roomId))
                return; // no-op

            try
            {
                using UnityWebRequest req = MakeDelete(
                    BuildUrl($"rooms/{Uri.EscapeDataString(roomId)}"));
                await UnityWebRequestAwaiter.SendAsync(req);
                // Best-effort: even if this fails, the room drops off via TTL after missed heartbeats.
            }
            catch
            {
                // Swallow — deregister is a courtesy cleanup, not a correctness requirement.
            }
        }

        // --- Client side (in-game Server List) ------------------------------------------------

        /// <inheritdoc/>
        public async Task<IReadOnlyList<RoomInfo>> ListRoomsAsync(RoomQuery query)
        {
            // Not configured → empty list. The Server List UI shows "master server not configured".
            if (!IsConfigured)
                return Array.Empty<RoomInfo>();

            try
            {
                using UnityWebRequest req = MakeGet(BuildListUrl(query));
                await UnityWebRequestAwaiter.SendAsync(req);

                if (!IsSuccess(req))
                    return Array.Empty<RoomInfo>();

                RoomInfo[] rooms = ParseRoomList(req.downloadHandler?.text);
                return rooms ?? Array.Empty<RoomInfo>();
            }
            catch
            {
                // Any transport/parse failure degrades to an empty list — the UI never breaks.
                return Array.Empty<RoomInfo>();
            }
        }

        // --- Request construction -------------------------------------------------------------

        private UnityWebRequest MakePostJson(string url, string json)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json ?? "")),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyCommonHeaders(req);
            return req;
        }

        private UnityWebRequest MakeGet(string url)
        {
            UnityWebRequest req = UnityWebRequest.Get(url); // includes a DownloadHandlerBuffer
            ApplyCommonHeaders(req);
            return req;
        }

        private UnityWebRequest MakeDelete(string url)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbDELETE)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };
            ApplyCommonHeaders(req);
            return req;
        }

        /// <summary>Applies timeout, the client API-key credential, and Accept to every request.</summary>
        private void ApplyCommonHeaders(UnityWebRequest req)
        {
            req.timeout = RequestTimeoutSeconds;
            req.SetRequestHeader("Accept", "application/json");

            // Gate the directory API: the credential marks this as a game-client request.
            // Blank while unconfigured — we simply omit the header rather than send an empty one.
            if (!string.IsNullOrEmpty(ClientApiKey))
                req.SetRequestHeader(ApiKeyHeader, ClientApiKey);
        }

        // --- URL building ---------------------------------------------------------------------

        private string BuildUrl(string path)
        {
            string baseUrl = MasterServerUrl.TrimEnd('/');
            return baseUrl + "/" + path.TrimStart('/');
        }

        private string BuildListUrl(RoomQuery query)
        {
            var pairs = new List<string>(5);

            if (!string.IsNullOrEmpty(query.gameId))
                pairs.Add("gameId=" + Uri.EscapeDataString(query.gameId));
            if (!string.IsNullOrEmpty(query.region))
                pairs.Add("region=" + Uri.EscapeDataString(query.region));
            if (query.hideFull)
                pairs.Add("hideFull=true");

            // Always send hidePrivate explicitly (private rooms are never registered anyway).
            pairs.Add("hidePrivate=" + (query.hidePrivate ? "true" : "false"));

            // NOTE: query.version is intentionally NOT sent as a filter. Resolved decision:
            // version-mismatched rooms MUST still be returned so the Server List can grey them out.
            // This client therefore never filters by version — neither server-side nor locally.

            var sb = new StringBuilder(BuildUrl("rooms"));
            if (pairs.Count > 0)
            {
                sb.Append('?');
                sb.Append(string.Join("&", pairs));
            }
            return sb.ToString();
        }

        // --- Parsing / result helpers ---------------------------------------------------------

        private static bool IsSuccess(UnityWebRequest req)
            => req.result == UnityWebRequest.Result.Success;

        private static string DescribeError(UnityWebRequest req)
            => $"HTTP {req.responseCode}: {req.error}";

        private static T ParseJson<T>(string text) where T : class
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try
            {
                return JsonUtility.FromJson<T>(text);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parses a room-list body into RoomInfo[]. Accepts both a wrapped object
        /// { "rooms": [ ... ] } and a bare top-level array [ ... ] (JsonUtility can't parse the
        /// latter directly, so it's wrapped first).
        /// </summary>
        private static RoomInfo[] ParseRoomList(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<RoomInfo>();

            string trimmed = text.TrimStart();
            string wrapped = trimmed.StartsWith("[", StringComparison.Ordinal)
                ? "{\"rooms\":" + text + "}"
                : text;

            RoomListResponseDto dto = ParseJson<RoomListResponseDto>(wrapped);
            return dto?.rooms ?? Array.Empty<RoomInfo>();
        }
    }
}
