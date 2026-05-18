using System;
using UnityEngine;
using Random = System.Random;

public class RebuiltShifts : ScoreOnlyOnce
{
    public static CurrentShift ActiveShift { get; private set; } = CurrentShift.Auto;
    public static bool BlueOwnsOddShifts { get; private set; } = true;
    
    [SerializeField] private CurrentShift currentShift;

    [Header("End Disable Scoring")]
    [SerializeField] private float endDisableScoreTime = 3f;

    private bool blueWonAuto;
    private float shiftTimer;
    private float endDisableScoreTimer;
    private MatchState previousMatchState;
    
    [SerializeField] private GameObject shiftOnLight;

    private void Start()
    {
        blueWonAuto = false;
        shiftTimer = 3f;
        endDisableScoreTimer = 0f;
        currentShift = CurrentShift.Auto;
        previousMatchState = MatchState.auto;
        ActiveShift = currentShift;
        BlueOwnsOddShifts = true;
    }

    private new void FixedUpdate()
    {
        handleShiftState();

        bool shouldScore = isOnShift();

        // Keep scoring active during the match-end disabled period.
        if (FMS.MatchState == MatchState.finished && endDisableScoreTimer > 0f)
        {
            shouldScore = true;
            endDisableScoreTimer -= Time.fixedDeltaTime;
        }

        if (shiftOnLight != null)
        {
            shiftOnLight.SetActive(shouldScore);
        }

        poolOccupyObjects();

        compareObjects(shouldScore);

        ScorePoints(totalScore);
    }

    private void handleShiftState()
    {
        // Auto just ended.
        if (FMS.MatchState != MatchState.auto && previousMatchState == MatchState.auto)
        {
            if (ScoreHolder.BlueScore > ScoreHolder.RedScore)
            {
                blueWonAuto = true;
            }
            else if (ScoreHolder.BlueScore == ScoreHolder.RedScore)
            {
                var rng = new Random();
                blueWonAuto = rng.Next(0, 2) == 1;
            }

            BlueOwnsOddShifts = !blueWonAuto;

            currentShift = CurrentShift.Auto;
        }

        // Normal teleop shift timing.
        if (FMS.MatchState == MatchState.teleop)
        {
            shiftTimer -= Time.fixedDeltaTime;

            if (shiftTimer <= 0f && currentShift < CurrentShift.EndGame)
            {
                shiftTimer = currentShift == CurrentShift.Auto ? 6.5f : 25f;
                currentShift += 1;
            }
        }

        // Endgame remains active through normal endgame.
        if (FMS.MatchState == MatchState.endgame)
        {
            currentShift = CurrentShift.EndGame;
        }

        // Finished is the final disabled period.
        // Keep the shift as EndGame instead of letting it advance past the enum.
        if (FMS.MatchState == MatchState.finished)
        {
            currentShift = CurrentShift.EndGame;

            if (previousMatchState != MatchState.finished)
            {
                endDisableScoreTimer = endDisableScoreTime;
            }
        }

        previousMatchState = FMS.MatchState;
        ActiveShift = currentShift;
    }

    private bool isOnShift()
    {
        var isBlue = GetIsBlue();

        if (blueWonAuto)
        {
            if (isBlue)
            {
                return currentShift is CurrentShift.Auto
                    or CurrentShift.Transition
                    or CurrentShift.Shift2
                    or CurrentShift.Shift4
                    or CurrentShift.EndGame;
            }
            else
            {
                return currentShift is CurrentShift.Auto
                    or CurrentShift.Transition
                    or CurrentShift.Shift1
                    or CurrentShift.Shift3
                    or CurrentShift.EndGame;
            }
        }
        else
        {
            if (isBlue)
            {
                return currentShift is CurrentShift.Auto
                    or CurrentShift.Transition
                    or CurrentShift.Shift1
                    or CurrentShift.Shift3
                    or CurrentShift.EndGame;
            }
            else
            {
                return currentShift is CurrentShift.Auto
                    or CurrentShift.Transition
                    or CurrentShift.Shift2
                    or CurrentShift.Shift4
                    or CurrentShift.EndGame;
            }
        }
    }

    [Serializable]
    public enum CurrentShift
    {
        Auto,
        Transition,
        Shift1,
        Shift2,
        Shift3,
        Shift4,
        EndGame,
    }
}