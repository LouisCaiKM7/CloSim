// CloSim Online Multiplayer — replay codec: binary deserializer (byte[] blob -> frames/events).
// SOURCE OF TRUTH: Documentation/online/architecture.md §11.2 "Blob layout (binary, little-endian)".
// Inverse of ReplayWriter.cs — see that file's header comment for the full byte layout. Both files
// MUST stay in lock-step; a layout change requires bumping ReplayFormat.SchemaVersion.
//
// This is the READ half of the IReplayService <-> codec <-> viewer seam: the in-game Replays screen
// calls IReplayService.GetAsync to fetch the opaque blob (architecture.md §11.6), then this reader
// reconstructs the ReplayHeader + ReplayFrame[] the viewer dequantizes and drives playback from.
// Zero networking/UnityEngine dependencies — pure binary math.

using System;
using System.IO;
using System.Text;
using Online.Contracts;
using Online.Contracts.Replay;

namespace Online.Replay.Codec
{
    /// <summary>
    /// The decoded contents of a replay blob: the header (roster/scene/mode/tick-rate) plus the full
    /// timeline of frames. This is a codec-local convenience wrapper — <see cref="Header"/> and
    /// <see cref="Frames"/> are exactly the existing <c>Online.Contracts.Replay</c> DTOs, nothing new.
    /// </summary>
    public readonly struct ReplayDocument
    {
        public ReplayHeader Header { get; }
        public ReplayFrame[] Frames { get; }

        public ReplayDocument(ReplayHeader header, ReplayFrame[] frames)
        {
            Header = header;
            Frames = frames ?? Array.Empty<ReplayFrame>();
        }
    }

    /// <summary>
    /// Deserializes a replay blob (as produced by <see cref="ReplayWriter.Write"/> and returned by
    /// <c>IReplayService.GetAsync</c>) back into a <see cref="ReplayHeader"/> + <see cref="ReplayFrame"/>
    /// timeline. Quantized transforms round-trip losslessly at the integer level written by the writer
    /// (the writer is where any real-world float -&gt; fixed-point precision loss happens, by design —
    /// see ReplayFormat's quantization comments); this reader does not introduce additional loss.
    /// </summary>
    public static class ReplayReader
    {
        /// <summary>
        /// Decodes a full blob into header + frames. Returns an empty document (default header, zero
        /// frames) for a null/empty blob rather than throwing, so a missing/not-yet-uploaded replay
        /// degrades gracefully like the rest of the replay stack (architecture.md §11.4/§11.5).
        /// </summary>
        public static ReplayDocument Read(byte[] blob)
        {
            if (blob == null || blob.Length == 0)
                return new ReplayDocument(default, Array.Empty<ReplayFrame>());

            using var input = new MemoryStream(blob, writable: false);
            using var br = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);

            ReplayHeader header = ReadHeader(br, out byte flags);

            if ((flags & ReplayFormat.FlagJsonFallback) != 0)
            {
                // ReplayWriter never emits this flag today (architecture.md §11.2's JSON+gzip
                // fallback is out of scope for this codec pass). Fail loudly rather than silently
                // mis-decoding a payload this reader cannot understand.
                throw new NotSupportedException(
                    "ReplayReader: blob uses the JSON-fallback payload (flags.bit1), which this " +
                    "binary codec does not implement.");
            }

            byte[] remaining = ReadToEnd(input);
            byte[] timelineBytes = (flags & ReplayFormat.FlagGzip) != 0
                ? ReplayGzip.Decompress(remaining)
                : remaining;

            ReplayFrame[] frames = ReadTimeline(timelineBytes);
            return new ReplayDocument(header, frames);
        }

        /// <summary>
        /// Decodes only the header (roster/scene/mode). Cheaper than <see cref="Read"/> when a caller
        /// only needs to bootstrap playback (scene name, roster) without the full timeline yet — the
        /// header is never gzip-compressed (architecture.md §11.2), so this never touches the payload.
        /// </summary>
        public static ReplayHeader ReadHeaderOnly(byte[] blob)
        {
            if (blob == null || blob.Length == 0)
                return default;

            using var input = new MemoryStream(blob, writable: false);
            using var br = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            return ReadHeader(br, out _);
        }

        // ---- HEADER --------------------------------------------------------------------------

