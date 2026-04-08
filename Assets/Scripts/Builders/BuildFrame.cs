using System;
using System.Collections;
using System.Collections.Generic;
using MyBox;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Util;

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
    private GeneratePart[] _usedModules = new GeneratePart[4]; //caches the modules that are in the world
    
    private GameObject[] _modules = new GameObject[6]; //holds the module types that could be spawned
    
    private float[] _moduleWheelDiameters = new float[6]; //sets the wheel size coresponding to the loaded module num
    
    private string[] _moduleNames = new string[4]; // array of names for the modules
    
    private Vector3[] _cornerModulePositions = new Vector3[4]; //position for cornerbiasedModules
    
    private Vector3[] _standardModulePositions = new Vector3[4];
    
    private Vector3[] _lowProfileModulePositions = new Vector3[4];
    
    private Vector3[] _usedModulePositions = new Vector3[4];

    private Vector3[] _moduleRotations = new Vector3[4];
    
    
    //Frame Stuff
    private GeneratePart[] _frameModels = new GeneratePart[4]; //caches the frame parts in the world
    
    private GameObject _frameModel;
    
    private Vector3[] _conerModuleFrameLengths = new Vector3[4];
    
    private Vector3[] _standardModuleFrameLengths = new Vector3[4];
    
    private Vector3[] _lowProfileModuleFrameLengths = new Vector3[4];
    
    private Vector3[] _usedFrameLengths = new Vector3[4];

    private Vector3[] _framePosition = new Vector3[4];
    
    private Vector3[] _frameRotation = new Vector3[4];
    
    private string[] _frameNames = new string[4];
    
    private float[] _frameHeights = new float[3];
    
    private float _usedFrameHeight;
    
    //
    
    private GameObject[] bumpers;

    private GameObject bumperParent;
    
    //
    
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
            _inputAsset = Resources.Load("Controls/Builder") as InputActionAsset;
            var playerInput = Utils.TryGetAddComponent<PlayerInput>(gameObject);
            playerInput.actions = _inputAsset;
            playerInput.neverAutoSwitchControlSchemes = true;
            playerInput.defaultControlScheme = playerNumber;
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
        var loadedModules =  Resources.LoadAll<GameObject>("Swerve") as GameObject[];

        foreach (var loadedModule in loadedModules)
        {
            if (string.Equals(loadedModule.name, ModuleType.invertedCorner.ToString(), StringComparison.CurrentCultureIgnoreCase))
            {
                _modules[0] = loadedModule;
            } else if (string.Equals(loadedModule.name, ModuleType.standardCorner.ToString(), StringComparison.CurrentCultureIgnoreCase))
            {
                _modules[1] = loadedModule;
            } else if (string.Equals(loadedModule.name, ModuleType.inverted.ToString(), StringComparison.CurrentCultureIgnoreCase))
            {
                _modules[2] = loadedModule;
            } else if (string.Equals(loadedModule.name, ModuleType.standard.ToString(), StringComparison.CurrentCultureIgnoreCase))
            {
                _modules[3] = loadedModule;
            } else if (string.Equals(loadedModule.name, ModuleType.inverted.ToString(), StringComparison.CurrentCultureIgnoreCase))
            {
                _modules[4] = loadedModule;
            } else if (string.Equals(loadedModule.name, ModuleType.lowProfile.ToString(), StringComparison.CurrentCultureIgnoreCase))
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
            ModuleType.invertedCorner => _cornerModulePositions,
            ModuleType.standardCorner => _cornerModulePositions,
            ModuleType.inverted => _standardModulePositions,
            ModuleType.standard => _standardModulePositions,
            ModuleType.lowProfile => _lowProfileModulePositions,
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
                
                _usedModules[i].Part = _modules[(int)moduleType];

                _usedModules[i].PartName = _moduleNames[i];

                _usedModules[i].LoadedPartLocation = _usedModulePositions[i];

                _usedModules[i].LoadedPartRotation = Quaternion.Euler(_moduleRotations[i]);
                        
                _usedModules[i].LoadedPartScale = Vector3.one;
            } 
            else if (_usedModules[i].Part != _modules[(int)moduleType])
            {
                _usedModules[i].Part = _modules[(int)moduleType];

                _usedModules[i].PartName = _moduleNames[i];

                _usedModules[i].LoadedPartLocation = _usedModulePositions[i];

                _usedModules[i].LoadedPartRotation = Quaternion.Euler(_moduleRotations[i]);
                        
                _usedModules[i].LoadedPartScale = Vector3.one;
            }
            else if (_usedModules[i] != null)
            {
                _usedModules[i].LoadedPartLocation = _usedModulePositions[i];

                _usedModules[i].LoadedPartRotation = Quaternion.Euler(_moduleRotations[i]);
                        
                _usedModules[i].LoadedPartScale = Vector3.one;
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
        
        var loadedTubes =  Resources.LoadAll<GameObject>("Parts/Tubing") as GameObject[];

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
            ModuleType.invertedCorner => _frameHeights[0],
            ModuleType.standardCorner => _frameHeights[0],
            ModuleType.inverted => _frameHeights[1],
            ModuleType.standard => _frameHeights[1],
            ModuleType.lowProfile => _frameHeights[2],
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
            ModuleType.invertedCorner => _conerModuleFrameLengths,
            ModuleType.standardCorner => _conerModuleFrameLengths,
            ModuleType.inverted => _standardModuleFrameLengths,
            ModuleType.standard => _standardModuleFrameLengths,
            ModuleType.lowProfile => _lowProfileModuleFrameLengths,
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

                    _frameModels[i].Part = _frameModel;

                    _frameModels[i].PartName = _frameNames[i];

                    _frameModels[i].LoadedPartLocation = _framePosition[i];

                    _frameModels[i].LoadedPartRotation = Quaternion.Euler(_frameRotation[i]);
                        
                    _frameModels[i].LoadedPartScale = _usedFrameLengths[i];
                } else if (_frameModels[i].Part !=_frameModel)
                {
                    _frameModels[i].Part = _frameModel;

                    _frameModels[i].PartName = _frameNames[i];

                    _frameModels[i].LoadedPartLocation = _framePosition[i];

                    _frameModels[i].LoadedPartRotation = Quaternion.Euler(_frameRotation[i]);
                        
                    _frameModels[i].LoadedPartScale = _usedFrameLengths[i];
                }
                else if (_frameModels[i] != null)
                {
                    _frameModels[i].LoadedPartLocation = _framePosition[i];

                    _frameModels[i].LoadedPartRotation = Quaternion.Euler(_frameRotation[i]);
                        
                    _frameModels[i].LoadedPartScale = _usedFrameLengths[i];
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
        if (!bumperParent && _driveTrain)
        {
            bumperParent = Utils.TryGetAddChild("bumpers", _driveTrain);
        }
        else if (bumperParent)
        {
            if (bumpers == null)
            {
                bumpers = new GameObject[8];
            }
            bumpers[0] = Utils.TryGetAddChild("FrontBumper", bumperParent);
            bumpers[1] = Utils.TryGetAddChild("BackBumper", bumperParent);
            bumpers[2] = Utils.TryGetAddChild("LeftBumper", bumperParent);
            bumpers[3] = Utils.TryGetAddChild("RightBumper", bumperParent);
            bumpers[4] = Utils.TryGetAddChild("LeftFrontCornerBumper", bumperParent);
            bumpers[5] = Utils.TryGetAddChild("RightFrontCornerBumper", bumperParent);
            bumpers[6] = Utils.TryGetAddChild("LeftBackCornerBumper", bumperParent);
            bumpers[7] = Utils.TryGetAddChild("RightBackCornerBumper", bumperParent);

            var height = 1 * 0.0254f;

            if (moduleType == ModuleType.lowProfile)
            {
                height = 2 * 0.0254f;
            }
            
            setBumper(bumpers[0], BumperVariants.Side, new Vector3(0,height,frameSize.y * _unitValue * 0.5f), new Vector3(0,-90,0), frameSize.x);
            setBumper(bumpers[1], BumperVariants.Side, new Vector3(0,height,-frameSize.y * _unitValue * 0.5f), new Vector3(0,90,0), frameSize.x);
            setBumper(bumpers[2], BumperVariants.Side, new Vector3(frameSize.x * _unitValue * 0.5f,height,0), new Vector3(0,0,0), frameSize.y);
            setBumper(bumpers[3], BumperVariants.Side, new Vector3(-frameSize.x * _unitValue * 0.5f,height,0), new Vector3(0,180,0), frameSize.y);
            
            setBumper(bumpers[4], BumperVariants.Corner, new Vector3(-frameSize.x * _unitValue * 0.5f,height,frameSize.y * _unitValue * 0.5f), new Vector3(0,-90,0), 1);
            setBumper(bumpers[5], BumperVariants.Corner, new Vector3(frameSize.x * _unitValue * 0.5f,height,frameSize.y * _unitValue * 0.5f), new Vector3(0,0,0), 1);
            setBumper(bumpers[6], BumperVariants.Corner, new Vector3(-frameSize.x * _unitValue * 0.5f,height,-frameSize.y * _unitValue * 0.5f), new Vector3(0,180,0), 1);
            setBumper(bumpers[7], BumperVariants.Corner, new Vector3(frameSize.x * _unitValue * 0.5f,height,-frameSize.y * _unitValue * 0.5f), new Vector3(0,90,0), 1);
        }
        
    }

    private void setBumper(GameObject bumper, BumperVariants variant, Vector3 position, Vector3 rotation, float size)
    {
        var builder = Utils.TryGetAddComponent<BuildBumper>(bumper);
        builder.setUnits(units);
        builder.SetBumper(bumperStyle, variant);
        builder.setLength(size);
        builder.SetRotation(rotation);
        builder.SetPosition(position);
    }
    private void Startup()
    {
        //load module models
        var loadedModules =  Resources.LoadAll<GameObject>("Swerve") as GameObject[];

        foreach (var loadedModule in loadedModules)
        {
            if (loadedModule.name ==  ModuleType.invertedCorner.ToString())
            {
                _modules[0] = loadedModule;
            } else if (loadedModule.name == ModuleType.standardCorner.ToString())
            {
                _modules[1] = loadedModule;
            } else if (loadedModule.name == ModuleType.inverted.ToString())
            {
                _modules[2] = loadedModule;
            } else if (loadedModule.name == ModuleType.standard.ToString())
            {
                _modules[3] = loadedModule;
            } else if (loadedModule.name == ModuleType.lowProfile.ToString())
            {
                _modules[4] = loadedModule;
            }
        }
        
        var loadedTubes =  Resources.LoadAll<GameObject>("Tubing") as GameObject[];

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
                    if (part.PartName == _moduleNames[t])
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
                        if (part.PartName == _frameNames[t])
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
