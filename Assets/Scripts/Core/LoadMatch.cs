using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CameraControls;
using Field.Core;
using Field.Scoring;
using Field.SeasonSpecific.Rebuilt;
using Robot.Builders;
using UI.RobotSelection;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UnityEngine.InputSystem.Utilities;
using Utilities;

namespace Core
{
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

    public readonly struct RobotCatalogEntry
    {
        public readonly int Index;
        public readonly GameObject Prefab;
        public readonly string DisplayName;
        public readonly int TeamNumber;
        public readonly Sprite TeamIcon;
        public readonly Sprite PreviewSprite;

        public RobotCatalogEntry(int index, GameObject prefab, string displayName, int teamNumber, Sprite teamIcon,
            Sprite previewSprite)
        {
            Index = index;
            Prefab = prefab;
            DisplayName = displayName;
            TeamNumber = teamNumber;
            TeamIcon = teamIcon;
            PreviewSprite = previewSprite;
        }

        public bool HasTeamNumber => TeamNumber > 0;
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

        public PlayMode playMode = PlayMode.OneVsZero;
        public bool useBlueAlliance = true;
        public TrackingType trackingType = TrackingType.TrackRobot;

        // --- Online count model (ADDITIVE for A4 gameplay sync) ---
        // Offline play leaves these at their defaults, so GetPlayerCount()/IsPlayerBlue() fall through
        // to the legacy PlayMode switch and behave EXACTLY as before. Online play (Online.Sync) sets
        // useNetworkCounts = true with (networkBlueCount, networkRedCount) resolved from NetworkMatchConfig.
        // Slot ordering is blue-first: slots [0..networkBlueCount-1] are blue, the remainder are red.
        public bool useNetworkCounts;
        public int networkBlueCount;
        public int networkRedCount;

        public MatchSettings Clone()
        {
            var clone = new MatchSettings
            {
                playMode = playMode,
                useBlueAlliance = useBlueAlliance,
                trackingType = trackingType,
                useNetworkCounts = useNetworkCounts,
                networkBlueCount = networkBlueCount,
                networkRedCount = networkRedCount,
                players = new List<PlayerMatchSettings>()
            };

            for (int i = 0; i < players.Count; i++)
                clone.players.Add(players[i].Clone());

            // Pad to at least 4 (offline default stays byte-identical) but never truncate 5–6 online slots.
            int targetCount = Mathf.Max(4, players.Count);
            while (clone.players.Count < targetCount)
                clone.players.Add(new PlayerMatchSettings());

            return clone;
        }

        public PlayerMatchSettings GetPlayer(int index)
        {
            // Pad to cover the requested index while always keeping the offline 4-slot default.
            int targetCount = Mathf.Max(4, index + 1);
            while (players.Count < targetCount)
                players.Add(new PlayerMatchSettings());

            return players[Mathf.Clamp(index, 0, players.Count - 1)];
        }
    }

    public class LoadMatch : MonoBehaviour
    {
        [Header("Field")] [SerializeField] private GameObject[] fieldPrefab;

        [Header("Game Resources")]
        [Tooltip(
            "Resources folder used for this game scene's robot prefabs. Example: Robots/Rebuilt or Robots/Reefscape.")]
        [SerializeField]
        private string robotResourceFolder = "Robots/Rebuilt";
        
        [Tooltip("Game-specific vanity bumper material folder. Leave as the default if vanity materials are shared.")]
        [SerializeField]
        private string vanityBumperMaterialFolder = "Materials/Bumpers/Vanity";

        [Header("Spawn Points")] [SerializeField]
        private List<TeamSpawnLocation> blueSideSpawns = new();

        [SerializeField] private List<TeamSpawnLocation> redSideSpawns = new();

        [Header("Spawn Rotation Overrides")] [SerializeField]
        private List<string> robotsToFlipSpawn180 = new();

        [Header("Camera")] [SerializeField] private string fieldCameraAnchorName = "FieldCameraAnchor";
        [SerializeField] private string flipCameraActionName = "FlipCamera";

        [Header("Input")] [SerializeField] private string robotActionMap = "Robot";
        [SerializeField] private string gamepadControlScheme = "Gamepad";
        [SerializeField] private string keyboardControlScheme = "Keyboard";
        [SerializeField] private InputActionAsset builderActions;

        private bool _runtimeCameraViewsInitialized;

        private readonly List<GameObject> _availableRobots = new List<GameObject>();
        private readonly List<RobotCatalogEntry> _robotCatalog = new List<RobotCatalogEntry>();
        private readonly HashSet<PlayerInput> _runtimeInputAssetsCloned = new();

