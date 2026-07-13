using System.Collections.Generic;
using UnityEngine;

namespace Field.Scoring
{
    public class ScoreOnlyOnce : FieldScorer
    {
        private readonly HashSet<GameObject> _scoredPieces = new HashSet<GameObject>();
        protected int TotalScore;

        protected void FixedUpdate()
        {
            PoolOccupyObjects();
        
            // Create a set of current objects for comparison
            CompareObjects();
        
            ScorePoints(TotalScore); // Pass the total accumulated score
        }

        protected void PoolOccupyObjects()
        {
            OccupyObjects = OccupyPieces();
        }
    
    

        protected void CompareObjects(bool shouldScore = true)
        {
            HashSet<GameObject> currentObjects = new HashSet<GameObject>();
            foreach (var piece in OccupyObjects)
            {
                currentObjects.Add(piece.gameObject);
            
                // Only score if this piece hasn't been scored yet
                if (_scoredPieces.Add(piece.gameObject))
                {
                    if (shouldScore) TotalScore++; // Increment total score
                }
            }
        
            // Remove pieces that left the zone from scoredPieces
            _scoredPieces.RemoveWhere(obj => !currentObjects.Contains(obj));
        }
    }
}