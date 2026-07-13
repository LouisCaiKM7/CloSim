using System;
using System.Collections.Generic;
using Core;
using Field.Core;
using MyBox;
using Robot.Runtime;
using UnityEngine;
using Utilities;
using PlayMode = Core.PlayMode;


namespace Field.Scoring
{
    public class ClimbScorer : MonoBehaviour
    {
        private enum ClimbGame
        {
            Rebuilt,
            Reefscape
        }

        private enum ClimbResult
        {
            None,
            RebuiltL1,
            RebuiltL2,
            RebuiltL3,
            ReefscapeShallow,
            ReefscapeDeep
        }

        [Header("References")]
        [SerializeField] private LoadMatch loadMatch;

        [Header("Game")]
        [SerializeField] private ClimbGame climbGame = ClimbGame.Rebuilt;

        private bool IsRebuilt() => climbGame == ClimbGame.Rebuilt;
        private bool IsReefscape() => climbGame == ClimbGame.Reefscape;

        [Header("Ground / Carpet")]
        [SerializeField] private LayerMask carpetMask;
        [SerializeField] private float groundContactSkin = 0.02f;
        [SerializeField] private bool carpetCollidersAreTriggers;

        [Header("Alliance Zones")]
        [SerializeField] private LayerMask blueAllianceZoneMask;
        [SerializeField] private LayerMask redAllianceZoneMask;
        [SerializeField] private float allianceZoneContactSkin = 0.01f;
        [SerializeField] private bool allianceZoneCollidersAreTriggers = true;

        [Header("Rebuilt Points")]
        [ConditionalField(true, nameof(IsRebuilt))]
        [SerializeField] private int rebuiltAutoClimbPoints = 15;
        
        [ConditionalField(true, nameof(IsRebuilt))]
        [SerializeField] private int rebuiltL1Points = 10;

        [ConditionalField(true, nameof(IsRebuilt))]
        [SerializeField] private int rebuiltL2Points = 20;

        [ConditionalField(true, nameof(IsRebuilt))]
        [SerializeField] private int rebuiltL3Points = 30;

        [Header("Rebuilt Rung Thresholds")]
        [Tooltip("Forbidden bumper-contact volume for Rebuilt L2. If bumpers touch this mask, L2 does not count.")]
        [ConditionalField(true, nameof(IsRebuilt))]
        [SerializeField] private LayerMask rebuiltL1RungInvalidMask;

        [Tooltip("Forbidden bumper-contact volume for Rebuilt L3. If bumpers touch this mask, L3 does not count.")]
        [ConditionalField(true, nameof(IsRebuilt))]
        [SerializeField] private LayerMask rebuiltL2RungInvalidMask;

        [ConditionalField(true, nameof(IsRebuilt))]
        [SerializeField] private float rebuiltRungContactSkin = 0.01f;

        [ConditionalField(true, nameof(IsRebuilt))]
        [SerializeField] private bool rebuiltRungCollidersAreTriggers = true;

        [Header("Reefscape Points")]
        [ConditionalField(true, nameof(IsReefscape))]
        [SerializeField] private int reefscapeShallowPoints = 6;

        [ConditionalField(true, nameof(IsReefscape))]
        [SerializeField] private int reefscapeDeepPoints = 12;

        [Header("Reefscape Cages")]
        [ConditionalField(true, nameof(IsReefscape))]
        [SerializeField] private LayerMask shallowCageMask;

        [ConditionalField(true, nameof(IsReefscape))]
        [SerializeField] private LayerMask deepCageMask;

        [ConditionalField(true, nameof(IsReefscape))]
        [SerializeField] private float cageContactSkin = 0.01f;

        [ConditionalField(true, nameof(IsReefscape))]
        [SerializeField] private bool cageCollidersAreTriggers = true;

        [Header("Bumper Lookup")]
        [SerializeField] private string bumperRootName = "bumpers";

        [ConditionalField(true, nameof(IsRebuilt))]
        [SerializeField] private bool fallbackToAllRobotCollidersForBumpers;

        [Header("Climber Lookup")]
        [ConditionalField(true, nameof(IsReefscape))]
        [Tooltip("Used only if no ClimberComponent exists on the robot.")]
        [SerializeField] private string climberRootName = "Climber";

        [ConditionalField(true, nameof(IsReefscape))]
        [SerializeField] private bool fallbackToAllRobotCollidersForClimber;

        private Fms _fms;
        private bool _scoredRebuiltAutoClimb;
        private bool _scoredEndgameClimb;

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
            _scoredRebuiltAutoClimb = false;
            _scoredEndgameClimb = false;
        }

