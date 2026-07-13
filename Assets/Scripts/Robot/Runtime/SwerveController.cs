using UnityEngine;
using UnityEngine.InputSystem;
using Utilities;

namespace Robot.Runtime
{
    public class SwerveController : MonoBehaviour
    {

        //used by build frame
        private ModuleBehaviour[] _modules;
        [HideInInspector] public float gearRatio;
        [HideInInspector] public Rigidbody rb;

        public float wheelDiameter;
        //-=-=-=-=-=

        //begin visible section
        public bool fieldCentric;
        public bool reversed;
        public bool isRed;
        //end visible section

        //Settings
        private readonly float _velocityMp = 1;
        private readonly float _steerMp = 1;

        //control stuff
        private Vector2 _translateValue;
        private Vector2 _rotateValue;

        [SerializeField] private string actionMapName = "Robot";
        [SerializeField] private string driveActionName = "Drive";
        [SerializeField] private string rotateActionName = "Rotate";

        private PlayerInput _playerInput;
        private InputActionMap _inputActionMap;
        private InputAction _driveAction;
        private InputAction _rotateAction;

        private readonly string[] _moduleNames = new string[4];
    
        private readonly SwerveSetpoint[] _swerveSetpoints = new SwerveSetpoint[4];
        
        // Module indices
        private const int FlModule = 0;
        private const int FrModule = 1;
        private const int BLModule = 2;
        private const int BRModule = 3;
    
        private const float RadToDeg = 180f / Mathf.PI;

        private bool _inputsOveriden;
        private bool _steerOveriden;

        private bool _inputsOveridable;

        private float _length;
        private float _width;
        private float _radius;
    

        // Start is called before the first frame update
        void Start()
        {
            _playerInput = gameObject.GetComponent<PlayerInput>();

            if (_playerInput == null)
            {
                Debug.LogError($"{gameObject.name} is missing PlayerInput on the same GameObject as SwerveController.");
                enabled = false;
                return;
            }

            if (_playerInput.actions == null)
            {
                Debug.LogError($"{gameObject.name} PlayerInput has no Actions asset assigned.");
                enabled = false;
                return;
            }

            _inputActionMap = _playerInput.actions.FindActionMap(actionMapName);

            if (_inputActionMap == null)
            {
                Debug.LogError($"{gameObject.name} could not find action map '{actionMapName}'.");
                enabled = false;
                return;
            }

            _driveAction = _inputActionMap.FindAction(driveActionName);
            _rotateAction = _inputActionMap.FindAction(rotateActionName);

            if (_driveAction == null || _rotateAction == null)
            {
                Debug.LogError(
                    $"{gameObject.name} could not find '{driveActionName}' and/or '{rotateActionName}' in '{actionMapName}'."
                );
                enabled = false;
                return;
            }

            _inputActionMap.Enable();
            _driveAction.Enable();
            _rotateAction.Enable();

            _moduleNames[FlModule] = "lf";
            _moduleNames[FrModule] = "rf";
            _moduleNames[BLModule] = "lr";
            _moduleNames[BRModule] = "rr";
            _modules = new ModuleBehaviour[4];

            var driveTrain = Utils.FindChild("driveTrain", gameObject);

            for (int i = 0; i < _modules.Length; i++)
            {
                if (Utils.FindChild(_moduleNames[i], driveTrain).GetComponent<ModuleBehaviour>())
                {
                    _modules[i] = Utils.FindChild(_moduleNames[i], driveTrain).GetComponent<ModuleBehaviour>();
                    _modules[i].gearRatio = gearRatio;
                    _modules[i].wheelDiameter = (wheelDiameter + 0.01f) * 0.0254f;
                    _modules[i].rb = rb;
                }
            }

            _inputsOveriden = false;
        
            _length = Mathf.Abs(_modules[FlModule].transform.localPosition.z - 
                               _modules[BLModule].transform.localPosition.z);
            _width = Mathf.Abs(_modules[FlModule].transform.localPosition.x - 
                              _modules[FrModule].transform.localPosition.x);
            _radius = Mathf.Sqrt(_length * _length + _width * _width);
        }

        public void OverideInputs(float x, float y, float angle, bool disruptable = false)
        {
            _translateValue = new Vector2(x, y);
            _rotateValue = new Vector2(angle, 0);
            _inputsOveriden = true;
            _inputsOveridable = disruptable;
        }

