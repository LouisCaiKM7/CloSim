using System;
using System.Collections.Generic;
using Core;
using MyBox;
using Robot.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Utilities;

namespace Robot.Builders.Extensions
{
    public class AutoAim : MonoBehaviour
    {
        [SerializeField] private TargetType targetType;
        [SerializeField] private AimAtWhen targetWhen;
    
        [Header("Targeting Settings")]
        [ConditionalField(true, nameof(IsPreset))]
        [SerializeField] private Vector3 targetPosition;
    
        [SerializeField] private bool aimOppositeDirection;
    
        [Header("Alliance Passing Targets")]
        [ConditionalField(true, nameof(IsPreset))]
        [SerializeField] private bool useAllianceTargets;

        [ConditionalField(true, nameof(UseAllianceTargets))]
        [SerializeField] private Vector3 blueTargetPosition;

        [ConditionalField(true, nameof(UseAllianceTargets))]
        [SerializeField] private Vector3 redTargetPosition;

        [ConditionalField(true, nameof(WhenAtSetpoint))] 
        [SerializeField] private BuildMechanism drivingMechanism;
    
        [FormerlySerializedAs("SetpointName")]
        [ConditionalField(true, nameof(WhenAtSetpoint))] 
        [SerializeField] private string setpointName;

        [ConditionalField(true, nameof(IsPreset), true)]
        [SerializeField] private Vector3[] extraTargets;

        [Header("Button Control Settings")]
        [ConditionalField(true, nameof(WhenButton))]
        [SerializeField] private RobotCommand command = RobotCommand.Hub;

        [ConditionalField(true, nameof(WhenButton))]
        [Tooltip("Optional exact Input Action name. If filled, this overrides Command.")]
        [SerializeField] private string actionName = "";
    
        [Header("Input Settings")]
        [SerializeField] private string actionMapName = "Robot";

        [Header("Range Settings")]
        [ConditionalField(true, nameof(WhenWithinRange))]
        [SerializeField] private float activationRange = 20f; // in inches

        [Header("PID Tuning")]
        [SerializeField] private bool advanced;

        [FormerlySerializedAs("steeringPID")]
        [ConditionalField(nameof(advanced))]
        [SerializeField] private Pid steeringPid;
    
        [Header("Region Filtering")]
        [SerializeField] private bool requireInsideRegion;

        [ConditionalField(nameof(requireInsideRegion))]
        [SerializeField] private Transform bumperRoot;
    
        [SerializeField] private AimRegionId[] allowedRegions;

        private bool IsPreset() => targetType == TargetType.Preset;
        private bool WhenAtSetpoint() => targetWhen == AimAtWhen.AtSetpoint;
        private bool WhenButton() => targetWhen == AimAtWhen.WhenPressing;
        private bool WhenWithinRange() => targetWhen == AimAtWhen.WithinRange;
        private bool UseAllianceTargets() => targetType == TargetType.Preset && useAllianceTargets;

        private SwerveController _controller;
        private PidController _steeringPidController;
        private PlayerInput _playerInput;
        private InputActionMap _inputMap;
        private readonly List<Vector3> _allTargets = new List<Vector3>();
        private Collider[] _bumperColliders = Array.Empty<Collider>();
    
        private readonly List<AimRegion> _regions = new List<AimRegion>();

