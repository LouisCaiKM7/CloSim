// CloSim Online Multiplayer — replay codec: binary serializer (frames/events -> byte[] blob).
// SOURCE OF TRUTH: Documentation/online/architecture.md §11.2 "Blob layout (binary, little-endian)".
// Codes against Online.Contracts.Replay (ReplayFormat/ReplayModels/ReplayEnums) — no parallel types.
//
// This is the WRITE half of the recorder <-> codec <-> IReplayService seam:
//   IReplayRecorder (host, A4-adjacent) accumulates ReplayHeader + ReplayFrame[] as a match plays,
//   then calls ReplayWriter.Write(...) to produce the opaque byte[] blob that StopAndSerialize
//   hands to IReplayService.UploadAsync. This file has ZERO networking/UnityEngine dependencies —
//   pure binary math, matching Online.Replay.Contracts' noEngineReferences discipline.
//
// Blob shape (see architecture.md §11.2 for the authoritative byte-by-byte table):
//   HEADER   (uncompressed): magic, schemaVersion, flags, gameId, sceneName, mode, tickRate,
//             keyframeInterval, durationSec, finalScore, createdAt, roster[]
//   TIMELINE (gzip'd when flags.bit0 is set): frameCount, then FRAME x frameCount
//     FRAME: timestampMs, kind (Delta/Keyframe), snapCount, SNAPSHOT x snapCount, evtCount, EVENT x evtCount
//     SNAPSHOT: kind, entityId (uvarint), pos (i16 abs on Keyframe / zig-zag varint delta on Delta),
//               rotEncoding, rot (u16 for Yaw16, u32 for SmallestThree32), [piece only] pieceType, motion
//     EVENT: timestampMs, type, alliance, actorSlot (i8), pieceId (zig-zag varint), intValue,
//            hasPosition, [pos i16 mm if hasPosition]
//
// All multi-byte integers/floats are written with System.IO.BinaryWriter, which is little-endian on
// every .NET runtime CloSim targets — matching the doc's "binary, little-endian" header.
//
// NOTE on the JSON+gzip fallback (flags.bit1, architecture.md §11.2): this writer only ever produces
// the packed binary timeline (flags.bit1 never set). The fallback exists for debugging/interop and is
// out of scope for this codec pass — see ReplayReader's matching note.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Online.Contracts.Replay;

namespace Online.Replay.Codec
{
    /// <summary>
    /// Serializes a <see cref="ReplayHeader"/> + a sequence of <see cref="ReplayFrame"/>s into the
    /// compact binary replay format (architecture.md §11.2), optionally gzip-compressing the
    /// timeline payload. The result is an opaque <c>byte[]</c> blob ready for
    /// <c>IReplayService.UploadAsync</c> / <c>ReplayServiceClient.UploadAsync</c>.
    /// </summary>
    public static class ReplayWriter
    {
        /// <summary>
        /// Encodes <paramref name="header"/> + <paramref name="frames"/> into the versioned binary
        /// blob. The header section is always written uncompressed; the timeline section (frame
        /// count + frames) is gzip-compressed when <paramref name="gzip"/> is true (the default —
        /// matches <see cref="ReplayFormat.FlagGzip"/> being the expected production encoding).
        /// </summary>
        /// <param name="header">Roster/scene/mode/tick-rate header, as built by the recorder.</param>
        /// <param name="frames">Timeline frames in recording order (may be empty, not null-checked
        /// strictly — a null list is treated as empty).</param>
        /// <param name="gzip">Whether to gzip the timeline payload (default true).</param>
        public static byte[] Write(ReplayHeader header, IReadOnlyList<ReplayFrame> frames, bool gzip = true)
        {
            frames ??= Array.Empty<ReplayFrame>();

            byte flags = 0;
            if (gzip)
                flags |= ReplayFormat.FlagGzip;

            using var output = new MemoryStream();

            using (var headerWriter = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(headerWriter, header, flags);
            }

            byte[] timelineBytes = WriteTimeline(frames);
            byte[] payload = gzip ? ReplayGzip.Compress(timelineBytes) : timelineBytes;
            output.Write(payload, 0, payload.Length);

            return output.ToArray();
        }

        // ---- HEADER --------------------------------------------------------------------------

