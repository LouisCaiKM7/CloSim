using System;
using MyBox;
using UnityEngine;
using UnityEngine.Audio;
using Util;
using Random = Unity.Mathematics.Random;

[ExecuteAlways]
public class Buildelevator : BuildMechanism
{
    [Header("General Settings")] [SerializeField]
    private SetPoint[] setPoints;

    [SerializeField] private ElevatorType elevatorType;

    [Header("ModelSettings")] [SerializeField]
    private bool model = true;

    [SerializeField]
    private Units units = Units.Inch;

    [SerializeField]
    private float width = 10;

    [SerializeField]
    private float height = 20;

    [SerializeField]
    private int stages = 2;

    [ConditionalField(nameof(model), false)] [SerializeField]
    private bool carriage = true;

    [SerializeField]
    private float carriageHeight = 3;

    private bool Predicate() => model && carriage;

    [Tooltip("The final stage is the carriage stage")] [SerializeField]
    private float[] stageWeights;

    [Header("Advanced Settings")] [SerializeField]
    private bool useAdvancedSettings = false;

    [ConditionalField(nameof(useAdvancedSettings), false)] [SerializeField]
    private float maxSpeed;

    [ConditionalField(nameof(useAdvancedSettings), false)] [SerializeField]
    private float kP;

    [ConditionalField(nameof(useAdvancedSettings), false)] [SerializeField]
    private float kI;

    [ConditionalField(nameof(useAdvancedSettings), false)] [SerializeField]
    private float kD;

    private Vector3 _startPose;

    private GameObject _connectedBody;

    private JointDrive _drive;

    private GeneratePart[][] _tubings;

    private GameObject _tubingObject;

    private float _scaleFactor;

    private GameObject[] _modelObjects;

    private GameObject[] _stageModels;

    private Rigidbody[] _rigidbodies;

    private ConfigurableJoint[] _joints;

    private JointDrive[] _drives;

    private JointController[] _controllers;

    private bool[] _engaged;

    private bool[] _wasEngaged;

    private bool _wasCarriage;

    private AudioSource _audioSource;

    private AudioResource[] _audioClips;


    // Start is called before the first frame update
    void Start()
    {
        Startup();
        if (Application.isPlaying)
        {
            Initialize();
        }
    }

    private void OnEnable()
    {
        Startup();
    }

    private void Startup()
    {
        var loadedTubes = Resources.LoadAll<GameObject>("Parts/Tubing") as GameObject[];

        foreach (var loadedTube in loadedTubes)
        {
            if (loadedTube.name == "OneXTwoXEighth")
            {
                _tubingObject = loadedTube;
            }
        }

        var loadedSounds = Resources.LoadAll<AudioResource>("Sounds") as AudioResource[];

        _audioClips = new AudioResource[2];
        foreach (var loadedSound in loadedSounds)
        {
            if (loadedSound.name == "ElevatorClick")
            {
                _audioClips[0] = loadedSound;
            }
            else if (loadedSound.name == "ElevatorClick2")
            {
                _audioClips[1] = loadedSound;
            }
        }


        _engaged = new bool[stages];
        _wasEngaged = new bool[stages];

        for (int i = 0; i < _engaged.Length; i++)
        {
            _engaged[i] = false;
            _wasEngaged[i] = false;
        }

        _stageModels = new GameObject[stages + 1];
        for (int i = 0; i <= stages; i++)
        {
            _stageModels[i] = Utils.FindChild("Stage" + i, gameObject);
        }

        _modelObjects = new GameObject[stages + 1];
        for (int i = 0; i <= stages; i++)
        {
            _modelObjects[i] = Utils.FindChild("Model" + i, _stageModels[i]);
        }
    }

    private void Initialize()
    {
        GenerateRBs();
        GenerateJoints();
        generateControllers();

        _audioSource = gameObject.AddComponent<AudioSource>();
    }
    
    public override JointController GetController()
    {
        return _controllers[^1];
    }

    // Update is called once per frame
    void Update()
    {
        switch (units)
        {
            case Units.Inch:
                _scaleFactor = 0.0254f;
                break;
            case Units.Centimeter:
                _scaleFactor = 0.01f;
                break;
            case Units.Meter:
                _scaleFactor = 1.0f;
                break;
            case Units.Millimeter:
                _scaleFactor = 0.001f;
                break;
        }
        if (!Application.isPlaying)
        {
            
            BuildModel();
            

            if (setPoints == null) return;
            foreach (var point in setPoints)
            {
                point.shouldScaleToUnits = true;
                point.units = units;
            }
        }
        else if (elevatorType == ElevatorType.Cascade)
        {
            CascadeMovement();
        }
        else
        {
            ContinuousClick();
            ContinuousMovement();
        }
    }

