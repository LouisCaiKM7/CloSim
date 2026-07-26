// CloSim Online Multiplayer — replay backend HTTP wire helpers (client-side).
// Namespace: Online.Replay.Service. Additive only; consumes Online.Contracts.Replay, changes nothing.
//
// This file holds the *internal* JSON DTOs used on the wire and a tiny helper that adapts
// UnityWebRequest's async operation to a Task, so ReplayServiceClient can `await` requests
// without a MonoBehaviour/coroutine. It deliberately mirrors Online.Net.MasterClient's
// MasterServerWire.cs (a self-contained copy — the sibling file is NOT edited or referenced).
//
// Why separate DTOs instead of the contract types?
//  * JsonUtility can only (de)serialize types marked [System.Serializable]. ReplayMetadata /
//    ReplayMode / ReplayScore ARE serializable, so they ride the wire directly (as RoomInfo does
//    in MasterServerWire). But the backend's RESPONSE ENVELOPES ({ ok, replayId, upload:{...} },
//    { ok, replay, download:{...} }, { replays:[...] }) have no contract counterpart, and the
//    contract's ReplayUploadResult / ReplayFetchResult / ReplayQuery structs are NOT [Serializable].
//    So we keep private serializable mirrors for the envelopes and map to/from the contract types
//    in ReplayServiceClient. This also decouples the wire format (A5's backend) from the contract
//    types (A1).

using System;
using System.Threading.Tasks;
using UnityEngine.Networking;
using Online.Contracts.Replay;

namespace Online.Replay.Service
{
    /// <summary>
    /// Presigned-URL instruction returned inside a POST /replays response (<c>upload</c>) — a
    /// short-lived S3 PUT target. The blob is streamed straight to <see cref="url"/>; no
    /// <c>X-Api-Key</c> is sent to it (the URL is self-signed).
    /// </summary>
    [Serializable]
    internal sealed class UploadInstructionDto
    {
        public string url;          // presigned S3 PUT URL
        public string method;       // "PUT" (informational; we always PUT)
        public int expiresInSec;    // TTL of the signature (informational)
        // NOTE: the backend also sends headers:{ "Content-Type":"application/octet-stream" }, but
        // JsonUtility cannot bind a hyphenated key to a C# field, and the value is fixed by contract,
        // so ReplayServiceClient applies the Content-Type constant directly instead of parsing it.
    }

    /// <summary>
    /// Presigned-URL instruction returned inside a GET /replays/{id} response (<c>download</c>) — a
    /// short-lived S3 GET source for the opaque blob bytes.
    /// </summary>
    [Serializable]
    internal sealed class DownloadInstructionDto
    {
        public string url;          // presigned S3 GET URL
        public string method;       // "GET" (informational)
        public int expiresInSec;    // TTL of the signature (informational)
    }

    /// <summary>
    /// Server response for POST /replays (create metadata record + issue an upload URL).
    /// Shape: { ok, replayId, upload:{ url, method, headers, expiresInSec } }.
    /// </summary>
    [Serializable]
    internal sealed class PostReplayResponseDto
    {
        public bool ok;
        public string replayId;             // backend-assigned id
        public UploadInstructionDto upload; // presigned PUT target
    }

    /// <summary>
    /// Server response for GET /replays/{id} (metadata + a presigned download URL).
    /// Shape: { ok, replay:&lt;ReplayMetadata&gt;, download:{ url, method, expiresInSec } }.
    /// <see cref="ReplayMetadata"/> is [Serializable] in the contract, so it binds directly.
    /// </summary>
    [Serializable]
    internal sealed class GetReplayResponseDto
    {
        public bool ok;
        public ReplayMetadata replay;           // the stored metadata record
        public DownloadInstructionDto download; // presigned GET source
    }

    /// <summary>
    /// Wrapper for GET /replays (list). Shape: { replays:[ &lt;ReplayMetadata&gt;... ], cursor }.
    /// JsonUtility cannot deserialize a bare top-level array, so a bare "[ ... ]" body is wrapped
    /// into { "replays": [ ... ] } before parsing. <see cref="ReplayMetadata"/> is [Serializable].
    /// </summary>
    [Serializable]
    internal sealed class ListReplayResponseDto
    {
        public ReplayMetadata[] replays;
        public string cursor;   // opaque next-page token ("" / absent on last page)
    }

    /// <summary>
    /// Adapts <see cref="UnityWebRequest.SendWebRequest"/> (a UnityWebRequestAsyncOperation)
    /// to a <see cref="Task"/>. Must be called on Unity's main thread (UnityWebRequest requirement);
    /// the completion callback also resumes on the main thread, which is what game code expects.
    /// Self-contained copy of the same helper in MasterServerWire.cs (that file is not shared).
    /// </summary>
    internal static class UnityWebRequestAwaiter
    {
        public static Task<UnityWebRequest> SendAsync(UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<UnityWebRequest>();
            try
            {
                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                if (op.isDone)
                {
                    tcs.TrySetResult(request);
                }
                else
                {
                    op.completed += _ => tcs.TrySetResult(request);
                }
            }
            catch (Exception e)
            {
                // Never surface a transport-setup exception raw — ReplayServiceClient turns a faulted
                // task into graceful degradation, but we complete cleanly here too.
                tcs.TrySetException(e);
            }

            return tcs.Task;
        }
    }
}
