// CloSim Online Multiplayer — replay recorder driver for OFFLINE / single-player matches (ADDITIVE).
// Namespace: Online.Replay.Recorder. Sibling of MatchReplayRecorderRunner (which is HOST-ONLY and only
// ever runs for online matches); this file is the offline counterpart so "every game gets a replay"
// holds for local split-screen play too, not just online-hosted matches.
//
// Entered via OfflineMatchReplayRecorderRunner.RunForScene(loadMatch), called once from
// MatchSceneBootstrap right next to the existing offline path (the branch that is NOT
// Mirror.NetworkServer.active / Mirror.NetworkClient.active). Never runs online — MatchSceneBootstrap's
// online branch `return`s before reaching the offline code, so the two recorders are mutually exclusive
// by construction; this file has zero Mirror/Online.Sync/Online.Rooms dependency and reads robots
// straight off LoadMatch's own slot arrays instead of a networked roster.
//
// FIELD-READY DETECTION: unlike online play, LoadMatch has no "field ready" event for offline mode (that
// event, OnOnlineFieldReady, only fires in online mode). Offline, this runner instead polls
// LoadMatch.RobotLoaded() each frame until it goes true, then waits two more frames before snapshotting
// the roster — this rides out MatchSceneBootstrap re-applying the menu-selected MatchSettings shortly
// after LoadMatch's own Start() has already run a default 1v0 ResetField, so the runner records the
// ACTUAL match settings rather than a transient default. This is a best-effort heuristic (documented,
// not a hard guarantee); if it ever races, the worst case is a replay recorded from a still-transient
// robot set — never a change to gameplay itself, since this runner only reads state, it writes nothing
// back into LoadMatch/Fms/ScoreHolder.
//
// Timeline sampling, quantization, keyframe cadence, and finalize/save logic mirror
// MatchReplayRecorderRunner exactly (same ReplayFormat constants, same ReplayRecorder/ReplayWriter), so
// online and offline replays play back through the exact same ReplayPlaybackScreen code path.
//
// GAME PIECES: not sampled here either, for the same reason as the online recorder (see that file's
// header) — robots-only v1.

