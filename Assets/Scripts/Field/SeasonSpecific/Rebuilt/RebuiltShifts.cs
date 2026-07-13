using System;
using Field.Core;
using Field.Scoring;
using UnityEngine;

namespace Field.SeasonSpecific.Rebuilt
{
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

        private static bool _autoWinnerResolved;
        private static bool _blueWonAuto;
        private static bool _pendingAutoWinnerResolve;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color1 = Shader.PropertyToID("_Color");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private float _shiftTimer;
        private float _endDisableScoreTimer;
        private MatchState _previousMatchState;
        private float _blueCountingGraceTimer;
        private float _redCountingGraceTimer;
        private bool _previousBlueActive;
        private bool _previousRedActive;

        [Header("Shift Light")]
        [SerializeField] private GameObject shiftOnLight;
        [SerializeField] private Material finishedShiftLightMaterial;

        [Header("Shift Light Blink / Fade")]
        [SerializeField] private float shiftLightBlinkStartTime = 3f;
        [SerializeField] private float shiftLightBlinkInterval = 0.5f;

        [Tooltip("Lowest visible alpha during the fade blink. Use 0 for fully invisible.")]
        [SerializeField] private float shiftLightMinAlpha;

        [Tooltip("Highest alpha during the fade blink.")]
        [SerializeField] private float shiftLightMaxAlpha = 0.7f;

        [Tooltip("Lowest emission multiplier during the fade blink.")]
        [SerializeField] private float shiftLightMinEmissionMultiplier;

        [Tooltip("Highest emission multiplier during the fade blink.")]
        [SerializeField] private float shiftLightMaxEmissionMultiplier = 1f;

        [Tooltip("Smooths the fade curve instead of using a linear triangle wave.")]
        [SerializeField] private bool smoothBlinkFade = true;

        [Tooltip("Finished light emission brightness relative to the normal shift light. 0.5 means half as bright.")]
        [SerializeField] private float finishedShiftLightEmissionRelativeBrightness = 0.5f;
    
        private Renderer[] _shiftLightRenderers = Array.Empty<Renderer>();
        
        private Material[][] _runtimeShiftLightMaterials = Array.Empty<Material[]>();
        private Material[][] _runtimeFinishedShiftLightMaterials = Array.Empty<Material[]>();

        private Color[][] _originalBaseColors = Array.Empty<Color[]>();
        private Color[][] _originalEmissionColors = Array.Empty<Color[]>();
        private Color[][] _finishedBaseColors = Array.Empty<Color[]>();
        private Color[][] _finishedEmissionColors = Array.Empty<Color[]>();

        private bool _finishedLightMaterialApplied;

        private Fms _cachedFms;

        private void Start()
        {
            _autoWinnerResolved = false;
            _blueWonAuto = false;
            _pendingAutoWinnerResolve = false;

            _shiftTimer = InitialShiftDelay;
            _endDisableScoreTimer = 0f;
            currentShift = CurrentShift.Auto;
            _previousMatchState = MatchState.Auto;
            ActiveShift = currentShift;
            BlueOwnsOddShifts = true;
            _blueCountingGraceTimer = 0f;
            _redCountingGraceTimer = 0f;

            CacheShiftLightRenderers();

            _cachedFms = FindFirstObjectByType<Fms>(FindObjectsInactive.Include);

            _previousBlueActive = IsHubScheduledActive(true);
            _previousRedActive = IsHubScheduledActive(false);
        }

        private new void FixedUpdate()
        {
            PoolOccupyObjects();

            HandleShiftState();
            UpdateCountingGraceTimers();

            bool shouldScore = IsHubCounting(GetIsBlue());
            if (Fms.MatchState == MatchState.Finished && _endDisableScoreTimer > 0f)
            {
                shouldScore = true;
                _endDisableScoreTimer -= Time.fixedDeltaTime;
            }

            float shiftLightFade = GetShiftLightFade(GetIsBlue());
            UpdateShiftLightVisuals(shiftLightFade);

            CompareObjects(shouldScore);

            ScorePoints(TotalScore);
        }
    
        private void LateUpdate()
        {
            if (!_pendingAutoWinnerResolve)
                return;

            ResolveAutoWinner();
            _pendingAutoWinnerResolve = false;
        }

