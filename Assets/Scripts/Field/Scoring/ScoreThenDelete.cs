using System.Collections.Generic;
using Core;
using Robot.Runtime;
using UnityEngine;

namespace Field.Scoring
{
    public class ScoreThenDelete : FieldScorer
    {
        private readonly HashSet<GamePiece> _scoredPieces = new HashSet<GamePiece>();
        private int _scoredCount;

        private void FixedUpdate()
        {
            OccupyObjects = OccupyPieces();

            foreach (var piece in OccupyObjects)
            {
                if (piece == null)
                    continue;

                if (!_scoredPieces.Add(piece))
                    continue;

                _scoredCount++;

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

                // ONLINE: host-authoritative game pieces (Online.Sync.GamePieceNetworkRegistrar). Falls back
                // to a plain Destroy offline / for non-networked pieces, so this is behavior-preserving.
                Online.Sync.GamePieceNetworkRegistrar.Despawn(piece.gameObject);
            }

            ScorePoints(_scoredCount);
        }

        private void OnDisable()
        {
            _scoredPieces.Clear();
            _scoredCount = 0;
        }
    }
}