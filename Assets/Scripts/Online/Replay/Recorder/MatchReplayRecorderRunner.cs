// CloSim Online Multiplayer — replay recorder driver (host-only). Namespace: Online.Replay.Recorder.
//
// Bridges the engine world to the dependency-free Online.Replay.Recorder.ReplayRecorder + the wire codec
// (Online.Replay.Codec.ReplayWriter) + the backend client (Online.Replay.Service.ReplayServiceClient).
//
// HOST-ONLY, NEVER runs offline: entered via MatchReplayRecorderRunner.RunForScene(loadMatch), called
// once from MatchSceneBootstrap right next to the existing online hook (Online.Sync.MatchSpawnManager.
// RunForScene). RunForScene itself no-ops unless Mirror.NetworkServer.active — a pure client or offline
// play never even instantiates this component, so there is zero behavioral change for those paths.
//
// Timeline: waits for NetworkMatchContext.MatchSpawned (all robots server-spawned) before sampling, then
// snapshots every robot's transform at ReplayFormat.DefaultTickRate, emitting a Keyframe (absolute
// quantized position/yaw) every ReplayFormat.DefaultKeyframeInterval frames and a Delta (signed
// quantized difference from the previous frame) otherwise — exactly what ReplayWriter/ReplayReader
// expect. On Fms reaching MatchState.Finished it finalizes (durationSec + ScoreHolder totals) and
// uploads via ReplayServiceClient, which is itself a graceful no-op while ReplayServiceConfig.BaseUrl
// is blank (golden rule 2) — so this never blocks or breaks a match today.
//
// GAME PIECES: not sampled in this pass. Game-piece network identity (Online.Sync.Pieces.*) is being
// wired up separately; once a piece has a stable network id, its ReplaySnapshot(kind=Piece) can be
// captured the same way robots are here. Leaving pieces out keeps this recorder decoupled from that
// parallel work and still delivers a fully watchable robot-only replay.

using System;
using System.Collections.Generic;
using Core;
using Field.Core;
using Field.Scoring;
using Mirror;
using Online.Contracts;
using Online.Contracts.Replay;
using UnityEngine;

namespace Online.Replay.Recorder
{
    [AddComponentMenu("CloSim/Replay/Match Replay Recorder Runner")]
    public sealed class MatchReplayRecorderRunner : MonoBehaviour
    {
        private struct PrevEntityState
        {
            public int x, y, z, yaw;
        }

        /// <summary>
        /// Online hook entry (host-only). Safe to call unconditionally from MatchSceneBootstrap's online
        /// branch — no-ops on pure clients and is never reached at all offline.
        /// </summary>
        public static void RunForScene(LoadMatch loadMatch)
        {
            if (!NetworkServer.active)
                return; // recorder is host-only; pure clients never record

            if (loadMatch == null)
                loadMatch = Online.Sync.MatchSpawnManager.FindLoadMatch();
            if (loadMatch == null)
                return;

            if (FindFirstObjectByType<MatchReplayRecorderRunner>() != null)
                return; // already running for this scene load (re-entrant ResetField calls)

            var go = new GameObject(nameof(MatchReplayRecorderRunner));
            var runner = go.AddComponent<MatchReplayRecorderRunner>();
            runner.Begin(loadMatch);
        }

        private readonly ReplayRecorder _recorder = new ReplayRecorder();
        private readonly List<(int slot, Transform transform)> _robots = new();
        private readonly Dictionary<int, PrevEntityState> _prev = new();

        private LoadMatch _loadMatch;
        private Online.Sync.NetworkMatchContext _context;

        private float _recordStartTime;
        private int _frameIndex;
        private ReplayMatchPhase? _lastPhase;
        private int _lastBlueScore = int.MinValue;
        private int _lastRedScore = int.MinValue;
        private bool _capturing;
        private bool _finalized;

        private void Begin(LoadMatch loadMatch)
        {
            _loadMatch = loadMatch;
            _context = Online.Sync.NetworkMatchContext.Instance;

            if (_context == null || _context.BuiltSettings == null)
            {
                Debug.LogWarning("[MatchReplayRecorderRunner] No NetworkMatchContext plan; skipping replay recording for this match.");
                Destroy(gameObject);
                return;
            }

            _context.MatchSpawned += OnMatchSpawned;
            if (_context.IsSpawned)
                OnMatchSpawned();
        }

        private void OnMatchSpawned()
        {
            if (_context != null)
                _context.MatchSpawned -= OnMatchSpawned;

            StartRecording();
        }

        private void StartRecording()
        {
            _robots.Clear();
            foreach (Online.Sync.RobotNetworkController rnc in FindObjectsByType<Online.Sync.RobotNetworkController>(FindObjectsSortMode.None))
            {
                if (rnc == null || rnc.Slot < 0)
                    continue;
                _robots.Add((rnc.Slot, rnc.transform));
            }

            ReplayHeader header = BuildHeader();
            _recorder.BeginRecording(header);

            _recordStartTime = Time.time;
            _frameIndex = 0;
            _lastPhase = null;
            _prev.Clear();
            _capturing = true;

            float interval = 1f / Mathf.Max(1, ReplayFormat.DefaultTickRate);
            InvokeRepeating(nameof(CaptureTick), 0f, interval);
        }

