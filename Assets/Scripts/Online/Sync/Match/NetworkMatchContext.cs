// CloSim Online Multiplayer — Phase 3 Gameplay Sync (T2). Namespace: Online.Sync.
//
// A DontDestroyOnLoad hand-off object populated HOST-SIDE by MatchLauncher before the match scene
// is loaded (Mirror ServerChangeScene). Once the scene is up, the per-machine MatchSpawnManager picks
// the built MatchSettings + roster mapping back up on the server to drive server-authoritative spawn.
//
// Nothing here is networked — it is host-local scratch state. The authoritative per-robot data that
// travels to clients lives on RobotNetworkController SyncVars (slot / robotIndex / view / counts).

using System;
using System.Collections.Generic;
using Core;
using Online.Contracts;
using UnityEngine;

namespace Online.Sync
{
    /// <summary>
    /// Host-local carrier for the built <see cref="MatchSettings"/> + roster→slot mapping produced by
    /// <see cref="MatchLauncher"/>, so the in-scene <see cref="MatchSpawnManager"/> can spawn robots after
    /// the networked scene load. Survives the scene change via DontDestroyOnLoad.
    /// </summary>
    public sealed class NetworkMatchContext : MonoBehaviour
    {
        public static NetworkMatchContext Instance { get; private set; }

        // --- Host-built match description (set before ServerChangeScene) ---
        public NetworkMatchConfig Config { get; private set; }
        public MatchSettings BuiltSettings { get; private set; }
        public IReadOnlyList<RoomMemberSlot> Roster { get; private set; }

        /// <summary>LoadMatch slot (blue-first) -> Mirror connectionId of the member that owns it.</summary>
        public int[] SlotConnectionIds { get; private set; } = Array.Empty<int>();

        /// <summary>The blue-first LoadMatch slot owned by the host (its local robot). -1 if the host is a spectator.</summary>
        public int HostOwnedSlot { get; private set; } = -1;

        /// <summary>True once every robot has been server-spawned + owned.</summary>
        public bool IsSpawned { get; private set; }

        /// <summary>Raised on the server once all robots are spawned. MatchLauncher relays this as OnMatchSpawned.</summary>
        public event Action MatchSpawned;

        public static NetworkMatchContext EnsureExists()
        {
            if (Instance != null)
                return Instance;

            var go = new GameObject(nameof(NetworkMatchContext));
            Instance = go.AddComponent<NetworkMatchContext>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Host-side: stash the built match description for the next scene's spawn manager.</summary>
        public void SetPlan(
            NetworkMatchConfig config,
            MatchSettings builtSettings,
            IReadOnlyList<RoomMemberSlot> roster,
            int[] slotConnectionIds,
            int hostOwnedSlot)
        {
            Config = config;
            BuiltSettings = builtSettings;
            Roster = roster;
            SlotConnectionIds = slotConnectionIds ?? Array.Empty<int>();
            HostOwnedSlot = hostOwnedSlot;
            IsSpawned = false;
        }

        /// <summary>Server-side: called by MatchSpawnManager once all robots are spawned + owned.</summary>
        public void MarkSpawned()
        {
            if (IsSpawned)
                return;

            IsSpawned = true;
            MatchSpawned?.Invoke();
        }

        public void Clear()
        {
            Config = default;
            BuiltSettings = null;
            Roster = null;
            SlotConnectionIds = Array.Empty<int>();
            HostOwnedSlot = -1;
            IsSpawned = false;
        }
    }
}
