using System;
using System.Collections.Generic;
using BuilderLib;
using MyBox;
using UnityEngine;
using Util;

[ExecuteAlways]
public class InterpolateNode: MonoBehaviour
{
    [SerializeField] private TargetType targetType;

    [SerializeField] private TargetWhen targetWhen;

    [SerializeField] private InspectorDropdown targetOuttake;
    
    [Header("Targeting Settings")]
    [ConditionalField(true, nameof(IsPreset))]
    [SerializeField] private Vector3 targetPosition;

    [Header("Alliance Passing Targets")]
    [ConditionalField(true, nameof(IsPreset))]
    [SerializeField] private bool useAllianceTargets;

    [ConditionalField(true, nameof(UseAllianceTargets))]
    [SerializeField] private Vector3 blueTargetPosition;

    [ConditionalField(true, nameof(UseAllianceTargets))]
    [SerializeField] private Vector3 redTargetPosition;

    [ConditionalField(true, nameof(WhenAtSetpoint))] [SerializeField]
    private string SetpointName;
    [ConditionalField(true, nameof(WhenAtSetpoint))]
    [SerializeField] private string connectedTo = "none";

    [ConditionalField(true, nameof(IsPreset), true)]
    [SerializeField] private Vector3[] extraTargets;

    [Header("Tuning Settings")]
    [SerializeField] private PointAtTarget.DistanceValue[] interpolationTable;

    [ConditionalField(true, nameof(IsPlaying))]
    [SerializeField] private float Distance;
    [ConditionalField(true, nameof(IsPlaying))]
    [SerializeField] private float Output;
    private bool IsPreset() => targetType == TargetType.Preset;
    private bool WhenAtSetpoint() => targetWhen == TargetWhen.AtSetpoint;
    private bool UseAllianceTargets() => targetType == TargetType.Preset && useAllianceTargets;
    
    private bool IsPlaying() => Application.isPlaying;

    private BuildMechanism targetMechanism;
    private BuildNode targetNode;
    private SwerveController _swerveController;
    private List<Vector3> _allTargets = new List<Vector3>();
    private PointAtTarget.DistanceValue[] _sortedCache;

    private Dictionary<string, NodeAction> actionLookup = new Dictionary<string, NodeAction>();
    private void Start()
    {
        InitializeCache();
        if (!Application.isPlaying) return;
        targetNode = GetComponent<BuildNode>();
        foreach (var action in targetNode.Actions)
        {
            actionLookup.Add(action.Name, action);
        }
        
        var foundTargets = Utils.FindGameObjectsOnLayer("AutoAngleNodes");
        
        foreach (var target in foundTargets)
        {
            _allTargets.Add(target.transform.position); 
        }

        
        _allTargets.AddRange(extraTargets);
        targetMechanism = Utils.FindParentObjectComponent<BuildMechanism>(gameObject);
        _swerveController = GetComponentInParent<SwerveController>();
        
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            targetMechanism = Utils.FindParentObjectComponent<BuildMechanism>(gameObject);
            targetNode = GetComponent<BuildNode>();

            if (!targetNode) return;
            foreach (var action in targetNode.Actions)
            {
                if (action.Type != NodeType.Outake) continue;
                if (!targetOuttake.canBeSelected.Contains(action.Name))
                {
                    targetOuttake.canBeSelected.Add(action.Name);
                }
            }
        }
        
        connectedTo = targetMechanism ? targetMechanism.name : "none";

        if (!Application.isPlaying) return;
        var currentSetpoint = "";
        if (targetMechanism && targetMechanism.GetController())
        {
            currentSetpoint = targetMechanism.GetController().getActiveSetpoint();
        }
        else if (targetWhen == TargetWhen.AtSetpoint)
        {
            return;
        }

        bool shouldTarget = targetWhen == TargetWhen.Always || 
                            (targetWhen == TargetWhen.AtSetpoint && 
                             String.Equals(
                                 (currentSetpoint ?? "").ToLower().Trim(), 
                                 SetpointName.ToLower().Trim(), 
                                 StringComparison.OrdinalIgnoreCase));
        
        if (!shouldTarget) return;
        
        Vector3 target = GetTargetValue();
    
        float speed;
    
        Vector3 originPos = transform.position;
        float currentDistance = Vector3.Distance(originPos, target);
        speed = Interpolate(currentDistance);

        actionLookup.TryGetValue(targetOuttake.selectedName, out var nodeAction); 
        nodeAction.overideSpeed = (speed);
    }
    
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
                if (useAllianceTargets)
                {
                    if (_swerveController == null)
                    {
                        _swerveController = GetComponentInParent<SwerveController>();
                    }

                    if (_swerveController != null)
                    {
                        return _swerveController.isRed ? redTargetPosition : blueTargetPosition;
                    }
                }

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
    
    //Interpolation stuff
    private void InitializeCache()
    {
        if (interpolationTable == null || interpolationTable.Length == 0)
        {
            _sortedCache = Array.Empty<PointAtTarget.DistanceValue>();
            return;
        }

        // Allocate the cache array exactly once
        _sortedCache = new PointAtTarget.DistanceValue[interpolationTable.Length];
    
        // Copy the serialized data to our working cache
        Array.Copy(interpolationTable, _sortedCache, interpolationTable.Length);

        // Sort the cache immediately to enable Binary Search
        // Using the struct comparer prevents boxing allocations
        Array.Sort(_sortedCache, new PointAtTarget.DistanceComparer());
    }
    
    private void UpdateTable(PointAtTarget.DistanceValue[] newData)
    {
        // Avoid re-allocating if the size hasn't changed
        if (_sortedCache == null || _sortedCache.Length != newData.Length)
        {
            _sortedCache = new PointAtTarget.DistanceValue[newData.Length];
        }
        
        Array.Copy(newData, _sortedCache, newData.Length);
        Array.Sort(_sortedCache, (a, b) => a.distance.CompareTo(b.distance));
    }

    private float Interpolate(float currentDistance)
    {
        Distance = currentDistance;
        if (_sortedCache == null || _sortedCache.Length == 0) return 0f;

        // BinarySearch on a struct array is O(log n) and zero GC
        int index = Array.BinarySearch(_sortedCache, new PointAtTarget.DistanceValue { distance = currentDistance }, new PointAtTarget.DistanceComparer());

        if (index >= 0) return _sortedCache[index].value;

        int nextIndex = ~index;

        // Handle bounds
        if (nextIndex == 0) return _sortedCache[0].value;
        if (nextIndex >= _sortedCache.Length) return _sortedCache[^1].value;
        
        var lower = _sortedCache[nextIndex - 1];
        var upper = _sortedCache[nextIndex];
        float t = (currentDistance - lower.distance) / (upper.distance - lower.distance);
        var output = Mathf.Lerp(lower.value, upper.value, t);
        Output = output;
        return output;
    }
}