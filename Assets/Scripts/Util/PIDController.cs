using System;
using UnityEngine;

    [Serializable]
    public class PIDController
    {
        public enum DerivativeMeasurement {
            Velocity,
            ErrorRateOfChange
        }

        /// <summary>
        /// The P value of the PID
        /// </summary>
        public float proportionalGain;
        /// <summary>
        /// The I gain of the PID
        /// </summary>
        public float integralGain;
        /// <summary>
        /// The D gain of the PID
        /// </summary>
        public float derivativeGain;

        /// <summary>
        /// Minimum output value for the PID
        /// </summary>
        public float outputMin = -1;
        /// <summary>
        /// Maximum Output value for the PID
        /// </summary>
        public float outputMax = 1;
        public float integralSaturation;
        public DerivativeMeasurement derivativeMeasurement;

        public float valueLast;
        public float errorLast;
        public float integrationStored;
        public float velocity;
        public bool derivativeInitialized;

        /// <summary>
        /// Resets the PID controller
        /// </summary>
        public void ResetController() {
            derivativeInitialized = false;
        }

        /// <summary>
        /// Update the PID in linear mode
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="currentValue"></param>
        /// <param name="targetValue"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public float UpdateLinear(float dt, float currentValue, float targetValue) {
            if (dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));

            float error = targetValue - currentValue;

            float P = proportionalGain * error;

            integrationStored = Mathf.Clamp(integrationStored + error * dt, -integralSaturation, integralSaturation);
            float I = integralGain * integrationStored;

            float errorRateOfChange = (error - errorLast) / dt;
            errorLast = error;

            float valueRateOfChange = (currentValue - valueLast) / dt;
            valueLast = currentValue;
            velocity = valueRateOfChange;

            float deriveMeasure = 0;

            if (derivativeInitialized) {
                if (derivativeMeasurement == DerivativeMeasurement.Velocity)
                    deriveMeasure = -valueRateOfChange;
                else
                    deriveMeasure = errorRateOfChange;
            }
            else {
                derivativeInitialized = true;
            }

            float D = derivativeGain * deriveMeasure;

            var pd = Mathf.Clamp(P + D, outputMin, outputMax);

            float result = pd + I;

            return Mathf.Clamp(result, outputMin - integralSaturation, outputMax + integralSaturation);
        }

        /// <summary>
        /// Calculates the angle difference
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public float AngleDifference(float a, float b) {
            return (a - b + 540) % 360 - 180;
        }

        /// <summary>
        /// Updates the angle value (for angular mode)
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="currentAngle"></param>
        /// <param name="targetAngle"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public float UpdateAngle(float dt, float currentAngle, float targetAngle) {
            if (dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
            float error = AngleDifference(targetAngle, currentAngle);

            float P = proportionalGain * error;

            integrationStored = Mathf.Clamp(integrationStored + error * dt, -integralSaturation, integralSaturation);
            float I = integralGain * integrationStored;

            float errorRateOfChange = AngleDifference(error, errorLast) / dt;
            errorLast = error;

            float valueRateOfChange = AngleDifference(currentAngle, valueLast) / dt;
            valueLast = currentAngle;
            velocity = valueRateOfChange;

            float deriveMeasure = 0;

            if (derivativeInitialized) {
                if (derivativeMeasurement == DerivativeMeasurement.Velocity)
                    deriveMeasure = -valueRateOfChange;
                else
                    deriveMeasure = errorRateOfChange;
            }
            else {
                derivativeInitialized = true;
            }

            float D = derivativeGain * deriveMeasure;

            var pd = Mathf.Clamp(P + D, outputMin, outputMax);

            float result = pd + I;

            return Mathf.Clamp(result, outputMin - integralSaturation, outputMax+ integralSaturation);
        }
    }