    /// <summary>
    /// Sets stages to the correct height for a continuous Elevator rigging
    /// </summary>
    private void ContinuousMovement()
    {
        for (int i = 0; i < _rigidbodies.Length; i++)
        {
            if (i == _rigidbodies.Length - 1)
            {
                _controllers[i].setPoints = setPoints;
                _controllers[i].currentPosition = transform.InverseTransformPoint(_rigidbodies[i].transform.position).y;
                _controllers[i].follower = false;
                continue; //skip follower calculations
            }
            else
            {
                _controllers[i].follower = true;
            }
            
            var combinedHeight = carriage? (-carriageHeight * _scaleFactor) - (3f * 0.0254f) - ((i) * 0.0254f): 0;
            for (int j = i; j < stages-1; j++)
            {
                combinedHeight += (height * _scaleFactor) - (j < stages-2 ? (5 * 0.0254f) + (((stages-j)*2) * 0.0254f): 0);
            }
            
            float setPoint = 0;

            if (combinedHeight < transform.InverseTransformPoint(_rigidbodies[^1].transform.position).y)
            {
                setPoint = transform.InverseTransformPoint(_rigidbodies[^1].transform.position).y - combinedHeight;
            }

            _controllers[i].FollowPosition(setPoint);
            _controllers[i].currentPosition = transform.InverseTransformPoint(_rigidbodies[i].transform.position).y;
        }
    }

    /// <summary>
    /// Ques a random click when stages engage/disengage
    /// </summary>
    private void ContinuousClick()
    {
        for (int i = 0; i < _engaged.Length; i++)
        {
            if (_wasEngaged[i] != _engaged[i])
            {
                RandomClick();
            }

            _wasEngaged[i] = _engaged[i];
        }
    }

    /// <summary>
    /// Plays a random click
    /// </summary>
    private void RandomClick()
    {
        var random = new Random(100);

        // Generate a random integer between 1 (inclusive) and 3 (exclusive).
        int randomNumber = random.NextInt(1, 3);

        if (randomNumber == 1)
        {
            _audioSource.resource = _audioClips[0];
        }
        else if (randomNumber == 2)
        {
            _audioSource.resource = _audioClips[1];
        }

        _audioSource.pitch = 1.85f;
        _audioSource.reverbZoneMix = 0;
        _audioSource.Play();
    }

    /// <summary>
    /// Set stages to correct locations for a cascade rigged elevator
    /// </summary>
    private void CascadeMovement()
    {
        //TODO: add the cascade rigged motion to this function
        float totalHeight = transform.InverseTransformPoint(_rigidbodies[^1].transform.position).y;
        
        //Sets carriage position
        _controllers[^1].follower = false;
        _controllers[^1].setPoints = setPoints;
        _controllers[^1].currentPosition = totalHeight;
        
        //Sets other stages to follow carriage
        for (int i = 0; i < _rigidbodies.Length - 1; i++)
        {
            float targetHeight = totalHeight * (i + 1) / _rigidbodies.Length;
            _controllers[i].follower = true;
            _controllers[i].FollowPosition(targetHeight);
            _controllers[i].currentPosition = transform.InverseTransformPoint(_rigidbodies[i].transform.position).y;
        }
    }

    /// <summary>
    /// Generates Rigidbodies at startup
    /// </summary>
    private void GenerateRBs()
    {
        var driveTrain = Utils.FindParentRB(gameObject).GetComponent<Rigidbody>();
        _rigidbodies = new Rigidbody[_stageModels.Length - 1]; //stationary stage doesnt have a rb
        for (int i = 0; i < _stageModels.Length - 1; i++) //skip the stationary stage (0)
        {
            _rigidbodies[i] = _stageModels[i + 1].AddComponent<Rigidbody>();

            _rigidbodies[i].mass = stageWeights[i];
            _rigidbodies[i].drag = 0;
            _rigidbodies[i].angularDrag = driveTrain.angularDrag;
            _rigidbodies[i].useGravity = true;
            _rigidbodies[i].isKinematic = false;
            _rigidbodies[i].interpolation = RigidbodyInterpolation.None;
            _rigidbodies[i].collisionDetectionMode = CollisionDetectionMode.Discrete;
        }
    }

    /// <summary>
    /// Generates the Joints at startup
    /// </summary>
    private void GenerateJoints()
    {
        var driveTrain = Utils.FindParentRB(gameObject).GetComponent<Rigidbody>();
        _joints = new ConfigurableJoint[_stageModels.Length - 1]; //stationary stage doesnt have a rb
        _drives = new JointDrive[_stageModels.Length - 1];
        for (int i = 0; i < _modelObjects.Length - 1; i++) //skip the stationary stage (0)
        {
            _joints[i] = _stageModels[i + 1].AddComponent<ConfigurableJoint>();
            _joints[i].connectedBody = driveTrain;
            _joints[i].xMotion = ConfigurableJointMotion.Locked;
            _joints[i].zMotion = ConfigurableJointMotion.Locked;
            _joints[i].angularYMotion = ConfigurableJointMotion.Locked;
            _joints[i].angularZMotion = ConfigurableJointMotion.Locked;
            _joints[i].angularXMotion = ConfigurableJointMotion.Locked;
            _joints[i].yMotion = ConfigurableJointMotion.Free;

            _drive.maximumForce = 8000000;
            _drive.positionDamper = 10000;
            _drive.positionSpring = 0;
            _drive.useAcceleration = false;

            GenerateDrive(i);
        }
    }

