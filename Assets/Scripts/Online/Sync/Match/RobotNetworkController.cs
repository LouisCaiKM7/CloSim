// CloSim Online Multiplayer — Phase 3 Gameplay Sync (T2). Namespace: Online.Sync.
//
// ============================================================================================
// REQUIRED ROBOT-PREFAB COMPONENT ADDITIONS (added by the prefab-plumbing task, NOT by this script):
//   Each spawnable robot prefab root must carry, IN ADDITION to its existing gameplay components:
//     1. Mirror.NetworkIdentity                         (server-authoritative identity; no "Local Player
//                                                        Authority" needed — ownership is per-connection)
//     2. Mirror.NetworkTransformUnreliable (or NetworkTransformReliable)
//                                                        - Sync Direction = Server To Client
//                                                        - Sync Position + Rotation (root pose only; wheel
//                                                          modules are cosmetic and not synced)
//     3. Online.Sync.RobotNetworkController (this component)
//   The prefab must ALSO be registered in the NetworkManager spawnable-prefab list — MatchSpawnManager does
//   this at runtime from the LoadMatch robot catalog, so no manual inspector wiring of spawnPrefabs is needed.
// ============================================================================================
//
// Server-authoritative movement model:
//   * SERVER runs each robot's (unchanged) SwerveController physics. NetworkTransform replicates the resulting
//     root pose down to every client.
//   * The OWNER client reads its local Drive/Rotate each FixedUpdate and ships them to the server via an
//     unreliable [Command]. The server feeds them into a ServerRobotInputSource which drives the robot's
//     Input System actions (synthetic input) so the stock SwerveController consumes them EXACTLY as if a local
//     device produced them — the fieldCentric / reversed "feel" branches are preserved (we never call
//     OverideInputs, which would force the field-centric branch).
//   * On non-server clients this robot is a replicated visual; its local SwerveController is disabled so it
//     does not fight NetworkTransform (client-side prediction is intentionally out of scope for T2).

using Core;
using Mirror;
using Robot.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Online.Sync
{
    [AddComponentMenu("CloSim/Sync/Robot Network Controller")]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class RobotNetworkController : NetworkBehaviour
    {
        [Header("Input action names (must match SwerveController)")]
        [SerializeField] private string actionMapName = "Robot";
        [SerializeField] private string driveActionName = "Drive";
        [SerializeField] private string rotateActionName = "Rotate";

        // --- Authoritative per-robot state (server writes at spawn; all machines read) ---
        [SyncVar] private int _slot = -1;
        [SyncVar] private int _robotIndex;
        [SyncVar] private int _viewIndex = (int)Cameras.ThirdPerson;
        [SyncVar] private int _blueCount;
        [SyncVar] private int _redCount;

        public int Slot => _slot;

        private LoadMatch _loadMatch;
        private bool _registered;

        // Owner-client input reading.
        private InputAction _driveAction;
        private InputAction _rotateAction;
        private bool _ownerInputResolved;

        // Server-side synthetic input injection (remote-owned robots only).
        private ServerRobotInputSource _inputSource;

        /// <summary>Server-only: seed the authoritative SyncVars BEFORE NetworkServer.Spawn.</summary>
        [Server]
        public void ServerInit(int slot, int robotIndex, Cameras view, int blueCount, int redCount)
        {
            _slot = slot;
            _robotIndex = robotIndex;
            _viewIndex = (int)view;
            _blueCount = blueCount;
            _redCount = redCount;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            EnsureRegistered();

            // Remote-owned robot on the server: it has no local device, so drive its Input System actions from
            // the networked values. The host's OWN robot (isOwned) keeps its real local device via LoadMatch.
            if (!isOwned)
                SetupServerInjection();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            EnsureRegistered();

            if (isOwned)
            {
                SetupLocalOwner();
            }
            else if (!isServer)
            {
                // Remote robot on a pure client: network-driven visual only.
                if (_loadMatch != null)
                    _loadMatch.DisableRobotInput(gameObject);

                DisableLocalSwerve();
            }
        }

        // ---------------------------------------------------------------- Registration / configuration

        private void EnsureRegistered()
        {
            if (_registered)
                return;

            _loadMatch = MatchSpawnManager.FindLoadMatch();
            if (_loadMatch == null)
            {
                Debug.LogError("[RobotNetworkController] No LoadMatch found in the loaded scene; cannot register robot.");
                return;
            }

            // On clients the LoadMatch settings default to the offline 1-player config, so patch THIS slot's
            // alliance/robot/view before registering so bumper colours + drive mode resolve correctly. The host
            // already applied the full built settings via MatchSpawnManager, so it skips the patch.
            if (!isServer)
            {
                MatchSpawnManager.PatchClientSettingsForSlot(
                    _loadMatch, _slot, _robotIndex, (Cameras)_viewIndex, _blueCount, _redCount);
            }

            _loadMatch.RegisterNetworkedRobot(_slot, gameObject);
            _registered = true;
        }

        private void SetupLocalOwner()
        {
            if (_loadMatch == null)
                return;

            // Keep LoadMatch's notion of the local owned slot in sync for its own book-keeping.
            _loadMatch.SetOnlineMode(true, _slot);
            _loadMatch.AddOnlineCamera(_slot, (Cameras)_viewIndex);
            _loadMatch.PairLocalInput(_slot);

            // On a pure client, the server owns physics; disable the local drivetrain so it does not fight the
            // replicated NetworkTransform. The host is the server, so it keeps its SwerveController running.
            if (!isServer)
                DisableLocalSwerve();

            ResolveOwnerInputActions();
        }

        private void ResolveOwnerInputActions()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput == null || playerInput.actions == null)
                return;

            InputActionMap map = playerInput.actions.FindActionMap(actionMapName);
            if (map == null)
                return;

            _driveAction = map.FindAction(driveActionName);
            _rotateAction = map.FindAction(rotateActionName);
            _ownerInputResolved = _driveAction != null && _rotateAction != null;
        }

        private void DisableLocalSwerve()
        {
            var swerve = GetComponent<SwerveController>();
            if (swerve != null)
                swerve.enabled = false;
        }

        // ---------------------------------------------------------------- Server-side injection

        private void SetupServerInjection()
        {
            _inputSource = GetComponent<ServerRobotInputSource>();
            if (_inputSource == null)
                _inputSource = gameObject.AddComponent<ServerRobotInputSource>();

            _inputSource.Initialize(_slot, actionMapName, driveActionName, rotateActionName);
        }

        // ---------------------------------------------------------------- Movement transport

        private void FixedUpdate()
        {
            // Only a pure-client owner needs to ship input; the host's owned robot uses its real local device,
            // and non-owners never send. Sending every physics step over the unreliable channel keeps the
            // server's synthetic input source fed with low latency.
            if (!isOwned || isServer)
                return;

            if (!_ownerInputResolved)
            {
                ResolveOwnerInputActions();
                if (!_ownerInputResolved)
                    return;
            }

            Vector2 drive = _driveAction.ReadValue<Vector2>();
            Vector2 rotate = _rotateAction.ReadValue<Vector2>();
            CmdSendInput(drive, rotate);
        }

        [Command(channel = Channels.Unreliable, requiresAuthority = true)]
        private void CmdSendInput(Vector2 drive, Vector2 rotate)
        {
            if (_inputSource != null)
                _inputSource.SetInput(drive, rotate);
        }
    }
}
