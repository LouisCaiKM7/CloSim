using System.Collections.Generic;
using Core;
using Field.Core;
using Robot.Builders.Extensions;
using Robot.Runtime;
using UnityEngine;

namespace Field.SeasonSpecific.Rebuilt
{
    public class LaunchZonePenalty : MonoBehaviour
    {
        [Header("Region Check")]
        [SerializeField] private Transform bumperRoot;
        [SerializeField] private AimRegionId blueAimRegion;
        [SerializeField] private AimRegionId redAimRegion;

        [Header("Optional Filtering")]
        [SerializeField] private PieceNames[] penalizedPieces;

        private SwerveController _swerve;
        private readonly List<AimRegion> _regions = new List<AimRegion>();
        private readonly HashSet<PieceNames> _penalizedPieceSet = new HashSet<PieceNames>();
        private Collider[] _bumperColliders = System.Array.Empty<Collider>();


        private void Awake()
        {
            if (bumperRoot == null)
                bumperRoot = transform;

            CacheBumperColliders();

            _penalizedPieceSet.Clear();

            if (penalizedPieces != null)
            {
                foreach (var piece in penalizedPieces)
                    _penalizedPieceSet.Add(piece);
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

            if (Fms.RobotState != RobotState.Enabled)
                return;

            if (_penalizedPieceSet.Count > 0 && !_penalizedPieceSet.Contains(piece.pieceType))
                return;

            if (_regions.Count == 0)
                FindAimRegions();

            AimRegionId requiredRegion = robotIsRed ? redAimRegion : blueAimRegion;

            piece.g407IllegalLaunch = !IsInsideRegion(requiredRegion);
            piece.g407PenaltyAssessed = false;
        }

        private void FindAimRegions()
        {
            _regions.Clear();

            var found = FindObjectsByType<AimRegion>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            foreach (var region in found)
            {
                if (region != null)
                    _regions.Add(region);
            }
        }

        private bool IsInsideRegion(AimRegionId requiredRegion)
        {
            if (_regions.Count == 0)
                FindAimRegions();

            if (_bumperColliders == null || _bumperColliders.Length == 0)
            {
                CacheBumperColliders();

                if (_bumperColliders == null || _bumperColliders.Length == 0)
                {
                    return false;
                }
            }

            foreach (var region in _regions)
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
}