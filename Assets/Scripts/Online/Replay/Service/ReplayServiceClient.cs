// CloSim Online Multiplayer — client-side replay backend HTTP client (Replay Service).
// Concrete implementation of Online.Contracts.Replay.IReplayService (architecture.md §11.4/§11.5).
//
// HARD CONSTRAINTS honored here (mirror MasterServerClient exactly):
//  * The ONLY caller is the game client (CloSim.exe). The replay backend has NO web UI — plain HTTP
//    under the hood, but every /replays request carries a client credential (X-Api-Key) so the
//    directory is game-client-only. The opaque blob itself moves over PRESIGNED S3 URLs (storage,
//    not a browsable API), and those URL-signed transfers deliberately send NO X-Api-Key.
//  * CONFIG IS BLANK. BaseUrl / ApiKey default to ReplayServiceConfig's "" (the user supplies them
//    later, once the AWS endpoint exists). While the URL is blank, IsConfigured == false, NO network
//    calls are made, and every operation degrades gracefully (empty list / not-configured result) and
//    NEVER throws — so the game runs fine before the backend exists.
//  * Additive only. This file adds behavior; it does not modify the contract or any game content.
//
// Two-step transfers (blob never transits the API host):
//   Upload:   POST /replays (metadata JSON, X-Api-Key)  ->  { replayId, upload:{ url,... } }
//             PUT  <upload.url> (raw bytes, Content-Type: application/octet-stream, NO X-Api-Key)
//   Download: GET  /replays/{id} (X-Api-Key)            ->  { replay, download:{ url,... } }
//             GET  <download.url> (NO X-Api-Key)         ->  opaque blob bytes
//
// JSON: Unity's JsonUtility (com.unity.modules.jsonserialize). No third-party JSON dependency.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Online.Contracts.Replay;

namespace Online.Replay.Service
{
    /// <summary>
    /// In-game HTTP client for the AWS replay backend. Implements every member of
    /// <see cref="IReplayService"/> using <see cref="UnityWebRequest"/>, in the same style as
    /// <c>Online.Net.MasterClient.MasterServerClient</c>.
    ///
    /// Operations (architecture.md §11.5):
    ///   UploadAsync → POST metadata, then PUT the opaque blob to a presigned S3 URL,
    ///   ListAsync   → GET a user's replays (metadata only),
    ///   GetAsync    → GET metadata + a presigned download URL, then GET the blob bytes.
    ///
    /// Note: the contract's <see cref="IReplayService"/> declares no Delete member, so none is
    /// implemented here (the backend's owner-gated DELETE /replays/{id} is unused by this client).
    /// </summary>
    public sealed class ReplayServiceClient : IReplayService
    {
        // --- CONFIG IS BLANK ------------------------------------------------------------------
        // Defaults come from the contract's ReplayServiceConfig (both "" today). The user provides
        // real values later (AWS endpoint + client credential). NEVER commit real values.
        private readonly string BaseUrl = ReplayServiceConfig.BaseUrl; // TODO: user provides endpoint
        private readonly string ApiKey  = ReplayServiceConfig.ApiKey;  // TODO: user provides credential

        // --- Constants ------------------------------------------------------------------------
        private const string ApiKeyHeader = "X-Api-Key";                 // client credential gates the API
        private const string BlobContentType = "application/octet-stream"; // presigned PUT/GET body type
        private const int ApiTimeoutSeconds = 10;   // metadata calls — keep the UI responsive
        private const int BlobTimeoutSeconds = 30;  // presigned S3 transfers — blobs are up to a few MB

        /// <summary>
        /// Default constructor: config stays blank (production default — user supplies the endpoint
        /// later via a configured instance, and until then the client no-ops gracefully).
        /// </summary>
        public ReplayServiceClient()
        {
        }

        /// <summary>
        /// Optional constructor for LOCAL TESTING ONLY (e.g. point at A5's local backend in the
        /// editor). Never commit a non-empty endpoint. Passing null leaves the blank default.
        /// </summary>
        public ReplayServiceClient(string baseUrl, string apiKey = "")
        {
            if (baseUrl != null) BaseUrl = baseUrl;
            if (apiKey != null) ApiKey = apiKey;
        }

        /// <inheritdoc/>
        /// <remarks>False while BaseUrl is blank — no network calls are made in that state.</remarks>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

        // --- Upload (POST metadata -> PUT blob) -----------------------------------------------

