using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Util;

public class JointController : MonoBehaviour
{
    /// <summary>
    /// Sets the location for the controller to base its targets off of
    /// </summary>
    public float currentPosition;

    /// <summary>
    /// The joint for the controller to affect control over
    /// </summary>
    public ConfigurableJoint joint;

    /// <summary>
    /// Whether the joint is moving in a linear or angular axis (true is angular)
    /// </summary>
    public bool angular;

    public bool useNoWrap;
    public float noWrapAngle;

    /// <summary>
    /// Specifies the Euler axis to control. must be (1,0,0) (0,1,0) or (0,0,1)
    /// </summary>
    public Vector3 driveAxis;

    /// <summary>
    /// Sets the home location.
    /// </summary>
    public float home;

    /// <summary>
    /// Used when another scripts needs to control the target instead of the passed through setpoints.
    /// </summary>
    public bool follower = false;

    private PlayerInput _playerInput;
    public InputActionMap _inputMap;
    public float _targetPosition;

    private PIDController _pidController;

    private Dictionary<SetPoint, float> originalPositions = new Dictionary<SetPoint, float>();

    private bool _sequenceInterrupted, _isSequenceUsingDelay;
    private float _sequenceTime;
    private string _activeSequenceName;
    private SetPoint _nextSequencePoint;
    private bool OverideActive;
    private string _activeSetpointName;

    [HideInInspector] public float p;
    [HideInInspector] public float i;
    [HideInInspector] public float d;
    [HideInInspector] public float iSat;
    [HideInInspector] public float max;
    [HideInInspector] public float offset = 0;

    /// <summary>
    /// The setpoint struct to base the logic around.
    /// </summary>
    [HideInInspector] public SetPoint[] setPoints;

    private float overidePosition;
    private float lastTime;

    private bool _disabledHoldCaptured;
    private float _disabledHoldPosition;

    void Start()
    {
        _sequenceTime = 0;
        _targetPosition = 0;
        _activeSequenceName = null;
        _sequenceInterrupted = false;
        _playerInput = Utils.FindParentObjectComponent<PlayerInput>(gameObject);

        if (_playerInput != null && _playerInput.actions != null)
        {
            _inputMap = _playerInput.actions.FindActionMap("Robot");
            _inputMap?.Enable();
        }

        OverideActive = false;

        _pidController = new PIDController
        {
            proportionalGain = p,
            derivativeGain = d,
            integralGain = i,
            outputMax = max,
            outputMin = -max,
            integralSaturation = iSat
        };

        lastTime = Time.time;
    }

    public string getActiveSetpoint()
    {
        return _activeSetpointName;
    }

    /// <summary>
    /// the overide function for running a joint PID directly instead of through the setpoint object
    /// </summary>
    public void FollowPosition(float position)
    {
        _targetPosition = position;
    }

    public void OveridePosition(float position)
    {
        if (FMS.RobotState == RobotState.disabled)
            return;

        _targetPosition = position;
        OverideActive = true;
        overidePosition = position;
    }

