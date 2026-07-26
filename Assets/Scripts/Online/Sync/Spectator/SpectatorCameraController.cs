// CloSim Online Multiplayer — Gameplay Sync (additive). Namespace: Online.Sync.
//
// Gives a robot-less online client (a spectator, or any client that otherwise ends up owning zero
// robots) a field-overview camera once match spawning has settled. Fixes the gap where
// RobotNetworkController.SetupLocalOwner() — the only call site of LoadMatch.AddOnlineCamera() — never
// runs for a client that owns no NetworkIdentity, leaving that client with no camera at all.
//
// DETECTION: after RunForScene is invoked (same call site/timing as MatchSpawnManager.RunForScene, from
// MatchSceneBootstrap's online branch), this polls for up to GracePeriodSeconds for ANY
// RobotNetworkController in the scene reporting isOwned == true on THIS machine. Robot spawn is
// server-authoritative and near-instant but not synchronous with scene load (Mirror has to spawn +
// replicate ownership to each client), so a short grace period avoids a false "spectator" read while a
// client's own robot is still in flight. If nothing is ever owned locally once the grace period elapses,
// this client activates a static field-overview camera and stops watching.
//
// CAMERA: reuses the exact anchor LoadMatch.AddFieldCamera() already uses for the offline 3v0 "4th
// quadrant" field camera (LoadMatch.GetFieldCameraAnchor(), additive read-only exposure of the existing
// private lookup) and the same "Cameras/FirstPerson" Resources prefab, so this is a well-understood,
// guaranteed-to-exist starting point rather than a new camera concept. The viewport/AudioListener setup
// mirrors LoadMatch.ConfigureOnlineCameraViewport() exactly: full-screen Rect(0,0,1,1), depth 0, exactly
// one enabled AudioListener. v1 is a static elevated view (matches what the offline field camera already
// gives today) — no controls, no LookAtRobot chasing any particular robot.
//
// OFFLINE / OWNED-ROBOT NO-OP: RunForScene bails immediately unless NetworkServer.active or
// NetworkClient.active (same online-only gate MatchSceneBootstrap already applies before calling this),
// and the watch routine exits the moment it ever observes a locally-owned robot, so a playing client is
// completely unaffected and never sees this camera.
//
// No networking here: every client independently decides "do I own a robot" from local NetworkIdentity
// state, so no new SyncVars/Commands are needed for this feature.

using System.Collections;
using CameraControls;
using Core;
using Mirror;
using UnityEngine;

namespace Online.Sync
{
    [AddComponentMenu("CloSim/Sync/Spectator Camera Controller")]
    public sealed class SpectatorCameraController : MonoBehaviour
    {
        // Robot spawn is near-instant but arrives over the network for non-host clients; give it a short
        // window before concluding "this client owns nothing" (task guidance: a few frames up to ~1-2s).
        private const float GracePeriodSeconds = 2f;
        private const string OverviewCameraResourcePath = "Cameras/FirstPerson"; // same prefab LoadMatch.AddFieldCamera() uses

        private LoadMatch _loadMatch;
        private bool _started;
        private GameObject _spectatorCamera;

        /// <summary>
        /// Online hook entry (per machine), invoked the same way as MatchSpawnManager.RunForScene from
        /// MatchSceneBootstrap's online branch. No-op offline. Safe to call once per scene load.
        /// </summary>
        public static SpectatorCameraController RunForScene(LoadMatch loadMatch)
        {
            if (!NetworkServer.active && !NetworkClient.active)
                return null; // Golden-rule gate: this feature does not exist offline.

            if (loadMatch == null)
                loadMatch = MatchSpawnManager.FindLoadMatch();

            if (loadMatch == null)
            {
                Debug.LogError("[SpectatorCameraController] No LoadMatch in scene; cannot watch for spectator state.");
                return null;
            }

            var controller = FindFirstObjectByType<SpectatorCameraController>();
            if (controller == null)
            {
                var go = new GameObject(nameof(SpectatorCameraController));
                controller = go.AddComponent<SpectatorCameraController>();
            }

            controller.Begin(loadMatch);
            return controller;
        }

        private void Begin(LoadMatch loadMatch)
        {
            if (_started)
                return;

            _started = true;
            _loadMatch = loadMatch;
            StartCoroutine(WatchForSpectatorState());
        }

        private IEnumerator WatchForSpectatorState()
        {
            float deadline = Time.unscaledTime + GracePeriodSeconds;

            while (Time.unscaledTime < deadline)
            {
                if (!NetworkServer.active && !NetworkClient.active)
                    yield break; // Connection dropped before we decided; nothing to activate.

                if (LocalClientOwnsAnyRobot())
                    yield break; // This client owns a robot; its single-view camera is already handled.

                yield return null;
            }

            // Grace period elapsed with no locally-owned robot ever observed: this machine is a
            // spectator (or otherwise robot-less) for this match. Re-check state once more (cheap) before
            // committing, in case ownership or the connection changed on the final frame.
            if (!NetworkServer.active && !NetworkClient.active)
                yield break;

            if (LocalClientOwnsAnyRobot())
                yield break;

            ActivateSpectatorCamera();
        }

        private bool LocalClientOwnsAnyRobot()
        {
            var robotControllers = FindObjectsByType<RobotNetworkController>(FindObjectsSortMode.None);

            foreach (var robotController in robotControllers)
            {
                if (robotController != null && robotController.isOwned)
                    return true;
            }

            return false;
        }

        private void ActivateSpectatorCamera()
        {
            if (_spectatorCamera != null)
                return; // Already activated (e.g. RunForScene invoked twice for this coordinator instance).

            GameObject prefab = Resources.Load<GameObject>(OverviewCameraResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[SpectatorCameraController] Could not load Resources/{OverviewCameraResourcePath} for the spectator overview camera.");
                return;
            }

            Transform anchor = _loadMatch != null ? _loadMatch.GetFieldCameraAnchor() : null;

            if (anchor != null)
            {
                _spectatorCamera = Instantiate(prefab, anchor.position, anchor.rotation, anchor);
                _spectatorCamera.transform.localPosition = Vector3.zero;
                _spectatorCamera.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Debug.LogWarning(
                    "[SpectatorCameraController] No field camera anchor found on the loaded field; " +
                    "placing the spectator overview camera at the world origin.");
                _spectatorCamera = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            }

            _spectatorCamera.name = "SpectatorOverviewCamera";

            // Static overview for v1: hold the anchor's framing of the whole field rather than chasing a
            // specific robot (matches the existing offline field-camera behavior in LoadMatch.AddFieldCamera()).
            foreach (LookAtRobot lookAt in _spectatorCamera.GetComponentsInChildren<LookAtRobot>(true))
                lookAt.enabled = false;

            ConfigureFullScreenViewport(_spectatorCamera);
        }

        // Mirrors LoadMatch.ConfigureOnlineCameraViewport() exactly: full-screen viewport, single active
        // AudioListener. Kept local (rather than exposing LoadMatch's private helper) since it is a
        // trivial, self-contained two-loop operation with no other LoadMatch state involved.
        private void ConfigureFullScreenViewport(GameObject cameraObject)
        {
            if (cameraObject == null)
                return;

            Rect full = new Rect(0f, 0f, 1f, 1f);

            Camera[] cameras = cameraObject.GetComponentsInChildren<Camera>(true);
            foreach (Camera cam in cameras)
            {
                cam.rect = full;
                cam.depth = 0f;
            }

            AudioListener[] listeners = cameraObject.GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
                listeners[i].enabled = i == 0;
        }
    }
}
