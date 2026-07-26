// CloSim Online Multiplayer — dequantizes a decoded replay timeline into absolute per-frame robot poses.
// Namespace: Online.Replay.UI. Inverse of MatchReplayRecorderRunner's quantization: keyframes are
// absolute, deltas accumulate onto each entity's running fixed-point state (kept as integer millimetres /
// quantized yaw units to avoid float drift across long delta chains), matching ReplayFormat exactly.
//
// Robots only in this pass (ReplayEntityKind.Piece snapshots are skipped) — see
// MatchReplayRecorderRunner's header comment for why game-piece capture is deferred.

using System;
using System.Collections.Generic;
using Online.Contracts.Replay;
using UnityEngine;

namespace Online.Replay.UI
{
    /// <summary>One entity's resolved (dequantized, absolute, world-space) pose at a single frame.</summary>
    public struct ResolvedPose
    {
        public Vector3 position;
        public float yawDegrees;
    }

    /// <summary>Dequantizes a <see cref="ReplayFrame"/>[] timeline into per-frame absolute robot poses.</summary>
    public static class ReplayTimelineResolver
    {
        public readonly struct Result
        {
            /// <summary>Per-frame lookup: entityId (robot slotIndex) -> resolved world pose.</summary>
            public readonly Dictionary<int, ResolvedPose>[] FramePoses;

            /// <summary>Frame indices that were keyframes, in ascending order (for stepping controls).</summary>
            public readonly int[] KeyframeIndices;

            public Result(Dictionary<int, ResolvedPose>[] framePoses, int[] keyframeIndices)
            {
                FramePoses = framePoses;
                KeyframeIndices = keyframeIndices;
            }
        }

        public static Result Resolve(ReplayFrame[] frames)
        {
            frames ??= Array.Empty<ReplayFrame>();

            var framePoses = new Dictionary<int, ResolvedPose>[frames.Length];
            var keyframeIndices = new List<int>();

            var absPosMm = new Dictionary<int, (int x, int y, int z)>();
            var absYawUnits = new Dictionary<int, int>();

            for (int i = 0; i < frames.Length; i++)
            {
                ReplayFrame f = frames[i];
                bool isKeyframe = f.kind == ReplayFrameKind.Keyframe;
                if (isKeyframe)
                    keyframeIndices.Add(i);

                ReplaySnapshot[] snaps = f.snapshots ?? Array.Empty<ReplaySnapshot>();
                foreach (ReplaySnapshot snap in snaps)
                {
                    if (snap.kind != ReplayEntityKind.Robot)
                        continue; // pieces not reconstructed in this pass

                    (int x, int y, int z) posMm;
                    int yawUnits;

                    if (isKeyframe || !absPosMm.TryGetValue(snap.entityId, out posMm))
                    {
                        // Absolute (keyframe), or the first sighting of this entity: treat the snapshot's
                        // own values as absolute. A delta frame introducing a brand-new entity (which the
                        // recorder never actually emits — every robot is sampled every tick) would be
                        // mis-decoded here; documented as a defensive fallback, not a supported timeline.
                        posMm = (snap.posX, snap.posY, snap.posZ);
                        yawUnits = unchecked((int)(ushort)snap.rot);
                    }
                    else
                    {
                        posMm = (posMm.x + snap.posX, posMm.y + snap.posY, posMm.z + snap.posZ);

                        int prevYaw = absYawUnits.TryGetValue(snap.entityId, out int py) ? py : 0;
                        int deltaYaw = unchecked((short)(ushort)snap.rot);
                        yawUnits = Mod(prevYaw + deltaYaw, ReplayFormat.YawQuantSteps);
                    }

                    absPosMm[snap.entityId] = posMm;
                    absYawUnits[snap.entityId] = yawUnits;
                }

                var frameLookup = new Dictionary<int, ResolvedPose>(absPosMm.Count);
                foreach (KeyValuePair<int, (int x, int y, int z)> kvp in absPosMm)
                {
                    int entityId = kvp.Key;
                    (int x, int y, int z) mm = kvp.Value;
                    int yawUnits = absYawUnits.TryGetValue(entityId, out int yu) ? yu : 0;

                    frameLookup[entityId] = new ResolvedPose
                    {
                        position = new Vector3(
                            mm.x / (float)ReplayFormat.PositionUnitsPerMeter,
                            mm.y / (float)ReplayFormat.PositionUnitsPerMeter,
                            mm.z / (float)ReplayFormat.PositionUnitsPerMeter),
                        yawDegrees = yawUnits / (float)ReplayFormat.YawQuantSteps * 360f,
                    };
                }

                framePoses[i] = frameLookup;
            }

            return new Result(framePoses, keyframeIndices.ToArray());
        }

        private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
    }
}
