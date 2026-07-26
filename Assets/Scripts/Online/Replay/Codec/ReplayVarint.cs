// CloSim Online Multiplayer — replay codec internals: LEB128 varint helpers.
// SOURCE OF TRUTH for the wire shapes these encode: Documentation/online/architecture.md §11.2.
// Internal to Online.Replay.Codec; not part of any public contract.
//
// The blob format (architecture.md §11.2) uses "varint" for entityId and (in delta frames)
// "zig-zag varint" for position deltas. Both ReplayWriter and ReplayReader must agree on the
// exact bit layout, so it lives once here instead of being duplicated in each file.

using System;
using System.IO;

namespace Online.Replay.Codec
{
    /// <summary>
    /// Standard LEB128 unsigned varint (7 data bits per byte, MSB = continuation) plus a zig-zag
    /// wrapper for signed values. Little-endian byte order throughout, matching the rest of the
    /// blob (architecture.md §11.2 header comment: "binary, little-endian").
    /// </summary>
    internal static class ReplayVarint
    {
        /// <summary>Writes an unsigned LEB128 varint.</summary>
        public static void WriteUVarInt(BinaryWriter bw, ulong value)
        {
            while (value >= 0x80)
            {
                bw.Write((byte)(value | 0x80));
                value >>= 7;
            }
            bw.Write((byte)value);
        }

        /// <summary>Reads an unsigned LEB128 varint written by <see cref="WriteUVarInt"/>.</summary>
        public static ulong ReadUVarInt(BinaryReader br)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte b = br.ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;

                shift += 7;
                if (shift > 63)
                    throw new InvalidDataException("ReplayVarint: varint exceeds 64 bits (corrupt stream).");
            }
            return result;
        }

        /// <summary>
        /// Writes a signed value as a zig-zag encoded unsigned varint (small magnitudes — positive or
        /// negative — stay small on the wire). Used for delta-frame position deltas and for fields that
        /// can legitimately be -1 (e.g. <c>ReplayEvent.pieceId</c> when "N/A").
        /// </summary>
        public static void WriteZigZagVarInt(BinaryWriter bw, long value)
        {
            ulong zigzag = (ulong)((value << 1) ^ (value >> 63));
            WriteUVarInt(bw, zigzag);
        }

        /// <summary>Reads a value written by <see cref="WriteZigZagVarInt"/>.</summary>
        public static long ReadZigZagVarInt(BinaryReader br)
        {
            ulong zigzag = ReadUVarInt(br);
            return (long)(zigzag >> 1) ^ -(long)(zigzag & 1);
        }
    }
}
