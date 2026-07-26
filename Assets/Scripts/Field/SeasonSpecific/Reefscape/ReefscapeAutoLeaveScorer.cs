using Core;
using Field.Core;
using Field.Scoring;
using UnityEngine;
using Utilities;
using PlayMode = Core.PlayMode;

namespace Field.SeasonSpecific.Reefscape
{
    public sealed class ReefscapeAutoLeaveScorer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LoadMatch loadMatch;

        [Header("Scoring")]
        [SerializeField] private int autoLeavePointsPerRobot = 3;

        [Header("Line Contact / Overlap")]
        [Tooltip("Put the Auto Leave line trigger/collider objects on this layer.")]
        [SerializeField] private LayerMask autoLeaveLineMask;

        [Tooltip("Small expansion so exact touching counts as invalid.")]
        [SerializeField] private float contactSkin = 0.01f;

        [Tooltip("Use trigger line volumes. Recommended.")]
        [SerializeField] private bool lineCollidersAreTriggers = true;

        [Header("Robot Bumper Detection")]
        [Tooltip("Preferred child name containing bumper colliders. Common examples: Bumpers, Bumper, bumper.")]
        [SerializeField] private string bumperRootName = "Bumpers";

        [Tooltip("If no bumper root is found, scan all robot non-trigger colliders instead.")]
        [SerializeField] private bool fallbackToAllRobotColliders = true;

        private Fms _fms;
        private bool _scoredAutoLeave;

        private void Awake()
        {
            if (loadMatch == null)
                loadMatch = Utils.FindParentObjectComponent<LoadMatch>(gameObject);

            _fms = GetComponentInParent<Fms>();

            if (_fms == null)
                _fms = FindFirstObjectByType<Fms>(FindObjectsInactive.Include);
        }

        private void OnEnable()
        {
            _scoredAutoLeave = false;
        }

        private void Update()
        {
            if (_fms == null || loadMatch == null)
                return;

            float autoEndTimerValue = _fms.matchTime - _fms.autoTime;

            // Allows this component to work after a full field/match reset.
            if (_scoredAutoLeave && Fms.MatchState == MatchState.Auto && Fms.MatchTimer > autoEndTimerValue + 0.25f)
                _scoredAutoLeave = false;

            if (_scoredAutoLeave)
                return;

            if (Fms.MatchTimer > autoEndTimerValue)
                return;

            ScoreAutoLeave();
            _scoredAutoLeave = true;
        }

        private void ScoreAutoLeave()
        {
            GameObject[] robots = loadMatch.GetLoadedRobots();

            for (int i = 0; i < robots.Length; i++)
            {
                GameObject robot = robots[i];

                if (robot == null)
                    continue;

                if (!HasValidAutoLeave(robot))
                    continue;

                AddPointsForRobotSlot(i, autoLeavePointsPerRobot);
            }
        }

        private bool HasValidAutoLeave(GameObject robot)
        {
            // Valid Auto Leave means bumpers are not touching and not over the Auto Leave line.
            return !RobotBumpersTouchOrOverlapLine(robot);
        }

        private bool RobotBumpersTouchOrOverlapLine(GameObject robot)
        {
            Collider[] bumperColliders = GetBumperColliders(robot);

            QueryTriggerInteraction triggerMode = lineCollidersAreTriggers
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            foreach (Collider bumperCollider in bumperColliders)
            {
                if (bumperCollider == null || !bumperCollider.enabled)
                    continue;

                if (bumperCollider.isTrigger)
                    continue;

                Bounds bounds = bumperCollider.bounds;

                Collider[] possibleLineContacts = Physics.OverlapBox(
                    bounds.center,
                    bounds.extents + Vector3.one * contactSkin,
                    Quaternion.identity,
                    autoLeaveLineMask,
                    triggerMode
                );

                foreach (Collider lineCollider in possibleLineContacts)
                {
                    if (lineCollider == null || !lineCollider.enabled)
                        continue;

                    if (lineCollider.transform.IsChildOf(robot.transform))
                        continue;

                    if (lineCollider.isTrigger)
                        return true;

                    bool penetrating = Physics.ComputePenetration(
                        bumperCollider,
                        bumperCollider.transform.position,
                        bumperCollider.transform.rotation,
                        lineCollider,
                        lineCollider.transform.position,
                        lineCollider.transform.rotation,
                        out _,
                        out _
                    );

                    if (penetrating)
                        return true;

                    Vector3 closestToBumper = lineCollider.ClosestPoint(bounds.center);
                    Vector3 closestToLine = bumperCollider.ClosestPoint(closestToBumper);
                    float sqrDistance = (closestToBumper - closestToLine).sqrMagnitude;

                    if (sqrDistance <= contactSkin * contactSkin)
                        return true;
                }
            }

            return false;
        }

        private Collider[] GetBumperColliders(GameObject robot)
        {
            Transform bumperRoot = FindChildRecursive(robot.transform, bumperRootName);

            if (bumperRoot != null)
                return bumperRoot.GetComponentsInChildren<Collider>(true);

            return fallbackToAllRobotColliders
                ? robot.GetComponentsInChildren<Collider>(true)
                : System.Array.Empty<Collider>();
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
                return null;

            foreach (Transform child in root)
            {
                if (string.Equals(child.name, childName, System.StringComparison.OrdinalIgnoreCase))
                    return child;

                Transform nested = FindChildRecursive(child, childName);
                if (nested != null)
                    return nested;
            }

            return null;
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
            // A4 correctness fix: resolve alliance from LoadMatch's single source of truth instead of a
            // local PlayMode switch (which returned true for online-only slots 5/6). Offline behavior is
            // identical (LoadMatch.IsPlayerBlue reproduces this exact switch); scoring math is unchanged.
            return loadMatch.IsPlayerBlue(robotSlot);
        }
    }
}