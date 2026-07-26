// CloSim Online Multiplayer — replay codec internals: gzip helpers.
// SOURCE OF TRUTH: Documentation/online/architecture.md §11.2 (flags.bit0 = gzip payload).
// Internal to Online.Replay.Codec. Uses only System.IO.Compression.GZipStream — no third-party
// compression dependency, per the task constraints.

using System.IO;
using System.IO.Compression;

namespace Online.Replay.Codec
{
    /// <summary>Thin wrapper around <see cref="GZipStream"/> for compressing/decompressing the
    /// replay TIMELINE payload (the blob HEADER itself is never compressed — see architecture.md
    /// §11.2, the header sits ahead of the "payload; gzip'd when flags.bit0" section).</summary>
    internal static class ReplayGzip
    {
        public static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }

        public static byte[] Decompress(byte[] data)
        {
            using var input = new MemoryStream(data, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
    }
}