        private void Update()
        {
            if (_fms == null || loadMatch == null)
                return;

            float autoEndTimerValue = _fms.matchTime - _fms.autoTime;
            
            if (_scoredRebuiltAutoClimb && Fms.MatchTimer > autoEndTimerValue + 0.25f)
                _scoredRebuiltAutoClimb = false;

            if (_scoredEndgameClimb && Fms.MatchTimer > 0.25f)
                _scoredEndgameClimb = false;

            if (climbGame == ClimbGame.Rebuilt &&
                !_scoredRebuiltAutoClimb &&
                Fms.MatchTimer <= autoEndTimerValue)
            {
                ScoreRebuiltAutoClimbs();
                _scoredRebuiltAutoClimb = true;
            }

            if (!_scoredEndgameClimb && Fms.MatchTimer <= 0f)
            {
                ScoreClimbs();
                _scoredEndgameClimb = true;
            }
        }
        
        private void ScoreRebuiltAutoClimbs()
        {
            GameObject[] robots = loadMatch.GetLoadedRobots();

            if (robots == null)
                return;

            for (int i = 0; i < robots.Length; i++)
            {
                GameObject robot = robots[i];

                if (robot == null)
                    continue;

                if (!IsRobotOffGround(robot))
                    continue;

                AddPointsForRobotSlot(i, rebuiltAutoClimbPoints);
            }
        }

        private void ScoreClimbs()
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
                ClimbResult result = EvaluateRobotClimb(robot, robotIsBlue);
                int points = GetPoints(result);

                if (points <= 0)
                    continue;

