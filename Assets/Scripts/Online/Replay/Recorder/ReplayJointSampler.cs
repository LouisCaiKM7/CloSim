// CloSim Online Multiplayer — replay articulation sampler. Namespace: Online.Replay.Recorder.
//
// Captures each robot's MOVING mechanism child transforms (arm/intake/shooter/wheels/...) as local poses
// so a replay shows the robot FUNCTION, not just slide by its root. Used by both recorder runners
// (online host + offline). Engine-facing (reads Transforms) — the pure ReplayRecorder stays UnityEngine-free.
//
// KEYING: a joint is addressed by its depth-first index among the robot ROOT's descendant transforms.
// Record and playback both Instantiate the SAME catalog prefab (by robotIndex), so the descendant
// enumeration matches; playback re-enumerates the same way and applies each joint by that index.
//
// WHAT'S RECORDED: a descendant is emitted once it has ever moved beyond a small epsilon from its bind
// (rest) pose, and from then on EVERY frame (absolute local pose) — so scrubbing to any time lands the
// mechanism correctly. Parts that never move are never recorded, keeping the channel to the few real joints.

using System;
using System.Collections.Generic;
using Online.Contracts.Replay;
using UnityEngine;

namespace Online.Replay.Recorder
{
    /// <summary>Samples robots' moving-joint local poses into <see cref="ReplayRobotJoints"/> per frame.</summary>
    public sealed class ReplayJointSampler
    {
        private const float PositionEpsilonMeters = 0.001f;    // 1 mm
        private const float RotationEpsilonDegrees = 0.5f;

        private sealed class RobotEntry
        {
            public Transform[] descendants;
            public Vector3[] bindPos;
            public Quaternion[] bindRot;
            public bool[] active;
        }

        private readonly Dictionary<int, RobotEntry> _robots = new Dictionary<int, RobotEntry>();

        /// <summary>Snapshot a robot's descendant transforms + their bind poses at record start.</summary>
        public void Register(int slot, Transform root)
        {
            if (root == null)
                return;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var desc = new List<Transform>(all.Length);
            foreach (Transform t in all)
                if (t != root)
                    desc.Add(t);

            var entry = new RobotEntry
            {
                descendants = desc.ToArray(),
                bindPos = new Vector3[desc.Count],
                bindRot = new Quaternion[desc.Count],
                active = new bool[desc.Count],
            };
            for (int i = 0; i < desc.Count; i++)
            {
                entry.bindPos[i] = desc[i].localPosition;
                entry.bindRot[i] = desc[i].localRotation;
            }

            _robots[slot] = entry;
        }

        public bool HasRobot(int slot) => _robots.ContainsKey(slot);

        /// <summary>
        /// The moving-joint local poses for this robot right now. Returns an empty set (never null joints)
        /// when the robot isn't registered or nothing has moved yet.
        /// </summary>
        public ReplayRobotJoints Sample(int slot)
        {
            if (!_robots.TryGetValue(slot, out RobotEntry entry))
                return new ReplayRobotJoints { slotIndex = slot, joints = Array.Empty<ReplayJoint>() };

            List<ReplayJoint> joints = null;
            for (int i = 0; i < entry.descendants.Length; i++)
            {
                Transform t = entry.descendants[i];
                if (t == null)
                    continue;

                Vector3 lp = t.localPosition;
                Quaternion lr = t.localRotation;

                if (!entry.active[i])
                {
                    bool moved =
                        (lp - entry.bindPos[i]).sqrMagnitude > (PositionEpsilonMeters * PositionEpsilonMeters)
                        || Quaternion.Angle(lr, entry.bindRot[i]) > RotationEpsilonDegrees;
                    if (!moved)
                        continue;
                    entry.active[i] = true; // once a joint moves, record it every subsequent frame
                }

                joints ??= new List<ReplayJoint>();
                joints.Add(new ReplayJoint
                {
                    jointIndex = i,
                    posX = Quantize(lp.x),
                    posY = Quantize(lp.y),
                    posZ = Quantize(lp.z),
                    rotX = lr.x,
                    rotY = lr.y,
                    rotZ = lr.z,
                    rotW = lr.w,
                });
            }

            return new ReplayRobotJoints
            {
                slotIndex = slot,
                joints = joints != null ? joints.ToArray() : Array.Empty<ReplayJoint>(),
            };
        }

        private static int Quantize(float meters) =>
            Mathf.RoundToInt(meters * ReplayFormat.PositionUnitsPerMeter);
    }
}
