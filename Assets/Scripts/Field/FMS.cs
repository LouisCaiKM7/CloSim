using System;
using System.Collections;
using TMPro;
using UnityEngine;
using Util;

public class FMS : MonoBehaviour
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

    [Header("Match Sounds")]
    public AudioSource audioSource;
    public AudioClip StartMatch;
    public AudioClip BeginTeleop;
    public AudioClip Shift;
    public AudioClip Endgame;
    public AudioClip End;

    [Header("Menu Sound Blocking")]
    [SerializeField] private OptionsMenuController optionsMenu;

    private MatchState previousMatchState;

    private LoadMatch matchLoader;
    private TextMeshProUGUI timer;
    private TextMeshProUGUI hubTimer;
    private RebuiltShifts hubShifts;
    private TextMeshProUGUI startCountdownText;
    private Coroutine startCountdownCoroutine;
    private bool startCountdownActive;
    private bool scheduledMatchActive;
    private double scheduledMatchStartServerTime = -1d;
    private Func<double> scheduledServerTimeProvider;

    private bool playedStartMatch;
    private bool playedAutoEnd;
    private bool playedBeginTeleop;
    private bool playedShift10;
    private bool playedShift25;
    private bool playedShift50;
    private bool playedShift85;
    private bool playedEndgame;
    private bool playedMatchEnd;

    private bool autoToTeleopPauseStarted;
    private bool matchEndPauseStarted;

    private float previousMatchTimer;
    private float teleopStartMatchTimer;

    public RobotState robotState;

    void OnEnable()
    {
        Restart();
    }

    void Update()
    {
        state = MatchState;
        robotState = RobotState;

        previousMatchTimer = MatchTimer;

        if (scheduledMatchActive)
        {
            ApplyScheduledState();
        }
        else
        {
            if (RobotState == RobotState.enabled && !startCountdownActive)
            {
                MatchTimer -= Time.deltaTime;
            }

            if (!startCountdownActive)
            {
                UpdateMatchState();
            }
        }

        HandleSounds();

        previousMatchState = MatchState;

        float minutes = Mathf.FloorToInt(MatchTimer / 60);
        float seconds = Mathf.FloorToInt(MatchTimer % 60);

        if (minutes < 0) minutes = 0;
        if (seconds < 0) seconds = 0;

        if (timer != null)
        {
            timer.text = $"{minutes:00}:{seconds:00}";
        }
    }

    private void UpdateMatchState()
    {
        float autoEndTime = matchTime - autoTime;

        if (MatchTimer < 0)
        {
            if (!matchEndPauseStarted)
            {
                StartCoroutine(MatchEndPause());
            }

            return;
        }

        if (MatchTimer <= endgameTime)
        {
            MatchState = MatchState.endgame;
        }
        else if (autoToTeleopPauseStarted && playedBeginTeleop)
        {
            MatchState = MatchState.teleop;
        }
        else if (MatchTimer <= autoEndTime && !autoToTeleopPauseStarted)
        {
            StartCoroutine(AutoToTeleopPause());
        }
        else if (MatchTimer > autoEndTime)
        {
            MatchState = MatchState.auto;
        }
    }

    private void HandleSounds()
    {
        float autoEndTime = matchTime - autoTime;

        if (!playedStartMatch && !startCountdownActive)
        {
            PlaySound(StartMatch);
            playedStartMatch = true;
        }

        if (!playedAutoEnd && CrossedTime(autoEndTime))
        {
            PlaySound(End);
            playedAutoEnd = true;
        }

        if (playedBeginTeleop)
        {
            float shift10Time = teleopStartMatchTimer - 9f;
            float shift35Time = teleopStartMatchTimer - 34f;
            float shift60Time = teleopStartMatchTimer - 59f;
            float shift85Time = teleopStartMatchTimer - 84f;

            if (!playedShift10 && CrossedTime(shift10Time))
            {
                PlaySound(Shift);
                playedShift10 = true;
            }

            if (!playedShift25 && CrossedTime(shift35Time))
            {
                PlaySound(Shift);
                playedShift25 = true;
            }

            if (!playedShift50 && CrossedTime(shift60Time))
            {
                PlaySound(Shift);
                playedShift50 = true;
            }

            if (!playedShift85 && CrossedTime(shift85Time))
            {
                PlaySound(Shift);
                playedShift85 = true;
            }
        }

        if (!playedEndgame && CrossedTime(endgameTime))
        {
            PlaySound(Endgame);
            playedEndgame = true;
        }

        if (!playedMatchEnd && CrossedTime(0f))
        {
            PlaySound(End);
            playedMatchEnd = true;
        }
    }

    private IEnumerator AutoToTeleopPause()
    {
        autoToTeleopPauseStarted = true;

        MatchState = MatchState.auto;
        RobotState = RobotState.disabled;

        yield return new WaitForSeconds(autoDisableTime);

        MatchState = MatchState.teleop;
        RobotState = RobotState.enabled;

        teleopStartMatchTimer = MatchTimer;

        if (!playedBeginTeleop)
        {
            PlaySound(BeginTeleop);
            playedBeginTeleop = true;
        }
    }

    private IEnumerator MatchEndPause()
    {
        matchEndPauseStarted = true;
        
        RobotState = RobotState.disabled;

        yield return new WaitForSeconds(matchDisabledTime);

        MatchState = MatchState.finished;
        RobotState = RobotState.enabled;
    }

    private bool CrossedTime(float targetTime)
    {
        return previousMatchTimer > targetTime && MatchTimer <= targetTime;
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

    public void Restart()
    {
        Restart(0f);
    }

    public void Restart(float startCountdownSeconds)
    {
        scheduledMatchActive = false;
        scheduledMatchStartServerTime = -1d;
        scheduledServerTimeProvider = null;

        if (startCountdownCoroutine != null)
        {
            StopCoroutine(startCountdownCoroutine);
            startCountdownCoroutine = null;
        }

        startCountdownActive = false;
        HideStartCountdown();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (optionsMenu == null)
        {
            optionsMenu = FindFirstObjectByType<OptionsMenuController>();
        }

        var dispT = GameObject.Find("TimerDisplay");
        if (dispT != null)
        {
            timer = dispT.GetComponent<TextMeshProUGUI>();
        }

        matchLoader = Utils.FindParentObjectComponent<LoadMatch>(gameObject);
        matchLoader.SetFms(this);
        hubShifts = GetComponentInChildren<RebuiltShifts>(true);
        MatchTimer = matchTime;
        previousMatchTimer = matchTime;
        teleopStartMatchTimer = matchTime - autoTime;

        previousMatchState = MatchState.auto;
        MatchState = MatchState.auto;
        RobotState = startCountdownSeconds > 0f ? RobotState.disabled : RobotState.enabled;

        autoToTeleopPauseStarted = false;
        matchEndPauseStarted = false;

        playedStartMatch = false;
        playedAutoEnd = false;
        playedBeginTeleop = false;
        playedShift10 = false;
        playedShift25 = false;
        playedShift50 = false;
        playedShift85 = false;
        playedEndgame = false;
        playedMatchEnd = false;

        if (startCountdownSeconds > 0f)
        {
            startCountdownCoroutine = StartCoroutine(StartCountdown(startCountdownSeconds));
        }
    }

    public bool HasScheduledMatch => scheduledMatchActive;
    
    public float ScheduledTeleopElapsedSeconds
    {
        get
        {
            if (!scheduledMatchActive)
                return 0f;

            double teleopStartTime = scheduledMatchStartServerTime + autoTime + autoDisableTime;
            return Mathf.Max(0f, (float)(GetScheduledServerTime() - teleopStartTime));
        }
    }

    public float ScheduledSecondsUntilEndgame
    {
        get
        {
            if (!scheduledMatchActive)
                return Mathf.Max(0f, MatchTimer - endgameTime);

            double endgameStartTime = scheduledMatchStartServerTime + autoTime + autoDisableTime + (matchTime - autoTime - endgameTime);
            return Mathf.Max(0f, (float)(endgameStartTime - GetScheduledServerTime()));
        }
    }

    private void ApplyScheduledState()
    {
        double now = GetScheduledServerTime();
        double matchStartTime = scheduledMatchStartServerTime;
        double autoEndTime = matchStartTime + autoTime;
        double teleopStartTime = autoEndTime + autoDisableTime;
        double endgameStartTime = teleopStartTime + (matchTime - autoTime - endgameTime);
        double matchEndTime = teleopStartTime + (matchTime - autoTime);
        double finishedTime = matchEndTime + matchDisabledTime;

        if (now < matchStartTime)
        {
            MatchTimer = matchTime;
            MatchState = MatchState.auto;
            RobotState = RobotState.disabled;
            startCountdownActive = true;
            ShowStartCountdown(Mathf.CeilToInt((float)(matchStartTime - now)).ToString());
            return;
        }

        if (startCountdownActive)
        {
            HideStartCountdown();
            startCountdownActive = false;
        }

        if (!playedStartMatch)
        {
            PlaySound(StartMatch);
            playedStartMatch = true;
        }

        if (now < autoEndTime)
        {
            MatchTimer = Mathf.Max(0f, matchTime - (float)(now - matchStartTime));
            MatchState = MatchState.auto;
            RobotState = RobotState.enabled;
            return;
        }

        if (now < teleopStartTime)
        {
            MatchTimer = matchTime - autoTime;
            MatchState = MatchState.auto;
            RobotState = RobotState.disabled;
            return;
        }

        if (!playedBeginTeleop)
        {
            MatchTimer = matchTime - autoTime;
            teleopStartMatchTimer = MatchTimer;
            PlaySound(BeginTeleop);
            playedBeginTeleop = true;
        }

        if (now < endgameStartTime)
        {
            MatchTimer = Mathf.Max(0f, matchTime - autoTime - (float)(now - teleopStartTime));
            MatchState = MatchState.teleop;
            RobotState = RobotState.enabled;
            return;
        }

        if (now < matchEndTime)
        {
            MatchTimer = Mathf.Max(0f, matchTime - autoTime - (float)(now - teleopStartTime));
            MatchState = MatchState.endgame;
            RobotState = RobotState.enabled;
            return;
        }

        MatchTimer = 0f;
        RobotState = now < finishedTime ? RobotState.disabled : RobotState.enabled;
        MatchState = now < finishedTime ? MatchState.endgame : MatchState.finished;
    }

    private double GetScheduledServerTime()
    {
        return scheduledServerTimeProvider != null
            ? scheduledServerTimeProvider()
            : Time.realtimeSinceStartup;
    }
    
    private IEnumerator StartCountdown(float seconds)
    {
        startCountdownActive = true;
        MatchState = MatchState.auto;
        RobotState = RobotState.disabled;

        PlaySound(StartMatch);
        playedStartMatch = true;

        float remaining = Mathf.Max(0f, seconds);
        while (remaining > 0f)
        {
            ShowStartCountdown(Mathf.CeilToInt(remaining).ToString());
            yield return null;
            remaining -= Time.unscaledDeltaTime;
        }

        HideStartCountdown();
        startCountdownActive = false;
        RobotState = RobotState.enabled;
        previousMatchTimer = MatchTimer;
        startCountdownCoroutine = null;
    }

    private void ShowStartCountdown(string text)
    {
        EnsureStartCountdownDisplay();

        if (startCountdownText == null)
            return;

        startCountdownText.text = text;
        startCountdownText.gameObject.SetActive(true);
    }

    private void HideStartCountdown()
    {
        if (startCountdownText != null)
        {
            startCountdownText.text = string.Empty;
            startCountdownText.gameObject.SetActive(false);
        }
    }

    private void EnsureStartCountdownDisplay()
    {
        if (startCountdownText != null)
            return;

        GameObject existing = GameObject.Find("MatchStartCountdownDisplay");
        if (existing != null)
        {
            startCountdownText = existing.GetComponent<TextMeshProUGUI>();
            if (startCountdownText != null)
                return;
        }

        Canvas canvas = timer != null ? timer.GetComponentInParent<Canvas>() : FindAnyObjectByType<Canvas>();
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

        startCountdownText = countdownObject.GetComponent<TextMeshProUGUI>();
        startCountdownText.raycastTarget = false;
        startCountdownText.text = string.Empty;
        startCountdownText.color = Color.white;
        startCountdownText.alignment = TextAlignmentOptions.Center;
        startCountdownText.enableAutoSizing = true;
        startCountdownText.fontSizeMin = 96f;
        startCountdownText.fontSizeMax = 260f;
        startCountdownText.fontStyle = FontStyles.Bold;
        startCountdownText.outlineWidth = 0.25f;
        startCountdownText.outlineColor = Color.black;

        if (timer != null)
        {
            startCountdownText.font = timer.font;
            startCountdownText.fontSharedMaterial = timer.fontSharedMaterial;
        }

        countdownObject.SetActive(false);
    }
}

[Serializable]
public enum RobotState
{
    enabled,
    disabled,
}

[Serializable]
public enum MatchState
{
    auto,
    teleop,
    endgame,
    finished
}