// CloSim Online Multiplayer — replay codec EditMode tests.
// Verifies Online.Replay.Codec.ReplayWriter/ReplayReader round-trip the
// Online.Contracts.Replay DTOs per the binary layout in
// Documentation/online/architecture.md §11.2, and that gzip actually shrinks a
// repetitive synthetic timeline. Style mirrors Assets/Tests/EditMode/ContractsSmokeTests.cs.

using System;
using System.IO;
using NUnit.Framework;
using Online.Contracts;
using Online.Contracts.Replay;
using Online.Replay.Codec;

namespace CloSim.Tests.EditMode
{
    [TestFixture]
    public class ReplayCodecTests
    {
        private static ReplayHeader MakeHeader(int rosterSize = 2)
        {
            var roster = new ReplayRosterEntry[rosterSize];
            for (int i = 0; i < rosterSize; i++)
            {
                roster[i] = new ReplayRosterEntry
                {
                    slotIndex = i,
                    robotIndex = i + 10,
                    alliance = (i % 2 == 0) ? RoomAlliance.Blue : RoomAlliance.Red,
                    teamNumber = 1000 + i,
                    displayName = $"driver{i}",
                };
            }

            return new ReplayHeader
            {
                schemaVersion = ReplayFormat.SchemaVersion,
                gameId = "Reefscape",
                sceneName = "ReefscapeField",
                mode = new ReplayMode { blue = 3, red = 3 },
                tickRate = ReplayFormat.DefaultTickRate,
                keyframeInterval = ReplayFormat.DefaultKeyframeInterval,
                durationSec = 150f,
                finalScore = new ReplayScore { blue = 12, red = 7 },
                createdAt = 1_700_000_000_000L,
                roster = roster,
            };
        }

        // ---- Empty input ------------------------------------------------------------------

        [Test]
        public void RoundTrip_EmptyFrames_ProducesZeroFrameDocumentWithMatchingHeader()
        {
            ReplayHeader header = MakeHeader();

            byte[] blob = ReplayWriter.Write(header, Array.Empty<ReplayFrame>());
            Assert.IsNotNull(blob);
            Assert.Greater(blob.Length, 0, "Even an empty timeline still carries a non-empty header.");

            ReplayDocument doc = ReplayReader.Read(blob);

            Assert.AreEqual(0, doc.Frames.Length);
            Assert.AreEqual(header.gameId, doc.Header.gameId);
            Assert.AreEqual(header.sceneName, doc.Header.sceneName);
            Assert.AreEqual(header.mode.blue, doc.Header.mode.blue);
            Assert.AreEqual(header.mode.red, doc.Header.mode.red);
            Assert.AreEqual(header.roster.Length, doc.Header.roster.Length);
        }

        [Test]
        public void Read_NullOrEmptyBlob_ReturnsEmptyDocument_DoesNotThrow()
        {
            ReplayDocument fromNull = ReplayReader.Read(null);
            Assert.AreEqual(0, fromNull.Frames.Length);

            ReplayDocument fromEmpty = ReplayReader.Read(Array.Empty<byte>());
            Assert.AreEqual(0, fromEmpty.Frames.Length);
        }

        // ---- Small multi-frame synthetic replay --------------------------------------------

