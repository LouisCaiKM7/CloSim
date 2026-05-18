using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using Util;

[Serializable]
public class TeamSpawnLocation
{
    public string name;
    public Transform point;
}

[Serializable]
public class MatchSettings
{
    public int robotIndex1;
    public int robotIndex2;

    public int blueSpawnIndex1;
    public int blueSpawnIndex2;

    public int redSpawnIndex1;
    public int redSpawnIndex2;

    public Cameras view = Cameras.ThirdPerson;
    public Util.PlayMode playMode = Util.PlayMode.OneVsZero;
    public bool useBlueAlliance = true;

    public TrackingType trackingType = TrackingType.TrackRobot;

    public MatchSettings Clone()
    {
        return new MatchSettings
        {
            robotIndex1 = robotIndex1,
            robotIndex2 = robotIndex2,
            blueSpawnIndex1 = blueSpawnIndex1,
            blueSpawnIndex2 = blueSpawnIndex2,
            redSpawnIndex1 = redSpawnIndex1,
            redSpawnIndex2 = redSpawnIndex2,
            view = view,
            playMode = playMode,
            useBlueAlliance = useBlueAlliance,
            trackingType = trackingType
        };
    }
}

public class LoadMatch : MonoBehaviour
{
    [Header("Field")]
    [SerializeField] private GameObject[] fieldPrefab;

    [Header("Spawn Points")]
    [SerializeField] private List<TeamSpawnLocation> blueSideSpawns = new();
    [SerializeField] private List<TeamSpawnLocation> redSideSpawns = new();

    [Header("Default Robot Selection")]
    [SerializeField] private InspectorDropdown robotSelected1;
    [SerializeField] private InspectorDropdown robotSelected2;

    [Header("Default Match Settings")]
    [SerializeField] private Cameras defaultView = Cameras.ThirdPerson;
    [SerializeField] private Util.PlayMode defaultPlayMode = Util.PlayMode.OneVsZero;
    [SerializeField] private bool defaultUseBlueAlliance = true;
    [SerializeField] private TrackingType defaultTrackingType = TrackingType.TrackRobot;

    [Header("Driver Station Cameras")]
    [SerializeField] private StationNum player1DriverStation = 0;
    
    [SerializeField] private StationNum player2DriverStation = (StationNum) 2;

    [Header("Input")]
    [SerializeField] private string robotActionMap = "Robot";
    [SerializeField] private string gamepadControlScheme = "Gamepad";
    [SerializeField] private string keyboardControlScheme = "Keyboard";
    [SerializeField] private InputActionAsset builderActions;

    private int _selectedRobotIndex1;
    private int _selectedRobotIndex2;
    private string _selectedName1;
    private string _selectedName2;

    private readonly List<GameObject> _availableRobots = new List<GameObject>();

    private GameObject _fieldHolder;
    private GameObject _activeRobot1;
    private GameObject _activeRobot2;
    private GameObject _activeCam;
    private GameObject _spawnedCamera1;
    private GameObject _spawnedCamera2;

    private FMS _fms;

    private bool _isResettingField;
    private int _setupVersion;
    private int _pairedVersion = -1;
    private Coroutine _inputSetupCoroutine;

    private MatchSettings _settings = new MatchSettings();

    private enum CameraSide
    {
        Full,
        Left,
        Right
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;

        CheckRobots();
        RefreshInspectorDropdownData();
    }

    private void LateUpdate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;

