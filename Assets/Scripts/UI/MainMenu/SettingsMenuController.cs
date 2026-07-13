using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using UI.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace UI.MainMenu
{
    public class SettingsMenuController : MonoBehaviour
    {
        private enum ControlCommandGroup
        {
            Common,
            Rebuilt,
            Reefscape
        }

        private readonly struct CommandRow
        {
            public readonly string Label;
            public readonly string ActionName;
            public readonly BindingKind Kind;
            public readonly ControlCommandGroup Group;

            public CommandRow(string label, string actionName, BindingKind kind, ControlCommandGroup group)
            {
                Label = label;
                ActionName = actionName;
                Kind = kind;
                Group = group;
            }
        }

        private readonly struct BindingOption
        {
            public readonly string Label;
            public readonly string Path;

            public BindingOption(string label, string path)
            {
                Label = label;
                Path = path;
            }
        }

        private sealed class VectorPreset
        {
            public string Label;
            public string Up;
            public string Down;
            public string Left;
            public string Right;
            public string SimplePath;
        }

        [Header("Display")]
        [SerializeField] private GamepadDropdown frameRateDropdown;
        [SerializeField] private GamepadDropdown resolutionDropdown;
        [SerializeField] private GamepadDropdown windowModeDropdown;
        [SerializeField] private GamepadDropdown graphicsDropdown;

        [Header("Controls")]
        [Tooltip("Assign the same Input Actions asset used by robot PlayerInput/LoadMatch. This replaces the old LoadMatch dependency so the settings menu can work in the Main Menu scene.")]
        [SerializeField] private InputActionAsset controlsActions;
        [Tooltip("Selects which control group is shown in the binding rows.")]
        [SerializeField] private GamepadDropdown controlTypeDropdown;
        [SerializeField] private GamepadDropdown playerDropdown;
        [SerializeField] private GamepadDropdown deviceDropdown;
        [SerializeField] private GamepadDropdown gamepadDropdown;
        [SerializeField] private Button resetPlayerButton;
        [SerializeField] private Transform rowsRoot;
        [SerializeField] private ControlBindingRow rowPrefab;

        [Header("Control Input")]
        [SerializeField] private string actionMapName = "Robot";
        [SerializeField] private string keyboardControlScheme = "Keyboard";
        [SerializeField] private string gamepadControlScheme = "Gamepad";

        [Header("Buttons")]
        [SerializeField] private Button applyButton;

        private const string FrameRatePrefKey = "FrameRateMode";
        private const string WindowModePrefKey = "WindowMode";
        private const string ResolutionPrefKey = "ResolutionMode";
        private const string GraphicsPrefKey = "GraphicsQuality";
        private const string ControlTypePrefKey = "Controls_SelectedType";

        private const string BindingPrefsPrefix = "Controls_Player_";
        private const string DevicePrefsPrefix = "Controls_PlayerDevice_";
        private const string GamepadPrefsPrefix = "Controls_PlayerGamepadIndex_";

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

        private readonly List<(ControlCommandGroup value, string label)> _controlTypes = new()
        {
            (ControlCommandGroup.Common, "Common"),
            (ControlCommandGroup.Rebuilt, "Rebuilt"),
            (ControlCommandGroup.Reefscape, "Reefscape")
        };

        private readonly List<(int width, int height, string label)> _resolutionModes = new();
        private readonly List<ControlBindingRow> _spawnedRows = new();

        private InputActionAsset _workingActions;
        private int _selectedPlayerIndex;
        private DeviceKind _selectedDevice;
        private ControlCommandGroup _selectedControlGroup = ControlCommandGroup.Common;
        private bool _isRefreshing;

        private static readonly CommandRow[] Commands =
        {
            // Common / General
            new("Drive", "Drive", BindingKind.Drive, ControlCommandGroup.Common),
            new("Rotate", "Rotate", BindingKind.Rotate, ControlCommandGroup.Common),
            new("Flip Camera", "FlipCamera", BindingKind.Button, ControlCommandGroup.Common),
            new("Restart", "Restart", BindingKind.Button, ControlCommandGroup.Common),
            new("Menu", "Menu", BindingKind.Button, ControlCommandGroup.Common),
            new("Shoot", "Shoot", BindingKind.Button, ControlCommandGroup.Common),
            new("Intake", "Intake", BindingKind.Button, ControlCommandGroup.Common),

            // Rebuilt
            new("Pass Left", "PassLeft", BindingKind.Button, ControlCommandGroup.Rebuilt),
            new("Pass Right", "PassRight", BindingKind.Button, ControlCommandGroup.Rebuilt),
            new("Hub", "Hub", BindingKind.Button, ControlCommandGroup.Rebuilt),
            new("Robot Special", "RobotSpecial", BindingKind.Button, ControlCommandGroup.Rebuilt),
            new("Human Player Dump", "HumanPlayerDump", BindingKind.Button, ControlCommandGroup.Rebuilt),

            // Reefscape
            new("Auto Align", "AutoAlign", BindingKind.Button, ControlCommandGroup.Reefscape),
            new("L1", "L1", BindingKind.Button, ControlCommandGroup.Reefscape),
            new("L2", "L2", BindingKind.Button, ControlCommandGroup.Reefscape),
            new("L3", "L3", BindingKind.Button, ControlCommandGroup.Reefscape),
            new("L4", "L4", BindingKind.Button, ControlCommandGroup.Reefscape),
            new("Barge", "Barge", BindingKind.Button, ControlCommandGroup.Reefscape),
            new("Algae High", "AlgaeHigh", BindingKind.Button, ControlCommandGroup.Reefscape),
            new("Algae Low", "AlgaeLow", BindingKind.Button, ControlCommandGroup.Reefscape),
            new("Algae Hold", "AlgaeHold", BindingKind.Button, ControlCommandGroup.Reefscape),
            new("Climb", "Climb", BindingKind.Button, ControlCommandGroup.Reefscape)
        };

        private static readonly BindingOption[] KeyboardButtonOptions =
        {
            new("Default", null),
            new("Q", "<Keyboard>/q"),
            new("E", "<Keyboard>/e"),
            new("R", "<Keyboard>/r"),
            new("T", "<Keyboard>/t"),
            new("Y", "<Keyboard>/y"),
            new("U", "<Keyboard>/u"),
            new("I", "<Keyboard>/i"),
            new("O", "<Keyboard>/o"),
            new("P", "<Keyboard>/p"),
            new("F", "<Keyboard>/f"),
            new("G", "<Keyboard>/g"),
            new("H", "<Keyboard>/h"),
            new("J", "<Keyboard>/j"),
            new("K", "<Keyboard>/k"),
            new("L", "<Keyboard>/l"),
            new("Z", "<Keyboard>/z"),
            new("X", "<Keyboard>/x"),
            new("C", "<Keyboard>/c"),
            new("V", "<Keyboard>/v"),
            new("B", "<Keyboard>/b"),
            new("N", "<Keyboard>/n"),
            new("M", "<Keyboard>/m"),
            new("Left Shift", "<Keyboard>/leftShift"),
            new("Left Ctrl", "<Keyboard>/leftCtrl"),
            new("Left Alt", "<Keyboard>/leftAlt"),
            new("Space", "<Keyboard>/space"),
            new("Tab", "<Keyboard>/tab"),
            new("Escape", "<Keyboard>/escape"),
            new("Up Arrow", "<Keyboard>/upArrow"),
            new("Down Arrow", "<Keyboard>/downArrow"),
            new("Left Arrow", "<Keyboard>/leftArrow"),
            new("Right Arrow", "<Keyboard>/rightArrow"),
            new("1", "<Keyboard>/1"),
            new("2", "<Keyboard>/2"),
            new("3", "<Keyboard>/3"),
            new("4", "<Keyboard>/4"),
            new("5", "<Keyboard>/5"),
            new("6", "<Keyboard>/6"),
            new("7", "<Keyboard>/7"),
            new("8", "<Keyboard>/8"),
            new("9", "<Keyboard>/9"),
            new("0", "<Keyboard>/0")
        };

        private static readonly BindingOption[] GamepadButtonOptions =
        {
            new("Default", null),
            new("A / South", "<Gamepad>/buttonSouth"),
            new("B / East", "<Gamepad>/buttonEast"),
            new("X / West", "<Gamepad>/buttonWest"),
            new("Y / North", "<Gamepad>/buttonNorth"),
            new("Left Bumper", "<Gamepad>/leftShoulder"),
            new("Right Bumper", "<Gamepad>/rightShoulder"),
            new("Left Trigger", "<Gamepad>/leftTrigger"),
            new("Right Trigger", "<Gamepad>/rightTrigger"),
            new("Left Stick Press", "<Gamepad>/leftStickPress"),
            new("Right Stick Press", "<Gamepad>/rightStickPress"),
            new("D-Pad Up", "<Gamepad>/dpad/up"),
            new("D-Pad Down", "<Gamepad>/dpad/down"),
            new("D-Pad Left", "<Gamepad>/dpad/left"),
            new("D-Pad Right", "<Gamepad>/dpad/right")
        };

        private static readonly VectorPreset[] KeyboardDrivePresets =
        {
            new() { Label = "Default" },
            new() { Label = "WASD", Up = "<Keyboard>/w", Down = "<Keyboard>/s", Left = "<Keyboard>/a", Right = "<Keyboard>/d" },
            new() { Label = "Arrow Keys", Up = "<Keyboard>/upArrow", Down = "<Keyboard>/downArrow", Left = "<Keyboard>/leftArrow", Right = "<Keyboard>/rightArrow" },
            new() { Label = "IJKL", Up = "<Keyboard>/i", Down = "<Keyboard>/k", Left = "<Keyboard>/j", Right = "<Keyboard>/l" }
        };

        private static readonly VectorPreset[] KeyboardRotatePresets =
        {
            new() { Label = "Default" },
            new() { Label = "J / L", Left = "<Keyboard>/j", Right = "<Keyboard>/l" },
            new() { Label = "Q / E", Left = "<Keyboard>/q", Right = "<Keyboard>/e" },
            new() { Label = "Left Arrow / Right Arrow", Left = "<Keyboard>/leftArrow", Right = "<Keyboard>/rightArrow" }
        };

        private static readonly VectorPreset[] GamepadDrivePresets =
        {
            new() { Label = "Default" },
            new() { Label = "Left Stick", SimplePath = "<Gamepad>/leftStick" },
            new() { Label = "Right Stick", SimplePath = "<Gamepad>/rightStick" }
        };

        private static readonly VectorPreset[] GamepadRotatePresets =
        {
            new() { Label = "Default" },
            new() { Label = "Right Stick", SimplePath = "<Gamepad>/rightStick" },
            new() { Label = "Left Stick", SimplePath = "<Gamepad>/leftStick" },
            new() { Label = "D-Pad", SimplePath = "<Gamepad>/dpad" }
        };

        private void Awake()
        {
            if (rowPrefab == null)
                rowPrefab = Resources.Load<ControlBindingRow>("UI/ControlBindingRow");

            BuildResolutionModes();
            PopulateDropdowns();
            WireUi();
            LoadSavedSettings();
            RefreshControls();
        }

        private void OnEnable()
        {
            RefreshControls();
        }

        private void OnDestroy()
        {
            if (_workingActions != null)
                Destroy(_workingActions);
        }

        private void WireUi()
        {
            if (frameRateDropdown != null)
                frameRateDropdown.onValueChanged.AddListener(_ => ApplyFrameRate());

            if (resolutionDropdown != null)
                resolutionDropdown.onValueChanged.AddListener(_ => ApplyResolution());

            if (windowModeDropdown != null)
                windowModeDropdown.onValueChanged.AddListener(_ => ApplyWindowMode());

            if (graphicsDropdown != null)
                graphicsDropdown.onValueChanged.AddListener(_ => ApplyGraphicsQuality());

            if (controlTypeDropdown != null)
                controlTypeDropdown.onValueChanged.AddListener(OnControlTypeChanged);

            if (playerDropdown != null)
                playerDropdown.onValueChanged.AddListener(OnPlayerChanged);

            if (deviceDropdown != null)
                deviceDropdown.onValueChanged.AddListener(OnDeviceChanged);

            if (gamepadDropdown != null)
                gamepadDropdown.onValueChanged.AddListener(OnGamepadChanged);

            if (resetPlayerButton != null)
                resetPlayerButton.onClick.AddListener(ResetSelectedDeviceForPlayer);

            if (applyButton != null)
                applyButton.onClick.AddListener(ApplyAllSettings);
        }

        private void PopulateDropdowns()
        {
            _isRefreshing = true;

            if (frameRateDropdown != null)
            {
                frameRateDropdown.ClearOptions();
                frameRateDropdown.AddOptions(_frameRateModes.ConvertAll(x => x.label));
            }

            if (windowModeDropdown != null)
            {
                windowModeDropdown.ClearOptions();
                windowModeDropdown.AddOptions(_windowModes.ConvertAll(x => x.label));
            }

            if (resolutionDropdown != null)
            {
                resolutionDropdown.ClearOptions();
                resolutionDropdown.AddOptions(_resolutionModes.ConvertAll(x => x.label));
            }

            if (graphicsDropdown != null)
            {
                graphicsDropdown.ClearOptions();
                graphicsDropdown.AddOptions(new List<string>(QualitySettings.names));
            }

            if (controlTypeDropdown != null)
            {
                controlTypeDropdown.ClearOptions();
                controlTypeDropdown.AddOptions(_controlTypes.ConvertAll(x => x.label));
            }

            _isRefreshing = false;
        }

        private void BuildResolutionModes()
        {
            _resolutionModes.Clear();
            _resolutionModes.Add((1280, 720, "1280 x 720"));
            _resolutionModes.Add((1600, 900, "1600 x 900"));
            _resolutionModes.Add((1920, 1080, "1920 x 1080"));
            _resolutionModes.Add((2560, 1440, "2560 x 1440"));
            _resolutionModes.Add((3840, 2160, "3840 x 2160"));
        }

        private void LoadSavedSettings()
        {
            _isRefreshing = true;

            if (frameRateDropdown != null)
                frameRateDropdown.SetValueWithoutNotify(Mathf.Clamp(PlayerPrefs.GetInt(FrameRatePrefKey, 1), 0, _frameRateModes.Count - 1));

            if (windowModeDropdown != null)
                windowModeDropdown.SetValueWithoutNotify(Mathf.Clamp(PlayerPrefs.GetInt(WindowModePrefKey, 1), 0, _windowModes.Count - 1));

            if (resolutionDropdown != null)
                resolutionDropdown.SetValueWithoutNotify(Mathf.Clamp(PlayerPrefs.GetInt(ResolutionPrefKey, 2), 0, _resolutionModes.Count - 1));

            if (graphicsDropdown != null)
                graphicsDropdown.SetValueWithoutNotify(Mathf.Clamp(PlayerPrefs.GetInt(GraphicsPrefKey, QualitySettings.GetQualityLevel()), 0, Mathf.Max(0, QualitySettings.names.Length - 1)));

            int controlTypeIndex = Mathf.Clamp(PlayerPrefs.GetInt(ControlTypePrefKey, 0), 0, _controlTypes.Count - 1);
            _selectedControlGroup = _controlTypes[controlTypeIndex].value;

            if (controlTypeDropdown != null)
                controlTypeDropdown.SetValueWithoutNotify(controlTypeIndex);

            _isRefreshing = false;
            ApplyAllSettings();
        }

        public void ApplyAllSettings()
        {
            ApplyFrameRate();
            ApplyWindowMode();
            ApplyResolution();
            ApplyGraphicsQuality();
            PlayerPrefs.Save();
        }

        private void ApplyFrameRate()
        {
            if (_isRefreshing || frameRateDropdown == null || _frameRateModes.Count == 0)
                return;

            int index = Mathf.Clamp(frameRateDropdown.value, 0, _frameRateModes.Count - 1);
            PlayerPrefs.SetInt(FrameRatePrefKey, index);

            FrameRateMode mode = _frameRateModes[index].value;
            QualitySettings.vSyncCount = mode == FrameRateMode.VSync ? 1 : 0;

            Application.targetFrameRate = mode switch
            {
                FrameRateMode.FPS30 => 30,
                FrameRateMode.FPS60 => 60,
                FrameRateMode.FPS75 => 75,
                FrameRateMode.FPS90 => 90,
                FrameRateMode.FPS120 => 120,
                FrameRateMode.FPS144 => 144,
                FrameRateMode.FPS165 => 165,
                FrameRateMode.FPS240 => 240,
                FrameRateMode.Unlimited => -1,
                FrameRateMode.VSync => -1,
                _ => 60
            };
        }

        private void ApplyWindowMode()
        {
            if (_isRefreshing || windowModeDropdown == null || _windowModes.Count == 0)
                return;

            int index = Mathf.Clamp(windowModeDropdown.value, 0, _windowModes.Count - 1);
            PlayerPrefs.SetInt(WindowModePrefKey, index);

            if (resolutionDropdown != null && _resolutionModes.Count > 0)
            {
                int resolutionIndex = Mathf.Clamp(resolutionDropdown.value, 0, _resolutionModes.Count - 1);
                var resolution = _resolutionModes[resolutionIndex];
                Screen.SetResolution(resolution.width, resolution.height, GetFullScreenMode());
            }
        }

        private void ApplyResolution()
        {
            if (_isRefreshing || resolutionDropdown == null || _resolutionModes.Count == 0)
                return;

            int index = Mathf.Clamp(resolutionDropdown.value, 0, _resolutionModes.Count - 1);
            PlayerPrefs.SetInt(ResolutionPrefKey, index);

            var resolution = _resolutionModes[index];
            Screen.SetResolution(resolution.width, resolution.height, GetFullScreenMode());
        }

        private void ApplyGraphicsQuality()
        {
            if (_isRefreshing || graphicsDropdown == null || QualitySettings.names.Length == 0)
                return;

            int index = Mathf.Clamp(graphicsDropdown.value, 0, QualitySettings.names.Length - 1);
            PlayerPrefs.SetInt(GraphicsPrefKey, index);
            QualitySettings.SetQualityLevel(index, true);
        }

        private FullScreenMode GetFullScreenMode()
        {
            if (windowModeDropdown == null || _windowModes.Count == 0)
                return FullScreenMode.FullScreenWindow;

            int index = Mathf.Clamp(windowModeDropdown.value, 0, _windowModes.Count - 1);
            return _windowModes[index].value switch
            {
                WindowMode.Windowed => FullScreenMode.Windowed,
                WindowMode.BorderlessFullscreen => FullScreenMode.FullScreenWindow,
                WindowMode.ExclusiveFullscreen => FullScreenMode.ExclusiveFullScreen,
                _ => FullScreenMode.FullScreenWindow
            };
        }

        public void ResetDefaults()
        {
            PlayerPrefs.DeleteKey(FrameRatePrefKey);
            PlayerPrefs.DeleteKey(WindowModePrefKey);
            PlayerPrefs.DeleteKey(ResolutionPrefKey);
            PlayerPrefs.DeleteKey(GraphicsPrefKey);
            PlayerPrefs.DeleteKey(ControlTypePrefKey);
            LoadSavedSettings();
        }

        public void RefreshControls()
        {
            _isRefreshing = true;

            _selectedPlayerIndex = Mathf.Clamp(_selectedPlayerIndex, 0, 3);
            _selectedDevice = GetSavedDeviceKind(_selectedPlayerIndex);
            ReloadWorkingActionsForSelectedPlayer();

            RefreshControlTypeDropdown();
            RefreshPlayerDropdown();
            RefreshDeviceDropdown();
            RefreshGamepadDropdown();
            RebuildRows();

            _isRefreshing = false;
        }

        private void ReloadWorkingActionsForSelectedPlayer()
        {
            if (_workingActions != null)
                Destroy(_workingActions);

            _workingActions = controlsActions != null ? Instantiate(controlsActions) : null;

            if (_workingActions == null)
                return;

            string key = GetBindingPrefsKey(_selectedPlayerIndex);
            if (!PlayerPrefs.HasKey(key))
                return;

            string json = PlayerPrefs.GetString(key);
            if (string.IsNullOrWhiteSpace(json))
                return;

            try
            {
                _workingActions.LoadBindingOverridesFromJson(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load binding overrides for Player {_selectedPlayerIndex + 1}: {ex.Message}");
            }
        }

        private void RefreshControlTypeDropdown()
        {
            if (controlTypeDropdown == null)
                return;

            int index = _controlTypes.FindIndex(x => x.value == _selectedControlGroup);
            if (index < 0)
                index = 0;

            controlTypeDropdown.SetValueWithoutNotify(index);
            controlTypeDropdown.RefreshShownValue();
        }

        private void RefreshPlayerDropdown()
        {
            if (playerDropdown == null)
                return;

            playerDropdown.ClearOptions();
            playerDropdown.AddOptions(new List<string> { "Player 1", "Player 2", "Player 3", "Player 4" });
            playerDropdown.SetValueWithoutNotify(_selectedPlayerIndex);
            playerDropdown.RefreshShownValue();
        }

        private void RefreshDeviceDropdown()
        {
            if (deviceDropdown == null)
                return;

            deviceDropdown.ClearOptions();
            deviceDropdown.AddOptions(new List<string> { "Keyboard", "Gamepad" });
            deviceDropdown.SetValueWithoutNotify((int)_selectedDevice);
            deviceDropdown.RefreshShownValue();
        }

        private void RefreshGamepadDropdown()
        {
            if (gamepadDropdown == null)
                return;

            bool show = _selectedDevice == DeviceKind.Gamepad;
            gamepadDropdown.gameObject.SetActive(show);

            if (!show)
                return;

            gamepadDropdown.ClearOptions();
            List<string> options = GetAvailableGamepadLabels();
            gamepadDropdown.AddOptions(options);

            int selectedIndex = GetSavedGamepadIndex(_selectedPlayerIndex);
            selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, options.Count - 1));
            gamepadDropdown.SetValueWithoutNotify(selectedIndex);
            gamepadDropdown.RefreshShownValue();
            gamepadDropdown.interactable = Gamepad.all.Count > 0;
        }

        private void RebuildRows()
        {
            if (rowsRoot == null || rowPrefab == null)
                return;

            foreach (var t in _spawnedRows)
            {
                if (t != null)
                    Destroy(t.gameObject);
            }

            _spawnedRows.Clear();

            foreach (CommandRow command in Commands)
            {
                if (command.Group != _selectedControlGroup)
                    continue;

                CommandRow capturedCommand = command;
                ControlBindingRow row = Instantiate(rowPrefab, rowsRoot);
                row.gameObject.SetActive(true);

                row.Configure(
                    capturedCommand.ActionName,
                    capturedCommand.Label,
                    GetOptionLabels(capturedCommand),
                    GetSelectedOptionIndex(capturedCommand),
                    selected => OnBindingChanged(capturedCommand, selected),
                    () => ResetCommandToDefault(capturedCommand)
                );

                _spawnedRows.Add(row);
            }
        }

        private List<string> GetOptionLabels(CommandRow command)
        {
            if (command.Kind == BindingKind.Button)
                return GetButtonOptions().Select(x => x.Label).ToList();

            return GetVectorPresets(command).Select(x => x.Label).ToList();
        }

        private BindingOption[] GetButtonOptions()
        {
            return _selectedDevice == DeviceKind.Keyboard ? KeyboardButtonOptions : GamepadButtonOptions;
        }

        private VectorPreset[] GetVectorPresets(CommandRow command)
        {
            if (_selectedDevice == DeviceKind.Gamepad)
                return command.Kind == BindingKind.Drive ? GamepadDrivePresets : GamepadRotatePresets;

            return command.Kind == BindingKind.Drive ? KeyboardDrivePresets : KeyboardRotatePresets;
        }

        private void OnControlTypeChanged(int index)
        {
            if (_isRefreshing)
                return;

            index = Mathf.Clamp(index, 0, _controlTypes.Count - 1);
            _selectedControlGroup = _controlTypes[index].value;
            PlayerPrefs.SetInt(ControlTypePrefKey, index);
            PlayerPrefs.Save();
            RebuildRows();
        }

        private void OnPlayerChanged(int index)
        {
            if (_isRefreshing)
                return;

            _selectedPlayerIndex = Mathf.Clamp(index, 0, 3);
            _selectedDevice = GetSavedDeviceKind(_selectedPlayerIndex);
            ReloadWorkingActionsForSelectedPlayer();

            if (deviceDropdown != null)
            {
                deviceDropdown.SetValueWithoutNotify((int)_selectedDevice);
                deviceDropdown.RefreshShownValue();
            }

            RefreshGamepadDropdown();
            RebuildRows();
        }

        private void OnDeviceChanged(int index)
        {
            if (_isRefreshing)
                return;

            _selectedDevice = (DeviceKind)Mathf.Clamp(index, 0, 1);
            SaveDeviceKind(_selectedPlayerIndex, _selectedDevice);
            RefreshGamepadDropdown();
            RebuildRows();
        }

        private void OnGamepadChanged(int index)
        {
            if (_isRefreshing || _selectedDevice != DeviceKind.Gamepad)
                return;

            SaveGamepadIndex(_selectedPlayerIndex, index);
            RebuildRows();
        }

        private void OnBindingChanged(CommandRow command, int selectedIndex)
        {
            if (_workingActions == null)
                return;

            InputAction action = FindAction(_workingActions, command.ActionName);
            if (action == null)
                return;

            string group = GetSelectedControlScheme();

            if (command.Kind == BindingKind.Button)
            {
                BindingOption[] options = GetButtonOptions();
                selectedIndex = Mathf.Clamp(selectedIndex, 0, options.Length - 1);
                ApplyButtonOverride(action, group, options[selectedIndex].Path);
            }
            else
            {
                VectorPreset[] presets = GetVectorPresets(command);
                selectedIndex = Mathf.Clamp(selectedIndex, 0, presets.Length - 1);
                ApplyVectorOverride(action, group, presets[selectedIndex]);
            }

            SaveSelectedPlayerOverrides();
            RebuildRows();
            ReselectRowControl(command.ActionName, preferDropdown: true);
        }

        private void ResetCommandToDefault(CommandRow command)
        {
            if (_workingActions == null)
                return;

            InputAction action = FindAction(_workingActions, command.ActionName);
            if (action == null)
                return;

            RemoveOverridesForActionGroup(action, GetSelectedControlScheme());
            SaveSelectedPlayerOverrides();
            RebuildRows();
            ReselectRowControl(command.ActionName, preferDropdown: false);
        }
        
        private void ReselectRowControl(string actionName, bool preferDropdown)
        {
            if (EventSystem.current == null)
                return;

            ControlBindingRow row = _spawnedRows.Find(r => r != null && r.ActionName == actionName);
            if (row == null)
                return;

            GameObject target = preferDropdown ? row.DropdownGameObject : row.DefaultButtonGameObject;
            if (target == null)
                target = preferDropdown ? row.DefaultButtonGameObject : row.DropdownGameObject;

            if (target == null)
                return;

            StartCoroutine(SelectNextFrame(target));
        }

        private System.Collections.IEnumerator SelectNextFrame(GameObject target)
        {
            if (EventSystem.current == null)
                yield break;

            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            EventSystem.current.SetSelectedGameObject(target);
        }

        private void ResetSelectedDeviceForPlayer()
        {
            if (_workingActions == null)
                return;

            InputActionMap map = _workingActions.FindActionMap(actionMapName);
            if (map == null)
                return;

            string group = GetSelectedControlScheme();

            foreach (InputAction action in map.actions)
                RemoveOverridesForActionGroup(action, group);

            SaveSelectedPlayerOverrides();
            RebuildRows();
        }

        private InputAction FindAction(InputActionAsset actions, string actionName)
        {
            if (actions == null)
                return null;

            InputActionMap map = actions.FindActionMap(actionMapName);
            return map?.FindAction(actionName);
        }

        private string GetSelectedControlScheme()
        {
            return _selectedDevice == DeviceKind.Keyboard ? keyboardControlScheme : gamepadControlScheme;
        }

        private void SaveSelectedPlayerOverrides()
        {
            if (_workingActions == null)
                return;

            PlayerPrefs.SetString(GetBindingPrefsKey(_selectedPlayerIndex), _workingActions.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        private string GetBindingPrefsKey(int playerIndex)
        {
            return $"{BindingPrefsPrefix}{Mathf.Clamp(playerIndex, 0, 3)}";
        }

        private string GetDevicePrefsKey(int playerIndex)
        {
            return $"{DevicePrefsPrefix}{Mathf.Clamp(playerIndex, 0, 3)}";
        }

        private string GetGamepadPrefsKey(int playerIndex)
        {
            return $"{GamepadPrefsPrefix}{Mathf.Clamp(playerIndex, 0, 3)}";
        }

        private DeviceKind GetSavedDeviceKind(int playerIndex)
        {
            int value = PlayerPrefs.GetInt(GetDevicePrefsKey(playerIndex), 1);
            value = Mathf.Clamp(value, 1, 2);
            return value == 2 ? DeviceKind.Gamepad : DeviceKind.Keyboard;
        }

        private void SaveDeviceKind(int playerIndex, DeviceKind deviceKind)
        {
            int value = deviceKind == DeviceKind.Gamepad ? 2 : 1;
            PlayerPrefs.SetInt(GetDevicePrefsKey(playerIndex), value);
            PlayerPrefs.Save();
        }

        private int GetSavedGamepadIndex(int playerIndex)
        {
            if (!PlayerPrefs.HasKey(GetGamepadPrefsKey(playerIndex)))
                return Mathf.Clamp(playerIndex, 0, Mathf.Max(0, Gamepad.all.Count - 1));

            int savedIndex = PlayerPrefs.GetInt(GetGamepadPrefsKey(playerIndex), 0);
            return Mathf.Clamp(savedIndex, 0, Mathf.Max(0, Gamepad.all.Count - 1));
        }

        private void SaveGamepadIndex(int playerIndex, int gamepadIndex)
        {
            int maxIndex = Mathf.Max(0, Gamepad.all.Count - 1);
            PlayerPrefs.SetInt(GetGamepadPrefsKey(playerIndex), Mathf.Clamp(gamepadIndex, 0, maxIndex));
            PlayerPrefs.Save();
        }

        private List<string> GetAvailableGamepadLabels()
        {
            List<string> labels = new();

            for (int i = 0; i < Gamepad.all.Count; i++)
                labels.Add($"Gamepad {i + 1}");

            if (labels.Count == 0)
                labels.Add("No gamepads connected");

            return labels;
        }

        private static void ApplyButtonOverride(InputAction action, string group, string path)
        {
            if (path == null)
            {
                RemoveOverridesForActionGroup(action, group);
                return;
            }

            int bindingIndex = FindFirstNonCompositeBindingIndex(action, group);
            if (bindingIndex < 0)
            {
                action.AddBinding(path).WithGroup(group);
                return;
            }

            action.ApplyBindingOverride(bindingIndex, path);
        }

        private static void ApplyVectorOverride(InputAction action, string group, VectorPreset preset)
        {
            if (preset == null || preset.Label == "Default")
            {
                RemoveOverridesForActionGroup(action, group);
                return;
            }

            if (!string.IsNullOrWhiteSpace(preset.SimplePath))
            {
                RemoveCompositeOverridesForGroup(action, group);

                int bindingIndex = FindFirstNonCompositeBindingIndex(action, group);
                if (bindingIndex >= 0)
                    action.ApplyBindingOverride(bindingIndex, preset.SimplePath);
                else
                    action.AddBinding(preset.SimplePath).WithGroup(group);

                return;
            }

            RemoveNonCompositeOverridesForGroup(action, group);
            ApplyCompositePartOverride(action, group, "Up", preset.Up);
            ApplyCompositePartOverride(action, group, "Down", preset.Down);
            ApplyCompositePartOverride(action, group, "Left", preset.Left);
            ApplyCompositePartOverride(action, group, "Right", preset.Right);
        }

        private static void ApplyCompositePartOverride(InputAction action, string group, string partName, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (!binding.isPartOfComposite || !BindingMatchesGroup(binding, group))
                    continue;

                if (!string.Equals(binding.name, partName, StringComparison.OrdinalIgnoreCase))
                    continue;

                action.ApplyBindingOverride(i, path);
                return;
            }
        }

        private int GetSelectedOptionIndex(CommandRow command)
        {
            if (_workingActions == null)
                return 0;

            InputAction action = FindAction(_workingActions, command.ActionName);
            if (action == null)
                return 0;

            string group = GetSelectedControlScheme();

            if (command.Kind == BindingKind.Button)
            {
                string path = GetEffectiveButtonPath(action, group);
                BindingOption[] options = GetButtonOptions();

                for (int i = 0; i < options.Length; i++)
                {
                    if (string.Equals(options[i].Path, path, StringComparison.OrdinalIgnoreCase))
                        return i;
                }

                return 0;
            }

            VectorPreset[] presets = GetVectorPresets(command);
            for (int i = 1; i < presets.Length; i++)
            {
                if (VectorPresetMatches(action, group, presets[i]))
                    return i;
            }

            return 0;
        }

        private static string GetEffectiveButtonPath(InputAction action, string group)
        {
            int index = FindFirstNonCompositeBindingIndex(action, group);
            if (index < 0)
                return null;

            InputBinding binding = action.bindings[index];
            return string.IsNullOrWhiteSpace(binding.overridePath) ? binding.path : binding.overridePath;
        }

        private static bool VectorPresetMatches(InputAction action, string group, VectorPreset preset)
        {
            if (!string.IsNullOrWhiteSpace(preset.SimplePath))
                return string.Equals(GetEffectiveButtonPath(action, group), preset.SimplePath, StringComparison.OrdinalIgnoreCase);

            return PartMatches(action, group, "Up", preset.Up) &&
                   PartMatches(action, group, "Down", preset.Down) &&
                   PartMatches(action, group, "Left", preset.Left) &&
                   PartMatches(action, group, "Right", preset.Right);
        }

        private static bool PartMatches(InputAction action, string group, string partName, string expectedPath)
        {
            if (string.IsNullOrWhiteSpace(expectedPath))
                return true;

            foreach (var binding in action.bindings)
            {
                if (!binding.isPartOfComposite || !BindingMatchesGroup(binding, group))
                    continue;

                if (!string.Equals(binding.name, partName, StringComparison.OrdinalIgnoreCase))
                    continue;

                string path = string.IsNullOrWhiteSpace(binding.overridePath) ? binding.path : binding.overridePath;
                return string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static void RemoveOverridesForActionGroup(InputAction action, string group)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (BindingMatchesGroup(action.bindings[i], group))
                    action.RemoveBindingOverride(i);
            }
        }

        private static void RemoveCompositeOverridesForGroup(InputAction action, string group)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if ((binding.isComposite || binding.isPartOfComposite) && BindingMatchesGroup(binding, group))
                    action.RemoveBindingOverride(i);
            }
        }

        private static void RemoveNonCompositeOverridesForGroup(InputAction action, string group)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding is { isComposite: false, isPartOfComposite: false } && BindingMatchesGroup(binding, group))
                    action.RemoveBindingOverride(i);
            }
        }

        private static int FindFirstNonCompositeBindingIndex(InputAction action, string group)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite || binding.isPartOfComposite)
                    continue;

                if (BindingMatchesGroup(binding, group))
                    return i;
            }

            return -1;
        }

        private static bool BindingMatchesGroup(InputBinding binding, string group)
        {
            if (string.IsNullOrWhiteSpace(group))
                return true;

            if (string.IsNullOrWhiteSpace(binding.groups))
                return false;

            return binding.groups
                .Split(';')
                .Any(x => string.Equals(x.Trim(), group, StringComparison.OrdinalIgnoreCase));
        }
    }
}