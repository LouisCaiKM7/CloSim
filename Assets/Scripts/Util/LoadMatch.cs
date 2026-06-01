using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using Util;
using UnityEngine.InputSystem.Users;

[Serializable]
public class TeamSpawnLocation
{
    public string name;
    public Transform point;
}

[Serializable]
public class PlayerMatchSettings
{
    public int robotIndex;
    public int blueSpawnIndex;
    public int redSpawnIndex;
    public bool useVanityBumpers = true;
    public StationNum driverStation = StationNum.One;
    public Cameras view = Cameras.ThirdPerson;

    public PlayerMatchSettings Clone()
    {
        return new PlayerMatchSettings
        {
            robotIndex = robotIndex,
            blueSpawnIndex = blueSpawnIndex,
            redSpawnIndex = redSpawnIndex,
            useVanityBumpers = useVanityBumpers,
            driverStation = driverStation,
            view = view
        };
    }
}

[Serializable]
public class MatchSettings
{
    public List<PlayerMatchSettings> players = new()
    {
        new PlayerMatchSettings { driverStation = StationNum.One },
        new PlayerMatchSettings { driverStation = StationNum.Three },
        new PlayerMatchSettings { driverStation = StationNum.One },
        new PlayerMatchSettings { driverStation = StationNum.Three }
    };

    public Util.PlayMode playMode = Util.PlayMode.OneVsZero;
    public bool useBlueAlliance = true;
    public TrackingType trackingType = TrackingType.TrackRobot;

    public MatchSettings Clone()
    {
        var clone = new MatchSettings
        {
            playMode = playMode,
            useBlueAlliance = useBlueAlliance,
            trackingType = trackingType,
            players = new List<PlayerMatchSettings>()
        };

        for (int i = 0; i < players.Count; i++)
            clone.players.Add(players[i].Clone());

        while (clone.players.Count < 4)
            clone.players.Add(new PlayerMatchSettings());

        return clone;
    }

    public PlayerMatchSettings GetPlayer(int index)
    {
        while (players.Count < 4)
            players.Add(new PlayerMatchSettings());

        return players[Mathf.Clamp(index, 0, 3)];
    }
}

public class LoadMatch : MonoBehaviour
{
    [Header("Field")] [SerializeField] private GameObject[] fieldPrefab;

    [Header("Spawn Points")] [SerializeField]
    private List<TeamSpawnLocation> blueSideSpawns = new();

    [SerializeField] private List<TeamSpawnLocation> redSideSpawns = new();

    [Header("Spawn Rotation Overrides")] [SerializeField]
    private List<string> robotsToFlipSpawn180 = new();

    [Header("Default Match Settings")] [SerializeField]
    private Cameras defaultView = Cameras.ThirdPerson;

    [SerializeField] private Util.PlayMode defaultPlayMode = Util.PlayMode.OneVsZero;
    [SerializeField] private bool defaultUseBlueAlliance = true;
    [SerializeField] private TrackingType defaultTrackingType = TrackingType.TrackRobot;

    [Header("Driver Station Cameras")] [SerializeField]
    private StationNum player1DriverStation = 0;

    [SerializeField] private StationNum player2DriverStation = (StationNum)2;

    [Header("Field Camera")] [SerializeField]
    private string fieldCameraAnchorName = "FieldCameraAnchor";

    [Header("Input")] [SerializeField] private string robotActionMap = "Robot";
    [SerializeField] private string gamepadControlScheme = "Gamepad";
    [SerializeField] private string keyboardControlScheme = "Keyboard";
    [SerializeField] private InputActionAsset builderActions;

    [Header("Runtime Camera Toggle")] [SerializeField]
    private bool allowRightStickCameraToggle = true;

    private bool _runtimeCameraViewsInitialized;

    private readonly List<GameObject> _availableRobots = new List<GameObject>();
    private readonly HashSet<PlayerInput> _runtimeInputAssetsCloned = new();

    private GameObject _fieldHolder;
    private readonly GameObject[] _activeRobots = new GameObject[4];
    private readonly GameObject[] _spawnedCameras = new GameObject[4];
    private readonly Cameras[] _runtimeViews = new Cameras[4];

    private readonly bool[] _rightStickWasPressed = new bool[4];
    private readonly bool[] _keyboardEWasPressed = new bool[4];

    private GameObject _fieldCamera;
    private GameObject _activeCam;

    private FMS _fms;

    private bool _isResettingField;
    private int _setupVersion;
    private int _pairedVersion = -1;
    private Coroutine _inputSetupCoroutine;

    private MatchSettings _settings = new MatchSettings();

    private HumanPlayerOutpost[] _humanPlayerOutposts = System.Array.Empty<HumanPlayerOutpost>();
    private HumanPlayerType _selectedHumanPlayerType = HumanPlayerType.Bucket;

    private Coroutine _resetCoroutine;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;

        CheckRobots();
    }

    private void LateUpdate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;

        CheckRobots();