        private void Start()
        {
            if (!Application.isPlaying) return;
        
            var foundTargets = Utils.FindGameObjectsOnLayer("AutoAngleNodes");
        
            foreach (var target in foundTargets)
            {
                _allTargets.Add(target.transform.position); 
            }

            _controller = GetComponent<SwerveController>();
            _allTargets.AddRange(extraTargets);

            // Initialize PID controller for steering
            if (advanced)
            {
                _steeringPidController = new PidController
                {
                    proportionalGain = steeringPid.p,
                    derivativeGain = steeringPid.d,
                    integralGain = steeringPid.i,
                    outputMax = Mathf.Clamp(steeringPid.max, 0, 1),
                    outputMin = -Mathf.Clamp(steeringPid.max, 0, 1),
                    integralSaturation = 1
                };
            }
            else
            {
                _steeringPidController = new PidController
                {
                    proportionalGain = 0.5f,
                    derivativeGain = 0.05f,
                    integralGain = 0,
                    outputMax = 0.5f,
                    outputMin = -0.5f,
                    integralSaturation = 1
                };
            }
        
            CacheBumperColliders();
            FindAimRegions();
        }

        private void FixedUpdate()
        {
            if (!Application.isPlaying) return;

            if (targetWhen == AimAtWhen.WhenPressing && !TryResolveInput())
                return;
        
            bool shouldTarget = ShouldActivateAiming();

            if (!shouldTarget)
            {
                return;
            }

            if (!IsInsideAllowedRegion())
            {
                return;
            }
        
            Vector3 target = GetTargetValue();

            float targetAngle = CalculateTargetAngle(target) + 180f;

            if (aimOppositeDirection)
            {
                targetAngle += 180f;
            }

            targetAngle = Mathf.Repeat(targetAngle, 360f);

            float currentAngle = transform.localRotation.eulerAngles.y;

            float pidOutput = _steeringPidController.UpdateAngle(
                Time.fixedDeltaTime,
                currentAngle,
                targetAngle
            );

            _controller.OverideSteer(pidOutput, true);
        }
    
        private void CacheBumperColliders()
        {
            if (bumperRoot == null)
            {
                _bumperColliders = Array.Empty<Collider>();
                return;
            }

            _bumperColliders = bumperRoot.GetComponentsInChildren<Collider>(true);
        }

        private bool ShouldActivateAiming()
        {
            switch (targetWhen)
            {
                case AimAtWhen.Always:
                    return true;
                
                case AimAtWhen.AtSetpoint:
                    return CheckSetpointCondition();
                
                case AimAtWhen.WhenPressing:
                    return CheckButtonCondition();
                
                case AimAtWhen.WithinRange:
                    return CheckRangeCondition();
                
                default:
                    return false;
            }
        }

        private bool CheckSetpointCondition()
        {
            if (!drivingMechanism || !drivingMechanism.GetController())
                return false;

            var currentSetpoint = drivingMechanism.GetController().GetActiveSetpoint();
        
            return String.Equals(
                (currentSetpoint ?? "").ToLower().Trim(), 
                setpointName.ToLower().Trim(), 
                StringComparison.OrdinalIgnoreCase);
        }

        private bool CheckButtonCondition()
        {
            if (!TryResolveInput())
                return false;

            string resolvedActionName =
                !string.IsNullOrWhiteSpace(actionName)
                    ? actionName
                    : command.ToString();

            InputAction action = _inputMap.FindAction(resolvedActionName);

            return action != null && action.IsPressed();
        }

        private bool TryResolveInput()
        {
            if (_playerInput == null)
                _playerInput = GetComponent<PlayerInput>() ?? GetComponentInParent<PlayerInput>();

            if (_playerInput == null || _playerInput.actions == null)
                return false;

            _inputMap ??= _playerInput.actions.FindActionMap(actionMapName);

            if (_inputMap == null)
                return false;

            _inputMap.Enable();
            return true;
        }

        private bool CheckRangeCondition()
        {
            if (_allTargets.Count == 0) return false;

            Vector3 target = GetTargetValue();
            float distance = Vector3.Distance(transform.position, target);
        
            // Convert inches to meters (like AutoAlign does with 0.0254f)
            return distance <= activationRange * 0.0254f;
        }
    
