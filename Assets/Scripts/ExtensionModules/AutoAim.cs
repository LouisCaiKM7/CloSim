using System;
using System.Collections.Generic;
using MyBox;
using UnityEngine;
using UnityEngine.InputSystem;
using Util;

public class AutoAim : MonoBehaviour
{
    [SerializeField] private TargetType targetType;
    [SerializeField] private AimAtWhen targetWhen;
    
    [Header("Targeting Settings")]
    [ConditionalField(true, nameof(IsPreset))]
    [SerializeField] private Vector3 targetPosition;
    
    [Header("Alliance Passing Targets")]
    [SerializeField] private bool useAllianceTargets;

    [SerializeField] private Vector3 blueTargetPosition;
    [SerializeField] private Vector3 redTargetPosition;

    [ConditionalField(true, nameof(WhenAtSetpoint))] 
    [SerializeField] private BuildMechanism drivingMechanism;
    
    [ConditionalField(true, nameof(WhenAtSetpoint))] 
    [SerializeField] private string SetpointName;
    
    [ConditionalField(true, nameof(WhenAtSetpoint))]
    [SerializeField] private string connectedTo = "none";

    [ConditionalField(true, nameof(IsPreset), true)]
    [SerializeField] private Vector3[] extraTargets;

    [Header("Button Control Settings")]
    [ConditionalField(true, nameof(WhenButton))]
    [SerializeField] private ControllerInputs controllerButton;

    [ConditionalField(true, nameof(WhenButton))]
    [SerializeField] private KeyboardInputs keyboardButton;

    [Header("Range Settings")]
    [ConditionalField(true, nameof(WhenWithinRange))]
    [SerializeField] private float activationRange = 20f; // in inches

    [Header("PID Tuning")]
    [SerializeField] private bool advanced;

    [ConditionalField(nameof(advanced))]
    [SerializeField] private PID steeringPID;
    
    [Header("Region Filtering")]
    [SerializeField] private bool requireInsideRegion = false;
    [SerializeField] private Transform positionReference;
    [SerializeField] private AimRegionId[] allowedRegions;

    [Header("Debug Info")]
    [ConditionalField(true, nameof(IsPlaying))]
    [SerializeField] private float Distance;
    
    [ConditionalField(true, nameof(IsPlaying))]
    [SerializeField] private float Output;
    
    [ConditionalField(true, nameof(IsPlaying))]
    [SerializeField] private float CurrentAngle;
    
    [ConditionalField(true, nameof(IsPlaying))]
    [SerializeField] private float TargetAngle;

    private bool IsPreset() => targetType == TargetType.Preset;
    private bool WhenAtSetpoint() => targetWhen == AimAtWhen.AtSetpoint;
    private bool WhenButton() => targetWhen == AimAtWhen.WhenPressing;
    private bool WhenWithinRange() => targetWhen == AimAtWhen.WithinRange;
    private bool IsPlaying() => Application.isPlaying;

    private SwerveController controller;
    private PIDController _steeringPIDController;
    private PlayerInput _playerInput;
    private InputActionMap _inputMap;
    private List<Vector3> _allTargets = new List<Vector3>();
    
    private readonly List<AimRegion> _regions = new List<AimRegion>();

    private void Start()
    {
        if (!Application.isPlaying) return;
        
        var foundTargets = Utils.FindGameObjectsOnLayer("AutoAngleNodes");
        
        foreach (var target in foundTargets)
        {
            _allTargets.Add(target.transform.position); 
        }

        controller = GetComponent<SwerveController>();
        _allTargets.AddRange(extraTargets);

        // Initialize PID controller for steering
        if (advanced)
        {
            _steeringPIDController = new PIDController
            {
                proportionalGain = steeringPID.p,
                derivativeGain = steeringPID.d,
                integralGain = steeringPID.i,
                outputMax = Mathf.Clamp(steeringPID.max, 0, 1),
                outputMin = -Mathf.Clamp(steeringPID.max, 0, 1),
                integralSaturation = 1
            };
        }
        else
        {
            _steeringPIDController = new PIDController
            {
                proportionalGain = 0.5f,
                derivativeGain = 0.05f,
                integralGain = 0,
                outputMax = 0.5f,
                outputMin = -0.5f,
                integralSaturation = 1
            };
        }
        
        if (positionReference == null)
            positionReference = transform;

        FindAimRegions();
    }

