// CloSim Online Multiplayer — Phase 3 Gameplay Sync (T2). Namespace: Online.Sync.
//
// ============================================================================================
// REQUIRED ROBOT-PREFAB COMPONENT ADDITIONS (added by the prefab-plumbing task, NOT by this script):
//   Each spawnable robot prefab root must carry, IN ADDITION to its existing gameplay components:
//     1. Mirror.NetworkIdentity
//     2. Mirror.NetworkTransformUnreliable (or NetworkTransformReliable) — Sync Position + Rotation.
//        Sync Direction is set AT RUNTIME by this controller (ClientToServer for a player-owned robot,
//        ServerToClient for an unowned one), so the prefab's inspector value does not matter.
//     3. Online.Sync.RobotNetworkController (this component)
//   The prefab must ALSO be registered in the NetworkManager spawnable-prefab list — MatchSpawnManager does
//   this at runtime from the LoadMatch robot catalog.
// ============================================================================================
//
// OWNER-AUTHORITATIVE movement model (drives EXACTLY like offline single-player):
//   * The OWNER of a robot (the host for its own slot, or a client for its slot) runs the robot's stock
//     SwerveController locally and reads its own local device through LoadMatch's normal input pairing —
//     identical feel to offline. Its NetworkTransform is set ClientToServer so the resulting pose
//     replicates out to the server and every other client.
//   * On every NON-owner machine (the server for a client-owned robot, and all other clients) the robot is
//     a replicated visual: its SwerveController + input are disabled so local physics never fights the
//     networked pose.
//   * Robots with NO owner (empty roster slots) stay server-driven (ServerToClient) exactly as before.
//   Scoring / Fms remain fully server-authoritative — only robot MOVEMENT is owner-authoritative.

using Core;
using Mirror;
using Robot.Runtime;
using UnityEngine;

namespace Online.Sync
{
    [AddComponentMenu("CloSim/Sync/Robot Network Controller")]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class RobotNetworkController : NetworkBehaviour
    {
        // --- Authoritative per-robot state (server writes at spawn; all machines read) ---
        [SyncVar] private int _slot = -1;
        [SyncVar] private int _robotIndex;
        [SyncVar] private int _viewIndex = (int)Cameras.ThirdPerson;
        [SyncVar] private int _blueCount;
        [SyncVar] private int _redCount;

        // True when this robot has a player owner (host or client), so its movement is owner-authoritative.
        [SyncVar] private bool _clientAuthoritative;

        public int Slot => _slot;

        private LoadMatch _loadMatch;
        private bool _registered;

        /// <summary>Server-only: seed the authoritative SyncVars BEFORE NetworkServer.Spawn.</summary>
        [Server]
        public void ServerInit(int slot, int robotIndex, Cameras view, int blueCount, int redCount, bool hasOwner)
        {
            _slot = slot;
            _robotIndex = robotIndex;
            _viewIndex = (int)view;
            _blueCount = blueCount;
            _redCount = redCount;
            _clientAuthoritative = hasOwner;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            EnsureRegistered();
            ConfigureTransformAuthority();

            // A robot owned by a REMOTE client is driven by that client and its pose arrives over the
            // network (ClientToServer). The server must NOT simulate it, or its physics fights the incoming
            // pose. The host's OWN robot (isOwned) keeps its SwerveController — it drives locally. An
            // unowned robot stays server-driven.
            if (_clientAuthoritative && !isOwned)
                DisableLocalSwerve();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            EnsureRegistered();
            ConfigureTransformAuthority();

            if (isOwned)
            {
                SetupLocalOwner();
            }
            else if (!isServer)
            {
                // Replicated visual on a pure client: no input, no local physics fighting the network pose.
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

        /// <summary>
        /// Owner drives locally EXACTLY like offline single-player: run the stock SwerveController and pair
        /// the local device through LoadMatch's normal input path. Runs on the host for its own robot and on
        /// a pure client for its robot.
        /// </summary>
        private void SetupLocalOwner()
        {
            if (_loadMatch == null)
                return;

            _loadMatch.SetOnlineMode(true, _slot);
            _loadMatch.AddOnlineCamera(_slot, (Cameras)_viewIndex);

            // Ensure the drivetrain is running (a pure client would otherwise leave it disabled) and bind the
            // local device + action map — identical to how offline single-player controls its robot.
            EnableLocalSwerve();
            _loadMatch.PairLocalInput(_slot);
        }

        /// <summary>
        /// Point the robot's NetworkTransform the right way: owner-authoritative (ClientToServer) for a
        /// player-owned robot so the owner's local movement replicates out; server-authoritative
        /// (ServerToClient) for an unowned robot the server drives.
        /// </summary>
        private void ConfigureTransformAuthority()
        {
            var netTransform = GetComponent<NetworkTransformBase>();
            if (netTransform != null)
                netTransform.syncDirection =
                    _clientAuthoritative ? SyncDirection.ClientToServer : SyncDirection.ServerToClient;
        }

        private void EnableLocalSwerve()
        {
            var swerve = GetComponent<SwerveController>();
            if (swerve != null)
                swerve.enabled = true;
        }

        private void DisableLocalSwerve()
        {
            var swerve = GetComponent<SwerveController>();
            if (swerve != null)
                swerve.enabled = false;
        }
    }
}
