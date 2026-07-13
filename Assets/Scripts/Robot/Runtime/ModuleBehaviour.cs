using Field.Core;
using UnityEngine;
using UnityEngine.Serialization;
using Utilities;

namespace Robot.Runtime
{
    public class ModuleBehaviour : MonoBehaviour
    {
        [HideInInspector] public float wheelDiameter;
        [HideInInspector] public float gearRatio;
        [HideInInspector] public float targetVelocity;
        [HideInInspector] public float targetModuleAngle;
        [HideInInspector] public float lateralFrictionMultiplier = 1;
        [HideInInspector] public float tractionCoefficient = 1.1f;

        private WheelBehaviour _wheelBehaviour;
        private DriveMotor     _driveMotor;
        [FormerlySerializedAs("_rb")] [HideInInspector] public Rigidbody rb;

        private float         _startingRotation;
        private GameObject    _wheelModel;
        private PidController _pidController;

        void Start()
        {
            _pidController = new PidController
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
            if (rb == null) return;

            _wheelBehaviour.wheelDiameter = wheelDiameter;
            _driveMotor.gearRatio         = gearRatio;

            float dt = Time.fixedDeltaTime;

            float targetRotation = Mathf.Repeat(targetModuleAngle - _startingRotation, 360f);
            float angleError     = targetRotation - _wheelBehaviour.transform.localEulerAngles.y;

            if (Fms.RobotState == RobotState.Disabled)
                targetVelocity = 0f;

            Vector3 localVel          = _wheelBehaviour.transform
                .InverseTransformDirection(rb.GetPointVelocity(_wheelBehaviour.transform.position));
        
            localVel.y = 0f;
            float chassisSurfaceSpeed = localVel.z;
        
            float realSpeedRpm = (chassisSurfaceSpeed / (Mathf.PI * wheelDiameter)) * 60f;

            float feedForward = targetVelocity * 18f;
            float pValue      = _pidController.UpdateLinear(dt, _driveMotor.motorSpeed, targetVelocity * 6000f);
            float alignFactor = (90f - Mathf.Clamp(Mathf.Abs(angleError), 0f, 90f)) / 90f;
            float voltage     = Mathf.Clamp(feedForward + pValue * alignFactor, -12f, 12f);

            _driveMotor.DriveSimUpdate(voltage, realSpeedRpm * gearRatio);
            float wheelSurfaceSpeed = (_driveMotor.motorSpeed / gearRatio / 60f)
                                      * (Mathf.PI * wheelDiameter); 
        
            float slipVelocity = wheelSurfaceSpeed - chassisSurfaceSpeed;

            float maxGrip  = rb.mass * 9.81f * tractionCoefficient;
            float forceZ   = slipVelocity * 125f;
            float forceX   = localVel.x * -4f * rb.mass * lateralFrictionMultiplier;

            Vector3 totalForce = new Vector3(forceX, 0f, forceZ);
            if (totalForce.sqrMagnitude > maxGrip * maxGrip)
                totalForce = totalForce.normalized * maxGrip;

            int contactCount = _wheelBehaviour.collisionPoints.Count;
            if (contactCount > 0)
            {
                for (int i = 0; i < contactCount; i++)
                {
                    rb.AddForceAtPosition(
                        (_wheelBehaviour.transform.forward * totalForce.z) / contactCount,
                        _wheelBehaviour.collisionPoints[i]);

                    rb.AddForceAtPosition(
                        (_wheelBehaviour.transform.right * totalForce.x) / contactCount,
                        _wheelBehaviour.collisionPoints[i]);
                }
            }

            if (Fms.RobotState == RobotState.Enabled)
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
}