        /// <inheritdoc/>
        public async Task<ReplayUploadResult> UploadAsync(ReplayMetadata metadata, byte[] blob)
        {
            // Not configured → graceful no-op. Per IReplayService's contract doc the upload no-op
            // reports ok=false / error="not configured" (an upload that did NOT happen must never
            // masquerade as ok=true), yet it still never throws and never blocks the match.
            if (!IsConfigured)
                return NotConfiguredUpload(metadata.replayId);

            blob ??= Array.Empty<byte>();

            try
            {
                // Step 1: POST the metadata JSON, receive the backend id + a presigned PUT URL.
                string json = JsonUtility.ToJson(metadata);
                PostReplayResponseDto post;
                using (UnityWebRequest req = MakePostJson(BuildUrl("replays"), json))
                {
                    await UnityWebRequestAwaiter.SendAsync(req);
                    if (!IsSuccess(req))
                        return FailUpload(metadata.replayId, DescribeError(req));
                    post = ParseJson<PostReplayResponseDto>(req.downloadHandler?.text);
                }

                if (post == null || post.upload == null || string.IsNullOrEmpty(post.upload.url))
                    return FailUpload(metadata.replayId, "upload url missing in POST /replays response");

                string replayId = string.IsNullOrEmpty(post.replayId) ? metadata.replayId : post.replayId;

                // Step 2: PUT the opaque blob straight to S3 (no X-Api-Key — the URL is self-signed).
                using (UnityWebRequest put = MakePutBlob(post.upload.url, blob))
                {
                    await UnityWebRequestAwaiter.SendAsync(put);
                    if (!IsSuccess(put))
                        return FailUpload(replayId, "blob PUT failed: " + DescribeError(put));
                }

                return new ReplayUploadResult
                {
                    ok = true,
                    replayId = replayId,
                    blobUrl = post.upload.url,
                    error = ""
                };
            }
            catch (Exception e)
            {
                // Never throw to the caller — surface as a failed-but-safe result.
                return FailUpload(metadata.replayId, e.Message);
            }
        }

        // --- List (GET metadata page) ---------------------------------------------------------

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ReplayMetadata>> ListAsync(ReplayQuery query)
        {
            // Not configured → empty list. The replay-list UI shows "replays not configured".
            if (!IsConfigured)
                return Array.Empty<ReplayMetadata>();

            try
            {
                using UnityWebRequest req = MakeGet(BuildListUrl(query));
                await UnityWebRequestAwaiter.SendAsync(req);

                if (!IsSuccess(req))
                    return Array.Empty<ReplayMetadata>();

                ReplayMetadata[] replays = ParseReplayList(req.downloadHandler?.text);
                return replays ?? Array.Empty<ReplayMetadata>();
            }
            catch
            {
                // Any transport/parse failure degrades to an empty list — the UI never breaks.
                return Array.Empty<ReplayMetadata>();
            }
        }

        // --- Get (GET metadata+URL -> GET blob) -----------------------------------------------

        /// <inheritdoc/>
        public async Task<ReplayFetchResult> GetAsync(string replayId)
        {
            if (!IsConfigured)
                return new ReplayFetchResult { ok = false, error = "not configured" };

            if (string.IsNullOrEmpty(replayId))
                return new ReplayFetchResult { ok = false, error = "replayId is empty" };

            try
            {
                // Step 1: GET the metadata record + a presigned download URL.
                GetReplayResponseDto get;
                using (UnityWebRequest req = MakeGet(BuildUrl($"replays/{Uri.EscapeDataString(replayId)}")))
                {
                    await UnityWebRequestAwaiter.SendAsync(req);
                    if (!IsSuccess(req))
                        return new ReplayFetchResult { ok = false, error = DescribeError(req) };
                    get = ParseJson<GetReplayResponseDto>(req.downloadHandler?.text);
                }

                if (get == null)
                    return new ReplayFetchResult { ok = false, error = "empty GET /replays/{id} response" };

                string downloadUrl = get.download?.url;
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    // Metadata is available but no blob URL — return what we have without the bytes.
                    return new ReplayFetchResult
                    {
                        ok = false,
                        metadata = get.replay,
                        blob = null,
                        blobUrl = "",
                        error = "download url missing in GET /replays/{id} response"
                    };
                }

                // Step 2: GET the opaque blob bytes from S3 (no X-Api-Key — the URL is self-signed).
                using (UnityWebRequest dl = MakeGetBlob(downloadUrl))
                {
                    await UnityWebRequestAwaiter.SendAsync(dl);
                    if (!IsSuccess(dl))
                        return new ReplayFetchResult
                        {
                            ok = false,
                            metadata = get.replay,
                            blob = null,
                            blobUrl = downloadUrl,
                            error = "blob GET failed: " + DescribeError(dl)
                        };

                    return new ReplayFetchResult
                    {
                        ok = true,
                        metadata = get.replay,
                        blob = dl.downloadHandler?.data ?? Array.Empty<byte>(),
                        blobUrl = downloadUrl,
                        error = ""
                    };
                }
            }
            catch (Exception e)
            {
                return new ReplayFetchResult { ok = false, error = e.Message };
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
            ApplyApiHeaders(req);
            return req;
        }

