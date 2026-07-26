// CloSim Online Multiplayer — host-authoritative game-piece pose/physics sync (T5, gameplay-sync).
// Namespace: Online.Sync. File under Assets/Scripts/Online/Sync/Pieces/.
//
// PURPOSE
//   Make every client render the HOST's authoritative game-piece pose WITHOUT changing any piece
//   math/behavior. This component is ADDITIVE plumbing that lives on the piece PREFAB alongside a
//   `NetworkIdentity` and a `NetworkTransform` (see prefab requirements below). It does exactly one
//   thing: it decides *who runs the piece's local Rigidbody physics*.
//
//     * On the SERVER (host)      -> physics stays fully active. The host simulates the piece exactly
//                                    as it does offline (gravity + velocity from GamePieceManager).
//     * On a CLIENT-ONLY instance -> the local Rigidbody is made kinematic so the client does NOT run
//                                    its own divergent simulation; the co-located NetworkTransform is
//                                    then the sole driver of the visual pose (server -> client).
//
//   No piece script is edited. The Rigidbody is discovered generically via GetComponent, so this works
//   for any piece prefab (Algae/Coral/Fuel today) regardless of its GamePiece wiring.
//
// OFFLINE SAFETY (golden rule 7 — no offline regression)
//   Mirror's OnStartServer/OnStartClient only fire when the object is network-spawned. Offline the
//   piece is a plain Instantiate: these callbacks never run, we never touch the Rigidbody, and the
//   piece behaves EXACTLY as today. The extra NetworkIdentity/NetworkTransform components are inert
//   while unspawned (their update loops early-out on !isServer && !isClient).
//
// PREFAB REQUIREMENTS (must be added by the prefab owner — documented, not gameplay tuning):
//   On each Resources/Pieces/*.prefab ROOT (the object carrying the GamePiece + Rigidbody):
//     1) Mirror `NetworkIdentity`.
//     2) Mirror `NetworkTransformUnreliable` (or NetworkTransformReliable) with
//          Sync Direction = Server To Client,  Sync Position = on, Sync Rotation = on,
//          (leave Sync Scale off — pieces do not rescale).
//     3) This `GamePieceNetworkSync`.
//   The prefab must ALSO be registered as a spawnable prefab on the CloSimNetworkManager
//   (spawnPrefabs / RegisterPrefab) so clients can instantiate it on NetworkServer.Spawn.
//   NOTE: getting the piece network-spawned at all (host authoritative) is handled by
//   GamePieceNetworkRegistrar + the spawn-site integration documented there.

using Mirror;
using UnityEngine;

namespace Online.Sync
{
    /// <summary>
    /// Piece-prefab component that runs the game-piece's Rigidbody physics HOST-ONLY and lets the
    /// co-located <c>NetworkTransform</c> drive the pose on client-only instances. Purely additive:
    /// it never alters piece math and is completely inert when the piece is not network-spawned
    /// (i.e. offline split-screen is unaffected).
    /// </summary>
    [AddComponentMenu("CloSim/Sync/Game Piece Network Sync")]
    [RequireComponent(typeof(NetworkIdentity))]
    public class GamePieceNetworkSync : NetworkBehaviour
    {
        [Tooltip("Discovered automatically. The piece's own Rigidbody (the physics driver we gate).")]
        [SerializeField] private Rigidbody _body;

        // Remember the prefab's authored physics state so we can restore it exactly if ownership changes.
        private bool _cachedOriginalState;
        private bool _originalIsKinematic;
        private bool _originalDetectCollisions;

        private void Awake()
        {
            if (_body == null)
                _body = GetComponent<Rigidbody>();

            CacheOriginalState();
        }

        private void CacheOriginalState()
        {
            if (_cachedOriginalState || _body == null)
                return;

            _originalIsKinematic = _body.isKinematic;
            _originalDetectCollisions = _body.detectCollisions;
            _cachedOriginalState = true;
        }

        // ---------------------------------------------------------------- Mirror lifecycle

        /// <summary>
        /// Host path. The server owns the simulation, so we keep the authored physics state (active).
        /// Fires on the host's local instance too (host is server+client), which is why the client hook
        /// below only acts on <see cref="NetworkBehaviour.isClientOnly"/>.
        /// </summary>
        public override void OnStartServer()
        {
            base.OnStartServer();
            RestoreServerPhysics();
        }

        /// <summary>
        /// Client-only path. Disable the local physics driver so it cannot diverge from the host; the
        /// NetworkTransform now owns the visual pose. On the host this method also runs but is skipped
        /// because <see cref="NetworkBehaviour.isServer"/> is true there.
        /// </summary>
        public override void OnStartClient()
        {
            base.OnStartClient();

            if (isServer)
                return; // host keeps authoritative physics

            DisableLocalPhysics();
        }

        // ---------------------------------------------------------------- Physics gating

        private void RestoreServerPhysics()
        {
            if (_body == null)
                return;

            CacheOriginalState();
            _body.isKinematic = _originalIsKinematic;
            _body.detectCollisions = _originalDetectCollisions;
        }

        private void DisableLocalPhysics()
        {
            if (_body == null)
                return;

            CacheOriginalState();

            // Kinematic body: Unity physics will not integrate it, so the NetworkTransform's applied
            // pose is authoritative. We keep collider detection off client-side because the host is the
            // only authority for piece/robot contacts; the client just renders the replicated transform.
            _body.velocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.isKinematic = true;
            _body.detectCollisions = false;
        }
    }
}
