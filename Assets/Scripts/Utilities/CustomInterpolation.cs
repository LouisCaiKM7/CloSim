using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Utilities
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class CustomInterpolation : MonoBehaviour
    {
        private Rigidbody _rb;
    
        [Header("Target State")]
        public Vector3 targetPosition;
        public Quaternion targetRotation;

        [Header("Control Flags")]
        public bool isInterpolatingPosition = true;
        public bool isInterpolatingRotation = true;
        public bool useFixedUpdate = true;

        [Header("Spring-Damper Settings")]
        [Tooltip("P-Term: Higher value means faster convergence.")]
        public float positionStiffness = 10f;
        [Tooltip("D-Term: Higher value means more resistance to movement (damping).")]
        public float positionDamping = 2f;
        public float rotationStiffness = 10f;
        public float rotationDamping = 2f;

        private Vector3 _velocity;
        private Vector3 _angularVelocity;
        private Joint _joint;

        [Header("Kalman Filter Settings - Position")]
        [Tooltip("Q: Process noise (model uncertainty).")]
        public float positionProcessNoise = 0.02f;
        [Tooltip("R: Measurement noise (sensor uncertainty).")]
        public float positionMeasurementNoise = 0.1f;

        [Header("Kalman Filter Settings - Rotation")]
        public float rotationProcessNoise = 0.02f;
        public float rotationMeasurementNoise = 0.1f;

        // Kalman Filter state - Position (3x3)
        private Vector3 _positionEstimate;
        private Matrix3X3 _positionCovariance;
    
        // Kalman Filter state - Rotation (4x4)
        private Vector4 _rotationEstimate;
        private Matrix4X4Custom _rotationCovariance;

        // Cached values for GC reduction
        private const float PositionThreshold = 0.01f;
        private const float RotationThreshold = 0.1f;
        private const float SingularityEpsilon = 1e-6f;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
            {
                Debug.LogError("Rigidbody component not found!");
                enabled = false;
                return;
            }

            _rb.interpolation = RigidbodyInterpolation.None;
            targetPosition = _rb.position;
            targetRotation = _rb.rotation;
            _velocity = Vector3.zero;
            _angularVelocity = Vector3.zero;

            _joint = GetComponent<Joint>();

            // Initialize Kalman Filters
            _positionEstimate = _rb.position;
            _positionCovariance = Matrix3X3.Identity();

            _rotationEstimate = QuaternionToVector4(_rb.rotation);
            _rotationCovariance = Matrix4X4Custom.Identity();
        }

        void FixedUpdate()
        {
            if (useFixedUpdate)
            {
                Interpolate(Time.fixedDeltaTime);
            }
        }

        void Update()
        {
            if (!useFixedUpdate)
            {
                Interpolate(Time.deltaTime);
            }
        }
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Interpolate(float deltaTime)
        {
            if (isInterpolatingPosition)
            {
                InterpolatePosition(deltaTime);
            }
            if (isInterpolatingRotation)
            {
                InterpolateRotation(deltaTime);
            }
        }

        private void InterpolatePosition(float deltaTime)
        {
            // Get target position with joint offset
            Vector3 targetPosWithOffset = targetPosition;
            if (_joint != null && _joint.connectedBody != null)
            {
                targetPosWithOffset = _joint.connectedBody.transform.TransformPoint(_joint.connectedAnchor);
            }

            // Spring-Damper (P-D Control)
            Vector3 displacement = targetPosWithOffset - _positionEstimate;
            Vector3 force = displacement * positionStiffness;
            Vector3 damping = _velocity * positionDamping;
            Vector3 netForce = force - damping;

            _velocity += netForce * deltaTime;
        
            // Prediction Step
            Vector3 predictedPosition = _positionEstimate + _velocity * deltaTime;
        
            float processNoise = positionProcessNoise * deltaTime;
            _positionCovariance.AddScaledIdentity(processNoise);

            // Measurement Update
            Vector3 innovation = Vector3.zero; // predictedPosition - predictedPosition = 0
        
            // Innovation Covariance (S = P + R)
            Matrix3X3 innovationCov = _positionCovariance;
            innovationCov.AddScaledIdentity(positionMeasurementNoise);
        
            // Kalman Gain (K = P * S^-1)
            Matrix3X3 invInnovationCov = innovationCov.Invert();
            Matrix3X3 kalmanGain = _positionCovariance.Multiply(invInnovationCov);

            // Update Estimate
            _positionEstimate = predictedPosition + kalmanGain.MultiplyVector(innovation);
        
            // Update Covariance (P = (I - K) * P)
            Matrix3X3 identity = Matrix3X3.Identity();
            identity.Subtract(kalmanGain);
            _positionCovariance = identity.Multiply(_positionCovariance);

            _rb.MovePosition(_positionEstimate);

            // Stop condition
            float distSqr = (_positionEstimate - targetPosWithOffset).sqrMagnitude;
            if (distSqr < PositionThreshold * PositionThreshold)
            {
                _rb.MovePosition(targetPosWithOffset);
                isInterpolatingPosition = false;
                _velocity = Vector3.zero;
            }
        }

        private void InterpolateRotation(float deltaTime)
        {
            Quaternion targetRotWithOffset = targetRotation;
            if (_joint != null && _joint.connectedBody != null)
            {
                targetRotWithOffset = _joint.connectedBody.transform.rotation;
            }

            // Spring-Damper (P-D Control)
            Quaternion currentRotEstimate = Vector4ToQuaternion(_rotationEstimate);
            Quaternion rotationDifference = targetRotWithOffset * Quaternion.Inverse(currentRotEstimate);
            rotationDifference.ToAngleAxis(out float angle, out Vector3 axis);

            // Shortest path
            if (angle > 180f) angle -= 360f;

            Vector3 torque = axis * (angle * rotationStiffness);
            Vector3 damping = _angularVelocity * rotationDamping;
            Vector3 netTorque = torque - damping;

            _angularVelocity += netTorque * deltaTime;
        
            // Prediction
            float angularMagnitude = _angularVelocity.magnitude;
            Quaternion deltaRotation = (angularMagnitude > SingularityEpsilon) 
                ? Quaternion.AngleAxis(angularMagnitude * deltaTime, _angularVelocity / angularMagnitude)
                : Quaternion.identity;
            
            Quaternion predictedRotationPd = currentRotEstimate * deltaRotation;
            // Correct quaternion prediction: multiply, then convert to Vector4
            Vector4 predictedRotationV4 = QuaternionToVector4(predictedRotationPd);
        
            float processNoise = rotationProcessNoise * deltaTime;
            _rotationCovariance.AddScaledIdentity(processNoise);

            // Measurement Update
            Vector4 measurement = QuaternionToVector4(predictedRotationPd);
            Vector4 innovation = measurement - predictedRotationV4;

            // Innovation Covariance
            Matrix4X4Custom innovationCov = _rotationCovariance;
            innovationCov.AddScaledIdentity(rotationMeasurementNoise);

            // Kalman Gain
            Matrix4X4Custom invInnovationCov = innovationCov.Invert();
            Matrix4X4Custom kalmanGain = _rotationCovariance.Multiply(invInnovationCov);

            // Update Estimate
            _rotationEstimate = predictedRotationV4 + kalmanGain.MultiplyVector(innovation);
        
            // Update Covariance
            Matrix4X4Custom identity = Matrix4X4Custom.Identity();
            identity.Subtract(kalmanGain);
            _rotationCovariance = identity.Multiply(_rotationCovariance);
        
            // Apply filtered rotation
            Quaternion finalRotation = Vector4ToQuaternion(_rotationEstimate).normalized;
            _rb.MoveRotation(finalRotation);
            _rotationEstimate = QuaternionToVector4(finalRotation);

            // Stop condition
            if (Quaternion.Angle(finalRotation, targetRotWithOffset) < RotationThreshold)
            {
                _rb.MoveRotation(targetRotWithOffset);
                isInterpolatingRotation = false;
                _angularVelocity = Vector3.zero;
            }
        }

        public void MoveToPosition(Vector3 newPosition)
        {
            targetPosition = newPosition;
            isInterpolatingPosition = true;
        }

        public void RotateToRotation(Quaternion newRotation)
        {
            targetRotation = newRotation;
            isInterpolatingRotation = true;
        }

        public void MoveToPositionAndRotation(Vector3 newPosition, Quaternion newRotation)
        {
            targetPosition = newPosition;
            targetRotation = newRotation;
            isInterpolatingPosition = true;
            isInterpolatingRotation = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInterpolating() => isInterpolatingPosition || isInterpolatingRotation;

        public void StopInterpolation()
        {
            isInterpolatingPosition = false;
            isInterpolatingRotation = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector4 QuaternionToVector4(Quaternion q) => new Vector4(q.x, q.y, q.z, q.w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Quaternion Vector4ToQuaternion(Vector4 v) => new Quaternion(v.x, v.y, v.z, v.w);

        // Optimized 3x3 Matrix struct (value type, no GC allocation)
        private struct Matrix3X3
        {
            // Row-major storage
            private float _m00, _m01, _m02;
            private float _m10, _m11, _m12;
            private float _m20, _m21, _m22;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Matrix3X3 Identity()
            {
                return new Matrix3X3
                {
                    _m00 = 1f, _m01 = 0f, _m02 = 0f,
                    _m10 = 0f, _m11 = 1f, _m12 = 0f,
                    _m20 = 0f, _m21 = 0f, _m22 = 1f
                };
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddScaledIdentity(float scale)
            {
                _m00 += scale;
                _m11 += scale;
                _m22 += scale;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Subtract(Matrix3X3 other)
            {
                _m00 -= other._m00; _m01 -= other._m01; _m02 -= other._m02;
                _m10 -= other._m10; _m11 -= other._m11; _m12 -= other._m12;
                _m20 -= other._m20; _m21 -= other._m21; _m22 -= other._m22;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Matrix3X3 Multiply(Matrix3X3 b)
            {
                return new Matrix3X3
                {
                    _m00 = _m00 * b._m00 + _m01 * b._m10 + _m02 * b._m20,
                    _m01 = _m00 * b._m01 + _m01 * b._m11 + _m02 * b._m21,
                    _m02 = _m00 * b._m02 + _m01 * b._m12 + _m02 * b._m22,
                
                    _m10 = _m10 * b._m00 + _m11 * b._m10 + _m12 * b._m20,
                    _m11 = _m10 * b._m01 + _m11 * b._m11 + _m12 * b._m21,
                    _m12 = _m10 * b._m02 + _m11 * b._m12 + _m12 * b._m22,
                
                    _m20 = _m20 * b._m00 + _m21 * b._m10 + _m22 * b._m20,
                    _m21 = _m20 * b._m01 + _m21 * b._m11 + _m22 * b._m21,
                    _m22 = _m20 * b._m02 + _m21 * b._m12 + _m22 * b._m22
                };
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vector3 MultiplyVector(Vector3 v)
            {
                return new Vector3(
                    _m00 * v.x + _m01 * v.y + _m02 * v.z,
                    _m10 * v.x + _m11 * v.y + _m12 * v.z,
                    _m20 * v.x + _m21 * v.y + _m22 * v.z
                );
            }

            public Matrix3X3 Invert()
            {
                // Compute determinant
                float det = _m00 * (_m11 * _m22 - _m12 * _m21) -
                            _m01 * (_m10 * _m22 - _m12 * _m20) +
                            _m02 * (_m10 * _m21 - _m11 * _m20);

                if (Mathf.Abs(det) < SingularityEpsilon)
                {
                    throw new InvalidOperationException("Matrix is singular");
                }

                float invDet = 1f / det;

                return new Matrix3X3
                {
                    _m00 = (_m11 * _m22 - _m12 * _m21) * invDet,
                    _m01 = (_m02 * _m21 - _m01 * _m22) * invDet,
                    _m02 = (_m01 * _m12 - _m02 * _m11) * invDet,
                
                    _m10 = (_m12 * _m20 - _m10 * _m22) * invDet,
                    _m11 = (_m00 * _m22 - _m02 * _m20) * invDet,
                    _m12 = (_m02 * _m10 - _m00 * _m12) * invDet,
                
                    _m20 = (_m10 * _m21 - _m11 * _m20) * invDet,
                    _m21 = (_m01 * _m20 - _m00 * _m21) * invDet,
                    _m22 = (_m00 * _m11 - _m01 * _m10) * invDet
                };
            }
        }

        // Optimized 4x4 Matrix struct for rotation Kalman filter
        private struct Matrix4X4Custom
        {
            private float _m00, _m01, _m02, _m03;
            private float _m10, _m11, _m12, _m13;
            private float _m20, _m21, _m22, _m23;
            private float _m30, _m31, _m32, _m33;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Matrix4X4Custom Identity()
            {
                return new Matrix4X4Custom
                {
                    _m00 = 1f, _m11 = 1f, _m22 = 1f, _m33 = 1f
                };
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddScaledIdentity(float scale)
            {
                _m00 += scale; _m11 += scale; _m22 += scale; _m33 += scale;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Subtract(Matrix4X4Custom other)
            {
                _m00 -= other._m00; _m01 -= other._m01; _m02 -= other._m02; _m03 -= other._m03;
                _m10 -= other._m10; _m11 -= other._m11; _m12 -= other._m12; _m13 -= other._m13;
                _m20 -= other._m20; _m21 -= other._m21; _m22 -= other._m22; _m23 -= other._m23;
                _m30 -= other._m30; _m31 -= other._m31; _m32 -= other._m32; _m33 -= other._m33;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Matrix4X4Custom Multiply(Matrix4X4Custom b)
            {
                return new Matrix4X4Custom
                {
                    _m00 = _m00*b._m00 + _m01*b._m10 + _m02*b._m20 + _m03*b._m30,
                    _m01 = _m00*b._m01 + _m01*b._m11 + _m02*b._m21 + _m03*b._m31,
                    _m02 = _m00*b._m02 + _m01*b._m12 + _m02*b._m22 + _m03*b._m32,
                    _m03 = _m00*b._m03 + _m01*b._m13 + _m02*b._m23 + _m03*b._m33,
                
                    _m10 = _m10*b._m00 + _m11*b._m10 + _m12*b._m20 + _m13*b._m30,
                    _m11 = _m10*b._m01 + _m11*b._m11 + _m12*b._m21 + _m13*b._m31,
                    _m12 = _m10*b._m02 + _m11*b._m12 + _m12*b._m22 + _m13*b._m32,
                    _m13 = _m10*b._m03 + _m11*b._m13 + _m12*b._m23 + _m13*b._m33,
                
                    _m20 = _m20*b._m00 + _m21*b._m10 + _m22*b._m20 + _m23*b._m30,
                    _m21 = _m20*b._m01 + _m21*b._m11 + _m22*b._m21 + _m23*b._m31,
                    _m22 = _m20*b._m02 + _m21*b._m12 + _m22*b._m22 + _m23*b._m32,
                    _m23 = _m20*b._m03 + _m21*b._m13 + _m22*b._m23 + _m23*b._m33,
                
                    _m30 = _m30*b._m00 + _m31*b._m10 + _m32*b._m20 + _m33*b._m30,
                    _m31 = _m30*b._m01 + _m31*b._m11 + _m32*b._m21 + _m33*b._m31,
                    _m32 = _m30*b._m02 + _m31*b._m12 + _m32*b._m22 + _m33*b._m32,
                    _m33 = _m30*b._m03 + _m31*b._m13 + _m32*b._m23 + _m33*b._m33
                };
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vector4 MultiplyVector(Vector4 v)
            {
                return new Vector4(
                    _m00*v.x + _m01*v.y + _m02*v.z + _m03*v.w,
                    _m10*v.x + _m11*v.y + _m12*v.z + _m13*v.w,
                    _m20*v.x + _m21*v.y + _m22*v.z + _m23*v.w,
                    _m30*v.x + _m31*v.y + _m32*v.z + _m33*v.w
                );
            }

            // Simplified 4x4 inversion using cofactor method (faster for small matrices)
            public Matrix4X4Custom Invert()
            {
                // Using Unity's Matrix4x4 for inversion (optimized native code)
                Matrix4x4 unity = new Matrix4x4
                {
                    m00 = _m00,
                    m01 = _m01,
                    m02 = _m02,
                    m03 = _m03,
                    m10 = _m10,
                    m11 = _m11,
                    m12 = _m12,
                    m13 = _m13,
                    m20 = _m20,
                    m21 = _m21,
                    m22 = _m22,
                    m23 = _m23,
                    m30 = _m30,
                    m31 = _m31,
                    m32 = _m32,
                    m33 = _m33
                };

                Matrix4x4 inv = unity.inverse;
            
                return new Matrix4X4Custom
                {
                    _m00=inv.m00, _m01=inv.m01, _m02=inv.m02, _m03=inv.m03,
                    _m10=inv.m10, _m11=inv.m11, _m12=inv.m12, _m13=inv.m13,
                    _m20=inv.m20, _m21=inv.m21, _m22=inv.m22, _m23=inv.m23,
                    _m30=inv.m30, _m31=inv.m31, _m32=inv.m32, _m33=inv.m33
                };
            }
        }
    }
}