using System;
using System.Collections;
using System.Collections.Generic;
using Core;
using Field.Core;
using Field.Scoring;
using Online.Contracts;
using Online.Contracts.Replay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Online.Replay.Recorder
{
    [AddComponentMenu("CloSim/Replay/Offline Match Replay Recorder Runner")]
    public sealed class OfflineMatchReplayRecorderRunner : MonoBehaviour
    {
        private struct PrevEntityState
        {
            public int x, y, z, yaw;
        }

        /// <summary>
        /// Offline hook entry. Safe to call unconditionally from MatchSceneBootstrap's offline branch —
        /// no-ops if a LoadMatch can't be found or the match is (unexpectedly) in online mode.
        /// </summary>
        public static void RunForScene(LoadMatch loadMatch)
        {
            if (loadMatch == null)
                return;

            if (loadMatch.OnlineMode)
                return; // defensive: this path is offline-only; MatchSceneBootstrap already guarantees this

            if (FindFirstObjectByType<OfflineMatchReplayRecorderRunner>() != null)
                return; // already running for this scene load (re-entrant ResetField calls)

            var go = new GameObject(nameof(OfflineMatchReplayRecorderRunner));
            var runner = go.AddComponent<OfflineMatchReplayRecorderRunner>();
            runner.Begin(loadMatch);
        }

        private readonly ReplayRecorder _recorder = new ReplayRecorder();
        private readonly List<(int slot, Transform transform)> _robots = new();
        private readonly Dictionary<int, PrevEntityState> _prev = new();

        private LoadMatch _loadMatch;

        private float _recordStartTime;
        private int _frameIndex;
        private ReplayMatchPhase? _lastPhase;
        private bool _capturing;
        private bool _finalized;

        private void Begin(LoadMatch loadMatch)
        {
            _loadMatch = loadMatch;
            StartCoroutine(WaitForFieldReadyAndStart());
        }

        private IEnumerator WaitForFieldReadyAndStart()
        {
            const float timeoutSeconds = 5f;
            float start = Time.time;

            while (Time.time - start < timeoutSeconds)
            {
                if (_loadMatch == null)
                {
                    Destroy(gameObject);
                    yield break;
                }

                if (_loadMatch.RobotLoaded())
                    break;

                yield return null;
            }

            // Ride out any immediately-following re-reset (see file header) before trusting the roster.
            yield return null;
            yield return null;

            if (_loadMatch == null || !_loadMatch.RobotLoaded())
            {
                Destroy(gameObject); // nothing to record — no-op cleanup, never blocks the match
                yield break;
            }

            StartRecording();
        }

        private void StartRecording()
        {
            _robots.Clear();

            int playerCount = GetPlayerCount();
            GameObject[] loaded = _loadMatch.GetLoadedRobots();

            for (int slot = 0; slot < playerCount && slot < loaded.Length; slot++)
            {
                GameObject robot = loaded[slot];
                if (robot == null)
                    continue;

                _robots.Add((slot, robot.transform));
            }

            if (_robots.Count == 0)
            {
                Destroy(gameObject); // no robots to sample — nothing worth recording
                return;
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

        private int GetPlayerCount()
        {
            GameObject[] loaded = _loadMatch.GetLoadedRobots();
            int count = 0;
            for (int i = 0; i < loaded.Length; i++)
            {
                if (loaded[i] != null)
                    count = i + 1;
            }
            return count;
        }

        private ReplayHeader BuildHeader()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            string gameId = ResolveGameId(sceneName);

            int blueCount = 0, redCount = 0;
            foreach ((int slot, Transform _) in _robots)
            {
                if (_loadMatch.IsPlayerBlue(slot)) blueCount++; else redCount++;
            }

            return new ReplayHeader
            {
                schemaVersion = ReplayFormat.SchemaVersion,
                gameId = gameId,
                sceneName = sceneName,
                mode = new ReplayMode { blue = blueCount, red = redCount },
                tickRate = ReplayFormat.DefaultTickRate,
                keyframeInterval = ReplayFormat.DefaultKeyframeInterval,
                durationSec = 0f,
                finalScore = default,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                roster = BuildRoster(),
            };
        }

        /// <summary>Best-effort gameId label matching Online.Rooms.LobbyGameCatalog's ids; falls back to
        /// the scene name itself for scenes that catalog doesn't (yet) know about.</summary>
        private static string ResolveGameId(string sceneName)
        {
            foreach (Online.Rooms.LobbyGame game in Online.Rooms.LobbyGameCatalog.Default)
            {
                if (string.Equals(game.sceneName, sceneName, StringComparison.Ordinal))
                    return game.gameId;
            }
            return sceneName ?? "";
        }

        private ReplayRosterEntry[] BuildRoster()
        {
            MatchSettings settings = _loadMatch.GetSettingsCopy();
            IReadOnlyList<RobotCatalogEntry> catalog = _loadMatch.GetRobotCatalog();

            var entries = new ReplayRosterEntry[_robots.Count];
            for (int i = 0; i < _robots.Count; i++)
            {
                int slot = _robots[i].slot;
                PlayerMatchSettings player = settings.GetPlayer(slot);
                RoomAlliance alliance = _loadMatch.IsPlayerBlue(slot) ? RoomAlliance.Blue : RoomAlliance.Red;

                string displayName = "";
                int teamNumber = 0;
                foreach (RobotCatalogEntry entry in catalog)
                {
                    if (entry.Index != player.robotIndex)
                        continue;
                    teamNumber = entry.TeamNumber;
                    displayName = entry.DisplayName;
                    break;
                }

                if (string.IsNullOrEmpty(displayName))
                    displayName = $"Player {slot + 1}";

                entries[i] = new ReplayRosterEntry
                {
                    slotIndex = slot,
                    robotIndex = player.robotIndex,
                    alliance = alliance,
                    teamNumber = teamNumber,
                    displayName = displayName,
                };
            }

            return entries;
        }

        // ---------------------------------------------------------------- capture tick

        private void CaptureTick()
        {
            if (!_capturing)
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
                FinalizeAndSave();
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

        private static int QuantizePos(float meters) => Mathf.RoundToInt(meters * ReplayFormat.PositionUnitsPerMeter);

        private static int QuantizeYaw(float yawDeg)
        {
            float normalized = ((yawDeg % 360f) + 360f) % 360f;
            int raw = Mathf.RoundToInt(normalized / 360f * ReplayFormat.YawQuantSteps);
            return ((raw % ReplayFormat.YawQuantSteps) + ReplayFormat.YawQuantSteps) % ReplayFormat.YawQuantSteps;
        }

        // ---------------------------------------------------------------- finalize / save

        private void FinalizeAndSave()
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
                Debug.LogWarning($"[OfflineMatchReplayRecorderRunner] Replay blob ({blob.Length} bytes) exceeds the soft cap ({ReplayFormat.SoftMaxBlobBytes}); saving anyway.");

            SaveAsync(blob);
        }

        private async void SaveAsync(byte[] blob)
        {
            // Always saves locally (Online.Replay.Service.LocalReplayService via CompositeReplayService);
            // additionally mirrors to the AWS-backed ReplayServiceClient if/when configured. Offline play
            // never has a network session, so this is purely a local disk write — no network calls unless
            // the user has supplied ReplayServiceConfig.BaseUrl.
            var service = new Online.Replay.Service.CompositeReplayService();

            ReplayMetadata metadata = _recorder.BuildMetadata(replayId: "", userId: "", sizeBytes: blob.Length);

            ReplayUploadResult result;
            try
            {
                result = await service.UploadAsync(metadata, blob);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OfflineMatchReplayRecorderRunner] Replay save threw unexpectedly: {e.Message}");
                return;
            }

            if (!result.ok)
                Debug.LogWarning($"[OfflineMatchReplayRecorderRunner] Replay save failed: {result.error}");
            else
                Debug.Log($"[OfflineMatchReplayRecorderRunner] Replay saved: {result.replayId}");
        }

        private void OnDestroy()
        {
            CancelInvoke(nameof(CaptureTick));
        }
    }
}