        private void HandleShiftState()
        {
            if (ApplyScheduledShiftStateIfAvailable())
            {
                if (Fms.MatchState != MatchState.Auto && _previousMatchState == MatchState.Auto)
                    _pendingAutoWinnerResolve = true;

                _previousMatchState = Fms.MatchState;
                ActiveShift = currentShift;
                return;
            }

            // Auto just ended.
            if (Fms.MatchState != MatchState.Auto && _previousMatchState == MatchState.Auto)
            {
                _pendingAutoWinnerResolve = true;

                currentShift = CurrentShift.Auto;
                _shiftTimer = InitialShiftDelay;
            }

            // Normal teleop shift timing.
            if (Fms.MatchState == MatchState.Teleop)
            {
                _shiftTimer -= Time.fixedDeltaTime;

                if (_shiftTimer <= 0f && currentShift < CurrentShift.EndGame)
                {
                    _shiftTimer = currentShift == CurrentShift.Auto ? TransitionDuration : AllianceShiftDuration;
                    currentShift = NextShift(currentShift);
                }
            }

            // Endgame remains active through normal endgame.
            if (Fms.MatchState == MatchState.Endgame)
            {
                currentShift = CurrentShift.EndGame;
                _shiftTimer = Mathf.Max(0f, Fms.MatchTimer);
            }

            // Finished is reached only after the match-end disabled pause.
            if (Fms.MatchState == MatchState.Finished)
            {
                currentShift = CurrentShift.EndGame;

                if (_previousMatchState != MatchState.Finished)
                {
                    _endDisableScoreTimer = endDisableScoreTime;
                }
            }

            _previousMatchState = Fms.MatchState;
            ActiveShift = currentShift;
        }

        private void ResolveAutoWinner()
        {
            if (_autoWinnerResolved)
                return;

            _autoWinnerResolved = true;

            int blueAutoFuel = BlueFuel;
            int redAutoFuel = RedFuel;

            if (blueAutoFuel > redAutoFuel)
            {
                _blueWonAuto = true;
            }
            else if (blueAutoFuel < redAutoFuel)
            {
                _blueWonAuto = false;
            }
            else
            {
                _blueWonAuto = UnityEngine.Random.Range(0, 2) == 1;
            }

            BlueOwnsOddShifts = !_blueWonAuto;
        }

        private bool ApplyScheduledShiftStateIfAvailable()
        {
            if (_cachedFms == null)
                _cachedFms = FindFirstObjectByType<Fms>(FindObjectsInactive.Include);

            if (_cachedFms == null || !_cachedFms.HasScheduledMatch)
                return false;

            if (Fms.MatchState == MatchState.Auto)
            {
                currentShift = CurrentShift.Auto;
                _shiftTimer = 0f;
                return true;
            }

            if (Fms.MatchState == MatchState.Endgame || Fms.MatchState == MatchState.Finished)
            {
                currentShift = CurrentShift.EndGame;
                _shiftTimer = Mathf.Max(0f, Fms.MatchTimer);
                return true;
            }

            float teleopElapsed = _cachedFms.ScheduledTeleopElapsedSeconds;

            if (teleopElapsed < TransitionDuration)
            {
                currentShift = CurrentShift.Transition;
                _shiftTimer = TransitionDuration - teleopElapsed;
            }
            else if (teleopElapsed < TransitionDuration + AllianceShiftDuration)
            {
                currentShift = CurrentShift.Shift1;
                _shiftTimer = TransitionDuration + AllianceShiftDuration - teleopElapsed;
            }
            else if (teleopElapsed < TransitionDuration + AllianceShiftDuration * 2f)
            {
                currentShift = CurrentShift.Shift2;
                _shiftTimer = TransitionDuration + AllianceShiftDuration * 2f - teleopElapsed;
            }
            else if (teleopElapsed < TransitionDuration + AllianceShiftDuration * 3f)
            {
                currentShift = CurrentShift.Shift3;
                _shiftTimer = TransitionDuration + AllianceShiftDuration * 3f - teleopElapsed;
            }
            else
            {
                currentShift = CurrentShift.Shift4;
                _shiftTimer = _cachedFms.ScheduledSecondsUntilEndgame;
            }

            return true;
        }

