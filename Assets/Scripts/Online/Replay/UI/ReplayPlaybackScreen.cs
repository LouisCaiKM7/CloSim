// CloSim Online Multiplayer — in-game replay playback screen. Namespace: Online.Replay.UI.
// Native-Unity playback of a stored replay: fetches the blob (IReplayService.GetAsync), decodes it
// (Online.Replay.Codec.ReplayReader), then reconstructs the match KINEMATICALLY and LOCALLY — no
// networking, no physics simulation. It loads the recorded scene (via the existing
// UI.Transitions.SceneTransitionManager, same path the rest of the game uses to change scenes), spawns
// one non-networked robot per roster entry using LoadMatch's existing catalog resolution
// (LoadMatch.GetNetworkRobotPrefab, the same lookup Online.Sync.MatchSpawnManager uses to server-spawn
// networked robots), and drives their transforms directly from the decoded, dequantized timeline
// (ReplayTimelineResolver). This canvas is marked DontDestroyOnLoad (see ReplaysMenuController) so the
// playback HUD survives the scene swap into the field scene and back.
//
// SIMPLIFICATIONS (v1, documented per the task brief's explicit allowance):
//   * Robots only — no game-piece playback (see MatchReplayRecorderRunner's header comment).
//   * A single fixed third-person camera follows the roster's first slot; no manual camera switching.
//   * No score/HUD overlay from the original match — only the playback transport controls below.
//   * Scrubbing is keyframe-granular (prev/next keyframe), not an arbitrary drag-scrub bar; play/pause +
//     a speed multiplier + keyframe stepping covers the "watch it back" use case without a full scrub UI.

