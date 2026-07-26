// CloSim Online Multiplayer — replay format enums (contract stubs).
// SOURCE OF TRUTH: Documentation/online/architecture.md "Replay System".
// Owned by A1 (Architect). Consumed by the recorder (A4-adjacent), uploader, and viewer.
// Dependency-free (no UnityEngine / no Mirror) so the format stays portable and testable.
//
// The replay blob is a DETERMINISTIC STATE SNAPSHOT stream — NOT video. A viewer replays it by
// re-spawning robots from the existing robot catalog (by robotIndex) and driving their transforms
// from the recorded quantized frames. Nothing in this format references prefabs directly.

namespace Online.Contracts.Replay
{
    /// <summary>
    /// Which kind of entity a <see cref="ReplaySnapshot"/> describes. The viewer routes a snapshot
    /// to a robot (spawned from the catalog by roster.robotIndex) or to a tracked game piece.
    /// </summary>
    public enum ReplayEntityKind : byte
    {
        Robot = 0,
        Piece = 1
    }

    /// <summary>
    /// A frame is either a KEYFRAME (all values absolute — a resync/seek point) or a DELTA
    /// (values are signed differences from the previous frame). Delta frames dominate the timeline;
    /// keyframes are emitted every <see cref="ReplayFormat.DefaultKeyframeInterval"/> frames.
    /// </summary>
    public enum ReplayFrameKind : byte
    {
        Delta = 0,
        Keyframe = 1
    }

    /// <summary>
    /// Rotation encoding used by a snapshot. Planar drivetrains only need yaw; tumbling game pieces
    /// need a full orientation, stored as a "smallest-three" compressed quaternion.
    /// </summary>
    public enum ReplayRotationEncoding : byte
    {
        /// <summary>Single yaw about world-up, quantized to a ushort (0..65535 maps 0..360°).</summary>
        Yaw16 = 0,

        /// <summary>Smallest-three compressed quaternion packed into a uint (2-bit index + 3×10-bit).</summary>
        SmallestThree32 = 1
    }

    /// <summary>
    /// Discrete gameplay events on the timeline, stamped with a match-relative millisecond time.
    /// These reconstruct scoring pops, penalties, human-player actions, and phase transitions that
    /// are not derivable from transforms alone.
    /// </summary>
    public enum ReplayEventType : byte
    {
        MatchStart = 0,
        PhaseChange = 1,     // intValue = ReplayMatchPhase (Auto/Teleop/Endgame/Finished)
        Shot = 2,            // actorSlot fired a piece; pieceId set if tracked
        Score = 3,           // intValue = points delta; alliance set; pieceId optional
        Penalty = 4,         // intValue = penalty points; alliance = penalized alliance
        HumanPlayerAction = 5, // intValue = HumanPlayerType; alliance set
        PieceSpawn = 6,      // a tracked piece entered the world; pieceId + pieceType set
        PieceDespawn = 7,    // a tracked piece left play (scored/removed); pieceId set
        MatchEnd = 8
    }

    /// <summary>Match phase carried by a PhaseChange event. Mirrors Field.Core.MatchState ordering.</summary>
    public enum ReplayMatchPhase : byte
    {
        Auto = 0,
        Teleop = 1,
        Endgame = 2,
        Finished = 3
    }

    /// <summary>Game piece kind carried by piece snapshots/events. Mirrors Core.PieceNames ordering.</summary>
    public enum ReplayPieceType : byte
    {
        Coral = 0,
        Algae = 1,
        Fuel = 2
    }

    /// <summary>Piece motion state carried by piece snapshots. Mirrors Core.GamePieceState ordering.</summary>
    public enum ReplayPieceMotion : byte
    {
        World = 0,
        Stationary = 1,
        Moving = 2
    }
}
