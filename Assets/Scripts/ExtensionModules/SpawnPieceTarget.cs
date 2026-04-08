using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Util;

public class SpawnPieceTarget : MonoBehaviour
{
    public SpawnType spawnType;

    public float SpawnDistance;

    public float Velocity;

    void OnEnable()
    {
        if (!SpawnGamePiece.Targets.Contains(this))
        {
            SpawnGamePiece.Targets.Add(this);
        }
    }

    void OnDisable()
    {
        SpawnGamePiece.Targets.Remove(this);
    }
}
