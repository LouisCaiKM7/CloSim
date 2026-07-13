using System;
using Core;
using Field.Core;
using MyBox;
using Robot.Runtime;
using UnityEngine;
using Utilities;

namespace Robot.Builders
{
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

        [SerializeField] private bool useNoWrapPoint;
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

        private float _scaleModifier;

        private bool _armFrozenForDisable;
        // Start is called before the first frame update
        void Start()
        {
            if (Application.isPlaying)
            {
                CreateAngleHolderParent();
                GenRb();
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
                    _scaleModifier = 0.0254f;
                    break;
                case Units.Centimeter:
                    _scaleModifier = 0.01f;
                    break;
                case Units.Meter:
                    _scaleModifier = 1.0f;
                    break;
                case Units.Millimeter:
                    _scaleModifier = 0.001f;
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
                bool disabled = Fms.RobotState == RobotState.Disabled;
                SetArmDisabledHold(disabled);

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
            _scaleModifier = 0.0254f;
        
            if (_tubingObject == null)
            {
                var loadedTubes = Resources.LoadAll<GameObject>("Parts/Tubing");
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
                        _parts[0].loadedPartLocation = new Vector3(0,0, (length/2) * _scaleModifier);
                        _parts[0].loadedPartRotation = Quaternion.Euler(0, 0, 0);
                        _parts[0].loadedPartScale = new Vector3(1, 1, length * _scaleModifier);
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
                        _parts[0].loadedPartLocation = new Vector3(((width/2) - 0.5f) * _scaleModifier,0, (length/2) * _scaleModifier);
                        _parts[0].loadedPartRotation = Quaternion.Euler(0, 0, 0);
                        _parts[0].loadedPartScale = new Vector3(1, 1, length * _scaleModifier);
                    
                        _parts[1].loadedPartLocation = new Vector3(((-width/2) + 0.5f) * _scaleModifier,0, (length/2) * _scaleModifier);
                        _parts[1].loadedPartRotation = Quaternion.Euler(0, 0, 0);
                        _parts[1].loadedPartScale = new Vector3(1, 1, length * _scaleModifier);
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
                        _parts[0].loadedPartLocation = new Vector3(0,0, (length/2) * _scaleModifier);
                        _parts[0].loadedPartRotation = Quaternion.Euler(0, 0, 0);
                        _parts[0].loadedPartScale = new Vector3(1, 1, length * _scaleModifier);
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
                _parts[0].part = _tubingObject[0];
                _parts[0].partName = "DoubleL";
                _parts[0].loadedPartLocation = new Vector3(((width/2* _scaleModifier) - (0.5f * 0.0254f)) ,0, (length/2* _scaleModifier) );
                _parts[0].loadedPartRotation = Quaternion.Euler(0, 0, 0);
                _parts[0].loadedPartScale = new Vector3(1, 1, length * _scaleModifier);
            }

            if (!_parts[1])
            {
                _parts[1] = _modelObject.AddComponent<GeneratePart>();
                _parts[1].part = _tubingObject[0];
                _parts[1].partName = "DoubleR";
                _parts[1].loadedPartLocation = new Vector3(((-width/2 * _scaleModifier) + (0.5f * 0.0254f)),0, (length/2) * _scaleModifier);
                _parts[1].loadedPartRotation = Quaternion.Euler(0, 0, 0);
                _parts[1].loadedPartScale = new Vector3(1, 1, length * _scaleModifier);
            }
        }
    
        private void CreateSingleArm()
        {
            _parts = CheckTubes(_modelObject, 1);

            if (_parts[0] == null)
            {
                _parts[0] = _modelObject.AddComponent<GeneratePart>();
                _parts[0].part = _tubingObject[0];
                _parts[0].partName = "Single";
                _parts[0].loadedPartLocation = new Vector3(0,0, (length/2) * _scaleModifier);
                _parts[0].loadedPartRotation = Quaternion.Euler(0, 0, 0);
                _parts[0].loadedPartScale = new Vector3(1, 1, length * _scaleModifier);
            }
        }
    
        private void CreateSingleTwoByTwoArm()
        {
            _parts = CheckTubes(_modelObject, 1);

            if (_parts[0] == null)
            {
                _parts[0] = _modelObject.AddComponent<GeneratePart>();
                _parts[0].part = _tubingObject[1];
                _parts[0].partName = "SingleTwo";
                _parts[0].loadedPartLocation = new Vector3(0,0, (length/2) * _scaleModifier);
                _parts[0].loadedPartRotation = Quaternion.Euler(0, 0, 0);
                _parts[0].loadedPartScale = new Vector3(1, 1, length * _scaleModifier);
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
                        if (t.partName == "Single")
                        {
                            filtered[0] = t;
                        }
                        break;
                    case ArmModel.SplitParallel:
                        if (t.partName == "DoubleL")
                        {
                            filtered[0] = t;
                        } 
                        else if (t.partName == "DoubleR")
                        {
                            filtered[1] = t;
                        }
                        break;
                    case ArmModel.SingleTwoByTwo:
                        if (t.partName == "SingleTwo")
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

        private void SetArmDisabledHold(bool disabled)
        {
            if (_rigidbody == null)
                return;

            if (disabled)
            {
                _rigidbody.velocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = false;

                if (_joint != null)
                    _joint.targetAngularVelocity = Vector3.zero;

                if (_controller != null && _controller.enabled)
                    _controller.enabled = false;

                _armFrozenForDisable = true;
                return;
            }

            if (!_armFrozenForDisable)
                return;

            _armFrozenForDisable = false;
            _rigidbody.velocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.useGravity = true;

            if (_joint != null)
                _joint.targetAngularVelocity = Vector3.zero;

            if (_controller != null && !_controller.enabled)
                _controller.enabled = true;
        }

        private void GenJoint()
        {
            _joint = gameObject.AddComponent<ConfigurableJoint>();
            
            _connectedBody = Utils.FindParentRb(gameObject);

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

        private void GenRb()
        {
            _rigidbody = gameObject.AddComponent<Rigidbody>();
            _rigidbody.mass = armWeight;
            _rigidbody.interpolation = RigidbodyInterpolation.None;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }
    }
}