    void Update()
    {
        if (!_playerInput)
        {
            _playerInput = Utils.FindParentObjectComponent<PlayerInput>(gameObject);

            if (_playerInput != null && _playerInput.actions != null)
            {
                _inputMap = _playerInput.actions.FindActionMap("Robot");
                _inputMap?.Enable();
            }

            return;
        }

        if (FMS.RobotState == RobotState.disabled)
        {
            if (!_disabledHoldCaptured)
            {
                _disabledHoldPosition = currentPosition;
                _disabledHoldCaptured = true;
            }

            OverideActive = false;

            // Angular targets are inverted later in FixedUpdate:
            // targetForPid = -_targetPosition;
            _targetPosition = angular ? -_disabledHoldPosition : _disabledHoldPosition;
            return;
        }

        _disabledHoldCaptured = false;

        noWrapAngle = Mathf.Repeat(noWrapAngle, 360);

        if (_sequenceTime > 0)
        {
            _sequenceTime -= Time.deltaTime;
        }

        if (follower) return;
        if (setPoints == null) return;
        if (_inputMap == null) return;

        for (int i = 0; i < setPoints.Length; i++)
        {
            var setPoint = setPoints[i];
            var controllerAction = _inputMap.FindAction(setPoint.controllerButton.ToString());
            var keyboardAction = _inputMap.FindAction(setPoint.keyboardButton.ToString());

            if (controllerAction == null || keyboardAction == null)
                continue;

            var buttonPressed = false;

            if (controllerAction.triggered)
            {
                if (controllerAction.activeControl?.device is Gamepad)
                {
                    buttonPressed = true;
                }
            }

            if (keyboardAction.triggered)
            {
                if (keyboardAction.activeControl?.device is Keyboard)
                {
                    buttonPressed = true;
                }
            }

            var controllerHeld = controllerAction.IsPressed() &&
                                 (controllerAction.activeControl?.device is Gamepad);
            var keyboardHeld = keyboardAction.IsPressed() &&
                               (keyboardAction.activeControl?.device is Keyboard);
            var buttonHeld = controllerHeld || keyboardHeld;

            switch (setPoint.controlType)
            {
                case ControlType.Sequence:
                    if (_sequenceInterrupted)
                    {
                        _sequenceInterrupted = false;
                        _nextSequencePoint = null;
                        _activeSequenceName = null;
                    }

                    if (_isSequenceUsingDelay ? _sequenceTime <= 0 : buttonPressed)
                    {
                        if (_nextSequencePoint != null)
                        {
                            if (setPoint.setpointName != _nextSequencePoint.setpointName) continue;

                            _activeSetpointName = setPoint.setpointName;

                            if (_nextSequencePoint.sequenceType != SequenceType.end)
                            {
                                _targetPosition = _nextSequencePoint.getPoint();
                            }
                            else
                            {
                                if (!_nextSequencePoint.getPersist())
                                    _targetPosition = _nextSequencePoint.getPoint();

                                originalPositions.Clear();
                                originalPositions[_nextSequencePoint] = _nextSequencePoint.getPoint();
                                _nextSequencePoint = null;
                                _activeSequenceName = null;
                                _isSequenceUsingDelay = false;
                                _sequenceTime = 0;
                                continue;
                            }

                            switch (setPoint.sequenceType)
                            {
                                case SequenceType.delay:
                                    _sequenceTime = setPoint.delay;
                                    _isSequenceUsingDelay = true;
                                    break;

                                case SequenceType.nextPress:
                                    _sequenceTime = 0;
                                    _isSequenceUsingDelay = false;
                                    break;
                            }

                            foreach (var t in setPoints)
                            {
                                if (t.setpointName != _nextSequencePoint.sequenceTo) continue;
                                _nextSequencePoint = t;
                                return;
                            }

                            _nextSequencePoint = null;
                        }
                        else if (_activeSequenceName != null)
                        {
                            _targetPosition = home;
                            _activeSetpointName = null;
                            _nextSequencePoint = null;
                            _activeSequenceName = null;
                            return;
                        }
                    }

                    break;

                case ControlType.Hold:
                    if (buttonPressed)
                    {
                        _sequenceInterrupted = true;

                        if (!originalPositions.ContainsKey(setPoint))
                        {
                            originalPositions.Clear();
                            originalPositions[setPoint] = home;
                            _targetPosition = setPoint.getPoint();
                            _activeSetpointName = setPoint.setpointName;
                        }
                    }
                    else if (originalPositions.ContainsKey(setPoint) && !buttonHeld)
                    {
                        _targetPosition = originalPositions[setPoint];
                        originalPositions.Remove(setPoint);
                        _activeSetpointName = null;
                    }

                    break;

                case ControlType.SequenceStart:
                    if (buttonPressed)
                    {
                        if (_sequenceInterrupted)
                        {
                            _sequenceInterrupted = false;
                            _nextSequencePoint = null;
                            _activeSequenceName = null;
                        }

                        if (_nextSequencePoint == null && _activeSequenceName == null)
                        {
                            _sequenceInterrupted = false;
                            _activeSequenceName = setPoint.setpointName;
                            _activeSetpointName = setPoint.setpointName;

                            switch (setPoint.sequenceType)
                            {
                                case SequenceType.delay:
                                    _sequenceTime = setPoint.delay;
                                    _isSequenceUsingDelay = true;
                                    break;

                                case SequenceType.nextPress:
                                    _sequenceTime = 0;
                                    _isSequenceUsingDelay = false;
                                    break;
                            }

                            if (!setPoint.getPersist())
                            {
                                _targetPosition = setPoint.getPoint();
                            }

                            foreach (var t in setPoints)
                            {
                                if (t.setpointName != setPoint.sequenceTo) continue;
                                _nextSequencePoint = t;
                                return;
                            }

                            _nextSequencePoint = null;
                            return;
                        }

                        if (_activeSequenceName == setPoint.setpointName)
                        {
                            if (_nextSequencePoint != null &&
                                (_nextSequencePoint.keyboardButton == setPoint.keyboardButton ||
                                 _nextSequencePoint.controllerButton == setPoint.controllerButton))
                            {
                                continue;
                            }

                            _targetPosition = home;
                            _nextSequencePoint = null;
                            _activeSequenceName = null;
                            return;
                        }
                    }

                    break;

                case ControlType.Toggle:
                    if (buttonPressed)
                    {
                        _sequenceInterrupted = true;

                        if (originalPositions.ContainsKey(setPoint))
                        {
                            _targetPosition = home;
                            originalPositions.Remove(setPoint);
                            _activeSetpointName = null;
                        }
                        else
                        {
                            originalPositions[setPoint] = setPoint.getPoint();
                            _targetPosition = setPoint.getPoint();
                            _activeSetpointName = setPoint.setpointName;
                        }
                    }

                    break;

                case ControlType.LastPressed:
                    if (buttonPressed)
                    {
                        _sequenceInterrupted = true;

                        originalPositions.Clear();
                        originalPositions[setPoint] = setPoint.getPoint();

                        _activeSetpointName = setPoint.setpointName;

                        if (!setPoint.getPersist())
                        {
                            _targetPosition = setPoint.getPoint();
                        }
                    }

                    break;
            }
        }

        if (OverideActive)
        {
            _targetPosition = overidePosition;
            OverideActive = false;
        }
    }