using System;
using System.Collections;
using System.Collections.Generic;
using Core;
using Online.Contracts.Replay;
using Online.Replay.Codec;
using Online.Replay.Service;
using Online.UI.Lobby;
using TMPro;
using UI.Components;
using UI.Transitions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Online.Replay.UI
{
    /// <summary>Fetches, decodes, and locally plays back one replay. See file header for the full flow.</summary>
    public class ReplayPlaybackScreen : LobbyScreen
    {
        [Tooltip("Scene to return to when playback ends or the user backs out.")]
        [SerializeField] private string mainMenuSceneName = "Main_Menu";

        /// <inheritdoc/>
        public override string Key => ReplayScreenKeys.Playback;

        private TMP_Text _statusLabel;
        private TMP_Text _progressLabel;
        private Button _playPauseButton;
        private TMP_Text _playPauseLabel;
        private Button _prevKeyframeButton;
        private Button _nextKeyframeButton;
        private GamepadDropdown _speedDropdown;

        private string _pendingReplayId;
        private ReplayHeader _header;
        private ReplayTimelineResolver.Result _resolved;
        private readonly Dictionary<int, Transform> _spawnedRobots = new();

        private bool _sceneReady;
        private bool _isPlaying;
        private float _playbackTime;
        private float _speed = 1f;
        private bool _destroyed;

        private static readonly float[] SpeedOptions = { 0.25f, 0.5f, 1f, 2f };

        // ---------------------------------------------------------------- build

        /// <inheritdoc/>
        protected override void BuildUi()
        {
            LobbyUiKit.Panel(transform, "bg", LobbyUiKit.PanelBg);

            GameObject col = LobbyUiKit.Column(transform, "root", LobbyUiKit.SpaceMd, LobbyUiKit.PadLg, TextAnchor.UpperCenter);
            LobbyUiKit.Stretch(LobbyUiKit.RectOf(col));

            BuildHeader(col.transform);
            LobbyUiKit.Divider(col.transform);

            _statusLabel = LobbyUiKit.Label(col.transform, "", LobbyUiKit.FontLabel, TextAlignmentOptions.Left, LobbyUiKit.TextMuted);
            LobbyUiKit.SetSize(_statusLabel.gameObject, -1, 28);

            LobbyUiKit.Spacer(col.transform);

            BuildControls(col.transform);

            FirstSelected = _playPauseButton.gameObject;
        }

        private void BuildHeader(Transform parent)
        {
            GameObject header = LobbyUiKit.HeaderRow(parent, "Replay Playback", out _);

            Button exitButton = LobbyUiKit.SecondaryButton(header.transform, "Exit", out _, LobbyUiKit.FontBody);
            LobbyUiKit.SetSize(exitButton.gameObject, 160, LobbyUiKit.ButtonHeight);
            exitButton.onClick.AddListener(Back);
        }

        private void BuildControls(Transform parent)
        {
            GameObject bar = LobbyUiKit.Card(parent, "ControlsBar", LobbyUiKit.CardBg, LobbyUiKit.SpaceMd, LobbyUiKit.PadMd);
            LobbyUiKit.SetSize(bar, -1, -1);

            _progressLabel = LobbyUiKit.Label(bar.transform, "00:00 / 00:00", LobbyUiKit.FontBody, TextAlignmentOptions.Center);
            LobbyUiKit.SetSize(_progressLabel.gameObject, -1, 32);

            GameObject row = LobbyUiKit.Row(bar.transform, "TransportRow", LobbyUiKit.SpaceMd, 0, TextAnchor.MiddleCenter);
            LobbyUiKit.SetSize(row, -1, LobbyUiKit.ButtonHeight + 4f);

            _prevKeyframeButton = LobbyUiKit.SecondaryButton(row.transform, "|< Keyframe", out _, LobbyUiKit.FontLabel);
            LobbyUiKit.SetSize(_prevKeyframeButton.gameObject, 190, LobbyUiKit.ButtonHeight);
            _prevKeyframeButton.onClick.AddListener(StepToPreviousKeyframe);

            _playPauseButton = LobbyUiKit.PrimaryButton(row.transform, "Pause", out _playPauseLabel, LobbyUiKit.FontBody);
            LobbyUiKit.SetSize(_playPauseButton.gameObject, 160, LobbyUiKit.ButtonHeight);
            _playPauseButton.onClick.AddListener(TogglePlayPause);

            _nextKeyframeButton = LobbyUiKit.SecondaryButton(row.transform, "Keyframe >|", out _, LobbyUiKit.FontLabel);
            LobbyUiKit.SetSize(_nextKeyframeButton.gameObject, 190, LobbyUiKit.ButtonHeight);
            _nextKeyframeButton.onClick.AddListener(StepToNextKeyframe);

            var speedLabels = new List<string>();
            foreach (float s in SpeedOptions) speedLabels.Add(s + "x");
            _speedDropdown = LobbyUiKit.Dropdown(row.transform, speedLabels, Array.IndexOf(SpeedOptions, 1f));
            LobbyUiKit.SetSize(_speedDropdown.gameObject, 120, LobbyUiKit.ButtonHeight);
            _speedDropdown.onValueChanged.AddListener(OnSpeedChanged);
        }

        // ---------------------------------------------------------------- lifecycle

        /// <summary>Called by ReplaysScreen right before navigating here. Consumed once in OnShow.</summary>
        public void SetPendingReplay(string replayId) => _pendingReplayId = replayId;

        /// <inheritdoc/>
        public override void OnShow()
        {
            if (!string.IsNullOrEmpty(_pendingReplayId))
            {
                string id = _pendingReplayId;
                _pendingReplayId = null;
                StartLoadAndPlay(id);
            }
        }

        /// <inheritdoc/>
        public override void OnHide()
        {
            _isPlaying = false;
            IsPlaybackActive = false;

            // Any way of leaving this screen (Exit button or Back/Escape) unwinds the same way: if we
            // left the menu scene for the field scene, return to it. Offline, additive, no networking.
            if (_sceneReady)
            {
                _sceneReady = false;
                _spawnedRobots.Clear(); // stale refs — the scene unload below destroys the GameObjects
                SceneTransitionManager.EnsureExists().LoadScene(mainMenuSceneName);
            }
        }

        private void OnDestroy()
        {
            _destroyed = true;
            IsPlaybackActive = false;
        }

        // ---------------------------------------------------------------- fetch + decode

        /// <summary>
        /// True while a replay is loading/playing back the field scene. Read by the in-field pre-match menu
        /// (OptionsMenuController) so it keeps the offline robot-selection screen closed during playback.
        /// </summary>
        public static bool IsPlaybackActive { get; private set; }

        private async void StartLoadAndPlay(string replayId)
        {
            SetStatus("Loading replay…");
            SetTransportInteractable(false);

            // ADDITIVE: CompositeReplayService checks the local on-disk store first (LocalReplayService),
            // so replays recorded offline/single-player play back with zero AWS config, falling back to
            // the remote AWS-backed store for ids it doesn't own locally.
            var service = new CompositeReplayService();

            ReplayFetchResult fetch;
            try
            {
                fetch = await service.GetAsync(replayId);
            }
            catch (Exception e)
            {
                if (_destroyed) return;
                SetStatus("Failed to load replay: " + e.Message);
                return;
            }

            if (_destroyed) return;

            if (!fetch.ok || fetch.blob == null)
            {
                SetStatus("Failed to load replay: " + (string.IsNullOrEmpty(fetch.error) ? "unknown error" : fetch.error));
                return;
            }

            ReplayDocument doc;
            try
            {
                doc = ReplayReader.Read(fetch.blob);
            }
            catch (Exception e)
            {
                SetStatus("Replay data is corrupt or unsupported: " + e.Message);
                return;
            }

            _header = doc.Header;
            _resolved = ReplayTimelineResolver.Resolve(doc.Frames);

            if (string.IsNullOrEmpty(_header.sceneName))
            {
                SetStatus("Replay is missing its scene name; cannot play back.");
                return;
            }

            SetStatus("Loading field…");
            // Signal the in-field pre-match menu to stay closed for playback (no robot-selection overlay).
            IsPlaybackActive = true;
            SceneManager.sceneLoaded += OnFieldSceneLoaded;
            SceneTransitionManager.EnsureExists().LoadScene(_header.sceneName);
        }

        // ---------------------------------------------------------------- scene reconstruction

        private void OnFieldSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnFieldSceneLoaded; // one-shot

            // Defensive: our canvas persisted (DontDestroyOnLoad) across the scene swap, but the
            // EventSystem that used to route input to it may not have (Main_Menu's EventSystem is not
            // itself DontDestroyOnLoad). Ensures gamepad/mouse input still reaches the playback controls.
            LobbyUiKit.EnsureEventSystem();

            LoadMatch loadMatch = FindFirstObjectByType<LoadMatch>();
            if (loadMatch == null)
            {
                SetStatus("Loaded field scene has no LoadMatch; cannot reconstruct robots.");
                return;
            }

            StartCoroutine(PrepareFieldAndSpawn(loadMatch));
        }

        /// <summary>
        /// Mirrors Online.Sync.MatchSpawnManager's re-drive trick: LoadMatch.Start() kicks off a default
        /// OFFLINE single-player ResetField the same frame the scene loads. Switching to "online" mode
        /// (no local owner) and re-driving ResetField until it wins the guard clears that transient robot
        /// so only OUR replay robots occupy the field.
        /// </summary>
        private IEnumerator PrepareFieldAndSpawn(LoadMatch loadMatch)
        {
            bool fieldReady = false;
            void OnFieldReady() => fieldReady = true;
            loadMatch.OnOnlineFieldReady += OnFieldReady;

            loadMatch.SetOnlineMode(true, -1);

            const int maxFrames = 180;
            for (int i = 0; i < maxFrames && !fieldReady; i++)
            {
                loadMatch.ResetField();
                yield return null;
            }

            loadMatch.OnOnlineFieldReady -= OnFieldReady;

            if (!fieldReady)
            {
                SetStatus("Timed out preparing the field for playback.");
                yield break;
            }

            SpawnReplayRobots(loadMatch);
        }

        private void SpawnReplayRobots(LoadMatch loadMatch)
        {
            // Patch the (blue,red) count model onto LoadMatch's settings BEFORE registering any robot so
            // EnsureSlotArrays() (called from ApplySettings) grows _activeRobots/_spawnedCameras/_runtimeViews
            // to cover up to 6 slots — mirrors Online.Sync.MatchSpawnManager.PatchClientSettingsForSlot.
            // ApplySettings does NOT reload the field or destroy robots, so this is safe to call here.
            MatchSettings patched = loadMatch.GetSettingsCopy();
            patched.useNetworkCounts = true;
            patched.networkBlueCount = _header.mode.blue;
            patched.networkRedCount = _header.mode.red;
            loadMatch.ApplySettings(patched);

            _spawnedRobots.Clear();

            ReplayRosterEntry[] roster = _header.roster ?? Array.Empty<ReplayRosterEntry>();
            int cameraSlot = -1;

            foreach (ReplayRosterEntry entry in roster)
            {
                GameObject prefab = loadMatch.GetNetworkRobotPrefab(entry.robotIndex);
                if (prefab == null)
                    continue;

                GameObject robot = Instantiate(prefab);
                robot.name = $"ReplayRobot_S{entry.slotIndex}_{entry.displayName}";

                DisableForPlayback(robot);

                // Cosmetic registration only (vanity bumpers / alliance colour / outpost ownership) — no
                // input pairing happens here since we never pass a local-owned slot to SetOnlineMode.
                loadMatch.RegisterNetworkedRobot(entry.slotIndex, robot);

                _spawnedRobots[entry.slotIndex] = robot.transform;
                if (cameraSlot < 0)
                    cameraSlot = entry.slotIndex;
            }

            if (cameraSlot >= 0)
                loadMatch.AddOnlineCamera(cameraSlot, Cameras.ThirdPerson);

            _sceneReady = true;
            _isPlaying = true;
            _playbackTime = 0f;
            SetTransportInteractable(true);
            SetStatus("");
            ApplyPlaybackTime();
        }

        private static void DisableForPlayback(GameObject robot)
        {
            var rnc = robot.GetComponent<Online.Sync.RobotNetworkController>();
            if (rnc != null) rnc.enabled = false;

            var swerve = robot.GetComponent<Robot.Runtime.SwerveController>();
            if (swerve != null) swerve.enabled = false;

            var playerInput = robot.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null) playerInput.enabled = false;

            foreach (Rigidbody rb in robot.GetComponentsInChildren<Rigidbody>())
                rb.isKinematic = true;
        }

        // ---------------------------------------------------------------- playback driving

        private void Update()
        {
            if (!_sceneReady || _resolved.FramePoses == null || _resolved.FramePoses.Length == 0)
                return;

            if (_isPlaying)
            {
                _playbackTime += Time.unscaledDeltaTime * _speed;

                float totalDuration = Mathf.Max(0f, _header.durationSec);
                if (totalDuration > 0f && _playbackTime >= totalDuration)
                {
                    _playbackTime = totalDuration;
                    _isPlaying = false; // linear play-through; no auto-loop (v1 simplification)
                }

                UpdatePlayPauseLabel();
            }

            ApplyPlaybackTime();
            UpdateProgressLabel();
        }

        private void ApplyPlaybackTime()
        {
            Dictionary<int, ResolvedPose>[] framePoses = _resolved.FramePoses;
            int frameCount = framePoses.Length;
            float tickRate = Mathf.Max(1, _header.tickRate);

            float rawIndex = _playbackTime * tickRate;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(rawIndex), 0, frameCount - 1);
            int i1 = Mathf.Clamp(i0 + 1, 0, frameCount - 1);
            float t = Mathf.Clamp01(rawIndex - i0);

            Dictionary<int, ResolvedPose> poseA = framePoses[i0];
            Dictionary<int, ResolvedPose> poseB = framePoses[i1];

            foreach (KeyValuePair<int, Transform> kvp in _spawnedRobots)
            {
                Transform robotTransform = kvp.Value;
                if (robotTransform == null || !poseA.TryGetValue(kvp.Key, out ResolvedPose a))
                    continue;

                if (poseB.TryGetValue(kvp.Key, out ResolvedPose b))
                {
                    robotTransform.position = Vector3.Lerp(a.position, b.position, t);
                    robotTransform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(a.yawDegrees, b.yawDegrees, t), 0f);
                }
                else
                {
                    robotTransform.position = a.position;
                    robotTransform.rotation = Quaternion.Euler(0f, a.yawDegrees, 0f);
                }
            }
        }

        // ---------------------------------------------------------------- transport controls

        private void TogglePlayPause()
        {
            if (!_sceneReady) return;

            if (!_isPlaying && _header.durationSec > 0f && _playbackTime >= _header.durationSec)
                _playbackTime = 0f; // replay from the start once it has finished

            _isPlaying = !_isPlaying;
            UpdatePlayPauseLabel();
        }

        private void StepToPreviousKeyframe()
        {
            if (!_sceneReady) return;

            int[] keyframes = _resolved.KeyframeIndices;
            if (keyframes == null || keyframes.Length == 0) return;

            float tickRate = Mathf.Max(1, _header.tickRate);
            int currentFrame = Mathf.FloorToInt(_playbackTime * tickRate);

            int target = keyframes[0];
            for (int i = 0; i < keyframes.Length; i++)
            {
                if (keyframes[i] < currentFrame)
                    target = keyframes[i];
                else
                    break;
            }

            _isPlaying = false;
            _playbackTime = target / tickRate;
            ApplyPlaybackTime();
            UpdatePlayPauseLabel();
        }

        private void StepToNextKeyframe()
        {
            if (!_sceneReady) return;

            int[] keyframes = _resolved.KeyframeIndices;
            if (keyframes == null || keyframes.Length == 0) return;

            float tickRate = Mathf.Max(1, _header.tickRate);
            int currentFrame = Mathf.FloorToInt(_playbackTime * tickRate);

            int target = keyframes[keyframes.Length - 1];
            for (int i = keyframes.Length - 1; i >= 0; i--)
            {
                if (keyframes[i] > currentFrame)
                    target = keyframes[i];
                else
                    break;
            }

            _isPlaying = false;
            _playbackTime = target / tickRate;
            ApplyPlaybackTime();
            UpdatePlayPauseLabel();
        }

        private void OnSpeedChanged(int index)
        {
            if (index < 0 || index >= SpeedOptions.Length) return;
            _speed = SpeedOptions[index];
        }

        // ---------------------------------------------------------------- labels

        private void UpdatePlayPauseLabel()
        {
            if (_playPauseLabel != null)
                _playPauseLabel.text = _isPlaying ? "Pause" : "Play";
        }

        private void UpdateProgressLabel()
        {
            if (_progressLabel == null) return;
            _progressLabel.text = FormatTime(_playbackTime) + " / " + FormatTime(_header.durationSec);
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        private void SetStatus(string text)
        {
            if (_destroyed || _statusLabel == null) return;
            _statusLabel.text = text ?? "";
        }

        private void SetTransportInteractable(bool interactable)
        {
            LobbyUiKit.SetButtonInteractable(_playPauseButton, interactable);
            LobbyUiKit.SetButtonInteractable(_prevKeyframeButton, interactable);
            LobbyUiKit.SetButtonInteractable(_nextKeyframeButton, interactable);
        }
    }
}
