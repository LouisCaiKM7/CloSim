using System;
using System.Collections;
using System.Collections.Generic;
using MyBox;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;
using Util;

public class PointAtTarget : MonoBehaviour
{
    [SerializeField] private TargetType targetType;
    
    [SerializeField] private TargetWhen targetWhen;
    
    [SerializeField] private TargetingMethod targetingMethod;
    
    [Header("Targeting Settings")]
    [ConditionalField(true, nameof(IsPreset))]
    [SerializeField] private bool useAlliancePreset;

    [ConditionalField(true, nameof(IsPreset))]
    [SerializeField] private Vector3 targetPosition;

    [ConditionalField(true, nameof(IsPreset))]
    [SerializeField] private Vector3 blueTargetPosition;

    [ConditionalField(true, nameof(IsPreset))]
    [SerializeField] private Vector3 redTargetPosition;

    [ConditionalField(true, nameof(WhenAtSetpoint))] [SerializeField]
    private string SetpointName;
    
    [ConditionalField(true, nameof(IsPreset), true)]
    [SerializeField] private Vector3[] extraTargets;

    [ConditionalField(true, nameof(IsPreset), true)]
    [SerializeField] private GameObject robotPosition;

    [Header("Tuning Settings")]
    [ConditionalField(true, nameof(IsInterpolating), true)] 
    [SerializeField] private float heightOffset;
    [ConditionalField(true, nameof(IsInterpolating), true)] 
    [SerializeField] private float angleOffset;

    [ConditionalField(true, nameof(IsInterpolating))] 
    [SerializeField] private DistanceValue[] interpolationTable;
    private bool IsPreset() => targetType == TargetType.Preset;
    private bool WhenAtSetpoint() => targetWhen == TargetWhen.AtSetpoint;
    private bool IsInterpolating() => targetingMethod == TargetingMethod.Interpolation;
    
    [Header("Stow / Bounds Override")]
    [SerializeField] private Vector2 xBounds = new Vector2(-8f, -4f);
    [SerializeField] private Vector2 leftZBounds = new Vector2(-3.5f, 0f);
    [SerializeField] private Vector2 rightZBounds = new Vector2(0f, 3.5f);

    private List<Vector3> _allTargets;
    
    private DistanceValue[] _sortedCache;
    
    private JointController _controller;
    private SwerveController _swerveController;

    private bool _lateStartup;
    // Start is called before the first frame update
    void Start()
    {
        InitializeCache();
        var foundTargets = Utils.FindGameObjectsOnLayer("AutoAngleNodes");

        _allTargets = new List<Vector3>();
        
        foreach (var target in foundTargets)
        {
            _allTargets.Add(target.transform.position); 
        }

        foreach (var target in extraTargets)
        {
            _allTargets.Add(target);
        }
        
        _lateStartup = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (_lateStartup)
        {
            _controller = GetComponent<BuildMechanism>().GetController();
            _swerveController = GetComponentInParent<SwerveController>();
            _lateStartup = false;
        }

        bool shouldTarget = targetWhen == TargetWhen.Always || 
                            (targetWhen == TargetWhen.AtSetpoint && 
                             String.Equals(
                                 (_controller.getActiveSetpoint() ?? "").ToLower().Trim(), 
                                 SetpointName.ToLower().Trim(), 
                                 StringComparison.OrdinalIgnoreCase));

        if (!shouldTarget) return;

        Vector3 target = GetTargetValue();
    
        float setpointValue;
    
        switch (targetingMethod)
        {
            case TargetingMethod.PointAtOffset:
                setpointValue = CalculateTargetAngle(target) + angleOffset;
                break;
            
            case TargetingMethod.Interpolation:
                Vector3 originPos = transform.position;
                float currentDistance = Vector3.Distance(originPos, target);
                setpointValue = Interpolate(currentDistance) + angleOffset;
                break;
            
            default:
                setpointValue = 0f;
                break;
        }

        if (!IsRobotInsideBounds())
        {
            setpointValue = 0;
        }
    
        _controller.OveridePosition(setpointValue);
    }
    
