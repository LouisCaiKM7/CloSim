using System;
using System.Collections.Generic;
using MyBox;
using UnityEngine;
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

    [ConditionalField(true, nameof(WhenAtSetpoint))]
    [SerializeField] private string SetpointName;

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

    [Header("Region Filtering")]
    [SerializeField] private bool requireInsideRegion = false;
    [SerializeField] private Transform positionReference;
    [SerializeField] private AimRegionId[] allowedRegions;
    [SerializeField] private bool goToHome = false;

    private bool IsPreset() => targetType == TargetType.Preset;
    private bool WhenAtSetpoint() => targetWhen == TargetWhen.AtSetpoint;
    private bool IsInterpolating() => targetingMethod == TargetingMethod.Interpolation;

    private List<Vector3> _allTargets;
    private DistanceValue[] _sortedCache;

    private JointController _controller;
    private SwerveController _swerveController;
    private readonly List<AimRegion> _regions = new List<AimRegion>();

    private bool _lateStartup;

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

        if (positionReference == null)
        {
            if (robotPosition != null)
                positionReference = robotPosition.transform;
            else
                positionReference = transform;
        }

        FindAimRegions();

        _lateStartup = true;
    }

    void Update()
    {
        if (_lateStartup)
        {
            var mechanism = GetComponent<BuildMechanism>();
            if (mechanism != null)
            {
                _controller = mechanism.GetController();
            }

            _swerveController = GetComponentInParent<SwerveController>();
            _lateStartup = false;
        }

        if (_controller == null)
            return;

        if (requireInsideRegion && _regions.Count == 0)
        {
            FindAimRegions();
        }

        bool shouldTarget =
            targetWhen == TargetWhen.Always ||
            (targetWhen == TargetWhen.AtSetpoint &&
             String.Equals(
                 (_controller.getActiveSetpoint() ?? "").ToLower().Trim(),
                 SetpointName.ToLower().Trim(),
                 StringComparison.OrdinalIgnoreCase));

        if (!shouldTarget)
            return;

        Vector3 target = GetTargetValue();

        float setpointValue;

        switch (targetingMethod)
        {
            case TargetingMethod.PointAtOffset:
                if (IsInsideAllowedRegion())
                    setpointValue = CalculateTargetAngle(target) + angleOffset;
                else if (goToHome)
                    setpointValue = 0;
                else
                {
                    setpointValue = -90;
                }
                
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

        _controller.OveridePosition(setpointValue);
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            UpdateTable(interpolationTable);
        }
    }

    private Vector3 GetTargetValue()
    {
        switch (targetType)
        {
            case TargetType.Preset:
                if (useAlliancePreset && _swerveController != null)
                    return _swerveController.isRed ? redTargetPosition : blueTargetPosition;

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
        if (extraTargets == null || extraTargets.Length == 0)
            return Vector3.zero;

        float closestDistance = float.MaxValue;
        Vector3 closestTarget = Vector3.zero;
        Vector3 originPos = transform.position;

        foreach (var target in extraTargets)
        {
            float distance = Vector3.Distance(originPos, target);
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
        if (_allTargets == null || _allTargets.Count == 0)
            return Vector3.zero;

        float closestDistance = float.MaxValue;
        Vector3 closestTarget = Vector3.zero;
        Vector3 originPos = transform.position;

        foreach (var target in _allTargets)
        {
            float distance = Vector3.Distance(originPos, target);
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
        if (_allTargets == null || _allTargets.Count == 0)
            return Vector3.zero;

        float furthestDistance = float.MinValue;
        Vector3 furthestTarget = Vector3.zero;
        Vector3 originPos = transform.position;

        foreach (var target in _allTargets)
        {
            float distance = Vector3.Distance(originPos, target);
            if (distance > furthestDistance)
            {
                furthestDistance = distance;
                furthestTarget = target;
            }
        }

        return furthestTarget;
    }

    private float CalculateTargetAngle(Vector3 targetPos)
    {
        Transform refPoint = transform;

        targetPos -= heightOffset * Vector3.up;

        Vector3 localTarget = refPoint.parent.InverseTransformPoint(targetPos);

        float angleRad = Mathf.Atan2(localTarget.y, localTarget.z);
        float angleDeg = angleRad * Mathf.Rad2Deg;

        return angleDeg + angleOffset;
    }

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
        if (newData == null)
        {
            _sortedCache = Array.Empty<DistanceValue>();
            return;
        }

        if (_sortedCache == null || _sortedCache.Length != newData.Length)
        {
            _sortedCache = new DistanceValue[newData.Length];
        }

        Array.Copy(newData, _sortedCache, newData.Length);
        Array.Sort(_sortedCache, (a, b) => a.distance.CompareTo(b.distance));
    }

    private float Interpolate(float currentDistance)
    {
        if (_sortedCache == null || _sortedCache.Length == 0)
            return 0f;

        int index = Array.BinarySearch(
            _sortedCache,
            new DistanceValue { distance = currentDistance },
            new DistanceComparer());

        if (index >= 0)
            return _sortedCache[index].value;

        int nextIndex = ~index;

        if (nextIndex == 0)
            return _sortedCache[0].value;

        if (nextIndex >= _sortedCache.Length)
            return _sortedCache[_sortedCache.Length - 1].value;

        var lower = _sortedCache[nextIndex - 1];
        var upper = _sortedCache[nextIndex];

        float t = (currentDistance - lower.distance) / (upper.distance - lower.distance);
        return Mathf.Lerp(lower.value, upper.value, t);
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
        {
            if (robotPosition != null)
                positionReference = robotPosition.transform;
            else
                positionReference = transform;
        }

        if (_regions.Count == 0)
            return false;

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

    [Serializable]
    public struct DistanceValue
    {
        public float distance;
        public float value;
    }

    public struct DistanceComparer : IComparer<DistanceValue>
    {
        public int Compare(DistanceValue x, DistanceValue y) => x.distance.CompareTo(y.distance);
    }
}