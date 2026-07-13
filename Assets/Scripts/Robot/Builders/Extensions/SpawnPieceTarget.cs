using Core;
using Field.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace Robot.Builders.Extensions
{
    public class SpawnPieceTarget : MonoBehaviour
    {
        public SpawnType spawnType;

        [FormerlySerializedAs("SpawnDistance")] public float spawnDistance;

        [FormerlySerializedAs("Velocity")] public float velocity;

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
}