    //runs on editor change
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            UpdateTable(interpolationTable);
        }
    }
    
    //Get target
    private Vector3 GetTargetValue()
    {
        switch (targetType)
        {
            case TargetType.Preset:
                if (useAlliancePreset && _swerveController != null)
                    return _swerveController.isRed ? redTargetPosition : blueTargetPosition;
                return targetPosition;
            case TargetType.Closest:
                return getClosestTarget();
            case TargetType.Furthest:
                return getFurthestTarget();
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

    private Vector3 getClosestTarget()
    {
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

    private Vector3 getFurthestTarget()
    {
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

    //direct calculation stuff
    private float CalculateTargetAngle(Vector3 targetPos)
    {
        Transform refPoint = transform;
    
        targetPos -= heightOffset * Vector3.up;

        Vector3 localTarget = refPoint.parent.InverseTransformPoint(targetPos);
      
        float angleRad = Mathf.Atan2(localTarget.y, localTarget.z);
        float angleDeg = angleRad * Mathf.Rad2Deg;

        return angleDeg + angleOffset;
    }
    
    //Interpolation stuff
    private void InitializeCache()
    {
        if (interpolationTable == null || interpolationTable.Length == 0)
        {
            _sortedCache = Array.Empty<DistanceValue>();
            return;
        }

        _sortedCache = new DistanceValue[interpolationTable.Length];
    
        Array.Copy(interpolationTable, _sortedCache, interpolationTable.Length);

        Array.Sort(_sortedCache, new DistanceComparer());
    }
    
    private void UpdateTable(DistanceValue[] newData)
    {
        if (_sortedCache == null || _sortedCache.Length != newData.Length)
        {
            _sortedCache = new DistanceValue[newData.Length];
        }
        
        Array.Copy(newData, _sortedCache, newData.Length);
        Array.Sort(_sortedCache, (a, b) => a.distance.CompareTo(b.distance));
    }

    private float Interpolate(float currentDistance)
    {
        if (_sortedCache == null || _sortedCache.Length == 0) return 0f;

        int index = Array.BinarySearch(_sortedCache, new DistanceValue { distance = currentDistance }, new DistanceComparer());

        if (index >= 0) return _sortedCache[index].value;

        int nextIndex = ~index;

        if (nextIndex == 0) return _sortedCache[0].value;
        if (nextIndex >= _sortedCache.Length) return _sortedCache[_sortedCache.Length - 1].value;
        
        var lower = _sortedCache[nextIndex - 1];
        var upper = _sortedCache[nextIndex];
        float t = (currentDistance - lower.distance) / (upper.distance - lower.distance);
        return Mathf.Lerp(lower.value, upper.value, t);
    }

    [Serializable]
    public struct DistanceValue
    {
        public float distance;
        public float value;
    }
    
    public struct DistanceComparer : System.Collections.Generic.IComparer<DistanceValue>
    {
        public int Compare(DistanceValue x, DistanceValue y) => x.distance.CompareTo(y.distance);
    }

    private bool IsRobotInsideBounds()
    {
        if (robotPosition == null) return true;

        var t = robotPosition.transform;
        var p = t.position;
        float yRot = t.eulerAngles.y;

        // Basic X/Z bounds
        bool insideXZ =
            p.x >= xBounds.x && p.x <= xBounds.y &&
            p.z >= leftZBounds.x && p.z <= rightZBounds.y;

        if (!insideXZ)
            return false;

        // Now-leftZBounds actually corresponds to the RIGHT physical zone => 30..90
        if (p.z >= leftZBounds.x && p.z <= leftZBounds.y)
        {
            if (!IsAngleInRange(yRot, 5f, 90f))
                return false;
        }

        // Now-rightZBounds actually corresponds to the LEFT physical zone => 90..150
        if (p.z >= rightZBounds.x && p.z <= rightZBounds.y)
        {
            if (!IsAngleInRange(yRot, 90f, 175f))
                return false;
        }

        return true;
    }

    private bool IsAngleInRange(float angle, float min, float max)
    {
        angle = Mathf.Repeat(angle, 360f);
        min   = Mathf.Repeat(min, 360f);
        max   = Mathf.Repeat(max, 360f);

        if (min <= max)
            return angle >= min && angle <= max;

        return angle >= min || angle <= max;
    }
}
