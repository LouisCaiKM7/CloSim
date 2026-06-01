using UnityEngine;
using Util;

public class GamePiece : MonoBehaviour
{
    public PieceNames pieceType;
    public Transform owner;
    public Rigidbody rb;
    public GamePieceState state;
    public GameObject colliderParent;

    [HideInInspector] public Vector3 startPosition;
    [HideInInspector] public Transform originalParent;
    [HideInInspector] public float startingDistance;

    [HideInInspector] public LaunchSource launchSource = LaunchSource.None;

    [HideInInspector] public bool g407IllegalLaunch;
    [HideInInspector] public bool g407PenaltyAssessed;
    [HideInInspector] public AllianceColor g407PenalizedAlliance = AllianceColor.None;

    private bool hasId;
    
    private void Start()
    {
        hasId = false;
    }

    private void Update()
    {
        if (hasId) return;

        if (!rb)
        {
            rb = GetComponent<Rigidbody>();
        }

        var core = Utils.FindParentObjectComponent<LoadMatch>(gameObject);

        if (!core)
        {
            return;
        }

        var fieldHolder = core.GetFieldHolder();

        if (!fieldHolder || fieldHolder.transform.childCount == 0)
        {
            return;
        }

        var returnTo = fieldHolder.transform.GetChild(0);

        originalParent = returnTo;
        hasId = true;
    }
    
    public void ClearG407PenaltyState()
    {
        launchSource = LaunchSource.None;
        g407IllegalLaunch = false;
        g407PenaltyAssessed = false;
        g407PenalizedAlliance = AllianceColor.None;
    }
}