        // ---------------------------------------------------------------- header / roster

        private ReplayHeader BuildHeader()
        {
            MatchSettings built = _context.BuiltSettings;

            return new ReplayHeader
            {
                schemaVersion = ReplayFormat.SchemaVersion,
                gameId = _context.Config.gameId ?? "",
                sceneName = _context.Config.sceneName ?? "",
                mode = new ReplayMode { blue = built.networkBlueCount, red = built.networkRedCount },
                tickRate = ReplayFormat.DefaultTickRate,
                keyframeInterval = ReplayFormat.DefaultKeyframeInterval,
                durationSec = 0f,
                finalScore = default,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                roster = BuildRoster(built),
            };
        }

        private ReplayRosterEntry[] BuildRoster(MatchSettings built)
        {
            int blueCount = built.networkBlueCount;
            int redCount = built.networkRedCount;
            int total = Mathf.Clamp(blueCount + redCount, 1, 6);

            IReadOnlyList<RoomMemberSlot> roster = _context.Roster;
            int[] slotConnectionIds = _context.SlotConnectionIds;
            IReadOnlyList<RobotCatalogEntry> catalog = _loadMatch.GetRobotCatalog();

            var entries = new ReplayRosterEntry[total];
            for (int slot = 0; slot < total; slot++)
            {
                PlayerMatchSettings player = built.GetPlayer(slot);
                RoomAlliance alliance = slot < blueCount ? RoomAlliance.Blue : RoomAlliance.Red;

                string displayName = "";
                int connId = (slotConnectionIds != null && slot < slotConnectionIds.Length) ? slotConnectionIds[slot] : -1;

                if (roster != null)
                {
                    foreach (RoomMemberSlot member in roster)
                    {
                        bool matchesHost = slot == _context.HostOwnedSlot && member.isHost;
                        if (matchesHost || (connId >= 0 && member.connectionId == connId))
                        {
                            displayName = member.displayName;
                            break;
                        }
                    }
                }

                int teamNumber = 0;
                foreach (RobotCatalogEntry entry in catalog)
                {
                    if (entry.Index != player.robotIndex)
                        continue;
                    teamNumber = entry.TeamNumber;
                    if (string.IsNullOrEmpty(displayName))
                        displayName = entry.DisplayName;
                    break;
                }

                entries[slot] = new ReplayRosterEntry
                {
                    slotIndex = slot,
                    robotIndex = player.robotIndex,
                    alliance = alliance,
                    teamNumber = teamNumber,
                    displayName = displayName ?? "",
                };
            }

            return entries;
        }

        // ---------------------------------------------------------------- capture tick

        private void CaptureTick()
        {
            if (!_capturing || !NetworkServer.active)
                return;

            uint timestampMs = (uint)Mathf.Max(0, Mathf.RoundToInt((Time.time - _recordStartTime) * 1000f));
            bool isKeyframe = (_frameIndex % Mathf.Max(1, (int)ReplayFormat.DefaultKeyframeInterval)) == 0;

            if (_frameIndex == 0)
            {
                _recorder.RecordEvent(new ReplayEvent
                {
                    timestampMs = timestampMs,
                    type = ReplayEventType.MatchStart,
                    alliance = RoomAlliance.Unassigned,
                    actorSlot = -1,
                    pieceId = -1,
                });
            }

            RecordPhaseChangeIfNeeded(timestampMs);
            RecordScoreChangeIfNeeded(timestampMs);

            bool matchJustFinished = Fms.MatchState == MatchState.Finished && !_finalized;
            if (matchJustFinished)
            {
                _recorder.RecordEvent(new ReplayEvent
                {
                    timestampMs = timestampMs,
                    type = ReplayEventType.MatchEnd,
                    alliance = RoomAlliance.Unassigned,
                    actorSlot = -1,
                    pieceId = -1,
                });
            }

            var snapshots = new List<ReplaySnapshot>(_robots.Count);
            foreach ((int slot, Transform t) in _robots)
            {
                if (t == null)
                    continue; // robot destroyed mid-match; skip gracefully

                snapshots.Add(BuildRobotSnapshot(slot, t, isKeyframe));
            }

            var frame = new ReplayFrame
            {
                timestampMs = timestampMs,
                kind = isKeyframe ? ReplayFrameKind.Keyframe : ReplayFrameKind.Delta,
                snapshots = snapshots.ToArray(),
                events = null,
            };

            _recorder.RecordFrame(frame);
            _frameIndex++;

            if (matchJustFinished)
                FinalizeAndUpload();
        }

