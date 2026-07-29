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

        // Fallback overview sits far behind any real single-view camera, so an owned-robot camera (depth 0)
        // always renders on top of it while a robot-less client still sees the field instead of black.
        private const float FallbackCameraDepth = -100f;

        private LoadMatch _loadMatch;
        private bool _started;
        private bool _reanchored;
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
            StartCoroutine(GuaranteeCameraRoutine());
        }

        // Guarantee a rendering camera on EVERY online instance (host AND clients), immediately and for the
        // life of the match, so the field is never a black, cameraless screen while robots spawn/replicate.
        // The fallback overview sits at a very low depth (FallbackCameraDepth), so a client that owns a robot
        // renders its own full-screen single-view camera (AddOnlineCamera, depth 0) ON TOP of this background
        // — a playing client is visually unaffected, and a robot-less/spectator client still sees the field.
        private IEnumerator GuaranteeCameraRoutine()
        {
            // Activate straight away so there is never a black frame, even before any robot has spawned.
            ActivateSpectatorCamera();

            // The field loads asynchronously the same frame the scene opens, so the camera anchor may not
            // exist yet on the first frame; re-frame onto it once it does.
            float deadline = Time.unscaledTime + GracePeriodSeconds;
            while (Time.unscaledTime < deadline)
            {
                if (!NetworkServer.active && !NetworkClient.active)
                    yield break; // Connection ended; leave whatever camera exists.

                ReanchorSpectatorCamera();
                yield return null;
            }
        }

        private void ReanchorSpectatorCamera()
        {
            if (_spectatorCamera == null || _reanchored)
                return;

            Transform anchor = _loadMatch != null ? _loadMatch.GetFieldCameraAnchor() : null;
            if (anchor == null)
                return;

            _spectatorCamera.transform.SetParent(anchor, false);
            _spectatorCamera.transform.localPosition = Vector3.zero;
            _spectatorCamera.transform.localRotation = Quaternion.identity;
            _reanchored = true;
        }

        /// <summary>
        /// One-shot camera guarantee usable OUTSIDE a networked session (e.g. replay playback): if the scene
        /// has no enabled camera, spawn the same field-overview camera so the field is visible, not black.
        /// </summary>
        public static void EnsureFieldOverviewCamera(LoadMatch loadMatch)
        {
            if (AnyEnabledCamera())
                return;

            if (loadMatch == null)
                loadMatch = MatchSpawnManager.FindLoadMatch();

            var go = new GameObject(nameof(SpectatorCameraController) + "_Fallback");
            var controller = go.AddComponent<SpectatorCameraController>();
            controller._started = true;
            controller._loadMatch = loadMatch;
            controller.ActivateSpectatorCamera();
            controller.ReanchorSpectatorCamera();
        }

        public static bool AnyEnabledCamera()
        {
            var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in cams)
                if (cam != null && cam.isActiveAndEnabled)
                    return true;
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
                cam.depth = FallbackCameraDepth; // stay behind any owned-robot single-view camera (depth 0)
            }

            AudioListener[] listeners = cameraObject.GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
                listeners[i].enabled = i == 0;
        }
    }
}
