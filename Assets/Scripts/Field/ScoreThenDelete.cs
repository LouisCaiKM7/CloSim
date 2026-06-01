using System.Collections.Generic;
using UnityEngine;
using Util;

public class ScoreThenDelete : FieldScorer
{
    private readonly HashSet<GamePiece> scoredPieces = new HashSet<GamePiece>();
    private int scoredCount;

    private void FixedUpdate()
    {
        occupyObjects = occupyPieces();

        for (int i = 0; i < occupyObjects.Count; i++)
        {
            GamePiece piece = occupyObjects[i];

            if (piece == null)
                continue;

            if (scoredPieces.Contains(piece))
                continue;

            scoredPieces.Add(piece);
            scoredCount++;

            // Make it impossible for any scorer/spawner to see this fuel again.
            piece.state = GamePieceState.Moving;

            if (piece.colliderParent != null)
                piece.colliderParent.SetActive(false);

            if (piece.rb != null)
            {
                piece.rb.velocity = Vector3.zero;
                piece.rb.angularVelocity = Vector3.zero;
                piece.rb.detectCollisions = false;
            }

            Destroy(piece.gameObject);
        }

        ScorePoints(scoredCount);
    }

    private void OnDisable()
    {
        scoredPieces.Clear();
        scoredCount = 0;
    }
}