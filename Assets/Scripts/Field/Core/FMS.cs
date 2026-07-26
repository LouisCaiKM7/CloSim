using System;
using System.Collections;
using Core;
using Field.SeasonSpecific.Rebuilt;
using TMPro;
using UI.MainMenu;
using UnityEngine;
using UnityEngine.Serialization;
using Utilities;

namespace Field.Core
{
    public class Fms : MonoBehaviour
    {
        public int matchTime = 150;
        public int autoTime = 15;
        public float autoDisableTime = 3f;
        public int endgameTime = 20;
        public float matchDisabledTime = 3f;

        public GameObject[] blueStationCams;
        public GameObject[] redStationCams;

        public static float MatchTimer;
        public static RobotState RobotState;
        public static MatchState MatchState;
        public MatchState state;

        // ONLINE (additive): when true, this Fms is under network authority and must NOT run the
        // independent client-side countdown in Update(). It is set by the Online.Sync flow layer
        // (MatchFlowSync) on every networked machine and reset to false when leaving the network.
        // Offline this stays false, so the legacy countdown branch runs exactly as before.
        public static bool NetworkAuthoritative;

        [Header("Match Sounds")]
        public AudioSource audioSource;
        [FormerlySerializedAs("StartMatch")] public AudioClip startMatch;
        [FormerlySerializedAs("BeginTeleop")] public AudioClip beginTeleop;
        [FormerlySerializedAs("Shift")] public AudioClip shift;
        [FormerlySerializedAs("Endgame")] public AudioClip endgame;
        [FormerlySerializedAs("End")] public AudioClip end;

        [Header("Menu Sound Blocking")]
        [SerializeField] private OptionsMenuController optionsMenu;

        private LoadMatch _matchLoader;
        private TextMeshProUGUI _timer;
        private TextMeshProUGUI _hubTimer;
        private TextMeshProUGUI _startCountdownText;
        private Coroutine _startCountdownCoroutine;
        private bool _startCountdownActive;
        private bool _scheduledMatchActive;
        private double _scheduledMatchStartServerTime = -1d;
        private Func<double> _scheduledServerTimeProvider;

        private bool _playedStartMatch;
        private bool _playedAutoEnd;
        private bool _playedBeginTeleop;
        private bool _playedShift10;
        private bool _playedShift25;
        private bool _playedShift50;
        private bool _playedShift85;
        private bool _playedEndgame;
        private bool _playedMatchEnd;

        private bool _autoToTeleopPauseStarted;
        private bool _matchEndPauseStarted;

        private float _previousMatchTimer;
        private float _teleopStartMatchTimer;

        public RobotState robotState;

        void OnEnable()
        {
            Restart();
        }

        void Update()
        {
            state = MatchState;
            robotState = RobotState;

            _previousMatchTimer = MatchTimer;

            if (_scheduledMatchActive)
            {
                ApplyScheduledState();
            }
            else if (!NetworkAuthoritative)
            {
                // Offline / non-networked path (byte-identical to legacy behavior): advance the local
                // countdown independently. When NetworkAuthoritative is true but the scheduled match has
                // not started yet, we intentionally do NOTHING here so clients never independently drive
                // match state — authoritative timing arrives via StartScheduled()/ApplyScheduledState().
                if (RobotState == RobotState.Enabled && !_startCountdownActive)
                {
                    MatchTimer -= Time.deltaTime;
                }

                if (!_startCountdownActive)
                {
                    UpdateMatchState();
                }
            }

            HandleSounds();

            float minutes = Mathf.FloorToInt(MatchTimer / 60);
            float seconds = Mathf.FloorToInt(MatchTimer % 60);

            if (minutes < 0) minutes = 0;
            if (seconds < 0) seconds = 0;

            if (_timer != null)
            {
                _timer.text = $"{minutes:00}:{seconds:00}";
            }
        }

