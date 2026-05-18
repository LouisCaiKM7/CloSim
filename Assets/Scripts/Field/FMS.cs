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

        if (RobotState == RobotState.enabled)
        {
            MatchTimer -= Time.deltaTime;
        }

        UpdateMatchState();
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

        if (!playedStartMatch)
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

        MatchTimer = matchTime;
        previousMatchTimer = matchTime;
        teleopStartMatchTimer = matchTime - autoTime;

        previousMatchState = MatchState.auto;
        MatchState = MatchState.auto;
        RobotState = RobotState.enabled;

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