using System;
using Core;
using MyBox;
using Robot.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using Utilities;

namespace Robot.Builders
{
    [ExecuteAlways]
    public class BuildFrame : MonoBehaviour
    {
        [Header("Frame Info")]
        [SerializeField] private Units units = Units.Inch;
        [SerializeField] private Vector2 frameSize = new Vector2(29.5f, 29.5f);
        
        [SerializeField] private float robotWeight = 50f;

        [Header("Drive Train Settings")]
        [Tooltip("The simulation is currently hardcoded to Kraken X60s")]
        [SerializeField] private float gearRatio = 5.85f;
    
        [SerializeField] private ModuleType moduleType;
    
        [SerializeField] private bool generateBumpers = true;

        [ConditionalField(nameof(generateBumpers))] [SerializeField]
        private BumperType bumperStyle;

        [Header("Model Settings")] [SerializeField]
        private bool useFrameModel = true;
    
        private GameObject _driveTrain; // the game object all drivetrain spawns are handled under
        private GameObject _frame;
    
        //Moudle stuff
        private readonly GeneratePart[] _usedModules = new GeneratePart[4]; //caches the modules that are in the world
    
        private readonly GameObject[] _modules = new GameObject[6]; //holds the module types that could be spawned
    
        private readonly float[] _moduleWheelDiameters = new float[6]; //sets the wheel size coresponding to the loaded module num
    
        private readonly string[] _moduleNames = new string[4]; // array of names for the modules
    
        private readonly Vector3[] _cornerModulePositions = new Vector3[4]; //position for cornerbiasedModules
    
        private readonly Vector3[] _standardModulePositions = new Vector3[4];
    
        private readonly Vector3[] _lowProfileModulePositions = new Vector3[4];
    
        private Vector3[] _usedModulePositions = new Vector3[4];

        private readonly Vector3[] _moduleRotations = new Vector3[4];
    
    
        //Frame Stuff
        private readonly GeneratePart[] _frameModels = new GeneratePart[4]; //caches the frame parts in the world
    
        private GameObject _frameModel;
    
        private readonly Vector3[] _conerModuleFrameLengths = new Vector3[4];
    
        private readonly Vector3[] _standardModuleFrameLengths = new Vector3[4];
    
        private readonly Vector3[] _lowProfileModuleFrameLengths = new Vector3[4];
    
        private Vector3[] _usedFrameLengths = new Vector3[4];

        private readonly Vector3[] _framePosition = new Vector3[4];
    
        private readonly Vector3[] _frameRotation = new Vector3[4];
    
        private readonly string[] _frameNames = new string[4];
    
        private readonly float[] _frameHeights = new float[3];
    
        private float _usedFrameHeight;
    
        private GameObject[] _bumpers;

        private GameObject _bumperParent;
    
        private InputActionAsset _inputAsset;
    
        [HideInInspector] public string playerNumber = "Player1";
    
        private SwerveController _swerve;

        private float _unitValue;
    
        // Start is called before the first frame update
        private void Start()
        {
            Startup();
            BuildBumpers();
        
            if (Application.isPlaying)
            {
                gameObject.AddComponent<RestartMatch>();
            
                Utils.TryGetAddComponent<CustomInterpolation>(gameObject);
            }
        }

        public SwerveController GetSwerveController()
        {
            if (_swerve == null)
            {
                var playerInput = Utils.TryGetAddComponent<PlayerInput>(gameObject);

                if (playerInput.actions == null)
                {
                    _inputAsset = Resources.Load("Controls/Builder") as InputActionAsset;

                    if (_inputAsset != null)
                    {
                        playerInput.actions = Instantiate(_inputAsset);
                    }
                    else
                    {
                        Debug.LogError($"{gameObject.name} could not load Controls/Builder InputActionAsset.");
                    }
                }

                playerInput.neverAutoSwitchControlSchemes = true;
                playerInput.defaultActionMap = "Robot";
                playerInput.notificationBehavior = PlayerNotifications.InvokeUnityEvents;

                _swerve = Utils.TryGetAddComponent<SwerveController>(gameObject);
                var rb = Utils.TryGetAddComponent<Rigidbody>(gameObject);
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.mass = robotWeight;
                rb.drag = 0.5f;
                rb.angularDrag = 0.05f;
                _swerve.rb = rb;
                _swerve.gearRatio = gearRatio;
                _swerve.wheelDiameter = _moduleWheelDiameters[(int)moduleType];
            }
        
            return _swerve;
        }

