using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Util;

public class ScoreOnlyOnce : FieldScorer
{
    private HashSet<GameObject> scoredPieces = new HashSet<GameObject>();
    private HashSet<GameObject> previousOccupyObjects = new HashSet<GameObject>();
    protected int totalScore = 0;

    protected void FixedUpdate()
    {
        poolOccupyObjects();
        
        // Create a set of current objects for comparison
        compareObjects();
        
        ScorePoints(totalScore); // Pass the total accumulated score
    }

    protected void poolOccupyObjects()
    {
        occupyObjects = occupyPieces();
    }
    
    

    protected void compareObjects(bool shouldScore = true)
    {
        HashSet<GameObject> currentObjects = new HashSet<GameObject>();
        foreach (var piece in occupyObjects)
        {
            currentObjects.Add(piece.gameObject);
            
            // Only score if this piece hasn't been scored yet
            if (!scoredPieces.Contains(piece.gameObject))
            {
                scoredPieces.Add(piece.gameObject);
                if (shouldScore) totalScore++; // Increment total score
            }
        }
        
        // Remove pieces that left the zone from scoredPieces
        scoredPieces.RemoveWhere(obj => !currentObjects.Contains(obj));
    }
}