using System.Collections;
using System.Collections.Generic;
using Audio;
using Core;
using TMPro;
using UI.Components;
using UI.RobotSelection;
using UI.Transitions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using PlayMode = Core.PlayMode;

namespace UI.MainMenu
{
    public class OptionsMenuController : MonoBehaviour
    {
        [Header("References")] [SerializeField]
        private LoadMatch loadMatch;

        [SerializeField] private GameObject menuRoot;
        [SerializeField] private ScreenFader screenFader;
        [SerializeField] private RobotGridUI robotGridUI;
        [SerializeField] private RobotSelectCursorController cursorController;

        [Header("Match Controls")] [SerializeField]
        private GamepadDropdown gameModeDropdown;

        [SerializeField] private GamepadDropdown humanPlayerDropdown;
        [SerializeField] private Button allianceButton;
        [SerializeField] private TMP_Text allianceText;

        [Header("Player Detail Panels")] [SerializeField]
        private RobotSelectDetailPanel[] detailPanels = new RobotSelectDetailPanel[4];

        [Header("Buttons")] [SerializeField] private Button playButton;
        [SerializeField] private Button backButton;
        [SerializeField] private string mainMenuSceneName = "Main_Menu";

        [Header("Input System")] [SerializeField]
        private InputActionReference toggleMenuAction;

        [SerializeField] private InputActionAsset fallbackActions;

        [Header("Behavior")] [SerializeField] private bool openOnStart = true;
        [SerializeField] private float startMenuBlackHoldTime = 0.35f;
        [SerializeField] private float startMenuFadeDuration = 3.5f;
        [SerializeField] private bool unlockCursorWhenOpen = true;
        [SerializeField] private bool relockCursorOnClose;
        [SerializeField] private bool resetFieldWhenOpening = true;
        [SerializeField] private bool resetFieldWhenPlaying = true;

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

        private bool _isOpen;
        private bool _isTransitioning;
        private bool _isRefreshingUi;
        private MatchSettings _workingSettings = new();
        private HumanPlayerType _workingHumanPlayer = HumanPlayerType.Bucket;
        private InputAction _resolvedToggleAction;
        private List<string> _blueSpawnNames = new();
        private List<string> _redSpawnNames = new();

        private void Awake()
        {
            if (loadMatch == null)
                loadMatch = FindFirstObjectByType<LoadMatch>();

            if (menuRoot != null)
                menuRoot.SetActive(false);

            PopulateStaticDropdowns();
            WireButtons();
            WireDetailPanels();
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
        }

        private void OnDisable()
        {
            if (_resolvedToggleAction != null)
            {
                _resolvedToggleAction.performed -= OnToggleMenuPerformed;
                _resolvedToggleAction.Disable();
            }
        }

        private IEnumerator Start()
        {
            if (loadMatch == null || menuRoot == null)
            {
                enabled = false;
                yield break;
            }

            yield return null;

            if (openOnStart)
                yield return OpenMenuOnStartRoutine();
        }

        private void Update()
        {
            if (_resolvedToggleAction == null)
                ResolveToggleAction();
        }

        private void WireButtons()
        {
            if (gameModeDropdown != null)
                gameModeDropdown.onValueChanged.AddListener(OnGameModeChanged);

            if (humanPlayerDropdown != null)
                humanPlayerDropdown.onValueChanged.AddListener(OnHumanPlayerChanged);

            if (allianceButton != null)
                allianceButton.onClick.AddListener(ToggleAlliance);

            if (playButton != null)
                playButton.onClick.AddListener(ApplyAndPlay);

            if (backButton != null)
                backButton.onClick.AddListener(BackToMainMenu);
        }

        private void WireDetailPanels()
        {
            if (detailPanels == null)
                return;

            for (int i = 0; i < detailPanels.Length; i++)
            {
                int playerIndex = i;
                RobotSelectDetailPanel panel = detailPanels[i];

                if (panel == null)
                    continue;

                panel.OnSpawnChanged += value => SetSpawnIndexForPanel(playerIndex, value);
                panel.OnVanityBumperChanged += value => SetVanityBumpersForPanel(playerIndex, value);
                panel.OnCameraChanged += value => SetCameraForPanel(playerIndex, value);
                panel.OnDriverStationChanged += station => SetDriverStationForPanel(playerIndex, station);
                panel.OnReadyClicked += () => SetReadyForPanel(playerIndex);
            }
        }

