using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Util;

public class ModuleBehaviour : MonoBehaviour
{
    [HideInInspector] public float wheelDiameter;
    [HideInInspector] public float gearRatio;
    [HideInInspector] public float targetVelocity = 0;
    [HideInInspector] public float targetModuleAngle = 0;
    [HideInInspector] public float lateralFrictionMultiplier = 1;
    [HideInInspector] public float tractionCoefficient = 1.1f;

    private WheelBehaviour _wheelBehaviour;
    private DriveMotor     _driveMotor;
    [HideInInspector] public Rigidbody _rb;

    private float         _startingRotation;
    private GameObject    _wheelModel;
    private PIDController _pidController;

    void Start()
    {
        _pidController = new PIDController
        {
            proportionalGain = 1f,
            integralGain     = 0f,
            derivativeGain   = 0.005f,
            outputMax        =  12f,
            outputMin        = -12f
        };

        _wheelBehaviour = Utils.FindChild("Wheel", gameObject).AddComponent<WheelBehaviour>();
        _wheelBehaviour.wheelDiameter = wheelDiameter;

        _driveMotor           = gameObject.AddComponent<DriveMotor>();
        _driveMotor.gearRatio = gearRatio;

        _startingRotation = transform.localRotation.eulerAngles.y;
        _wheelModel       = Utils.FindChild("Model", _wheelBehaviour.gameObject);
    }

    void FixedUpdate()
    {
        if (_rb == null) return;

        _wheelBehaviour.wheelDiameter = wheelDiameter;
        _driveMotor.gearRatio         = gearRatio;

        float dt = Time.fixedDeltaTime;
        float r  = wheelDiameter * 0.5f;
        
        float targetRotation = Mathf.Repeat(targetModuleAngle - _startingRotation, 360f);
        float angleError     = targetRotation - _wheelBehaviour.transform.localEulerAngles.y;

        if (FMS.RobotState == RobotState.disabled)
            targetVelocity = 0f;

        Vector3 localVel          = _wheelBehaviour.transform
            .InverseTransformDirection(_rb.GetPointVelocity(_wheelBehaviour.transform.position));
        
        localVel.y = 0f;
        float chassisSurfaceSpeed = localVel.z;
        
        float realSpeedRPM = (chassisSurfaceSpeed / (Mathf.PI * wheelDiameter)) * 60f;

        float feedForward = targetVelocity * 18f;
        float pValue      = _pidController.UpdateLinear(dt, _driveMotor.motorSpeed, targetVelocity * 6000f);
        float alignFactor = (90f - Mathf.Clamp(Mathf.Abs(angleError), 0f, 90f)) / 90f;
        float voltage     = Mathf.Clamp(feedForward + pValue * alignFactor, -12f, 12f);

        _driveMotor.DriveSimUpdate(voltage, realSpeedRPM * gearRatio);
        float wheelSurfaceSpeed = (_driveMotor.motorSpeed / gearRatio / 60f)
                                  * (Mathf.PI * wheelDiameter); 
        
        float slipVelocity = wheelSurfaceSpeed - chassisSurfaceSpeed;

        float maxGrip  = _rb.mass * 9.81f * tractionCoefficient;
        float forceZ   = slipVelocity * 125f;
        float forceX   = localVel.x * -4f * _rb.mass * lateralFrictionMultiplier;

        Vector3 totalForce = new Vector3(forceX, 0f, forceZ);
        if (totalForce.sqrMagnitude > maxGrip * maxGrip)
            totalForce = totalForce.normalized * maxGrip;

        int contactCount = _wheelBehaviour.collisionPoints.Count;
        if (contactCount > 0)
        {
            for (int i = 0; i < contactCount; i++)
            {
                _rb.AddForceAtPosition(
                    (_wheelBehaviour.transform.forward * totalForce.z) / contactCount,
                    _wheelBehaviour.collisionPoints[i]);

                _rb.AddForceAtPosition(
                    (_wheelBehaviour.transform.right * totalForce.x) / contactCount,
                    _wheelBehaviour.collisionPoints[i]);
            }
        }

        if (FMS.RobotState == RobotState.enabled)
        {
            _wheelBehaviour.transform.localEulerAngles = Quaternion
                .Lerp(_wheelBehaviour.transform.localRotation,
                      Quaternion.Euler(0f, targetRotation, 0f),
                      360f * dt)
                .eulerAngles;

            _wheelModel.transform.Rotate(Vector3.right,
                (_driveMotor.motorSpeed / gearRatio) * dt);
        }
    }
}