        private static ReplayHeader ReadHeader(BinaryReader br, out byte flags)
        {
            uint magic = br.ReadUInt32();
            if (magic != ReplayFormat.Magic)
            {
                throw new InvalidDataException(
                    $"ReplayReader: bad magic 0x{magic:X8} (expected 0x{ReplayFormat.Magic:X8}) — " +
                    "not a CloSim replay blob, or the blob is corrupt.");
            }

            ushort schemaVersion = br.ReadUInt16();
            flags = br.ReadByte();

            string gameId = br.ReadString();
            string sceneName = br.ReadString();

            byte modeBlue = br.ReadByte();
            byte modeRed = br.ReadByte();

            byte tickRate = br.ReadByte();
            ushort keyframeInterval = br.ReadUInt16();
            float durationSec = br.ReadSingle();

            int scoreBlue = br.ReadInt32();
            int scoreRed = br.ReadInt32();

            long createdAt = br.ReadInt64();

            byte rosterCount = br.ReadByte();
            var roster = new ReplayRosterEntry[rosterCount];
            for (int i = 0; i < rosterCount; i++)
            {
                roster[i] = new ReplayRosterEntry
                {
                    slotIndex = br.ReadByte(),
                    robotIndex = br.ReadInt32(),
                    alliance = (RoomAlliance)br.ReadByte(),
                    teamNumber = br.ReadInt32(),
                    displayName = br.ReadString(),
                };
            }

            return new ReplayHeader
            {
                schemaVersion = schemaVersion,
                gameId = gameId,
                sceneName = sceneName,
                mode = new ReplayMode { blue = modeBlue, red = modeRed },
                tickRate = tickRate,
                keyframeInterval = keyframeInterval,
                durationSec = durationSec,
                finalScore = new ReplayScore { blue = scoreBlue, red = scoreRed },
                createdAt = createdAt,
                roster = roster,
            };
        }

        // ---- TIMELINE --------------------------------------------------------------------------

        private static ReplayFrame[] ReadTimeline(byte[] timelineBytes)
        {
            using var ms = new MemoryStream(timelineBytes, writable: false);
            using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            uint frameCount = br.ReadUInt32();
            var frames = new ReplayFrame[frameCount];
            for (uint i = 0; i < frameCount; i++)
                frames[i] = ReadFrame(br);

            return frames;
        }

        private static ReplayFrame ReadFrame(BinaryReader br)
        {
            uint timestampMs = br.ReadUInt32();
            var kind = (ReplayFrameKind)br.ReadByte();
            ushort snapCount = br.ReadUInt16();

            bool isKeyframe = kind == ReplayFrameKind.Keyframe;
            var snapshots = new ReplaySnapshot[snapCount];
            for (int i = 0; i < snapCount; i++)
                snapshots[i] = ReadSnapshot(br, isKeyframe);

            byte evtCount = br.ReadByte();
            var events = new ReplayEvent[evtCount];
            for (int i = 0; i < evtCount; i++)
                events[i] = ReadEvent(br);

            return new ReplayFrame
            {
                timestampMs = timestampMs,
                kind = kind,
                snapshots = snapshots,
                events = events,
            };
        }

        private static ReplaySnapshot ReadSnapshot(BinaryReader br, bool isKeyframe)
        {
            var kind = (ReplayEntityKind)br.ReadByte();
            long entityId = (long)ReplayVarint.ReadUVarInt(br);

            int posX, posY, posZ;
            if (isKeyframe)
            {
                posX = br.ReadInt16();
                posY = br.ReadInt16();
                posZ = br.ReadInt16();
            }
            else
            {
                posX = (int)ReplayVarint.ReadZigZagVarInt(br);
                posY = (int)ReplayVarint.ReadZigZagVarInt(br);
                posZ = (int)ReplayVarint.ReadZigZagVarInt(br);
            }

            var rotEncoding = (ReplayRotationEncoding)br.ReadByte();
            uint rot = rotEncoding == ReplayRotationEncoding.SmallestThree32
                ? br.ReadUInt32()
                : br.ReadUInt16();

            var snapshot = new ReplaySnapshot
            {
                kind = kind,
                entityId = (int)entityId,
                posX = posX,
                posY = posY,
                posZ = posZ,
                rotEncoding = rotEncoding,
                rot = rot,
            };

            if (kind == ReplayEntityKind.Piece)
            {
                snapshot.pieceType = (ReplayPieceType)br.ReadByte();
                snapshot.motion = (ReplayPieceMotion)br.ReadByte();
            }

            return snapshot;
        }

        private static ReplayEvent ReadEvent(BinaryReader br)
        {
            uint timestampMs = br.ReadUInt32();
            var type = (ReplayEventType)br.ReadByte();
            var alliance = (RoomAlliance)br.ReadByte();
            int actorSlot = br.ReadSByte();
            int pieceId = (int)ReplayVarint.ReadZigZagVarInt(br);
            int intValue = br.ReadInt32();
            bool hasPosition = br.ReadByte() != 0;

            int posX = 0, posY = 0, posZ = 0;
            if (hasPosition)
            {
                posX = br.ReadInt16();
                posY = br.ReadInt16();
                posZ = br.ReadInt16();
            }

            return new ReplayEvent
            {
                timestampMs = timestampMs,
                type = type,
                alliance = alliance,
                actorSlot = actorSlot,
                pieceId = pieceId,
                intValue = intValue,
                hasPosition = hasPosition,
                posX = posX,
                posY = posY,
                posZ = posZ,
            };
        }

        // ---- helpers ---------------------------------------------------------------------------

        private static byte[] ReadToEnd(Stream input)
        {
            using var ms = new MemoryStream();
            input.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
