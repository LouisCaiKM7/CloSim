using System.Collections.Generic;
using Robot.Runtime;
using UnityEngine;
using UnityEngine.Serialization;

namespace Field.Scoring
{
    public class ScoreOnOccupy : FieldScorer
    {
        [FormerlySerializedAs("maxPeices")] [SerializeField] private int maxPieces;
        [SerializeField] private FieldScorer[] checkForDoubleScore;

        // Update is called once per frame
        void FixedUpdate()
        {
            OccupyObjects = OccupyPieces();

            foreach (var node in checkForDoubleScore)
            {
                if (!node) break;
                if (node == this) continue;
                DoubleScored(OccupyObjects, node.GetOccupyPieces());   
            }

            var pieces = OccupyObjects.Count;

            if (maxPieces > 0)
            {
                pieces = Mathf.Clamp(pieces, 0, maxPieces);
            }
        
            ScorePoints(pieces);
        }

        private void DoubleScored(List<GamePiece> a, List<GamePiece> b)
        {
            for (int i = a.Count - 1; i >= 0; i--)
            {
                if (b.Contains(a[i]))
                    a.RemoveAt(i);
            }
        }
    }
}
