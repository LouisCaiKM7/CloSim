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
        [SerializeField] private TMP_Dropdown frameRateDropdown;
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private TMP_Dropdown windowModeDropdown;
        [SerializeField] private Button allianceButton;

        [Header("Human Player")]
        [SerializeField] private TMP_Dropdown humanPlayerDropdown;

        [Header("Robot Panel")]
        [SerializeField] private RobotPanelUI robotPanel;

        [Header("Player Selection")] 
        [SerializeField] private GameObject playerNumberRoot;
        [SerializeField] private TMP_Dropdown playerDropdown;

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
        private const string ResolutionPrefKey = "ResolutionMode";

        private readonly List<(int width, int height, string label)> _resolutionModes = new();

        private readonly List<(FrameRateMode value, string label)> _frameRateModes = new()
        {
            (FrameRateMode.FPS30, "30 FPS"),
            (FrameRateMode.FPS60, "60 FPS"),
            (FrameRateMode.FPS75, "75 FPS"),
            (FrameRateMode.FPS90, "90 FPS"),
            (FrameRateMode.FPS120, "120 FPS"),
            (FrameRateMode.FPS144, "144 FPS"),
            (FrameRateMode.FPS165, "165 FPS"),
            (FrameRateMode.FPS240, "240 FPS"),
            (FrameRateMode.Unlimited, "Unlimited"),
            (FrameRateMode.VSync, "VSync")
        };

        private readonly List<(WindowMode value, string label)> _windowModes = new()
        {
            (WindowMode.Windowed, "Windowed"),
            (WindowMode.BorderlessFullscreen, "Borderless"),
            (WindowMode.ExclusiveFullscreen, "Fullscreen")
        };

        private readonly List<(PlayMode value, string label)> _gameModes = new()
        {
            (PlayMode.OneVsZero, "Singleplayer"),
            (PlayMode.TwoVsZero, "Multiplayer: 2v0"),
            (PlayMode.OneVsOne, "Multiplayer: 1v1"),
            (PlayMode.ThreeVsZero, "Multiplayer: 3v0"),
            (PlayMode.TwoVsTwo, "Multiplayer: 2v2")
        };
        private readonly List<(HumanPlayerType value, string label)> _humanPlayerModes = new()
        {
            (HumanPlayerType.Bucket, "Certified Bucket"),
            (HumanPlayerType.Dumper, "Certified Dumper")
        };

        private readonly List<(Cameras value, string label)> _cameraModes = new()
        {
            (Cameras.ThirdPerson, "Third Person"),
            (Cameras.FirstPerson, "First Person"),
            (Cameras.DriverStation, "Driver Station")
        };
        
        private readonly List<(StationNum value, string label)> _driverStationModes = new()
        {
            (StationNum.One, "Station 1"),
            (StationNum.Two, "Station 2"),
            (StationNum.Three, "Station 3")
        };

        private bool _isOpen;
        private bool _isTransitioning;
        private bool _isRefreshingUi;
        private int _selectedPlayerIndex;

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
            
            BuildResolutionModes();

            WireButtons();
            WirePanels();
            PopulateStaticDropdowns();
            ResolveToggleAction();

            ApplySavedFrameRate();
            ApplySavedWindowMode();
            ApplySavedResolution();
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
            
            if (playerDropdown != null)
                playerDropdown.onValueChanged.AddListener(OnPlayerSelectionChanged);
            
            if (resolutionDropdown != null)
                resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        }

        private void WirePanels()
        {
            if (robotPanel == null)
                return;

            robotPanel.OnPreviousRobot += () => CycleRobotIndex(_selectedPlayerIndex, -1);
            robotPanel.OnNextRobot += () => CycleRobotIndex(_selectedPlayerIndex, 1);
            robotPanel.OnSpawnChanged += value => SetSpawnIndexForPanel(_selectedPlayerIndex, value);
            robotPanel.OnVanityBumperChanged += value => SetVanityBumpersForPanel(_selectedPlayerIndex, value);
            robotPanel.OnDriverStationChanged += value => SetDriverStationForPanel(_selectedPlayerIndex, value);
            robotPanel.OnCameraChanged += value => SetCameraForPanel(_selectedPlayerIndex, value);
        }

        private void PopulateStaticDropdowns()
        {
            if (gameModeDropdown != null)
            {
                gameModeDropdown.ClearOptions();
                gameModeDropdown.AddOptions(_gameModes.ConvertAll(x => x.label));
            }

            if (frameRateDropdown != null)
            {
                frameRateDropdown.ClearOptions();
                frameRateDropdown.AddOptions(_frameRateModes.ConvertAll(x => x.label));

                int savedIndex = PlayerPrefs.GetInt(FrameRatePrefKey, FindFrameRateIndex(FrameRateMode.VSync));
                savedIndex = Mathf.Clamp(savedIndex, 0, _frameRateModes.Count - 1);

                frameRateDropdown.SetValueWithoutNotify(savedIndex);
                frameRateDropdown.RefreshShownValue();
            }
            
            if (resolutionDropdown != null)
            {
                resolutionDropdown.ClearOptions();
                resolutionDropdown.AddOptions(_resolutionModes.ConvertAll(x => x.label));

                int savedIndex = PlayerPrefs.GetInt(ResolutionPrefKey, FindCurrentResolutionIndex());
                savedIndex = Mathf.Clamp(savedIndex, 0, _resolutionModes.Count - 1);

                resolutionDropdown.SetValueWithoutNotify(savedIndex);
                resolutionDropdown.RefreshShownValue();
            }

            if (windowModeDropdown != null)
            {
                windowModeDropdown.ClearOptions();
                windowModeDropdown.AddOptions(_windowModes.ConvertAll(x => x.label));

                int savedIndex = PlayerPrefs.GetInt(WindowModePrefKey, FindWindowModeIndex(WindowMode.Windowed));
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
                loadMatch.SetHumanPlayerType(_workingHumanPlayer);

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

            if (frameRateDropdown != null)
            {
                int savedIndex = PlayerPrefs.GetInt(FrameRatePrefKey, FindFrameRateIndex(FrameRateMode.VSync));
                savedIndex = Mathf.Clamp(savedIndex, 0, _frameRateModes.Count - 1);

                frameRateDropdown.SetValueWithoutNotify(savedIndex);
                frameRateDropdown.RefreshShownValue();
            }

            if (windowModeDropdown != null)
            {
                int savedIndex = PlayerPrefs.GetInt(WindowModePrefKey, FindWindowModeIndex(WindowMode.BorderlessFullscreen));
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
                int playerCount = GetPlayerCountForMode(_workingSettings.playMode);
                bool isVersusMode =
                    _workingSettings.playMode == PlayMode.OneVsOne ||
                    _workingSettings.playMode == PlayMode.TwoVsTwo;

                _selectedPlayerIndex = Mathf.Clamp(
                    _selectedPlayerIndex,
                    0,
                    Mathf.Max(0, playerCount - 1)
                );

                RefreshPlayerDropdown(playerCount);

                if (robotPanel != null)
                    robotPanel.SetVisible(playerCount > 0);

                if (allianceButton != null)
                    allianceButton.interactable = !isVersusMode;
                
                RefreshPanel(_selectedPlayerIndex);
                RefreshHumanPlayerObjects(configureOwnership);
            }
            finally
            {
                _isRefreshingUi = false;
            }
        }
        
        private void RefreshPlayerDropdown(int playerCount)
        {
            bool visible = playerCount > 1;

            if (playerNumberRoot != null)
                playerNumberRoot.SetActive(visible);
            else if (playerDropdown != null)
                playerDropdown.gameObject.SetActive(visible);

            if (playerDropdown == null)
                return;

            if (!visible)
            {
                playerDropdown.ClearOptions();
                playerDropdown.SetValueWithoutNotify(0);
                return;
            }

            playerDropdown.ClearOptions();

            List<string> options = new();

            for (int i = 0; i < playerCount; i++)
            {
                string alliance = IsPlayerBlue(i) ? "Blue" : "Red";
                options.Add($"Player {i + 1} ({alliance})");
            }

            playerDropdown.AddOptions(options);

            int selectedIndex = Mathf.Clamp(_selectedPlayerIndex, 0, playerCount - 1);
            playerDropdown.SetValueWithoutNotify(selectedIndex);
            playerDropdown.RefreshShownValue();
        }

        private void RefreshPanel(int playerIndex)
        {
            if (robotPanel == null)
                return;

            PlayerMatchSettings player = _workingSettings.GetPlayer(playerIndex);

            bool multiplayer = GetPlayerCountForMode(_workingSettings.playMode) > 1;
            bool playerIsBlue = IsPlayerBlue(playerIndex);

            List<string> spawnNames = playerIsBlue ? _blueSpawnNames : _redSpawnNames;
            int selectedSpawnIndex = playerIsBlue
                ? player.blueSpawnIndex
                : player.redSpawnIndex;

            string sideLabel = playerIsBlue ? "Blue Alliance" : "Red Alliance";

            robotPanel.SetPlayerNumber(playerIndex + 1, multiplayer);
            robotPanel.SetSideLabel(sideLabel);
            robotPanel.SetRobotName(loadMatch.GetRobotNameAt(player.robotIndex));
            robotPanel.SetRobotPreview(loadMatch.GetRobotPreviewSpriteAt(player.robotIndex));
            robotPanel.SetSpawnOptions(spawnNames, selectedSpawnIndex);

            robotPanel.SetCameraOptions(
                _cameraModes.ConvertAll(x => x.label),
                FindCameraModeIndex(player.view),
                true
            );

            robotPanel.SetDriverStationOptions(
                _driverStationModes.ConvertAll(x => x.label),
                FindDriverStationIndex(player.driverStation),
                player.view == Cameras.DriverStation
            );

            bool hasVanityMaterial = loadMatch.HasVanityBumperMaterialAt(player.robotIndex);
            robotPanel.SetVanityBumperToggle(player.useVanityBumpers, hasVanityMaterial);
        }

        private void ToggleAlliance()
        {
            if (_workingSettings.playMode == PlayMode.OneVsOne ||
                _workingSettings.playMode == PlayMode.TwoVsTwo)
            {
                return;
            }

            _workingSettings.useBlueAlliance = !_workingSettings.useBlueAlliance;
            RefreshVisibleState(true);
        }

        private void OnGameModeChanged(int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            _workingSettings.playMode = _gameModes[
                Mathf.Clamp(dropdownIndex, 0, _gameModes.Count - 1)
            ].value;

            int playerCount = GetPlayerCountForMode(_workingSettings.playMode);
            _selectedPlayerIndex = Mathf.Clamp(_selectedPlayerIndex, 0, Mathf.Max(0, playerCount - 1));

            RefreshVisibleState(true);
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
        
        private void OnPlayerSelectionChanged(int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            int playerCount = GetPlayerCountForMode(_workingSettings.playMode);
            _selectedPlayerIndex = Mathf.Clamp(dropdownIndex, 0, Mathf.Max(0, playerCount - 1));

            RefreshVisibleState(false);
        }
        
        private void OnResolutionChanged(int dropdownIndex)
        {
            dropdownIndex = Mathf.Clamp(dropdownIndex, 0, _resolutionModes.Count - 1);

            PlayerPrefs.SetInt(ResolutionPrefKey, dropdownIndex);
            PlayerPrefs.Save();

            ApplyResolution(_resolutionModes[dropdownIndex].width, _resolutionModes[dropdownIndex].height);
        }

        private void ApplySavedResolution()
        {
            int savedIndex = PlayerPrefs.GetInt(ResolutionPrefKey, FindCurrentResolutionIndex());
            savedIndex = Mathf.Clamp(savedIndex, 0, _resolutionModes.Count - 1);

            ApplyResolution(_resolutionModes[savedIndex].width, _resolutionModes[savedIndex].height);
        }

        private void ApplyResolution(int width, int height)
        {
            FullScreenMode mode = Screen.fullScreenMode;

            Screen.SetResolution(width, height, mode);
        }

        private int FindCurrentResolutionIndex()
        {
            int currentWidth = Screen.width;
            int currentHeight = Screen.height;

            for (int i = 0; i < _resolutionModes.Count; i++)
            {
                if (_resolutionModes[i].width == currentWidth &&
                    _resolutionModes[i].height == currentHeight)
                {
                    return i;
                }
            }

            return FindResolutionIndex(1920, 1080);
        }

        private int FindResolutionIndex(int width, int height)
        {
            for (int i = 0; i < _resolutionModes.Count; i++)
            {
                if (_resolutionModes[i].width == width &&
                    _resolutionModes[i].height == height)
                    return i;
            }

            return 0;
        }
        
        private void BuildResolutionModes()
        {
            _resolutionModes.Clear();

            HashSet<string> seen = new();

            foreach (Resolution resolution in Screen.resolutions)
            {
                if (resolution.width < 1280 || resolution.height < 720)
                    continue;

                string key = $"{resolution.width}x{resolution.height}";

                if (seen.Contains(key))
                    continue;

                seen.Add(key);
                _resolutionModes.Add((
                    resolution.width,
                    resolution.height,
                    $"{resolution.width} x {resolution.height}"
                ));
            }

            if (_resolutionModes.Count == 0)
            {
                _resolutionModes.Add((1280, 720, "1280 x 720"));
                _resolutionModes.Add((1920, 1080, "1920 x 1080"));
            }
        }

        private void ApplySavedWindowMode()
        {
            int savedIndex = PlayerPrefs.GetInt(WindowModePrefKey, FindWindowModeIndex(WindowMode.BorderlessFullscreen));
            savedIndex = Mathf.Clamp(savedIndex, 0, _windowModes.Count - 1);

            ApplyWindowMode(_windowModes[savedIndex].value);
        }

        private void ApplyWindowMode(WindowMode mode)
        {
            switch (mode)
            {
                case WindowMode.Windowed:
                    ApplyWindowedMode();
                    break;

                case WindowMode.BorderlessFullscreen:
                    ApplyBorderlessFullscreenMode();
                    break;

                case WindowMode.ExclusiveFullscreen:
                    ApplyExclusiveFullscreenMode();
                    break;
            }
        }

        private void ApplyWindowedMode()
        {
            var resolution = GetSelectedResolution();
            Screen.SetResolution(resolution.width, resolution.height, FullScreenMode.Windowed);
        }

        private void ApplyBorderlessFullscreenMode()
        {
            var resolution = GetSelectedResolution();
            Screen.SetResolution(resolution.width, resolution.height, FullScreenMode.FullScreenWindow);
        }

        private void ApplyExclusiveFullscreenMode()
        {
            var resolution = GetSelectedResolution();

#if UNITY_STANDALONE_WIN
            Screen.SetResolution(
                resolution.width,
                resolution.height,
                FullScreenMode.ExclusiveFullScreen
            );
#else
    Screen.SetResolution(
        resolution.width,
        resolution.height,
        FullScreenMode.FullScreenWindow
    );
#endif
        }
        
        private (int width, int height) GetSelectedResolution()
        {
            int index = resolutionDropdown != null
                ? resolutionDropdown.value
                : PlayerPrefs.GetInt(ResolutionPrefKey, FindCurrentResolutionIndex());

            index = Mathf.Clamp(index, 0, _resolutionModes.Count - 1);
            return (_resolutionModes[index].width, _resolutionModes[index].height);
        }

        private int FindWindowModeIndex(WindowMode value)
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
            int savedIndex = PlayerPrefs.GetInt(FrameRatePrefKey, FindFrameRateIndex(FrameRateMode.VSync));
            savedIndex = Mathf.Clamp(savedIndex, 0, _frameRateModes.Count - 1);

            ApplyFrameRate(_frameRateModes[savedIndex].value);
        }

        private void ApplyFrameRate(FrameRateMode mode)
        {
            switch (mode)
            {
                case FrameRateMode.FPS30:
                    SetManualFrameRate(30);
                    break;

                case FrameRateMode.FPS60:
                    SetManualFrameRate(60);
                    break;

                case FrameRateMode.FPS75:
                    SetManualFrameRate(75);
                    break;

                case FrameRateMode.FPS90:
                    SetManualFrameRate(90);
                    break;

                case FrameRateMode.FPS120:
                    SetManualFrameRate(120);
                    break;

                case FrameRateMode.FPS144:
                    SetManualFrameRate(144);
                    break;

                case FrameRateMode.FPS165:
                    SetManualFrameRate(165);
                    break;

                case FrameRateMode.FPS240:
                    SetManualFrameRate(240);
                    break;

                case FrameRateMode.Unlimited:
                    SetUnlimitedFrameRate();
                    break;

                case FrameRateMode.VSync:
                    SetVSync();
                    break;
            }
        }
        
        private void SetUnlimitedFrameRate()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
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
        
        private void SetVanityBumpersForPanel(int playerIndex, bool enabled)
        {
            if (_isRefreshingUi)
                return;

            _workingSettings.GetPlayer(playerIndex).useVanityBumpers = enabled;
        }
        
        private void SetDriverStationForPanel(int playerIndex, int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            dropdownIndex = Mathf.Clamp(dropdownIndex, 0, _driverStationModes.Count - 1);
            _workingSettings.GetPlayer(playerIndex).driverStation = _driverStationModes[dropdownIndex].value;
        }
        
        private void SetCameraForPanel(int playerIndex, int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            dropdownIndex = Mathf.Clamp(dropdownIndex, 0, _cameraModes.Count - 1);
            _workingSettings.GetPlayer(playerIndex).view = _cameraModes[dropdownIndex].value;

            RefreshPanel(playerIndex);
        }
        
        private int FindDriverStationIndex(StationNum value)
        {
            for (int i = 0; i < _driverStationModes.Count; i++)
            {
                if (_driverStationModes[i].value == value)
                    return i;
            }

            return 0;
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

        private void CycleRobotIndex(int playerIndex, int delta)
        {
            int count = loadMatch.GetAvailableRobotCount();
            if (count <= 0)
                return;

            PlayerMatchSettings player = _workingSettings.GetPlayer(playerIndex);
            player.robotIndex = WrapIndex(player.robotIndex + delta, count);

            RefreshPanel(playerIndex);
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

        private void SetSpawnIndexForPanel(int playerIndex, int value)
        {
            if (_isRefreshingUi)
                return;

            PlayerMatchSettings player = _workingSettings.GetPlayer(playerIndex);

            if (IsPlayerBlue(playerIndex))
            {
                player.blueSpawnIndex = value;
                EnforceUniqueSpawnForAlliance(true, playerIndex);
            }
            else
            {
                player.redSpawnIndex = value;
                EnforceUniqueSpawnForAlliance(false, playerIndex);
            }

            RefreshVisibleState(false);
        }
        
        private void EnforceUniqueSpawnForAlliance(bool blueAlliance, int changedPlayerIndex)
        {
            int spawnCount = blueAlliance ? _blueSpawnNames.Count : _redSpawnNames.Count;

            if (spawnCount <= 1)
                return;

            HashSet<int> used = new();

            int playerCount = GetPlayerCountForMode(_workingSettings.playMode);

            for (int i = 0; i < playerCount; i++)
            {
                if (i == changedPlayerIndex)
                    continue;

                if (IsPlayerBlue(i) != blueAlliance)
                    continue;

                PlayerMatchSettings player = _workingSettings.GetPlayer(i);
                int spawnIndex = blueAlliance ? player.blueSpawnIndex : player.redSpawnIndex;
                used.Add(spawnIndex);
            }

            PlayerMatchSettings changedPlayer = _workingSettings.GetPlayer(changedPlayerIndex);
            int changedSpawnIndex = blueAlliance
                ? changedPlayer.blueSpawnIndex
                : changedPlayer.redSpawnIndex;

            if (!used.Contains(changedSpawnIndex))
                return;

            for (int i = 0; i < spawnCount; i++)
            {
                if (used.Contains(i))
                    continue;

                if (blueAlliance)
                    changedPlayer.blueSpawnIndex = i;
                else
                    changedPlayer.redSpawnIndex = i;

                return;
            }
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

        private int FindFrameRateIndex(FrameRateMode value)
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
            if (loadMatch == null)
                return;

            loadMatch.SetHumanPlayerType(_workingHumanPlayer);
        }

        private void ConfigureAllDumperOwnership()
        {
            if (loadMatch == null)
                return;

            foreach (var release in loadMatch.GetOutpostReleases())
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
                   _workingSettings.playMode == PlayMode.TwoVsTwo ||
                   _workingSettings.useBlueAlliance;
        }

        private bool IsRedAllianceUsed()
        {
            return _workingSettings.playMode == PlayMode.OneVsOne ||
                   _workingSettings.playMode == PlayMode.TwoVsTwo ||
                   !_workingSettings.useBlueAlliance;
        }

        private void SetActiveSafe(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }
        
        private int GetPlayerCountForMode(PlayMode mode)
        {
            return mode switch
            {
                PlayMode.OneVsZero => 1,
                PlayMode.TwoVsZero => 2,
                PlayMode.OneVsOne => 2,
                PlayMode.ThreeVsZero => 3,
                PlayMode.TwoVsTwo => 4,
                _ => 1
            };
        }
        
        private bool IsPlayerBlue(int playerIndex)
        {
            return _workingSettings.playMode switch
            {
                PlayMode.OneVsZero => _workingSettings.useBlueAlliance,
                PlayMode.TwoVsZero => _workingSettings.useBlueAlliance,
                PlayMode.ThreeVsZero => _workingSettings.useBlueAlliance,

                PlayMode.OneVsOne => playerIndex == 0,
                PlayMode.TwoVsTwo => playerIndex < 2,

                _ => true
            };
        }
    }
}