        private void UpdateCountingGraceTimers()
        {
            bool blueActive = IsHubScheduledActive(true);
            bool redActive = IsHubScheduledActive(false);

            _blueCountingGraceTimer = UpdateCountingGraceTimer(_blueCountingGraceTimer, _previousBlueActive, blueActive);
            _redCountingGraceTimer = UpdateCountingGraceTimer(_redCountingGraceTimer, _previousRedActive, redActive);

            _previousBlueActive = blueActive;
            _previousRedActive = redActive;
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
            return allianceIsBlue ? _blueCountingGraceTimer : _redCountingGraceTimer;
        }

        private bool IsHubScheduledActive(bool allianceIsBlue)
        {
            return IsHubScheduledActive(allianceIsBlue, currentShift);
        }

        private bool IsHubScheduledActive(bool allianceIsBlue, CurrentShift shift)
        {
            if (!_autoWinnerResolved)
            {
                return shift is CurrentShift.Auto or CurrentShift.Transition;
            }

            if (_blueWonAuto)
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
            if (Fms.MatchState == MatchState.Finished)
                return finishedShiftLightMaterial != null ? 1f : 0f;

            // Light is off during deactivation grace because grace scoring is not scheduled active time.
            if (!IsHubScheduledActive(allianceIsBlue))
                return 0f;

            // Fade before the light turns off at the end of any timed active period.
            if (IsCurrentActivePeriodEndingForAlliance(allianceIsBlue) &&
                _shiftTimer <= shiftLightBlinkStartTime)
            {
                if (shiftLightBlinkInterval <= 0f)
                    return 1f;

                // Counts backward because shiftTimer counts down, but still creates a repeating 0->1->0 fade.
                float phase = Mathf.Repeat(_shiftTimer, shiftLightBlinkInterval) / shiftLightBlinkInterval;
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
                _shiftLightRenderers = Array.Empty<Renderer>();
                _runtimeShiftLightMaterials = Array.Empty<Material[]>();
                _runtimeFinishedShiftLightMaterials = Array.Empty<Material[]>();
                _originalBaseColors = Array.Empty<Color[]>();
                _originalEmissionColors = Array.Empty<Color[]>();
                _finishedBaseColors = Array.Empty<Color[]>();
                _finishedEmissionColors = Array.Empty<Color[]>();
                return;
            }

            _shiftLightRenderers = shiftOnLight.GetComponentsInChildren<Renderer>(true);

            _runtimeShiftLightMaterials = new Material[_shiftLightRenderers.Length][];
            _runtimeFinishedShiftLightMaterials = new Material[_shiftLightRenderers.Length][];

            _originalBaseColors = new Color[_shiftLightRenderers.Length][];
            _originalEmissionColors = new Color[_shiftLightRenderers.Length][];
            _finishedBaseColors = new Color[_shiftLightRenderers.Length][];
            _finishedEmissionColors = new Color[_shiftLightRenderers.Length][];

            for (int i = 0; i < _shiftLightRenderers.Length; i++)
            {
                Renderer lightRenderer = _shiftLightRenderers[i];

                if (lightRenderer == null)
                    continue;

                Material[] sharedMaterials = lightRenderer.sharedMaterials;

                _runtimeShiftLightMaterials[i] = CreateMaterialInstances(sharedMaterials);
                _originalBaseColors[i] = CacheBaseColors(_runtimeShiftLightMaterials[i]);
                _originalEmissionColors[i] = CacheEmissionColors(_runtimeShiftLightMaterials[i]);

                _runtimeFinishedShiftLightMaterials[i] = CreateFinishedMaterialInstances(sharedMaterials.Length);
                _finishedBaseColors[i] = CacheBaseColors(_runtimeFinishedShiftLightMaterials[i]);
                _finishedEmissionColors[i] = CacheEmissionColors(_runtimeFinishedShiftLightMaterials[i]);

                lightRenderer.materials = _runtimeShiftLightMaterials[i];
            }

            _finishedLightMaterialApplied = false;
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

            if (_shiftLightRenderers == null || _shiftLightRenderers.Length == 0)
                CacheShiftLightRenderers();

            fade = Mathf.Clamp01(fade);

            bool shouldShowShiftLight = fade > 0.001f;

            if (shiftOnLight.activeSelf != shouldShowShiftLight)
                shiftOnLight.SetActive(shouldShowShiftLight);

            if (!shouldShowShiftLight)
                return;

            bool isFinished = Fms.MatchState == MatchState.Finished;

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
            if (_finishedLightMaterialApplied)
                return;

            if (finishedShiftLightMaterial == null)
                return;

            if (_shiftLightRenderers == null || _shiftLightRenderers.Length == 0)
                CacheShiftLightRenderers();

            if (_shiftLightRenderers != null)
                for (int i = 0; i < _shiftLightRenderers.Length; i++)
                {
                    Renderer lightRenderer = _shiftLightRenderers[i];

                    if (lightRenderer == null)
                        continue;

                    if (i >= _runtimeFinishedShiftLightMaterials.Length)
                        continue;

                    lightRenderer.materials = _runtimeFinishedShiftLightMaterials[i];
                }

            _finishedLightMaterialApplied = true;
        }