    private void FixedUpdate()
    {
        connectedTo = drivingMechanism ? drivingMechanism.name : "none";

        if (!Application.isPlaying) return;

        // Initialize PlayerInput if needed (similar to AutoAlign pattern)
        if (_playerInput == null && targetWhen == AimAtWhen.WhenPressing)
        {
            _playerInput = gameObject.GetComponent<PlayerInput>();
            if (_playerInput != null)
            {
                _inputMap = _playerInput.actions.FindActionMap("Robot");
                _inputMap?.Enable();
            }
            return;
        }
        
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
        float targetAngle = CalculateTargetAngle(target) + 180;
        float currentAngle = transform.localRotation.eulerAngles.y;
        
        // Use PID controller to calculate smooth steering output
        float pidOutput = _steeringPIDController.UpdateAngle(
            Time.fixedDeltaTime, 
            currentAngle, 
            targetAngle
        );
        
        // Update debug values
        Distance = Vector3.Distance(transform.position, target) * 39.37008f;
        CurrentAngle = currentAngle;
        TargetAngle = targetAngle;
        Output = pidOutput;

        controller.OverideSteer(pidOutput, true);
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

        var currentSetpoint = drivingMechanism.GetController().getActiveSetpoint();
        
        return String.Equals(
            (currentSetpoint ?? "").ToLower().Trim(), 
            SetpointName.ToLower().Trim(), 
            StringComparison.OrdinalIgnoreCase);
    }

    private bool CheckButtonCondition()
    {
        if (_inputMap == null) return false;

        var controllerAction = _inputMap.FindAction(controllerButton.ToString());
        var keyboardAction = _inputMap.FindAction(keyboardButton.ToString());
        
        var controllerHeld = controllerAction != null && 
                            controllerAction.IsPressed() && 
                            (controllerAction.activeControl?.device is Gamepad);
                            
        var keyboardHeld = keyboardAction != null && 
                          keyboardAction.IsPressed() && 
                          (keyboardAction.activeControl?.device is Keyboard);
        
        return controllerHeld || keyboardHeld;
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
                if (useAllianceTargets && controller != null)
                {
                    return controller.isRed ? redTargetPosition : blueTargetPosition;
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
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
            {
                _regions.Add(found[i]);
            }
        }
    }
    
    private bool IsInsideAllowedRegion()
    {
        if (!requireInsideRegion)
            return true;

        if (positionReference == null)
            positionReference = transform;

        if (_regions.Count == 0)
            return true;

        Vector3 point = positionReference.position;

        for (int i = 0; i < _regions.Count; i++)
        {
            var region = _regions[i];
            if (region == null || region.RegionBox == null)
                continue;

            if (!IsRegionAllowed(region.RegionId))
                continue;

            if (IsPointInsideBox(region.RegionBox, point))
                return true;
        }

        return false;
    }

    private bool IsRegionAllowed(AimRegionId regionId)
    {
        if (allowedRegions == null || allowedRegions.Length == 0)
            return false;

        for (int i = 0; i < allowedRegions.Length; i++)
        {
            if (allowedRegions[i] == regionId)
                return true;
        }

        return false;
    }

    private bool IsPointInsideBox(BoxCollider box, Vector3 worldPoint)
    {
        Vector3 localPoint = box.transform.InverseTransformPoint(worldPoint) - box.center;
        Vector3 halfSize = box.size * 0.5f;

        return Mathf.Abs(localPoint.x) <= halfSize.x &&
               Mathf.Abs(localPoint.y) <= halfSize.y &&
               Mathf.Abs(localPoint.z) <= halfSize.z;
    }
}