using System;
using System.Collections.Generic;
using MyBox;
using UnityEngine;
using Util;

public class SpawnGamePiece : MonoBehaviour
{
    [Header("Piece Settings")]
    [SerializeField] private PieceNames peiceType;
    [SerializeField] private Direction velocityDirection;
    [SerializeField] private float velocity;
    
    [Header("Sliding Settings")]
    [SerializeField] private bool axisSlides;
    [ConditionalField(nameof(axisSlides))]
    [SerializeField] private Direction direction;
    [ConditionalField(nameof(axisSlides))]
    [SerializeField] private float maxSlideDistance;

    [Header("Spawn Control")]
    [SerializeField] private float delayTimer;
    
    [Header("Threshold Spawn Settings")]
    [SerializeField] private BoxCollider detectionVolume; 
    [SerializeField] private int thresholdCount = 1;

    private static Dictionary<PieceNames, GameObject> _piecesMap;
    public static readonly List<SpawnPieceTarget> Targets = new List<SpawnPieceTarget>();
    
    private float _lastSpawnTime = -999f;  // Track last spawn time instead of boolean
    private int _pieceMask;
    
    private Collider[] _overlapResults;
    private readonly List<GamePiece> _currentGamePieces = new List<GamePiece>();
    
    private Vector3 _cachedForwardVelocity;
    private Vector3 _cachedSidewaysVelocity;
    private Vector3 _cachedUpVelocity;
    
    void Awake()
    {
        if (_piecesMap == null)
        {
            _piecesMap = new Dictionary<PieceNames, GameObject>();
            var pieces = Resources.LoadAll<GameObject>("Pieces");
            
            foreach (var piece in pieces)
            {
                var gamePieceComponent = piece.GetComponent<GamePiece>();
                if (gamePieceComponent)
                {
                    _piecesMap[gamePieceComponent.pieceType] = piece;
                }
            }
        }
        
        _overlapResults = new Collider[20 * thresholdCount];
        _pieceMask = LayerMask.GetMask("Piece");
        
        _cachedForwardVelocity = Vector3.forward * velocity;
        _cachedSidewaysVelocity = Vector3.right * velocity;
        _cachedUpVelocity = Vector3.up * velocity;
    }

    private bool CanSpawn()
    {
        return Time.time >= _lastSpawnTime + delayTimer;
    }

    private void SpawnPiece(PieceNames pieceTypeEnum, float velocityValue, Vector3 spawnPosition)
    {
        if (!CanSpawn() || !_piecesMap.TryGetValue(pieceTypeEnum, out GameObject piecePrefab)) 
            return;

        var item = Instantiate(piecePrefab, spawnPosition, transform.rotation, transform)
            .GetComponent<GamePiece>();
    
        // Use local directions transformed to world space
        Vector3 finalVelocity;
    
        if (Mathf.Approximately(velocityValue, velocity))
        {
            finalVelocity = velocityDirection switch
            {
                Direction.forward => transform.forward * velocity,
                Direction.sideways => transform.right * velocity,
                Direction.up => transform.up * velocity,
                _ => transform.forward * velocity
            };
        }
        else
        {
            finalVelocity = velocityDirection switch
            {
                Direction.forward => transform.forward * velocityValue,
                Direction.sideways => transform.right * velocityValue,
                Direction.up => transform.up * velocityValue,
                _ => transform.forward * velocityValue
            };
        }
    
        item.rb.velocity = finalVelocity;
        _lastSpawnTime = Time.time;  // Record spawn time
    }

    private Vector3 GetClosestPointOnAxis(Vector3 targetPosition, Direction slideDirection, float maxSlideDistance)
    {
        Vector3 spawnerPos = transform.position;

        // Get the spawner's local axis in world space
        Vector3 axisDirection = slideDirection switch
        {
            Direction.sideways => transform.right,   // Local X axis
            Direction.forward => transform.forward,  // Local Z axis
            Direction.up => transform.up,            // Local Y axis
            _ => transform.forward
        };

        // Project the vector from spawner to target onto the axis
        Vector3 toTarget = targetPosition - spawnerPos;
        float projectionLength = Vector3.Dot(toTarget, axisDirection);

        // Clamp the distance so it stays within [-maxSlideDistance, maxSlideDistance]
        float clampedDistance = Mathf.Clamp(projectionLength, -maxSlideDistance, maxSlideDistance);

        // Return the point along the spawner's axis within the bounds
        return spawnerPos + axisDirection * clampedDistance;
    }

    private bool CheckInternalThreshold()
    {
        if (!detectionVolume) return false;

        _currentGamePieces.Clear();
        
        int numColliders = Physics.OverlapBoxNonAlloc(
            detectionVolume.transform.position, 
            detectionVolume.size * 0.5f,
            _overlapResults,
            detectionVolume.transform.rotation, 
            _pieceMask
        );

        for (int i = 0; i < numColliders; i++)
        {
            var piece = Utils.FindParentObjectComponent<GamePiece>(_overlapResults[i].gameObject);
            
            if (!piece || piece.pieceType != peiceType || piece.state != GamePieceState.World) 
                continue;
            
            if (!_currentGamePieces.Contains(piece))
            {
                _currentGamePieces.Add(piece);
            }
        }

        return _currentGamePieces.Count >= thresholdCount;
    }

    void FixedUpdate()
    {
        if (!CanSpawn()) return;  // Check timer at the start

        bool shouldSpawn = false;
        float targetVelocity = velocity;
        Vector3 spawnPosition = transform.position;
        bool hasDistanceTargets = false;

        // Check distance-based targets first
        for (int i = 0; i < Targets.Count; i++)
        {
            var target = Targets[i];
            if (!target) continue;

            if (target.spawnType == SpawnType.Distance)
            {
                hasDistanceTargets = true;
                
                Vector3 effectiveSpawnPosition = axisSlides 
                    ? GetClosestPointOnAxis(target.transform.position, direction, maxSlideDistance)
                    : transform.position;
                
                float distanceSq = (effectiveSpawnPosition - target.transform.position).sqrMagnitude;
                
                if (distanceSq <= target.SpawnDistance * target.SpawnDistance)
                {
                    shouldSpawn = true;
                    targetVelocity = target.Velocity;
                    spawnPosition = effectiveSpawnPosition;
                    break;
                }
            }
        }
        
        // If distance targets exist, only handle distance spawning
        if (hasDistanceTargets)
        {
            if (shouldSpawn)
            {
                SpawnPiece(peiceType, targetVelocity, spawnPosition);
            }
            return;  // Early return to prevent checking threshold logic
        }
        
        // No distance targets - check threshold-based targets
        for (int i = 0; i < Targets.Count; i++)
        {
            var target = Targets[i];
            if (!target) continue;

            if (target.spawnType == SpawnType.Threshold)
            {
                if (!CheckInternalThreshold())
                {
                    shouldSpawn = true;
                    targetVelocity = target.Velocity;
                    spawnPosition = transform.position;
                    break;
                }
            }
        }
        
        if (shouldSpawn)
        {
            SpawnPiece(peiceType, targetVelocity, spawnPosition);
            return;
        }

        // Final fallback threshold check (only if no targets at all)
        if (!CheckInternalThreshold())
        {
            SpawnPiece(peiceType, velocity, transform.position);
        }
    }
}