                AddPointsForRobotSlot(i, points);
            }
        }

        private ClimbResult EvaluateRobotClimb(GameObject robot, bool robotIsBlue)
        {
            switch (climbGame)
            {
                case ClimbGame.Rebuilt:
                    return EvaluateRebuiltClimb(robot, robotIsBlue);

                case ClimbGame.Reefscape:
                    return EvaluateReefscapeClimb(robot, robotIsBlue);

                default:
                    return ClimbResult.None;
            }
        }

        private ClimbResult EvaluateRebuiltClimb(GameObject robot, bool robotIsBlue)
        {
            if (!IsRobotOffGround(robot))
                return ClimbResult.None;

            if (!IsRobotInAllianceZone(robot, robotIsBlue))
                return ClimbResult.None;

            Collider[] bumperColliders = GetBumperColliders(robot);

            if (bumperColliders.Length == 0)
                return ClimbResult.None;

            bool clearL1Rung = !MaskIsEmpty(rebuiltL1RungInvalidMask) &&
                               !AnyColliderTouchingMask(
                                   bumperColliders,
                                   rebuiltL1RungInvalidMask,
                                   rebuiltRungContactSkin,
                                   GetTriggerMode(rebuiltRungCollidersAreTriggers),
                                   robot.transform
                               );

            bool clearL2Rung = !MaskIsEmpty(rebuiltL2RungInvalidMask) &&
                               !AnyColliderTouchingMask(
                                   bumperColliders,
                                   rebuiltL2RungInvalidMask,
                                   rebuiltRungContactSkin,
                                   GetTriggerMode(rebuiltRungCollidersAreTriggers),
                                   robot.transform
                               );

            // Highest valid climb wins.
            if (clearL2Rung)
                return ClimbResult.RebuiltL3;

            if (clearL1Rung)
                return ClimbResult.RebuiltL2;

            return ClimbResult.RebuiltL1;
        }

        private ClimbResult EvaluateReefscapeClimb(GameObject robot, bool robotIsBlue)
        {
            if (!IsRobotOffGround(robot))
                return ClimbResult.None;

            if (!IsRobotInAllianceZone(robot, robotIsBlue))
                return ClimbResult.None;

            Collider[] climberColliders = GetClimberColliders(robot);

            if (climberColliders.Length == 0)
                return ClimbResult.None;

            // Highest valid climb wins.
            if (AnyColliderTouchingMask(
                    climberColliders,
                    deepCageMask,
                    cageContactSkin,
                    GetTriggerMode(cageCollidersAreTriggers),
                    robot.transform))
            {
                return ClimbResult.ReefscapeDeep;
            }

            if (AnyColliderTouchingMask(
                    climberColliders,
                    shallowCageMask,
                    cageContactSkin,
                    GetTriggerMode(cageCollidersAreTriggers),
                    robot.transform))
            {
                return ClimbResult.ReefscapeShallow;
            }

            return ClimbResult.None;
        }

        private bool IsRobotOffGround(GameObject robot)
        {
            Collider[] robotColliders = robot.GetComponentsInChildren<Collider>(true);

            bool touchingGround = AnyColliderTouchingMask(
                robotColliders,
                carpetMask,
                groundContactSkin,
                GetTriggerMode(carpetCollidersAreTriggers),
                robot.transform
            );

            return !touchingGround;
        }

        private bool IsRobotInAllianceZone(GameObject robot, bool robotIsBlue)
        {
            LayerMask zoneMask = robotIsBlue ? blueAllianceZoneMask : redAllianceZoneMask;

            Collider[] robotColliders = robot.GetComponentsInChildren<Collider>(true);

            return AnyColliderTouchingMask(
                robotColliders,
                zoneMask,
                allianceZoneContactSkin,
                GetTriggerMode(allianceZoneCollidersAreTriggers),
                robot.transform
            );
        }

        private Collider[] GetBumperColliders(GameObject robot)
        {
            Transform bumperRoot = FindChildRecursive(robot.transform, bumperRootName);

            if (bumperRoot != null)
                return FilterUsableColliders(bumperRoot.GetComponentsInChildren<Collider>(true));

            return fallbackToAllRobotCollidersForBumpers
                ? FilterUsableColliders(robot.GetComponentsInChildren<Collider>(true))
                : Array.Empty<Collider>();
        }

        private Collider[] GetClimberColliders(GameObject robot)
        {
            ClimberComponent[] climbers = robot.GetComponentsInChildren<ClimberComponent>(true);

            if (climbers is { Length: > 0 })
            {
                List<Collider> colliders = new();

                foreach (ClimberComponent climber in climbers)
                {
                    if (climber == null)
                        continue;

                    colliders.AddRange(climber.GetComponentsInChildren<Collider>(true));
                }

                return FilterUsableColliders(colliders.ToArray());
            }

            Transform climberRoot = FindChildRecursive(robot.transform, climberRootName);

            if (climberRoot != null)
                return FilterUsableColliders(climberRoot.GetComponentsInChildren<Collider>(true));

            return fallbackToAllRobotCollidersForClimber
                ? FilterUsableColliders(robot.GetComponentsInChildren<Collider>(true))
                : Array.Empty<Collider>();
        }

        private static Collider[] FilterUsableColliders(Collider[] colliders)
        {
            if (colliders == null || colliders.Length == 0)
                return Array.Empty<Collider>();

            List<Collider> filtered = new();

            foreach (Collider collider in colliders)
            {
                if (IsUsableRobotCollider(collider))
                    filtered.Add(collider);
            }

            return filtered.ToArray();
        }

        private static bool AnyColliderTouchingMask(
            Collider[] sourceColliders,
            LayerMask targetMask,
            float contactSkin,
            QueryTriggerInteraction triggerMode,
            Transform sourceRoot)
        {
            if (sourceColliders == null || sourceColliders.Length == 0)
                return false;

            if (MaskIsEmpty(targetMask))
                return false;

            foreach (Collider sourceCollider in sourceColliders)
            {
                if (!IsUsableRobotCollider(sourceCollider))
                    continue;

                if (IsColliderTouchingMask(
                        sourceCollider,
                        targetMask,
                        contactSkin,
                        triggerMode,
                        sourceRoot))
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

                // Trigger volumes represent "inside/touching this scoring area."
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

        private int GetPoints(ClimbResult result)
        {
            switch (result)
            {
                case ClimbResult.RebuiltL1:
                    return rebuiltL1Points;

                case ClimbResult.RebuiltL2:
                    return rebuiltL2Points;

                case ClimbResult.RebuiltL3:
                    return rebuiltL3Points;

                case ClimbResult.ReefscapeShallow:
                    return reefscapeShallowPoints;

                case ClimbResult.ReefscapeDeep:
                    return reefscapeDeepPoints;

                default:
                    return 0;
            }
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
                case PlayMode.OneVsZero:
                case PlayMode.TwoVsZero:
                case PlayMode.ThreeVsZero:
                    return loadMatch.UsesBlueAlliance();

                case PlayMode.OneVsOne:
                    return robotSlot == 0;

                case PlayMode.TwoVsTwo:
                    return robotSlot < 2;

                default:
                    return true;
            }
        }

        private static bool IsUsableRobotCollider(Collider collider)
        {
            return collider != null &&
                   collider.enabled &&
                   !collider.isTrigger;
        }

        private static QueryTriggerInteraction GetTriggerMode(bool includeTriggers)
        {
            return includeTriggers
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;
        }

        private static bool MaskIsEmpty(LayerMask mask)
        {
            return mask.value == 0;
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
    }
}