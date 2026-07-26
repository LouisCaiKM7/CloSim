// CloSim Online Multiplayer — replay recorder: pure data accumulator implementing IReplayRecorder.
// Namespace: Online.Replay.Recorder. Dependency-free of UnityEngine/Mirror (mirrors the discipline of
// Online.Replay.Codec / Online.Contracts.Replay) so it stays trivially testable — all the engine-facing
// work (finding robots, sampling transforms, quantizing, detecting match end) lives in the companion
// MonoBehaviour driver, MatchReplayRecorderRunner, which calls into this class exactly like an
// EditMode test would.
//
// This class only assembles the timeline the caller hands it (BeginRecording/RecordFrame/RecordEvent)
// and serializes it via Online.Replay.Codec.ReplayWriter on StopAndSerialize — it does not sample the
// world itself and does not decide keyframe cadence (the caller marks each ReplayFrame.kind).

using System;
using System.Collections.Generic;
using Online.Contracts.Replay;
using Online.Replay.Codec;

namespace Online.Replay.Recorder
{
    /// <summary>
    /// Default <see cref="IReplayRecorder"/> implementation. Buffers frames in recording order and folds
    /// any events queued via <see cref="RecordEvent"/> into the next appended frame (per the interface's
    /// "implementations may fold them into the next frame" contract).
    /// </summary>
    public sealed class ReplayRecorder : IReplayRecorder
    {
        private readonly List<ReplayFrame> _frames = new List<ReplayFrame>();
        private readonly List<ReplayEvent> _pendingEvents = new List<ReplayEvent>();

        private ReplayHeader _header;
        private bool _isRecording;

        public bool IsRecording => _isRecording;
        public ReplayHeader Header => _header;
        public int FrameCount => _frames.Count;

        public void BeginRecording(ReplayHeader header)
        {
            _header = header;
            _frames.Clear();
            _pendingEvents.Clear();
            _isRecording = true;
        }

        public void RecordFrame(ReplayFrame frame)
        {
            if (!_isRecording)
                return;

            if (_pendingEvents.Count > 0)
            {
                var merged = new List<ReplayEvent>(frame.events ?? Array.Empty<ReplayEvent>());
                merged.AddRange(_pendingEvents);
                frame.events = merged.ToArray();
                _pendingEvents.Clear();
            }

            _frames.Add(frame);
        }

        public void RecordEvent(ReplayEvent gameEvent)
        {
            if (!_isRecording)
                return;

            _pendingEvents.Add(gameEvent);
        }

        public byte[] StopAndSerialize(float durationSec, ReplayScore finalScore)
        {
            _header.durationSec = durationSec;
            _header.finalScore = finalScore;

            byte[] blob = ReplayWriter.Write(_header, _frames);
            _isRecording = false;
            return blob;
        }

        public ReplayMetadata BuildMetadata(string replayId, string userId, int sizeBytes)
        {
            return new ReplayMetadata
            {
                replayId = replayId ?? "",
                userId = userId ?? "",
                gameId = _header.gameId ?? "",
                mode = _header.mode,
                sceneName = _header.sceneName ?? "",
                durationSec = _header.durationSec,
                finalScore = _header.finalScore,
                sizeBytes = sizeBytes,
                createdAt = _header.createdAt,
                schemaVersion = _header.schemaVersion,
            };
        }

        public void Reset()
        {
            _frames.Clear();
            _pendingEvents.Clear();
            _header = default;
            _isRecording = false;
        }
    }
}
