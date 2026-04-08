using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Util;


public class SwerveController : MonoBehaviour
{

    //used by build frame
    [HideInInspector] private ModuleBehaviour[] _modules;
    [HideInInspector] public float gearRatio;
    [HideInInspector] public Rigidbody rb;

    public float wheelDiameter;
    //-=-=-=-=-=

    //begin visible section
    public bool fieldCentric = false;
    public bool reversed = false;
    public bool isRed = false;
    //end visible section

    //Settings
    private float velocityMp = 1;
    private float steerMp = 1;

    //control stuff
    private Vector2 _translateValue;
    private Vector2 _rotateValue;

    private PlayerInput _playerInput;
    private InputActionMap _inputActionMap;

    private InputAction _translateAction;
    private InputAction _rotateAction;

    private string[] _moduleNames = new string[4];
    
    private readonly SwerveSetpoint[] _swerveSetpoints = new SwerveSetpoint[4];
        
    // Module indices
    private const int FL_MODULE = 0;
    private const int FR_MODULE = 1;
    private const int BL_MODULE = 2;
    private const int BR_MODULE = 3;
    
    private const float RAD_TO_DEG = 180f / Mathf.PI;

    private bool inputsOveriden;
    private bool steerOveriden;

    private bool inputsOveridable;

    private float length;
    private float width;
    private float radius;
    

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

        _translateAction = _playerInput.actions.FindAction("LeftStick");
        _rotateAction = _playerInput.actions.FindAction("RightStick");

        if (_translateAction == null || _rotateAction == null)
        {
            Debug.LogError($"{gameObject.name} could not find LeftStick and/or RightStick actions in the assigned Input Actions asset.");
            enabled = false;
            return;
        }

        _translateAction.Enable();
        _rotateAction.Enable();

        _playerInput = gameObject.GetComponent<PlayerInput>();

        _moduleNames[FL_MODULE] = "lf";
        _moduleNames[FR_MODULE] = "rf";
        _moduleNames[BL_MODULE] = "lr";
        _moduleNames[BR_MODULE] = "rr";
        _modules = new ModuleBehaviour[4];

        var driveTrain = Utils.FindChild("driveTrain", gameObject);

        for (int i = 0; i < _modules.Length; i++)
        {
            if (Utils.FindChild(_moduleNames[i], driveTrain).GetComponent<ModuleBehaviour>())
            {
                _modules[i] = Utils.FindChild(_moduleNames[i], driveTrain).GetComponent<ModuleBehaviour>();
                _modules[i].gearRatio = gearRatio;
                _modules[i].wheelDiameter = (wheelDiameter + 0.01f) * 0.0254f;
                _modules[i]._rb = rb;
            }
        }

        inputsOveriden = false;
        
        length = Mathf.Abs(_modules[FL_MODULE].transform.localPosition.z - 
                           _modules[BL_MODULE].transform.localPosition.z);
        width = Mathf.Abs(_modules[FL_MODULE].transform.localPosition.x - 
                          _modules[FR_MODULE].transform.localPosition.x);
        radius = Mathf.Sqrt(length * length + width * width);
    }

    public void overideInputs(float x, float y, float angle, bool disruptable = false)
    {
        _translateValue = new Vector2(x, y);
        _rotateValue = new Vector2(angle, 0);
        inputsOveriden = true;
        inputsOveridable = disruptable;
    }

    public void OverideSteer(float angle, bool disruptable = false)
    {
        _rotateValue = new Vector2(angle, 0);
        steerOveriden = true;
        inputsOveridable = disruptable;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        //update controls
        if (_translateAction.ReadValue<Vector2>().magnitude > 0.05f && inputsOveridable && !steerOveriden)
        {
            _translateValue = _translateAction.ReadValue<Vector2>();
            _rotateValue = _rotateAction.ReadValue<Vector2>();
            inputsOveriden = false;
        } else if (_rotateAction.ReadValue<Vector2>().magnitude > 0.05f && inputsOveridable && steerOveriden)
        {
            _translateValue = _translateAction.ReadValue<Vector2>();
            _rotateValue = _rotateAction.ReadValue<Vector2>();
            steerOveriden = false;
        }else if (steerOveriden)
        {
            _translateValue = _translateAction.ReadValue<Vector2>();
        }
        else if (!inputsOveriden || !steerOveriden)
        {
            _translateValue = _translateAction.ReadValue<Vector2>();
            _rotateValue = _rotateAction.ReadValue<Vector2>();
        }

        //rotate input to match alliance and scheme
        Vector3 driveInput = new Vector3(_translateValue.y, 0, _translateValue.x);

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

        float fwd, str;


        if (fieldCentric || inputsOveriden)
        {

            if (!reversed || inputsOveriden)
            {
                inputsOveriden = false;

                fwd = fieldRelativeAngle.x * velocityMp;

                str = fieldRelativeAngle.z * velocityMp;
            }
            else
            {
                fwd = -fieldRelativeAngle.x * velocityMp;

                str = -fieldRelativeAngle.z * velocityMp;
            }
        }
        else
        {
            if (!reversed)
            {
                fwd = driveInput.x * velocityMp;

                str = driveInput.z * velocityMp;
            }
            else
            {
                fwd = -driveInput.x * velocityMp;

                str = -driveInput.z * velocityMp;
            }
        }

        // Swerve Math
        var RCW = -_rotateValue.x * steerMp;
        steerOveriden = false;

        GenerateSwerveSetpoints(fwd, str, -RCW);

        //assign outputs
        _modules[FL_MODULE].targetVelocity = _swerveSetpoints[FL_MODULE].Velocity;
        _modules[BL_MODULE].targetVelocity = _swerveSetpoints[BL_MODULE].Velocity;
        _modules[FR_MODULE].targetVelocity = _swerveSetpoints[FR_MODULE].Velocity;
        _modules[BR_MODULE].targetVelocity = _swerveSetpoints[BR_MODULE].Velocity;

        _modules[FL_MODULE].targetModuleAngle = _swerveSetpoints[FL_MODULE].Angle;
        _modules[BL_MODULE].targetModuleAngle = _swerveSetpoints[BL_MODULE].Angle;
        _modules[FR_MODULE].targetModuleAngle = _swerveSetpoints[FR_MODULE].Angle;
        _modules[BR_MODULE].targetModuleAngle = _swerveSetpoints[BR_MODULE].Angle;
    }
    
    private void GenerateSwerveSetpoints(float fwd, float str, float rotation)
    {
        // Calculate wheelbase dimensions
            

        // Calculate wheel vectors
        var a = str - rotation * (length / radius);
        var b = str + rotation * (length / radius);
        var c = fwd - rotation * (width / radius);
        var d = fwd + rotation * (width / radius);

        // Calculate speeds and angles for each module
        CalculateModuleSetpoint(FR_MODULE, b, c);
        CalculateModuleSetpoint(FL_MODULE, b, d);
        CalculateModuleSetpoint(BL_MODULE, a, d);
        CalculateModuleSetpoint(BR_MODULE, a, c);
    }
    
    private void CalculateModuleSetpoint(int moduleIndex, float x, float y)
    {
        var speed = Mathf.Sqrt(x * x + y * y);
        _swerveSetpoints[moduleIndex].Velocity = speed;
            
        // Only update angle if there's movement
        if (speed > 0f)
        {
            _swerveSetpoints[moduleIndex].Angle = Mathf.Atan2(x, y) * RAD_TO_DEG;
        }
    }
    
    private struct SwerveSetpoint
    {
        public float Angle;
        public float Velocity;
    }
}
