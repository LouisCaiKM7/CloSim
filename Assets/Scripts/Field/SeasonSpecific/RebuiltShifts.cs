using System;
using UnityEngine;

public class RebuiltShifts : ScoreOnlyOnce
{
    public static CurrentShift ActiveShift { get; private set; } = CurrentShift.Auto;
    public static bool BlueOwnsOddShifts { get; private set; } = true;

    private const float InitialShiftDelay = 0f;
    private const float TransitionDuration = 10f;
    private const float AllianceShiftDuration = 25f;

    [SerializeField] private CurrentShift currentShift;
    [SerializeField] private float deactivationCountingGraceTime = 3f;

    [Header("End Disable Scoring")]
    [SerializeField] private float endDisableScoreTime = 3f;

    private static bool autoWinnerResolved;
    private static bool blueWonAuto;
    private static bool pendingAutoWinnerResolve;
    private float shiftTimer;
    private float endDisableScoreTimer;
    private MatchState previousMatchState;
    private float blueCountingGraceTimer;
    private float redCountingGraceTimer;
    private bool previousBlueActive;
    private bool previousRedActive;

    [Header("Shift Light")]
    [SerializeField] private GameObject shiftOnLight;
    [SerializeField] private Material finishedShiftLightMaterial;

    [Header("Shift Light Blink / Fade")]
    [SerializeField] private float shiftLightBlinkStartTime = 3f;
    [SerializeField] private float shiftLightBlinkInterval = 0.5f;

    [Tooltip("Lowest visible alpha during the fade blink. Use 0 for fully invisible.")]
    [SerializeField] private float shiftLightMinAlpha = 0f;

    [Tooltip("Highest alpha during the fade blink.")]
    [SerializeField] private float shiftLightMaxAlpha = 1f;

    [Tooltip("Lowest emission multiplier during the fade blink.")]
    [SerializeField] private float shiftLightMinEmissionMultiplier = 0f;

    [Tooltip("Highest emission multiplier during the fade blink.")]
    [SerializeField] private float shiftLightMaxEmissionMultiplier = 1f;

    [Tooltip("Smooths the fade curve instead of using a linear triangle wave.")]
    [SerializeField] private bool smoothBlinkFade = true;

    [Tooltip("Finished light emission brightness relative to the normal shift light. 0.5 means half as bright.")]
    [SerializeField] private float finishedShiftLightEmissionRelativeBrightness = 0.5f;
    
    private Renderer[] shiftLightRenderers = Array.Empty<Renderer>();

    private Material[][] originalShiftLightSharedMaterials = Array.Empty<Material[]>();
    private Material[][] runtimeShiftLightMaterials = Array.Empty<Material[]>();
    private Material[][] runtimeFinishedShiftLightMaterials = Array.Empty<Material[]>();

    private Color[][] originalBaseColors = Array.Empty<Color[]>();
    private Color[][] originalEmissionColors = Array.Empty<Color[]>();
    private Color[][] finishedBaseColors = Array.Empty<Color[]>();
    private Color[][] finishedEmissionColors = Array.Empty<Color[]>();

    private bool finishedLightMaterialApplied;

    private FMS cachedFms;

    private void Start()
    {
        autoWinnerResolved = false;
        blueWonAuto = false;
        pendingAutoWinnerResolve = false;

        shiftTimer = InitialShiftDelay;
        endDisableScoreTimer = 0f;
        currentShift = CurrentShift.Auto;
        previousMatchState = MatchState.auto;
        ActiveShift = currentShift;
        BlueOwnsOddShifts = true;
        blueCountingGraceTimer = 0f;
        redCountingGraceTimer = 0f;

        CacheShiftLightRenderers();

        cachedFms = FindFirstObjectByType<FMS>(FindObjectsInactive.Include);

        previousBlueActive = IsHubScheduledActive(true);
        previousRedActive = IsHubScheduledActive(false);
    }

    private new void FixedUpdate()
    {
        poolOccupyObjects();

        handleShiftState();
        UpdateCountingGraceTimers();

        bool shouldScore = IsHubCounting(GetIsBlue());
        if (FMS.MatchState == MatchState.finished && endDisableScoreTimer > 0f)
        {
            shouldScore = true;
            endDisableScoreTimer -= Time.fixedDeltaTime;
        }

        float shiftLightFade = GetShiftLightFade(GetIsBlue());
        UpdateShiftLightVisuals(shiftLightFade);

        compareObjects(shouldScore);

        ScorePoints(totalScore);
    }
    