        CheckRobots();
        RefreshInspectorDropdownData();
        SyncInspectorDropdownSelection();
#endif
    }

    private void Start()
    {
        if (!Application.isPlaying)
            return;

        _selectedName1 = robotSelected1 != null ? robotSelected1.selectedName : string.Empty;
        _selectedName2 = robotSelected2 != null ? robotSelected2.selectedName : string.Empty;

        _selectedRobotIndex1 = 3;
        _selectedRobotIndex2 = 3;

        CheckRobots();

        _settings = new MatchSettings
        {
            robotIndex1 = _selectedRobotIndex1,
            robotIndex2 = _selectedRobotIndex2,
            blueSpawnIndex1 = 0,
            blueSpawnIndex2 = Mathf.Min(1, Mathf.Max(0, blueSideSpawns.Count - 1)),
            redSpawnIndex1 = 0,
            redSpawnIndex2 = Mathf.Min(1, Mathf.Max(0, redSideSpawns.Count - 1)),
            view = defaultView,
            playMode = defaultPlayMode,
            useBlueAlliance = defaultUseBlueAlliance,
            trackingType = defaultTrackingType
        };

        SanitizeSettings();
        SanitizeSpawnSettings();
        SyncSelectionNamesFromSettings();

        ResetField();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        if (robotSelected1 != null)
        {
            _selectedName1 = robotSelected1.selectedName;
            _selectedRobotIndex1 = robotSelected1.selectedIndex;
        }

        if (robotSelected2 != null)
        {
            _selectedName2 = robotSelected2.selectedName;
            _selectedRobotIndex2 = robotSelected2.selectedIndex;
        }
    }

    private void RefreshInspectorDropdownData()
    {
        var robotNames = _availableRobots.Select(x => x.name).ToList();

        if (robotSelected1 != null)
            robotSelected1.canBeSelected = robotNames;

        if (robotSelected2 != null)
            robotSelected2.canBeSelected = robotNames;
    }

    private void SyncInspectorDropdownSelection()
    {
        if (robotSelected1 != null)
        {
            robotSelected1.selectedIndex = _settings.robotIndex1;
            robotSelected1.selectedName = _selectedName1;
        }

        if (robotSelected2 != null)
        {
            robotSelected2.selectedIndex = _settings.robotIndex2;
            robotSelected2.selectedName = _selectedName2;
        }
    }

    private void SyncSelectionNamesFromSettings()
    {
        _selectedRobotIndex1 = _settings.robotIndex1;
        _selectedRobotIndex2 = _settings.robotIndex2;

        _selectedName1 = _availableRobots.Count > _selectedRobotIndex1
            ? _availableRobots[_selectedRobotIndex1].name
            : string.Empty;

        _selectedName2 = _availableRobots.Count > _selectedRobotIndex2
            ? _availableRobots[_selectedRobotIndex2].name
            : string.Empty;
    }

    private void SanitizeSettings()
    {
        if (_availableRobots.Count == 0)
        {
            _settings.robotIndex1 = 0;
            _settings.robotIndex2 = 0;
            return;
        }

        _settings.robotIndex1 = Mathf.Clamp(_settings.robotIndex1, 0, _availableRobots.Count - 1);
        _settings.robotIndex2 = Mathf.Clamp(_settings.robotIndex2, 0, _availableRobots.Count - 1);

        if (IsRedAllianceDriverStationForbidden())
        {
            Debug.LogWarning("1v0 and 2v0 cannot use Red alliance with Driver Station camera. Forcing Blue alliance.");
            _settings.useBlueAlliance = true;
        }
    }

    private void SanitizeSpawnSettings()
    {
        _settings.blueSpawnIndex1 = ClampSpawnIndex(_settings.blueSpawnIndex1, blueSideSpawns.Count);
        _settings.blueSpawnIndex2 = ClampSpawnIndex(_settings.blueSpawnIndex2, blueSideSpawns.Count);
        _settings.redSpawnIndex1 = ClampSpawnIndex(_settings.redSpawnIndex1, redSideSpawns.Count);
        _settings.redSpawnIndex2 = ClampSpawnIndex(_settings.redSpawnIndex2, redSideSpawns.Count);

        EnforceUniqueSpawnSelectionForSide(ref _settings.blueSpawnIndex1, ref _settings.blueSpawnIndex2, blueSideSpawns.Count);
        EnforceUniqueSpawnSelectionForSide(ref _settings.redSpawnIndex1, ref _settings.redSpawnIndex2, redSideSpawns.Count);
    }
    
    private void EnforceUniqueSpawnSelectionForSide(ref int firstIndex, ref int secondIndex, int count)
    {
        if (count <= 1)
            return;

        if (firstIndex != secondIndex)
            return;

        for (int i = 0; i < count; i++)
        {
            if (i != firstIndex)
            {
                secondIndex = i;
                return;
            }
        }
    }

    private int ClampSpawnIndex(int value, int count)
    {
        if (count <= 0) return 0;
        return Mathf.Clamp(value, 0, count - 1);
    }

    public MatchSettings GetSettingsCopy()
    {
        return _settings.Clone();
    }

    public void ApplySettings(MatchSettings newSettings)
    {
        if (newSettings == null)
            return;

        _settings = newSettings.Clone();
        CheckRobots();
        SanitizeSettings();
        SanitizeSpawnSettings();
        SyncSelectionNamesFromSettings();
        SyncInspectorDropdownSelection();
    }

    public List<string> GetAvailableRobotNames()
    {
        CheckRobots();
        return _availableRobots.Select(r => r.name).ToList();
    }

    public int GetAvailableRobotCount()
    {
        CheckRobots();
        return _availableRobots.Count;
    }

    public string GetRobotNameAt(int index)
    {
        CheckRobots();

        if (_availableRobots.Count == 0)
            return "No Robots";

        index = Mathf.Clamp(index, 0, _availableRobots.Count - 1);
        return _availableRobots[index].name;
    }

    public Sprite GetRobotPreviewSpriteAt(int index)
    {
        CheckRobots();

        if (_availableRobots.Count == 0)
            return null;

        index = Mathf.Clamp(index, 0, _availableRobots.Count - 1);

        string robotName = _availableRobots[index].name;
        Sprite sprite = Resources.Load<Sprite>($"RobotPreviews/{robotName}");

        if (sprite == null)
        {
            Debug.LogWarning($"No robot preview sprite found at Resources/RobotPreviews/{robotName}");
        }

        return sprite;
    }

    public List<string> GetBlueSpawnNames()
    {
        return blueSideSpawns
            .Select(s => string.IsNullOrWhiteSpace(s.name) ? "(Unnamed Blue Spawn)" : s.name)
            .ToList();
    }

    public List<string> GetRedSpawnNames()
    {
        return redSideSpawns
            .Select(s => string.IsNullOrWhiteSpace(s.name) ? "(Unnamed Red Spawn)" : s.name)
            .ToList();
    }

    private Transform GetBlueSpawnPoint(int slot)
    {
        if (blueSideSpawns.Count == 0)
            return null;

        SanitizeSpawnSettings();

        int index = slot == 0 ? _settings.blueSpawnIndex1 : _settings.blueSpawnIndex2;
        return blueSideSpawns[index].point;
    }

    private Transform GetRedSpawnPoint(int slot)
    {
        if (redSideSpawns.Count == 0)
            return null;

        SanitizeSpawnSettings();

        int index = slot == 0 ? _settings.redSpawnIndex1 : _settings.redSpawnIndex2;
        return redSideSpawns[index].point;
    }

    private StationNum GetStationNumberForRobot(int robotSlot)
    {
        return robotSlot switch
        {
            0 => player1DriverStation,
            1 => player2DriverStation,
            _ => player1DriverStation
        };
    }

    private void LoadField()
    {
        _fieldHolder = new GameObject
        {
            name = "FieldHolder",
            transform = { position = Vector3.zero, rotation = Quaternion.identity, parent = transform }
        };

        if (fieldPrefab is { Length: > 0 } && fieldPrefab[0] != null)
        {
            Instantiate(fieldPrefab[0], Vector3.zero, Quaternion.identity, _fieldHolder.transform);
        }
    }

    private void DestroyField()
    {
        if (transform.Find("FieldHolder"))
        {
            _fieldHolder = transform.Find("FieldHolder").GameObject();
            Destroy(_fieldHolder);
        }
    }

    public TrackingType GetTrackingType()
    {
        return _settings.trackingType;
    }

    public Cameras GetViewType()
    {
        return _settings.view;
    }

    public Util.PlayMode GetPlayMode()
    {
        return _settings.playMode;
    }

    public bool UsesBlueAlliance()
    {
        return _settings.useBlueAlliance;
    }

    public void ResetField()
    {
        if (_isResettingField)
            return;

        _isResettingField = true;
        _setupVersion++;
        _pairedVersion = -1;

        if (_inputSetupCoroutine != null)
        {
            StopCoroutine(_inputSetupCoroutine);
            _inputSetupCoroutine = null;
        }

        CheckRobots();
        SanitizeSettings();
        SanitizeSpawnSettings();
        SyncSelectionNamesFromSettings();

        DestroySpawnedCameraOnly();
        DeleteRobots();
        DestroyField();
        LoadField();
        SpawnRobots();
        AddSplitScreenCameras();
        Utils.resetParentCache();

        _inputSetupCoroutine = StartCoroutine(SetupInputsWhenReady(_setupVersion));

        if (_fms)
        {
            _fms.Restart();
        }

        StartCoroutine(ClearResetLockNextFrame());
    }

    private IEnumerator ClearResetLockNextFrame()
    {
        yield return null;
        _isResettingField = false;
    }

    private IEnumerator SetupInputsWhenReady(int version)
    {
        float timeout = 2f;
        float startTime = Time.time;

        while (Time.time - startTime < timeout)
        {
            if (version != _setupVersion)
                yield break;

            if (_activeRobot1 != null)
                EnsurePlayerInputConfigured(_activeRobot1);

            if (_activeRobot2 != null)
                EnsurePlayerInputConfigured(_activeRobot2);

            bool p1Ready = _activeRobot1 == null || HasReadyPlayerInput(_activeRobot1);
            bool p2Ready = _activeRobot2 == null || HasReadyPlayerInput(_activeRobot2);

            if (p1Ready && p2Ready)
                break;

            yield return null;
        }

        if (version != _setupVersion)
            yield break;

        if (_pairedVersion == version)
            yield break;

        PairInputs();
        _pairedVersion = version;
        _inputSetupCoroutine = null;
    }

    public void SetFms(FMS fmsInstance)
    {
        _fms = fmsInstance;
    }

    public GameObject GetFieldHolder()
    {
        return _fieldHolder;
    }

    private void SpawnRobots()
    {
        _activeRobot1 = null;
        _activeRobot2 = null;

        if (_availableRobots.Count == 0)
        {
            Debug.LogWarning("No robots found in Resources/Robots.");
            return;
        }

        if (_fieldHolder == null)
        {
            Debug.LogError("FieldHolder has not been created.");
            return;
        }

        Transform p1Spawn = GetSpawnPointForRobot(0);
        if (p1Spawn == null)
        {
            Debug.LogError("Player 1 spawn point is not assigned.");
            return;
        }

        GameObject robotPrefab1 = GetRobotPrefabBySelection(_settings.robotIndex1);
        if (robotPrefab1 == null)
        {
            Debug.LogError("Selected robot 1 prefab is invalid.");
            return;
        }

        _activeRobot1 = Instantiate(robotPrefab1, p1Spawn.position, p1Spawn.rotation, _fieldHolder.transform);
        _activeRobot1.name = robotPrefab1.name + "_P1";
        EnsurePlayerInputConfigured(_activeRobot1);
        ConfigureRobotDriveMode(_activeRobot1, false);
        ConfigureOutpostReleaseOwnership(_activeRobot1, false);

        bool spawnSecondRobot =
            _settings.playMode == Util.PlayMode.TwoVsZero ||
            _settings.playMode == Util.PlayMode.OneVsOne;

        if (!spawnSecondRobot)
            return;

        Transform p2Spawn = GetSpawnPointForRobot(1);
        if (p2Spawn == null)
        {
            Debug.LogError($"Player 2 spawn point is not assigned for play mode {_settings.playMode}.");
            return;
        }

        GameObject robotPrefab2 = GetRobotPrefabBySelection(_settings.robotIndex2);
        if (robotPrefab2 == null)
        {
            Debug.LogError("Selected robot 2 prefab is invalid.");
            return;
        }

        _activeRobot2 = Instantiate(robotPrefab2, p2Spawn.position, p2Spawn.rotation, _fieldHolder.transform);
        _activeRobot2.name = robotPrefab2.name + "_P2";
        EnsurePlayerInputConfigured(_activeRobot2);
        ConfigureRobotDriveMode(_activeRobot2, true);
        ConfigureOutpostReleaseOwnership(_activeRobot2, true);
    }

    private Transform GetSpawnPointForRobot(int robotSlot)
    {
        return _settings.playMode switch
        {
            Util.PlayMode.OneVsZero => _settings.useBlueAlliance
                ? GetBlueSpawnPoint(0)
                : GetRedSpawnPoint(0),

            Util.PlayMode.TwoVsZero => _settings.useBlueAlliance
                ? GetBlueSpawnPoint(robotSlot)
                : GetRedSpawnPoint(robotSlot),

            Util.PlayMode.OneVsOne => robotSlot == 0
                ? GetBlueSpawnPoint(0)
                : GetRedSpawnPoint(0),

            _ => null
        };
    }
    
    public int GetHumanPlayerOwnerSlotForAlliance(bool blueAlliance)
    {
        switch (_settings.playMode)
        {
            case Util.PlayMode.OneVsZero:
                return 0;

            case Util.PlayMode.TwoVsZero:
                return 0;

            case Util.PlayMode.OneVsOne:
                return blueAlliance ? 0 : 1;

            default:
                return 0;
        }
    }

    private GameObject GetRobotPrefabBySelection(int selectedIndex)
    {
        if (_availableRobots.Count == 0)
            return null;

        selectedIndex = Mathf.Clamp(selectedIndex, 0, _availableRobots.Count - 1);
        return _availableRobots[selectedIndex];
    }

    private void PairInputs()
    {
        if (_pairedVersion == _setupVersion)
            return;

        var pads = Gamepad.all;

        switch (_settings.playMode)
        {
            case Util.PlayMode.OneVsZero:
                PairPlayerOneOnly(pads);
                if (_activeRobot2 != null)
                    DisableRobotInput(_activeRobot2);
                break;

            case Util.PlayMode.TwoVsZero:
            case Util.PlayMode.OneVsOne:
                PairTwoRobots(pads);
                break;
        }
    }

    private void PairPlayerOneOnly(ReadOnlyArray<Gamepad> pads)
    {
        if (_activeRobot1 == null)
            return;

        if (pads.Count >= 1)
        {
            BindRobotToGamepad(_activeRobot1, pads[0], gamepadControlScheme);
        }
        else if (Keyboard.current != null)
        {
            BindRobotToKeyboard(_activeRobot1, keyboardControlScheme);
        }
        else
        {
            DisableRobotInput(_activeRobot1);
            Debug.LogWarning("Player 1 has no valid device available.");
        }
    }

    private void PairTwoRobots(ReadOnlyArray<Gamepad> pads)
    {
        if (_activeRobot1 != null)
        {
            if (pads.Count >= 1)
            {
                BindRobotToGamepad(_activeRobot1, pads[0], gamepadControlScheme);
            }
            else if (Keyboard.current != null)
            {
                BindRobotToKeyboard(_activeRobot1, keyboardControlScheme);
            }
            else
            {
                DisableRobotInput(_activeRobot1);
                Debug.LogWarning("Player 1 has no valid device available.");
            }
        }

        if (_activeRobot2 != null)
        {
            if (pads.Count >= 2)
            {
                BindRobotToGamepad(_activeRobot2, pads[1], gamepadControlScheme);
            }
            else if (pads.Count >= 1 && Keyboard.current != null)
            {
                BindRobotToKeyboard(_activeRobot2, keyboardControlScheme);
            }
            else
            {
                DisableRobotInput(_activeRobot2);
                Debug.LogWarning("Player 2 has no valid device available.");
            }
        }
    }

    private void BindRobotToGamepad(GameObject robot, Gamepad gamepad, string controlScheme)
    {
        if (robot == null || gamepad == null)
            return;

        if (!EnsurePlayerInputConfigured(robot))
            return;

        var playerInput = robot.GetComponent<PlayerInput>();
        if (playerInput == null || playerInput.actions == null)
            return;

        try
        {
            bool alreadyCorrect =
                playerInput.currentControlScheme == controlScheme &&
                playerInput.user.valid &&
                playerInput.user.pairedDevices.Contains(gamepad);

            if (alreadyCorrect)
                return;

            playerInput.neverAutoSwitchControlSchemes = true;
            playerInput.defaultActionMap = robotActionMap;

            playerInput.actions.Disable();
            playerInput.actions.bindingMask = null;

            playerInput.SwitchCurrentControlScheme(controlScheme, gamepad);
            playerInput.SwitchCurrentActionMap(robotActionMap);
            playerInput.actions.bindingMask = InputBinding.MaskByGroup(controlScheme);
            playerInput.ActivateInput();
        }
        catch (Exception ex)
        {
            Debug.LogError($"{robot.name} failed to bind gamepad: {ex}");
        }
    }

    private void BindRobotToKeyboard(GameObject robot, string controlScheme)
    {
        if (robot == null || Keyboard.current == null)
            return;

        if (!EnsurePlayerInputConfigured(robot))
            return;

        var playerInput = robot.GetComponent<PlayerInput>();
        if (playerInput == null || playerInput.actions == null)
            return;

        try
        {
            bool alreadyCorrect =
                playerInput.currentControlScheme == controlScheme &&
                playerInput.user.valid &&
                playerInput.user.pairedDevices.Contains(Keyboard.current);

            if (alreadyCorrect)
                return;

            playerInput.neverAutoSwitchControlSchemes = true;
            playerInput.defaultActionMap = robotActionMap;

            playerInput.actions.Disable();
            playerInput.actions.bindingMask = null;

            playerInput.SwitchCurrentControlScheme(controlScheme, Keyboard.current);
            playerInput.SwitchCurrentActionMap(robotActionMap);
            playerInput.actions.bindingMask = InputBinding.MaskByGroup(controlScheme);
            playerInput.ActivateInput();
        }
        catch (Exception ex)
        {
            Debug.LogError($"{robot.name} failed to bind keyboard: {ex}");
        }
    }

    private void DisableRobotInput(GameObject robot)
    {
        if (robot == null)
            return;

        var playerInput = robot.GetComponent<PlayerInput>();
        if (playerInput == null)
            return;

        if (playerInput.actions != null)
        {
            playerInput.actions.Disable();
            playerInput.actions.bindingMask = new InputBinding { groups = "__disabled__" };
        }
    }
    
    private bool IsRobotOnRedAllianceSide(bool isPlayer2)
    {
        return _settings.playMode switch
        {
            Util.PlayMode.OneVsZero => !_settings.useBlueAlliance,
            Util.PlayMode.TwoVsZero => !_settings.useBlueAlliance,
            Util.PlayMode.OneVsOne => isPlayer2,
            _ => false
        };
    }
    
    private bool IsRedAllianceDriverStationForbidden()
    {
        return _settings.view == Cameras.DriverStation &&
               !_settings.useBlueAlliance &&
               (_settings.playMode == Util.PlayMode.OneVsZero ||
                _settings.playMode == Util.PlayMode.TwoVsZero);
    }

    private void ConfigureRobotDriveMode(GameObject robot, bool isPlayer2)
    {
        if (robot == null)
            return;

        var frame = robot.GetComponent<BuildFrame>();
        if (frame == null)
            return;

        var controller = frame.GetSwerveController();
        if (controller == null)
            return;

        bool robotIsRedSide = IsRobotOnRedAllianceSide(isPlayer2);

        controller.isRed = robotIsRedSide;
        controller.reversed = false;

        switch (_settings.view)
        {
            case Cameras.FirstPerson:
                controller.fieldCentric = false;
                controller.reversed = false;
                break;

            case Cameras.FirstPersonReversed:
                controller.fieldCentric = false;
                controller.reversed = true;
                break;

            case Cameras.ThirdPerson:
                controller.fieldCentric = true;
                controller.reversed = false;
                break;

            case Cameras.ReversedThirdPerson:
                controller.fieldCentric = true;
                controller.reversed = true;
                break;

            case Cameras.DriverStation:
                controller.fieldCentric = true;
                controller.reversed = false;
                break;
        }
    }
    
    private void ConfigureOutpostReleaseOwnership(GameObject robot, bool isPlayer2)
    {
        if (robot == null)
            return;

        StartCoroutine(ConfigureOutpostReleaseOwnershipWhenReady(robot, isPlayer2));
    }

    private IEnumerator ConfigureOutpostReleaseOwnershipWhenReady(GameObject robot, bool isPlayer2)
    {
        int playerSlot = isPlayer2 ? 1 : 0;

        const float timeout = 2f;
        float startTime = Time.time;

        while (robot != null && Time.time - startTime < timeout)
        {
            var outpostReleases = robot.GetComponentsInChildren<OutpostRelease>(true);

            if (outpostReleases.Length > 0)
            {
                foreach (var release in outpostReleases)
                {
                    release.ConfigureOwnership(playerSlot);
                }

                yield break;
            }

            yield return null;
        }
    }
    
    public bool RobotLoaded()
    {
        return _activeRobot1 != null || _activeRobot2 != null;
    }

    public GameObject GetRobotLoaded()
    {
        return _activeRobot1;
    }

    public GameObject GetRobotLoaded(int index)
    {
        return index switch
        {
            0 => _activeRobot1,
            1 => _activeRobot2,
            _ => null
        };
    }

    public GameObject[] GetLoadedRobots()
    {
        return new[] { _activeRobot1, _activeRobot2 };
    }

    private void DeleteRobots()
    {
        DestroySpawnedCameraOnly();

        if (_activeRobot1 != null)
        {
            Destroy(_activeRobot1);
            _activeRobot1 = null;
        }

        if (_activeRobot2 != null)
        {
            Destroy(_activeRobot2);
            _activeRobot2 = null;
        }
    }

    private void DestroySpawnedCameraOnly()
    {
        if (_spawnedCamera1 != null)
        {
            Destroy(_spawnedCamera1);
            _spawnedCamera1 = null;
        }

        if (_spawnedCamera2 != null)
        {
            Destroy(_spawnedCamera2);
            _spawnedCamera2 = null;
        }
    }

    public void CheckRobots()
    {
        GameObject[] loadedRobots = Resources.LoadAll<GameObject>("Robots");

        _availableRobots.Clear();
        foreach (var robot in loadedRobots)
        {
            _availableRobots.Add(robot);
        }

        if (_availableRobots.Count == 0)
        {
            _selectedRobotIndex1 = 0;
            _selectedRobotIndex2 = 0;
            _selectedName1 = string.Empty;
            _selectedName2 = string.Empty;
            return;
        }

        _selectedRobotIndex1 = Mathf.Clamp(_selectedRobotIndex1, 0, _availableRobots.Count - 1);
        _selectedRobotIndex2 = Mathf.Clamp(_selectedRobotIndex2, 0, _availableRobots.Count - 1);

        SanitizeSettings();
    }

    private bool HasReadyPlayerInput(GameObject robot)
    {
        if (robot == null)
            return false;

        var playerInput = robot.GetComponent<PlayerInput>();
        return playerInput != null && playerInput.actions != null;
    }

    private bool EnsurePlayerInputConfigured(GameObject robot)
    {
        if (robot == null)
            return false;

        var playerInput = robot.GetComponent<PlayerInput>();

        if (playerInput == null)
        {
            if (builderActions == null)
            {
                Debug.LogError($"{robot.name} is missing PlayerInput and LoadMatch.builderActions is null.");
                return false;
            }

            playerInput = robot.AddComponent<PlayerInput>();
        }

        playerInput.defaultControlScheme = string.Empty;
        playerInput.defaultActionMap = robotActionMap;
        playerInput.neverAutoSwitchControlSchemes = true;

        if (playerInput.actions == null)
        {
            if (builderActions == null)
            {
                Debug.LogError($"{robot.name} PlayerInput has no Actions asset assigned, and LoadMatch.builderActions is also null.");
                return false;
            }

            playerInput.actions = Instantiate(builderActions);
        }

        return playerInput.actions != null;
    }

    private void AddSplitScreenCameras()
    {
        if (_activeRobot1 == null)
            return;

        bool hasSecondRobot = _activeRobot2 != null;

        Transform p1Spawn = GetSpawnPointForRobot(0);
        _spawnedCamera1 = CreateCameraForRobot(_activeRobot1, p1Spawn, 0);
        ConfigureCameraViewport(_spawnedCamera1, hasSecondRobot ? CameraSide.Left : CameraSide.Full);

        if (hasSecondRobot)
        {
            Transform p2Spawn = GetSpawnPointForRobot(1);
            _spawnedCamera2 = CreateCameraForRobot(_activeRobot2, p2Spawn, 1);
            ConfigureCameraViewport(_spawnedCamera2, CameraSide.Right);
        }
    }

    private GameObject CreateCameraForRobot(GameObject robot, Transform spawnPoint, int robotSlot)
    {
        if (robot == null)
            return null;

        string objectToLoad = "Cameras/" + _settings.view;
        _activeCam = Resources.Load<GameObject>(objectToLoad);

        if (_activeCam == null)
        {
            Debug.LogWarning($"Camera prefab not found at Resources/{objectToLoad}");
            return null;
        }

        var parent = robot;
        var spawnRotation = spawnPoint != null ? spawnPoint.gameObject : robot;

        if (_fms && _settings.view == Cameras.DriverStation)
        {
            StationNum station = GetStationNumberForRobot(robotSlot);
            bool useBlueSide = _settings.playMode == Util.PlayMode.TwoVsZero || robotSlot == 0;

            var stationCam = useBlueSide
                ? _fms.blueStationCams[(int)station]
                : _fms.redStationCams[(int)station];

            parent = stationCam;
            spawnRotation = stationCam.gameObject;
        }

        var spawnedCamera = Instantiate(
            _activeCam,
            Vector3.zero,
            spawnRotation.transform.rotation,
            parent.transform
        );

        spawnedCamera.transform.localPosition = Vector3.zero;

        var lookAt = spawnedCamera.GetComponentInChildren<LookAtRobot>(true);
        if (lookAt != null) 
            lookAt.SetRobotSlot(robotSlot);

        return spawnedCamera;
    }

    private void ConfigureCameraViewport(GameObject cameraObject, CameraSide side)
    {
        if (cameraObject == null)
            return;

        Rect rect;
        float depth;

        switch (side)
        {
            case CameraSide.Full:
                rect = new Rect(0f, 0f, 1f, 1f);
                depth = 0f;
                break;

            case CameraSide.Left:
                rect = new Rect(0f, 0f, 0.5f, 1f);
                depth = 0f;
                break;

            case CameraSide.Right:
                rect = new Rect(0.5f, 0f, 0.5f, 1f);
                depth = 1f;
                break;

            default:
                rect = new Rect(0f, 0f, 1f, 1f);
                depth = 0f;
                break;
        }

        var cameras = cameraObject.GetComponentsInChildren<Camera>(true);
        foreach (var cam in cameras)
        {
            cam.rect = rect;
            cam.depth = depth;
        }

        var listeners = cameraObject.GetComponentsInChildren<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++)
        {
            listeners[i].enabled = side != CameraSide.Right && i == 0;
        }
    }
}