        [Test]
        public void RoundTrip_MultiFrameSyntheticReplay_MatchesOriginalExactly()
        {
            ReplayHeader header = MakeHeader();

            var keyframe = new ReplayFrame
            {
                timestampMs = 0,
                kind = ReplayFrameKind.Keyframe,
                snapshots = new[]
                {
                    new ReplaySnapshot
                    {
                        kind = ReplayEntityKind.Robot,
                        entityId = 0,
                        posX = 12345,
                        posY = -8000,
                        posZ = 32767, // max i16 — exercises the upper edge of the fixed-point range
                        rotEncoding = ReplayRotationEncoding.Yaw16,
                        rot = 40000, // absolute yaw quant step
                    },
                    new ReplaySnapshot
                    {
                        kind = ReplayEntityKind.Piece,
                        entityId = 7,
                        posX = -32768, // min i16
                        posY = 0,
                        posZ = 1500,
                        rotEncoding = ReplayRotationEncoding.SmallestThree32,
                        rot = 0xABCD1234,
                        pieceType = ReplayPieceType.Coral,
                        motion = ReplayPieceMotion.Moving,
                    },
                },
                events = new[]
                {
                    new ReplayEvent
                    {
                        timestampMs = 0,
                        type = ReplayEventType.MatchStart,
                        alliance = RoomAlliance.Unassigned,
                        actorSlot = -1,
                        pieceId = -1,
                        intValue = 0,
                        hasPosition = false,
                    },
                },
            };

            var delta = new ReplayFrame
            {
                timestampMs = 67, // ~1 tick at 15 Hz
                kind = ReplayFrameKind.Delta,
                snapshots = new[]
                {
                    new ReplaySnapshot
                    {
                        kind = ReplayEntityKind.Robot,
                        entityId = 0,
                        posX = -42,   // negative delta — exercises zig-zag varint
                        posY = 5,
                        posZ = 0,
                        rotEncoding = ReplayRotationEncoding.Yaw16,
                        rot = unchecked((ushort)(-100)), // signed step, per ReplayModels.cs
                    },
                },
                events = new[]
                {
                    new ReplayEvent
                    {
                        timestampMs = 67,
                        type = ReplayEventType.Score,
                        alliance = RoomAlliance.Blue,
                        actorSlot = 2,
                        pieceId = 7,
                        intValue = 4,
                        hasPosition = true,
                        posX = 100,
                        posY = -200,
                        posZ = 300,
                    },
                },
            };

            var frames = new[] { keyframe, delta };

            byte[] blob = ReplayWriter.Write(header, frames);
            ReplayDocument doc = ReplayReader.Read(blob);

            Assert.AreEqual(2, doc.Frames.Length);

            // Header round-trip.
            Assert.AreEqual(header.schemaVersion, doc.Header.schemaVersion);
            Assert.AreEqual(header.tickRate, doc.Header.tickRate);
            Assert.AreEqual(header.keyframeInterval, doc.Header.keyframeInterval);
            Assert.AreEqual(header.durationSec, doc.Header.durationSec, 0.0001f);
            Assert.AreEqual(header.finalScore.blue, doc.Header.finalScore.blue);
            Assert.AreEqual(header.finalScore.red, doc.Header.finalScore.red);
            Assert.AreEqual(header.createdAt, doc.Header.createdAt);
            for (int i = 0; i < header.roster.Length; i++)
            {
                Assert.AreEqual(header.roster[i].slotIndex, doc.Header.roster[i].slotIndex);
                Assert.AreEqual(header.roster[i].robotIndex, doc.Header.roster[i].robotIndex);
                Assert.AreEqual(header.roster[i].alliance, doc.Header.roster[i].alliance);
                Assert.AreEqual(header.roster[i].teamNumber, doc.Header.roster[i].teamNumber);
                Assert.AreEqual(header.roster[i].displayName, doc.Header.roster[i].displayName);
            }

            // Keyframe round-trip.
            ReplayFrame kf = doc.Frames[0];
            Assert.AreEqual(keyframe.timestampMs, kf.timestampMs);
            Assert.AreEqual(ReplayFrameKind.Keyframe, kf.kind);
            Assert.AreEqual(2, kf.snapshots.Length);
            AssertSnapshotEqual(keyframe.snapshots[0], kf.snapshots[0]);
            AssertSnapshotEqual(keyframe.snapshots[1], kf.snapshots[1]);
            Assert.AreEqual(1, kf.events.Length);
            AssertEventEqual(keyframe.events[0], kf.events[0]);

            // Delta round-trip (including negative zig-zag deltas and the signed yaw step).
            ReplayFrame df = doc.Frames[1];
            Assert.AreEqual(delta.timestampMs, df.timestampMs);
            Assert.AreEqual(ReplayFrameKind.Delta, df.kind);
            Assert.AreEqual(1, df.snapshots.Length);
            AssertSnapshotEqual(delta.snapshots[0], df.snapshots[0]);
            Assert.AreEqual(1, df.events.Length);
            AssertEventEqual(delta.events[0], df.events[0]);
        }

        [Test]
        public void RoundTrip_WithoutGzip_AlsoMatches()
        {
            // The header's flags byte drives decoding — verify the non-gzip path independently
            // of the default-gzip path exercised by the other round-trip tests.
            ReplayHeader header = MakeHeader(rosterSize: 1);
            var frames = new[]
            {
                new ReplayFrame
                {
                    timestampMs = 33,
                    kind = ReplayFrameKind.Keyframe,
                    snapshots = new[]
                    {
                        new ReplaySnapshot
                        {
                            kind = ReplayEntityKind.Robot,
                            entityId = 0,
                            posX = 1,
                            posY = 2,
                            posZ = 3,
                            rotEncoding = ReplayRotationEncoding.Yaw16,
                            rot = 12345,
                        },
                    },
                    events = Array.Empty<ReplayEvent>(),
                },
            };

            byte[] blob = ReplayWriter.Write(header, frames, gzip: false);
            ReplayDocument doc = ReplayReader.Read(blob);

            Assert.AreEqual(1, doc.Frames.Length);
            AssertSnapshotEqual(frames[0].snapshots[0], doc.Frames[0].snapshots[0]);
        }