    private void LateUpdate()
    {
        if (!pendingAutoWinnerResolve)
            return;

        ResolveAutoWinner();
        pendingAutoWinnerResolve = false;
    }

    private void handleShiftState()
    {
        if (ApplyScheduledShiftStateIfAvailable())
        {
            if (FMS.MatchState != MatchState.auto && previousMatchState == MatchState.auto)
                pendingAutoWinnerResolve = true;

            previousMatchState = FMS.MatchState;
            ActiveShift = currentShift;
            return;
        }

        // Auto just ended.
        if (FMS.MatchState != MatchState.auto && previousMatchState == MatchState.auto)
        {
            pendingAutoWinnerResolve = true;

            currentShift = CurrentShift.Auto;
            shiftTimer = InitialShiftDelay;
        }

        // Normal teleop shift timing.
        if (FMS.MatchState == MatchState.teleop)
        {
            shiftTimer -= Time.fixedDeltaTime;

            if (shiftTimer <= 0f && currentShift < CurrentShift.EndGame)
            {
                shiftTimer = currentShift == CurrentShift.Auto ? TransitionDuration : AllianceShiftDuration;
                currentShift = NextShift(currentShift);
            }
        }

        // Endgame remains active through normal endgame.
        if (FMS.MatchState == MatchState.endgame)
        {
            currentShift = CurrentShift.EndGame;
            shiftTimer = Mathf.Max(0f, FMS.MatchTimer);
        }

        // Finished is reached only after the match-end disabled pause.
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

    private void ResolveAutoWinner()
    {
        if (autoWinnerResolved)
            return;

        autoWinnerResolved = true;

        int blueAutoFuel = BlueFuel;
        int redAutoFuel = RedFuel;

        if (blueAutoFuel > redAutoFuel)
        {
            blueWonAuto = true;
        }
        else if (blueAutoFuel < redAutoFuel)
        {
            blueWonAuto = false;
        }
        else
        {
            blueWonAuto = UnityEngine.Random.Range(0, 2) == 1;
        }

        BlueOwnsOddShifts = !blueWonAuto;
    }

    private bool ApplyScheduledShiftStateIfAvailable()
    {
        if (cachedFms == null)
            cachedFms = FindFirstObjectByType<FMS>(FindObjectsInactive.Include);

        if (cachedFms == null || !cachedFms.HasScheduledMatch)
            return false;

        if (FMS.MatchState == MatchState.auto)
        {
            currentShift = CurrentShift.Auto;
            shiftTimer = 0f;
            return true;
        }

        if (FMS.MatchState == MatchState.endgame || FMS.MatchState == MatchState.finished)
        {
            currentShift = CurrentShift.EndGame;
            shiftTimer = Mathf.Max(0f, FMS.MatchTimer);
            return true;
        }

        float teleopElapsed = cachedFms.ScheduledTeleopElapsedSeconds;

        if (teleopElapsed < TransitionDuration)
        {
            currentShift = CurrentShift.Transition;
            shiftTimer = TransitionDuration - teleopElapsed;
        }
        else if (teleopElapsed < TransitionDuration + AllianceShiftDuration)
        {
            currentShift = CurrentShift.Shift1;
            shiftTimer = TransitionDuration + AllianceShiftDuration - teleopElapsed;
        }
        else if (teleopElapsed < TransitionDuration + AllianceShiftDuration * 2f)
        {
            currentShift = CurrentShift.Shift2;
            shiftTimer = TransitionDuration + AllianceShiftDuration * 2f - teleopElapsed;
        }
        else if (teleopElapsed < TransitionDuration + AllianceShiftDuration * 3f)
        {
            currentShift = CurrentShift.Shift3;
            shiftTimer = TransitionDuration + AllianceShiftDuration * 3f - teleopElapsed;
        }
        else
        {
            currentShift = CurrentShift.Shift4;
            shiftTimer = cachedFms.ScheduledSecondsUntilEndgame;
        }

        return true;
    }

    private void UpdateCountingGraceTimers()
    {
        bool blueActive = IsHubScheduledActive(true);
        bool redActive = IsHubScheduledActive(false);

        blueCountingGraceTimer = UpdateCountingGraceTimer(blueCountingGraceTimer, previousBlueActive, blueActive);
        redCountingGraceTimer = UpdateCountingGraceTimer(redCountingGraceTimer, previousRedActive, redActive);

        previousBlueActive = blueActive;
        previousRedActive = redActive;
    }

    private float UpdateCountingGraceTimer(float timer, bool wasActive, bool isActive)
    {
        if (isActive)
            return 0f;

        if (wasActive)
            return deactivationCountingGraceTime;

        return Mathf.Max(0f, timer - Time.fixedDeltaTime);
    }

    private bool IsHubCounting(bool allianceIsBlue)
    {
        return IsHubScheduledActive(allianceIsBlue) || GetCountingGraceRemaining(allianceIsBlue) > 0f;
    }

    public bool IsThisHubCounting()
    {
        return IsHubCounting(GetIsBlue());
    }

    private float GetCountingGraceRemaining(bool allianceIsBlue)
    {
        return allianceIsBlue ? blueCountingGraceTimer : redCountingGraceTimer;
    }

    private bool IsHubScheduledActive(bool allianceIsBlue)
    {
        return IsHubScheduledActive(allianceIsBlue, currentShift);
    }

    private bool IsHubScheduledActive(bool allianceIsBlue, CurrentShift shift)
    {
        if (!autoWinnerResolved)
        {
            return shift is CurrentShift.Auto or CurrentShift.Transition;
        }

        if (blueWonAuto)
        {
            if (allianceIsBlue)
                return shift is CurrentShift.Auto or CurrentShift.Transition or CurrentShift.Shift2 or CurrentShift.Shift4 or CurrentShift.EndGame;

            return shift is CurrentShift.Auto or CurrentShift.Transition or CurrentShift.Shift1 or CurrentShift.Shift3 or CurrentShift.EndGame;
        }

        if (allianceIsBlue)
            return shift is CurrentShift.Auto or CurrentShift.Transition or CurrentShift.Shift1 or CurrentShift.Shift3 or CurrentShift.EndGame;

        return shift is CurrentShift.Auto or CurrentShift.Transition or CurrentShift.Shift2 or CurrentShift.Shift4 or CurrentShift.EndGame;
    }

    private float GetShiftLightFade(bool allianceIsBlue)
    {
        // After the match-end disabled pause, show the light fully with the finished material.
        if (FMS.MatchState == MatchState.finished)
            return finishedShiftLightMaterial != null ? 1f : 0f;

        // Light is off during deactivation grace because grace scoring is not scheduled active time.
        if (!IsHubScheduledActive(allianceIsBlue))
            return 0f;

        // Fade before the light turns off at the end of any timed active period.
        if (IsCurrentActivePeriodEndingForAlliance(allianceIsBlue) &&
            shiftTimer <= shiftLightBlinkStartTime)
        {
            if (shiftLightBlinkInterval <= 0f)
                return 1f;

            // Counts backward because shiftTimer counts down, but still creates a repeating 0->1->0 fade.
            float phase = Mathf.Repeat(shiftTimer, shiftLightBlinkInterval) / shiftLightBlinkInterval;
            float fade = 1f - Mathf.Abs(phase * 2f - 1f);

            if (smoothBlinkFade)
                fade = Mathf.SmoothStep(0f, 1f, fade);

            return fade;
        }

        return 1f;
    }

    private void CacheShiftLightRenderers()
    {
        if (shiftOnLight == null)
        {
            shiftLightRenderers = Array.Empty<Renderer>();
            originalShiftLightSharedMaterials = Array.Empty<Material[]>();
            runtimeShiftLightMaterials = Array.Empty<Material[]>();
            runtimeFinishedShiftLightMaterials = Array.Empty<Material[]>();
            originalBaseColors = Array.Empty<Color[]>();
            originalEmissionColors = Array.Empty<Color[]>();
            finishedBaseColors = Array.Empty<Color[]>();
            finishedEmissionColors = Array.Empty<Color[]>();
            return;
        }

        shiftLightRenderers = shiftOnLight.GetComponentsInChildren<Renderer>(true);

        originalShiftLightSharedMaterials = new Material[shiftLightRenderers.Length][];
        runtimeShiftLightMaterials = new Material[shiftLightRenderers.Length][];
        runtimeFinishedShiftLightMaterials = new Material[shiftLightRenderers.Length][];

        originalBaseColors = new Color[shiftLightRenderers.Length][];
        originalEmissionColors = new Color[shiftLightRenderers.Length][];
        finishedBaseColors = new Color[shiftLightRenderers.Length][];
        finishedEmissionColors = new Color[shiftLightRenderers.Length][];

        for (int i = 0; i < shiftLightRenderers.Length; i++)
        {
            Renderer renderer = shiftLightRenderers[i];

            if (renderer == null)
                continue;

            Material[] sharedMaterials = renderer.sharedMaterials;
            originalShiftLightSharedMaterials[i] = sharedMaterials;

            runtimeShiftLightMaterials[i] = CreateMaterialInstances(sharedMaterials);
            originalBaseColors[i] = CacheBaseColors(runtimeShiftLightMaterials[i]);
            originalEmissionColors[i] = CacheEmissionColors(runtimeShiftLightMaterials[i]);

            runtimeFinishedShiftLightMaterials[i] = CreateFinishedMaterialInstances(sharedMaterials.Length);
            finishedBaseColors[i] = CacheBaseColors(runtimeFinishedShiftLightMaterials[i]);
            finishedEmissionColors[i] = CacheEmissionColors(runtimeFinishedShiftLightMaterials[i]);

            renderer.materials = runtimeShiftLightMaterials[i];
        }

        finishedLightMaterialApplied = false;
    }

    private Material[] CreateMaterialInstances(Material[] sourceMaterials)
    {
        Material[] instances = new Material[sourceMaterials.Length];

        for (int i = 0; i < sourceMaterials.Length; i++)
        {
            instances[i] = sourceMaterials[i] != null ? new Material(sourceMaterials[i]) : null;
        }

        return instances;
    }

    private Material[] CreateFinishedMaterialInstances(int materialCount)
    {
        Material[] instances = new Material[materialCount];

        for (int i = 0; i < materialCount; i++)
        {
            instances[i] = finishedShiftLightMaterial != null ? new Material(finishedShiftLightMaterial) : null;
        }

        return instances;
    }

    private Color[] CacheBaseColors(Material[] materials)
    {
        Color[] colors = new Color[materials.Length];

        for (int i = 0; i < materials.Length; i++)
        {
            colors[i] = GetMaterialBaseColor(materials[i]);
        }

        return colors;
    }

    private Color[] CacheEmissionColors(Material[] materials)
    {
        Color[] colors = new Color[materials.Length];

        for (int i = 0; i < materials.Length; i++)
        {
            colors[i] = GetMaterialEmissionColor(materials[i]);
        }

        return colors;
    }

    private void UpdateShiftLightVisuals(float fade)
    {
        if (shiftOnLight == null)
            return;

        if (shiftLightRenderers == null || shiftLightRenderers.Length == 0)
            CacheShiftLightRenderers();

        fade = Mathf.Clamp01(fade);

        bool shouldShowShiftLight = fade > 0.001f;

        if (shiftOnLight.activeSelf != shouldShowShiftLight)
            shiftOnLight.SetActive(shouldShowShiftLight);

        if (!shouldShowShiftLight)
            return;

        bool isFinished = FMS.MatchState == MatchState.finished;

        if (isFinished)
        {
            ApplyFinishedShiftLightMaterial();
            SetShiftLightFade(1f, true);
        }
        else
        {
            RestoreOriginalShiftLightMaterials();
            SetShiftLightFade(fade, false);
        }
    }

    private void ApplyFinishedShiftLightMaterial()
    {
        if (finishedLightMaterialApplied)
            return;

        if (finishedShiftLightMaterial == null)
            return;

        if (shiftLightRenderers == null || shiftLightRenderers.Length == 0)
            CacheShiftLightRenderers();

        for (int i = 0; i < shiftLightRenderers.Length; i++)
        {
            Renderer renderer = shiftLightRenderers[i];

            if (renderer == null)
                continue;

            if (i >= runtimeFinishedShiftLightMaterials.Length)
                continue;

            renderer.materials = runtimeFinishedShiftLightMaterials[i];
        }

        finishedLightMaterialApplied = true;
    }

    private void RestoreOriginalShiftLightMaterials()
    {
        if (!finishedLightMaterialApplied)
            return;

        if (shiftLightRenderers == null || runtimeShiftLightMaterials == null)
            return;

        for (int i = 0; i < shiftLightRenderers.Length; i++)
        {
            Renderer renderer = shiftLightRenderers[i];

            if (renderer == null)
                continue;

            if (i >= runtimeShiftLightMaterials.Length)
                continue;

            renderer.materials = runtimeShiftLightMaterials[i];
        }

        finishedLightMaterialApplied = false;
    }

    private void SetShiftLightFade(float fade, bool usingFinishedMaterial)
    {
        fade = Mathf.Clamp01(fade);

        float alpha = Mathf.Lerp(shiftLightMinAlpha, shiftLightMaxAlpha, fade);
        float emissionMultiplier = Mathf.Lerp(
            shiftLightMinEmissionMultiplier,
            shiftLightMaxEmissionMultiplier,
            fade
        );

        for (int rendererIndex = 0; rendererIndex < shiftLightRenderers.Length; rendererIndex++)
        {
            Renderer renderer = shiftLightRenderers[rendererIndex];

            if (renderer == null)
                continue;

            Material[] materials = renderer.materials;

            Color[] baseColors = usingFinishedMaterial
                ? GetColorArray(finishedBaseColors, rendererIndex)
                : GetColorArray(originalBaseColors, rendererIndex);

            Color[] emissionColors = usingFinishedMaterial
                ? GetColorArray(finishedEmissionColors, rendererIndex)
                : GetColorArray(originalEmissionColors, rendererIndex);

            Color[] normalEmissionColors = GetColorArray(originalEmissionColors, rendererIndex);

            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];

                if (material == null)
                    continue;

                Color baseColor = GetIndexedColor(baseColors, materialIndex, GetMaterialBaseColor(material));
                baseColor.a = alpha;
                SetMaterialBaseColor(material, baseColor);

                Color emissionColor =
                    GetIndexedColor(emissionColors, materialIndex, GetMaterialEmissionColor(material));
                if (usingFinishedMaterial)
                {
                    Color normalEmissionColor = GetIndexedColor(
                        normalEmissionColors,
                        materialIndex,
                        emissionColor
                    );

                    emissionColor = MatchEmissionIntensity(
                        emissionColor,
                        normalEmissionColor * finishedShiftLightEmissionRelativeBrightness
                    );
                }

                SetMaterialEmissionColor(material, emissionColor * emissionMultiplier);
            }
        }
    }

    private Color[] GetColorArray(Color[][] colors, int index)
    {
        if (colors == null || index < 0 || index >= colors.Length)
            return Array.Empty<Color>();

        return colors[index] ?? Array.Empty<Color>();
    }

    private Color GetIndexedColor(Color[] colors, int index, Color fallback)
    {
        if (colors == null || index < 0 || index >= colors.Length)
            return fallback;

        return colors[index];
    }

    private Color GetMaterialBaseColor(Material material)
    {
        if (material == null)
            return Color.white;

        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");

        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");

        return Color.white;
    }

    private void SetMaterialBaseColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private Color GetMaterialEmissionColor(Material material)
    {
        if (material == null)
            return Color.black;

        if (material.HasProperty("_EmissionColor"))
            return material.GetColor("_EmissionColor");

        return Color.black;
    }

    private void SetMaterialEmissionColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (!material.HasProperty("_EmissionColor"))
            return;

        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color);
    }
    
    private Color MatchEmissionIntensity(Color sourceColor, Color targetIntensityColor)
    {
        float sourceIntensity = GetColorIntensity(sourceColor);
        float targetIntensity = GetColorIntensity(targetIntensityColor);

        if (sourceIntensity <= 0.0001f)
            return targetIntensityColor;

        return sourceColor * (targetIntensity / sourceIntensity);
    }

    private float GetColorIntensity(Color color)
    {
        return Mathf.Max(color.r, color.g, color.b);
    }

    private bool IsCurrentActivePeriodEndingForAlliance(bool allianceIsBlue)
    {
        if (!IsHubScheduledActive(allianceIsBlue, currentShift))
            return false;

        // Endgame light should blink before the match ends, then change material when finished.
        if (currentShift == CurrentShift.EndGame)
            return FMS.MatchState == MatchState.endgame;

        CurrentShift nextShift = NextShift(currentShift);

        // For normal shift changes, blink if this alliance is active now
        // but will not be active on the next shift.
        return !IsHubScheduledActive(allianceIsBlue, nextShift);
    }

    private CurrentShift NextShift(CurrentShift shift)
    {
        return shift == CurrentShift.EndGame ? CurrentShift.EndGame : (CurrentShift)((int)shift + 1);
    }

    private void OnDestroy()
    {
        DestroyMaterialInstances(runtimeShiftLightMaterials);
        DestroyMaterialInstances(runtimeFinishedShiftLightMaterials);
    }

    private void DestroyMaterialInstances(Material[][] materials)
    {
        if (materials == null)
            return;

        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] == null)
                continue;

            for (int j = 0; j < materials[i].Length; j++)
            {
                if (materials[i][j] == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(materials[i][j]);
                else
                    DestroyImmediate(materials[i][j]);
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