        private void UpdateMatchState()
        {
            float autoEndTime = matchTime - autoTime;

            if (MatchTimer < 0)
            {
                if (!_matchEndPauseStarted)
                {
                    StartCoroutine(MatchEndPause());
                }

                return;
            }

            if (MatchTimer <= endgameTime)
            {
                MatchState = MatchState.Endgame;
            }
            else if (_autoToTeleopPauseStarted && _playedBeginTeleop)
            {
                MatchState = MatchState.Teleop;
            }
            else if (MatchTimer <= autoEndTime && !_autoToTeleopPauseStarted)
            {
                StartCoroutine(AutoToTeleopPause());
            }
            else if (MatchTimer > autoEndTime)
            {
                MatchState = MatchState.Auto;
            }
        }

        private void HandleSounds()
        {
            float autoEndTime = matchTime - autoTime;

            if (!_playedStartMatch && !_startCountdownActive)
            {
                PlaySound(startMatch);
                _playedStartMatch = true;
            }

            if (!_playedAutoEnd && CrossedTime(autoEndTime))
            {
                PlaySound(end);
                _playedAutoEnd = true;
            }

            if (_playedBeginTeleop)
            {
                float shift10Time = _teleopStartMatchTimer - 9f;
                float shift35Time = _teleopStartMatchTimer - 34f;
                float shift60Time = _teleopStartMatchTimer - 59f;
                float shift85Time = _teleopStartMatchTimer - 84f;

                if (!_playedShift10 && CrossedTime(shift10Time))
                {
                    PlaySound(shift);
                    _playedShift10 = true;
                }

                if (!_playedShift25 && CrossedTime(shift35Time))
                {
                    PlaySound(shift);
                    _playedShift25 = true;
                }

                if (!_playedShift50 && CrossedTime(shift60Time))
                {
                    PlaySound(shift);
                    _playedShift50 = true;
                }

                if (!_playedShift85 && CrossedTime(shift85Time))
                {
                    PlaySound(shift);
                    _playedShift85 = true;
                }
            }

            if (!_playedEndgame && CrossedTime(endgameTime))
            {
                PlaySound(endgame);
                _playedEndgame = true;
            }

            if (!_playedMatchEnd && CrossedTime(0f))
            {
                PlaySound(end);
                _playedMatchEnd = true;
            }
        }

        private IEnumerator AutoToTeleopPause()
        {
            _autoToTeleopPauseStarted = true;

            MatchState = MatchState.Auto;
            RobotState = RobotState.Disabled;

            yield return new WaitForSeconds(autoDisableTime);

            MatchState = MatchState.Teleop;
            RobotState = RobotState.Enabled;

            _teleopStartMatchTimer = MatchTimer;

            if (!_playedBeginTeleop)
            {
                PlaySound(beginTeleop);
                _playedBeginTeleop = true;
            }
        }

        private IEnumerator MatchEndPause()
        {
            _matchEndPauseStarted = true;
        
            RobotState = RobotState.Disabled;

            yield return new WaitForSeconds(matchDisabledTime);

            MatchState = MatchState.Finished;
            RobotState = RobotState.Enabled;
        }

        private bool CrossedTime(float targetTime)
        {
            return _previousMatchTimer > targetTime && MatchTimer <= targetTime;
        }