        private static void WriteHeader(BinaryWriter bw, ReplayHeader header, byte flags)
        {
            bw.Write(ReplayFormat.Magic);
            bw.Write((ushort)header.schemaVersion);
            bw.Write(flags);

            bw.Write(header.gameId ?? string.Empty);
            bw.Write(header.sceneName ?? string.Empty);

            bw.Write((byte)header.mode.blue);
            bw.Write((byte)header.mode.red);

            bw.Write(header.tickRate);
            bw.Write(header.keyframeInterval);
            bw.Write(header.durationSec);

            bw.Write(header.finalScore.blue);
            bw.Write(header.finalScore.red);

            bw.Write(header.createdAt);

            ReplayRosterEntry[] roster = header.roster ?? Array.Empty<ReplayRosterEntry>();
            int rosterCount = Math.Min(roster.Length, byte.MaxValue);
            bw.Write((byte)rosterCount);
            for (int i = 0; i < rosterCount; i++)
            {
                ReplayRosterEntry entry = roster[i];
                bw.Write((byte)entry.slotIndex);
                bw.Write(entry.robotIndex);
                bw.Write((byte)entry.alliance);
                bw.Write(entry.teamNumber);
                bw.Write(entry.displayName ?? string.Empty);
            }
        }

        // ---- TIMELINE --------------------------------------------------------------------------

        private static byte[] WriteTimeline(IReadOnlyList<ReplayFrame> frames)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write((uint)frames.Count);
                for (int i = 0; i < frames.Count; i++)
                    WriteFrame(bw, frames[i]);
            }
            return ms.ToArray();
        }

        private static void WriteFrame(BinaryWriter bw, ReplayFrame frame)
        {
            bw.Write(frame.timestampMs);
            bw.Write((byte)frame.kind);

            ReplaySnapshot[] snapshots = frame.snapshots ?? Array.Empty<ReplaySnapshot>();
            int snapCount = Math.Min(snapshots.Length, ushort.MaxValue);
            bw.Write((ushort)snapCount);

            bool isKeyframe = frame.kind == ReplayFrameKind.Keyframe;
            for (int i = 0; i < snapCount; i++)
                WriteSnapshot(bw, snapshots[i], isKeyframe);

            ReplayEvent[] events = frame.events ?? Array.Empty<ReplayEvent>();
            int evtCount = Math.Min(events.Length, byte.MaxValue);
            bw.Write((byte)evtCount);
            for (int i = 0; i < evtCount; i++)
                WriteEvent(bw, events[i]);
        }

        private static void WriteSnapshot(BinaryWriter bw, ReplaySnapshot snap, bool isKeyframe)
        {
            bw.Write((byte)snap.kind);

            if (snap.entityId < 0)
                throw new ArgumentOutOfRangeException(nameof(snap.entityId),
                    "ReplayWriter: entityId must be >= 0 (robot slotIndex or a stable piece id).");
            ReplayVarint.WriteUVarInt(bw, (ulong)snap.entityId);

            if (isKeyframe)
            {
                // Keyframe: absolute millimetres, must fit i16 per ReplayFormat's position budget.
                bw.Write(ClampToInt16(snap.posX));
                bw.Write(ClampToInt16(snap.posY));
                bw.Write(ClampToInt16(snap.posZ));
            }
            else
            {
                // Delta: signed mm difference from this entity's previous frame, zig-zag varint.
                ReplayVarint.WriteZigZagVarInt(bw, snap.posX);
                ReplayVarint.WriteZigZagVarInt(bw, snap.posY);
                ReplayVarint.WriteZigZagVarInt(bw, snap.posZ);
            }

            bw.Write((byte)snap.rotEncoding);
            if (snap.rotEncoding == ReplayRotationEncoding.SmallestThree32)
            {
                bw.Write(snap.rot); // full u32 (2-bit index + 3x10-bit signed components)
            }
            else
            {
                // Yaw16: low 16 bits of `rot` carry either the absolute angle (keyframe) or the
                // signed delta step (delta) — ReplayModels.cs documents both as living in this
                // field; the wire width is always 2 bytes regardless of frame kind.
                bw.Write(unchecked((ushort)snap.rot));
            }

            if (snap.kind == ReplayEntityKind.Piece)
            {
                bw.Write((byte)snap.pieceType);
                bw.Write((byte)snap.motion);
            }
        }

        private static void WriteEvent(BinaryWriter bw, ReplayEvent evt)
        {
            bw.Write(evt.timestampMs);
            bw.Write((byte)evt.type);
            bw.Write((byte)evt.alliance);
            bw.Write((sbyte)Clamp(evt.actorSlot, sbyte.MinValue, sbyte.MaxValue));
            ReplayVarint.WriteZigZagVarInt(bw, evt.pieceId); // may legitimately be -1 (N/A)
            bw.Write(evt.intValue);
            bw.Write((byte)(evt.hasPosition ? 1 : 0));

            if (evt.hasPosition)
            {
                bw.Write(ClampToInt16(evt.posX));
                bw.Write(ClampToInt16(evt.posY));
                bw.Write(ClampToInt16(evt.posZ));
            }
        }

        // ---- helpers ---------------------------------------------------------------------------

        private static short ClampToInt16(int mm) => (short)Clamp(mm, short.MinValue, short.MaxValue);

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