        private void OnEnable()
        {
            Startup();
            BuildBumpers();
        }

        // Update is called once per frame
        private void Update()
        {
            switch (units)
            {
                case Units.Inch:
                    _unitValue = 0.0254f;
                    break;
                case Units.Centimeter:
                    _unitValue = 0.01f;
                    break;
                case Units.Meter:
                    _unitValue = 1.0f;
                    break;
                case Units.Millimeter:
                    _unitValue = 0.001f;
                    break;
            }

            //load module models
            var loadedModules =  Resources.LoadAll<GameObject>("Swerve");

            foreach (var loadedModule in loadedModules)
            {
                if (string.Equals(loadedModule.name, nameof(ModuleType.InvertedCorner), StringComparison.CurrentCultureIgnoreCase))
                {
                    _modules[0] = loadedModule;
                } else if (string.Equals(loadedModule.name, nameof(ModuleType.StandardCorner), StringComparison.CurrentCultureIgnoreCase))
                {
                    _modules[1] = loadedModule;
                } else if (string.Equals(loadedModule.name, nameof(ModuleType.Inverted), StringComparison.CurrentCultureIgnoreCase))
                {
                    _modules[2] = loadedModule;
                } else if (string.Equals(loadedModule.name, nameof(ModuleType.Standard), StringComparison.CurrentCultureIgnoreCase))
                {
                    _modules[3] = loadedModule;
                } else if (string.Equals(loadedModule.name, nameof(ModuleType.Inverted), StringComparison.CurrentCultureIgnoreCase))
                {
                    _modules[4] = loadedModule;
                } else if (string.Equals(loadedModule.name, nameof(ModuleType.LowProfile), StringComparison.CurrentCultureIgnoreCase))
                {
                    _modules[5] = loadedModule;
                }
            }
        
            if (_driveTrain == null)
            {
                _driveTrain = new GameObject
                {
                    name = "driveTrain",
                    transform =
                    {
                        parent = transform,
                        localPosition = Vector3.zero,
                        localRotation = Quaternion.identity,
                        localScale = Vector3.one
                    }
                };
            }

            //set module locations
            _cornerModulePositions[0] = new Vector3((frameSize.x * -0.5f * _unitValue) + (2.65f * 0.0254f), 0, frameSize.y * 0.5f * _unitValue - (2.65f * 0.0254f));
            _cornerModulePositions[1] = new Vector3(frameSize.x * 0.5f * _unitValue - (2.65f * 0.0254f), 0, frameSize.y * 0.5f * _unitValue - (2.65f * 0.0254f));
            _cornerModulePositions[2] = new Vector3(frameSize.x * -0.5f * _unitValue + (2.65f * 0.0254f), 0, frameSize.y * -0.5f * _unitValue + (2.65f * 0.0254f));
            _cornerModulePositions[3] = new Vector3(frameSize.x * 0.5f * _unitValue - (2.65f * 0.0254f), 0, frameSize.y * -0.5f * _unitValue + (2.65f * 0.0254f));
        
            _standardModulePositions[0] = new Vector3((frameSize.x * -0.5f * _unitValue) + (3.65f * 0.0254f), 0, frameSize.y * 0.5f * _unitValue - (3.65f * 0.0254f));
            _standardModulePositions[1] = new Vector3(frameSize.x * 0.5f * _unitValue - (3.65f * 0.0254f), 0, frameSize.y * 0.5f * _unitValue - (3.65f * 0.0254f));
            _standardModulePositions[2] = new Vector3(frameSize.x * -0.5f * _unitValue + (3.65f * 0.0254f), 0, frameSize.y * -0.5f * _unitValue + (3.65f * 0.0254f));
            _standardModulePositions[3] = new Vector3(frameSize.x * 0.5f * _unitValue - (3.65f * 0.0254f), 0, frameSize.y * -0.5f * _unitValue + (3.65f * 0.0254f));
        
            _lowProfileModulePositions[0] = new Vector3((frameSize.x * _unitValue * -0.5f) + (1.75f * 0.0254f), 0, frameSize.y * 0.5f * _unitValue - (1.75f * 0.0254f));
            _lowProfileModulePositions[1] = new Vector3(frameSize.x * _unitValue * 0.5f - (1.75f * 0.0254f), 0, frameSize.y * 0.5f * _unitValue - (1.75f * 0.0254f));
            _lowProfileModulePositions[2] = new Vector3(frameSize.x * _unitValue * -0.5f + (1.75f * 0.0254f), 0, frameSize.y * -0.5f * _unitValue + (1.75f * 0.0254f));
            _lowProfileModulePositions[3] = new Vector3(frameSize.x * _unitValue * 0.5f - (1.75f * 0.0254f), 0, frameSize.y * -0.5f * _unitValue + (1.75f * 0.0254f));

            _usedModulePositions = moduleType switch
            {
                ModuleType.InvertedCorner => _cornerModulePositions,
                ModuleType.StandardCorner => _cornerModulePositions,
                ModuleType.Inverted => _standardModulePositions,
                ModuleType.Standard => _standardModulePositions,
                ModuleType.LowProfile => _lowProfileModulePositions,
                _ => _usedModulePositions
            };

            _moduleRotations[0] = new Vector3(0, 0, 0);
            _moduleRotations[1] = new Vector3(0, 90, 0);
            _moduleRotations[2] = new Vector3(0, 270, 0);
            _moduleRotations[3] = new Vector3(0, 180, 0);

            //generate and check modules
            for (int i = 0; i < _usedModules.Length; i++)
            {
                if (_usedModules[i] == null)
                {
                    _usedModules[i] = _driveTrain.AddComponent<GeneratePart>();
                
                    _usedModules[i].part = _modules[(int)moduleType];

                    _usedModules[i].partName = _moduleNames[i];

                    _usedModules[i].loadedPartLocation = _usedModulePositions[i];

                    _usedModules[i].loadedPartRotation = Quaternion.Euler(_moduleRotations[i]);
                        
                    _usedModules[i].loadedPartScale = Vector3.one;
                } 
                else if (_usedModules[i].part != _modules[(int)moduleType])
                {
                    _usedModules[i].part = _modules[(int)moduleType];

                    _usedModules[i].partName = _moduleNames[i];

                    _usedModules[i].loadedPartLocation = _usedModulePositions[i];

                    _usedModules[i].loadedPartRotation = Quaternion.Euler(_moduleRotations[i]);
                        
                    _usedModules[i].loadedPartScale = Vector3.one;
                }
                else if (_usedModules[i] != null)
                {
                    _usedModules[i].loadedPartLocation = _usedModulePositions[i];

                    _usedModules[i].loadedPartRotation = Quaternion.Euler(_moduleRotations[i]);
                        
                    _usedModules[i].loadedPartScale = Vector3.one;
                }
            }
        
        
        
            // Frame Models

            if (_driveTrain != null)
            {
                if (_frame == null)
                {
                    _frame = new GameObject
                    {
                        name = "frameModel",
                        transform =
                        {
                            parent = _driveTrain.transform,
                            localPosition = Vector3.zero,
                            localRotation = Quaternion.identity,
                            localScale = Vector3.one
                        }
                    };
                }
            }
        
            var loadedTubes =  Resources.LoadAll<GameObject>("Parts/Tubing");

            foreach (var loadedTube in loadedTubes)
            {
                if (loadedTube.name == "OneXTwoXEighth")
                {
                    _frameModel = loadedTube;
                }
            }
        
            _frameHeights[0] = 0.8f; //corner
            _frameHeights[1] = 0.8f; //standard
            _frameHeights[2] = 2.65f; //low profile
        
            _usedFrameHeight = moduleType switch
            {
                ModuleType.InvertedCorner => _frameHeights[0],
                ModuleType.StandardCorner => _frameHeights[0],
                ModuleType.Inverted => _frameHeights[1],
                ModuleType.Standard => _frameHeights[1],
                ModuleType.LowProfile => _frameHeights[2],
                _ => 0
            };
        
            _framePosition[0] = new Vector3(0, _usedFrameHeight * 0.0254f, frameSize.y/2 * _unitValue - (0.5f * 0.0254f)); 
            _framePosition[1] = new Vector3(0, _usedFrameHeight * 0.0254f, -frameSize.y/2 * _unitValue + (0.5f * 0.0254f));
            _framePosition[2] = new Vector3(frameSize.x/2 * _unitValue - (0.5f * 0.0254f), _usedFrameHeight * 0.0254f, 0);
            _framePosition[3] = new Vector3(-frameSize.x/2 * _unitValue + (0.5f * 0.0254f), _usedFrameHeight * 0.0254f, 0);

            _conerModuleFrameLengths[0] = new Vector3(1, 1, frameSize.x * _unitValue - (8.25f * 0.0254f));
            _conerModuleFrameLengths[1] = new Vector3(1, 1, frameSize.x * _unitValue - (8.25f * 0.0254f));
            _conerModuleFrameLengths[2] = new Vector3(1, 1, frameSize.y * _unitValue - (8.25f * 0.0254f));
            _conerModuleFrameLengths[3] = new Vector3(1, 1, frameSize.y * _unitValue - (8.25f * 0.0254f));
        
            _standardModuleFrameLengths[0] = new Vector3(1, 1, frameSize.x * _unitValue- (2 * 0.0254f));
            _standardModuleFrameLengths[1] = new Vector3(1, 1, frameSize.x * _unitValue- (2 * 0.0254f));
            _standardModuleFrameLengths[2] = new Vector3(1, 1, frameSize.y * _unitValue);
            _standardModuleFrameLengths[3] = new Vector3(1, 1, frameSize.y * _unitValue);
        
            _lowProfileModuleFrameLengths[0] = new Vector3(1, 1, frameSize.x * _unitValue - (0.0254f * 7f));
            _lowProfileModuleFrameLengths[1] = new Vector3(1, 1, frameSize.x * _unitValue - (0.0254f * 7f));
            _lowProfileModuleFrameLengths[2] = new Vector3(1, 1, frameSize.y * _unitValue - (0.0254f * 7f));
            _lowProfileModuleFrameLengths[3] = new Vector3(1, 1, frameSize.y * _unitValue - (0.0254f * 7f));
        
            _usedFrameLengths = moduleType switch
            {
                ModuleType.InvertedCorner => _conerModuleFrameLengths,
                ModuleType.StandardCorner => _conerModuleFrameLengths,
                ModuleType.Inverted => _standardModuleFrameLengths,
                ModuleType.Standard => _standardModuleFrameLengths,
                ModuleType.LowProfile => _lowProfileModuleFrameLengths,
                _ => _usedModulePositions
            };
        
            _frameRotation[0] = new Vector3(0, 90, 0);
            _frameRotation[1] = new Vector3(0, 90, 0);
            _frameRotation[2] = new Vector3(0, 0, 0);
            _frameRotation[3] = new Vector3(0, 0, 0);
        
            if (useFrameModel)
            {
                for (int i = 0; i < _frameModels.Length; i++)
                {
                    if (_frameModels[i] == null)
                    {
                        _frameModels[i] = _frame.AddComponent<GeneratePart>();

                        _frameModels[i].part = _frameModel;

                        _frameModels[i].partName = _frameNames[i];

                        _frameModels[i].loadedPartLocation = _framePosition[i];

                        _frameModels[i].loadedPartRotation = Quaternion.Euler(_frameRotation[i]);
                        
                        _frameModels[i].loadedPartScale = _usedFrameLengths[i];
                    } else if (_frameModels[i].part !=_frameModel)
                    {
                        _frameModels[i].part = _frameModel;

                        _frameModels[i].partName = _frameNames[i];

                        _frameModels[i].loadedPartLocation = _framePosition[i];

                        _frameModels[i].loadedPartRotation = Quaternion.Euler(_frameRotation[i]);
                        
                        _frameModels[i].loadedPartScale = _usedFrameLengths[i];
                    }
                    else if (_frameModels[i] != null)
                    {
                        _frameModels[i].loadedPartLocation = _framePosition[i];

                        _frameModels[i].loadedPartRotation = Quaternion.Euler(_frameRotation[i]);
                        
                        _frameModels[i].loadedPartScale = _usedFrameLengths[i];
                    }
                }
            }
            else
            {
                if (_frame != null)
                {
                    DestroyImmediate(_frame);
                }
            }

            if (generateBumpers)
            {
                BuildBumpers();
            }
        }


