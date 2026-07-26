// CloSim Online Multiplayer — Phase 3 Gameplay Sync (T2). Namespace: Online.Sync.
//
// Concrete IMatchLauncher (Online.Contracts §6.4). HOST-ONLY authority: translates the room roster +
// NetworkMatchConfig into an N-slot server-authoritative match. It does NOT touch any game rule, physics,
// scoring math, or game-piece behavior — it only builds a MatchSettings, loads the scene via Mirror server
// scene management, and hands off to the in-scene MatchSpawnManager (via NetworkMatchContext) which does the
// actual server spawn once LoadMatch fires OnOnlineFieldReady.
//
// ROSTER -> SLOT MAPPING (blue-first, matches LoadMatch.IsPlayerBlue online):
//   * LoadMatch slots [0 .. blueCount-1]           = BLUE alliance
//   * LoadMatch slots [blueCount .. total-1]       = RED  alliance
//   * Blue members (RoomMemberSlot.alliance == Blue) are ordered by RoomMemberSlot.slotIndex ascending and
//     packed into the blue slots; red members likewise into the red slots.
//   * Each RoomMemberSlot.robotIndex -> PlayerMatchSettings.robotIndex for its assigned slot (network-safe
//     catalog key; a prefab reference NEVER travels over the wire). Spawn indices are left at default and
//     LoadMatch.SanitizeSpawnSettings() de-duplicates them per alliance.
//   * The host's assigned slot is recorded as HostOwnedSlot (its local, camera-owning robot).

using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Mirror;
using Online.Contracts;
using UnityEngine;

namespace Online.Sync
{
    /// <summary>Server-authoritative match launcher. Call <see cref="LaunchNetworkedMatch"/> on the host only.</summary>
    [AddComponentMenu("CloSim/Sync/Match Launcher")]
    public sealed class MatchLauncher : MonoBehaviour, IMatchLauncher
    {
        private NetworkMatchContext _context;
        private bool _launchRequested;

        public bool IsMatchActive => _context != null && _context.IsSpawned;

        public event Action OnMatchSpawned;

        /// <summary>
        /// Host-side entry (invoked by IRoomService.ServerStartMatch). Builds the N-slot MatchSettings from the
        /// roster, stashes it for the next scene's spawn manager, and triggers a Mirror server scene change so
        /// all clients follow into the match scene.
        /// </summary>
        public void LaunchNetworkedMatch(NetworkMatchConfig config, IReadOnlyList<RoomMemberSlot> roster)
        {
            if (!NetworkServer.active)
            {
                Debug.LogError("[MatchLauncher] LaunchNetworkedMatch is host-only; NetworkServer is not active.");
                return;
            }

            if (!config.IsValid)
            {
                Debug.LogError($"[MatchLauncher] Invalid NetworkMatchConfig (blue={config.blueCount}, red={config.redCount}).");
                return;
            }

            if (string.IsNullOrWhiteSpace(config.sceneName))
            {
                Debug.LogError("[MatchLauncher] NetworkMatchConfig.sceneName is empty.");
                return;
            }

            roster ??= Array.Empty<RoomMemberSlot>();

            MatchSettings built = BuildSettings(config, roster, out int[] slotConnectionIds, out int hostOwnedSlot);

            _context = NetworkMatchContext.EnsureExists();
            _context.MatchSpawned -= HandleMatchSpawned;
            _context.MatchSpawned += HandleMatchSpawned;
            _context.SetPlan(config, built, roster, slotConnectionIds, hostOwnedSlot);

            _launchRequested = true;

            // Mirror loads the scene on the server AND all connected clients; each machine's MatchSceneBootstrap
            // then routes into the online path (MatchSpawnManager) instead of the offline GameSessionManager path.
            NetworkManager.singleton.ServerChangeScene(config.sceneName);
        }

        private void HandleMatchSpawned()
        {
            if (!_launchRequested)
                return;

            _launchRequested = false;
            OnMatchSpawned?.Invoke();
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.MatchSpawned -= HandleMatchSpawned;
        }

        // ---------------------------------------------------------------- Settings construction

        private MatchSettings BuildSettings(
            NetworkMatchConfig config,
            IReadOnlyList<RoomMemberSlot> roster,
            out int[] slotConnectionIds,
            out int hostOwnedSlot)
        {
            int blueCount = Mathf.Clamp(config.blueCount, 0, 3);
            int redCount = Mathf.Clamp(config.redCount, 0, 3);
            int total = Mathf.Clamp(blueCount + redCount, 1, 6);

            var built = new MatchSettings
            {
                useNetworkCounts = true,
                networkBlueCount = blueCount,
                networkRedCount = redCount,
                useBlueAlliance = true,
                trackingType = TrackingType.TrackRobot,
                players = new List<PlayerMatchSettings>()
            };

            // Blue-first packing: blue members (by slotIndex) -> blue slots, red members -> red slots.
            List<RoomMemberSlot> blue = roster
                .Where(m => m.alliance == RoomAlliance.Blue && m.role == MemberRole.Player)
                .OrderBy(m => m.slotIndex)
                .ToList();

            List<RoomMemberSlot> red = roster
                .Where(m => m.alliance == RoomAlliance.Red && m.role == MemberRole.Player)
                .OrderBy(m => m.slotIndex)
                .ToList();

            slotConnectionIds = new int[total];
            hostOwnedSlot = -1;

            for (int slot = 0; slot < total; slot++)
            {
                bool isBlueSlot = slot < blueCount;
                List<RoomMemberSlot> pool = isBlueSlot ? blue : red;
                int poolIndex = isBlueSlot ? slot : slot - blueCount;

                var player = new PlayerMatchSettings
                {
                    // Blue-first driver stations mirror the offline defaults (odd slots use station Three).
                    driverStation = (slot % 2 == 0) ? StationNum.One : StationNum.Three,
                    view = Cameras.ThirdPerson,
                    useVanityBumpers = true
                };

                if (poolIndex < pool.Count)
                {
                    RoomMemberSlot member = pool[poolIndex];
                    player.robotIndex = Mathf.Max(0, member.robotIndex);
                    slotConnectionIds[slot] = member.connectionId;

                    if (member.isHost)
                        hostOwnedSlot = slot;
                }
                else
                {
                    // No roster member for this slot: leave a default robot and mark owner as "none" (-1).
                    player.robotIndex = 0;
                    slotConnectionIds[slot] = -1;
                    Debug.LogWarning($"[MatchLauncher] No roster member for slot {slot} ({(isBlueSlot ? "blue" : "red")}).");
                }

                built.players.Add(player);
            }

            if (hostOwnedSlot < 0)
                Debug.LogWarning("[MatchLauncher] No host member found in roster; host will spawn as a spectator (no owned robot).");

            return built;
        }
    }
}