        public void OverideSteer(float angle, bool disruptable = false)
        {
            _rotateValue = new Vector2(angle, 0);
            _steerOveriden = true;
            _inputsOveridable = disruptable;
        }

        // Update is called once per frame
        void FixedUpdate()
        {
            Vector2 driveInput2D = _driveAction?.ReadValue<Vector2>() ?? Vector2.zero;

            Vector2 rotateInput = _rotateAction?.ReadValue<Vector2>() ?? Vector2.zero;
            if (_inputsOveriden)
            {
                if (_inputsOveridable && driveInput2D.magnitude > 0.05f)
                {
                    _translateValue = driveInput2D;
                    _rotateValue = rotateInput;
                    _inputsOveriden = false;
                }
            }
            else if (_steerOveriden)
            {
                _translateValue = driveInput2D;

                if (_inputsOveridable && rotateInput.magnitude > 0.05f)
                {
                    _rotateValue = rotateInput;
                    _steerOveriden = false;
                }
            }
            else
            {
                _translateValue = driveInput2D;
                _rotateValue = rotateInput;
            }

            Vector3 driveInput = new Vector3(_translateValue.y, 0f, _translateValue.x);

            float angle;
            if (!isRed)
            {
                angle = transform.localRotation.eulerAngles.y + 270;
            }
            else
            {
                angle = transform.localRotation.eulerAngles.y + 90;
            }

            Vector3 fieldRelativeAngle = Quaternion.AngleAxis(angle, Vector3.up) * driveInput;

            float fwd;
            float str;

            if (fieldCentric || _inputsOveriden)
            {
                if (!reversed || _inputsOveriden)
                {
                    _inputsOveriden = false;

                    fwd = fieldRelativeAngle.x * _velocityMp;
                    str = fieldRelativeAngle.z * _velocityMp;
                }
                else
                {
                    fwd = -fieldRelativeAngle.x * _velocityMp;
                    str = -fieldRelativeAngle.z * _velocityMp;
                }
            }
            else
            {
                if (!reversed)
                {
                    fwd = driveInput.x * _velocityMp;
                    str = driveInput.z * _velocityMp;
                }
                else
                {
                    fwd = -driveInput.x * _velocityMp;
                    str = -driveInput.z * _velocityMp;
                }
            }

            float rcw = -_rotateValue.x * _steerMp;
            _steerOveriden = false;

            GenerateSwerveSetpoints(fwd, str, -rcw);

            _modules[FlModule].targetVelocity = _swerveSetpoints[FlModule].Velocity;
            _modules[BLModule].targetVelocity = _swerveSetpoints[BLModule].Velocity;
            _modules[FrModule].targetVelocity = _swerveSetpoints[FrModule].Velocity;
            _modules[BRModule].targetVelocity = _swerveSetpoints[BRModule].Velocity;

            _modules[FlModule].targetModuleAngle = _swerveSetpoints[FlModule].Angle;
            _modules[BLModule].targetModuleAngle = _swerveSetpoints[BLModule].Angle;
            _modules[FrModule].targetModuleAngle = _swerveSetpoints[FrModule].Angle;
            _modules[BRModule].targetModuleAngle = _swerveSetpoints[BRModule].Angle;
        }
    
        private void GenerateSwerveSetpoints(float fwd, float str, float rotation)
        {
            // Calculate wheelbase dimensions
            

            // Calculate wheel vectors
            var a = str - rotation * (_length / _radius);
            var b = str + rotation * (_length / _radius);
            var c = fwd - rotation * (_width / _radius);
            var d = fwd + rotation * (_width / _radius);

            // Calculate speeds and angles for each module
            CalculateModuleSetpoint(FrModule, b, c);
            CalculateModuleSetpoint(FlModule, b, d);
            CalculateModuleSetpoint(BLModule, a, d);
            CalculateModuleSetpoint(BRModule, a, c);
        }
    
        private void CalculateModuleSetpoint(int moduleIndex, float x, float y)
        {
            var speed = Mathf.Sqrt(x * x + y * y);
            _swerveSetpoints[moduleIndex].Velocity = speed;
            
            // Only update angle if there's movement
            if (speed > 0f)
            {
                _swerveSetpoints[moduleIndex].Angle = Mathf.Atan2(x, y) * RadToDeg;
            }
        }
    
        private struct SwerveSetpoint
        {
            public float Angle;
            public float Velocity;
        }
    }
}
