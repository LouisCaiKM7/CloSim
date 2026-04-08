using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Util;

public class ModuleBehaviour : MonoBehaviour
{
    //input settigns
    /// <summary>
    /// The diameter of the swerve wheel
    /// </summary>
    [HideInInspector] public float wheelDiameter;
    /// <summary>
    /// The gear ratio of the motor to drive wheel (positive reduction)
    /// </summary>
    [HideInInspector] public float gearRatio;
    /// <summary>
    /// The target velocity for the drive contorller
    /// </summary>
    [HideInInspector] public float targetVelocity = 0;
    /// <summary>
    /// The target angle to finish the modules at.
    /// </summary>
    [HideInInspector] public float targetModuleAngle = 0;
    
    [HideInInspector] public float lateralFrictionMultiplier = 1;
    
    private WheelBehaviour _wheelBehaviour;
    private DriveMotor _driveMotor;
    [HideInInspector] public Rigidbody _rb;
    private float _startingRotation;
    private GameObject _wheelModel;
    private PIDController _pidController;

    // Start is called before the first frame update
    void Start()
    {
        _pidController = new PIDController
        {
            proportionalGain = 1f,
            integralGain = 0,
            derivativeGain = 0.005f,
            outputMax = 12,
            outputMin = -12
        };

        //add wheel behaviour to the correct object
        _wheelBehaviour = Utils.FindChild("Wheel", gameObject).AddComponent<WheelBehaviour>();
        
        _wheelBehaviour.wheelDiameter = wheelDiameter;
       
        //add drive motor sim to object
        _driveMotor = gameObject.AddComponent<DriveMotor>();
        _driveMotor.gearRatio = gearRatio;
       
        _startingRotation = transform.localRotation.eulerAngles.y;

        _wheelModel = Utils.FindChild("Model", _wheelBehaviour.gameObject);
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if(_rb == null) return;
        _wheelBehaviour.wheelDiameter = wheelDiameter;
        _driveMotor.gearRatio = gearRatio;
        
        float targetRotation = Mathf.Repeat(targetModuleAngle-_startingRotation, 360);
        float realSpeed = (_wheelBehaviour.transform.InverseTransformDirection(_rb.GetPointVelocity(_wheelBehaviour.transform.position)).z / (Mathf.PI * wheelDiameter)) * 60;
        
        if (FMS.RobotState == RobotState.disabled)
        {
            targetVelocity = 0;
        }
        
        float feedForward = targetVelocity * 18; //Kv * target = voltage
        float pValue = _pidController.UpdateLinear(Time.fixedDeltaTime, _driveMotor.motorSpeed, targetVelocity * 6000);
        float angleError = targetRotation - _wheelBehaviour.transform.localEulerAngles.y;
        float voltage = Mathf.Clamp(feedForward + pValue * ((90 - Mathf.Clamp(Mathf.Abs(angleError),0,90))/90), -12, 12);
        
        float maxGrip = _rb.mass * 9.81f * 1.1f; 
        Vector3 localVel = _wheelBehaviour.transform.InverseTransformDirection(_rb.GetPointVelocity(_wheelBehaviour.transform.position));
        
        float motorTorqueForce = ((Mathf.PI * wheelDiameter * (_driveMotor.DriveSimUpdate(voltage, realSpeed * gearRatio) / gearRatio) / 60));
        float slipZ = motorTorqueForce - localVel.z;

        float forceZ = (slipZ * 125);

        float forceX = localVel.x * -4f * _rb.mass * lateralFrictionMultiplier;

        Vector3 totalForce = new Vector3(forceX, 0, forceZ);
        if (totalForce.magnitude > maxGrip)
        {
            totalForce = totalForce.normalized * maxGrip;
        }

        int contactCount = _wheelBehaviour.collisionPoints.Count;
        if (contactCount > 0)
        {
            for (int i = 0; i < contactCount; i++)
            {
                // Drive Force
                _rb.AddForceAtPosition(
                    (_wheelBehaviour.transform.forward * totalForce.z) / contactCount,
                    _wheelBehaviour.collisionPoints[i]);

                // Side Friction Force
                _rb.AddForceAtPosition(
                    (_wheelBehaviour.transform.right * totalForce.x) / contactCount, 
                    _wheelBehaviour.collisionPoints[i]);
            }
        }


        if (FMS.RobotState == RobotState.enabled)
        {
            _wheelBehaviour.transform.localEulerAngles = Quaternion.Lerp(_wheelBehaviour.transform.localRotation,
                Quaternion.Euler(0, targetRotation, 0), 360 * Time.deltaTime).eulerAngles;

            _wheelModel.transform.Rotate(Vector3.right, realSpeed * Time.deltaTime);
        }

    }
}