        private void PlaySound(AudioClip clip)
        {
            if (IsMenuOpen())
                return;

            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        private bool IsMenuOpen()
        {
            if (optionsMenu == null)
                optionsMenu = FindFirstObjectByType<OptionsMenuController>();

            return optionsMenu != null && optionsMenu.IsOpen();
        }

        public void Restart(float startCountdownSeconds = 0f)
        {
            _scheduledMatchActive = false;
            _scheduledMatchStartServerTime = -1d;
            _scheduledServerTimeProvider = null;

            if (_startCountdownCoroutine != null)
            {
                StopCoroutine(_startCountdownCoroutine);
                _startCountdownCoroutine = null;
            }

            _startCountdownActive = false;
            HideStartCountdown();

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (optionsMenu == null)
            {
                optionsMenu = FindFirstObjectByType<OptionsMenuController>();
            }

            var displayTimer = GameObject.Find("TimerDisplay");
            if (displayTimer != null)
            {
                _timer = displayTimer.GetComponent<TextMeshProUGUI>();
            }

            _matchLoader = Utils.FindParentObjectComponent<LoadMatch>(gameObject);
            _matchLoader.SetFms(this);
            GetComponentInChildren<RebuiltShifts>(true);
            MatchTimer = matchTime;
            _previousMatchTimer = matchTime;
            _teleopStartMatchTimer = matchTime - autoTime;

            MatchState = MatchState.Auto;
            RobotState = startCountdownSeconds > 0f ? RobotState.Disabled : RobotState.Enabled;

            _autoToTeleopPauseStarted = false;
            _matchEndPauseStarted = false;

            _playedStartMatch = false;
            _playedAutoEnd = false;
            _playedBeginTeleop = false;
            _playedShift10 = false;
            _playedShift25 = false;
            _playedShift50 = false;
            _playedShift85 = false;
            _playedEndgame = false;
            _playedMatchEnd = false;

            if (startCountdownSeconds > 0f)
            {
                _startCountdownCoroutine = StartCoroutine(StartCountdown(startCountdownSeconds));
            }
        }

        /// <summary>
        /// ONLINE (additive): activates the existing server-time scheduled-start path. Called on every
        /// networked machine (host and clients) by the Online.Sync flow layer with the SAME
        /// <paramref name="serverStartTime"/> and a provider that reads the synchronized network clock,
        /// so <see cref="ApplyScheduledState"/> produces identical timer/state everywhere with no parallel
        /// timer. Does not alter any scoring/match-rule math. Restart() clears these fields back to inactive.
        /// </summary>
        public void StartScheduled(double serverStartTime, Func<double> serverTimeProvider)
        {
            _scheduledMatchActive = true;
            _scheduledMatchStartServerTime = serverStartTime;
            _scheduledServerTimeProvider = serverTimeProvider;
        }

        public bool HasScheduledMatch => _scheduledMatchActive;
    
        public float ScheduledTeleopElapsedSeconds
        {
            get
            {
                if (!_scheduledMatchActive)
                    return 0f;

                double teleopStartTime = _scheduledMatchStartServerTime + autoTime + autoDisableTime;
                return Mathf.Max(0f, (float)(GetScheduledServerTime() - teleopStartTime));
            }
        }

        public float ScheduledSecondsUntilEndgame
        {
            get
            {
                if (!_scheduledMatchActive)
                    return Mathf.Max(0f, MatchTimer - endgameTime);

                double endgameStartTime = _scheduledMatchStartServerTime + autoTime + autoDisableTime + (matchTime - autoTime - endgameTime);
                return Mathf.Max(0f, (float)(endgameStartTime - GetScheduledServerTime()));
            }
        }

        private void ApplyScheduledState()
        {
            double now = GetScheduledServerTime();
            double matchStartTime = _scheduledMatchStartServerTime;
            double autoEndTime = matchStartTime + autoTime;
            double teleopStartTime = autoEndTime + autoDisableTime;
            double endgameStartTime = teleopStartTime + (matchTime - autoTime - endgameTime);
            double matchEndTime = teleopStartTime + (matchTime - autoTime);
            double finishedTime = matchEndTime + matchDisabledTime;

            if (now < matchStartTime)
            {
                MatchTimer = matchTime;
                MatchState = MatchState.Auto;
                RobotState = RobotState.Disabled;
                _startCountdownActive = true;
                ShowStartCountdown(Mathf.CeilToInt((float)(matchStartTime - now)).ToString());
                return;
            }

            if (_startCountdownActive)
            {
                HideStartCountdown();
                _startCountdownActive = false;
            }

            if (!_playedStartMatch)
            {
                PlaySound(startMatch);
                _playedStartMatch = true;
            }

            if (now < autoEndTime)
            {
                MatchTimer = Mathf.Max(0f, matchTime - (float)(now - matchStartTime));
                MatchState = MatchState.Auto;
                RobotState = RobotState.Enabled;
                return;
            }

            if (now < teleopStartTime)
            {
                MatchTimer = matchTime - autoTime;
                MatchState = MatchState.Auto;
                RobotState = RobotState.Disabled;
                return;
            }

            if (!_playedBeginTeleop)
            {
                MatchTimer = matchTime - autoTime;
                _teleopStartMatchTimer = MatchTimer;
                PlaySound(beginTeleop);
                _playedBeginTeleop = true;
            }

            if (now < endgameStartTime)
            {
                MatchTimer = Mathf.Max(0f, matchTime - autoTime - (float)(now - teleopStartTime));
                MatchState = MatchState.Teleop;
                RobotState = RobotState.Enabled;
                return;
            }

            if (now < matchEndTime)
            {
                MatchTimer = Mathf.Max(0f, matchTime - autoTime - (float)(now - teleopStartTime));
                MatchState = MatchState.Endgame;
                RobotState = RobotState.Enabled;
                return;
            }

            MatchTimer = 0f;
            RobotState = now < finishedTime ? RobotState.Disabled : RobotState.Enabled;
            MatchState = now < finishedTime ? MatchState.Endgame : MatchState.Finished;
        }

        private double GetScheduledServerTime()
        {
            return _scheduledServerTimeProvider?.Invoke() ?? Time.realtimeSinceStartup;
        }
    
        private IEnumerator StartCountdown(float seconds)
        {
            _startCountdownActive = true;
            MatchState = MatchState.Auto;
            RobotState = RobotState.Disabled;

            PlaySound(startMatch);
            _playedStartMatch = true;

            float remaining = Mathf.Max(0f, seconds);
            while (remaining > 0f)
            {
                ShowStartCountdown(Mathf.CeilToInt(remaining).ToString());
                yield return null;
                remaining -= Time.unscaledDeltaTime;
            }

            HideStartCountdown();
            _startCountdownActive = false;
            RobotState = RobotState.Enabled;
            _previousMatchTimer = MatchTimer;
            _startCountdownCoroutine = null;
        }

        private void ShowStartCountdown(string text)
        {
            EnsureStartCountdownDisplay();

            if (_startCountdownText == null)
                return;

            _startCountdownText.text = text;
            _startCountdownText.gameObject.SetActive(true);
        }

        private void HideStartCountdown()
        {
            if (_startCountdownText != null)
            {
                _startCountdownText.text = string.Empty;
                _startCountdownText.gameObject.SetActive(false);
            }
        }

        private void EnsureStartCountdownDisplay()
        {
            if (_startCountdownText != null)
                return;

            GameObject existing = GameObject.Find("MatchStartCountdownDisplay");
            if (existing != null)
            {
                _startCountdownText = existing.GetComponent<TextMeshProUGUI>();
                if (_startCountdownText != null)
                    return;
            }

            Canvas canvas = _timer != null ? _timer.GetComponentInParent<Canvas>() : FindAnyObjectByType<Canvas>();
            if (canvas == null)
                return;

            GameObject countdownObject = new GameObject(
                "MatchStartCountdownDisplay",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            countdownObject.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = countdownObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            _startCountdownText = countdownObject.GetComponent<TextMeshProUGUI>();
            _startCountdownText.raycastTarget = false;
            _startCountdownText.text = string.Empty;
            _startCountdownText.color = Color.white;
            _startCountdownText.alignment = TextAlignmentOptions.Center;
            _startCountdownText.enableAutoSizing = true;
            _startCountdownText.fontSizeMin = 96f;
            _startCountdownText.fontSizeMax = 260f;
            _startCountdownText.fontStyle = FontStyles.Bold;
            _startCountdownText.outlineWidth = 0.25f;
            _startCountdownText.outlineColor = Color.black;

            if (_timer != null)
            {
                _startCountdownText.font = _timer.font;
                _startCountdownText.fontSharedMaterial = _timer.fontSharedMaterial;
            }

            countdownObject.SetActive(false);
        }
    }

    [Serializable]
    public enum RobotState
    {
        Enabled,
        Disabled,
    }

    [Serializable]
    public enum MatchState
    {
        Auto,
        Teleop,
        Endgame,
        Finished
    }
}