        private ReplaySnapshot BuildRobotSnapshot(int slot, Transform t, bool isKeyframe)
        {
            Vector3 pos = t.position;
            int qx = QuantizePos(pos.x);
            int qy = QuantizePos(pos.y);
            int qz = QuantizePos(pos.z);
            int qYaw = QuantizeYaw(t.eulerAngles.y);

            int outX, outY, outZ;
            uint outRot;

            if (isKeyframe || !_prev.TryGetValue(slot, out PrevEntityState prev))
            {
                outX = qx;
                outY = qy;
                outZ = qz;
                outRot = unchecked((uint)qYaw);
            }
            else
            {
                outX = qx - prev.x;
                outY = qy - prev.y;
                outZ = qz - prev.z;
                int yawDelta = qYaw - prev.yaw;
                outRot = unchecked((uint)(ushort)yawDelta);
            }

            _prev[slot] = new PrevEntityState { x = qx, y = qy, z = qz, yaw = qYaw };

            return new ReplaySnapshot
            {
                kind = ReplayEntityKind.Robot,
                entityId = slot,
                posX = outX,
                posY = outY,
                posZ = outZ,
                rotEncoding = ReplayRotationEncoding.Yaw16,
                rot = outRot,
            };
        }

        private void RecordPhaseChangeIfNeeded(uint timestampMs)
        {
            var current = (ReplayMatchPhase)(int)Fms.MatchState;
            if (_lastPhase.HasValue && _lastPhase.Value == current)
                return;

            _lastPhase = current;
            _recorder.RecordEvent(new ReplayEvent
            {
                timestampMs = timestampMs,
                type = ReplayEventType.PhaseChange,
                alliance = RoomAlliance.Unassigned,
                actorSlot = -1,
                pieceId = -1,
                intValue = (int)current,
            });
        }

        // Records the real match score onto the timeline whenever it changes, so playback DISPLAYS the exact
        // recorded score (absolute per-alliance in intValue; the viewer tracks the latest per alliance).
        private void RecordScoreChangeIfNeeded(uint timestampMs)
        {
            int blue = ScoreHolder.BlueScore;
            int red = ScoreHolder.RedScore;

            if (blue != _lastBlueScore)
            {
                _lastBlueScore = blue;
                _recorder.RecordEvent(new ReplayEvent
                {
                    timestampMs = timestampMs,
                    type = ReplayEventType.Score,
                    alliance = RoomAlliance.Blue,
                    actorSlot = -1,
                    pieceId = -1,
                    intValue = blue,
                });
            }

            if (red != _lastRedScore)
            {
                _lastRedScore = red;
                _recorder.RecordEvent(new ReplayEvent
                {
                    timestampMs = timestampMs,
                    type = ReplayEventType.Score,
                    alliance = RoomAlliance.Red,
                    actorSlot = -1,
                    pieceId = -1,
                    intValue = red,
                });
            }
        }

        private static int QuantizePos(float meters) => Mathf.RoundToInt(meters * ReplayFormat.PositionUnitsPerMeter);

        private static int QuantizeYaw(float yawDeg)
        {
            float normalized = ((yawDeg % 360f) + 360f) % 360f;
            int raw = Mathf.RoundToInt(normalized / 360f * ReplayFormat.YawQuantSteps);
            return ((raw % ReplayFormat.YawQuantSteps) + ReplayFormat.YawQuantSteps) % ReplayFormat.YawQuantSteps;
        }

        // ---------------------------------------------------------------- finalize / upload

        private void FinalizeAndUpload()
        {
            if (_finalized)
                return;

            _finalized = true;
            _capturing = false;
            CancelInvoke(nameof(CaptureTick));

            float durationSec = Time.time - _recordStartTime;
            var finalScore = new ReplayScore { blue = ScoreHolder.BlueScore, red = ScoreHolder.RedScore };

            byte[] blob = _recorder.StopAndSerialize(durationSec, finalScore);
            if (blob.Length > ReplayFormat.SoftMaxBlobBytes)
                Debug.LogWarning($"[MatchReplayRecorderRunner] Replay blob ({blob.Length} bytes) exceeds the soft cap ({ReplayFormat.SoftMaxBlobBytes}); uploading anyway.");

            UploadAsync(blob);
        }

        private async void UploadAsync(byte[] blob)
        {
            // ADDITIVE: saves via CompositeReplayService, which ALWAYS writes a local copy under
            // Application.persistentDataPath (Online.Replay.Service.LocalReplayService) and additionally
            // mirrors to the AWS-backed ReplayServiceClient when ReplayServiceConfig is configured. This
            // is what makes host-recorded online matches watchable even before the user supplies an AWS
            // endpoint (golden rule 2) — previously this path discarded the recording entirely while
            // BaseUrl was blank.
            var service = new Online.Replay.Service.CompositeReplayService();

            ReplayMetadata metadata = _recorder.BuildMetadata(replayId: "", userId: "", sizeBytes: blob.Length);

            ReplayUploadResult result;
            try
            {
                result = await service.UploadAsync(metadata, blob);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MatchReplayRecorderRunner] Replay save/upload threw unexpectedly: {e.Message}");
                return;
            }

            if (!result.ok)
                Debug.LogWarning($"[MatchReplayRecorderRunner] Replay save failed: {result.error}");
            else
                Debug.Log($"[MatchReplayRecorderRunner] Replay saved: {result.replayId}");
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.MatchSpawned -= OnMatchSpawned;

            CancelInvoke(nameof(CaptureTick));
        }
    }
}