        private void BuildBumpers()
        {
            if (!generateBumpers) return; 
            if (!_bumperParent && _driveTrain)
            {
                _bumperParent = Utils.TryGetAddChild("bumpers", _driveTrain);
            }
            else if (_bumperParent)
            {
                _bumpers ??= new GameObject[8];
                _bumpers[0] = Utils.TryGetAddChild("FrontBumper", _bumperParent);
                _bumpers[1] = Utils.TryGetAddChild("BackBumper", _bumperParent);
                _bumpers[2] = Utils.TryGetAddChild("LeftBumper", _bumperParent);
                _bumpers[3] = Utils.TryGetAddChild("RightBumper", _bumperParent);
                _bumpers[4] = Utils.TryGetAddChild("LeftFrontCornerBumper", _bumperParent);
                _bumpers[5] = Utils.TryGetAddChild("RightFrontCornerBumper", _bumperParent);
                _bumpers[6] = Utils.TryGetAddChild("LeftBackCornerBumper", _bumperParent);
                _bumpers[7] = Utils.TryGetAddChild("RightBackCornerBumper", _bumperParent);

                var height = 1 * 0.0254f;

                if (moduleType == ModuleType.LowProfile)
                {
                    height = 2 * 0.0254f;
                }
            
                SetBumper(_bumpers[0], BumperVariants.Side, new Vector3(0,height,frameSize.y * _unitValue * 0.5f), new Vector3(0,-90,0), frameSize.x);
                SetBumper(_bumpers[1], BumperVariants.Side, new Vector3(0,height,-frameSize.y * _unitValue * 0.5f), new Vector3(0,90,0), frameSize.x);
                SetBumper(_bumpers[2], BumperVariants.Side, new Vector3(frameSize.x * _unitValue * 0.5f,height,0), new Vector3(0,0,0), frameSize.y);
                SetBumper(_bumpers[3], BumperVariants.Side, new Vector3(-frameSize.x * _unitValue * 0.5f,height,0), new Vector3(0,180,0), frameSize.y);
            
                SetBumper(_bumpers[4], BumperVariants.Corner, new Vector3(-frameSize.x * _unitValue * 0.5f,height,frameSize.y * _unitValue * 0.5f), new Vector3(0,-90,0), 1);
                SetBumper(_bumpers[5], BumperVariants.Corner, new Vector3(frameSize.x * _unitValue * 0.5f,height,frameSize.y * _unitValue * 0.5f), new Vector3(0,0,0), 1);
                SetBumper(_bumpers[6], BumperVariants.Corner, new Vector3(-frameSize.x * _unitValue * 0.5f,height,-frameSize.y * _unitValue * 0.5f), new Vector3(0,180,0), 1);
                SetBumper(_bumpers[7], BumperVariants.Corner, new Vector3(frameSize.x * _unitValue * 0.5f,height,-frameSize.y * _unitValue * 0.5f), new Vector3(0,90,0), 1);
            }
        
        }

