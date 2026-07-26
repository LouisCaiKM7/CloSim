// CloSim Online Multiplayer — master-server HTTP wire helpers (A2, client-side).
// Namespace: Online.Net.MasterClient. Additive only; consumes Online.Contracts, changes nothing.
//
// This file holds the *internal* JSON DTOs used on the wire and a tiny helper that adapts
// UnityWebRequest's async operation to a Task, so MasterServerClient can `await` requests
// without a MonoBehaviour/coroutine.
//
// Why separate DTOs instead of the contract structs?
//  * JsonUtility can only (de)serialize types marked [System.Serializable]. RoomInfo IS
//    serializable, but RegisterResult / RoomQuery in the contract are NOT — so we keep a
//    private, serializable mirror for the register response and the list wrapper, and map
//    to/from the contract types in MasterServerClient. This also decouples the wire format
//    (owned by A5's backend) from the contract types (owned by A1).

using System;
using System.Threading.Tasks;
using UnityEngine.Networking;
using Online.Contracts;

namespace Online.Net.MasterClient
{
    /// <summary>
    /// Server response for POST /rooms (register). Mirrors <see cref="RegisterResult"/> on the wire.
    /// JsonUtility-friendly (marked [Serializable]); the contract struct is not.
    /// </summary>
    [Serializable]
    internal sealed class RegisterResponseDto
    {
        public bool ok;
        public string roomId;
        public string error;
    }

    /// <summary>
    /// Wrapper for GET /rooms (list). JsonUtility cannot deserialize a bare top-level JSON array,
    /// so responses are read as { "rooms": [ ... ] }. A bare "[ ... ]" body is wrapped before parsing.
    /// <see cref="RoomInfo"/> is [Serializable] in the contract, so it deserializes as an array element.
    /// </summary>
    [Serializable]
    internal sealed class RoomListResponseDto
    {
        public RoomInfo[] rooms;
    }

    /// <summary>
    /// Adapts <see cref="UnityWebRequest.SendWebRequest"/> (a UnityWebRequestAsyncOperation)
    /// to a <see cref="Task"/>. Must be called on Unity's main thread (UnityWebRequest requirement);
    /// the completion callback also resumes on the main thread, which is what game code expects.
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
                // Never surface a transport-setup exception to the caller — MasterServerClient
                // turns a faulted task into graceful degradation, but we complete cleanly here too.
                tcs.TrySetException(e);
            }

            return tcs.Task;
        }
    }
}