        private bool HasDetailPanels()
        {
            if (detailPanels == null)
                return false;

            foreach (var t in detailPanels)
            {
                if (t != null)
                    return true;
            }

            return false;
        }

        private void PopulateStaticDropdowns()
        {
            _isRefreshingUi = true;

            if (gameModeDropdown != null)
            {
                gameModeDropdown.ClearOptions();
                gameModeDropdown.AddOptions(_gameModes.ConvertAll(x => x.label));
            }

            if (humanPlayerDropdown != null)
            {
                humanPlayerDropdown.ClearOptions();
                humanPlayerDropdown.AddOptions(_humanPlayerModes.ConvertAll(x => x.label));
            }

            _isRefreshingUi = false;
        }

        private IEnumerator OpenMenuOnStartRoutine()
        {
            if (screenFader == null)
            {
                OpenMenuImmediate();
                yield break;
            }

            _isTransitioning = true;
            screenFader.SetBlackImmediate(true);
            OpenMenuImmediate();

            if (startMenuBlackHoldTime > 0f)
                yield return new WaitForSecondsRealtime(startMenuBlackHoldTime);

            screenFader.FadeFromBlack(startMenuFadeDuration, () => _isTransitioning = false);
        }

        private void ResolveToggleAction()
        {
            if (toggleMenuAction != null && toggleMenuAction.action != null)
            {
                _resolvedToggleAction = toggleMenuAction.action;
                return;
            }

            _resolvedToggleAction = null;
        }

        private void OnToggleMenuPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || _isTransitioning)
                return;

            if (_isOpen)
                return;

