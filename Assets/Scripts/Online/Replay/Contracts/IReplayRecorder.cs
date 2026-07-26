// CloSim Online Multiplayer — replay recorder contract (stub).
// SOURCE OF TRUTH: Documentation/online/architecture.md "Replay System".
// Owned by A1 (Architect). IMPLEMENTED host-side by the Gameplay Sync layer (A4-adjacent).
//
// The recorder runs on the HOST (server-authoritative state) so the captured timeline is the single
// canonical match. It samples robot + game-piece transforms at header.tickRate, emits periodic
// keyframes (else deltas), and appends discrete events. StopAndSerialize produces the opaque blob.

namespace Online.Contracts.Replay
{
    /// <summary>
    /// Captures a deterministic state-snapshot timeline for one match and serializes it to a compact,
    /// versioned, (optionally gzip'd) byte blob. Not video — the viewer reconstructs from robot-catalog
    /// spawns driven by the recorded quantized frames.
    /// </summary>
    public interface IReplayRecorder
    {
        /// <summary>True between BeginRecording and StopAndSerialize/Reset.</summary>
        bool IsRecording { get; }

        /// <summary>The header captured at BeginRecording (roster, tickRate, scene, mode).</summary>
        ReplayHeader Header { get; }

        /// <summary>Frames appended so far (for size/telemetry and keyframe cadence).</summary>
        int FrameCount { get; }

        /// <summary>
        /// Begin a new recording. Supplies the roster, scene, mode, tick rate and keyframe interval.
        /// durationSec/finalScore in the header are placeholders finalized by StopAndSerialize.
        /// </summary>
        void BeginRecording(ReplayHeader header);

        /// <summary>
        /// Append one sampled frame. The caller marks it Keyframe or Delta (typically Keyframe every
        /// header.keyframeInterval frames). Snapshots must already be quantized per ReplayFormat.
        /// </summary>
        void RecordFrame(ReplayFrame frame);

        /// <summary>
        /// Append a discrete event (shot/score/penalty/human-player/phase/piece). Events are attached to
        /// the timeline at their timestamp; implementations may fold them into the next frame.
        /// </summary>
        void RecordEvent(ReplayEvent gameEvent);

        /// <summary>
        /// Finalize the timeline (patch durationSec/finalScore, choose encoding, compress) and return the
        /// opaque blob to hand to IReplayService.UploadAsync. Sets IsRecording false.
        /// </summary>
        byte[] StopAndSerialize(float durationSec, ReplayScore finalScore);

        /// <summary>
        /// Build the backend-facing metadata for an already-serialized blob. replayId/userId are assigned
        /// by the service/backend, so they are passed in (may be "" pre-upload). sizeBytes = blob length.
        /// </summary>
        ReplayMetadata BuildMetadata(string replayId, string userId, int sizeBytes);

        /// <summary>Discard any in-progress recording and free buffers without producing a blob.</summary>
        void Reset();
    }
}