#endif
    }

    private void Start()
    {
        if (!Application.isPlaying)
            return;

        CheckRobots();

        int defaultRobotIndex = _availableRobots.Count > 0
            ? Mathf.Clamp(4, 0, _availableRobots.Count - 1)
            : 0;

        _settings = new MatchSettings
        {
            playMode = defaultPlayMode,
            useBlueAlliance = defaultUseBlueAlliance,
            trackingType = defaultTrackingType
        };

        for (int i = 0; i < 4; i++)
        {
            PlayerMatchSettings player = _settings.GetPlayer(i);

            player.robotIndex = defaultRobotIndex;
            player.blueSpawnIndex = ClampSpawnIndex(i, blueSideSpawns.Count);
            player.redSpawnIndex = ClampSpawnIndex(i, redSideSpawns.Count);
            player.useVanityBumpers = true;
            player.view = defaultView;

            player.driverStation = i switch
            {
                0 => player1DriverStation,
                1 => player2DriverStation,
                2 => StationNum.One,
                3 => StationNum.Three,
                _ => StationNum.One
            };
        }

        SanitizeSettings();
        SanitizeSpawnSettings();
        SetRuntimeCameraViewsFromSettings();

        ResetField();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        HandleRuntimeCameraToggle();
    }

    private void SanitizeSettings()
    {
        int robotCount = _availableRobots.Count;

        for (int i = 0; i < 4; i++)
        {
            PlayerMatchSettings player = _settings.GetPlayer(i);

            player.robotIndex = robotCount > 0
                ? Mathf.Clamp(player.robotIndex, 0, robotCount - 1)
                : 0;

            player.driverStation = ClampDriverStation(player.driverStation);

            if (player.view == Cameras.DriverStation &&
                !IsPlayerBlue(i) &&
                (_settings.playMode == Util.PlayMode.OneVsZero ||
                 _settings.playMode == Util.PlayMode.TwoVsZero ||
                 _settings.playMode == Util.PlayMode.ThreeVsZero))
            {
                Debug.LogWarning(
                    "Driver Station camera is only valid from the blue-side station in same-alliance modes. Forcing Blue alliance.");
                _settings.useBlueAlliance = true;
            }
        }
    }

    private void SanitizeSpawnSettings()
    {
        for (int i = 0; i < 4; i++)
        {
            PlayerMatchSettings player = _settings.GetPlayer(i);

            player.blueSpawnIndex = ClampSpawnIndex(player.blueSpawnIndex, blueSideSpawns.Count);
            player.redSpawnIndex = ClampSpawnIndex(player.redSpawnIndex, redSideSpawns.Count);
        }

        EnforceUniqueSpawnSelectionsForAlliance(true);
        EnforceUniqueSpawnSelectionsForAlliance(false);
    }

    private void EnforceUniqueSpawnSelectionsForAlliance(bool blueAlliance)
    {
        int playerCount = GetPlayerCount();
        int spawnCount = blueAlliance ? blueSideSpawns.Count : redSideSpawns.Count;

        if (spawnCount <= 1)
            return;

        HashSet<int> used = new();

        for (int i = 0; i < playerCount; i++)
        {
            if (IsPlayerBlue(i) != blueAlliance)
                continue;

            PlayerMatchSettings player = _settings.GetPlayer(i);
            int currentIndex = blueAlliance ? player.blueSpawnIndex : player.redSpawnIndex;

            if (!used.Contains(currentIndex))
            {
                used.Add(currentIndex);
                continue;
            }

            int replacement = currentIndex;

            for (int j = 0; j < spawnCount; j++)
            {
                if (!used.Contains(j))
                {
                    replacement = j;
                    break;
                }
            }

            if (blueAlliance)
                player.blueSpawnIndex = replacement;
            else
                player.redSpawnIndex = replacement;

            used.Add(replacement);
        }
    }

    private int ClampSpawnIndex(int value, int count)
    {
        if (count <= 0) return 0;
        return Mathf.Clamp(value, 0, count - 1);
    }

    private StationNum ClampDriverStation(StationNum station)
    {
        int value = Mathf.Clamp((int)station, (int)StationNum.One, (int)StationNum.Three);
        return (StationNum)value;
    }

    public MatchSettings GetSettingsCopy()
    {
        return _settings.Clone();
    }

    private int GetPlayerCount()
    {
        return _settings.playMode switch
        {
            Util.PlayMode.OneVsZero => 1,
            Util.PlayMode.TwoVsZero => 2,
            Util.PlayMode.OneVsOne => 2,
            Util.PlayMode.ThreeVsZero => 3,
            Util.PlayMode.TwoVsTwo => 4,
            _ => 1
        };
    }

    private bool IsPlayerBlue(int playerIndex)
    {
        return _settings.playMode switch
        {
            Util.PlayMode.OneVsZero => _settings.useBlueAlliance,
            Util.PlayMode.TwoVsZero => _settings.useBlueAlliance,
            Util.PlayMode.ThreeVsZero => _settings.useBlueAlliance,

            Util.PlayMode.OneVsOne => playerIndex == 0,
            Util.PlayMode.TwoVsTwo => playerIndex < 2,

            _ => true
        };
    }

    private bool UsesFourWaySplit()
    {
        return _settings.playMode == Util.PlayMode.ThreeVsZero ||
               _settings.playMode == Util.PlayMode.TwoVsTwo;
    }

    public void ApplySettings(MatchSettings newSettings)
    {
        if (newSettings == null)
            return;

        _settings = newSettings.Clone();

        CheckRobots();
        SanitizeSettings();
        SanitizeSpawnSettings();
        SetRuntimeCameraViewsFromSettings();

        ApplyHumanPlayerObjects();
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

    private StationNum GetStationNumberForRobot(int robotSlot)
    {
        return _settings.GetPlayer(robotSlot).driverStation;
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
        return _settings.GetPlayer(0).view;
    }

    public Cameras GetViewType(int playerIndex)
    {
        return _settings.GetPlayer(playerIndex).view;
    }

    public Util.PlayMode GetPlayMode()
    {
        return _settings.playMode;
    }

    private void SetRuntimeCameraViewsFromSettings()
    {
        for (int i = 0; i < 4; i++)
            _runtimeViews[i] = _settings.GetPlayer(i).view;

        _runtimeCameraViewsInitialized = true;
    }

    private void InitializeRuntimeCameraViewsIfNeeded()
    {
        if (_runtimeCameraViewsInitialized)
            return;

        SetRuntimeCameraViewsFromSettings();
    }

    public bool UsesBlueAlliance()
    {
        return _settings.useBlueAlliance;
    }

    public void ResetField()
    {
        if (_isResettingField)
            return;

        if (_resetCoroutine != null)
            StopCoroutine(_resetCoroutine);

        _resetCoroutine = StartCoroutine(ResetFieldRoutine());
    }

    private IEnumerator ResetFieldRoutine()
    {
        _isResettingField = true;
        _setupVersion++;
        _pairedVersion = -1;

        if (_inputSetupCoroutine != null)
        {
            StopCoroutine(_inputSetupCoroutine);
            _inputSetupCoroutine = null;
        }

        if (_fieldHolder != null)
        {
            foreach (var spawner in _fieldHolder.GetComponentsInChildren<SpawnGamePiece>(true))
                spawner.enabled = false;

            foreach (var scorer in _fieldHolder.GetComponentsInChildren<FieldScorer>(true))
                scorer.enabled = false;

            foreach (var fms in _fieldHolder.GetComponentsInChildren<FMS>(true))
                fms.enabled = false;
        }

        SpawnGamePiece.ClearTargets();
        Utils.resetParentCache();

        CheckRobots();
        SanitizeSettings();
        SanitizeSpawnSettings();

        InitializeRuntimeCameraViewsIfNeeded();

        DestroySpawnedCameraOnly();
        DeleteRobots();
        DestroyField();

        yield return null;

        LoadField();

        // Must happen AFTER LoadField(), not before DestroyField().
        CacheHumanPlayerOutposts();

        SpawnRobots();
        AddSplitScreenCameras();

        // Must happen AFTER CacheHumanPlayerOutposts().
        ApplyHumanPlayerObjects();

        Utils.resetParentCache();

        _inputSetupCoroutine = StartCoroutine(SetupInputsWhenReady(_setupVersion));

        FieldScorer.ResetFuelCounters();

        if (_fms)
            _fms.Restart();

        yield return null;

        _isResettingField = false;
        _resetCoroutine = null;
    }

    private IEnumerator SetupInputsWhenReady(int version)
    {
        float timeout = 2f;
        float startTime = Time.time;

        while (Time.time - startTime < timeout)
        {
            if (version != _setupVersion)
                yield break;

            bool allReady = true;
            int playerCount = GetPlayerCount();

            for (int i = 0; i < playerCount; i++)
            {
                GameObject robot = _activeRobots[i];

                if (robot == null)
                    continue;

                EnsurePlayerInputConfigured(robot);

                if (!HasReadyPlayerInput(robot))
                    allReady = false;
            }

            if (allReady)
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
        Array.Clear(_activeRobots, 0, _activeRobots.Length);

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

        int playerCount = GetPlayerCount();

        for (int i = 0; i < playerCount; i++)
        {
            PlayerMatchSettings player = _settings.GetPlayer(i);

            Transform spawn = GetSpawnPointForRobot(i);
            if (spawn == null)
            {
                Debug.LogError($"Player {i + 1} spawn point is not assigned.");
                continue;
            }

            GameObject robotPrefab = GetRobotPrefabBySelection(player.robotIndex);
            if (robotPrefab == null)
            {
                Debug.LogError($"Selected robot prefab for Player {i + 1} is invalid.");
                continue;
            }

            Quaternion rotation = GetSpawnRotationForRobot(spawn, robotPrefab);

            GameObject robot = Instantiate(
                robotPrefab,
                spawn.position,
                rotation,
                _fieldHolder.transform
            );

            robot.name = $"{robotPrefab.name}_P{i + 1}";
            _activeRobots[i] = robot;

            EnsurePlayerInputConfigured(robot);
            ConfigureRobotDriveMode(robot, i);

            StartCoroutine(ConfigureRobotBumpersWhenReady(
                robot,
                robotPrefab,
                i,
                player.useVanityBumpers
            ));

            ConfigureOutpostReleaseOwnership(robot, i);
        }
    }

    private Transform GetSpawnPointForRobot(int playerIndex)
    {
        PlayerMatchSettings player = _settings.GetPlayer(playerIndex);

        bool blue = IsPlayerBlue(playerIndex);
        int index = blue ? player.blueSpawnIndex : player.redSpawnIndex;

        List<TeamSpawnLocation> spawns = blue ? blueSideSpawns : redSideSpawns;

        if (spawns == null || spawns.Count == 0)
            return null;

        index = Mathf.Clamp(index, 0, spawns.Count - 1);
        return spawns[index].point;
    }

    private bool ShouldFlipSpawnRotation(GameObject robotPrefab)
    {
        if (robotPrefab == null)
            return false;

        for (int i = 0; i < robotsToFlipSpawn180.Count; i++)
        {
            if (string.Equals(
                    robotsToFlipSpawn180[i]?.Trim(),
                    robotPrefab.name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private Quaternion GetSpawnRotationForRobot(Transform spawnPoint, GameObject robotPrefab)
    {
        if (spawnPoint == null)
            return Quaternion.identity;

        Quaternion rotation = spawnPoint.rotation;

        if (ShouldFlipSpawnRotation(robotPrefab))
            rotation *= Quaternion.Euler(0f, 180f, 0f);

        return rotation;
    }

    public int GetHumanPlayerOwnerSlotForAlliance(bool blueAlliance)
    {
        int playerCount = GetPlayerCount();

        for (int i = 0; i < playerCount; i++)
        {
            if (IsPlayerBlue(i) == blueAlliance)
                return i;
        }

        return 0;
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

        ReadOnlyArray<Gamepad> pads = Gamepad.all;
        int playerCount = GetPlayerCount();

        bool allowKeyboardForPlayer2 =
            playerCount == 2 &&
            (_settings.playMode == Util.PlayMode.TwoVsZero ||
             _settings.playMode == Util.PlayMode.OneVsOne);

        for (int i = 0; i < playerCount; i++)
        {
            GameObject robot = _activeRobots[i];

            if (robot == null)
                continue;

            if (i < pads.Count)
            {
                BindRobotToGamepad(robot, pads[i], gamepadControlScheme);
            }
            else if (i == 0 && Keyboard.current != null)
            {
                BindRobotToKeyboard(robot, keyboardControlScheme);
            }
            else if (i == 1 && allowKeyboardForPlayer2 && Keyboard.current != null)
            {
                BindRobotToKeyboard(robot, keyboardControlScheme);
            }
            else
            {
                DisableRobotInput(robot);
                Debug.LogWarning($"Player {i + 1} has no valid input device. Connect a gamepad.");
            }
        }

        for (int i = playerCount; i < _activeRobots.Length; i++)
        {
            if (_activeRobots[i] != null)
                DisableRobotInput(_activeRobots[i]);
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
            playerInput.DeactivateInput();

            playerInput.neverAutoSwitchControlSchemes = true;
            playerInput.defaultActionMap = robotActionMap;

            playerInput.actions.Disable();
            playerInput.actions.bindingMask = null;

            if (playerInput.user.valid)
                playerInput.user.UnpairDevices();

            InputUser.PerformPairingWithDevice(gamepad, playerInput.user);

            playerInput.SwitchCurrentControlScheme(controlScheme, gamepad);
            playerInput.SwitchCurrentActionMap(robotActionMap);

            playerInput.actions.bindingMask = InputBinding.MaskByGroup(controlScheme);
            playerInput.ActivateInput();
        }
        catch (Exception ex)
        {
            Debug.LogError($"{robot.name} failed to bind gamepad {gamepad.displayName}: {ex}");
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
            playerInput.DeactivateInput();

            playerInput.neverAutoSwitchControlSchemes = true;
            playerInput.defaultActionMap = robotActionMap;

            playerInput.actions.Disable();
            playerInput.actions.bindingMask = null;

            if (playerInput.user.valid)
                playerInput.user.UnpairDevices();

            InputUser.PerformPairingWithDevice(Keyboard.current, playerInput.user);

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

    private bool IsRobotOnRedAllianceSide(int playerIndex)
    {
        return !IsPlayerBlue(playerIndex);
    }

    private void ConfigureRobotDriveMode(GameObject robot, int playerIndex)
    {
        if (robot == null)
            return;

        var frame = robot.GetComponent<BuildFrame>();
        if (frame == null)
            return;

        var controller = frame.GetSwerveController();
        if (controller == null)
            return;

        bool robotIsRedSide = !IsPlayerBlue(playerIndex);
        Cameras view = _runtimeViews[playerIndex];

        controller.isRed = robotIsRedSide;
        controller.reversed = false;

        switch (view)
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

    private void ConfigureOutpostReleaseOwnership(GameObject robot, int playerSlot)
    {
        if (robot == null)
            return;

        StartCoroutine(ConfigureOutpostReleaseOwnershipWhenReady(robot, playerSlot));
    }

    private IEnumerator ConfigureOutpostReleaseOwnershipWhenReady(GameObject robot, int playerSlot)
    {
        const float timeout = 2f;
        float startTime = Time.time;

        while (robot != null && Time.time - startTime < timeout)
        {
            var outpostReleases = robot.GetComponentsInChildren<OutpostRelease>(true);

            if (outpostReleases.Length > 0)
            {
                foreach (var release in outpostReleases)
                    release.ConfigureOwnership(playerSlot);

                yield break;
            }

            yield return null;
        }
    }

    public bool RobotLoaded()
    {
        for (int i = 0; i < _activeRobots.Length; i++)
        {
            if (_activeRobots[i] != null)
                return true;
        }

        return false;
    }

    public GameObject GetRobotLoaded()
    {
        return GetRobotLoaded(0);
    }

    public GameObject GetRobotLoaded(int index)
    {
        if (index < 0 || index >= _activeRobots.Length)
            return null;

        return _activeRobots[index];
    }

    public GameObject[] GetLoadedRobots()
    {
        return _activeRobots;
    }

    private void DeleteRobots()
    {
        DestroySpawnedCameraOnly();

        for (int i = 0; i < _activeRobots.Length; i++)
        {
            GameObject robot = _activeRobots[i];

            if (robot == null)
                continue;

            var input = robot.GetComponent<PlayerInput>();
            if (input != null)
                _runtimeInputAssetsCloned.Remove(input);

            Destroy(robot);
            _activeRobots[i] = null;
        }
    }

    private void DestroySpawnedCameraOnly()
    {
        for (int i = 0; i < _spawnedCameras.Length; i++)
        {
            if (_spawnedCameras[i] != null)
            {
                Destroy(_spawnedCameras[i]);
                _spawnedCameras[i] = null;
            }
        }

        if (_fieldCamera != null)
        {
            Destroy(_fieldCamera);
            _fieldCamera = null;
        }
    }

    public void CheckRobots()
    {
        GameObject[] loadedRobots = Resources.LoadAll<GameObject>("Robots");

        _availableRobots.Clear();
        _availableRobots.AddRange(loadedRobots);

        SanitizeSettings();
    }

    private bool HasReadyPlayerInput(GameObject robot)
    {
        if (robot == null)
            return false;

        var playerInput = robot.GetComponent<PlayerInput>();
        return playerInput != null && playerInput.actions != null;
    }

    public bool HasVanityBumperMaterialAt(int index)
    {
        CheckRobots();

        if (_availableRobots.Count == 0)
            return false;

        index = Mathf.Clamp(index, 0, _availableRobots.Count - 1);

        string robotName = _availableRobots[index].name;
        Material material = Resources.Load<Material>($"{VanityBumperMaterialFolder}/{robotName}");

        return material != null;
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

        if (!_runtimeInputAssetsCloned.Contains(playerInput))
        {
            InputActionAsset source = playerInput.actions != null
                ? playerInput.actions
                : builderActions;

            if (source == null)
            {
                Debug.LogError($"{robot.name} has no InputActionAsset source.");
                return false;
            }

            playerInput.actions = Instantiate(source);
            _runtimeInputAssetsCloned.Add(playerInput);
        }

        return playerInput.actions != null;
    }

    private void AddSplitScreenCameras()
    {
        int playerCount = GetPlayerCount();

        for (int i = 0; i < playerCount; i++)
        {
            if (_activeRobots[i] == null)
                continue;

            Transform spawn = GetSpawnPointForRobot(i);
            _spawnedCameras[i] = CreateCameraForRobot(_activeRobots[i], spawn, i, _runtimeViews[i]);
            ConfigureCameraViewport(_spawnedCameras[i], i);
        }

        if (_settings.playMode == Util.PlayMode.ThreeVsZero)
            AddFieldCamera();
    }

    private GameObject CreateCameraForRobot(GameObject robot, Transform spawnPoint, int robotSlot, Cameras view)
    {
        if (robot == null)
            return null;

        string objectToLoad = GetCameraPrefabPath(view);
        _activeCam = Resources.Load<GameObject>(objectToLoad);

        if (_activeCam == null)
        {
            Debug.LogWarning($"Camera prefab not found at Resources/{objectToLoad}");
            return null;
        }

        var parent = robot;
        var spawnRotation = spawnPoint != null ? spawnPoint.gameObject : robot;

        if (_fms && view == Cameras.DriverStation)
        {
            StationNum station = GetStationNumberForRobot(robotSlot);
            bool useBlueSide = IsPlayerBlue(robotSlot);

            GameObject[] stationCams = useBlueSide
                ? _fms.blueStationCams
                : _fms.redStationCams;

            int stationIndex = Mathf.Clamp((int)station, 0, stationCams.Length - 1);

            GameObject stationCam = stationCams.Length > 0
                ? stationCams[stationIndex]
                : null;

            if (stationCam == null)
            {
                Debug.LogWarning($"Missing driver station camera for Player {robotSlot + 1}.");
                return null;
            }

            parent = stationCam;
            spawnRotation = stationCam;
        }

        var spawnedCamera = Instantiate(
            _activeCam,
            Vector3.zero,
            spawnRotation.transform.rotation,
            parent.transform
        );

        spawnedCamera.transform.localPosition = Vector3.zero;
        ConfigureSpawnedCameraLocalRotation(spawnedCamera, view);

        var lookAt = spawnedCamera.GetComponentInChildren<LookAtRobot>(true);
        if (lookAt != null)
            lookAt.SetRobotSlot(robotSlot);

        return spawnedCamera;
    }

    private void ConfigureCameraViewport(GameObject cameraObject, int playerIndex)
    {
        if (cameraObject == null)
            return;

        Rect rect = GetViewportRect(playerIndex);
        float depth = playerIndex;

        Camera[] cameras = cameraObject.GetComponentsInChildren<Camera>(true);
        foreach (Camera cam in cameras)
        {
            cam.rect = rect;
            cam.depth = depth;
        }

        AudioListener[] listeners = cameraObject.GetComponentsInChildren<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++)
            listeners[i].enabled = playerIndex == 0 && i == 0;
    }

    private Rect GetViewportRect(int playerIndex)
    {
        if (!UsesFourWaySplit())
        {
            return playerIndex switch
            {
                0 when GetPlayerCount() == 1 => new Rect(0f, 0f, 1f, 1f),
                0 => new Rect(0f, 0f, 0.5f, 1f),
                1 => new Rect(0.5f, 0f, 0.5f, 1f),
                _ => new Rect(0f, 0f, 1f, 1f)
            };
        }

        if (_settings.playMode == Util.PlayMode.ThreeVsZero)
        {
            return playerIndex switch
            {
                0 => new Rect(0f, 0.5f, 0.5f, 0.5f), // P1 top-left
                1 => new Rect(0.5f, 0.5f, 0.5f, 0.5f), // P2 top-right
                2 => new Rect(0f, 0f, 0.5f, 0.5f), // P3 bottom-left
                3 => new Rect(0.5f, 0f, 0.5f, 0.5f), // field cam bottom-right
                _ => new Rect(0f, 0f, 1f, 1f)
            };
        }

        // 2v2: blue left, red right
        return playerIndex switch
        {
            0 => new Rect(0f, 0.5f, 0.5f, 0.5f), // P1 blue top-left
            1 => new Rect(0f, 0f, 0.5f, 0.5f), // P2 blue bottom-left
            2 => new Rect(0.5f, 0.5f, 0.5f, 0.5f), // P3 red top-right
            3 => new Rect(0.5f, 0f, 0.5f, 0.5f), // P4 red bottom-right
            _ => new Rect(0f, 0f, 1f, 1f)
        };
    }

    private void AddFieldCamera()
    {
        Transform anchor = FindFieldCameraAnchor();
        if (anchor == null)
        {
            Debug.LogWarning($"No field camera anchor named {fieldCameraAnchorName} found on the loaded field.");
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(GetCameraPrefabPath(Cameras.FirstPerson));
        if (prefab == null)
        {
            Debug.LogWarning("Field camera could not load Resources/Cameras/FirstPerson.");
            return;
        }

        _fieldCamera = Instantiate(prefab, anchor.position, anchor.rotation, anchor);
        _fieldCamera.transform.localPosition = Vector3.zero;
        _fieldCamera.transform.localRotation = Quaternion.identity;

        foreach (LookAtRobot lookAt in _fieldCamera.GetComponentsInChildren<LookAtRobot>(true))
            lookAt.enabled = false;

        ConfigureCameraViewport(_fieldCamera, 3);
    }

    private Transform FindFieldCameraAnchor()
    {
        if (_fieldHolder == null)
            return null;

        Transform[] children = _fieldHolder.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child.name == fieldCameraAnchorName)
                return child;
        }

        return null;
    }

    private void ConfigureSpawnedCameraLocalRotation(GameObject spawnedCamera, Cameras view)
    {
        if (spawnedCamera == null)
            return;

        bool isFirstPerson =
            view == Cameras.FirstPerson ||
            view == Cameras.FirstPersonReversed;

        if (!isFirstPerson)
            return;

        bool reversed = view == Cameras.FirstPersonReversed;

        spawnedCamera.transform.localPosition = Vector3.zero;
        spawnedCamera.transform.localRotation = reversed
            ? Quaternion.Euler(0f, 180f, 0f)
            : Quaternion.identity;

        Camera[] childCameras = spawnedCamera.GetComponentsInChildren<Camera>(true);

        foreach (Camera cam in childCameras)
        {
            Transform camTransform = cam.transform;
            Vector3 localEuler = camTransform.localEulerAngles;

            camTransform.localRotation = Quaternion.Euler(
                NormalizeEulerAngle(localEuler.x),
                0f,
                NormalizeEulerAngle(localEuler.z)
            );
        }

        LookAtRobot[] lookAts = spawnedCamera.GetComponentsInChildren<LookAtRobot>(true);
        foreach (LookAtRobot lookAt in lookAts)
        {
            lookAt.enabled = false;
        }
    }

    private float NormalizeEulerAngle(float angle)
    {
        angle %= 360f;

        if (angle > 180f)
            angle -= 360f;

        return angle;
    }

    private string GetCameraPrefabPath(Cameras view)
    {
        return view switch
        {
            Cameras.FirstPerson => "Cameras/FirstPerson",
            Cameras.FirstPersonReversed => "Cameras/FirstPerson",

            Cameras.ThirdPerson => "Cameras/ThirdPerson",
            Cameras.ReversedThirdPerson => "Cameras/ReversedThirdPerson",

            Cameras.DriverStation => "Cameras/DriverStation",

            _ => "Cameras/" + view
        };
    }

    private void HandleRuntimeCameraToggle()
    {
        if (!allowRightStickCameraToggle)
            return;

        int playerCount = GetPlayerCount();

        for (int i = 0; i < playerCount; i++)
        {
            HandleRuntimeCameraToggleForRobot(
                _activeRobots[i],
                i,
                ref _rightStickWasPressed[i],
                ref _keyboardEWasPressed[i]
            );
        }
    }

    private void HandleRuntimeCameraToggleForRobot(
        GameObject robot,
        int robotSlot,
        ref bool rightStickWasPressed,
        ref bool keyboardEWasPressed
    )
    {
        if (robot == null)
            return;

        var playerInput = robot.GetComponent<PlayerInput>();
        if (playerInput == null || !playerInput.user.valid)
            return;

        bool rightStickPressed = IsPairedRightStickPressed(playerInput);
        bool keyboardEPressed = IsPairedKeyboardEPressed(playerInput);

        if ((rightStickPressed && !rightStickWasPressed) ||
            (keyboardEPressed && !keyboardEWasPressed))
        {
            ToggleCameraViewForRobot(robotSlot);
        }

        rightStickWasPressed = rightStickPressed;
        keyboardEWasPressed = keyboardEPressed;
    }

    private bool IsPairedRightStickPressed(PlayerInput playerInput)
    {
        foreach (var device in playerInput.user.pairedDevices)
        {
            if (device is Gamepad gamepad)
                return gamepad.rightStickButton.isPressed;
        }

        return false;
    }

    private bool IsPairedKeyboardEPressed(PlayerInput playerInput)
    {
        foreach (var device in playerInput.user.pairedDevices)
        {
            if (device is Keyboard keyboard)
                return keyboard.eKey.isPressed;
        }

        return false;
    }

    private void ToggleCameraViewForRobot(int playerIndex)
    {
        Cameras newView = GetToggledCameraView(_runtimeViews[playerIndex]);

        if (newView == _runtimeViews[playerIndex])
            return;

        _runtimeViews[playerIndex] = newView;

        ConfigureRobotDriveMode(_activeRobots[playerIndex], playerIndex);
        RebuildSpawnedCameraForRobot(playerIndex);
    }

    private Cameras GetToggledCameraView(Cameras view)
    {
        return view switch
        {
            Cameras.FirstPerson => Cameras.FirstPersonReversed,
            Cameras.FirstPersonReversed => Cameras.FirstPerson,

            Cameras.ThirdPerson => Cameras.ReversedThirdPerson,
            Cameras.ReversedThirdPerson => Cameras.ThirdPerson,

            Cameras.DriverStation => Cameras.DriverStation,

            _ => view
        };
    }

    private void RebuildSpawnedCameraForRobot(int playerIndex)
    {
        if (_spawnedCameras[playerIndex] != null)
        {
            Destroy(_spawnedCameras[playerIndex]);
            _spawnedCameras[playerIndex] = null;
        }

        if (_activeRobots[playerIndex] == null)
            return;

        Transform spawn = GetSpawnPointForRobot(playerIndex);
        _spawnedCameras[playerIndex] = CreateCameraForRobot(
            _activeRobots[playerIndex],
            spawn,
            playerIndex,
            _runtimeViews[playerIndex]
        );

        ConfigureCameraViewport(_spawnedCameras[playerIndex], playerIndex);
    }

    public HumanPlayerOutpost[] GetHumanPlayerOutposts()
    {
        return _humanPlayerOutposts;
    }

    private void CacheHumanPlayerOutposts()
    {
        if (_fieldHolder == null)
        {
            _humanPlayerOutposts = System.Array.Empty<HumanPlayerOutpost>();
            return;
        }

        _humanPlayerOutposts = _fieldHolder.GetComponentsInChildren<HumanPlayerOutpost>(true);
    }

    public void SetHumanPlayerType(HumanPlayerType selectedType)
    {
        _selectedHumanPlayerType = selectedType;
        ApplyHumanPlayerObjects();
    }

    private void ApplyHumanPlayerObjects()
    {
        bool blueAllianceUsed = IsBlueAllianceUsedForCurrentSettings();
        bool redAllianceUsed = IsRedAllianceUsedForCurrentSettings();

        HumanPlayerRuntimeState.SetState(
            _selectedHumanPlayerType,
            blueAllianceUsed,
            redAllianceUsed
        );

        foreach (var outpost in _humanPlayerOutposts)
        {
            if (outpost == null)
                continue;

            bool allianceUsed = outpost.IsBlue ? blueAllianceUsed : redAllianceUsed;
            bool typeSelected = outpost.Type == _selectedHumanPlayerType;

            outpost.SetVisible(allianceUsed && typeSelected);
        }

        ConfigureAllOutpostReleaseOwnership();
    }

    private bool IsBlueAllianceUsedForCurrentSettings()
    {
        return _settings.playMode == Util.PlayMode.OneVsOne ||
               _settings.playMode == Util.PlayMode.TwoVsTwo ||
               _settings.useBlueAlliance;
    }

    private bool IsRedAllianceUsedForCurrentSettings()
    {
        return _settings.playMode == Util.PlayMode.OneVsOne ||
               _settings.playMode == Util.PlayMode.TwoVsTwo ||
               !_settings.useBlueAlliance;
    }

    private void ConfigureAllOutpostReleaseOwnership()
    {
        foreach (var release in GetOutpostReleases())
        {
            if (release == null)
                continue;

            bool releaseIsBlue = release.IsBlue();
            int ownerSlot = GetHumanPlayerOwnerSlotForAlliance(releaseIsBlue);

            release.ConfigureOwnership(ownerSlot);
        }
    }

    public OutpostRelease[] GetOutpostReleases()
    {
        if (_fieldHolder == null)
            return System.Array.Empty<OutpostRelease>();

        return _fieldHolder.GetComponentsInChildren<OutpostRelease>(true);
    }

    #region Bumper Materials

    private const string BlueBumperMaterialPath = "Materials/Bumpers/Blue";
    private const string RedBumperMaterialPath = "Materials/Bumpers/Red";
    private const string VanityBumperMaterialFolder = "Materials/Bumpers/Vanity";

    private Material _blueBumperMaterial;
    private Material _redBumperMaterial;
    private readonly Dictionary<string, Material> _vanityBumperMaterialCache = new();

    private IEnumerator ConfigureRobotBumpersWhenReady(
        GameObject robot,
        GameObject robotPrefab,
        int playerIndex,
        bool useVanityBumpers
    )
    {
        if (robot == null || robotPrefab == null)
            yield break;

        Material materialToApply = useVanityBumpers
            ? GetVanityBumperMaterial(robotPrefab.name)
            : null;

        if (materialToApply == null)
            materialToApply = GetAllianceBumperMaterial(playerIndex);

        const int maxFramesToWait = 10;

        for (int frame = 0; frame < maxFramesToWait; frame++)
        {
            int changedCount = ApplyMaterialToAllBumpers(robot, materialToApply);

            if (changedCount > 0)
            {
                yield break;
            }

            yield return null;
        }
    }

    private Material GetAllianceBumperMaterial(int playerIndex)
    {
        _blueBumperMaterial ??= Resources.Load<Material>(BlueBumperMaterialPath);
        _redBumperMaterial ??= Resources.Load<Material>(RedBumperMaterialPath);

        bool isRed = IsRobotOnRedAllianceSide(playerIndex);
        return isRed ? _redBumperMaterial : _blueBumperMaterial;
    }

    private Material GetVanityBumperMaterial(string robotPrefabName)
    {
        if (string.IsNullOrWhiteSpace(robotPrefabName))
            return null;

        if (_vanityBumperMaterialCache.TryGetValue(robotPrefabName, out Material cached))
            return cached;

        Material material = Resources.Load<Material>($"{VanityBumperMaterialFolder}/{robotPrefabName}");
        _vanityBumperMaterialCache[robotPrefabName] = material;

        return material;
    }

    private int ApplyMaterialToAllBumpers(GameObject robot, Material material)
    {
        int changedCount = 0;

        Renderer[] renderers = robot.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!IsBumperRenderer(renderer))
                continue;

            Material[] materials = renderer.sharedMaterials;

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = material;
            }

            renderer.sharedMaterials = materials;
            changedCount++;
        }

        return changedCount;
    }

    private bool IsBumperRenderer(Renderer renderer)
    {
        Transform current = renderer.transform;

        while (current != null)
        {
            string objectName = current.name.ToLowerInvariant();

            if (objectName.Contains("bumper"))
                return true;

            if (current.GetComponent<BuildBumper>() != null)
                return true;

            current = current.parent;
        }

        return false;
    }

    #endregion
}