            OpenMenu();
        }

        private void OpenMenu()
        {
            if (_isTransitioning || loadMatch == null || menuRoot == null)
                return;

            _isTransitioning = true;

            void ShowMenu()
            {
                OpenMenuImmediate();
                if (resetFieldWhenOpening)
                    loadMatch.ResetField();
            }

            void Done() => _isTransitioning = false;

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
            LoadWorkingSettings();

            _isOpen = true;

            if (menuRoot != null)
                menuRoot.SetActive(true);

            Time.timeScale = 0f;

            SetupCursorController();
            RefreshAllUi();
            RefreshPlayButtonState();

            if (unlockCursorWhenOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            SetRobotInputsEnabled(false);
        }

        private void SetupCursorController()
        {
            if (cursorController == null)
                return;

            cursorController.OnPlayerJoined -= OnPlayerJoined;
            cursorController.OnPlayerUnjoined -= OnPlayerUnjoined;
            cursorController.OnPlayerHoverChanged -= OnPlayerHoverChanged;
            cursorController.OnPlayerLockedIn -= OnPlayerLockedIn;
            cursorController.OnPlayerUnlocked -= OnPlayerUnlocked;
            cursorController.OnPlayerReadyChanged -= OnPlayerReadyChanged;
            cursorController.OnPlayerDetailInput -= OnPlayerDetailInput;

            cursorController.OnPlayerJoined += OnPlayerJoined;
            cursorController.OnPlayerUnjoined += OnPlayerUnjoined;
            cursorController.OnPlayerHoverChanged += OnPlayerHoverChanged;
            cursorController.OnPlayerLockedIn += OnPlayerLockedIn;
            cursorController.OnPlayerUnlocked += OnPlayerUnlocked;
            cursorController.OnPlayerReadyChanged += OnPlayerReadyChanged;
            cursorController.OnPlayerDetailInput += OnPlayerDetailInput;

            cursorController.ActivateSelectScreen(GetPlayerCountForMode(_workingSettings.playMode));
        }

        private void LoadDynamicData()
        {
            if (robotGridUI != null)
                robotGridUI.BuildGrid();

            _blueSpawnNames = loadMatch != null ? loadMatch.GetBlueSpawnNames() : new List<string>();
            _redSpawnNames = loadMatch != null ? loadMatch.GetRedSpawnNames() : new List<string>();
        }

        private void LoadWorkingSettings()
        {
            _workingSettings = loadMatch != null ? loadMatch.GetSettingsCopy() : new MatchSettings();

            MatchLaunchData launchData = GameSessionManager.Instance != null
                ? GameSessionManager.Instance.CurrentLaunchData
                : null;
            if (launchData != null)
                _workingHumanPlayer = launchData.humanPlayerType;
        }

        private void ApplyAndPlay()
        {
            if (_isTransitioning || loadMatch == null)
                return;

            if (cursorController != null && !cursorController.AreAllRequiredPlayersReady())
            {
                AudioManager.Instance?.PlayError();
                RefreshPlayButtonState();
                return;
            }

            _isTransitioning = true;

            void ApplyAndClose()
            {
                SyncLockedRobotsIntoSettings();
                CloseMenuImmediate();
                loadMatch.ApplySettings(_workingSettings);
                loadMatch.SetHumanPlayerType(_workingHumanPlayer);

                if (menuRoot != null)
                    menuRoot.SetActive(false);

                _isOpen = false;
                ResumeRuntimeState();

                if (resetFieldWhenPlaying)
                    loadMatch.ResetField();
                else
                    SetRobotInputsEnabled(true);
            }

            void Done() => _isTransitioning = false;

            if (screenFader != null)
                screenFader.FadeToBlackThen(ApplyAndClose, true, Done);
            else
            {
                ApplyAndClose();
                Done();
            }
        }

        private void BackToMainMenu()
        {
            if (_isTransitioning)
                return;

            CloseMenuImmediate();

            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (GameSessionManager.Instance != null)
                GameSessionManager.Instance.ClearLaunchData();

            if (!string.IsNullOrWhiteSpace(mainMenuSceneName))
                SceneTransitionManager.EnsureExists().LoadScene(mainMenuSceneName);
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

        private void RefreshAllUi()
        {
            _isRefreshingUi = true;

            if (gameModeDropdown != null)
                gameModeDropdown.SetValueWithoutNotify(FindGameModeIndex(_workingSettings.playMode));

            if (humanPlayerDropdown != null)
                humanPlayerDropdown.SetValueWithoutNotify(FindHumanPlayerIndex(_workingHumanPlayer));

            RefreshAllianceUi();

            _isRefreshingUi = false;

            if (HasDetailPanels())
                RefreshAllDetailPanels();

            RefreshPlayButtonState();
        }

        private void RefreshAllianceUi()
        {
            bool canSwapAlliance = CanSwapAllianceForMode(_workingSettings.playMode);

            if (allianceButton != null)
            {
                allianceButton.gameObject.SetActive(canSwapAlliance);
                allianceButton.interactable = canSwapAlliance;
            }

            if (allianceText != null)
            {
                allianceText.gameObject.SetActive(canSwapAlliance);
                allianceText.text = _workingSettings.useBlueAlliance ? "Blue Alliance" : "Red Alliance";
            }
        }

        private static bool CanSwapAllianceForMode(PlayMode mode)
        {
            return mode == PlayMode.OneVsZero ||
                   mode == PlayMode.TwoVsZero ||
                   mode == PlayMode.ThreeVsZero;
        }

        private void RefreshAllDetailPanels()
        {
            for (int i = 0; i < 4; i++)
                RefreshDetailPanel(i);
        }

        private void RefreshDetailPanel(int playerIndex)
        {
            if (detailPanels == null || playerIndex < 0 || playerIndex >= detailPanels.Length)
                return;

            RobotSelectDetailPanel panel = detailPanels[playerIndex];
            if (panel == null)
                return;

            if (_workingSettings == null || loadMatch == null)
            {
                panel.ShowInactive(playerIndex);
                return;
            }

            int playerCount = GetPlayerCountForMode(_workingSettings.playMode);
            if (playerIndex >= playerCount)
            {
                panel.ShowInactive(playerIndex);
                return;
            }

            bool isBlue = IsPlayerBlue(playerIndex);
            bool joined = cursorController == null || cursorController.IsPlayerJoined(playerIndex);

            if (!joined)
            {
                DeviceKind preferred = cursorController != null
                    ? cursorController.GetPreferredDeviceKind(playerIndex)
                    : DeviceKind.Gamepad;

                panel.ShowJoinPrompt(playerIndex, preferred, isBlue);
                return;
            }

            int displayRobotIndex = GetDisplayRobotIndex(playerIndex);
            Sprite portrait = loadMatch.GetRobotPreviewSpriteAt(displayRobotIndex);
            string robotName = loadMatch.GetRobotNameAt(displayRobotIndex);

            bool locked = cursorController == null || cursorController.IsPlayerLocked(playerIndex);
            bool ready = cursorController != null && cursorController.IsPlayerReady(playerIndex);

            if (!locked)
            {
                panel.ShowSelectingRobot(playerIndex, GetPlayerColor(playerIndex), isBlue, portrait, robotName);
                return;
            }

            if (ready)
            {
                panel.ShowReady(playerIndex, GetPlayerColor(playerIndex), isBlue, portrait, robotName);
                return;
            }

            PlayerMatchSettings player = _workingSettings.GetPlayer(playerIndex);
            List<string> spawnNames = isBlue ? _blueSpawnNames : _redSpawnNames;
            int selectedSpawnIndex = isBlue ? player.blueSpawnIndex : player.redSpawnIndex;
            bool hasVanityMaterial = loadMatch.HasVanityBumperMaterialAt(displayRobotIndex);

            panel.Show(
                playerIndex,
                GetPlayerColor(playerIndex),
                isBlue,
                portrait,
                robotName,
                spawnNames,
                selectedSpawnIndex,
                hasVanityMaterial,
                player.useVanityBumpers,
                _cameraModes.ConvertAll(x => x.label),
                FindCameraModeIndex(player.view),
                player.view == Cameras.DriverStation,
                player.driverStation
            );
        }

        private int GetDisplayRobotIndex(int playerIndex)
        {
            if (cursorController != null && cursorController.IsPlayerJoined(playerIndex))
            {
                int locked = cursorController.GetLockedRobotIndex(playerIndex);
                if (locked >= 0)
                    return locked;

                return cursorController.GetHoverRobotIndex(playerIndex);
            }

            return _workingSettings.GetPlayer(playerIndex).robotIndex;
        }

        private static Color GetPlayerColor(int playerIndex)
        {
            return playerIndex switch
            {
                0 => new Color(0.25f, 0.6f, 1f),
                1 => new Color(1f, 0.25f, 0.25f),
                2 => new Color(0.3f, 1f, 0.4f),
                3 => new Color(1f, 0.9f, 0.2f),
                _ => Color.white
            };
        }

        private void OnGameModeChanged(int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            dropdownIndex = Mathf.Clamp(dropdownIndex, 0, _gameModes.Count - 1);
            _workingSettings.playMode = _gameModes[dropdownIndex].value;

            if (cursorController != null)
                cursorController.ActivateSelectScreen(GetPlayerCountForMode(_workingSettings.playMode));

            RefreshAllUi();
        }

        private void OnHumanPlayerChanged(int dropdownIndex)
        {
            if (_isRefreshingUi)
                return;

            dropdownIndex = Mathf.Clamp(dropdownIndex, 0, _humanPlayerModes.Count - 1);
            _workingHumanPlayer = _humanPlayerModes[dropdownIndex].value;

            if (loadMatch != null)
                loadMatch.SetHumanPlayerType(_workingHumanPlayer);
        }

        private void OnPlayerDetailInput(int playerIndex, Vector2 nav, bool confirmPressed, bool cancelPressed)
        {
            if (detailPanels == null ||
                playerIndex < 0 ||
                playerIndex >= detailPanels.Length ||
                detailPanels[playerIndex] == null)
            {
                return;
            }

            if (cancelPressed)
                return;

            detailPanels[playerIndex].MoveSelection(nav);

            if (confirmPressed)
                detailPanels[playerIndex].SubmitSelection();
        }

        private void ToggleAlliance()
        {
            if (!CanSwapAllianceForMode(_workingSettings.playMode))
            {
                AudioManager.Instance?.PlayError();
                return;
            }

            _workingSettings.useBlueAlliance = !_workingSettings.useBlueAlliance;
            RefreshAllUi();
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

            if (HasDetailPanels())
                RefreshDetailPanel(playerIndex);
        }

        private void SetVanityBumpersForPanel(int playerIndex, bool value)
        {
            if (_isRefreshingUi)
                return;

            _workingSettings.GetPlayer(playerIndex).useVanityBumpers = value;
        }

        private void SetDriverStationForPanel(int playerIndex, StationNum station)
        {
            if (_isRefreshingUi)
                return;

            _workingSettings.GetPlayer(playerIndex).driverStation = station;

            if (HasDetailPanels())
                RefreshDetailPanel(playerIndex);
        }

        private void SetCameraForPanel(int playerIndex, int cameraIndex)
        {
            if (_isRefreshingUi)
                return;

            cameraIndex = Mathf.Clamp(cameraIndex, 0, _cameraModes.Count - 1);
            _workingSettings.GetPlayer(playerIndex).view = _cameraModes[cameraIndex].value;

            if (HasDetailPanels())
                RefreshDetailPanel(playerIndex);
        }

        private void SetReadyForPanel(int playerIndex)
        {
            if (cursorController == null || !cursorController.IsPlayerLocked(playerIndex))
                return;

            cursorController.SetPlayerReady(playerIndex, true);
            RefreshDetailPanel(playerIndex);
            RefreshPlayButtonState();

            if (playerIndex == 0)
                cursorController.FocusPlayerOneOnTopBar(playButton);
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
                if (i == changedPlayerIndex || IsPlayerBlue(i) != blueAlliance)
                    continue;

                PlayerMatchSettings player = _workingSettings.GetPlayer(i);
                used.Add(blueAlliance ? player.blueSpawnIndex : player.redSpawnIndex);
            }

            PlayerMatchSettings changedPlayer = _workingSettings.GetPlayer(changedPlayerIndex);
            int changedSpawnIndex = blueAlliance ? changedPlayer.blueSpawnIndex : changedPlayer.redSpawnIndex;

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
                if (_gameModes[i].value == value)
                    return i;

            return 0;
        }

        private int FindHumanPlayerIndex(HumanPlayerType value)
        {
            for (int i = 0; i < _humanPlayerModes.Count; i++)
                if (_humanPlayerModes[i].value == value)
                    return i;

            return 0;
        }

        private int FindCameraModeIndex(Cameras value)
        {
            for (int i = 0; i < _cameraModes.Count; i++)
                if (_cameraModes[i].value == value)
                    return i;

            return 0;
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

        private void SetRobotInputsEnabled(bool enabledBool)
        {
            if (loadMatch == null)
                return;

            GameObject[] robots = loadMatch.GetLoadedRobots();
            if (robots == null)
                return;

            foreach (GameObject robot in robots)
            {
                if (robot == null)
                    continue;

                PlayerInput playerInput = robot.GetComponent<PlayerInput>();
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

        private void CloseMenuImmediate()
        {
            if (cursorController != null)
            {
                cursorController.OnPlayerJoined -= OnPlayerJoined;
                cursorController.OnPlayerUnjoined -= OnPlayerUnjoined;
                cursorController.OnPlayerHoverChanged -= OnPlayerHoverChanged;
                cursorController.OnPlayerLockedIn -= OnPlayerLockedIn;
                cursorController.OnPlayerUnlocked -= OnPlayerUnlocked;
                cursorController.OnPlayerReadyChanged -= OnPlayerReadyChanged;
                cursorController.OnPlayerDetailInput -= OnPlayerDetailInput;
                cursorController.Deactivate();
            }
        }

        private void OnPlayerJoined(int playerIndex)
        {
            RefreshDetailPanel(playerIndex);
            RefreshPlayButtonState();
        }

        private void OnPlayerUnjoined(int playerIndex)
        {
            RefreshDetailPanel(playerIndex);
            RefreshPlayButtonState();
        }

        private void OnPlayerHoverChanged(int playerIndex, int robotIndex)
        {
            _workingSettings.GetPlayer(playerIndex).robotIndex = robotIndex;
            RefreshDetailPanel(playerIndex);
        }

        private void OnPlayerLockedIn(int playerIndex, int robotIndex)
        {
            _workingSettings.GetPlayer(playerIndex).robotIndex = robotIndex;
            RefreshDetailPanel(playerIndex);

            if (detailPanels != null &&
                playerIndex >= 0 &&
                playerIndex < detailPanels.Length &&
                detailPanels[playerIndex] != null)
            {
                detailPanels[playerIndex].SelectThirdPersonCameraButton();
            }

            RefreshPlayButtonState();
        }

        private void OnPlayerUnlocked(int playerIndex)
        {
            RefreshDetailPanel(playerIndex);
            RefreshPlayButtonState();
        }

        private void OnPlayerReadyChanged(int playerIndex)
        {
            RefreshDetailPanel(playerIndex);
            RefreshPlayButtonState();
        }

        private void SyncLockedRobotsIntoSettings()
        {
            if (cursorController == null)
                return;

            int playerCount = GetPlayerCountForMode(_workingSettings.playMode);
            for (int i = 0; i < playerCount; i++)
            {
                int robotIndex = cursorController.GetLockedRobotIndex(i);
                if (robotIndex >= 0)
                    _workingSettings.GetPlayer(i).robotIndex = robotIndex;
            }
        }

        private void RefreshPlayButtonState()
        {
            if (playButton == null)
                return;

            playButton.interactable = cursorController != null && cursorController.AreAllRequiredPlayersReady();
        }
    }
}