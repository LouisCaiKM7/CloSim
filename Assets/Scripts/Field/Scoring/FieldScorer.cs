using System;
using System.Collections.Generic;
using Core;
using Field.Core;
using Field.SeasonSpecific;
using Field.SeasonSpecific.Rebuilt;
using Mirror;
using MyBox;
using Robot.Runtime;
using UnityEngine;
using Utilities;

namespace Field.Scoring
{
    public class FieldScorer : MonoBehaviour
    {
        [Tooltip("this will display a blue box around the scoring node when in the editor")]
        [SerializeField] private bool displayDebugBox;

        [SerializeField] private bool isBlue;
        [SerializeField] private int scoreToAdd;
        [SerializeField] private int autoScoreToAdd;
        [SerializeField] protected PieceNames[] scorePieces;

        private readonly HashSet<PieceNames> _scorePiecesSet = new HashSet<PieceNames>();

        [SerializeField] private Collider[] occupyColliders;

        [Header("Piece Counter")]
        [SerializeField] private ScoreCounterType counterType = ScoreCounterType.None;
        [SerializeField] private int counterAmountPerPiece = 1;

        public static int BlueFuel { get; private set; }
        public static int RedFuel { get; private set; }

        public static int BlueCoral { get; private set; }
        public static int RedCoral { get; private set; }

        public static int BlueAlgae { get; private set; }
        public static int RedAlgae { get; private set; }

        private int _lastAddedCounter;

        private readonly int _g407MajorFoulPoints = 15;

        private readonly HashSet<GamePiece> _uniquePieces = new HashSet<GamePiece>();
        private Vector3[] _halfExtents;

        protected List<GamePiece> OccupyObjects = new List<GamePiece>();
        private readonly List<GamePiece> _pieces = new List<GamePiece>();

        private LayerMask _pieceMask;

        private int _lastAddedPoints;
        private int _scoredInAuto;
    
        private RebuiltShifts _rebuiltShifts;

        private void OnEnable()
        {
            OccupyObjects = new List<GamePiece>();
            _scoredInAuto = 0;
            _lastAddedPoints = 0;
            _lastAddedCounter = 0;

            occupyColliders ??= Array.Empty<Collider>();

            _halfExtents = new Vector3[occupyColliders.Length];
            _scorePiecesSet.Clear();

            _rebuiltShifts = GetComponent<RebuiltShifts>();

            foreach (var coll in occupyColliders)
            {
                if (coll == null)
                    continue;

                Vector3 localHalfExtents = Vector3.zero;
                int index = occupyColliders.IndexOfItem(coll);

                if (coll is BoxCollider boxCollider)
                {
                    localHalfExtents = boxCollider.size / 2f;
                }
                else if (coll is CapsuleCollider capsuleCollider)
                {
                    switch (capsuleCollider.direction)
                    {
                        case 0:
                            localHalfExtents = new Vector3(
                                capsuleCollider.height / 2f,
                                capsuleCollider.radius,
                                capsuleCollider.radius
                            );
                            break;

                        case 1:
                            localHalfExtents = new Vector3(
                                capsuleCollider.radius,
                                capsuleCollider.height / 2f,
                                capsuleCollider.radius
                            );
                            break;

                        case 2:
                            localHalfExtents = new Vector3(
                                capsuleCollider.radius,
                                capsuleCollider.radius,
                                capsuleCollider.height / 2f
                            );
                            break;
                    }
                }

                if (index >= 0 && index < _halfExtents.Length)
                    _halfExtents[index] = localHalfExtents;
            }

            foreach (var pieceNames in scorePieces)
                if (_scorePiecesSet != null)
                    _scorePiecesSet.Add(pieceNames);

            _pieceMask = LayerMask.GetMask("Piece");
        }

        // ONLINE (additive): true offline (no client active) and on the host/server; false on pure
        // clients. Score is server-authoritative online, so all score-MUTATING paths are gated behind
        // this. Offline this is always true, so behavior is byte-identical to before.
        private static bool ServerControlsScore()
        {
            return !NetworkClient.active || NetworkServer.active;
        }

