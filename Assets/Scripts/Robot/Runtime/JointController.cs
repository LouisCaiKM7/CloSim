using System.Collections.Generic;
using System.Reflection;
using Core;
using Field.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Utilities;

namespace Robot.Runtime
{
    public class JointController : MonoBehaviour
    {
        /// <summary>
        /// Sets the location for the controller to base its targets off of.
        /// </summary>
        public float currentPosition;

        /// <summary>
        /// The joint for the controller to affect control over.
        /// </summary>
        public ConfigurableJoint joint;

        /// <summary>
        /// Whether the joint is moving in a linear or angular axis. True is angular.
        /// </summary>
        public bool angular;

        public bool useNoWrap;
        public float noWrapAngle;

        /// <summary>
        /// Specifies the Euler axis to control. Must be (1,0,0), (0,1,0), or (0,0,1).
        /// </summary>
        public Vector3 driveAxis;

        /// <summary>
        /// Sets the home location.
        /// </summary>
        public float home;

        /// <summary>
        /// Used when another script needs to control the target instead of the passed-through setpoints.
        /// </summary>
        public bool follower;

        [Header("Input")]
        [SerializeField] private string actionMapName = "Robot";

        private PlayerInput _playerInput;
        [FormerlySerializedAs("_inputMap")] public InputActionMap inputMap;
        [FormerlySerializedAs("_targetPosition")] public float targetPosition;

        private PidController _pidController;

        private readonly Dictionary<SetPoint, float> _originalPositions = new Dictionary<SetPoint, float>();

        private bool _sequenceInterrupted;
        private bool _isSequenceUsingDelay;
        private float _sequenceTime;
        private string _activeSequenceName;
        private SetPoint _nextSequencePoint;
        private bool _overideActive;
        private string _activeSetpointName;

        [HideInInspector] public float p;
        [HideInInspector] public float i;
        [HideInInspector] public float d;
        [HideInInspector] public float iSat;
        [HideInInspector] public float max;
        [HideInInspector] public float offset;

        /// <summary>
        /// The setpoint struct to base the logic around.
        /// </summary>
        [HideInInspector] public SetPoint[] setPoints;

        private float _overidePosition;

        private bool _disabledHoldCaptured;
        private float _disabledHoldPosition;

        private void Start()
        {
            _sequenceTime = 0f;
            targetPosition = 0f;
            _activeSequenceName = null;
            _sequenceInterrupted = false;

            ResolveInput();

            _overideActive = false;

            _pidController = new PidController
            {
                proportionalGain = p,
                derivativeGain = d,
                integralGain = i,
                outputMax = max,
                outputMin = -max,
                integralSaturation = iSat
            };
        }

        public string GetActiveSetpoint()
        {
            return _activeSetpointName;
        }

        /// <summary>
        /// The override function for running a joint PID directly instead of through the setpoint object.
        /// </summary>
        public void FollowPosition(float position)
        {
            targetPosition = position;
        }

        public void OveridePosition(float position)
        {
            if (Fms.RobotState == RobotState.Disabled)
                return;

            targetPosition = position;
            _overideActive = true;
            _overidePosition = position;
        }