        private void SetBumper(GameObject bumper, BumperVariants variant, Vector3 position, Vector3 rotation, float size)
        {
            var builder = Utils.TryGetAddComponent<BuildBumper>(bumper);
            builder.SetUnits(units);
            builder.SetBumper(bumperStyle, variant);
            builder.SetLength(size);
            builder.SetRotation(rotation);
            builder.SetPosition(position);
        }
        private void Startup()
        {
            //load module models
            var loadedModules =  Resources.LoadAll<GameObject>("Swerve");

            foreach (var loadedModule in loadedModules)
            {
                if (loadedModule.name ==  nameof(ModuleType.InvertedCorner))
                {
                    _modules[0] = loadedModule;
                } else if (loadedModule.name == nameof(ModuleType.StandardCorner))
                {
                    _modules[1] = loadedModule;
                } else if (loadedModule.name == nameof(ModuleType.Inverted))
                {
                    _modules[2] = loadedModule;
                } else if (loadedModule.name == nameof(ModuleType.Standard))
                {
                    _modules[3] = loadedModule;
                } else if (loadedModule.name == nameof(ModuleType.LowProfile))
                {
                    _modules[4] = loadedModule;
                }
            }
        
            var loadedTubes =  Resources.LoadAll<GameObject>("Tubing");

            foreach (var loadedTube in loadedTubes)
            {
                if (loadedTube.name == "OneXTwoXEighth")
                {
                    _frameModel = loadedTube;
                }
            }

            _moduleWheelDiameters[0] = 4;
            _moduleWheelDiameters[1] = 4;
            _moduleWheelDiameters[2] = 4;
            _moduleWheelDiameters[3] = 4;
            _moduleWheelDiameters[4] = 3;

            //set modules names
            _moduleNames[0] = "lf";
            _moduleNames[1] = "rf";
            _moduleNames[2] = "lr";
            _moduleNames[3] = "rr";

            _frameNames[0] = "front";
            _frameNames[1] = "back";
            _frameNames[2] = "left";
            _frameNames[3] = "right";

            //find generated objects at startup
            _driveTrain = Utils.FindChild("driveTrain", gameObject);
        
            if (_driveTrain != null)
            {
                var generatedParts = _driveTrain.GetComponents<GeneratePart>();
                for (int t = 0; t < _usedModules.Length; t++)
                {
                    foreach (var part in generatedParts)
                    {
                        if (part.partName == _moduleNames[t])
                        {
                            _usedModules[t] = part;
                        }
                    }
                }
            
                _frame = Utils.FindChild("frameModel", _driveTrain);

                if (_frame != null)
                {
                    var frameParts = _frame.GetComponents<GeneratePart>();
                    for (int t = 0; t < _usedModules.Length; t++)
                    {
                        foreach (var part in frameParts)
                        {
                            if (part.partName == _frameNames[t])
                            {
                                _frameModels[t] = part;
                            }
                        }
                    }
                }
            }

            if (gameObject.GetComponent<SwerveController>())
            {
                _swerve = gameObject.GetComponent<SwerveController>();
            }

            if (_swerve != null && _driveTrain != null)
            {
                _swerve.gearRatio = gearRatio;
                _swerve.wheelDiameter = _moduleWheelDiameters[(int)moduleType];
            }
        }
    }
}