        [Test]
        public void ReadHeaderOnly_MatchesFullReadHeader_WithoutDecodingTimeline()
        {
            ReplayHeader header = MakeHeader();
            var frames = new[]
            {
                new ReplayFrame
                {
                    timestampMs = 0,
                    kind = ReplayFrameKind.Keyframe,
                    snapshots = Array.Empty<ReplaySnapshot>(),
                    events = Array.Empty<ReplayEvent>(),
                },
            };

            byte[] blob = ReplayWriter.Write(header, frames);

            ReplayHeader headerOnly = ReplayReader.ReadHeaderOnly(blob);
            ReplayDocument full = ReplayReader.Read(blob);

            Assert.AreEqual(full.Header.gameId, headerOnly.gameId);
            Assert.AreEqual(full.Header.sceneName, headerOnly.sceneName);
            Assert.AreEqual(full.Header.roster.Length, headerOnly.roster.Length);
        }

        // ---- Gzip actually shrinks a repetitive payload --------------------------------------

        [Test]
        public void Write_Gzip_ShrinksRepetitiveSyntheticPayload()
        {
            ReplayHeader header = MakeHeader();

            // 200 near-identical keyframes (repetitive, highly compressible) — simulates a robot
            // sitting still, which is common in a real match (auto start, endgame climb hold, etc).
            var frames = new ReplayFrame[200];
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = new ReplayFrame
                {
                    timestampMs = (uint)(i * 67),
                    kind = ReplayFrameKind.Keyframe,
                    snapshots = new[]
                    {
                        new ReplaySnapshot
                        {
                            kind = ReplayEntityKind.Robot,
                            entityId = 0,
                            posX = 1000,
                            posY = 2000,
                            posZ = 0,
                            rotEncoding = ReplayRotationEncoding.Yaw16,
                            rot = 100,
                        },
                    },
                    events = Array.Empty<ReplayEvent>(),
                };
            }

            byte[] compressed = ReplayWriter.Write(header, frames, gzip: true);
            byte[] uncompressed = ReplayWriter.Write(header, frames, gzip: false);

            Assert.Less(compressed.Length, uncompressed.Length,
                "gzip'd repetitive timeline must be smaller than the raw packed timeline.");
            Assert.Less(compressed.Length, uncompressed.Length / 2,
                "highly repetitive frames should compress by more than 2x.");

            // And it must still round-trip correctly after compression.
            ReplayDocument doc = ReplayReader.Read(compressed);
            Assert.AreEqual(frames.Length, doc.Frames.Length);
            int lastIndex = frames.Length - 1;
            AssertSnapshotEqual(frames[0].snapshots[0], doc.Frames[0].snapshots[0]);
            AssertSnapshotEqual(frames[lastIndex].snapshots[0], doc.Frames[lastIndex].snapshots[0]);
        }

        [Test]
        public void Read_BadMagic_ThrowsInvalidDataException()
        {
            byte[] garbage = { 1, 2, 3, 4, 5, 6, 7, 8 };
            Assert.Throws<InvalidDataException>(() => ReplayReader.Read(garbage));
        }

        // ---- helpers ---------------------------------------------------------------------------

        private static void AssertSnapshotEqual(ReplaySnapshot expected, ReplaySnapshot actual)
        {
            Assert.AreEqual(expected.kind, actual.kind);
            Assert.AreEqual(expected.entityId, actual.entityId);
            Assert.AreEqual(expected.posX, actual.posX);
            Assert.AreEqual(expected.posY, actual.posY);
            Assert.AreEqual(expected.posZ, actual.posZ);
            Assert.AreEqual(expected.rotEncoding, actual.rotEncoding);
            Assert.AreEqual(expected.rot, actual.rot);
            if (expected.kind == ReplayEntityKind.Piece)
            {
                Assert.AreEqual(expected.pieceType, actual.pieceType);
                Assert.AreEqual(expected.motion, actual.motion);
            }
        }

        private static void AssertEventEqual(ReplayEvent expected, ReplayEvent actual)
        {
            Assert.AreEqual(expected.timestampMs, actual.timestampMs);
            Assert.AreEqual(expected.type, actual.type);
            Assert.AreEqual(expected.alliance, actual.alliance);
            Assert.AreEqual(expected.actorSlot, actual.actorSlot);
            Assert.AreEqual(expected.pieceId, actual.pieceId);
            Assert.AreEqual(expected.intValue, actual.intValue);
            Assert.AreEqual(expected.hasPosition, actual.hasPosition);
            if (expected.hasPosition)
            {
                Assert.AreEqual(expected.posX, actual.posX);
                Assert.AreEqual(expected.posY, actual.posY);
                Assert.AreEqual(expected.posZ, actual.posZ);
            }
        }
    }
}
