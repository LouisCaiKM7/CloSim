using System.Collections.Generic;
using MyBox;
using UnityEngine;
using Util;

public class FieldScorer : MonoBehaviour
{
    [Tooltip("this will display a blue box around the scoring node when in the editor")]
    [SerializeField] private bool displayDebugBox = false;

    [SerializeField] private bool isBlue;
    [SerializeField] private int scoreToAdd;
    [SerializeField] private int autoScoreToAdd;
    [SerializeField] protected PieceNames[] scorePieces;

    private readonly HashSet<PieceNames> scorePiecesSet = new HashSet<PieceNames>();

    [SerializeField] private Collider[] occupyColliders;

    [Header("Fuel Counter")]
    [SerializeField] private bool countsAsFuel = false;
    [SerializeField] private int fuelPerScoredPiece = 1;

    public static int BlueFuel { get; private set; }
    public static int RedFuel { get; private set; }

    private int lastAddedFuel;

    private int g407MajorFoulPoints = 15;

    private readonly HashSet<GamePiece> uniquePieces = new HashSet<GamePiece>();
    private Vector3[] halfExtents;

    protected List<GamePiece> occupyObjects = new List<GamePiece>();
    private List<GamePiece> pieces = new List<GamePiece>();

    private LayerMask peiceMask;

    private int lastAddedPoints;
    private int scoredInAuto;
    
    private RebuiltShifts rebuiltShifts;

    private void OnEnable()
    {
        occupyObjects = new List<GamePiece>();
        scoredInAuto = 0;
        lastAddedPoints = 0;
        lastAddedFuel = 0;

        if (occupyColliders == null)
            occupyColliders = new Collider[0];

        halfExtents = new Vector3[occupyColliders.Length];
        scorePiecesSet.Clear();
        
        rebuiltShifts = GetComponent<RebuiltShifts>();

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

            if (index >= 0 && index < halfExtents.Length)
                halfExtents[index] = localHalfExtents;
        }

        foreach (var name in scorePieces)
            scorePiecesSet.Add(name);

        peiceMask = LayerMask.GetMask("Piece");
    }

    public static void ResetFuelCounters()
    {
        BlueFuel = 0;
        RedFuel = 0;
    }

    protected void ScorePoints(int multiplyer = 1)
    {
        bool auto = FMS.MatchState == MatchState.auto;
        bool matchOver = FMS.MatchState == MatchState.finished;

        if (matchOver)
            return;
        
        ApplyG407PenaltiesForScoredPieces();

        int autoAdded = 0;

        if (auto)
        {
            scoredInAuto += multiplyer - scoredInAuto;

            if (scoredInAuto < 0)
                scoredInAuto = 0;
        }
        else
        {
            autoAdded = scoredInAuto * (autoScoreToAdd - scoreToAdd);
        }

        int pointsToAdd = ((auto ? autoScoreToAdd : scoreToAdd) * multiplyer) + autoAdded;

        if (isBlue)
        {
            ScoreHolder.BlueScore -= lastAddedPoints;
            ScoreHolder.BlueScore += pointsToAdd;
        }
        else
        {
            ScoreHolder.RedScore -= lastAddedPoints;
            ScoreHolder.RedScore += pointsToAdd;
        }

        lastAddedPoints = pointsToAdd;

        ScoreFuel(multiplyer);
    }

    private void ScoreFuel(int scoredPieceCount)
    {
        if (!countsAsFuel)
            return;

        int fuelToAdd = Mathf.Max(0, scoredPieceCount) * fuelPerScoredPiece;

        if (isBlue)
        {
            BlueFuel -= lastAddedFuel;
            BlueFuel += fuelToAdd;

            if (BlueFuel < 0)
                BlueFuel = 0;
        }
        else
        {
            RedFuel -= lastAddedFuel;
            RedFuel += fuelToAdd;

            if (RedFuel < 0)
                RedFuel = 0;
        }

        lastAddedFuel = fuelToAdd;
    }

    public List<GamePiece> getOccupyPieces()
    {
        return occupyObjects;
    }

    protected List<GamePiece> occupyPieces()
    {
        uniquePieces.Clear();
        pieces.Clear();

        foreach (var coll in occupyColliders)
        {
            if (coll == null)
                continue;

            int index = occupyColliders.IndexOfItem(coll);

            if (index < 0 || index >= halfExtents.Length)
                continue;

            var overlapBox = Physics.OverlapBox(
                coll.gameObject.transform.position,
                halfExtents[index],
                coll.gameObject.transform.rotation,
                peiceMask
            );

            foreach (var box in overlapBox)
            {
                var piece = Utils.FindParentObjectComponent<GamePiece>(box.gameObject);

                if (!piece) continue;
                if (!scorePiecesSet.Contains(piece.pieceType)) continue;
                if (uniquePieces.Contains(piece)) continue;
                if (piece.state != GamePieceState.World) continue;

                uniquePieces.Add(piece);
            }
        }

        pieces.AddRange(uniquePieces);
        return pieces;
    }

    private void ApplyG407PenaltiesForScoredPieces()
    {
        if (FMS.MatchState == MatchState.finished ||
            FMS.RobotState == RobotState.disabled ||
            FMS.MatchTimer <= 0f)
        {
            return;
        }

        if (rebuiltShifts != null && !rebuiltShifts.IsThisHubCounting())
            return;

        if (occupyObjects == null)
            return;

        foreach (var piece in occupyObjects)
        {
            if (piece == null)
                continue;

            if (piece.launchSource != LaunchSource.Robot)
                continue;

            if (!piece.g407IllegalLaunch)
                continue;

            if (piece.g407PenaltyAssessed)
                continue;

            switch (piece.g407PenalizedAlliance)
            {
                case AllianceColor.Blue:
                    ScoreHolder.RedScore += g407MajorFoulPoints;
                    break;

                case AllianceColor.Red:
                    ScoreHolder.BlueScore += g407MajorFoulPoints;
                    break;

                default:
                    continue;
            }

            piece.g407PenaltyAssessed = true;
        }
    }

    public bool GetIsBlue()
    {
        return isBlue;
    }

    private void OnDrawGizmosSelected()
    {
        if (!displayDebugBox) return;
        if (occupyColliders == null || halfExtents == null) return;

        Gizmos.color = new Color(0f, 0f, 1f, 0.6f);

        for (int i = 0; i < occupyColliders.Length; i++)
        {
            Collider coll = occupyColliders[i];

            if (i >= halfExtents.Length || coll == null)
                continue;

            Transform collTransform = coll.gameObject.transform;
            Vector3 position = collTransform.position;
            Quaternion rotation = collTransform.rotation;
            Vector3 halfExtent = halfExtents[i];

            Matrix4x4 originalMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, halfExtent * 2f);
            Gizmos.matrix = originalMatrix;
        }
    }
}