    /// <summary>
    /// Generates the joint controllers at startup
    /// </summary>
    private void generateControllers()
    {
        _controllers = new JointController[_stageModels.Length - 1];
        for (int i = 0; i < _stageModels.Length - 1; i++)
        {
            _controllers[i] = _stageModels[i + 1].AddComponent<JointController>();

            if (useAdvancedSettings)
            {
                _controllers[i].p = kP;
                _controllers[i].i = kI;
                _controllers[i].d = kD;
                _controllers[i].iSat = 0;
                _controllers[i].max = maxSpeed;
            }
            else
            {
                _controllers[i].p = 10;
                _controllers[i].i = 0;
                _controllers[i].d = 0.0005f;
                _controllers[i].iSat = 0;
                _controllers[i].max = 4;
            }

            _controllers[i].angular = false;
            _controllers[i].driveAxis = new Vector3(0, 1, 0);
            _controllers[i].joint = _joints[i];

            if (i < _stageModels.Length - 2)
            {
                _controllers[i].p = 50;
                _controllers[i].i = 0;
                _controllers[i].d = 0.005f;
                _controllers[i].iSat = 0;
                _controllers[i].max = 500;
            }
        }
    }

    /// <summary>
    /// Generates a joint drive
    /// </summary>
    /// <param name="i"></param>
    private void GenerateDrive(int i)
    {
        _drives[i].maximumForce = 8000000;
        _drives[i].positionDamper = 10000;
        _drives[i].positionSpring = 0;
        _drives[i].useAcceleration = false;
        _joints[i].yDrive = _drive;
    }

    //ai warning
    // Separate function to handle the hierarchy reordering
    private void ReorderHierarchy()
    {
        Transform[] children = new Transform[transform.childCount];
        for (int i = 0; i < transform.childCount; i++)
        {
            children[i] = transform.GetChild(i);
        }

        System.Array.Sort(children, (a, b) =>
        {
            int indexA = -1;
            int indexB = -1;

            if (a.name.StartsWith("Stage"))
            {
                if (int.TryParse(a.name.Substring(5), out int result))
                {
                    indexA = result;
                }
            }

            if (b.name.StartsWith("Stage"))
            {
                if (int.TryParse(b.name.Substring(5), out int result))
                {
                    indexB = result;
                }
            }

            return indexA.CompareTo(indexB);
        });

        for (int i = 0; i < children.Length; i++)
        {
            children[i].SetSiblingIndex(i);
        }
    }
    //end ai warning

