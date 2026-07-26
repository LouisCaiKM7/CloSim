// CloSim Online Multiplayer — contract DTO smoke tests (EditMode).
// Owned by the Build/QA & Integration Harness (additive; references Online.Contracts only).
//
// Purpose: a compile-and-usability smoke test proving the shared DTOs in
// Online.Contracts (architecture.md §5) are instantiable and their basic
// invariants hold — capacity math, per-alliance cap (<=3), and total cap (<=6).
// This is NOT a behavioural test of the netcode; it guards the CONTRACT shape
// the whole team codes against, so a breaking change to the DTOs fails CI fast.

using NUnit.Framework;
using Online.Contracts;

namespace CloSim.Tests.EditMode
{
    [TestFixture]
    public class ContractsSmokeTests
    {
        [Test]
        public void NetworkMatchConfig_TotalPlayers_IsSumOfCounts()
        {
            var cfg = new NetworkMatchConfig { blueCount = 3, redCount = 2 };
            Assert.AreEqual(5, cfg.TotalPlayers, "TotalPlayers must equal blueCount + redCount.");
        }

        // Every versus/co-op shape the design allows must validate.
        [TestCase(1, 0)] // OneVsZero  (solo)
        [TestCase(2, 0)] // TwoVsZero  (co-op)
        [TestCase(3, 0)] // ThreeVsZero(co-op)
        [TestCase(1, 1)] // 1v1
        [TestCase(2, 1)] // 2v1 (online-only, asymmetric)
        [TestCase(2, 2)] // 2v2
        [TestCase(3, 1)] // 3v1
        [TestCase(3, 2)] // 3v2
        [TestCase(3, 3)] // 3v3 (max)
        public void NetworkMatchConfig_SupportedShapes_AreValid(int blue, int red)
        {
            var cfg = new NetworkMatchConfig { blueCount = blue, redCount = red };
            Assert.IsTrue(cfg.IsValid, $"Shape {blue}v{red} should be a valid match config.");
        }

        [TestCase(0, 0)] // no players
        [TestCase(4, 0)] // >3 on one alliance
        [TestCase(0, 4)] // >3 on one alliance
        [TestCase(3, 4)] // per-alliance cap broken
        [TestCase(-1, 2)] // negative count
        public void NetworkMatchConfig_IllegalShapes_AreInvalid(int blue, int red)
        {
            var cfg = new NetworkMatchConfig { blueCount = blue, redCount = red };
            Assert.IsFalse(cfg.IsValid, $"Shape {blue}v{red} must be rejected by IsValid.");
        }

        [Test]
        public void NetworkMatchConfig_TotalPlayers_NeverExceedsSix()
        {
            // Max legal shape is 3v3 == 6. IsValid caps TotalPlayers at 6.
            var maxed = new NetworkMatchConfig { blueCount = 3, redCount = 3 };
            Assert.AreEqual(6, maxed.TotalPlayers);
            Assert.IsTrue(maxed.IsValid);
        }

        [Test]
        public void RoomInfo_DefaultCapacity_CanBeSetToSix_AndNeverCarriesToken()
        {
            // capacity is fixed at 6 for now; requiresToken is the ONLY token-related
            // field exposed publicly — the join token itself must never live on RoomInfo.
            var room = new RoomInfo
            {
                roomId = "abc123",
                name = "Test Room",
                hostName = "host",
                address = "127.0.0.1",
                port = 7777,
                gameId = "Rebuilt",
                region = "",
                playerCount = 2,
                capacity = 6,
                visibility = RoomVisibility.Public,
                requiresToken = true,
                state = RoomState.Lobby,
                version = "1.0.0",
            };

            Assert.AreEqual(6, room.capacity, "Room capacity is fixed at 6 for now.");
            Assert.LessOrEqual(room.playerCount, room.capacity, "playerCount must never exceed capacity.");
            Assert.IsTrue(room.requiresToken);

            // Reflection guard: RoomInfo must not expose any join-token/password field.
            foreach (var field in typeof(RoomInfo).GetFields())
            {
                string n = field.Name.ToLowerInvariant();
                Assert.IsFalse(n.Contains("token") && n != "requirestoken",
                    $"RoomInfo must not expose a join token field (found '{field.Name}').");
                Assert.IsFalse(n.Contains("password"),
                    $"RoomInfo must not expose a password field (found '{field.Name}').");
            }
        }

        [Test]
        public void RoomMemberSlot_SlotIndex_IsWithinRoomCapacity()
        {
            // Six occupied slots (indices 0..5), <=3 per alliance, exactly one host.
            var slots = new RoomMemberSlot[6];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new RoomMemberSlot
                {
                    connectionId = i,
                    slotIndex = i,
                    displayName = $"player{i}",
                    alliance = (i < 3) ? RoomAlliance.Blue : RoomAlliance.Red,
                    role = MemberRole.Player,
                    isReady = false,
                    isHost = (i == 0),
                    robotIndex = i,
                };
            }

            int blue = 0, red = 0, hosts = 0;
            foreach (var s in slots)
            {
                Assert.GreaterOrEqual(s.slotIndex, 0);
                Assert.Less(s.slotIndex, 6, "slotIndex must be within the 6-slot room.");
                if (s.alliance == RoomAlliance.Blue) blue++;
                if (s.alliance == RoomAlliance.Red) red++;
                if (s.isHost) hosts++;
            }

            Assert.LessOrEqual(blue, 3, "No more than 3 members per alliance (blue).");
            Assert.LessOrEqual(red, 3, "No more than 3 members per alliance (red).");
            Assert.AreEqual(6, blue + red);
            Assert.AreEqual(1, hosts, "Exactly one member is the host.");
        }

        [Test]
        public void MasterServerConfig_ShipsBlank_SoNoAccidentalEndpoint()
        {
            // Golden rule 2: master-server config stays blank in the repo.
            Assert.IsEmpty(MasterServerConfig.MasterServerUrl,
                "MasterServerUrl MUST ship blank ('') — the user provides it at deploy time.");
            Assert.IsEmpty(MasterServerConfig.ClientApiKey,
                "ClientApiKey MUST ship blank ('') — never commit a real credential.");
            Assert.Greater(MasterServerConfig.HeartbeatSeconds, 0);
            Assert.Greater(MasterServerConfig.RoomTtlSeconds, MasterServerConfig.HeartbeatSeconds,
                "Room TTL must exceed the heartbeat interval so a live host is not dropped.");
        }
    }
}
