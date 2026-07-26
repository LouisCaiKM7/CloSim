// CloSim Online Multiplayer — replay data models / DTOs (contract stubs).
// SOURCE OF TRUTH: Documentation/online/architecture.md "Replay System".
// Owned by A1 (Architect). Plain serializable data; dependency-free (no UnityEngine / no Mirror).
//
// Two audiences:
//  * ReplayMetadata is the BACKEND-FACING record. Its JSON shape mirrors the master-server style and
//    the replay backend's stored document: { replayId, userId, gameId, mode:{blue,red}, sceneName,
//    durationSec, finalScore:{blue,red}, sizeBytes, createdAt, schemaVersion }.
//  * ReplayHeader / ReplayFrame / ReplaySnapshot / ReplayEvent describe the OPAQUE BLOB the backend
//    only stores and serves — the game is the sole reader/writer.
//
// Transforms are QUANTIZED (fixed-point mm + compressed rotation) — see ReplayFormat. These structs
// carry the already-quantized integer values; the recorder quantizes, the viewer dequantizes.

using System;
using Online.Contracts; // RoomAlliance — reuse, do not fork a parallel alliance enum.

namespace Online.Contracts.Replay
{
    // =============================================================================================
    //  Backend-facing metadata (JSON). The blob is uploaded separately (presigned S3 PUT).
    // =============================================================================================

    /// <summary>blue/red count pair. Serializes as mode:{blue,red}.</summary>
    [Serializable]
    public struct ReplayMode
    {
        public int blue;
        public int red;
    }

    /// <summary>blue/red final score pair. Serializes as finalScore:{blue,red}.</summary>
    [Serializable]
    public struct ReplayScore
    {
        public int blue;
        public int red;
    }

    /// <summary>
    /// The record the replay backend stores and the in-game replay list shows. JSON-serializable and
    /// intentionally free of transform data — the heavy timeline lives in the opaque blob.
    /// replayId/userId are assigned by the service/backend; the recorder leaves them blank.
    /// </summary>
    [Serializable]
    public struct ReplayMetadata
    {
        public string replayId;     // backend-assigned; "" until UploadAsync returns it
        public string userId;       // owner; set by the service from the signed-in identity
        public string gameId;       // "Rebuilt" | "Reefscape"
        public ReplayMode mode;     // { blue, red } — matches NetworkMatchConfig counts
        public string sceneName;    // scene to reconstruct into
        public float durationSec;   // recorded match length
        public ReplayScore finalScore; // { blue, red } at match end
        public int sizeBytes;       // blob size (post-compression)
        public long createdAt;      // unix epoch milliseconds (UTC)
        public int schemaVersion;   // equals the blob header's schemaVersion (ReplayFormat.SchemaVersion)
    }

    // =============================================================================================
    //  Blob header (once per file) + roster.
    // =============================================================================================

    /// <summary>
    /// One roster slot, enough for the viewer to re-spawn the right robot from the existing catalog and
    /// label it. Mirrors the meaningful fields of RoomMemberSlot / RobotCatalogEntry.
    /// </summary>
    [Serializable]
    public struct ReplayRosterEntry
    {
        public int slotIndex;       // 0..5 — also the robot entity id in snapshots
        public int robotIndex;      // index into the game's robot catalog (network-safe key)
        public RoomAlliance alliance;
        public int teamNumber;      // FRC team number (0 if none), for display
        public string displayName;  // driver / robot label
    }

    /// <summary>
    /// Blob header: everything needed to bootstrap playback before the first frame is read.
    /// The recorder fills this at BeginRecording; durationSec/finalScore are patched in at finalize.
    /// </summary>
    [Serializable]
    public struct ReplayHeader
    {
        public int schemaVersion;   // ReplayFormat.SchemaVersion at record time
        public string gameId;       // "Rebuilt" | "Reefscape"
        public string sceneName;    // scene to load for reconstruction
        public ReplayMode mode;     // blue/red counts
        public byte tickRate;       // frames per second in the timeline
        public ushort keyframeInterval; // frames between keyframes
        public float durationSec;   // filled at finalize
        public ReplayScore finalScore;  // filled at finalize
        public long createdAt;      // unix epoch milliseconds (UTC)
        public ReplayRosterEntry[] roster; // one entry per player slot
    }

    // =============================================================================================
    //  Timeline: frames of snapshots + events.
    // =============================================================================================

    /// <summary>
    /// One entity's quantized transform + state at a single frame. On a KEYFRAME the position/rotation
    /// values are ABSOLUTE; on a DELTA frame they are signed differences from this entity's previous
    /// frame. The viewer dequantizes with ReplayFormat (mm -> m, yaw/quat -> rotation).
    /// </summary>
    [Serializable]
    public struct ReplaySnapshot
    {
        public ReplayEntityKind kind; // Robot | Piece
        public int entityId;          // robot: slotIndex (0..5); piece: stable piece id

        // Fixed-point position. Absolute (keyframe) values are millimetres; delta values are mm deltas.
        public int posX;
        public int posY;
        public int posZ;

        public ReplayRotationEncoding rotEncoding; // Yaw16 (robots) | SmallestThree32 (pieces)
        public uint rot;             // packed per rotEncoding; on deltas, yaw stores a signed step

        // Piece-only fields (ignored for robots).
        public ReplayPieceType pieceType;
        public ReplayPieceMotion motion;
    }

    /// <summary>
    /// One timeline frame: all entity snapshots for this tick plus any discrete events that fired since
    /// the previous frame. Keyframes carry the full active set; delta frames may omit unchanged entities.
    /// </summary>
    [Serializable]
    public struct ReplayFrame
    {
        public uint timestampMs;      // match-relative milliseconds
        public ReplayFrameKind kind;  // Keyframe | Delta
        public ReplaySnapshot[] snapshots;
        public ReplayEvent[] events;  // may be null/empty
    }

    /// <summary>
    /// A discrete, timestamped gameplay event that transforms alone cannot reconstruct (scores, shots,
    /// penalties, human-player actions, phase changes, piece spawn/despawn).
    /// </summary>
    [Serializable]
    public struct ReplayEvent
    {
        public uint timestampMs;      // match-relative milliseconds
        public ReplayEventType type;
        public RoomAlliance alliance; // affected/acting alliance (Unassigned if N/A)
        public int actorSlot;         // acting robot slot (-1 if N/A)
        public int pieceId;           // related tracked piece (-1 if N/A)
        public int intValue;          // event-specific payload (points / phase / human-player type)

        // Optional quantized world position of the event (mm), e.g. where a piece was scored.
        public bool hasPosition;
        public int posX;
        public int posY;
        public int posZ;
    }
}