        // ONLINE (additive): pure clients receive authoritative counter totals from Online.Sync (ScoreSync)
        // and push them into the private statics here. Never called on the server; does not touch score math.
        public static void ApplyReplicatedCounters(
            int blueFuel, int redFuel,
            int blueCoral, int redCoral,
            int blueAlgae, int redAlgae)
        {
            BlueFuel = blueFuel;
            RedFuel = redFuel;
            BlueCoral = blueCoral;
            RedCoral = redCoral;
            BlueAlgae = blueAlgae;
            RedAlgae = redAlgae;
        }

        public static void ResetCounters()
        {
            // ONLINE: clients never reset authoritative counters; they arrive via ScoreSync.
            if (!ServerControlsScore())
                return;

            BlueFuel = 0;
            RedFuel = 0;

            BlueCoral = 0;
            RedCoral = 0;

            BlueAlgae = 0;
            RedAlgae = 0;
        }

        protected void ScorePoints(int multiplayer = 1)
        {
            // ONLINE: only the server/host mutates score (this also gates ApplyG407PenaltiesForScoredPieces
            // and ScoreCounter, which are reached only from here). Pure clients get score via ScoreSync.
            if (!ServerControlsScore())
                return;

            bool auto = Fms.MatchState == MatchState.Auto;
            bool matchOver = Fms.MatchState == MatchState.Finished;

            if (matchOver)
                return;
        
            ApplyG407PenaltiesForScoredPieces();

            int autoAdded = 0;

            if (auto)
            {
                _scoredInAuto += multiplayer - _scoredInAuto;

                if (_scoredInAuto < 0)
                    _scoredInAuto = 0;
            }
            else
            {
                autoAdded = _scoredInAuto * (autoScoreToAdd - scoreToAdd);
            }

            int pointsToAdd = ((auto ? autoScoreToAdd : scoreToAdd) * multiplayer) + autoAdded;

            if (isBlue)
            {
                ScoreHolder.BlueScore -= _lastAddedPoints;
                ScoreHolder.BlueScore += pointsToAdd;
            }
            else
            {
                ScoreHolder.RedScore -= _lastAddedPoints;
                ScoreHolder.RedScore += pointsToAdd;
            }

            _lastAddedPoints = pointsToAdd;

            ScoreCounter(multiplayer);
        }

        private void ScoreCounter(int scoredPieceCount)
        {
            if (counterType == ScoreCounterType.None)
                return;

            int amountToAdd = Mathf.Max(0, scoredPieceCount) * counterAmountPerPiece;

            if (isBlue)
            {
                RemoveFromBlueCounter(_lastAddedCounter);
                AddToBlueCounter(amountToAdd);
            }
            else
            {
                RemoveFromRedCounter(_lastAddedCounter);
                AddToRedCounter(amountToAdd);
            }

            _lastAddedCounter = amountToAdd;
        }

        private void AddToBlueCounter(int amount)
        {
            switch (counterType)
            {
                case ScoreCounterType.Fuel:
                    BlueFuel += amount;
                    break;

                case ScoreCounterType.Coral:
                    BlueCoral += amount;
                    break;

                case ScoreCounterType.Algae:
                    BlueAlgae += amount;
                    break;
            }
        }

        private void RemoveFromBlueCounter(int amount)
        {
            switch (counterType)
            {
                case ScoreCounterType.Fuel:
                    BlueFuel -= amount;
                    BlueFuel = Mathf.Max(0, BlueFuel);
                    break;

                case ScoreCounterType.Coral:
                    BlueCoral -= amount;
                    BlueCoral = Mathf.Max(0, BlueCoral);
                    break;

                case ScoreCounterType.Algae:
                    BlueAlgae -= amount;
                    BlueAlgae = Mathf.Max(0, BlueAlgae);
                    break;
            }
        }

        private void AddToRedCounter(int amount)
        {
            switch (counterType)
            {
                case ScoreCounterType.Fuel:
                    RedFuel += amount;
                    break;

                case ScoreCounterType.Coral:
                    RedCoral += amount;
                    break;

                case ScoreCounterType.Algae:
                    RedAlgae += amount;
                    break;
            }
        }

