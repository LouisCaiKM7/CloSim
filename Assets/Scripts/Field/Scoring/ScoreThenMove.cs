using System.Collections.Generic;
using Core;
using Field.Core;
using Robot.Runtime;
using UnityEngine;

namespace Field.Scoring
{
    public class ScoreThenMove : FieldScorer
    {
        [SerializeField] private FieldInteraction moveTo;

        private Queue<GamePiece> _buffer;

        private int _scoredCount;
    
        private GamePiece _lastAnimatedGamePiece;
        // Update is called once per frame
        private void Start()
        {
            _buffer = new Queue<GamePiece>();
        }

        void FixedUpdate()
        {
            OccupyObjects = OccupyPieces();

            for (int i = OccupyObjects.Count - 1; i >= 0; i--)
            {
                if (_buffer.Contains(OccupyObjects[i]))
                {
                    OccupyObjects.Remove(OccupyObjects[i]);
                }
                else
                {
                    _buffer.Enqueue(OccupyObjects[i]);
                }
            }

            var pieces = OccupyObjects.Count;
            _scoredCount += pieces;
        
            ScorePoints(_scoredCount);


            if (!moveTo.GetGamePiece() && _buffer.TryPeek(out GamePiece piece))
            {
                if (_lastAnimatedGamePiece != piece)
                {
                    var stillThere = OccupyPieces();
                    for (int i = 0; i < stillThere.Count; i++)
                    {
                        if (_buffer.Contains(stillThere[i]))
                        {
                            break;
                        }
                        else if (i == stillThere.Count - 1)
                        {
                            _lastAnimatedGamePiece = piece;
                            return;
                        }
                    }
                }

                bool finished = AnimateTo(piece, 60, 0);

                if (finished)
                {
                    moveTo.SetGamePiece(piece);
                    _buffer.Dequeue();
                }
            }
        }
    
        private bool AnimateTo(GamePiece piece, float incomingSpeed, float angularSpeed)
        {
            var speed = incomingSpeed * 0.0254f;

            var rbTransform = piece.rb.transform;
            var target = moveTo.gameObject.transform;
            if (piece.state != GamePieceState.Moving)
            {
                piece.state = GamePieceState.Moving;
                piece.startPosition = rbTransform.localPosition;
                GamePieceManager.DisableColliders(piece);
            }

            var distance = rbTransform.parent.InverseTransformPoint(target.position) - piece.startPosition;
            var parentPosition = rbTransform.parent.position;
            var step = distance.normalized * ((speed) * Time.deltaTime);
            var finalPosition = piece.startPosition + step;

            piece.startPosition = finalPosition;
            rbTransform.position = parentPosition + rbTransform.parent.TransformDirection(finalPosition);
            piece.rb.position = parentPosition + rbTransform.parent.TransformDirection(finalPosition);
            piece.rb.velocity = Vector3.zero;

            var distanceMagnitude = distance.magnitude;
            
            Quaternion targetRotation = target.rotation;
          

            // Smoothly rotate towards target rotation
            rbTransform.rotation = Quaternion.RotateTowards(
                rbTransform.rotation,
                targetRotation,
                angularSpeed * Time.fixedDeltaTime
            );

            if (angularSpeed == 0)
            {
                rbTransform.localRotation = Quaternion.identity;
            }

            if (distanceMagnitude <= 0.75f * 0.0254f)
            {
                return true; // Reached target
            }
            else
            {
                return false; // Moving towards target
            }
        }
    
    
    }
}