        private void RestoreOriginalShiftLightMaterials()
        {
            if (!_finishedLightMaterialApplied)
                return;

            if (_shiftLightRenderers == null || _runtimeShiftLightMaterials == null)
                return;

            for (int i = 0; i < _shiftLightRenderers.Length; i++)
            {
                Renderer lightRenderer = _shiftLightRenderers[i];

                if (lightRenderer == null)
                    continue;

                if (i >= _runtimeShiftLightMaterials.Length)
                    continue;

                lightRenderer.materials = _runtimeShiftLightMaterials[i];
            }

            _finishedLightMaterialApplied = false;
        }

        private void SetShiftLightFade(float fade, bool usingFinishedMaterial)
        {
            fade = Mathf.Clamp01(fade);

            float maxAlpha = usingFinishedMaterial ? 1f : shiftLightMaxAlpha;
            float alpha = Mathf.Lerp(shiftLightMinAlpha, maxAlpha, fade);

            float emissionMultiplier = Mathf.Lerp(
                shiftLightMinEmissionMultiplier,
                shiftLightMaxEmissionMultiplier,
                fade
            );

            for (int rendererIndex = 0; rendererIndex < _shiftLightRenderers.Length; rendererIndex++)
            {
                Renderer lightRenderer = _shiftLightRenderers[rendererIndex];

                if (lightRenderer == null)
                    continue;

                Material[] materials = lightRenderer.materials;

                Color[] baseColors = usingFinishedMaterial
                    ? GetColorArray(_finishedBaseColors, rendererIndex)
                    : GetColorArray(_originalBaseColors, rendererIndex);

                Color[] emissionColors = usingFinishedMaterial
                    ? GetColorArray(_finishedEmissionColors, rendererIndex)
                    : GetColorArray(_originalEmissionColors, rendererIndex);

                Color[] normalEmissionColors = GetColorArray(_originalEmissionColors, rendererIndex);

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

            if (material.HasProperty(BaseColor))
                return material.GetColor(BaseColor);

            if (material.HasProperty(Color1))
                return material.GetColor(Color1);

            return Color.white;
        }

        private void SetMaterialBaseColor(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty(BaseColor))
                material.SetColor(BaseColor, color);

            if (material.HasProperty(Color1))
                material.SetColor(Color1, color);
        }

        private Color GetMaterialEmissionColor(Material material)
        {
            if (material == null)
                return Color.black;

            if (material.HasProperty(EmissionColor))
                return material.GetColor(EmissionColor);

            return Color.black;
        }

        private void SetMaterialEmissionColor(Material material, Color color)
        {
            if (material == null)
                return;

            if (!material.HasProperty(EmissionColor))
                return;

            material.EnableKeyword("_EMISSION");
            material.SetColor(EmissionColor, color);
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
                return Fms.MatchState == MatchState.Endgame;

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
            DestroyMaterialInstances(_runtimeShiftLightMaterials);
            DestroyMaterialInstances(_runtimeFinishedShiftLightMaterials);
        }

        private void DestroyMaterialInstances(Material[][] materials)
        {
            if (materials == null)
                return;

            foreach (var t in materials)
            {
                if (t == null)
                    continue;

                foreach (var t1 in t)
                {
                    if (t1 == null)
                        continue;

                    if (Application.isPlaying)
                        Destroy(t1);
                    else
                        DestroyImmediate(t1);
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
}