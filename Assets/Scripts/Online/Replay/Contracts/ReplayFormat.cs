// CloSim Online Multiplayer — replay wire-format constants & quantization contract.
// SOURCE OF TRUTH: Documentation/online/architecture.md "Replay System".
// Owned by A1 (Architect). The recorder (serialize) and viewer (deserialize) MUST agree on every
// value here. Bumping the binary layout REQUIRES bumping SchemaVersion.
//
// The blob is OPAQUE to the backend (it stores/serves bytes via presigned S3). Only CloSim.exe reads it.

namespace Online.Contracts.Replay
{
    /// <summary>
    /// Binary format constants shared by the recorder and the viewer. See the architecture doc for the
    /// full byte layout; this class is the machine-readable half of that spec.
    /// </summary>
    public static class ReplayFormat
    {
        /// <summary>ASCII "CLSR" (CloSim Replay). First 4 bytes of every binary blob.</summary>
        public const uint Magic = 0x43_4C_53_52; // 'C''L''S''R'

        /// <summary>
        /// Current schema version. Bump on ANY layout/quantization change so old viewers reject new blobs
        /// (and the backend metadata's schemaVersion matches the blob header's schemaVersion).
        /// </summary>
        // v2 adds the OPTIONAL per-robot articulation (moving-joint) channel — see FlagJoints below and
        // ReplayFrame.jointSets. Old (v1) blobs stay readable: they simply carry no joint channel.
        public const ushort SchemaVersion = 2;

        // ---- Blob flags (header.flags byte) -----------------------------------------------------
        /// <summary>Payload after the header is gzip-compressed.</summary>
        public const byte FlagGzip = 1 << 0;

        /// <summary>Payload is the JSON fallback encoding (UTF-8) instead of the packed binary timeline.</summary>
        public const byte FlagJsonFallback = 1 << 1;

        /// <summary>
        /// The timeline carries the per-frame articulation channel (ReplayFrame.jointSets): each robot's
        /// moving mechanism children as absolute local poses. Absent (older replays) => robots animate by
        /// root transform only, exactly as before.
        /// </summary>
        public const byte FlagJoints = 1 << 2;

        // ---- Timeline rate ----------------------------------------------------------------------
        /// <summary>Default snapshot rate. 15 Hz keeps a 2.5-min match small while staying smooth on playback.</summary>
        public const byte DefaultTickRate = 15;

        /// <summary>Minimum / maximum sane tick rates the viewer will accept.</summary>
        public const byte MinTickRate = 5;
        public const byte MaxTickRate = 30;

        /// <summary>
        /// Frames between keyframes. At 15 Hz, 30 frames = one keyframe every 2 s — cheap seeking and
        /// resync without bloating the timeline.
        /// </summary>
        public const ushort DefaultKeyframeInterval = 30;

        // ---- Position quantization (fixed-point) ------------------------------------------------
        // Positions are stored as fixed-point millimetres in a short (int16): +/-32.767 m about origin.
        // A full FRC field (~16.5 m x 8.2 m) sits comfortably inside this range with headroom for
        // airborne pieces. Absolute values live in keyframes; delta frames store signed mm deltas.
        /// <summary>Fixed-point units per metre. 1000 => 1 mm resolution.</summary>
        public const int PositionUnitsPerMeter = 1000;

        /// <summary>Absolute keyframe positions fit in int16 (mm). Deltas usually fit in int8, else escape to int16.</summary>
        public const int PositionAbsoluteMinMillimeters = -32768;
        public const int PositionAbsoluteMaxMillimeters = 32767;

        // ---- Rotation quantization --------------------------------------------------------------
        /// <summary>Yaw is quantized to a ushort: value = round(yawDeg / 360 * 65536). ~0.0055° resolution.</summary>
        public const int YawQuantSteps = 65536;

        /// <summary>Smallest-three quaternion: 2-bit largest-component index + 3 x 10-bit signed components.</summary>
        public const int QuatComponentBits = 10;

        /// <summary>1 / sqrt(2), the max magnitude of the three transmitted quaternion components.</summary>
        public const float QuatSmallestThreeRange = 0.7071067811865476f;

        // ---- Size budget (informative) ----------------------------------------------------------
        // 2.5-min match @ 15 Hz = ~2250 frames. ~6 robots (~5 B/frame delta) + ~30 moving pieces
        // (~6 B/frame delta) => ~210 B/frame raw. Keyframes every 2 s add ~30 KB. Raw ~0.5 MB;
        // gzip on quantized deltas lands ~150-350 KB. Well inside the "few hundred KB" target.
        /// <summary>Soft cap the recorder warns past; hard-rejected uploads are the service's call.</summary>
        public const int SoftMaxBlobBytes = 8 * 1024 * 1024; // 8 MB
    }
}