        private GameObject _fieldHolder;
        // ADDITIVE (A4): these were fixed [4] arrays. They now grow to the resolved slot count (up to 6)
        // via EnsureSlotArrays(). Offline SlotCapacity() is always 4, so the arrays stay length-4 and
        // all offline indexing/looping is byte-identical.
        private GameObject[] _activeRobots = new GameObject[4];
        private GameObject[] _spawnedCameras = new GameObject[4];
        private Cameras[] _runtimeViews = new Cameras[4];

        // --- Online gameplay-sync state (ADDITIVE; offline path never sets these) ---
        private bool _onlineMode;
        private int _localOwnedSlot = -1;

        /// <summary>Fired (online only) once the field + human objects are ready and Online.Sync may
        /// server-spawn networked robots. Never fired offline.</summary>
        public event Action OnOnlineFieldReady;

        /// <summary>True when the match is being driven by the online (server-authoritative) path.</summary>
        public bool OnlineMode => _onlineMode;

        /// <summary>The local client's owned robot slot in online play (-1 offline / spectator).</summary>
        public int LocalOwnedSlot => _localOwnedSlot;

        private GameObject _fieldCamera;
        private GameObject _activeCam;

        private Fms _fms;

        private bool _isResettingField;
        private int _setupVersion;
        private int _pairedVersion = -1;
        private Coroutine _inputSetupCoroutine;

        private MatchSettings _settings = new MatchSettings();

        private HumanPlayerOutpost[] _humanPlayerOutposts = Array.Empty<HumanPlayerOutpost>();
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

            _settings = new MatchSettings
            {
                playMode = PlayMode.OneVsZero,
                useBlueAlliance = true,
                trackingType = TrackingType.TrackRobot
            };

