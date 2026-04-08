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
        [SerializeField] private Button allianceButton;
        [SerializeField] private TMP_Text allianceButtonText;

        [Header("Robot Panels")]
        [SerializeField] private RobotPanelUI robotPanel1;
        [SerializeField] private RobotPanelUI robotPanel2;

        [Header("Bottom Buttons")]
        [SerializeField] private Button applyButton;
        [SerializeField] private Button cancelButton;

        [Header("Input System")]
        [SerializeField] private InputActionReference toggleMenuAction;
        [SerializeField] private InputActionAsset fallbackActions;
        [SerializeField] private string fallbackToggleActionName = "ToggleMenu";

        [Header("Behavior")]
        [SerializeField] private bool openMenuOnStart = true;
        [SerializeField] private bool resetOnCancel;
        [SerializeField] private bool unlockCursorWhenOpen = true;
        [SerializeField] private bool relockCursorOnClose;
        [SerializeField] private bool debugLogs = true;

        private readonly List<(PlayMode value, string label)> _gameModes = new()
        {
            (PlayMode.OneVsZero, "Singleplayer"),
            (PlayMode.TwoVsZero, "2v0"),
            (PlayMode.OneVsOne, "1v1")
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

        private void Awake()
        {
            if (loadMatch == null)
                loadMatch = FindFirstObjectByType<LoadMatch>();

            if (menuRoot != null)
                menuRoot.SetActive(false);

            WireButtons();
            WirePanels();
            PopulateStaticDropdowns();
            ResolveToggleAction();
        }

        private void OnEnable()
        {
            ResolveToggleAction();

            if (_resolvedToggleAction != null)
            {
                _resolvedToggleAction.Enable();
                _resolvedToggleAction.performed += OnToggleMenuPerformed;
            }
            else if (debugLogs)
            {
                Debug.LogError("OptionsMenuController: no toggle action could be resolved.");
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
            if (loadMatch == null)
            {
                Debug.LogError("OptionsMenuController: LoadMatch reference is missing.");
                enabled = false;
                return;
            }

            if (menuRoot == null)
            {
                Debug.LogError("OptionsMenuController: menuRoot is missing.");
                enabled = false;
                return;
            }

            if (openMenuOnStart)
            {
                OpenMenuImmediate();
            }
            else
            {
                ForceClosedState();
            }
        }

        private void Update()
        {
            if (_isOpen)
                RefreshVisibleState();

            if (_resolvedToggleAction == null &&
                Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (debugLogs)
                    Debug.LogWarning("OptionsMenuController: using emergency Escape fallback because no InputAction resolved.");

                ToggleMenu();
            }
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
                CloseMenuWithoutApply(resetOnCancel);
            else
                OpenMenu();
        }

        private void WireButtons()
        {
            if (applyButton != null)
                applyButton.onClick.AddListener(ApplyAndClose);

            if (cancelButton != null)
                cancelButton.onClick.AddListener(() => CloseMenuWithoutApply(resetOnCancel));

            if (allianceButton != null)
                allianceButton.onClick.AddListener(ToggleAlliance);

            if (gameModeDropdown != null)
                gameModeDropdown.onValueChanged.AddListener(OnGameModeChanged);

            if (cameraDropdown != null)
                cameraDropdown.onValueChanged.AddListener(OnCameraChanged);
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
        }

        private void LoadDynamicData()
        {
            _blueSpawnNames = loadMatch.GetBlueSpawnNames();
            _redSpawnNames = loadMatch.GetRedSpawnNames();
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
                ApplySettingsToUI();

                _isOpen = true;
                menuRoot.SetActive(true);

                Time.timeScale = 0f;

                if (unlockCursorWhenOpen)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                SetRobotInputsEnabled(false);

                if (debugLogs) Debug.Log("OptionsMenuController: menu opened.");
            }

            void Done()
            {
                _isTransitioning = false;
            }

            if (screenFader != null)
            {
                screenFader.FadeToBlackThen(ShowMenu, true, Done);
            }
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
            ApplySettingsToUI();

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

                if (menuRoot != null) menuRoot.SetActive(false);

                _isOpen = false;
                loadMatch.ResetField();

                if (debugLogs) Debug.Log("OptionsMenuController: applied settings and reset field.");
            }

            void Done()
            {
                _isTransitioning = false;
            }

            if (screenFader != null)
            {
                screenFader.FadeToBlackThen(ApplyAndReset, true, Done);
            }
            else
            {
                ApplyAndReset();
                Done();
            }
        }

        private void CloseMenuWithoutApply(bool resetField)
        {
            if (_isTransitioning)
                return;

            _isTransitioning = true;

            void CloseAction()
            {
                ResumeRuntimeState();

                if (menuRoot != null) menuRoot.SetActive(false);

                _isOpen = false;

                if (resetField && loadMatch != null)
                {
                    loadMatch.ResetField();
                }
                else
                {
                    SetRobotInputsEnabled(true);
                }

                if (debugLogs) Debug.Log("OptionsMenuController: closed without apply.");
            }

            void Done()
            {
                _isTransitioning = false;
            }

            if (screenFader != null)
            {
                screenFader.FadeToBlackThen(CloseAction, true, Done);
            }
            else
            {
                CloseAction();
                Done();
            }
        }

        private void ForceClosedState()
        {
            _isOpen = false;
            _isTransitioning = false;

            if (menuRoot != null)
                menuRoot.SetActive(false);

            Time.timeScale = 1f;
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

        private void ApplySettingsToUI()
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

            RefreshVisibleState();
        }

        private void RefreshVisibleState()
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
                    selectedSpawnIndex = _workingSettings.redSpawnIndex2;
                }
            }
            else
            {
                bool useBlue = _workingSettings.useBlueAlliance;
                sideLabel = useBlue ? "Blue Alliance" : "Red Alliance";

                if (useBlue)
                {
                    spawnNames = _blueSpawnNames;
                    selectedSpawnIndex = panelIndex == 0 ? _workingSettings.blueSpawnIndex1 : _workingSettings.blueSpawnIndex2;
                }
                else
                {
                    spawnNames = _redSpawnNames;
                    selectedSpawnIndex = panelIndex == 0 ? _workingSettings.redSpawnIndex1 : _workingSettings.redSpawnIndex2;
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
            RefreshVisibleState();
        }

        private void OnGameModeChanged(int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            _workingSettings.playMode = _gameModes[Mathf.Clamp(dropdownIndex, 0, _gameModes.Count - 1)].value;
            RefreshVisibleState();
        }

        private void OnCameraChanged(int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            _workingSettings.view = _cameraModes[Mathf.Clamp(dropdownIndex, 0, _cameraModes.Count - 1)].value;
            RefreshVisibleState();
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
            if (count <= 0) return 0;
            value %= count;
            if (value < 0) value += count;
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
                    _workingSettings.redSpawnIndex2 = value;

                RefreshVisibleState();
                return;
            }

            if (_workingSettings.playMode == PlayMode.OneVsZero)
            {
                if (_workingSettings.useBlueAlliance)
                    _workingSettings.blueSpawnIndex1 = value;
                else
                    _workingSettings.redSpawnIndex1 = value;

                RefreshVisibleState();
                return;
            }

            if (_workingSettings.useBlueAlliance)
            {
                if (panelIndex == 0)
                {
                    _workingSettings.blueSpawnIndex1 = value;
                    if (_workingSettings.blueSpawnIndex1 == _workingSettings.blueSpawnIndex2)
                        _workingSettings.blueSpawnIndex2 = FindDifferentIndex(_workingSettings.blueSpawnIndex1, _blueSpawnNames.Count);
                }
                else
                {
                    _workingSettings.blueSpawnIndex2 = value;
                    if (_workingSettings.blueSpawnIndex2 == _workingSettings.blueSpawnIndex1)
                        _workingSettings.blueSpawnIndex1 = FindDifferentIndex(_workingSettings.blueSpawnIndex2, _blueSpawnNames.Count);
                }
            }
            else
            {
                if (panelIndex == 0)
                {
                    _workingSettings.redSpawnIndex1 = value;
                    if (_workingSettings.redSpawnIndex1 == _workingSettings.redSpawnIndex2)
                        _workingSettings.redSpawnIndex2 = FindDifferentIndex(_workingSettings.redSpawnIndex1, _redSpawnNames.Count);
                }
                else
                {
                    _workingSettings.redSpawnIndex2 = value;
                    if (_workingSettings.redSpawnIndex2 == _workingSettings.redSpawnIndex1)
                        _workingSettings.redSpawnIndex1 = FindDifferentIndex(_workingSettings.redSpawnIndex2, _redSpawnNames.Count);
                }
            }

            RefreshVisibleState();
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
    }
}