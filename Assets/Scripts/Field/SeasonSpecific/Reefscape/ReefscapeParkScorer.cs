using System;
using Core;
using Field.Core;
using Field.Scoring;
using UnityEngine;
using Utilities;
using PlayMode = Core.PlayMode;

namespace Field.SeasonSpecific.Reefscape
{
    public sealed class ReefscapeEndgameParkScorer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LoadMatch loadMatch;

        [Header("Scoring")]
        [SerializeField] private int parkPointsPerRobot = 2;

        [Header("Alliance Zone Contact")]
        [SerializeField] private LayerMask blueAllianceZoneMask;
        [SerializeField] private LayerMask redAllianceZoneMask;
        [SerializeField] private float zoneContactSkin = 0.01f;
        [SerializeField] private bool allianceZoneCollidersAreTriggers = true;

        [Header("Ground Contact")]
        [SerializeField] private LayerMask carpetMask;
        [SerializeField] private float groundContactSkin = 0.02f;
        [SerializeField] private bool carpetCollidersAreTriggers;

        [Header("Bumper Detection")]
        [SerializeField] private string bumperRootName = "Bumpers";
        [SerializeField] private bool fallbackToAllRobotColliders;

        private Fms _fms;
        private bool _scoredEndgamePark;

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
            _scoredEndgamePark = false;
        }

        private void Update()
        {
            if (_fms == null || loadMatch == null)
                return;

            if (_scoredEndgamePark && Fms.MatchTimer > 0.25f)
                _scoredEndgamePark = false;

            if (_scoredEndgamePark)
                return;

            if (Fms.MatchTimer > 0f)
                return;

            ScoreEndgamePark();
            _scoredEndgamePark = true;
        }

        private void ScoreEndgamePark()
        {
            GameObject[] robots = loadMatch.GetLoadedRobots();

            if (robots == null)
                return;

            for (int i = 0; i < robots.Length; i++)
            {
                GameObject robot = robots[i];

                if (robot == null)
                    continue;

                bool robotIsBlue = IsRobotSlotBlue(i);

                if (!IsParkValid(robot, robotIsBlue))
                    continue;

                AddPointsForRobotSlot(i, parkPointsPerRobot);
            }
        }

        private bool IsParkValid(GameObject robot, bool robotIsBlue)
        {
            return AreBumpersTouchingAllianceZone(robot, robotIsBlue) &&
                   IsRobotTouchingCarpet(robot);
        }

        private bool AreBumpersTouchingAllianceZone(GameObject robot, bool robotIsBlue)
        {
            Collider[] bumperColliders = GetBumperColliders(robot);

            if (bumperColliders == null || bumperColliders.Length == 0)
                return false;

            LayerMask zoneMask = robotIsBlue ? blueAllianceZoneMask : redAllianceZoneMask;

            QueryTriggerInteraction triggerMode = allianceZoneCollidersAreTriggers
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            foreach (Collider bumperCollider in bumperColliders)
            {
                if (!IsUsableRobotCollider(bumperCollider))
                    continue;

                if (IsColliderTouchingMask(
                        bumperCollider,
                        zoneMask,
                        zoneContactSkin,
                        triggerMode,
                        robot.transform))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsRobotTouchingCarpet(GameObject robot)
        {
            Collider[] robotColliders = robot.GetComponentsInChildren<Collider>(true);

            QueryTriggerInteraction triggerMode = carpetCollidersAreTriggers
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;

            foreach (Collider robotCollider in robotColliders)
            {
                if (!IsUsableRobotCollider(robotCollider))
                    continue;

                if (IsColliderTouchingMask(
                        robotCollider,
                        carpetMask,
                        groundContactSkin,
                        triggerMode,
                        robot.transform))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsColliderTouchingMask(
            Collider sourceCollider,
            LayerMask targetMask,
            float contactSkin,
            QueryTriggerInteraction triggerMode,
            Transform sourceRoot)
        {
            Bounds bounds = sourceCollider.bounds;

            Collider[] possibleContacts = Physics.OverlapBox(
                bounds.center,
                bounds.extents + Vector3.one * contactSkin,
                Quaternion.identity,
                targetMask,
                triggerMode
            );

            foreach (Collider targetCollider in possibleContacts)
            {
                if (targetCollider == null || !targetCollider.enabled)
                    continue;

                if (sourceRoot != null && targetCollider.transform.IsChildOf(sourceRoot))
                    continue;

                // If your zone is a trigger volume, overlap means contact/inside the zone.
                if (targetCollider.isTrigger)
                    return true;

                bool penetrating = Physics.ComputePenetration(
                    sourceCollider,
                    sourceCollider.transform.position,
                    sourceCollider.transform.rotation,
                    targetCollider,
                    targetCollider.transform.position,
                    targetCollider.transform.rotation,
                    out _,
                    out _
                );

                if (penetrating)
                    return true;

                Vector3 closestToSource = targetCollider.ClosestPoint(bounds.center);
                Vector3 closestToTarget = sourceCollider.ClosestPoint(closestToSource);
                float sqrDistance = (closestToSource - closestToTarget).sqrMagnitude;

                if (sqrDistance <= contactSkin * contactSkin)
                    return true;
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
                : Array.Empty<Collider>();
        }

        private static bool IsUsableRobotCollider(Collider collider)
        {
            return collider != null &&
                   collider.enabled &&
                   !collider.isTrigger;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
                return null;

            foreach (Transform child in root)
            {
                if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                    return child;

                Transform nested = FindChildRecursive(child, childName);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private void AddPointsForRobotSlot(int robotSlot, int points)
        {
            // ONLINE: score is server-authoritative (same gate as FieldScorer.ScorePoints). Pure clients
            // must not mutate the shared ScoreHolder. Offline this is always true, so behavior is unchanged.
            if (!FieldScorer.ServerControlsScore())
                return;

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