    //generates the standard elevator model.
    private void BuildModel()
    {
        if (stageWeights == null)
        {
            stageWeights = new float[stages];
            for (int i = 0; i < stages; i++)
            {
                stageWeights[i] = 5;
            }
        }
        else if (stageWeights.Length != stages)
        {
            float[] carriageWeightBuffer = stageWeights;
            stageWeights = new float[stages];

            if (carriageWeightBuffer.Length > stages)
            {
                var stagesRemoved = carriageWeightBuffer.Length - stages;

                for (int i = 0; i < stages; i++)
                {
                    stageWeights[i] = carriageWeightBuffer[i + stagesRemoved];
                }
            }
            else
            {
                stageWeights = new float[stages];

                var stagesAdded = stages - carriageWeightBuffer.Length;
                for (int i = 0; i < stages; i++)
                {
                    if (i < stagesAdded)
                    {
                        stageWeights[i] = 5;
                    }
                    else
                    {
                        stageWeights[i] = carriageWeightBuffer[i - (stagesAdded)];
                    }
                }
            }
        }

        if (_stageModels == null)
        {
            _stageModels = new GameObject[stages + 1];
            for (int i = 0; i < _stageModels.Length; i++)
            {
                _stageModels[i] = new GameObject("Stage" + i);
                _stageModels[i].transform.parent = transform;
                _stageModels[i].transform.localPosition = Vector3.zero;
                _stageModels[i].transform.localRotation = Quaternion.identity;
                _stageModels[i].transform.localScale = Vector3.one;
            }
        }
        else if (_stageModels.Length != stages + 1) //ai warning
        {
            int oldLength = _stageModels.Length;
            int newLength = stages + 1;

            if (newLength > oldLength) // Growing - Insert new stage at the beginning and reorder
            {
                int stagesToAdd = newLength - oldLength;

                // Resize the array to accommodate the new stages
                Array.Resize(ref _stageModels, newLength);

                // Shift all existing stage GameObjects up by the number of stages being added
                for (int i = _stageModels.Length - 1; i >= stagesToAdd; i--)
                {
                    _stageModels[i] = _stageModels[i - stagesToAdd];
                    if (_stageModels[i] != null)
                    {
                        _stageModels[i].name = "Stage" + i;
                        // Update Model child name as well
                        GameObject modelTransform = Utils.FindChild("Model" + (i - stagesToAdd), _stageModels[i]);
                        if (modelTransform != null)
                        {
                            modelTransform.name = "Model" + i;
                        }
                    }
                }

                // Create the new stage GameObjects at the beginning
                for (int i = 0; i < stagesToAdd; i++)
                {
                    GameObject newStage = new GameObject("Stage" + i);
                    newStage.transform.parent = transform;
                    newStage.transform.localPosition = Vector3.zero;
                    newStage.transform.localRotation = Quaternion.identity;
                    newStage.transform.localScale = Vector3.one;
                    _stageModels[i] = newStage;
                }

                for (int i = 0; i < _stageModels.Length; i++)
                {
                    var models = Utils.FindChild("Model" + (i), _stageModels[i]);
                    if (models != null)
                    {
                        DestroyImmediate(models);
                    }
                }

                ReorderHierarchy();
            }
            else // Shrinking - Revised to shift later GameObjects down
            {
                GameObject[] newStageModels = new GameObject[newLength];
                int offset = _stageModels.Length - newLength; // Number of elements to remove from the beginning

                // Shift the later GameObjects to the beginning of the new array
                for (int i = 0; i < newLength; i++)
                {
                    if (i + offset < _stageModels.Length && _stageModels[i + offset] != null)
                    {
                        newStageModels[i] = _stageModels[i + offset];
                        newStageModels[i].name = "Stage" + i;
                    }
                    else
                    {
                        newStageModels[i] = new GameObject("Stage" + i);
                        newStageModels[i].transform.parent = transform;
                        newStageModels[i].transform.localPosition = Vector3.zero;
                        newStageModels[i].transform.localRotation = Quaternion.identity;
                        newStageModels[i].transform.localScale = Vector3.one;
                    }
                }

                // Destroy the GameObjects that were at the beginning of the old array
                for (int i = 0; i < offset; i++)
                {
                    if (_stageModels[i] != null)
                    {
                        DestroyImmediate(_stageModels[i]);
                    }
                }

                _stageModels = newStageModels;

                for (int i = 0; i < _stageModels.Length; i++)
                {
                    var models = Utils.FindChild("Model" + (i + (oldLength - newLength)), _stageModels[i]);
                    if (models != null)
                    {
                        DestroyImmediate(models);
                    }
                }
            }
        }
        else
        {
            // If the size is correct, ensure names and transforms are still correct
            for (int i = 0; i < _stageModels.Length; i++)
            {
                if (_stageModels[i] == null)
                {
                    _stageModels[i] = new GameObject("Stage" + i);
                    _stageModels[i].transform.parent = transform;
                    _stageModels[i].transform.localPosition = Vector3.zero;
                    _stageModels[i].transform.localRotation = Quaternion.identity;
                    _stageModels[i].transform.localScale = Vector3.one;
                }
                else if (_stageModels[i].name != "Stage" + i)
                {
                    _stageModels[i].name = "Stage" + i;
                    _stageModels[i].transform.parent = transform;
                    _stageModels[i].transform.localPosition = Vector3.zero;
                    _stageModels[i].transform.localRotation = Quaternion.identity;
                    _stageModels[i].transform.localScale = Vector3.one;
                }
            }
        } //end ai warning
        
        if (!model) return;

        int nonCrossBraceStages = 1;
        if (carriage)
        {
            nonCrossBraceStages = 2;
        }

        if (_stageModels.Length == 0)
        {
            _modelObjects = new GameObject[stages + 1];

            _tubings = new GeneratePart[stages + 1][];
        }
        else if (_stageModels.Length != stages + 1)
        {
            foreach (var modelObject in _modelObjects)
            {
                if (_modelObjects != null)
                {
                    DestroyImmediate(modelObject.gameObject);
                }
            }

            _modelObjects = new GameObject[stages + 1];

            _tubings = new GeneratePart[stages + 1][];
        }
        else
        {
            if (_tubings == null || _tubings.Length != stages + 1)
            {
                _tubings = new GeneratePart[stages + 1][];
            }

            if (_modelObjects == null || _modelObjects.Length != stages + 1)
            {
                _modelObjects = new GameObject[stages + 1];
            }
        }

        for (int i = 0; i <= stages; i++) //0 is stationary so 1 stage should generate 2
        {
            if (i <= stages - nonCrossBraceStages || i == 0) //determines cross brace stage models
            {
                _modelObjects[i] = Utils.FindChild("Model" + i, _stageModels[i]);
                if (_modelObjects[i] == null)
                {
                    _modelObjects[i] = new GameObject
                    {
                        name = "Model" + i,
                        transform =
                        {
                            parent = _stageModels[i].transform,
                            localPosition = Vector3.zero,
                            localRotation = Quaternion.identity,
                            localScale = Vector3.one
                        }
                    };
                }


                _modelObjects[i].transform.localPosition =
                    new Vector3(0, ((i * 1) + (i * 0.05f)) * 0.0254f, 0); //step the bottom up by 1 inch

                var tubing = CheckTubes(_modelObjects[i], i, 6);

                if (tubing[0] == null)
                {
                    tubing[0] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[0].Part = _tubingObject;
                    tubing[0].PartName = "lower crossbar (" + i + ")";
                    tubing[0].LoadedPartLocation =
                        new Vector3(0, 0.5f * 0.0254f, 0); //raise model by an inch so 0,0,0 is the absolute bottom.
                    tubing[0].LoadedPartRotation = Quaternion.Euler(0, 90, 90);
                    tubing[0].LoadedPartScale =
                        new Vector3(1, 1,
                            (width * _scaleFactor) -
                            ((2 + ((i) * 2)) * ((i > 0) ? 1.25f : 0) *
                             0.0254f)); //if stationary width goes outside height
                }
                else
                {
                    tubing[0].LoadedPartLocation =
                        new Vector3(0, 0.5f * 0.0254f, 0); //raise model by an inch so 0,0,0 is the absolute bottom.
                    tubing[0].LoadedPartRotation = Quaternion.Euler(0, 90, 90);
                    tubing[0].LoadedPartScale =
                        new Vector3(1, 1,
                            (width * _scaleFactor) -
                            ((2 + ((i) * 2)) * ((i > 0) ? 1.25f : 0) *
                             0.0254f)); //if stationary width goes outside height
                }

                if (tubing[1] == null)
                {
                    tubing[1] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[1].Part = _tubingObject;
                    tubing[1].PartName = "left Upright (" + i + ")";
                    tubing[1].LoadedPartLocation =
                        new Vector3((width / 2.0f) * -_scaleFactor - (0.5f + (1.5f * (i))) * -0.0254f,
                            (height / 2 * _scaleFactor) + (1.0f - (i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) -
                                                           ((stages - (carriage ? 1 : 0) - i))) * 0.0254f, 0);
                    tubing[1].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[1].LoadedPartScale =
                        new Vector3(1, 1,
                            (height * _scaleFactor) -
                            (1 * ((i < 2) ? 0 : i - 1) - ((stages - (carriage ? 1 : 0) - i) * -2)) * 0.0254f);
                }
                else
                {
                    tubing[1].LoadedPartLocation =
                        new Vector3((width / 2.0f) * -_scaleFactor - (0.5f + (1.5f * (i))) * -0.0254f,
                            (height / 2 * _scaleFactor) + (1.0f - (i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) -
                                                           ((stages - (carriage ? 1 : 0) - i))) * 0.0254f, 0);
                    tubing[1].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[1].LoadedPartScale =
                        new Vector3(1, 1,
                            (height * _scaleFactor) -
                            (1 * ((i < 2) ? 0 : i - 1) - ((stages - (carriage ? 1 : 0) - i) * -2)) * 0.0254f);
                }

                if (tubing[2] == null)
                {
                    tubing[2] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[2].Part = _tubingObject;
                    tubing[2].PartName = "right Upright (" + i + ")";
                    tubing[2].LoadedPartLocation =
                        new Vector3((width / 2.0f) * _scaleFactor - (0.5f + (1.5f * (i))) * 0.0254f,
                            (height / 2 * _scaleFactor) + (1.0f - (i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) -
                                                           ((stages - (carriage ? 1 : 0) - i))) * 0.0254f, 0);
                    tubing[2].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[2].LoadedPartScale =
                        new Vector3(1, 1,
                            (height * _scaleFactor) -
                            (1 * ((i < 2) ? 0 : i - 1) - ((stages - (carriage ? 1 : 0) - i) * -2)) * 0.0254f);
                }
                else
                {
                    tubing[2].LoadedPartLocation =
                        new Vector3((width / 2.0f) * _scaleFactor - (0.5f + (1.5f * (i))) * 0.0254f,
                            (height / 2 * _scaleFactor) + (1.0f - (i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) -
                                                           ((stages - (carriage ? 1 : 0) - i))) * 0.0254f, 0);
                    tubing[2].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[2].LoadedPartScale =
                        new Vector3(1, 1,
                            (height * _scaleFactor) -
                            (1 * ((i < 2) ? 0 : i - 1) - ((stages - (carriage ? 1 : 0) - i) * -2)) * 0.0254f);
                }

                if (tubing[3] == null)
                {
                    tubing[3] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[3].Part = _tubingObject;
                    tubing[3].PartName = "left cross brace standoff (" + i + ")";
                    tubing[3].LoadedPartLocation =
                        new Vector3(((width / 2.0f) * -_scaleFactor) - ((0.5f + (1 * (i))) * -0.0254f),
                            (height * _scaleFactor) - ((stages - (carriage ? 1 : 0)) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f), -2 * 0.0254f);
                    tubing[3].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
                    tubing[3].LoadedPartScale =
                        new Vector3(1, 1, 2 * 0.0254f);
                }
                else
                {
                    tubing[3].LoadedPartLocation =
                        new Vector3(((width / 2.0f) * -_scaleFactor) - ((0.5f + (1.5f * (i))) * -0.0254f),
                            (height * _scaleFactor) - ((stages - (carriage ? 1 : 0)) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f), -2 * 0.0254f);
                    tubing[3].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
                    tubing[3].LoadedPartScale =
                        new Vector3(1, 1, 2 * 0.0254f);
                }

                if (tubing[4] == null)
                {
                    tubing[4] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[4].Part = _tubingObject;
                    tubing[4].PartName = "right cross brace standoff (" + i + ")";
                    tubing[4].LoadedPartLocation =
                        new Vector3(((width / 2.0f) * _scaleFactor) - ((0.5f + (1.5f * (i))) * 0.0254f),
                            (height * _scaleFactor) - ((stages - (carriage ? 1 : 0)) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f), -2 * 0.0254f);
                    tubing[4].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
                    tubing[4].LoadedPartScale =
                        new Vector3(1, 1, 2 * 0.0254f);
                }
                else
                {
                    tubing[4].LoadedPartLocation =
                        new Vector3(((width / 2.0f) * _scaleFactor) - ((0.5f + (1.5f * (i))) * 0.0254f),
                            (height * _scaleFactor) - ((stages - (carriage ? 1 : 0)) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f), -2 * 0.0254f);
                    tubing[4].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
                    tubing[4].LoadedPartScale =
                        new Vector3(1, 1, 2 * 0.0254f);
                }

                if (tubing[5] == null)
                {
                    tubing[5] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[5].Part = _tubingObject;
                    tubing[5].PartName = "upper cross brace (" + i + ")";
                    tubing[5].LoadedPartLocation =
                        new Vector3(0,
                            (height * _scaleFactor) - ((stages - (carriage ? 1 : 0)) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f), -3.5f * 0.0254f);
                    tubing[5].LoadedPartRotation = Quaternion.Euler(0, 90, 0);
                    tubing[5].LoadedPartScale =
                        new Vector3(1, 1, (width * _scaleFactor) - ((2 + ((i) * 2)) * (1) * 0.0254f) + (2 * 0.0254f));
                }
                else
                {
                    tubing[5].LoadedPartLocation =
                        new Vector3(0,
                            (height * _scaleFactor) - ((stages - (carriage ? 1 : 0)) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f), -3.5f * 0.0254f);
                    tubing[5].LoadedPartRotation = Quaternion.Euler(0, 90, 0);
                    tubing[5].LoadedPartScale =
                        new Vector3(1, 1,
                            (width * _scaleFactor) - ((2 + ((i) * 2) * 1.5f) * (1) * 0.0254f) + (2 * 0.0254f));
                }

                _tubings[i] = tubing;
            }
            else if (carriage && i == stages)
            {
                _modelObjects[i] = Utils.FindChild("Model" + i, _stageModels[i]);
                //TODO: fix carrage height math. 5 inches is 3
                if (_modelObjects[i] == null)
                {
                    _modelObjects[i] = new GameObject
                    {
                        name = "Model" + i,
                        transform =
                        {
                            parent = _stageModels[i].transform,
                            localPosition = Vector3.zero,
                            localRotation = Quaternion.identity,
                            localScale = Vector3.one
                        }
                    };
                }


                _modelObjects[i].transform.localPosition =
                    new Vector3(0, ((i * 1) + (i * 0.05f)) * 0.0254f, 0); //step the bottom up by 1 inch

                var tubing = CheckTubes(_modelObjects[i], i, 4);

                if (tubing[0] == null)
                {
                    tubing[0] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[0].Part = _tubingObject;
                    tubing[0].PartName = "lower crossbar (" + i + ")";
                    tubing[0].LoadedPartLocation =
                        new Vector3(0, 0.5f * 0.0254f, 0); //raise model by an inch so 0,0,0 is the absolute bottom.
                    tubing[0].LoadedPartRotation = Quaternion.Euler(0, 90, 90);
                    tubing[0].LoadedPartScale =
                        new Vector3(1, 1,
                            (width * _scaleFactor) -
                            ((2 + ((i) * 2) * 1.5f) * (1) * 0.0254f)); //if stationary width goes outside height
                }
                else
                {
                    tubing[0].LoadedPartLocation =
                        new Vector3(0, 0.5f * 0.0254f, 0); //raise model by an inch so 0,0,0 is the absolute bottom.
                    tubing[0].LoadedPartRotation = Quaternion.Euler(0, 90, 90);
                    tubing[0].LoadedPartScale =
                        new Vector3(1, 1,
                            (width * _scaleFactor) -
                            ((2 + ((i) * 2) * 1.5f) * (1) * 0.0254f)); //if stationary width goes outside height
                }

                if (tubing[1] == null)
                {
                    tubing[1] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[1].Part = _tubingObject;
                    tubing[1].PartName = "left Upright (" + i + ")";
                    tubing[1].LoadedPartLocation =
                        new Vector3((width / 2.0f) * -_scaleFactor - (0.5f + (1.5f * (i))) * -0.0254f,
                            ((carriageHeight / 2)) * _scaleFactor, 0);
                    tubing[1].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[1].LoadedPartScale =
                        new Vector3(1, 1, (carriageHeight * _scaleFactor));
                }
                else
                {
                    tubing[1].LoadedPartLocation =
                        new Vector3((width / 2.0f) * -_scaleFactor - (0.5f + (1.5f * (i))) * -0.0254f,
                            ((carriageHeight / 2)) * _scaleFactor, 0);
                    tubing[1].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[1].LoadedPartScale =
                        new Vector3(1, 1, (carriageHeight * _scaleFactor));
                }

                if (tubing[2] == null)
                {
                    tubing[2] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[2].Part = _tubingObject;
                    tubing[2].PartName = "right Upright (" + i + ")";
                    tubing[2].LoadedPartLocation =
                        new Vector3((width / 2.0f) * _scaleFactor - (0.5f + (1.5f * (i))) * 0.0254f,
                            ((carriageHeight / 2)) * _scaleFactor, 0);
                    tubing[2].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[2].LoadedPartScale =
                        new Vector3(1, 1, (carriageHeight * _scaleFactor));
                }
                else
                {
                    tubing[2].LoadedPartLocation =
                        new Vector3((width / 2.0f) * _scaleFactor - (0.5f + (1.5f * (i))) * 0.0254f,
                            ((carriageHeight / 2)) * _scaleFactor, 0);
                    tubing[2].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[2].LoadedPartScale =
                        new Vector3(1, 1, (carriageHeight * _scaleFactor));
                }

                if (tubing[3] == null)
                {
                    tubing[3] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[3].Part = _tubingObject;
                    tubing[3].PartName = "upper cross brace (" + i + ")";
                    tubing[3].LoadedPartLocation =
                        new Vector3(0, ((carriageHeight * _scaleFactor) - (0.5f * 0.0254f)), 0);
                    tubing[3].LoadedPartRotation = Quaternion.Euler(0, 90, 90);
                    tubing[3].LoadedPartScale =
                        new Vector3(1, 1, (width * _scaleFactor) - ((2 + ((i) * 2) * 1.5f) * (1) * 0.0254f));
                }
                else
                {
                    tubing[3].LoadedPartLocation =
                        new Vector3(0, ((carriageHeight * _scaleFactor) - (0.5f * 0.0254f)), 0);
                    tubing[3].LoadedPartRotation = Quaternion.Euler(0, 90, 90);
                    tubing[3].LoadedPartScale =
                        new Vector3(1, 1, (width * _scaleFactor) - ((2 + ((i) * 2) * 1.5f) * (1) * 0.0254f));
                }
            }
            else if (stages - i <= nonCrossBraceStages || !carriage)
            {
                _modelObjects[i] = Utils.FindChild("Model" + i, _stageModels[i]);
                if (_modelObjects[i] == null)
                {
                    _modelObjects[i] = new GameObject
                    {
                        name = "Model" + i,
                        transform =
                        {
                            parent = _stageModels[i].transform,
                            localPosition = Vector3.zero,
                            localRotation = Quaternion.identity,
                            localScale = Vector3.one
                        }
                    };
                }


                _modelObjects[i].transform.localPosition =
                    new Vector3(0, ((i * 1) + (i * 0.05f)) * 0.0254f, 0); //step the bottom up by 1 inch

                var tubing = CheckTubes(_modelObjects[i], i, 4);

                if (tubing[0] == null)
                {
                    tubing[0] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[0].Part = _tubingObject;
                    tubing[0].PartName = "lower crossbar (" + i + ")";
                    tubing[0].LoadedPartLocation =
                        new Vector3(0, 0.5f * 0.0254f, 0); //raise model by an inch so 0,0,0 is the absolute bottom.
                    tubing[0].LoadedPartRotation = Quaternion.Euler(0, 90, 90);
                    tubing[0].LoadedPartScale =
                        new Vector3(1, 1,
                            (width * _scaleFactor) - (((2 + (i * 2)) * (1)) * 0.0254f) -
                            (((i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) * 1) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f));
                }
                else
                {
                    tubing[0].LoadedPartLocation =
                        new Vector3(0, 0.5f * 0.0254f, 0); //raise model by an inch so 0,0,0 is the absolute bottom.
                    tubing[0].LoadedPartRotation = Quaternion.Euler(0, 90, 90);
                    tubing[0].LoadedPartScale =
                        new Vector3(1, 1,
                            (width * _scaleFactor) - (((2 + (i * 2)) * (1)) * 0.0254f) -
                            (((i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) * 1) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f));
                }

                if (tubing[1] == null)
                {
                    tubing[1] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[1].Part = _tubingObject;
                    tubing[1].PartName = "left Upright (" + i + ")";
                    tubing[1].LoadedPartLocation =
                        new Vector3((width / 2.0f) * -_scaleFactor - (0.5f + (1.5f * (i))) * -0.0254f,
                            (height / 2 * _scaleFactor) + (1.0f - (i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) -
                                                           ((stages - (carriage ? 1 : 0) - i))) * 0.0254f, 0);
                    tubing[1].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[1].LoadedPartScale =
                        new Vector3(1, 1,
                            (height * _scaleFactor) -
                            (1 * ((i < 2) ? 0 : i - 1) - ((stages - (carriage ? 1 : 0) - i) * -2)) * 0.0254f);
                }
                else
                {
                    tubing[1].LoadedPartLocation =
                        new Vector3((width / 2.0f) * -_scaleFactor - (0.5f + (1.5f * (i))) * -0.0254f,
                            (height / 2 * _scaleFactor) + (1.0f - (i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) -
                                                           ((stages - (carriage ? 1 : 0) - i))) * 0.0254f, 0);
                    tubing[1].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[1].LoadedPartScale =
                        new Vector3(1, 1,
                            (height * _scaleFactor) -
                            (1 * ((i < 2) ? 0 : i - 1) - ((stages - (carriage ? 1 : 0) - i) * -2)) * 0.0254f);
                }

                if (tubing[2] == null)
                {
                    tubing[2] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[2].Part = _tubingObject;
                    tubing[2].PartName = "right Upright (" + i + ")";
                    tubing[2].LoadedPartLocation =
                        new Vector3((width / 2.0f) * _scaleFactor - (0.5f + (1.5f * (i))) * 0.0254f,
                            (height / 2 * _scaleFactor) + (1.0f - (i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) -
                                                           ((stages - (carriage ? 1 : 0) - i))) * 0.0254f, 0);
                    tubing[2].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[2].LoadedPartScale =
                        new Vector3(1, 1,
                            (height * _scaleFactor) -
                            (1 * ((i < 2) ? 0 : i - 1) - ((stages - (carriage ? 1 : 0) - i) * -2)) * 0.0254f);
                }
                else
                {
                    tubing[2].LoadedPartLocation =
                        new Vector3((width / 2.0f) * _scaleFactor - (0.5f + (1.5f * (i))) * 0.0254f,
                            (height / 2 * _scaleFactor) + (1.0f - (i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) -
                                                           ((stages - (carriage ? 1 : 0) - i))) * 0.0254f, 0);
                    tubing[2].LoadedPartRotation = Quaternion.Euler(90, 0, 0);
                    tubing[2].LoadedPartScale =
                        new Vector3(1, 1,
                            (height * _scaleFactor) -
                            (1 * ((i < 2) ? 0 : i - 1) - ((stages - (carriage ? 1 : 0) - i) * -2)) * 0.0254f);
                }

                if (tubing[3] == null)
                {
                    tubing[3] = _modelObjects[i].AddComponent<GeneratePart>();
                    tubing[3].Part = _tubingObject;
                    tubing[3].PartName = "upper cross brace (" + i + ")";
                    tubing[3].LoadedPartLocation =
                        new Vector3(0,
                            (height * _scaleFactor) - ((stages - (carriage ? 1 : 0)) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f) + (0.5f * 0.0254f), 0);
                    tubing[3].LoadedPartRotation = Quaternion.Euler(0, 90, 90);
                    tubing[3].LoadedPartScale =
                        new Vector3(1, 1,
                            (width * _scaleFactor) - (((2 + (i * 2)) * (1)) * 0.0254f) -
                            (((i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) * 1) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f));
                }
                else
                {
                    tubing[3].LoadedPartLocation =
                        new Vector3(0,
                            (height * _scaleFactor) - ((stages - (carriage ? 1 : 0)) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f) + (0.5f * 0.0254f), 0);
                    tubing[3].LoadedPartRotation = Quaternion.Euler(0, 90, 90);
                    tubing[3].LoadedPartScale =
                        new Vector3(1, 1,
                            (width * _scaleFactor) - (((2 + (i * 2)) * (1)) * 0.0254f) -
                            (((i >= 1 ? 1 + ((i - 1) * 0.5f) : 0) * 1) * 0.0254f) -
                            (((stages - (carriage ? 1 : 0) - i)) * 0.0254f));
                }
            }
        }

        if (_wasCarriage != carriage && _modelObjects != null)
        {
            foreach (var modelObject in _modelObjects)
            {
                if (!modelObject)
                {
                    continue; 
                }
                
                GeneratePart[] componentsToDestroy = modelObject.GetComponents<GeneratePart>();

                if (componentsToDestroy.Length > 0)
                {
                    foreach (var component in componentsToDestroy)
                    {
                        DestroyImmediate(component);
                    }
                }
            }
        }

        _wasCarriage = carriage;
    }

