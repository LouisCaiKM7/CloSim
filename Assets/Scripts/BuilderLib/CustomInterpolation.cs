using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class CustomInterpolation : MonoBehaviour
{
    private Rigidbody rb;
    
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

    private Vector3 velocity;
    private Vector3 angularVelocity;
    private Joint joint;

    [Header("Kalman Filter Settings - Position")]
    [Tooltip("Q: Process noise (model uncertainty).")]
    public float positionProcessNoise = 0.02f;
    [Tooltip("R: Measurement noise (sensor uncertainty).")]
    public float positionMeasurementNoise = 0.1f;

    [Header("Kalman Filter Settings - Rotation")]
    public float rotationProcessNoise = 0.02f;
    public float rotationMeasurementNoise = 0.1f;

    // Kalman Filter state - Position (3x3)
    private Vector3 positionEstimate;
    private Matrix3x3 positionCovariance;
    
    // Kalman Filter state - Rotation (4x4)
    private Vector4 rotationEstimate;
    private Matrix4x4Custom rotationCovariance;

    // Cached values for GC reduction
    private const float POSITION_THRESHOLD = 0.01f;
    private const float ROTATION_THRESHOLD = 0.1f;
    private const float SINGULARITY_EPSILON = 1e-6f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("Rigidbody component not found!");
            enabled = false;
            return;
        }

        rb.interpolation = RigidbodyInterpolation.None;
        targetPosition = rb.position;
        targetRotation = rb.rotation;
        velocity = Vector3.zero;
        angularVelocity = Vector3.zero;

        joint = GetComponent<Joint>();

        // Initialize Kalman Filters
        positionEstimate = rb.position;
        positionCovariance = Matrix3x3.Identity();

        rotationEstimate = QuaternionToVector4(rb.rotation);
        rotationCovariance = Matrix4x4Custom.Identity();
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
        if (joint != null && joint.connectedBody != null)
        {
            targetPosWithOffset = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
        }

        // Spring-Damper (P-D Control)
        Vector3 displacement = targetPosWithOffset - positionEstimate;
        Vector3 force = displacement * positionStiffness;
        Vector3 damping = velocity * positionDamping;
        Vector3 netForce = force - damping;

        velocity += netForce * deltaTime;
        
        // Prediction Step
        Vector3 predictedPosition = positionEstimate + velocity * deltaTime;
        
        float processNoise = positionProcessNoise * deltaTime;
        positionCovariance.AddScaledIdentity(processNoise);

        // Measurement Update
        Vector3 innovation = Vector3.zero; // predictedPosition - predictedPosition = 0
        
        // Innovation Covariance (S = P + R)
        Matrix3x3 innovationCov = positionCovariance;
        innovationCov.AddScaledIdentity(positionMeasurementNoise);
        
        // Kalman Gain (K = P * S^-1)
        Matrix3x3 invInnovationCov = innovationCov.Invert();
        Matrix3x3 kalmanGain = positionCovariance.Multiply(invInnovationCov);

        // Update Estimate
        positionEstimate = predictedPosition + kalmanGain.MultiplyVector(innovation);
        
        // Update Covariance (P = (I - K) * P)
        Matrix3x3 identity = Matrix3x3.Identity();
        identity.Subtract(kalmanGain);
        positionCovariance = identity.Multiply(positionCovariance);

        rb.MovePosition(positionEstimate);

        // Stop condition
        float distSqr = (positionEstimate - targetPosWithOffset).sqrMagnitude;
        if (distSqr < POSITION_THRESHOLD * POSITION_THRESHOLD)
        {
            rb.MovePosition(targetPosWithOffset);
            isInterpolatingPosition = false;
            velocity = Vector3.zero;
        }
    }

    private void InterpolateRotation(float deltaTime)
    {
        Quaternion targetRotWithOffset = targetRotation;
        if (joint != null && joint.connectedBody != null)
        {
            targetRotWithOffset = joint.connectedBody.transform.rotation;
        }

        // Spring-Damper (P-D Control)
        Quaternion currentRotEstimate = Vector4ToQuaternion(rotationEstimate);
        Quaternion rotationDifference = targetRotWithOffset * Quaternion.Inverse(currentRotEstimate);
        rotationDifference.ToAngleAxis(out float angle, out Vector3 axis);

        // Shortest path
        if (angle > 180f) angle -= 360f;

        Vector3 torque = axis * angle * rotationStiffness;
        Vector3 damping = angularVelocity * rotationDamping;
        Vector3 netTorque = torque - damping;

        angularVelocity += netTorque * deltaTime;
        
        // Prediction
        float angularMagnitude = angularVelocity.magnitude;
        Quaternion deltaRotation = (angularMagnitude > SINGULARITY_EPSILON) 
            ? Quaternion.AngleAxis(angularMagnitude * deltaTime, angularVelocity / angularMagnitude)
            : Quaternion.identity;
            
        Quaternion predictedRotationPD = currentRotEstimate * deltaRotation;
        // Correct quaternion prediction: multiply, then convert to Vector4
        Vector4 predictedRotationV4 = QuaternionToVector4(predictedRotationPD);
        
        float processNoise = rotationProcessNoise * deltaTime;
        rotationCovariance.AddScaledIdentity(processNoise);

        // Measurement Update
        Vector4 measurement = QuaternionToVector4(predictedRotationPD);
        Vector4 innovation = measurement - predictedRotationV4;

        // Innovation Covariance
        Matrix4x4Custom innovationCov = rotationCovariance;
        innovationCov.AddScaledIdentity(rotationMeasurementNoise);

        // Kalman Gain
        Matrix4x4Custom invInnovationCov = innovationCov.Invert();
        Matrix4x4Custom kalmanGain = rotationCovariance.Multiply(invInnovationCov);

        // Update Estimate
        rotationEstimate = predictedRotationV4 + kalmanGain.MultiplyVector(innovation);
        
        // Update Covariance
        Matrix4x4Custom identity = Matrix4x4Custom.Identity();
        identity.Subtract(kalmanGain);
        rotationCovariance = identity.Multiply(rotationCovariance);
        
        // Apply filtered rotation
        Quaternion finalRotation = Vector4ToQuaternion(rotationEstimate).normalized;
        rb.MoveRotation(finalRotation);
        rotationEstimate = QuaternionToVector4(finalRotation);

        // Stop condition
        if (Quaternion.Angle(finalRotation, targetRotWithOffset) < ROTATION_THRESHOLD)
        {
            rb.MoveRotation(targetRotWithOffset);
            isInterpolatingRotation = false;
            angularVelocity = Vector3.zero;
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
    private struct Matrix3x3
    {
        // Row-major storage
        public float m00, m01, m02;
        public float m10, m11, m12;
        public float m20, m21, m22;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x3 Identity()
        {
            return new Matrix3x3
            {
                m00 = 1f, m01 = 0f, m02 = 0f,
                m10 = 0f, m11 = 1f, m12 = 0f,
                m20 = 0f, m21 = 0f, m22 = 1f
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddScaledIdentity(float scale)
        {
            m00 += scale;
            m11 += scale;
            m22 += scale;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Subtract(Matrix3x3 other)
        {
            m00 -= other.m00; m01 -= other.m01; m02 -= other.m02;
            m10 -= other.m10; m11 -= other.m11; m12 -= other.m12;
            m20 -= other.m20; m21 -= other.m21; m22 -= other.m22;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Matrix3x3 Multiply(Matrix3x3 b)
        {
            return new Matrix3x3
            {
                m00 = m00 * b.m00 + m01 * b.m10 + m02 * b.m20,
                m01 = m00 * b.m01 + m01 * b.m11 + m02 * b.m21,
                m02 = m00 * b.m02 + m01 * b.m12 + m02 * b.m22,
                
                m10 = m10 * b.m00 + m11 * b.m10 + m12 * b.m20,
                m11 = m10 * b.m01 + m11 * b.m11 + m12 * b.m21,
                m12 = m10 * b.m02 + m11 * b.m12 + m12 * b.m22,
                
                m20 = m20 * b.m00 + m21 * b.m10 + m22 * b.m20,
                m21 = m20 * b.m01 + m21 * b.m11 + m22 * b.m21,
                m22 = m20 * b.m02 + m21 * b.m12 + m22 * b.m22
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 MultiplyVector(Vector3 v)
        {
            return new Vector3(
                m00 * v.x + m01 * v.y + m02 * v.z,
                m10 * v.x + m11 * v.y + m12 * v.z,
                m20 * v.x + m21 * v.y + m22 * v.z
            );
        }

        public Matrix3x3 Invert()
        {
            // Compute determinant
            float det = m00 * (m11 * m22 - m12 * m21) -
                       m01 * (m10 * m22 - m12 * m20) +
                       m02 * (m10 * m21 - m11 * m20);

            if (Mathf.Abs(det) < SINGULARITY_EPSILON)
            {
                throw new InvalidOperationException("Matrix is singular");
            }

            float invDet = 1f / det;

            return new Matrix3x3
            {
                m00 = (m11 * m22 - m12 * m21) * invDet,
                m01 = (m02 * m21 - m01 * m22) * invDet,
                m02 = (m01 * m12 - m02 * m11) * invDet,
                
                m10 = (m12 * m20 - m10 * m22) * invDet,
                m11 = (m00 * m22 - m02 * m20) * invDet,
                m12 = (m02 * m10 - m00 * m12) * invDet,
                
                m20 = (m10 * m21 - m11 * m20) * invDet,
                m21 = (m01 * m20 - m00 * m21) * invDet,
                m22 = (m00 * m11 - m01 * m10) * invDet
            };
        }
    }

    // Optimized 4x4 Matrix struct for rotation Kalman filter
    private struct Matrix4x4Custom
    {
        public float m00, m01, m02, m03;
        public float m10, m11, m12, m13;
        public float m20, m21, m22, m23;
        public float m30, m31, m32, m33;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4Custom Identity()
        {
            return new Matrix4x4Custom
            {
                m00 = 1f, m11 = 1f, m22 = 1f, m33 = 1f
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddScaledIdentity(float scale)
        {
            m00 += scale; m11 += scale; m22 += scale; m33 += scale;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Subtract(Matrix4x4Custom other)
        {
            m00 -= other.m00; m01 -= other.m01; m02 -= other.m02; m03 -= other.m03;
            m10 -= other.m10; m11 -= other.m11; m12 -= other.m12; m13 -= other.m13;
            m20 -= other.m20; m21 -= other.m21; m22 -= other.m22; m23 -= other.m23;
            m30 -= other.m30; m31 -= other.m31; m32 -= other.m32; m33 -= other.m33;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Matrix4x4Custom Multiply(Matrix4x4Custom b)
        {
            return new Matrix4x4Custom
            {
                m00 = m00*b.m00 + m01*b.m10 + m02*b.m20 + m03*b.m30,
                m01 = m00*b.m01 + m01*b.m11 + m02*b.m21 + m03*b.m31,
                m02 = m00*b.m02 + m01*b.m12 + m02*b.m22 + m03*b.m32,
                m03 = m00*b.m03 + m01*b.m13 + m02*b.m23 + m03*b.m33,
                
                m10 = m10*b.m00 + m11*b.m10 + m12*b.m20 + m13*b.m30,
                m11 = m10*b.m01 + m11*b.m11 + m12*b.m21 + m13*b.m31,
                m12 = m10*b.m02 + m11*b.m12 + m12*b.m22 + m13*b.m32,
                m13 = m10*b.m03 + m11*b.m13 + m12*b.m23 + m13*b.m33,
                
                m20 = m20*b.m00 + m21*b.m10 + m22*b.m20 + m23*b.m30,
                m21 = m20*b.m01 + m21*b.m11 + m22*b.m21 + m23*b.m31,
                m22 = m20*b.m02 + m21*b.m12 + m22*b.m22 + m23*b.m32,
                m23 = m20*b.m03 + m21*b.m13 + m22*b.m23 + m23*b.m33,
                
                m30 = m30*b.m00 + m31*b.m10 + m32*b.m20 + m33*b.m30,
                m31 = m30*b.m01 + m31*b.m11 + m32*b.m21 + m33*b.m31,
                m32 = m30*b.m02 + m31*b.m12 + m32*b.m22 + m33*b.m32,
                m33 = m30*b.m03 + m31*b.m13 + m32*b.m23 + m33*b.m33
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 MultiplyVector(Vector4 v)
        {
            return new Vector4(
                m00*v.x + m01*v.y + m02*v.z + m03*v.w,
                m10*v.x + m11*v.y + m12*v.z + m13*v.w,
                m20*v.x + m21*v.y + m22*v.z + m23*v.w,
                m30*v.x + m31*v.y + m32*v.z + m33*v.w
            );
        }

        // Simplified 4x4 inversion using cofactor method (faster for small matrices)
        public Matrix4x4Custom Invert()
        {
            // Using Unity's Matrix4x4 for inversion (optimized native code)
            Matrix4x4 unity = new Matrix4x4();
            unity.m00=m00; unity.m01=m01; unity.m02=m02; unity.m03=m03;
            unity.m10=m10; unity.m11=m11; unity.m12=m12; unity.m13=m13;
            unity.m20=m20; unity.m21=m21; unity.m22=m22; unity.m23=m23;
            unity.m30=m30; unity.m31=m31; unity.m32=m32; unity.m33=m33;
            
            Matrix4x4 inv = unity.inverse;
            
            return new Matrix4x4Custom
            {
                m00=inv.m00, m01=inv.m01, m02=inv.m02, m03=inv.m03,
                m10=inv.m10, m11=inv.m11, m12=inv.m12, m13=inv.m13,
                m20=inv.m20, m21=inv.m21, m22=inv.m22, m23=inv.m23,
                m30=inv.m30, m31=inv.m31, m32=inv.m32, m33=inv.m33
            };
        }
    }
}