        private void RemoveFromRedCounter(int amount)
        {
            switch (counterType)
            {
                case ScoreCounterType.Fuel:
                    RedFuel -= amount;
                    RedFuel = Mathf.Max(0, RedFuel);
                    break;

                case ScoreCounterType.Coral:
                    RedCoral -= amount;
                    RedCoral = Mathf.Max(0, RedCoral);
                    break;

                case ScoreCounterType.Algae:
                    RedAlgae -= amount;
                    RedAlgae = Mathf.Max(0, RedAlgae);
                    break;
            }
        }

        public List<GamePiece> GetOccupyPieces()
        {
            return OccupyObjects;
        }

        protected List<GamePiece> OccupyPieces()
        {
            _uniquePieces.Clear();
            _pieces.Clear();

            foreach (var coll in occupyColliders)
            {
                if (coll == null)
                    continue;

                int index = occupyColliders.IndexOfItem(coll);

                if (index < 0 || index >= _halfExtents.Length)
                    continue;

                var overlapBox = Physics.OverlapBox(
                    coll.gameObject.transform.position,
                    _halfExtents[index],
                    coll.gameObject.transform.rotation,
                    _pieceMask
                );

                foreach (var box in overlapBox)
                {
                    var piece = Utils.FindParentObjectComponent<GamePiece>(box.gameObject);

                    if (!piece) continue;
                    if (!_scorePiecesSet.Contains(piece.pieceType)) continue;
                    if (_uniquePieces.Contains(piece)) continue;
                    if (piece.state != GamePieceState.World) continue;

                    _uniquePieces.Add(piece);
                }
            }

            _pieces.AddRange(_uniquePieces);
            return _pieces;
        }

        private void ApplyG407PenaltiesForScoredPieces()
        {
            if (Fms.MatchState == MatchState.Finished ||
                Fms.RobotState == RobotState.Disabled ||
                Fms.MatchTimer <= 0f)
            {
                return;
            }

            if (_rebuiltShifts != null && !_rebuiltShifts.IsThisHubCounting())
                return;

            if (OccupyObjects == null)
                return;

            foreach (var piece in OccupyObjects)
            {
                if (piece == null)
                    continue;

                if (piece.launchSource != LaunchSource.Robot)
                    continue;

                if (!piece.g407IllegalLaunch)
                    continue;

                if (piece.g407PenaltyAssessed)
                    continue;

                bool scoringHubIsBlue = isBlue;

                bool penalizedRobotIsBlue = piece.g407PenalizedAlliance == AllianceColor.Blue;
                bool penalizedRobotIsRed = piece.g407PenalizedAlliance == AllianceColor.Red;

                bool pieceScoredInOwnHub =
                    (penalizedRobotIsBlue && scoringHubIsBlue) ||
                    (penalizedRobotIsRed && !scoringHubIsBlue);

                if (!pieceScoredInOwnHub)
                    continue;

                switch (piece.g407PenalizedAlliance)
                {
                    case AllianceColor.Blue:
                        ScoreHolder.RedScore += _g407MajorFoulPoints;
                        break;

                    case AllianceColor.Red:
                        ScoreHolder.BlueScore += _g407MajorFoulPoints;
                        break;

                    default:
                        continue;
                }

                piece.g407PenaltyAssessed = true;
            }
        }

        protected bool GetIsBlue()
        {
            return isBlue;
        }

        private void OnDrawGizmosSelected()
        {
            if (!displayDebugBox) return;
            if (occupyColliders == null || _halfExtents == null) return;

            Gizmos.color = new Color(0f, 0f, 1f, 0.6f);

            for (int i = 0; i < occupyColliders.Length; i++)
            {
                Collider coll = occupyColliders[i];

                if (i >= _halfExtents.Length || coll == null)
                    continue;

                Transform collTransform = coll.gameObject.transform;
                Vector3 position = collTransform.position;
                Quaternion rotation = collTransform.rotation;
                Vector3 halfExtent = _halfExtents[i];

                Matrix4x4 originalMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, halfExtent * 2f);
                Gizmos.matrix = originalMatrix;
            }
        }
    }
}