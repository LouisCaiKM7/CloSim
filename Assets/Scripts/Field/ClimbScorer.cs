using UnityEngine;
using Util;

public class ClimbScorer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LoadMatch loadMatch;

    [Header("Scoring")]
    [SerializeField] private int autoClimbPoints = 15;
    [SerializeField] private int endgameClimbPoints = 10;
    
    [Header("Contact Rules")]
    [SerializeField] private LayerMask invalidClimbContactMask;
    [SerializeField] private float contactSkin = 0.01f; // meters. Tune 0.01-0.05.
    [SerializeField] private bool invalidMasksAreTriggers = true;

    private FMS fms;
    private bool scoredAutoClimb;
    private bool scoredEndgameClimb;

    private void Awake()
    {
        if (loadMatch == null)
            loadMatch = Utils.FindParentObjectComponent<LoadMatch>(gameObject);

        fms = GetComponentInParent<FMS>();

        if (fms == null)
            fms = FindFirstObjectByType<FMS>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        scoredAutoClimb = false;
        scoredEndgameClimb = false;
    }

    private void Update()
    {
        if (fms == null)
            return;

        float autoEndTimerValue = fms.matchTime - fms.autoTime;

        if (!scoredAutoClimb && FMS.MatchTimer <= autoEndTimerValue)
        {
            ScoreClimbs(autoClimbPoints, "AUTO");
            scoredAutoClimb = true;
        }

        if (!scoredEndgameClimb && FMS.MatchTimer <= 0f)
        {
            ScoreClimbs(endgameClimbPoints, "ENDGAME");
            scoredEndgameClimb = true;
        }
    }

    private void ScoreClimbs(int pointsPerRobot, string phaseName)
    {
        if (loadMatch == null)
        {
            return;
        }

        GameObject[] robots = loadMatch.GetLoadedRobots();

        for (int i = 0; i < robots.Length; i++)
        {
            GameObject robot = robots[i];

            if (robot == null)
                continue;

            bool valid = IsClimbValid(robot);
            
            if (!valid)
                continue;

            AddPointsForRobotSlot(i, pointsPerRobot);
        }
    }

    private bool IsClimbValid(GameObject robot)
    {
        Collider[] robotColliders = robot.GetComponentsInChildren<Collider>(true);

        QueryTriggerInteraction triggerMode = invalidMasksAreTriggers
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;

        foreach (Collider robotCollider in robotColliders)
        {
            if (robotCollider == null || !robotCollider.enabled)
                continue;

            // Keep this if robot trigger colliders should not count.
            if (robotCollider.isTrigger)
                continue;

            Bounds bounds = robotCollider.bounds;

            Collider[] possibleInvalidContacts = Physics.OverlapBox(
                bounds.center,
                bounds.extents + Vector3.one * contactSkin,
                Quaternion.identity,
                invalidClimbContactMask,
                triggerMode
            );

            foreach (Collider invalidCollider in possibleInvalidContacts)
            {
                if (invalidCollider == null || !invalidCollider.enabled)
                    continue;

                if (invalidCollider.transform.IsChildOf(robot.transform))
                    continue;
                
                if (invalidCollider.isTrigger)
                {
                    return false;
                }

                Vector3 direction;
                float distance;

                bool penetrating = Physics.ComputePenetration(
                    robotCollider,
                    robotCollider.transform.position,
                    robotCollider.transform.rotation,
                    invalidCollider,
                    invalidCollider.transform.position,
                    invalidCollider.transform.rotation,
                    out direction,
                    out distance
                );

                if (penetrating)
                {
                    return false;
                }

                // Handles actual touching / very close contact where physics keeps bodies separated.
                Vector3 closestToRobot = invalidCollider.ClosestPoint(bounds.center);
                Vector3 closestToInvalid = robotCollider.ClosestPoint(closestToRobot);
                float sqrDistance = (closestToRobot - closestToInvalid).sqrMagnitude;

                if (sqrDistance <= contactSkin * contactSkin)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void AddPointsForRobotSlot(int robotSlot, int points)
    {
        if (IsRobotSlotBlue(robotSlot))
            ScoreHolder.BlueScore += points;
        else
            ScoreHolder.RedScore += points;
    }

    private bool IsRobotSlotBlue(int robotSlot)
    {
        switch (loadMatch.GetPlayMode())
        {
            case Util.PlayMode.OneVsZero:
            case Util.PlayMode.TwoVsZero:
                return loadMatch.UsesBlueAlliance();

            case Util.PlayMode.OneVsOne:
                return robotSlot == 0;

            default:
                return true;
        }
    }
}