        private void Update()
        {
            if (_playerInput == null || inputMap == null)
            {
                ResolveInput();
                if (_playerInput == null || inputMap == null)
                    return;
            }

            if (Fms.RobotState == RobotState.Disabled)
            {
                if (!_disabledHoldCaptured)
                {
                    _disabledHoldPosition = currentPosition;
                    _disabledHoldCaptured = true;
                }

                _overideActive = false;

                // Angular targets are inverted later in FixedUpdate:
                // targetForPid = -_targetPosition.
                targetPosition = angular ? -_disabledHoldPosition : _disabledHoldPosition;
                return;
            }

            _disabledHoldCaptured = false;

            noWrapAngle = Mathf.Repeat(noWrapAngle, 360f);

            if (_sequenceTime > 0f)
                _sequenceTime -= Time.deltaTime;

            if (follower)
                return;

            if (setPoints == null || setPoints.Length == 0)
                return;

            foreach (var setPoint in setPoints)
            {
                if (!TryReadSetPointInput(setPoint, out bool buttonPressed, out bool buttonHeld))
                    continue;

                switch (setPoint.controlType)
                {
                    case ControlType.Sequence:
                        if (_sequenceInterrupted)
                        {
                            _sequenceInterrupted = false;
                            _nextSequencePoint = null;
                            _activeSequenceName = null;
                        }

                        if (_isSequenceUsingDelay ? _sequenceTime <= 0f : buttonPressed)
                        {
                            if (_nextSequencePoint != null)
                            {
                                if (setPoint.setpointName != _nextSequencePoint.setpointName)
                                    continue;

                                _activeSetpointName = setPoint.setpointName;

                                if (_nextSequencePoint.sequenceType != SequenceType.End)
                                {
                                    targetPosition = _nextSequencePoint.GetPoint();
                                }
                                else
                                {
                                    if (!_nextSequencePoint.GetPersist())
                                        targetPosition = _nextSequencePoint.GetPoint();

                                    _originalPositions.Clear();
                                    _originalPositions[_nextSequencePoint] = _nextSequencePoint.GetPoint();
                                    _nextSequencePoint = null;
                                    _activeSequenceName = null;
                                    _isSequenceUsingDelay = false;
                                    _sequenceTime = 0f;
                                    continue;
                                }

                                switch (setPoint.sequenceType)
                                {
                                    case SequenceType.Delay:
                                        _sequenceTime = setPoint.delay;
                                        _isSequenceUsingDelay = true;
                                        break;

                                    case SequenceType.NextPress:
                                        _sequenceTime = 0f;
                                        _isSequenceUsingDelay = false;
                                        break;
                                }

                                foreach (SetPoint t in setPoints)
                                {
                                    if (t.setpointName != _nextSequencePoint.sequenceTo)
                                        continue;

                                    _nextSequencePoint = t;
                                    return;
                                }

                                _nextSequencePoint = null;
                            }
                            else if (_activeSequenceName != null)
                            {
                                targetPosition = home;
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

                            if (!_originalPositions.ContainsKey(setPoint))
                            {
                                _originalPositions.Clear();
                                _originalPositions[setPoint] = home;
                                targetPosition = setPoint.GetPoint();
                                _activeSetpointName = setPoint.setpointName;
                            }
                        }
                        else if (_originalPositions.ContainsKey(setPoint) && !buttonHeld)
                        {
                            targetPosition = _originalPositions[setPoint];
                            _originalPositions.Remove(setPoint);
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
                                    case SequenceType.Delay:
                                        _sequenceTime = setPoint.delay;
                                        _isSequenceUsingDelay = true;
                                        break;

                                    case SequenceType.NextPress:
                                        _sequenceTime = 0f;
                                        _isSequenceUsingDelay = false;
                                        break;
                                }

                                if (!setPoint.GetPersist())
                                    targetPosition = setPoint.GetPoint();

                                foreach (SetPoint t in setPoints)
                                {
                                    if (t.setpointName != setPoint.sequenceTo)
                                        continue;

                                    _nextSequencePoint = t;
                                    return;
                                }

                                _nextSequencePoint = null;
                                return;
                            }

                            if (_activeSequenceName == setPoint.setpointName)
                            {
                                if (_nextSequencePoint != null && UsesSameInput(_nextSequencePoint, setPoint))
                                    continue;

                                targetPosition = home;
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

                            if (_originalPositions.ContainsKey(setPoint))
                            {
                                targetPosition = home;
                                _originalPositions.Remove(setPoint);
                                _activeSetpointName = null;
                            }
                            else
                            {
                                _originalPositions[setPoint] = setPoint.GetPoint();
                                targetPosition = setPoint.GetPoint();
                                _activeSetpointName = setPoint.setpointName;
                            }
                        }

                        break;

                    case ControlType.LastPressed:
                        if (buttonPressed)
                        {
                            _sequenceInterrupted = true;

                            _originalPositions.Clear();
                            _originalPositions[setPoint] = setPoint.GetPoint();

                            _activeSetpointName = setPoint.setpointName;

                            if (!setPoint.GetPersist())
                                targetPosition = setPoint.GetPoint();
                        }

                        break;
                }
            }

            if (_overideActive)
            {
                targetPosition = _overidePosition;
                _overideActive = false;
            }
        }

        private void FixedUpdate()
        {
            if (_pidController == null || joint == null)
                return;

            float rawPid;
            float dt = Time.fixedDeltaTime;

            currentPosition -= offset;

            if (Fms.RobotState == RobotState.Disabled)
            {
                if (angular)
                {
                    rawPid = _pidController.UpdateAngle(dt, currentPosition, -_disabledHoldPosition);
                    joint.targetAngularVelocity = rawPid * driveAxis;
                }
                else
                {
                    rawPid = _pidController.UpdateLinear(dt, currentPosition, _disabledHoldPosition);
                    joint.targetVelocity = -rawPid * driveAxis;
                }

                return;
            }

            if (angular)
            {
                float targetForPid = -targetPosition;
                float wrapAngle = noWrapAngle;
                wrapAngle = Utils.FlipAngle(wrapAngle);
                wrapAngle = Mathf.Repeat(wrapAngle, 360f);

                if (useNoWrap)
                {
                    if (PassesThroughWrapAngle(currentPosition, targetForPid, wrapAngle))
                    {
                        float difference = Utils.AngleDifference(targetPosition, currentPosition);

                        if (difference > 0f)
                            targetForPid = wrapAngle + 180f;
                        else
                            targetForPid = wrapAngle - 180f;
                    }
                    else
                    {
                        targetForPid = -targetPosition;
                    }
                }

                rawPid = _pidController.UpdateAngle(dt, currentPosition, targetForPid);
                joint.targetAngularVelocity = rawPid * driveAxis;
            }
            else
            {
                rawPid = _pidController.UpdateLinear(dt, currentPosition, targetPosition);
                joint.targetVelocity = -rawPid * driveAxis;
            }
        }

