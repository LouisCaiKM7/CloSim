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
        private Slider _scrubSlider;
        private Button _playPauseButton;
        private TMP_Text _playPauseLabel;
        private Button _prevKeyframeButton;
        private Button _nextKeyframeButton;
        private GamepadDropdown _speedDropdown;

        private string _pendingReplayId;
        private ReplayHeader _header;
        private ReplayTimelineResolver.Result _resolved;
        // Cumulative recorded score over time (seconds, blue, red), ascending — playback drives the field
        // score display from this so the replay shows the EXACT score of the recorded match.
        private (float timeSec, int blue, int red)[] _scoreTimeline = Array.Empty<(float, int, int)>();
        private readonly Dictionary<int, Transform> _spawnedRobots = new();

        // Articulation playback (schemaVersion >= 2): raw frames carry per-robot moving-joint local poses;
        // each spawned robot's descendants are enumerated the SAME way the recorder did (depth-first, excl.
        // root) so a joint's stored index maps back to the right child.
        private ReplayFrame[] _frames = Array.Empty<ReplayFrame>();
        private readonly Dictionary<int, Transform[]> _spawnedRobotDescendants = new();

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
            // NO full-screen background: replay playback must show the live field behind the HUD. Only the
            // header (top) and the transport bar (bottom) are drawn, as slim overlays. Adding a PanelBg here
            // rendered an opaque dim-gray layer OVER the field — that's why playback looked greyed out.

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

            // Draggable timeline: seek anywhere in [0, duration]. Dragging pauses and jumps the robots there.
            _scrubSlider = BuildScrubber(bar.transform);

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

        /// <summary>Builds a horizontal timeline slider (0..1 of the replay duration) wired to seek.</summary>
        private Slider BuildScrubber(Transform parent)
        {
            var go = new GameObject("Scrubber", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            LobbyUiKit.SetSize(go, -1, 26);
            var slider = go.GetComponent<Slider>();
            slider.transition = Selectable.Transition.None;

            var bg = NewUiImage("Background", go.transform, LobbyUiKit.CardBgAlt);
            SetAnchors(bg, new Vector2(0f, 0.3f), new Vector2(1f, 0.7f));

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            SetAnchors(fillArea.GetComponent<RectTransform>(), new Vector2(0f, 0.3f), new Vector2(1f, 0.7f));
            var fill = NewUiImage("Fill", fillArea.transform, LobbyUiKit.Accent);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f);
            fill.sizeDelta = new Vector2(8f, 0f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            SetAnchors(handleArea.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            var handle = NewUiImage("Handle", handleArea.transform, LobbyUiKit.TextPrimary);
            handle.sizeDelta = new Vector2(14f, 26f);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;
            slider.onValueChanged.AddListener(OnScrub);
            return slider;
        }

        private static RectTransform NewUiImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go.GetComponent<RectTransform>();
        }

        private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>User dragged the timeline: pause and jump playback to that fraction of the duration.</summary>
        private void OnScrub(float fraction)
        {
            if (!_sceneReady || _settingScrubFromCode) return;

            _isPlaying = false;
            UpdatePlayPauseLabel();
            _playbackTime = Mathf.Clamp01(fraction) * Mathf.Max(0f, _header.durationSec);
            ApplyPlaybackTime();
            UpdateProgressLabel();
        }

        private bool _settingScrubFromCode;

        /// <summary>Reflect current playback time on the slider without re-triggering OnScrub.</summary>
        private void SyncScrubber()
        {
            if (_scrubSlider == null || _header.durationSec <= 0f) return;
            _settingScrubFromCode = true;
            _scrubSlider.SetValueWithoutNotify(Mathf.Clamp01(_playbackTime / _header.durationSec));
            _settingScrubFromCode = false;
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
                _spawnedRobotDescendants.Clear();
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
            _frames = doc.Frames ?? Array.Empty<ReplayFrame>();
            _resolved = ReplayTimelineResolver.Resolve(doc.Frames);
            _scoreTimeline = BuildScoreTimeline(doc.Frames);

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

            // Guarantee the field is VISIBLE the instant the scene loads — a camera plus no black fade —
            // BEFORE and independent of robot reconstruction. Otherwise a spawn timeout or an empty roster
            // would leave the player staring at a black screen (the previous behaviour).
            Online.Sync.SpectatorCameraController.EnsureFieldOverviewCamera(loadMatch);
            RevealField();

            if (loadMatch == null)
            {
                SetStatus("Loaded field scene has no LoadMatch; cannot reconstruct robots.");
                return;
            }

            StartCoroutine(PrepareFieldAndSpawn(loadMatch));
        }

        /// <summary>Clear any leftover black fade overlay so the field is visible during playback.</summary>
        private void RevealField()
        {
            var fader = FindFirstObjectByType<ScreenFader>();
            if (fader != null)
                fader.SetBlackImmediate(false);
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
                // Field-ready never fired, but the overview camera is already guaranteed (OnFieldSceneLoaded),
                // so the field is visible rather than black. Attempt the spawn anyway — the field is usually
                // usable — instead of hard-failing to a blank screen.
                Debug.LogWarning("[ReplayPlaybackScreen] OnOnlineFieldReady timed out; showing the field and " +
                                 "attempting playback spawn anyway.");
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
            _spawnedRobotDescendants.Clear();

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
                _spawnedRobotDescendants[entry.slotIndex] = EnumerateDescendants(robot.transform);
                if (cameraSlot < 0)
                    cameraSlot = entry.slotIndex;
            }

            if (cameraSlot >= 0)
            {
                loadMatch.AddOnlineCamera(cameraSlot, Cameras.ThirdPerson);
                // A third-person FOLLOW camera now tracks the recorded robot. Turn off the static overview
                // fallback (added on field load) so the follow cam is the active view — otherwise the static
                // overview lingers and the camera looks like it isn't following the robot.
                DeactivateOverviewFallbackIfFollowCameraExists();
            }

            // Guarantee the field is visible even if no robot camera was set up (empty roster, unresolved
            // prefab, etc.) — otherwise playback is a black screen.
            Online.Sync.SpectatorCameraController.EnsureFieldOverviewCamera(loadMatch);

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
            SyncScrubber();
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

            ApplyJoints(i0, i1, t);
            ApplyRecordedScore();
        }

        // ---------------------------------------------------------------- articulation (mechanisms)

        /// <summary>Enumerate a robot's descendant transforms depth-first (excl. root), matching the recorder.</summary>
        private static Transform[] EnumerateDescendants(Transform root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var desc = new List<Transform>(all.Length);
            foreach (Transform t in all)
                if (t != root)
                    desc.Add(t);
            return desc.ToArray();
        }

        /// <summary>
        /// Applies recorded moving-joint LOCAL poses onto each replay robot's children so mechanisms
        /// (arms/intake/wheels) animate as recorded. Interpolates local pos/rot between frame i0 and i1.
        /// No-op for older (root-only) replays whose frames carry no jointSets.
        /// </summary>
        private void ApplyJoints(int i0, int i1, float t)
        {
            if (_frames == null || _frames.Length == 0)
                return;

            ReplayFrame f0 = _frames[Mathf.Clamp(i0, 0, _frames.Length - 1)];
            if (f0.jointSets == null || f0.jointSets.Length == 0)
                return; // this frame (and typically this replay) has no articulation channel

            ReplayFrame f1 = _frames[Mathf.Clamp(i1, 0, _frames.Length - 1)];

            foreach (ReplayRobotJoints set in f0.jointSets)
            {
                if (set.joints == null)
                    continue;
                if (!_spawnedRobotDescendants.TryGetValue(set.slotIndex, out Transform[] descendants))
                    continue;

                foreach (ReplayJoint joint in set.joints)
                {
                    if (joint.jointIndex < 0 || joint.jointIndex >= descendants.Length)
                        continue; // hierarchy mismatch — skip rather than mis-apply
                    Transform child = descendants[joint.jointIndex];
                    if (child == null)
                        continue;

                    Vector3 posA = JointPos(joint);
                    Quaternion rotA = JointRot(joint);

                    if (TryFindJoint(f1, set.slotIndex, joint.jointIndex, out ReplayJoint jb))
                    {
                        child.localPosition = Vector3.Lerp(posA, JointPos(jb), t);
                        child.localRotation = Quaternion.Slerp(rotA, JointRot(jb), t);
                    }
                    else
                    {
                        child.localPosition = posA;
                        child.localRotation = rotA;
                    }
                }
            }
        }

        private static bool TryFindJoint(ReplayFrame frame, int slotIndex, int jointIndex, out ReplayJoint joint)
        {
            joint = default;
            if (frame.jointSets == null)
                return false;
            foreach (ReplayRobotJoints set in frame.jointSets)
            {
                if (set.slotIndex != slotIndex || set.joints == null)
                    continue;
                foreach (ReplayJoint j in set.joints)
                {
                    if (j.jointIndex == jointIndex)
                    {
                        joint = j;
                        return true;
                    }
                }
                return false;
            }
            return false;
        }

        private static Vector3 JointPos(ReplayJoint j) => new Vector3(
            j.posX / (float)ReplayFormat.PositionUnitsPerMeter,
            j.posY / (float)ReplayFormat.PositionUnitsPerMeter,
            j.posZ / (float)ReplayFormat.PositionUnitsPerMeter);

        private static Quaternion JointRot(ReplayJoint j)
        {
            var q = new Quaternion(j.rotX, j.rotY, j.rotZ, j.rotW);
            // Guard against an un-normalized/zero quaternion (defensive; recorder writes unit quats).
            float m = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return m > 1e-6f ? q : Quaternion.identity;
        }

        // Drives the live field score display (ScoreHolder.BlueScore/RedScore, which the scene's score text
        // reads every frame) from the recorded score timeline, so the replay shows the recorded match's exact
        // score. The replay field never re-scores (robots are kinematic, no piece interactions), so nothing
        // overwrites these values.
        private void ApplyRecordedScore()
        {
            if (_scoreTimeline.Length == 0)
                return;

            int blue = 0, red = 0;
            for (int i = 0; i < _scoreTimeline.Length; i++)
            {
                if (_scoreTimeline[i].timeSec > _playbackTime)
                    break;
                blue = _scoreTimeline[i].blue;
                red = _scoreTimeline[i].red;
            }

            Field.Scoring.ScoreHolder.BlueScore = blue;
            Field.Scoring.ScoreHolder.RedScore = red;
        }

        // Folds the Score events across all frames into a cumulative (time, blue, red) timeline. Each Score
        // event carries the absolute score for one alliance (intValue) — see the recorders.
        private static (float, int, int)[] BuildScoreTimeline(ReplayFrame[] frames)
        {
            var points = new List<(float, int, int)>();
            int blue = 0, red = 0;
            frames ??= Array.Empty<ReplayFrame>();

            foreach (ReplayFrame f in frames)
            {
                ReplayEvent[] events = f.events;
                if (events == null)
                    continue;

                bool changed = false;
                foreach (ReplayEvent e in events)
                {
                    if (e.type != ReplayEventType.Score)
                        continue;
                    if (e.alliance == Online.Contracts.RoomAlliance.Blue) blue = e.intValue;
                    else if (e.alliance == Online.Contracts.RoomAlliance.Red) red = e.intValue;
                    changed = true;
                }

                if (changed)
                    points.Add((f.timestampMs / 1000f, blue, red));
            }

            return points.ToArray();
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

        /// <summary>
        /// If a real follow camera exists (any enabled camera that isn't the static overview fallback),
        /// deactivate the fallback so the follow cam is the sole/active view. Safe: if no follow camera was
        /// created (e.g. AddOnlineCamera couldn't resolve the robot), the fallback is left on so the field
        /// never goes cameraless (black).
        /// </summary>
        private static void DeactivateOverviewFallbackIfFollowCameraExists()
        {
            GameObject fallback = GameObject.Find(nameof(Online.Sync.SpectatorCameraController) + "_Fallback");
            if (fallback == null) return;

            foreach (Camera cam in Camera.allCameras) // enabled cameras on active GameObjects only
            {
                if (cam.gameObject != fallback)
                {
                    fallback.SetActive(false);
                    return;
                }
            }
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
            if (_scrubSlider != null) _scrubSlider.interactable = interactable;
        }
    }
}
