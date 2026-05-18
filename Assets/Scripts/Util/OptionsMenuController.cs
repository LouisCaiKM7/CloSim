using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Util
{
    public class OptionsMenuController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LoadMatch loadMatch;
        [SerializeField] private GameObject menuRoot;
        [SerializeField] private ScreenFader screenFader;

        [Header("Top Controls")]
        [SerializeField] private TMP_Dropdown gameModeDropdown;
        [SerializeField] private TMP_Dropdown cameraDropdown;
        [SerializeField] private TMP_Dropdown frameRateDropdown;
        [SerializeField] private TMP_Dropdown windowModeDropdown;
        [SerializeField] private Button allianceButton;
        [SerializeField] private TMP_Text allianceButtonText;

        [Header("Human Player")]
        [SerializeField] private TMP_Dropdown humanPlayerDropdown;

        [SerializeField] private GameObject blueBucket;
        [SerializeField] private GameObject blueDumper;
        [SerializeField] private GameObject redBucket;
        [SerializeField] private GameObject redDumper;

        [Header("Robot Panels")]
        [SerializeField] private RobotPanelUI robotPanel1;
        [SerializeField] private RobotPanelUI robotPanel2;

        [Header("Bottom Buttons")]
        [SerializeField] private Button applyButton;
        [SerializeField] private Button quitButton;

        [Header("Credits")]
        [SerializeField] private GameObject creditsRoot;
        [SerializeField] private Button creditsButton;
        [SerializeField] private Button creditsBackButton;

        [Header("Controls")]
        [SerializeField] private GameObject controlsRoot;
        [SerializeField] private Button controlsButton;
        [SerializeField] private Button controlsBackButton;

        [Header("Input System")]
        [SerializeField] private InputActionReference toggleMenuAction;
        [SerializeField] private InputActionAsset fallbackActions;
        [SerializeField] private string fallbackToggleActionName = "ToggleMenu";

        [Header("Behavior")]
        [SerializeField] private float startMenuBlackHoldTime = 0.35f;
        [SerializeField] private float startMenuFadeDuration = 3.5f;
        [SerializeField] private bool unlockCursorWhenOpen = true;
        [SerializeField] private bool relockCursorOnClose;

        private const string FrameRatePrefKey = "FrameRateMode";
        private const string WindowModePrefKey = "WindowMode";

        private readonly List<(Utils.FrameRateMode value, string label)> _frameRateModes = new()
        {
            (Utils.FrameRateMode.FPS30, "30 FPS"),
            (Utils.FrameRateMode.FPS60, "60 FPS"),
            (Utils.FrameRateMode.FPS120, "120 FPS"),
            (Utils.FrameRateMode.VSync, "VSync")
        };

        private readonly List<(Utils.WindowMode value, string label)> _windowModes = new()
        {
            (Utils.WindowMode.Windowed, "Windowed"),
            (Utils.WindowMode.BorderlessFullscreen, "Borderless"),
            (Utils.WindowMode.ExclusiveFullscreen, "Fullscreen")
        };

        private readonly List<(PlayMode value, string label)> _gameModes = new()
        {
            (PlayMode.OneVsZero, "Singleplayer"),
            (PlayMode.TwoVsZero, "Multiplayer: 2v0"),
            (PlayMode.OneVsOne, "Multiplayer: 1v1")
        };

        private readonly List<(HumanPlayerType value, string label)> _humanPlayerModes = new()
        {
            (HumanPlayerType.Bucket, "Certified Bucket"),
            (HumanPlayerType.Dumper, "Certified Dumper")
        };

        private readonly List<(Cameras value, string label)> _cameraModes = new()
        {
            (Cameras.ThirdPerson, "Third Person"),
            (Cameras.ReversedThirdPerson, "Reverse Third Person"),
            (Cameras.FirstPerson, "First Person"),
            (Cameras.FirstPersonReversed, "Reverse First Person"),
            (Cameras.DriverStation, "Driver Station")
        };

        private bool _isOpen;
        private bool _isTransitioning;
        private bool _isRefreshingUi;

        private MatchSettings _workingSettings;
        private InputAction _resolvedToggleAction;

        private List<string> _blueSpawnNames = new();
        private List<string> _redSpawnNames = new();

        private HumanPlayerType _workingHumanPlayer = HumanPlayerType.Bucket;

        private OutpostRelease[] _cachedOutpostReleases;

        private void Awake()
        {
            if (loadMatch == null)
                loadMatch = FindFirstObjectByType<LoadMatch>();

            CacheOutpostReleases();

            if (menuRoot != null)
                menuRoot.SetActive(false);

            if (creditsRoot != null)
                creditsRoot.SetActive(false);

            if (controlsRoot != null)
                controlsRoot.SetActive(false);

            WireButtons();
            WirePanels();
            PopulateStaticDropdowns();
            ResolveToggleAction();

            ApplySavedFrameRate();
            ApplySavedWindowMode();
        }

        private void OnEnable()
        {
            ResolveToggleAction();

            if (_resolvedToggleAction != null)
            {
                _resolvedToggleAction.Enable();
                _resolvedToggleAction.performed += OnToggleMenuPerformed;
            }
        }

        private void OnDisable()
        {
            if (_resolvedToggleAction != null)
            {
                _resolvedToggleAction.performed -= OnToggleMenuPerformed;
                _resolvedToggleAction.Disable();
            }
        }

        private void Start()
        {
            if (loadMatch == null || menuRoot == null)
            {
                enabled = false;
                return;
            }

            StartCoroutine(OpenMenuOnStartWithFadeRoutine());
        }

        private void Update()
        {
            if (_resolvedToggleAction == null &&
                Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ToggleMenu();
            }
        }

        private void CacheOutpostReleases()
        {
            _cachedOutpostReleases = FindObjectsByType<OutpostRelease>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
        }

        private void ResolveToggleAction()
        {
            if (toggleMenuAction != null && toggleMenuAction.action != null)
            {
                _resolvedToggleAction = toggleMenuAction.action;
                return;
            }

            if (fallbackActions != null && !string.IsNullOrWhiteSpace(fallbackToggleActionName))
            {
                _resolvedToggleAction = fallbackActions.FindAction(fallbackToggleActionName);
                if (_resolvedToggleAction != null)
                    return;
            }

            _resolvedToggleAction = null;
        }

        private void OnToggleMenuPerformed(InputAction.CallbackContext context)
        {
            ToggleMenu();
        }

        private void ToggleMenu()
        {
            if (_isTransitioning)
                return;

            if (_isOpen)
                CloseMenuWithoutApply();
            else
                OpenMenu();
        }

        private void WireButtons()
        {
            if (applyButton != null)
                applyButton.onClick.AddListener(ApplyAndClose);

            if (quitButton != null)
                quitButton.onClick.AddListener(Application.Quit);

            if (allianceButton != null)
                allianceButton.onClick.AddListener(ToggleAlliance);

            if (creditsButton != null)
                creditsButton.onClick.AddListener(OpenCredits);

            if (creditsBackButton != null)
                creditsBackButton.onClick.AddListener(CloseCredits);

            if (gameModeDropdown != null)
                gameModeDropdown.onValueChanged.AddListener(OnGameModeChanged);

            if (cameraDropdown != null)
                cameraDropdown.onValueChanged.AddListener(OnCameraChanged);

            if (frameRateDropdown != null)
                frameRateDropdown.onValueChanged.AddListener(OnFrameRateChanged);

            if (windowModeDropdown != null)
                windowModeDropdown.onValueChanged.AddListener(OnWindowModeChanged);

            if (humanPlayerDropdown != null)
                humanPlayerDropdown.onValueChanged.AddListener(OnHumanPlayerChanged);

            if (controlsButton != null)
                controlsButton.onClick.AddListener(OpenControls);

            if (controlsBackButton != null)
                controlsBackButton.onClick.AddListener(CloseControls);
        }

        private void WirePanels()
        {
            if (robotPanel1 != null)
            {
                robotPanel1.OnPreviousRobot += () => CycleRobotIndex(0, -1);
                robotPanel1.OnNextRobot += () => CycleRobotIndex(0, 1);
                robotPanel1.OnSpawnChanged += value => SetSpawnIndexForPanel(0, value);
            }

            if (robotPanel2 != null)
            {
                robotPanel2.OnPreviousRobot += () => CycleRobotIndex(1, -1);
                robotPanel2.OnNextRobot += () => CycleRobotIndex(1, 1);
                robotPanel2.OnSpawnChanged += value => SetSpawnIndexForPanel(1, value);
            }
        }

        private void PopulateStaticDropdowns()
        {
            if (gameModeDropdown != null)
            {
                gameModeDropdown.ClearOptions();
                gameModeDropdown.AddOptions(_gameModes.ConvertAll(x => x.label));
            }

            if (cameraDropdown != null)
            {
                cameraDropdown.ClearOptions();
                cameraDropdown.AddOptions(_cameraModes.ConvertAll(x => x.label));
            }

            if (frameRateDropdown != null)
            {
                frameRateDropdown.ClearOptions();
                frameRateDropdown.AddOptions(_frameRateModes.ConvertAll(x => x.label));

                int savedIndex = PlayerPrefs.GetInt(FrameRatePrefKey, FindFrameRateIndex(Utils.FrameRateMode.VSync));
                savedIndex = Mathf.Clamp(savedIndex, 0, _frameRateModes.Count - 1);

                frameRateDropdown.SetValueWithoutNotify(savedIndex);
                frameRateDropdown.RefreshShownValue();
            }

            if (windowModeDropdown != null)
            {
                windowModeDropdown.ClearOptions();
                windowModeDropdown.AddOptions(_windowModes.ConvertAll(x => x.label));

                int savedIndex = PlayerPrefs.GetInt(WindowModePrefKey, FindWindowModeIndex(Utils.WindowMode.Windowed));
                savedIndex = Mathf.Clamp(savedIndex, 0, _windowModes.Count - 1);

                windowModeDropdown.SetValueWithoutNotify(savedIndex);
                windowModeDropdown.RefreshShownValue();
            }

            if (humanPlayerDropdown != null)
            {
                humanPlayerDropdown.ClearOptions();
                humanPlayerDropdown.AddOptions(_humanPlayerModes.ConvertAll(x => x.label));
            }
        }

        private void LoadDynamicData()
        {
            _blueSpawnNames = loadMatch.GetBlueSpawnNames();
            _redSpawnNames = loadMatch.GetRedSpawnNames();
        }

        private IEnumerator OpenMenuOnStartWithFadeRoutine()
        {
            if (screenFader == null)
            {
                OpenMenuImmediate();
                yield break;
            }

            _isTransitioning = true;

            screenFader.SetBlackImmediate(true);
            OpenMenuImmediate();

            yield return null;

            if (startMenuBlackHoldTime > 0f)
                yield return new WaitForSecondsRealtime(startMenuBlackHoldTime);

            screenFader.FadeFromBlack(startMenuFadeDuration, () =>
            {
                _isTransitioning = false;
            });
        }

        private void OpenMenu()
        {
            if (_isTransitioning || loadMatch == null || menuRoot == null)
                return;

            _isTransitioning = true;

            void ShowMenu()
            {
                LoadDynamicData();

                _workingSettings = loadMatch.GetSettingsCopy();
                ApplySettingsToUI(true);

                _isOpen = true;
                menuRoot.SetActive(true);

                Time.timeScale = 0f;

                if (unlockCursorWhenOpen)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                SetRobotInputsEnabled(false);
            }

            void Done()
            {
                _isTransitioning = false;
            }

            if (screenFader != null)
                screenFader.FadeToBlackThen(ShowMenu, true, Done);
            else
            {
                ShowMenu();
                Done();
            }
        }

        private void OpenMenuImmediate()
        {
            LoadDynamicData();

            _workingSettings = loadMatch.GetSettingsCopy();
            ApplySettingsToUI(true);

            _isOpen = true;
            menuRoot.SetActive(true);

            Time.timeScale = 0f;

            if (unlockCursorWhenOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            SetRobotInputsEnabled(false);
        }

        private void ApplyAndClose()
        {
            if (_isTransitioning || loadMatch == null)
                return;

            _isTransitioning = true;

            void ApplyAndReset()
            {
                loadMatch.ApplySettings(_workingSettings);

                ResumeRuntimeState();

                if (controlsRoot != null)
                    controlsRoot.SetActive(false);

                if (creditsRoot != null)
                    creditsRoot.SetActive(false);

                if (menuRoot != null)
                    menuRoot.SetActive(false);

                _isOpen = false;
                loadMatch.ResetField();
            }

            void Done()
            {
                _isTransitioning = false;
            }

            if (screenFader != null)
                screenFader.FadeToBlackThen(ApplyAndReset, true, Done);
            else
            {
                ApplyAndReset();
                Done();
            }
        }

        private void CloseMenuWithoutApply()
        {
            if (_isTransitioning)
                return;

            _isTransitioning = true;

            void CloseAction()
            {
                ResumeRuntimeState();

                if (controlsRoot != null)
                    controlsRoot.SetActive(false);

                if (creditsRoot != null)
                    creditsRoot.SetActive(false);

                if (menuRoot != null)
                    menuRoot.SetActive(false);

                _isOpen = false;

                if (loadMatch != null)
                    loadMatch.ResetField();
                else
                    SetRobotInputsEnabled(true);
            }

            void Done()
            {
                _isTransitioning = false;
            }

            if (screenFader != null)
                screenFader.FadeToBlackThen(CloseAction, true, Done);
            else
            {
                CloseAction();
                Done();
            }
        }

        private void OpenCredits()
        {
            if (_isTransitioning)
                return;

            _isTransitioning = true;

            void ShowCredits()
            {
                if (menuRoot != null)
                    menuRoot.SetActive(false);

                if (creditsRoot != null)
                    creditsRoot.SetActive(true);

                if (controlsRoot != null)
                    controlsRoot.SetActive(false);
            }

            void Done()
            {
                _isTransitioning = false;
            }

            if (screenFader != null)
                screenFader.FadeToBlackThen(ShowCredits, true, Done);
            else
            {
                ShowCredits();
                Done();
            }
        }

        private void CloseCredits()
        {
            if (_isTransitioning)
                return;

            _isTransitioning = true;

            void ShowMenu()
            {
                if (creditsRoot != null)
                    creditsRoot.SetActive(false);

                if (menuRoot != null)
                    menuRoot.SetActive(true);

                if (controlsRoot != null)
                    controlsRoot.SetActive(false);

                RefreshVisibleState(false);
            }

            void Done()
            {
                _isTransitioning = false;
            }

            if (screenFader != null)
                screenFader.FadeToBlackThen(ShowMenu, true, Done);
            else
            {
                ShowMenu();
                Done();
            }
        }

        private void OpenControls()
        {
            if (_isTransitioning)
                return;

            _isTransitioning = true;

            void ShowControls()
            {
                if (menuRoot != null)
                    menuRoot.SetActive(false);

                if (creditsRoot != null)
                    creditsRoot.SetActive(false);

                if (controlsRoot != null)
                    controlsRoot.SetActive(true);
            }

            void Done()
            {
                _isTransitioning = false;
            }

            if (screenFader != null)
                screenFader.FadeToBlackThen(ShowControls, true, Done);
            else
            {
                ShowControls();
                Done();
            }
        }

        private void CloseControls()
        {
            if (_isTransitioning)
                return;

            _isTransitioning = true;

            void ShowMenu()
            {
                if (controlsRoot != null)
                    controlsRoot.SetActive(false);

                if (creditsRoot != null)
                    creditsRoot.SetActive(false);

                if (menuRoot != null)
                    menuRoot.SetActive(true);

                RefreshVisibleState(false);
            }

            void Done()
            {
                _isTransitioning = false;
            }

            if (screenFader != null)
                screenFader.FadeToBlackThen(ShowMenu, true, Done);
            else
            {
                ShowMenu();
                Done();
            }
        }

        private void ResumeRuntimeState()
        {
            Time.timeScale = 1f;

            if (relockCursorOnClose)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        private void ApplySettingsToUI(bool configureOwnership)
        {
            if (gameModeDropdown != null)
            {
                gameModeDropdown.SetValueWithoutNotify(FindGameModeIndex(_workingSettings.playMode));
                gameModeDropdown.RefreshShownValue();
            }

            if (cameraDropdown != null)
            {
                cameraDropdown.SetValueWithoutNotify(FindCameraModeIndex(_workingSettings.view));
                cameraDropdown.RefreshShownValue();
            }

            if (frameRateDropdown != null)
            {
                int savedIndex = PlayerPrefs.GetInt(FrameRatePrefKey, FindFrameRateIndex(Utils.FrameRateMode.VSync));
                savedIndex = Mathf.Clamp(savedIndex, 0, _frameRateModes.Count - 1);

                frameRateDropdown.SetValueWithoutNotify(savedIndex);
                frameRateDropdown.RefreshShownValue();
            }

            if (windowModeDropdown != null)
            {
                int savedIndex = PlayerPrefs.GetInt(WindowModePrefKey, FindWindowModeIndex(Utils.WindowMode.BorderlessFullscreen));
                savedIndex = Mathf.Clamp(savedIndex, 0, _windowModes.Count - 1);

                windowModeDropdown.SetValueWithoutNotify(savedIndex);
                windowModeDropdown.RefreshShownValue();
            }

            if (humanPlayerDropdown != null)
            {
                humanPlayerDropdown.SetValueWithoutNotify(FindHumanPlayerIndex(_workingHumanPlayer));
                humanPlayerDropdown.RefreshShownValue();
            }

            RefreshVisibleState(configureOwnership);
        }

        private void RefreshVisibleState(bool configureOwnership)
        {
            if (_isRefreshingUi)
                return;

            _isRefreshingUi = true;

            try
            {
                bool secondRobotVisible = _workingSettings.playMode != PlayMode.OneVsZero;
                bool isOneVsOne = _workingSettings.playMode == PlayMode.OneVsOne;

                if (robotPanel1 != null)
                    robotPanel1.SetVisible(true);

                if (robotPanel2 != null)
                    robotPanel2.SetVisible(secondRobotVisible);

                if (allianceButton != null)
                    allianceButton.interactable = !isOneVsOne;

                if (allianceButtonText != null)
                {
                    allianceButtonText.text = isOneVsOne
                        ? "Alliance Locked"
                        : (_workingSettings.useBlueAlliance ? "Blue Alliance" : "Red Alliance");
                }

                RefreshPanel(0);

                if (secondRobotVisible)
                    RefreshPanel(1);

                RefreshHumanPlayerObjects(configureOwnership);
            }
            finally
            {
                _isRefreshingUi = false;
            }
        }

        private void RefreshPanel(int panelIndex)
        {
            RobotPanelUI panel = panelIndex == 0 ? robotPanel1 : robotPanel2;
            if (panel == null)
                return;

            string sideLabel;
            List<string> spawnNames;
            int selectedSpawnIndex;

            if (_workingSettings.playMode == PlayMode.OneVsOne)
            {
                if (panelIndex == 0)
                {
                    sideLabel = "Blue Alliance";
                    spawnNames = _blueSpawnNames;
                    selectedSpawnIndex = _workingSettings.blueSpawnIndex1;
                }
                else
                {
                    sideLabel = "Red Alliance";
                    spawnNames = _redSpawnNames;
                    selectedSpawnIndex = _workingSettings.redSpawnIndex1;
                }
            }
            else
            {
                bool useBlue = _workingSettings.useBlueAlliance;
                sideLabel = useBlue ? "Blue Alliance" : "Red Alliance";

                if (useBlue)
                {
                    spawnNames = _blueSpawnNames;
                    selectedSpawnIndex = panelIndex == 0
                        ? _workingSettings.blueSpawnIndex1
                        : _workingSettings.blueSpawnIndex2;
                }
                else
                {
                    spawnNames = _redSpawnNames;
                    selectedSpawnIndex = panelIndex == 0
                        ? _workingSettings.redSpawnIndex1
                        : _workingSettings.redSpawnIndex2;
                }
            }

            int robotIndex = panelIndex == 0 ? _workingSettings.robotIndex1 : _workingSettings.robotIndex2;

            panel.SetSideLabel(sideLabel);
            panel.SetRobotName(loadMatch.GetRobotNameAt(robotIndex));
            panel.SetRobotPreview(loadMatch.GetRobotPreviewSpriteAt(robotIndex));
            panel.SetSpawnOptions(spawnNames, selectedSpawnIndex);
        }

        private void ToggleAlliance()
        {
            if (_workingSettings.playMode == PlayMode.OneVsOne)
                return;

            _workingSettings.useBlueAlliance = !_workingSettings.useBlueAlliance;
            RefreshVisibleState(true);
        }

        private void OnGameModeChanged(int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            _workingSettings.playMode = _gameModes[Mathf.Clamp(dropdownIndex, 0, _gameModes.Count - 1)].value;
            RefreshVisibleState(true);
        }

        private void OnCameraChanged(int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            _workingSettings.view = _cameraModes[Mathf.Clamp(dropdownIndex, 0, _cameraModes.Count - 1)].value;
            RefreshVisibleState(false);
        }

        private void OnFrameRateChanged(int dropdownIndex)
        {
            dropdownIndex = Mathf.Clamp(dropdownIndex, 0, _frameRateModes.Count - 1);

            PlayerPrefs.SetInt(FrameRatePrefKey, dropdownIndex);
            PlayerPrefs.Save();

            ApplyFrameRate(_frameRateModes[dropdownIndex].value);
        }

        private void OnWindowModeChanged(int dropdownIndex)
        {
            dropdownIndex = Mathf.Clamp(dropdownIndex, 0, _windowModes.Count - 1);

            PlayerPrefs.SetInt(WindowModePrefKey, dropdownIndex);
            PlayerPrefs.Save();

            ApplyWindowMode(_windowModes[dropdownIndex].value);
        }

        private void ApplySavedWindowMode()
        {
            int savedIndex = PlayerPrefs.GetInt(WindowModePrefKey, FindWindowModeIndex(Utils.WindowMode.BorderlessFullscreen));
            savedIndex = Mathf.Clamp(savedIndex, 0, _windowModes.Count - 1);

            ApplyWindowMode(_windowModes[savedIndex].value);
        }

        private void ApplyWindowMode(Utils.WindowMode mode)
        {
            switch (mode)
            {
                case Utils.WindowMode.Windowed:
                    ApplyWindowedMode();
                    break;

                case Utils.WindowMode.BorderlessFullscreen:
                    ApplyBorderlessFullscreenMode();
                    break;

                case Utils.WindowMode.ExclusiveFullscreen:
                    ApplyExclusiveFullscreenMode();
                    break;
            }
        }

        private void ApplyWindowedMode()
        {
            int width = Screen.width;
            int height = Screen.height;

            if (Screen.fullScreenMode != FullScreenMode.Windowed)
            {
                width = Mathf.Min(1600, Screen.currentResolution.width);
                height = Mathf.Min(900, Screen.currentResolution.height);
            }

            Screen.SetResolution(width, height, FullScreenMode.Windowed);
        }

        private void ApplyBorderlessFullscreenMode()
        {
            Resolution nativeResolution = Screen.currentResolution;

            Screen.SetResolution(
                nativeResolution.width,
                nativeResolution.height,
                FullScreenMode.FullScreenWindow
            );
        }

        private void ApplyExclusiveFullscreenMode()
        {
            Resolution nativeResolution = Screen.currentResolution;

#if UNITY_STANDALONE_WIN
            Screen.SetResolution(
                nativeResolution.width,
                nativeResolution.height,
                FullScreenMode.ExclusiveFullScreen
            );
#else
            Screen.SetResolution(
                nativeResolution.width,
                nativeResolution.height,
                FullScreenMode.FullScreenWindow
            );
#endif
        }

        private int FindWindowModeIndex(Utils.WindowMode value)
        {
            for (int i = 0; i < _windowModes.Count; i++)
            {
                if (_windowModes[i].value == value)
                    return i;
            }

            return 0;
        }

        private void ApplySavedFrameRate()
        {
            int savedIndex = PlayerPrefs.GetInt(FrameRatePrefKey, FindFrameRateIndex(Utils.FrameRateMode.VSync));
            savedIndex = Mathf.Clamp(savedIndex, 0, _frameRateModes.Count - 1);

            ApplyFrameRate(_frameRateModes[savedIndex].value);
        }

        private void ApplyFrameRate(Utils.FrameRateMode mode)
        {
            switch (mode)
            {
                case Utils.FrameRateMode.FPS30:
                    SetManualFrameRate(30);
                    break;

                case Utils.FrameRateMode.FPS60:
                    SetManualFrameRate(60);
                    break;

                case Utils.FrameRateMode.FPS120:
                    SetManualFrameRate(120);
                    break;

                case Utils.FrameRateMode.VSync:
                    SetVSync();
                    break;
            }
        }

        private void SetManualFrameRate(int fps)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
        }

        private void SetVSync()
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
        }

        private void OnHumanPlayerChanged(int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            _workingHumanPlayer = _humanPlayerModes[
                Mathf.Clamp(dropdownIndex, 0, _humanPlayerModes.Count - 1)
            ].value;

            RefreshHumanPlayerObjects(true);
        }

        private void CycleRobotIndex(int panelIndex, int delta)
        {
            int count = loadMatch.GetAvailableRobotCount();
            if (count <= 0)
                return;

            if (panelIndex == 0)
                _workingSettings.robotIndex1 = WrapIndex(_workingSettings.robotIndex1 + delta, count);
            else
                _workingSettings.robotIndex2 = WrapIndex(_workingSettings.robotIndex2 + delta, count);

            RefreshPanel(panelIndex);
        }

        private int WrapIndex(int value, int count)
        {
            if (count <= 0)
                return 0;

            value %= count;

            if (value < 0)
                value += count;

            return value;
        }

        private void SetSpawnIndexForPanel(int panelIndex, int value)
        {
            if (_isRefreshingUi)
                return;

            if (_workingSettings.playMode == PlayMode.OneVsOne)
            {
                if (panelIndex == 0)
                    _workingSettings.blueSpawnIndex1 = value;
                else
                    _workingSettings.redSpawnIndex1 = value;

                RefreshVisibleState(false);
                return;
            }

            if (_workingSettings.playMode == PlayMode.OneVsZero)
            {
                if (_workingSettings.useBlueAlliance)
                    _workingSettings.blueSpawnIndex1 = value;
                else
                    _workingSettings.redSpawnIndex1 = value;

                RefreshVisibleState(false);
                return;
            }

            if (_workingSettings.useBlueAlliance)
            {
                if (panelIndex == 0)
                {
                    _workingSettings.blueSpawnIndex1 = value;

                    if (_workingSettings.blueSpawnIndex1 == _workingSettings.blueSpawnIndex2)
                        _workingSettings.blueSpawnIndex2 = FindDifferentIndex(
                            _workingSettings.blueSpawnIndex1,
                            _blueSpawnNames.Count
                        );
                }
                else
                {
                    _workingSettings.blueSpawnIndex2 = value;

                    if (_workingSettings.blueSpawnIndex2 == _workingSettings.blueSpawnIndex1)
                        _workingSettings.blueSpawnIndex1 = FindDifferentIndex(
                            _workingSettings.blueSpawnIndex2,
                            _blueSpawnNames.Count
                        );
                }
            }
            else
            {
                if (panelIndex == 0)
                {
                    _workingSettings.redSpawnIndex1 = value;

                    if (_workingSettings.redSpawnIndex1 == _workingSettings.redSpawnIndex2)
                        _workingSettings.redSpawnIndex2 = FindDifferentIndex(
                            _workingSettings.redSpawnIndex1,
                            _redSpawnNames.Count
                        );
                }
                else
                {
                    _workingSettings.redSpawnIndex2 = value;

                    if (_workingSettings.redSpawnIndex2 == _workingSettings.redSpawnIndex1)
                        _workingSettings.redSpawnIndex1 = FindDifferentIndex(
                            _workingSettings.redSpawnIndex2,
                            _redSpawnNames.Count
                        );
                }
            }

            RefreshVisibleState(false);
        }

        private int FindDifferentIndex(int currentIndex, int count)
        {
            if (count <= 1)
                return currentIndex;

            for (int i = 0; i < count; i++)
            {
                if (i != currentIndex)
                    return i;
            }

            return currentIndex;
        }

        private int FindGameModeIndex(PlayMode value)
        {
            for (int i = 0; i < _gameModes.Count; i++)
            {
                if (_gameModes[i].value == value)
                    return i;
            }

            return 0;
        }

        private int FindCameraModeIndex(Cameras value)
        {
            for (int i = 0; i < _cameraModes.Count; i++)
            {
                if (_cameraModes[i].value == value)
                    return i;
            }

            return 0;
        }

        private int FindFrameRateIndex(Utils.FrameRateMode value)
        {
            for (int i = 0; i < _frameRateModes.Count; i++)
            {
                if (_frameRateModes[i].value == value)
                    return i;
            }

            return 0;
        }

        private int FindHumanPlayerIndex(HumanPlayerType value)
        {
            for (int i = 0; i < _humanPlayerModes.Count; i++)
            {
                if (_humanPlayerModes[i].value == value)
                    return i;
            }

            return 0;
        }

        private void SetRobotInputsEnabled(bool enabledBool)
        {
            if (loadMatch == null)
                return;

            var robots = loadMatch.GetLoadedRobots();
            if (robots == null)
                return;

            foreach (var robot in robots)
            {
                if (robot == null)
                    continue;

                var playerInput = robot.GetComponent<PlayerInput>();
                if (playerInput == null)
                    continue;

                if (enabledBool)
                    playerInput.ActivateInput();
                else
                    playerInput.DeactivateInput();
            }
        }

        public bool IsOpen()
        {
            return _isOpen;
        }

        private void RefreshHumanPlayerObjects(bool configureOwnership)
        {
            bool blueAllianceUsed = IsBlueAllianceUsed();
            bool redAllianceUsed = IsRedAllianceUsed();

            bool bucketSelected = _workingHumanPlayer == HumanPlayerType.Bucket;
            bool dumperSelected = _workingHumanPlayer == HumanPlayerType.Dumper;

            SetActiveSafe(blueBucket, blueAllianceUsed && bucketSelected);
            SetActiveSafe(blueDumper, blueAllianceUsed && dumperSelected);

            SetActiveSafe(redBucket, redAllianceUsed && bucketSelected);
            SetActiveSafe(redDumper, redAllianceUsed && dumperSelected);

            HumanPlayerRuntimeState.SetState(
                _workingHumanPlayer,
                blueAllianceUsed,
                redAllianceUsed
            );

            if (configureOwnership)
                ConfigureAllDumperOwnership();
        }

        private void ConfigureAllDumperOwnership()
        {
            if (loadMatch == null || _cachedOutpostReleases == null)
                return;

            foreach (var release in _cachedOutpostReleases)
            {
                if (release == null)
                    continue;

                bool releaseIsBlue = release.IsBlue();
                int ownerSlot = loadMatch.GetHumanPlayerOwnerSlotForAlliance(releaseIsBlue);

                release.ConfigureOwnership(ownerSlot);
            }
        }

        private bool IsBlueAllianceUsed()
        {
            return _workingSettings.playMode == PlayMode.OneVsOne ||
                   _workingSettings.useBlueAlliance;
        }

        private bool IsRedAllianceUsed()
        {
            return _workingSettings.playMode == PlayMode.OneVsOne ||
                   !_workingSettings.useBlueAlliance;
        }

        private void SetActiveSafe(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }
    }
}