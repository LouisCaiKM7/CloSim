// CloSim Online Multiplayer — replay backend client contract (stub).
// SOURCE OF TRUTH: Documentation/online/architecture.md "Replay System".
// Owned by A1 (Architect). HTTP CLIENT implemented alongside the netcode layer; BACKEND by A5.
//
// HARD CONSTRAINTS (mirror MasterServerClient):
//  * The ONLY caller is the game client (CloSim.exe). The replay backend has NO web UI.
//  * CONFIG IS BLANK. ReplayServiceConfig.BaseUrl defaults to "" — the user provides it later.
//    While blank, IsConfigured == false and every call is a graceful no-op (returns a not-configured
//    result; never throws, never blocks a match).
//  * The blob is OPAQUE to the backend. Upload/download of bytes uses PRESIGNED S3 URLs; only metadata
//    (small JSON) touches the directory API.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Online.Contracts.Replay
{
    /// <summary>
    /// In-game client for the replay backend. Uploads a finished match blob (presigned PUT), lists a
    /// user's replays, and fetches a single replay's metadata + blob for local playback reconstruction.
    /// </summary>
    public interface IReplayService
    {
        /// <summary>False while ReplayServiceConfig.BaseUrl == "" — UI shows "replays not configured".</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Upload a finished match. Flow: POST metadata to the backend -> receive { replayId, uploadUrl }
        /// (a presigned S3 PUT) -> PUT the opaque blob to uploadUrl -> POST complete. Returns the
        /// backend-assigned replayId. No-op (ok=false, error="not configured") when IsConfigured is false.
        /// </summary>
        Task<ReplayUploadResult> UploadAsync(ReplayMetadata metadata, byte[] blob);

        /// <summary>List replays matching the query (metadata only — no blobs). Empty when not configured.</summary>
        Task<IReadOnlyList<ReplayMetadata>> ListAsync(ReplayQuery query);

        /// <summary>
        /// Fetch one replay: GET metadata + a presigned download URL, then GET the blob bytes. The result
        /// carries both the metadata and the blob (and the URL for callers that stream it themselves).
        /// </summary>
        Task<ReplayFetchResult> GetAsync(string replayId);
    }

    /// <summary>Result of UploadAsync. blobUrl is the stored object URL (informational).</summary>
    public struct ReplayUploadResult
    {
        public bool ok;
        public string replayId;
        public string blobUrl;
        public string error;
    }

    /// <summary>Result of GetAsync: metadata + downloaded blob (blob may be null if only the URL is wanted).</summary>
    public struct ReplayFetchResult
    {
        public bool ok;
        public ReplayMetadata metadata;
        public byte[] blob;
        public string blobUrl;
        public string error;
    }

    /// <summary>Filter for ListAsync. Empty string fields mean "no filter".</summary>
    public struct ReplayQuery
    {
        public string userId;   // "" => any / caller's own, per backend policy
        public string gameId;   // "" => all
        public string mode;     // "" => all (e.g. "2v2"); free-form label
        public int limit;       // 0 => backend default page size
        public string cursor;   // "" => first page; opaque pagination token
    }

    /// <summary>
    /// Config holder. Concrete values are supplied by the user later — NEVER hardcode a real endpoint/key.
    /// Mirrors MasterServerConfig so the two directory clients degrade identically when unconfigured.
    /// </summary>
    public static class ReplayServiceConfig
    {
        public const string BaseUrl = ""; // TODO: user provides replay backend endpoint (AWS)
        public const string ApiKey = "";  // TODO: user provides client credential (gates the replay API)

        /// <summary>Upload blobs larger than this only if the backend permits; recorder should warn earlier.</summary>
        public const int MaxUploadBytes = 16 * 1024 * 1024; // 16 MB

        /// <summary>True once the user fills BaseUrl. While false, IReplayService calls are no-ops.</summary>
        public static bool IsConfigured => !string.IsNullOrEmpty(BaseUrl);
    }
}