        private void ResolveInput()
        {
            _playerInput = Utils.FindParentObjectComponent<PlayerInput>(gameObject);

            if (_playerInput == null || _playerInput.actions == null)
            {
                inputMap = null;
                return;
            }

            inputMap = _playerInput.actions.FindActionMap(actionMapName);
            inputMap?.Enable();
        }

        private bool TryReadSetPointInput(SetPoint setPoint, out bool pressed, out bool held)
        {
            pressed = false;
            held = false;

            if (inputMap == null || setPoint == null)
                return false;

            List<string> actionNames = GetSetPointActionNames(setPoint);
            if (actionNames.Count == 0)
                return false;

            bool foundAnyAction = false;

            foreach (string actionName in actionNames)
            {
                if (string.IsNullOrWhiteSpace(actionName))
                    continue;

                InputAction action = inputMap.FindAction(actionName);
                if (action == null)
                    continue;

                foundAnyAction = true;
                pressed |= action.WasPressedThisFrame();
                held |= action.IsPressed();
            }

            return foundAnyAction;
        }

        private bool UsesSameInput(SetPoint a, SetPoint b)
        {
            if (a == null || b == null)
                return false;

            List<string> aNames = GetSetPointActionNames(a);
            List<string> bNames = GetSetPointActionNames(b);

            foreach (var t in aNames)
            {
                foreach (var t1 in bNames)
                {
                    if (t == t1)
                        return true;
                }
            }

            return false;
        }

        private static List<string> GetSetPointActionNames(SetPoint setPoint)
        {
            List<string> names = new List<string>(3);

            // Preferred new model: SetPoint has a command / robotCommand / inputCommand enum or string.
            if (TryGetMemberValue(setPoint, "command", out object commandValue) ||
                TryGetMemberValue(setPoint, "Command", out commandValue) ||
                TryGetMemberValue(setPoint, "robotCommand", out commandValue) ||
                TryGetMemberValue(setPoint, "inputCommand", out commandValue))
            {
                AddName(names, commandValue);
            }

            // Backward-compatible old model: SetPoint has controllerButton and keyboardButton.
            // This keeps existing robot prefabs from breaking while allowing binding overrides on those actions.
            if (TryGetMemberValue(setPoint, "controllerButton", out object controllerValue) ||
                TryGetMemberValue(setPoint, "ControllerButton", out controllerValue))
            {
                AddName(names, controllerValue);
            }

            if (TryGetMemberValue(setPoint, "keyboardButton", out object keyboardValue) ||
                TryGetMemberValue(setPoint, "KeyboardButton", out keyboardValue))
            {
                AddName(names, keyboardValue);
            }

            return names;
        }

        private static bool TryGetMemberValue(object source, string memberName, out object value)
        {
            value = null;

            if (source == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            System.Type type = source.GetType();

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                value = field.GetValue(source);
                return value != null;
            }

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.CanRead)
            {
                value = property.GetValue(source, null);
                return value != null;
            }

            return false;
        }

        private static void AddName(List<string> names, object value)
        {
            if (value == null)
                return;

            string name = value.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (!names.Contains(name))
                names.Add(name);
        }

        private bool PassesThroughWrapAngle(float currentAngle, float targetAngle, float wrapAngle)
        {
            currentAngle = ((currentAngle % 360f) + 360f) % 360f;
            targetAngle = ((targetAngle % 360f) + 360f) % 360f;
            wrapAngle = ((wrapAngle % 360f) + 360f) % 360f;

            float diff = targetAngle - currentAngle;
            if (diff > 180f) diff -= 360f;
            if (diff < -180f) diff += 360f;

            float endAngle = currentAngle + diff;
            if (endAngle < 0f) endAngle += 360f;
            if (endAngle >= 360f) endAngle -= 360f;

            if (diff > 0f)
            {
                if (currentAngle <= endAngle)
                    return wrapAngle > currentAngle && wrapAngle < endAngle;

                return wrapAngle > currentAngle || wrapAngle < endAngle;
            }

            if (currentAngle >= endAngle)
                return wrapAngle < currentAngle && wrapAngle > endAngle;

            return wrapAngle < currentAngle || wrapAngle > endAngle;
        }
    }
}
