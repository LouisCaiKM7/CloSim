using System;
using MyBox;
using UnityEngine;
using Util;

[ExecuteAlways]
public class BuildArm : BuildMechanism
{
    [SerializeField] private SetPoint[] setPoints;
    
    [Header("Model Settings")]
    [SerializeField] private ArmModel armModel;

    [ConditionalField(true, nameof(Predicate))] 
    [SerializeField]
    private Units units = Units.Inch;
    
    [ConditionalField(true, nameof(Predicate))] 
    [SerializeField]
    private float length = 5;
    private bool Predicate() => armModel == ArmModel.Single || armModel == ArmModel.SplitParallel || armModel == ArmModel.SingleTwoByTwo;

    [ConditionalField(nameof(armModel), false, ArmModel.SplitParallel)] [SerializeField]
    private float width;
    
    [SerializeField] private float armWeight = 1;

    [SerializeField] private bool useNoWrapPoint = false;
    [ConditionalField(nameof(useNoWrapPoint), false)]
    [SerializeField] private float noWrapAngle = 180;

    [Header("Use Advanced Settings")]
    [SerializeField] private bool useAdvancedSettings;
    [ConditionalField(nameof(useAdvancedSettings), false)]
    [SerializeField]private float max;
    [ConditionalField(nameof(useAdvancedSettings), false)]
    [SerializeField]private float kP;
    [ConditionalField(nameof(useAdvancedSettings), false)]
    [SerializeField]private float kI;
    [ConditionalField(nameof(useAdvancedSettings), false)]
    [SerializeField]private float kD;

    
    private ConfigurableJoint _joint;
    
    private Rigidbody _rigidbody;

    private GameObject _connectedBody;

    private GeneratePart[] _parts;
    
    private GameObject _modelObject;

    private JointController _controller;

    private JointDrive _drive;
    
    private static GameObject[] _tubingObject;

    private ArmModel _oldModel;

    private float scaleModifier;
    // Start is called before the first frame update
    void Start()
    {
        if (Application.isPlaying)
        {
            CreateAngleHolderParent();
            GenRB();
            GenJoint();
            GenController();
        }
    }

    private void OnEnable()
    {
        Startup();
    }
    
    public override JointController GetController()
    {
        return _controller;
    }

