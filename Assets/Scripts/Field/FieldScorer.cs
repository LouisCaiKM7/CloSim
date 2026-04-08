using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MyBox;
using UnityEngine;
using Util;

public class FieldScorer : MonoBehaviour
{
    [Tooltip("this will display a blue box around the scoring node when in the editor")]
    [SerializeField] private bool displayDebugBox = false;
    [SerializeField] private bool isBlue;
    [SerializeField] private int scoreToAdd;
    [SerializeField] private int autoScoreToAdd;
    [SerializeField] protected PieceNames[] scorePieces;
    private readonly HashSet<PieceNames> scorePiecesSet = new HashSet<PieceNames>();
    [SerializeField] private Collider[] occupyColliders;
    private readonly HashSet<GamePiece> uniquePieces = new HashSet<GamePiece>();
    private Vector3[] halfExtents;
    protected List<GamePiece> occupyObjects = new List<GamePiece>();
    private List<GamePiece> pieces = new List<GamePiece>();
    private LayerMask peiceMask;

    private int lastAddedPoints;

    private int scoredInAuto;

    private void OnEnable()
    {
        occupyObjects = new List<GamePiece>();
        scoredInAuto = 0;
        halfExtents = new Vector3[occupyColliders.Length];
        scorePiecesSet.Clear();
        
        foreach (var coll in occupyColliders) 
        {
            Vector3 localHalfExtents = Vector3.zero;
            int index = occupyColliders.IndexOfItem(coll);

            if (coll is BoxCollider boxCollider)
            {
                localHalfExtents = boxCollider.size / 2f;
            }
            else if (coll is CapsuleCollider capsuleCollider)
            {
                switch (capsuleCollider.direction)
                {
                    case 0://x
                        localHalfExtents = new Vector3(capsuleCollider.height / 2f, capsuleCollider.radius, capsuleCollider.radius);
                        break;
                    case 1://y
                        localHalfExtents = new Vector3(capsuleCollider.radius, capsuleCollider.height / 2f, capsuleCollider.radius);
                        break;
                    case 2://z
                        localHalfExtents = new Vector3(capsuleCollider.radius, capsuleCollider.radius, capsuleCollider.height / 2f);
                        break;
                }
            }
            
            if (index >= 0 && index < halfExtents.Length)
            {
                halfExtents[index] = localHalfExtents;
            }
        }
        
        foreach (var name in scorePieces)
        {
            scorePiecesSet.Add(name);
        }
        peiceMask = LayerMask.GetMask("Piece");
    }

    protected void ScorePoints(int multiplyer = 1)
    {
        bool auto = FMS.MatchState == MatchState.auto;
        bool matchOver = FMS.MatchState == MatchState.finished;

        if (matchOver) return;

        int autoAdded = 0;
        if (auto)
        {
            scoredInAuto += multiplyer - scoredInAuto;
            if (scoredInAuto < 0) scoredInAuto = 0;
        }
        else
        {
            autoAdded = scoredInAuto * (autoScoreToAdd - scoreToAdd);
        }
        
        if (isBlue)
        {
            ScoreHolder.BlueScore -= lastAddedPoints;
            ScoreHolder.BlueScore += ((auto ? autoScoreToAdd : scoreToAdd) * multiplyer) + autoAdded;
        }
        else
        {
            ScoreHolder.RedScore -= lastAddedPoints;
            ScoreHolder.RedScore += ((auto ? autoScoreToAdd : scoreToAdd) * multiplyer) + autoAdded;
        }

        lastAddedPoints = ((auto ? autoScoreToAdd : scoreToAdd) * multiplyer) + autoAdded;
    }
    
    public List<GamePiece> getOccupyPieces()
    {
        return occupyObjects;
    }
    
    protected List<GamePiece> occupyPieces()
    {
        uniquePieces.Clear();
        pieces.Clear();
        
        foreach (var coll in occupyColliders) {
            var overlapBox = Physics.OverlapBox(coll.gameObject.transform.position, halfExtents[occupyColliders.IndexOfItem(coll)], coll.gameObject.transform.rotation, peiceMask);
            foreach (var box in overlapBox)
            {
                var piece = Utils.FindParentObjectComponent<GamePiece>(box.gameObject); 
                if (!piece) continue;
                if (!scorePiecesSet.Contains(piece.pieceType)) continue;
                if (uniquePieces.Contains(piece)) continue;
                if (piece.state != GamePieceState.World) continue;
                uniquePieces.Add(piece);
            }
        }

        pieces.AddRange(uniquePieces);
        return pieces;
    }

    public bool GetIsBlue()
    {
        return isBlue;
    }

    private void OnDrawGizmosSelected()
    {
        if (!displayDebugBox) return;
        if (occupyColliders == null || halfExtents == null) return;

        // Use a distinguishable color (e.g., Green/Cyan, similar to your desired box)
        Gizmos.color = new Color(0f, 0f, 1f, 0.6f); // Greenish-Cyan, semi-transparent

        for (int i = 0; i < occupyColliders.Length; i++)
        {
            Collider coll = occupyColliders[i];

            if (i >= halfExtents.Length || coll == null) continue;

            // 1. Get the necessary transformation data
            Transform collTransform = coll.gameObject.transform;
            Vector3 position = collTransform.position;
            Quaternion rotation = collTransform.rotation;
            Vector3 halfExtent = halfExtents[i]; // This MUST be the local half-extents

            // 2. Apply the same transformation matrix used in OverlapBox
            Matrix4x4 originalMatrix = Gizmos.matrix;

            // The TRS (Translate, Rotate, Scale) matrix applies the object's position and rotation
            Gizmos.matrix = Matrix4x4.TRS(position, rotation, Vector3.one);

            // 3. Draw the cube relative to the new matrix
            // We draw at Vector3.zero because the matrix already includes the position.
            // We multiply halfExtent by 2 to get the full size required by DrawWireCube.
            Gizmos.DrawWireCube(Vector3.zero, halfExtent * 2f);

            // Optionally, draw a solid cube to better visualize the volume
            // Gizmos.DrawCube(Vector3.zero, halfExtent * 2f);

            // 4. Restore the original Gizmos matrix
            Gizmos.matrix = originalMatrix;
        }
    }
}