        private UnityWebRequest MakeGet(string url)
        {
            UnityWebRequest req = UnityWebRequest.Get(url); // includes a DownloadHandlerBuffer
            ApplyApiHeaders(req);
            return req;
        }

        /// <summary>PUT the raw opaque blob to a presigned S3 URL. NO X-Api-Key (URL is self-signed).</summary>
        private UnityWebRequest MakePutBlob(string presignedUrl, byte[] blob)
        {
            var req = new UnityWebRequest(presignedUrl, UnityWebRequest.kHttpVerbPUT)
            {
                uploadHandler = new UploadHandlerRaw(blob ?? Array.Empty<byte>()),
                downloadHandler = new DownloadHandlerBuffer()
            };
            // The presigned signature covers this exact Content-Type — must match the backend's grant.
            req.SetRequestHeader("Content-Type", BlobContentType);
            req.timeout = BlobTimeoutSeconds;
            return req;
        }

        /// <summary>GET the raw opaque blob from a presigned S3 URL. NO X-Api-Key (URL is self-signed).</summary>
        private UnityWebRequest MakeGetBlob(string presignedUrl)
        {
            UnityWebRequest req = UnityWebRequest.Get(presignedUrl); // DownloadHandlerBuffer, raw bytes
            req.timeout = BlobTimeoutSeconds;
            return req;
        }

        /// <summary>Applies timeout, Accept, and the client API-key credential to a /replays API request.</summary>
        private void ApplyApiHeaders(UnityWebRequest req)
        {
            req.timeout = ApiTimeoutSeconds;
            req.SetRequestHeader("Accept", "application/json");

            // Gate the directory API: the credential marks this as a game-client request.
            // Blank while unconfigured — we omit the header rather than send an empty one.
            if (!string.IsNullOrEmpty(ApiKey))
                req.SetRequestHeader(ApiKeyHeader, ApiKey);
        }

        // --- URL building ---------------------------------------------------------------------

        private string BuildUrl(string path)
        {
            string baseUrl = BaseUrl.TrimEnd('/');
            return baseUrl + "/" + path.TrimStart('/');
        }

        private string BuildListUrl(ReplayQuery query)
        {
            var pairs = new List<string>(5);

            if (!string.IsNullOrEmpty(query.userId))
                pairs.Add("userId=" + Uri.EscapeDataString(query.userId));
            if (!string.IsNullOrEmpty(query.gameId))
                pairs.Add("gameId=" + Uri.EscapeDataString(query.gameId));
            if (!string.IsNullOrEmpty(query.mode))
                pairs.Add("mode=" + Uri.EscapeDataString(query.mode));
            if (query.limit > 0)
                pairs.Add("limit=" + query.limit);
            if (!string.IsNullOrEmpty(query.cursor))
                pairs.Add("cursor=" + Uri.EscapeDataString(query.cursor));

            var sb = new StringBuilder(BuildUrl("replays"));
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
        /// Parses a replay-list body into ReplayMetadata[]. Accepts both the wrapped object
        /// { "replays": [ ... ] } and a bare top-level array [ ... ] (JsonUtility cannot parse the
        /// latter directly, so it is wrapped first). Newest-first ordering is preserved as sent.
        /// </summary>
        private static ReplayMetadata[] ParseReplayList(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<ReplayMetadata>();

            string trimmed = text.TrimStart();
            string wrapped = trimmed.StartsWith("[", StringComparison.Ordinal)
                ? "{\"replays\":" + text + "}"
                : text;

            ListReplayResponseDto dto = ParseJson<ListReplayResponseDto>(wrapped);
            return dto?.replays ?? Array.Empty<ReplayMetadata>();
        }

        private static ReplayUploadResult NotConfiguredUpload(string replayId)
            => new ReplayUploadResult { ok = false, replayId = replayId, blobUrl = "", error = "not configured" };

        private static ReplayUploadResult FailUpload(string replayId, string error)
            => new ReplayUploadResult { ok = false, replayId = replayId, blobUrl = "", error = error };
    }
}
