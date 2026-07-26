// CloSim Online Multiplayer — game-piece network spawn integration seam (T5, gameplay-sync).
// Namespace: Online.Sync. File under Assets/Scripts/Online/Sync/Pieces/.
//
// WHY THIS EXISTS
//   GamePieceNetworkSync makes an already-networked piece render host-authoritatively. But CloSim
//   pieces are created with plain UnityEngine.Object.Instantiate at several game-content spawn sites
//   (Field.Core.SpawnGamePiece, Robot.Builders.BuildNode) and removed with Destroy
//   (Field.Scoring.ScoreThenDelete). A plain Instantiate on a client makes a LOCAL, non-networked
//   object — so without integration each client would spawn/simulate its OWN pieces and they would
//   diverge from the host.
//
//   For true host authority a networked object must be created ONLY on the server, published with
//   NetworkServer.Spawn (which replicates it to clients), and removed with NetworkServer.Destroy.
//   Mirror cannot intercept an arbitrary Instantiate, so the spawn/despawn SITES must call into this
//   helper. Those sites live in game-content files this task does NOT own; see "REQUIRED CALLER
//   CHANGES" below. This helper implements everything on the Online.Sync side so the caller edits are
//   one-line, behavior-preserving, and trivially reviewable.
//
// OFFLINE / CLIENT SAFETY
//   Every method here is a hard no-op unless a server is active, and the client-suppression guard is
//   false unless a client-only instance is active. Offline (no Mirror host/client) NOTHING changes:
//   pieces Instantiate/Destroy exactly as today. This preserves offline split-screen (golden rule 7).
//
// REQUIRED CALLER CHANGES (game-content files — OUT OF SCOPE for T5; must be done by their owners):
//   1) Field.Core.SpawnGamePiece.SpawnPiece(...)  — after `Instantiate(...).GetComponent<GamePiece>()`
//        HOST:   GamePieceNetworkRegistrar.RegisterSpawned(item.gameObject);
//        CLIENT: guard the local spawn — `if (GamePieceNetworkRegistrar.SuppressLocalSpawn) return;`
//                near the top of SpawnPiece / FixedUpdate so client-only instances do not spawn locally.
//   2) Robot.Builders.BuildNode.SpawnPiece(...)   — same two changes for the robot-intake piece.
//   3) Field.Scoring.ScoreThenDelete (and any other `Destroy(piece.gameObject)`):
//        replace `Destroy(piece.gameObject)` with `GamePieceNetworkRegistrar.Despawn(piece.gameObject);`
//        which destroys via NetworkServer when networked and falls back to Object.Destroy offline.
//   4) The piece prefabs must be registered on CloSimNetworkManager.spawnPrefabs (or via RegisterPrefab)
//        and carry NetworkIdentity + NetworkTransform + GamePieceNetworkSync (see that file's header).

using Mirror;
using UnityEngine;

namespace Online.Sync
{
    /// <summary>
    /// Static seam that bridges CloSim's Instantiate/Destroy game-piece lifecycle to Mirror's
    /// server-authoritative spawn system. All methods are inert offline and on client-only instances
    /// (except <see cref="SuppressLocalSpawn"/>, which tells a client-only instance to skip its local
    /// spawn). Intended to be called from the piece spawn/despawn sites (see file header).
    /// </summary>
    public static class GamePieceNetworkRegistrar
    {
        /// <summary>True when any networked session (host or client) is active.</summary>
        public static bool NetworkActive => NetworkServer.active || NetworkClient.active;

        /// <summary>
        /// True on a CLIENT-ONLY instance: the host owns piece creation, so a pure client must NOT
        /// locally Instantiate pieces. False offline and on the host (which must spawn normally).
        /// </summary>
        public static bool SuppressLocalSpawn => NetworkClient.active && !NetworkServer.active;

        /// <summary>
        /// Host-side: publish a freshly-Instantiated piece to all clients via NetworkServer.Spawn.
        /// No-op offline or on a client. Safe to call unconditionally right after Instantiate.
        /// </summary>
        public static void RegisterSpawned(GameObject piece)
        {
            if (piece == null)
                return;
            if (!NetworkServer.active)
                return; // offline or client-only: leave it as a plain local object / no-op

            if (!piece.TryGetComponent(out NetworkIdentity identity))
            {
                Debug.LogWarning(
                    $"[GamePieceNetworkRegistrar] '{piece.name}' has no NetworkIdentity; " +
                    "cannot host-spawn it. Add NetworkIdentity + NetworkTransform + GamePieceNetworkSync " +
                    "to the piece prefab and register it on CloSimNetworkManager.spawnPrefabs.");
                return;
            }

            if (identity.netId != 0)
                return; // already spawned

            NetworkServer.Spawn(piece);
        }

        /// <summary>
        /// Host-side removal of a networked piece (replicates the destroy to clients). Falls back to a
        /// plain <see cref="Object.Destroy(Object)"/> when offline or for a non-networked object, so it
        /// is a drop-in replacement for <c>Destroy(piece.gameObject)</c> at the despawn sites.
        /// </summary>
        public static void Despawn(GameObject piece)
        {
            if (piece == null)
                return;

            bool networked = piece.TryGetComponent(out NetworkIdentity identity) && identity.netId != 0;

            if (NetworkServer.active && networked)
            {
                NetworkServer.Destroy(piece); // replicated destroy
                return;
            }

            if (NetworkClient.active && !NetworkServer.active && networked)
                return; // client-only: the host owns lifetime; ignore the local destroy request

            Object.Destroy(piece); // offline / non-networked: unchanged behavior
        }

        /// <summary>
        /// Host-side: un-spawn (hide on clients) WITHOUT destroying — for future pooled reuse. CloSim
        /// pieces are currently Instantiate/Destroy (not pooled), so <see cref="Despawn"/> is the path
        /// in use; this is provided so a pool integration can recycle the same object. No-op offline.
        /// </summary>
        public static void UnregisterForReuse(GameObject piece)
        {
            if (piece == null || !NetworkServer.active)
                return;
            if (!piece.TryGetComponent(out NetworkIdentity identity) || identity.netId == 0)
                return;

            NetworkServer.UnSpawn(piece);
        }
    }
}