    /// <summary>
    /// identifies what models are present on an object and returns the sorted list.
    /// </summary>
    /// <param name="parent"></param>
    /// <param name="stageNum"></param>
    /// <param name="length"></param>
    /// <param name="crossBrace"></param>
    /// <returns></returns>
    private GeneratePart[] CheckTubes(GameObject parent, float stageNum, int length)
    {
        if (parent == null)
        {
            return new GeneratePart[length]; // Or handle the error differently
        }

        GeneratePart[] unfiltered = parent.GetComponents<GeneratePart>();

        GeneratePart[] filtered = new GeneratePart[length];

        foreach (var t in unfiltered)
        {
            if (t.PartName == "lower crossbar (" + stageNum + ")")
            {
                filtered[0] = t;
            }
            else if (t.PartName == "left Upright (" + stageNum + ")")
            {
                filtered[1] = t;
            }
            else if (t.PartName == "right Upright (" + stageNum + ")")
            {
                filtered[2] = t;
            }
            else if (t.PartName == "left cross brace standoff (" + stageNum + ")" && length > 4)
            {
                filtered[3] = t;
            }
            else if (t.PartName == "right cross brace standoff (" + stageNum + ")" && length > 4)
            {
                filtered[4] = t;
            }
            else if (t.PartName == "upper cross brace (" + stageNum + ")" && length != 4)
            {
                filtered[5] = t;
            }
            else if (t.PartName == "upper cross brace (" + stageNum + ")")
            {
                filtered[3] = t;
            }
        }

        return filtered;
    }
}