    // Update is called once per frame
    void Update()
    {
        switch (units)
        {
            case Units.Inch:
                scaleModifier = 0.0254f;
                break;
            case Units.Centimeter:
                scaleModifier = 0.01f;
                break;
            case Units.Meter:
                scaleModifier = 1.0f;
                break;
            case Units.Millimeter:
                scaleModifier = 0.001f;
                break;
        }
        
        if (!Application.isPlaying)
        {
            if (setPoints != null)
            {
                foreach (var point in setPoints)
                {
                    point.shouldScaleToUnits = false;
                }
            }

            BuildModel();
        }
        else
        {
            var targetAxis = Vector3.right;

            Quaternion deltaRotation = transform.localRotation;
        
            deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);
        

            float projection = Vector3.Dot(targetAxis, axis);
        
            //    - The signed angle around our target axis
            float signedAngle = angle * projection;
        
            signedAngle = Mathf.Repeat(signedAngle, 360);
            
            if (!Application.isPlaying)
            {

            }
            else
            {
                _controller.setPoints = setPoints;
                _controller.currentPosition = signedAngle;
            }
        }
    }

    private void Startup()
    {
        scaleModifier = 0.0254f;
        
        if (_tubingObject == null)
        {
            var loadedTubes = Resources.LoadAll<GameObject>("Parts/Tubing") as GameObject[];
            _tubingObject = new GameObject[2];
            foreach (var loadedTube in loadedTubes)
            {
                if (loadedTube.name == "OneXTwoXEighth")
                {
                    _tubingObject[0] = loadedTube;
                }
                else if (loadedTube.name == "TwoXTwoXEighth")
                {
                    _tubingObject[1] = loadedTube;
                }
            }
        }

        var detectedPart = Utils.FindChild("Model", gameObject);
        if (detectedPart)
        {
            _modelObject = detectedPart.gameObject;
        }

        _oldModel = armModel;
    }

    private void BuildModel()
    {
        CreateModelObject();
        
        switch (armModel)
        {
            case ArmModel.Single:
                if (_parts == null)
                {
                    CreateSingleArm();
                }
                else if (_parts.Length != 1 || _oldModel != armModel)
                {
                    DestroyImmediate(_modelObject);
                    CreateModelObject();
                    CreateSingleArm();
                }
                else
                {
                    _parts[0].LoadedPartLocation = new Vector3(0,0, (length/2) * scaleModifier);
                    _parts[0].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
                    _parts[0].LoadedPartScale = new Vector3(1, 1, length * scaleModifier);
                }
                break;
            case ArmModel.SplitParallel:
                if (_parts == null)
                {
                    CreateDoubleArm();
                }
                else if (_parts.Length != 2 || _oldModel != armModel)
                {
                    DestroyImmediate(_modelObject);
                    CreateModelObject();
                    CreateDoubleArm();
                }
                else
                {
                    _parts[0].LoadedPartLocation = new Vector3(((width/2) - 0.5f) * scaleModifier,0, (length/2) * scaleModifier);
                    _parts[0].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
                    _parts[0].LoadedPartScale = new Vector3(1, 1, length * scaleModifier);
                    
                    _parts[1].LoadedPartLocation = new Vector3(((-width/2) + 0.5f) * scaleModifier,0, (length/2) * scaleModifier);
                    _parts[1].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
                    _parts[1].LoadedPartScale = new Vector3(1, 1, length * scaleModifier);
                }
                break;
            case ArmModel.SingleTwoByTwo:
                if (_parts == null)
                {
                    CreateSingleTwoByTwoArm();
                }
                else if (_parts.Length != 1 || _oldModel != armModel)
                {
                    DestroyImmediate(_modelObject);
                    CreateModelObject();
                    CreateSingleTwoByTwoArm();
                }
                else
                {
                    _parts[0].LoadedPartLocation = new Vector3(0,0, (length/2) * scaleModifier);
                    _parts[0].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
                    _parts[0].LoadedPartScale = new Vector3(1, 1, length * scaleModifier);
                }
                break;
            case ArmModel.None:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        _oldModel = armModel;
    }
    
    private void CreateDoubleArm()
    {
        _parts = CheckTubes(_modelObject, 2);

        if (!_parts[0])
        {
            _parts[0] = _modelObject.AddComponent<GeneratePart>();
            _parts[0].Part = _tubingObject[0];
            _parts[0].PartName = "DoubleL";
            _parts[0].LoadedPartLocation = new Vector3(((width/2* scaleModifier) - (0.5f * 0.0254f)) ,0, (length/2* scaleModifier) );
            _parts[0].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
            _parts[0].LoadedPartScale = new Vector3(1, 1, length * scaleModifier);
        }

        if (!_parts[1])
        {
            _parts[1] = _modelObject.AddComponent<GeneratePart>();
            _parts[1].Part = _tubingObject[0];
            _parts[1].PartName = "DoubleR";
            _parts[1].LoadedPartLocation = new Vector3(((-width/2 * scaleModifier) + (0.5f * 0.0254f)),0, (length/2) * scaleModifier);
            _parts[1].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
            _parts[1].LoadedPartScale = new Vector3(1, 1, length * scaleModifier);
        }
    }
    
    private void CreateSingleArm()
    {
        _parts = CheckTubes(_modelObject, 1);

        if (_parts[0] == null)
        {
            _parts[0] = _modelObject.AddComponent<GeneratePart>();
            _parts[0].Part = _tubingObject[0];
            _parts[0].PartName = "Single";
            _parts[0].LoadedPartLocation = new Vector3(0,0, (length/2) * scaleModifier);
            _parts[0].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
            _parts[0].LoadedPartScale = new Vector3(1, 1, length * scaleModifier);
        }
    }
    
    private void CreateSingleTwoByTwoArm()
    {
        _parts = CheckTubes(_modelObject, 1);

        if (_parts[0] == null)
        {
            _parts[0] = _modelObject.AddComponent<GeneratePart>();
            _parts[0].Part = _tubingObject[1];
            _parts[0].PartName = "SingleTwo";
            _parts[0].LoadedPartLocation = new Vector3(0,0, (length/2) * scaleModifier);
            _parts[0].LoadedPartRotation = Quaternion.Euler(0, 0, 0);
            _parts[0].LoadedPartScale = new Vector3(1, 1, length * scaleModifier);
        }
    }
    
    private GeneratePart[] CheckTubes(GameObject parent, int num)
    {
        GeneratePart[] unfiltered = parent.GetComponents<GeneratePart>();
        
        GeneratePart[] filtered = new GeneratePart[num];

        for (int i = 0; i < num; i++)
        {
            filtered[i] = null;
        }

        foreach (var t in unfiltered)
        {
            switch (armModel)
            {
                case ArmModel.Single:
                    if (t.PartName == "Single")
                    {
                        filtered[0] = t;
                    }
                    break;
                case ArmModel.SplitParallel:
                    if (t.PartName == "DoubleL")
                    {
                        filtered[0] = t;
                    } 
                    else if (t.PartName == "DoubleR")
                    {
                        filtered[1] = t;
                    }
                    break;
                case ArmModel.SingleTwoByTwo:
                    if (t.PartName == "SingleTwo")
                    {
                        filtered[0] = t;
                    }
                    break;
                case ArmModel.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return filtered;
    }

    private void CreateModelObject()
    {
        if (_modelObject == null)
        {
            _modelObject = new GameObject
            {
                name = "Model"
            };
            
            _modelObject.transform.SetParent(transform);
            _modelObject.transform.localPosition = Vector3.zero;
            _modelObject.transform.localRotation = Quaternion.identity;
            
            _modelObject.transform.localScale = Vector3.one;
            
            _parts = null;
        }
    }

    private void CreateAngleHolderParent()
    {
        GameObject parent = new GameObject("AngleHolder");
        parent.transform.SetParent(transform.parent);
        parent.transform.position = transform.position;
        parent.transform.rotation = transform.rotation;
        transform.SetParent(parent.transform);
    }

    private void GenController()
    {
        _controller = gameObject.AddComponent<JointController>();

        if (useAdvancedSettings)
        {
            _controller.p = kP;
            _controller.i = kI;
            _controller.d = kD;
            _controller.max = max;
        }
        else
        {
            _controller.p = 0.25f;
            _controller.i = 0;
            _controller.d = 0.005f;
            _controller.max = 10;
        }
        
        _controller.iSat = 0;
        _controller.angular = true;
        _controller.driveAxis = new Vector3(1, 0, 0);
        _controller.joint = _joint;
        _controller.useNoWrap = useNoWrapPoint;
        _controller.noWrapAngle = noWrapAngle;
    }

    private void GenJoint()
    {
        _joint = gameObject.AddComponent<ConfigurableJoint>();
            
        _connectedBody = Utils.FindParentRB(gameObject);

        _joint.connectedBody = _connectedBody.GetComponent<Rigidbody>();
        _joint.xMotion = ConfigurableJointMotion.Locked;
        _joint.yMotion = ConfigurableJointMotion.Locked;
        _joint.zMotion = ConfigurableJointMotion.Locked;
        _joint.angularYMotion = ConfigurableJointMotion.Locked;
        _joint.angularZMotion = ConfigurableJointMotion.Locked;

        _joint.angularXMotion = ConfigurableJointMotion.Free;
        
        _drive.maximumForce = 8000;
        _drive.positionDamper = 100;
        _drive.positionSpring = 0;
        _drive.useAcceleration = false;
        _joint.angularXDrive = _drive;
    }

    private void GenRB()
    {
        _rigidbody = gameObject.AddComponent<Rigidbody>();
        _rigidbody.mass = armWeight;
        _rigidbody.interpolation = RigidbodyInterpolation.None;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
    }
}
