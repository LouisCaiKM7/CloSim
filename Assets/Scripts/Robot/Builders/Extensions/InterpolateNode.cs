using System;
using System.Collections.Generic;
using Core;
using MyBox;
using Robot.Runtime;
using UnityEngine;
using UnityEngine.Serialization;
using Utilities;

namespace Robot.Builders.Extensions
{
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

        [FormerlySerializedAs("SetpointName")] [ConditionalField(true, nameof(WhenAtSetpoint))] [SerializeField]
        private string setpointName;

        [ConditionalField(true, nameof(IsPreset), true)]
        [SerializeField] private Vector3[] extraTargets;

        [Header("Tuning Settings")]
        [SerializeField] private PointAtTarget.DistanceValue[] interpolationTable;

        private bool IsPreset() => targetType == TargetType.Preset;
        private bool WhenAtSetpoint() => targetWhen == TargetWhen.AtSetpoint;
        private bool UseAllianceTargets() => targetType == TargetType.Preset && useAllianceTargets;

        private BuildMechanism _targetMechanism;
        private BuildNode _targetNode;
        private SwerveController _swerveController;
        private readonly List<Vector3> _allTargets = new List<Vector3>();
        private PointAtTarget.DistanceValue[] _sortedCache;

        private readonly Dictionary<string, NodeAction> _actionLookup = new Dictionary<string, NodeAction>();
        private void Start()
        {
            InitializeCache();
            if (!Application.isPlaying) return;
            _targetNode = GetComponent<BuildNode>();
            foreach (var action in _targetNode.actions)
            {
                _actionLookup.Add(action.name, action);
            }
        
            var foundTargets = Utils.FindGameObjectsOnLayer("AutoAngleNodes");
        
            foreach (var target in foundTargets)
            {
                _allTargets.Add(target.transform.position); 
            }

        
            _allTargets.AddRange(extraTargets);
            _targetMechanism = Utils.FindParentObjectComponent<BuildMechanism>(gameObject);
            _swerveController = GetComponentInParent<SwerveController>();
        
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                _targetMechanism = Utils.FindParentObjectComponent<BuildMechanism>(gameObject);
                _targetNode = GetComponent<BuildNode>();

                if (!_targetNode) return;
                foreach (var action in _targetNode.actions)
                {
                    if (action.type != NodeType.OutTake) continue;
                    if (!targetOuttake.canBeSelected.Contains(action.name))
                    {
                        targetOuttake.canBeSelected.Add(action.name);
                    }
                }
            }

            if (!Application.isPlaying) return;
            var currentSetpoint = "";
            if (_targetMechanism && _targetMechanism.GetController())
            {
                currentSetpoint = _targetMechanism.GetController().GetActiveSetpoint();
            }
            else if (targetWhen == TargetWhen.AtSetpoint)
            {
                return;
            }

            bool shouldTarget = targetWhen == TargetWhen.Always || 
                                (targetWhen == TargetWhen.AtSetpoint && 
                                 String.Equals(
                                     (currentSetpoint ?? "").ToLower().Trim(), 
                                     setpointName.ToLower().Trim(), 
                                     StringComparison.OrdinalIgnoreCase));
        
            if (!shouldTarget) return;
        
            Vector3 target = GetTargetValue();

            Vector3 originPos = transform.position;
            float currentDistance = Vector3.Distance(originPos, target);
            var speed = Interpolate(currentDistance);

            _actionLookup.TryGetValue(targetOuttake.selectedName, out var nodeAction);
            if (nodeAction != null) nodeAction.overrideSpeed = (speed);
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
            return output;
        }
    }
}