        private float CalculateTargetAngle(Vector3 targetPos)
        {
            var targetAngle = transform.position - targetPos;

            // Atan2 returns the angle in radians between the positive x-axis and the point
            // We use the local X and Z coordinates
            float angleInRadians = Mathf.Atan2(targetAngle.x, targetAngle.z);

            // Convert to degrees for standard Unity rotation usage
            return angleInRadians * Mathf.Rad2Deg;
        }
    
        private Vector3 GetTargetValue()
        {
            switch (targetType)
            {
                case TargetType.Preset:
                    if (useAllianceTargets && _controller != null)
                    {
                        return _controller.isRed ? redTargetPosition : blueTargetPosition;
                    }

                    return targetPosition;

                case TargetType.Closest:
                    return GetClosestTarget();

                case TargetType.Furthest:
                    return GetFurthestTarget();

                case TargetType.Custom:
                    return GetClosestCustomTarget();
            }

            return Vector3.zero;
        }

        private Vector3 GetClosestCustomTarget()
        {
            if (extraTargets.Length == 0) return Vector3.zero;

            float closestDistance = float.MaxValue;
            Vector3 closestTarget = Vector3.zero;
            Vector3 originPos = transform.position;
    
            foreach (var target in extraTargets)
            {
                var distance = Vector3.Distance(originPos, target); 
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestTarget = target;
                }
            }
    
            return closestTarget;
        }

        private Vector3 GetClosestTarget()
        {
            if (_allTargets.Count == 0) return Vector3.zero;

            float closestDistance = float.MaxValue;
            Vector3 closestTarget = Vector3.zero;
            Vector3 originPos = transform.position;
    
            foreach (var target in _allTargets)
            {
                var distance = Vector3.Distance(originPos, target); 
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestTarget = target;
                }
            }
    
            return closestTarget;
        }

        private Vector3 GetFurthestTarget()
        {
            if (_allTargets.Count == 0) return Vector3.zero;

            float furthestDistance = float.MinValue;
            Vector3 furthestTarget = Vector3.zero;
            Vector3 originPos = transform.position;
    
            foreach (var target in _allTargets)
            {
                var distance = Vector3.Distance(originPos, target);
                if (distance > furthestDistance)
                {
                    furthestDistance = distance;
                    furthestTarget = target;
                }
            }
    
            return furthestTarget;
        }
    
        private void FindAimRegions()
        {
            _regions.Clear();

            var found = FindObjectsByType<AimRegion>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in found)
            {
                if (t != null)
                {
                    _regions.Add(t);
                }
            }
        }
    
        private bool IsInsideAllowedRegion()
        {
            if (!requireInsideRegion)
                return true;

            if (_regions.Count == 0)
                return false;

            if (_bumperColliders == null || _bumperColliders.Length == 0)
            {
                CacheBumperColliders();

                if (_bumperColliders == null || _bumperColliders.Length == 0)
                    return false;
            }

            foreach (var region in _regions)
            {
                if (region == null || region.RegionBox == null)
                    continue;

                if (!IsRegionAllowed(region.RegionId))
                    continue;

                foreach (var bumperCollider in _bumperColliders)
                {
                    if (bumperCollider == null || !bumperCollider.enabled)
                        continue;

                    if (IsColliderOverlappingRegion(region.RegionBox, bumperCollider))
                        return true;
                }
            }

            return false;
        }

        private bool IsRegionAllowed(AimRegionId regionId)
        {
            if (allowedRegions == null || allowedRegions.Length == 0)
                return false;

            foreach (var t in allowedRegions)
            {
                if (t == regionId)
                    return true;
            }

            return false;
        }

        private bool IsColliderOverlappingRegion(BoxCollider regionBox, Collider bumperCollider)
        {
            if (regionBox == null || bumperCollider == null)
                return false;

            return Physics.ComputePenetration(
                regionBox,
                regionBox.transform.position,
                regionBox.transform.rotation,
                bumperCollider,
                bumperCollider.transform.position,
                bumperCollider.transform.rotation,
                out _,
                out _
            );
        }
    }
}