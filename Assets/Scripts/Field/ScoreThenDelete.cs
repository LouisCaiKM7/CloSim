using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Util;

public class ScoreThenDelete : FieldScorer
{
    // Update is called once per frame
    private int scoredCount;
    void FixedUpdate()
    {
        occupyObjects = occupyPieces();

        var pieces = occupyObjects.Count;
        scoredCount += pieces;
        
        ScorePoints(scoredCount);

        for (int i = 0; i < pieces; i++)
        {
            Destroy(occupyObjects[i].gameObject);
        }
    }
}