            for (int i = 0; i < 4; i++)
            {
                PlayerMatchSettings player = _settings.GetPlayer(i);

                player.robotIndex = 0;
                player.blueSpawnIndex = ClampSpawnIndex(i, blueSideSpawns.Count);
                player.redSpawnIndex = ClampSpawnIndex(i, redSideSpawns.Count);
                player.useVanityBumpers = true;
                player.view = Cameras.ThirdPerson;

                player.driverStation = i switch
                {
                    0 => 0,
                    1 => (StationNum)2,
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
            int slots = SlotCapacity();

            for (int i = 0; i < slots; i++)
            {
                PlayerMatchSettings player = _settings.GetPlayer(i);

                player.robotIndex = robotCount > 0
                    ? Mathf.Clamp(player.robotIndex, 0, robotCount - 1)
                    : 0;

                player.driverStation = ClampDriverStation(player.driverStation);

                if (player.view == Cameras.DriverStation &&
                    !IsPlayerBlue(i) &&
                    (_settings.playMode == PlayMode.OneVsZero ||
                     _settings.playMode == PlayMode.TwoVsZero ||
                     _settings.playMode == PlayMode.ThreeVsZero))
                {
                    Debug.LogWarning(
                        "Driver Station camera is only valid from the blue-side station in same-alliance modes. Forcing Blue alliance.");
                    _settings.useBlueAlliance = true;
                }
            }
        }

        private void SanitizeSpawnSettings()
        {
            int slots = SlotCapacity();

            for (int i = 0; i < slots; i++)
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

                if (used.Add(currentIndex))
                {
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
            // ONLINE source: count model resolved from NetworkMatchConfig (1..6). One resolution point;
            // downstream keeps calling GetPlayerCount()/IsPlayerBlue() without switching on PlayMode.
            if (_settings.useNetworkCounts)
                return Mathf.Clamp(_settings.networkBlueCount + _settings.networkRedCount, 1, 6);

            // OFFLINE source: legacy PlayMode switch — unchanged.
            return _settings.playMode switch
            {
                PlayMode.OneVsZero => 1,
                PlayMode.TwoVsZero => 2,
                PlayMode.OneVsOne => 2,
                PlayMode.ThreeVsZero => 3,
                PlayMode.TwoVsTwo => 4,
                _ => 1
            };
        }

        /// <summary>
        /// Slot -> alliance query. PUBLIC (A4) so the season scorers resolve alliance from the single
        /// source here instead of duplicating the PlayMode switch (which broke for online slots 5/6).
        /// Offline returns exactly what the legacy private switch returned.
        /// </summary>
        public bool IsPlayerBlue(int playerIndex)
        {
            // ONLINE source: blue-first slot ordering — slots [0..networkBlueCount-1] are blue.
            if (_settings.useNetworkCounts)
                return playerIndex < _settings.networkBlueCount;

            // OFFLINE source: legacy PlayMode switch — unchanged.
            return _settings.playMode switch
            {
                PlayMode.OneVsZero => _settings.useBlueAlliance,
                PlayMode.TwoVsZero => _settings.useBlueAlliance,
                PlayMode.ThreeVsZero => _settings.useBlueAlliance,

                PlayMode.OneVsOne => playerIndex == 0,
                PlayMode.TwoVsTwo => playerIndex < 2,

                _ => true
            };
        }

        // ADDITIVE (A4): resolved slot capacity. Offline this is always 4 (Max(4, 1..4)), so the fixed
        // arrays and old `for (i < 4)` loops behave exactly as before; online it grows to 5–6.
        private int SlotCapacity() => Mathf.Max(4, GetPlayerCount());

        // ADDITIVE (A4): grow the per-slot arrays to the resolved capacity. Never shrinks below 4, so
        // offline is untouched. Safe to call repeatedly.
        private void EnsureSlotArrays()
        {
            int capacity = SlotCapacity();
            if (_activeRobots.Length < capacity) Array.Resize(ref _activeRobots, capacity);
            if (_spawnedCameras.Length < capacity) Array.Resize(ref _spawnedCameras, capacity);
            if (_runtimeViews.Length < capacity) Array.Resize(ref _runtimeViews, capacity);
        }

        // ==================== Online gameplay-sync API (ADDITIVE, A4 — consumed by Online.Sync) ====================
        // None of this runs offline: SetOnlineMode(false) (the default) leaves every offline code path intact.

        /// <summary>
        /// Switch this LoadMatch into online (server-authoritative) mode. In online mode ResetField loads
        /// the field + human objects but does NOT locally spawn robots / split-screen cameras / pair local
        /// devices — Online.Sync drives those server-authoritatively via the methods below.
        /// </summary>
        public void SetOnlineMode(bool online, int localOwnedSlot = -1)
        {
            _onlineMode = online;
            _localOwnedSlot = localOwnedSlot;
        }

        /// <summary>Catalog prefab for a network-safe robotIndex (host spawns by index, never by prefab ref).</summary>
        public GameObject GetNetworkRobotPrefab(int robotIndex)
        {
            EnsureRobotCatalogLoaded();
            return GetRobotPrefabBySelection(robotIndex);
        }

        /// <summary>
        /// Resolve the world spawn pose for a slot from this scene's blue/red spawn lists (blue-first
        /// ordering). Used host-side to place networked robots. Returns false if no spawn is available.
        /// </summary>
        public bool TryGetSpawnForSlot(int slot, GameObject robotPrefab, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            Transform spawn = GetSpawnPointForRobot(slot);
            if (spawn == null)
                return false;

            position = spawn.position;
            rotation = GetSpawnRotationForRobot(spawn, robotPrefab);
            return true;
        }

        /// <summary>
        /// Register an already-network-spawned robot into a slot (called on every machine by the robot's
        /// network controller) and run the same per-robot local configuration the offline spawner runs
        /// (input asset, drive mode, alliance/vanity bumpers, outpost ownership). Does NOT instantiate.
        /// </summary>
        public void RegisterNetworkedRobot(int slot, GameObject robot)
        {
            if (robot == null || slot < 0)
                return;

            EnsureSlotArrays();
            if (slot >= _activeRobots.Length)
                return;

            _activeRobots[slot] = robot;

            EnsurePlayerInputConfigured(robot);
            ConfigureRobotDriveMode(robot, slot);

            PlayerMatchSettings player = _settings.GetPlayer(slot);
            GameObject prefab = GetRobotPrefabBySelection(player.robotIndex);
            if (prefab != null)
                StartCoroutine(ConfigureRobotBumpersWhenReady(robot, prefab, slot, player.useVanityBumpers));

            ConfigureOutpostReleaseOwnership(robot, slot);
        }

        /// <summary>
        /// Online single-view camera for the locally-owned robot: reuses the offline camera pipeline but
        /// forces a full-screen viewport and enables the one AudioListener (this client's view owns it).
        /// </summary>
        public GameObject AddOnlineCamera(int ownedSlot, Cameras view)
        {
            if (ownedSlot < 0)
                return null;

            EnsureSlotArrays();
            if (ownedSlot >= _activeRobots.Length)
                return null;

            GameObject robot = _activeRobots[ownedSlot];
            if (robot == null)
                return null;

            _runtimeViews[ownedSlot] = view;

            Transform spawn = GetSpawnPointForRobot(ownedSlot);
            GameObject cam = CreateCameraForRobot(robot, spawn, ownedSlot, view);
            ConfigureOnlineCameraViewport(cam);
            _spawnedCameras[ownedSlot] = cam;
            return cam;
        }

        private void ConfigureOnlineCameraViewport(GameObject cameraObject)
        {
            if (cameraObject == null)
                return;

            Rect full = new Rect(0f, 0f, 1f, 1f);

            Camera[] cameras = cameraObject.GetComponentsInChildren<Camera>(true);
            foreach (Camera cam in cameras)
            {
                cam.rect = full;
                cam.depth = 0f;
            }

            // Exactly one AudioListener: the local client's single full-screen view owns it.
            AudioListener[] listeners = cameraObject.GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
                listeners[i].enabled = i == 0;
        }

        /// <summary>
        /// Pair local input to ONLY the locally-owned robot online (remote robots are network-driven and
        /// get DisableRobotInput). Mirrors the per-slot device-preference logic used by offline PairInputs.
        /// </summary>
        public void PairLocalInput(int ownedSlot)
        {
            if (ownedSlot < 0)
                return;

            EnsureSlotArrays();
            if (ownedSlot >= _activeRobots.Length)
                return;

            GameObject robot = _activeRobots[ownedSlot];
            if (robot == null)
                return;

            EnsurePlayerInputConfigured(robot);

            ReadOnlyArray<Gamepad> pads = Gamepad.all;
            HashSet<int> usedGamepadIndices = new();

            PlayerInputDevicePreference preference = GetPlayerInputDevicePreference(ownedSlot);
            switch (preference)
            {
                case PlayerInputDevicePreference.Keyboard:
                    BindPreferredKeyboard(robot, ownedSlot);
                    break;

                case PlayerInputDevicePreference.Gamepad:
                    BindPreferredGamepad(robot, ownedSlot, pads, usedGamepadIndices);
                    break;

                case PlayerInputDevicePreference.Auto:
                default:
                    BindAutomatically(robot, ownedSlot, pads, usedGamepadIndices, false);
                    break;
            }
        }

        private bool UsesFourWaySplit()
        {
            return _settings.playMode == PlayMode.ThreeVsZero ||
                   _settings.playMode == PlayMode.TwoVsTwo;
        }

        public void ApplySettings(MatchSettings newSettings)
        {
            if (newSettings == null)
                return;

            _settings = newSettings.Clone();

            EnsureSlotArrays();
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

        public PlayMode GetPlayMode()
        {
            return _settings.playMode;
        }

        private void SetRuntimeCameraViewsFromSettings()
        {
            EnsureSlotArrays();

            int slots = SlotCapacity();
            for (int i = 0; i < slots; i++)
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

                foreach (var fms in _fieldHolder.GetComponentsInChildren<Fms>(true))
                    fms.enabled = false;
            }

            SpawnGamePiece.ClearTargets();
            Utils.ResetParentCache();

            CheckRobots();
            SanitizeSettings();
            SanitizeSpawnSettings();

            InitializeRuntimeCameraViewsIfNeeded();

            DestroySpawnedCameraOnly();
            DeleteRobots();
            DestroyField();

            yield return null;

            LoadField();
            CacheHumanPlayerOutposts();

            if (_onlineMode)
            {
                // ONLINE (server-authoritative): robots are NOT instantiated locally from settings here.
                // Online.Sync (IMatchLauncher / spawn manager) server-spawns N networked robots and each
                // robot's network controller calls RegisterNetworkedRobot() + AddOnlineCamera() /
                // PairLocalInput() (owned) or DisableRobotInput() (remote). Local split-screen cameras and
                // local device pairing (SetupInputsWhenReady) are intentionally skipped online.
                EnsureSlotArrays();
                OnOnlineFieldReady?.Invoke();
            }
            else
            {
                SpawnRobots();
                AddSplitScreenCameras();
            }

            ApplyHumanPlayerObjects();

            Utils.ResetParentCache();

            if (!_onlineMode)
                _inputSetupCoroutine = StartCoroutine(SetupInputsWhenReady(_setupVersion));

            FieldScorer.ResetCounters();

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

        public void SetFms(Fms fmsInstance)
        {
            _fms = fmsInstance;
        }

        public GameObject GetFieldHolder()
        {
            return _fieldHolder;
        }

        private void SpawnRobots()
        {
            EnsureSlotArrays();
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

            foreach (var t in robotsToFlipSpawn180)
            {
                if (string.Equals(
                        t?.Trim(),
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
            HashSet<int> usedGamepadIndices = new();

            int playerCount = GetPlayerCount();

            bool allowKeyboardForPlayer2 =
                playerCount == 2 &&
                (_settings.playMode == PlayMode.TwoVsZero ||
                 _settings.playMode == PlayMode.OneVsOne);

            for (int i = 0; i < playerCount; i++)
            {
                GameObject robot = _activeRobots[i];

                if (robot == null)
                    continue;

                PlayerInputDevicePreference preference = GetPlayerInputDevicePreference(i);

                switch (preference)
                {
                    case PlayerInputDevicePreference.Keyboard:
                        BindPreferredKeyboard(robot, i);
                        break;

                    case PlayerInputDevicePreference.Gamepad:
                        BindPreferredGamepad(robot, i, pads, usedGamepadIndices);
                        break;

                    case PlayerInputDevicePreference.Auto:
                    default:
                        BindAutomatically(robot, i, pads, usedGamepadIndices, allowKeyboardForPlayer2);
                        break;
                }
            }

            for (int i = playerCount; i < _activeRobots.Length; i++)
            {
                if (_activeRobots[i] != null)
                    DisableRobotInput(_activeRobots[i]);
            }
        }

        private void BindPreferredKeyboard(GameObject robot, int playerIndex)
        {
            if (Keyboard.current == null)
            {
                DisableRobotInput(robot);
                Debug.LogWarning($"Player {playerIndex + 1} is assigned to keyboard, but no keyboard is available.");
                return;
            }

            BindRobotToKeyboard(robot, keyboardControlScheme, playerIndex);
        }

        private void BindPreferredGamepad(
            GameObject robot,
            int playerIndex,
            ReadOnlyArray<Gamepad> pads,
            HashSet<int> usedGamepadIndices)
        {
            int preferredIndex = GetPlayerPreferredGamepadIndex(playerIndex);

            if (pads.Count == 0)
            {
                DisableRobotInput(robot);
                Debug.LogWarning($"Player {playerIndex + 1} is assigned to gamepad, but no gamepads are connected.");
                return;
            }

            if (preferredIndex < 0 || preferredIndex >= pads.Count)
            {
                DisableRobotInput(robot);
                Debug.LogWarning(
                    $"Player {playerIndex + 1} is assigned to gamepad index {preferredIndex}, but that gamepad is unavailable.");
                return;
            }

            if (!usedGamepadIndices.Add(preferredIndex))
            {
                DisableRobotInput(robot);
                Debug.LogWarning(
                    $"Player {playerIndex + 1} is assigned to Gamepad {preferredIndex + 1}, but it is already used by another player.");
                return;
            }

            BindRobotToGamepad(robot, pads[preferredIndex], gamepadControlScheme, playerIndex);
        }

        private void BindAutomatically(
            GameObject robot,
            int playerIndex,
            ReadOnlyArray<Gamepad> pads,
            HashSet<int> usedGamepadIndices,
            bool allowKeyboardForPlayer2)
        {
            int padIndex = FindUnusedGamepadIndex(pads, usedGamepadIndices);

            if (padIndex >= 0)
            {
                usedGamepadIndices.Add(padIndex);
                BindRobotToGamepad(robot, pads[padIndex], gamepadControlScheme, playerIndex);
                return;
            }

            if (playerIndex == 0 && Keyboard.current != null)
            {
                BindRobotToKeyboard(robot, keyboardControlScheme, playerIndex);
                return;
            }

            if (playerIndex == 1 && allowKeyboardForPlayer2 && Keyboard.current != null)
            {
                BindRobotToKeyboard(robot, keyboardControlScheme, playerIndex);
                return;
            }

            DisableRobotInput(robot);
            Debug.LogWarning($"Player {playerIndex + 1} has no valid input device.");
        }

        private int FindUnusedGamepadIndex(ReadOnlyArray<Gamepad> pads, HashSet<int> usedIndices)
        {
            for (int i = 0; i < pads.Count; i++)
            {
                if (!usedIndices.Contains(i))
                    return i;
            }

            return -1;
        }

        private const string BindingPrefsPrefix = "Controls_Player_";
        private const string DevicePrefsPrefix = "Controls_PlayerDevice_";
        private const string GamepadPrefsPrefix = "Controls_PlayerGamepadIndex_";

        private string GetGamepadPrefsKey(int playerIndex)
        {
            return $"{GamepadPrefsPrefix}{Mathf.Clamp(playerIndex, 0, 3)}";
        }


        private enum PlayerInputDevicePreference
        {
            Auto = 0,
            Keyboard = 1,
            Gamepad = 2
        }

        private string GetDevicePrefsKey(int playerIndex)
        {
            return $"{DevicePrefsPrefix}{Mathf.Clamp(playerIndex, 0, 3)}";
        }

        private PlayerInputDevicePreference GetPlayerInputDevicePreference(int playerIndex)
        {
            string key = GetDevicePrefsKey(playerIndex);

            if (!PlayerPrefs.HasKey(key))
                return PlayerInputDevicePreference.Auto;

            int value = PlayerPrefs.GetInt(key, 0);
            value = Mathf.Clamp(value, 0, 2);

            return (PlayerInputDevicePreference)value;
        }

        public DeviceKind GetPlayerPreferredDeviceKind(int playerIndex)
        {
            PlayerInputDevicePreference preference = GetPlayerInputDevicePreference(playerIndex);

            if (preference == PlayerInputDevicePreference.Keyboard)
                return DeviceKind.Keyboard;

            if (preference == PlayerInputDevicePreference.Gamepad)
                return DeviceKind.Gamepad;

            PlayerInput playerInput = GetPlayerInput(playerIndex);

            if (playerInput != null && playerInput.currentControlScheme == gamepadControlScheme)
                return DeviceKind.Gamepad;

            return DeviceKind.Keyboard;
        }

        public void SetPlayerPreferredDevice(int playerIndex, DeviceKind deviceKind, bool rebindNow = true)
        {
            playerIndex = Mathf.Clamp(playerIndex, 0, 3);

            PlayerInputDevicePreference preference =
                deviceKind == DeviceKind.Gamepad
                    ? PlayerInputDevicePreference.Gamepad
                    : PlayerInputDevicePreference.Keyboard;

            PlayerPrefs.SetInt(GetDevicePrefsKey(playerIndex), (int)preference);
            PlayerPrefs.Save();

            if (rebindNow && Application.isPlaying)
                RebindCurrentRobotInputs();
        }

        public int GetPlayerPreferredGamepadIndex(int playerIndex)
        {
            string key = GetGamepadPrefsKey(playerIndex);

            if (!PlayerPrefs.HasKey(key))
                return Mathf.Clamp(playerIndex, 0, Mathf.Max(0, Gamepad.all.Count - 1));

            int savedIndex = PlayerPrefs.GetInt(key, 0);
            return Mathf.Clamp(savedIndex, 0, Mathf.Max(0, Gamepad.all.Count - 1));
        }

        public void SetPlayerPreferredGamepadIndex(int playerIndex, int gamepadIndex, bool rebindNow = true)
        {
            playerIndex = Mathf.Clamp(playerIndex, 0, 3);

            int maxIndex = Mathf.Max(0, Gamepad.all.Count - 1);
            gamepadIndex = Mathf.Clamp(gamepadIndex, 0, maxIndex);

            PlayerPrefs.SetInt(GetGamepadPrefsKey(playerIndex), gamepadIndex);
            PlayerPrefs.Save();

            if (rebindNow && Application.isPlaying)
                RebindCurrentRobotInputs();
        }

        public List<string> GetAvailableGamepadLabels()
        {
            List<string> labels = new();

            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                labels.Add($"Gamepad {i + 1}");
            }

            if (labels.Count == 0)
                labels.Add("No gamepads connected");

            return labels;
        }

        public void RebindCurrentRobotInputs()
        {
            if (_isResettingField)
                return;

            _pairedVersion = -1;
            PairInputs();
            _pairedVersion = _setupVersion;
        }

        private string GetBindingPrefsKey(int playerIndex)
        {
            return $"{BindingPrefsPrefix}{Mathf.Clamp(playerIndex, 0, 3)}";
        }

        private void LoadSavedBindingOverrides(PlayerInput playerInput, int playerIndex)
        {
            if (playerInput == null || playerInput.actions == null)
                return;

            string key = GetBindingPrefsKey(playerIndex);

            if (!PlayerPrefs.HasKey(key))
                return;

            string json = PlayerPrefs.GetString(key);

            if (string.IsNullOrWhiteSpace(json))
                return;

            try
            {
                playerInput.actions.LoadBindingOverridesFromJson(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load binding overrides for Player {playerIndex + 1}: {ex.Message}");
            }
        }

        public void SaveCurrentBindingOverrides(int playerIndex)
        {
            PlayerInput playerInput = GetPlayerInput(playerIndex);

            if (playerInput == null || playerInput.actions == null)
                return;

            string json = playerInput.actions.SaveBindingOverridesAsJson();
            PlayerPrefs.SetString(GetBindingPrefsKey(playerIndex), json);
            PlayerPrefs.Save();
        }

        public PlayerInput GetPlayerInput(int playerIndex)
        {
            GameObject robot = GetRobotLoaded(playerIndex);
            return robot != null ? robot.GetComponent<PlayerInput>() : null;
        }

        public void ResetSavedBindingOverrides(int playerIndex)
        {
            PlayerPrefs.DeleteKey(GetBindingPrefsKey(playerIndex));
            PlayerPrefs.Save();

            PlayerInput playerInput = GetPlayerInput(playerIndex);
            if (playerInput != null && playerInput.actions != null)
                playerInput.actions.RemoveAllBindingOverrides();
        }

        private void BindRobotToGamepad(GameObject robot, Gamepad gamepad, string controlScheme, int playerIndex)
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

                LoadSavedBindingOverrides(playerInput, playerIndex);
                playerInput.actions.bindingMask = InputBinding.MaskByGroup(controlScheme);
                playerInput.ActivateInput();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{robot.name} failed to bind gamepad {gamepad.displayName}: {ex}");
            }
        }

        private void BindRobotToKeyboard(GameObject robot, string controlScheme, int playerIndex)
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

                LoadSavedBindingOverrides(playerInput, playerIndex);
                playerInput.actions.bindingMask = InputBinding.MaskByGroup(controlScheme);
                playerInput.ActivateInput();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{robot.name} failed to bind keyboard: {ex}");
            }
        }

        // PUBLIC (A4): Online.Sync disables local device input on remote-owned robots (they are driven
        // by replicated network state, not local devices).
        public void DisableRobotInput(GameObject robot)
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
            foreach (var t in _activeRobots)
            {
                if (t != null)
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

        private static string GetResourceFolder(string configuredPath, string fallbackPath)
        {
            string path = string.IsNullOrWhiteSpace(configuredPath) ? fallbackPath : configuredPath.Trim();
            return path.Trim('/');
        }

        private bool _robotCatalogLoaded;

        public void CheckRobots(bool force = false)
        {
#if !UNITY_EDITOR
    if (_robotCatalogLoaded && !force)
        return;
#endif

            int slots = SlotCapacity();
            GameObject[] selectedPrefabs = new GameObject[slots];

            for (int i = 0; i < slots; i++)
            {
                PlayerMatchSettings player = _settings.GetPlayer(i);

                if (_availableRobots.Count > 0 &&
                    player.robotIndex >= 0 &&
                    player.robotIndex < _availableRobots.Count)
                {
                    selectedPrefabs[i] = _availableRobots[player.robotIndex];
                }
            }

            GameObject[] loadedRobots = Resources.LoadAll<GameObject>(
                GetResourceFolder(robotResourceFolder, "Robots")
            );

            _availableRobots.Clear();
            _availableRobots.AddRange(loadedRobots);

            _availableRobots.Sort(CompareRobotPrefabsByTeamNumber);

            for (int i = 0; i < slots; i++)
            {
                if (selectedPrefabs[i] == null)
                    continue;

                int newIndex = _availableRobots.IndexOf(selectedPrefabs[i]);
                if (newIndex >= 0)
                    _settings.GetPlayer(i).robotIndex = newIndex;
            }

            RebuildRobotCatalog();
            SanitizeSettings();

            _robotCatalogLoaded = true;
        }
        
        private void EnsureRobotCatalogLoaded()
        {
            if (!_robotCatalogLoaded)
                CheckRobots();
        }

        public string GetRobotNameAt(int index)
        {
            EnsureRobotCatalogLoaded();

            if (_availableRobots.Count == 0)
                return "No Robots";

            index = Mathf.Clamp(index, 0, _availableRobots.Count - 1);
            return _availableRobots[index].name;
        }

        public Sprite GetRobotPreviewSpriteAt(int index)
        {
            EnsureRobotCatalogLoaded();

            if (_availableRobots.Count == 0)
                return null;

            index = Mathf.Clamp(index, 0, _availableRobots.Count - 1);
            return index < _robotCatalog.Count ? _robotCatalog[index].PreviewSprite : null;
        }

        public IReadOnlyList<RobotCatalogEntry> GetRobotCatalog()
        {
            EnsureRobotCatalogLoaded();
            return _robotCatalog;
        }

        private static int CompareRobotPrefabsByTeamNumber(GameObject a, GameObject b)
        {
            int teamA = GetTeamNumberForSort(a);
            int teamB = GetTeamNumberForSort(b);

            int teamCompare = teamA.CompareTo(teamB);
            if (teamCompare != 0)
                return teamCompare;

            string nameA = a != null ? a.name : string.Empty;
            string nameB = b != null ? b.name : string.Empty;

            return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetTeamNumberForSort(GameObject prefab)
        {
            if (prefab == null)
                return int.MaxValue;

            RobotIdentity identity = prefab.GetComponent<RobotIdentity>();

            if (identity == null || identity.teamNumber <= 0)
                return int.MaxValue;

            return identity.teamNumber;
        }

        private void RebuildRobotCatalog()
        {
            _robotCatalog.Clear();

            for (int i = 0; i < _availableRobots.Count; i++)
            {
                GameObject prefab = _availableRobots[i];
                if (prefab == null)
                    continue;

                RobotIdentity identity = prefab.GetComponent<RobotIdentity>();

                string displayName =
                    identity != null && !string.IsNullOrWhiteSpace(identity.displayNameOverride)
                        ? identity.displayNameOverride.Trim()
                        : CleanRobotDisplayName(prefab.name);

                int teamNumber = identity != null ? identity.teamNumber : 0;
                Sprite teamIcon = identity != null ? identity.teamIcon : null;
                Sprite previewSprite = identity != null ? identity.GetRobotPreview() : null;

                _robotCatalog.Add(new RobotCatalogEntry(i, prefab, displayName, teamNumber, teamIcon, previewSprite));
            }
        }

        private static string CleanRobotDisplayName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return rawName;

            return rawName
                .Replace("_P1", "")
                .Replace("_P2", "")
                .Replace("(Clone)", "")
                .Trim();
        }

        private bool HasReadyPlayerInput(GameObject robot)
        {
            if (robot == null)
                return false;

            var playerInput = robot.GetComponent<PlayerInput>();
            return playerInput != null && playerInput.actions != null;
        }

        private readonly Dictionary<string, bool> _vanityBumperExistsCache = new();

        public bool HasVanityBumperMaterialAt(int index)
        {
            EnsureRobotCatalogLoaded();

            if (_availableRobots.Count == 0)
                return false;

            index = Mathf.Clamp(index, 0, _availableRobots.Count - 1);

            string robotName = _availableRobots[index].name;

            if (_vanityBumperExistsCache.TryGetValue(robotName, out bool exists))
                return exists;

            Material material = Resources.Load<Material>(
                $"{GetResourceFolder(vanityBumperMaterialFolder, VanityBumperMaterialFolder)}/{robotName}"
            );

            exists = material != null;
            _vanityBumperExistsCache[robotName] = exists;
            return exists;
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

            if (_settings.playMode == PlayMode.ThreeVsZero)
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

            if (_settings.playMode == PlayMode.ThreeVsZero)
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
            int playerCount = GetPlayerCount();

            for (int i = 0; i < playerCount; i++)
                HandleRuntimeCameraToggleForRobot(_activeRobots[i], i);
        }

        private void HandleRuntimeCameraToggleForRobot(GameObject robot, int robotSlot)
        {
            if (robot == null)
                return;

            PlayerInput playerInput = robot.GetComponent<PlayerInput>();

            if (playerInput == null || playerInput.actions == null)
                return;

            InputActionMap inputMap = playerInput.actions.FindActionMap(robotActionMap);

            if (inputMap == null)
                return;

            InputAction flipCameraAction = inputMap.FindAction(flipCameraActionName);

            if (flipCameraAction == null)
                return;

            if (flipCameraAction.WasPressedThisFrame())
                ToggleCameraViewForRobot(robotSlot);
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
                _humanPlayerOutposts = Array.Empty<HumanPlayerOutpost>();
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
            return _settings.playMode == PlayMode.OneVsOne ||
                   _settings.playMode == PlayMode.TwoVsTwo ||
                   _settings.useBlueAlliance;
        }

        private bool IsRedAllianceUsedForCurrentSettings()
        {
            return _settings.playMode == PlayMode.OneVsOne ||
                   _settings.playMode == PlayMode.TwoVsTwo ||
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
                return Array.Empty<OutpostRelease>();

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

            Material material =
                Resources.Load<Material>(
                    $"{GetResourceFolder(vanityBumperMaterialFolder, VanityBumperMaterialFolder)}/{robotPrefabName}");
            _vanityBumperMaterialCache[robotPrefabName] = material;

            return material;
        }

        private int ApplyMaterialToAllBumpers(GameObject robot, Material material)
        {
            int changedCount = 0;

            Renderer[] renderers = robot.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer rendererValue in renderers)
            {
                if (rendererValue == null)
                    continue;

                if (!IsBumperRenderer(rendererValue))
                    continue;

                Material[] materials = rendererValue.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                rendererValue.sharedMaterials = materials;
                changedCount++;
            }

            return changedCount;
        }

        private bool IsBumperRenderer(Renderer rendererValue)
        {
            Transform current = rendererValue.transform;

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
}