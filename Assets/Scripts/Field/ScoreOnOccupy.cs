using System.Collections.Generic;
using UnityEngine;

public class ScoreOnOccupy : FieldScorer
{
    [SerializeField] private int maxPeices;
    [SerializeField] private FieldScorer[] checkForDoubleScore;

    // Update is called once per frame
    void FixedUpdate()
    {
        occupyObjects = occupyPieces();

        foreach (var node in checkForDoubleScore)
        {
            if (!node) break;
            if (node == this) continue;
            DoubleScored(occupyObjects, node.getOccupyPieces());   
        }

        var pieces = occupyObjects.Count;

        if (maxPeices > 0)
        {
            pieces = Mathf.Clamp(pieces, 0, maxPeices);
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