    private void FixedUpdate()
    {
        if (_pidController == null || joint == null)
            return;

        float rawPID;
        float dt = Time.fixedDeltaTime;

        currentPosition -= offset;

        if (FMS.RobotState == RobotState.disabled)
        {
            if (angular)
            {
                rawPID = _pidController.UpdateAngle(dt, currentPosition, -_disabledHoldPosition);
                joint.targetAngularVelocity = rawPID * driveAxis;
            }
            else
            {
                rawPID = _pidController.UpdateLinear(dt, currentPosition, _disabledHoldPosition);
                joint.targetVelocity = -rawPID * driveAxis;
            }

            lastTime = Time.time;
            return;
        }

        if (angular)
        {
            float targetForPid = -_targetPosition;
            var wrapAngle = noWrapAngle;
            wrapAngle = Utils.FlipAngle(wrapAngle);
            wrapAngle = Mathf.Repeat(wrapAngle, 360);

            if (useNoWrap)
            {
                if (PassesThroughWrapAngle(currentPosition, targetForPid, wrapAngle))
                {
                    float difference = Utils.AngleDifference(_targetPosition, currentPosition);

                    if (difference > 0)
                    {
                        targetForPid = wrapAngle + 180;
                    }
                    else
                    {
                        targetForPid = wrapAngle - 180;
                    }
                }
                else
                {
                    targetForPid = -_targetPosition;
                }
            }

            rawPID = _pidController.UpdateAngle(dt, currentPosition, targetForPid);
            joint.targetAngularVelocity = rawPID * driveAxis;
        }
        else
        {
            rawPID = _pidController.UpdateLinear(dt, currentPosition, _targetPosition);
            joint.targetVelocity = -rawPID * driveAxis;
        }

        lastTime = Time.time;
    }

    bool PassesThroughWrapAngle(float currentAngle, float targetAngle, float wrapAngle)
    {
        currentAngle = ((currentAngle % 360) + 360) % 360;
        targetAngle = ((targetAngle % 360) + 360) % 360;
        wrapAngle = ((wrapAngle % 360) + 360) % 360;

        float diff = targetAngle - currentAngle;
        if (diff > 180.0f) diff -= 360.0f;
        if (diff < -180.0f) diff += 360.0f;

        float endAngle = currentAngle + diff;
        if (endAngle < 0) endAngle += 360.0f;
        if (endAngle >= 360.0f) endAngle -= 360.0f;

        if (diff > 0)
        {
            if (currentAngle <= endAngle)
            {
                return wrapAngle > currentAngle && wrapAngle < endAngle;
            }
            else
            {
                return wrapAngle > currentAngle || wrapAngle < endAngle;
            }
        }
        else
        {
            if (currentAngle >= endAngle)
            {
                return wrapAngle < currentAngle && wrapAngle > endAngle;
            }
            else
            {
                return wrapAngle < currentAngle || wrapAngle > endAngle;
            }
        }
    }
}