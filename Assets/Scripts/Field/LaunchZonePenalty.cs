using System.Collections.Generic;
using UnityEngine;
using Util;

public class LaunchZonePenalty : MonoBehaviour
{
    [Header("Region Check")]
    [SerializeField] private Transform bumperRoot;
    [SerializeField] private AimRegionId blueAimRegion;
    [SerializeField] private AimRegionId redAimRegion;

    [Header("Optional Filtering")]
    [SerializeField] private PieceNames[] penalizedPieces;

    private SwerveController swerve;
    private readonly List<AimRegion> regions = new List<AimRegion>();
    private readonly HashSet<PieceNames> penalizedPieceSet = new HashSet<PieceNames>();
    private Collider[] _bumperColliders = System.Array.Empty<Collider>();


    private void Awake()
    {
        if (bumperRoot == null)
            bumperRoot = transform;

        CacheBumperColliders();

        penalizedPieceSet.Clear();

        if (penalizedPieces != null)
        {
            foreach (var piece in penalizedPieces)
                penalizedPieceSet.Add(piece);
        }

        FindAimRegions();
    }
    
    private void CacheBumperColliders()
    {
        if (bumperRoot == null)
        {
            _bumperColliders = System.Array.Empty<Collider>();
            return;
        }

        _bumperColliders = bumperRoot.GetComponentsInChildren<Collider>(true);
    }

    public void MarkLaunchIfIllegal(GamePiece piece, bool robotIsRed)
    {
        if (piece == null)
            return;

        if (FMS.RobotState != RobotState.enabled)
            return;

        if (penalizedPieceSet.Count > 0 && !penalizedPieceSet.Contains(piece.pieceType))
            return;

        if (regions.Count == 0)
            FindAimRegions();

        AimRegionId requiredRegion = robotIsRed ? redAimRegion : blueAimRegion;

        piece.g407IllegalLaunch = !IsInsideRegion(requiredRegion);
        piece.g407PenaltyAssessed = false;
    }

    private void FindAimRegions()
    {
        regions.Clear();

        var found = FindObjectsByType<AimRegion>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (var region in found)
        {
            if (region != null)
                regions.Add(region);
        }
    }

    private bool IsInsideRegion(AimRegionId requiredRegion)
    {
        if (regions.Count == 0)
            FindAimRegions();

        if (_bumperColliders == null || _bumperColliders.Length == 0)
        {
            CacheBumperColliders();

            if (_bumperColliders == null || _bumperColliders.Length == 0)
            {
                return false;
            }
        }

        foreach (var region in regions)
        {
            if (region == null || region.RegionBox == null)
                continue;

            if (region.RegionId != requiredRegion)
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