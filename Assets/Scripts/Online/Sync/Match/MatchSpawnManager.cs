// CloSim Online Multiplayer — Phase 3 Gameplay Sync (T2). Namespace: Online.Sync.
//
// Per-machine coordinator that runs in the loaded match scene (invoked by MatchSceneBootstrap's online hook).
// It puts LoadMatch into online (server-authoritative) mode and:
//   * SERVER (host): applies the host-built MatchSettings, then on LoadMatch.OnOnlineFieldReady server-spawns
//     one networked robot per slot with per-connection ownership. Robot selection travels ONLY as a
//     robotIndex catalog key; a prefab reference is NEVER sent over the wire.
//   * CLIENT: registers the spawnable robot prefabs and loads the field online; the robots themselves arrive
//     via Mirror and self-register through RobotNetworkController.
//
// Robustness: LoadMatch.Start() kicks off a default OFFLINE ResetField the same frame the scene loads. We set
// online mode and re-drive ResetField (retried until it wins the in-flight guard) so any transient offline
// robot is cleared and the field reloads in online mode.

using System.Collections;
using System.Collections.Generic;
using Core;
using Mirror;
using UnityEngine;

namespace Online.Sync
{
    [AddComponentMenu("CloSim/Sync/Match Spawn Manager")]
    public sealed class MatchSpawnManager : MonoBehaviour
    {
        private LoadMatch _loadMatch;
        private bool _fieldReadyHandled;
        private bool _started;

        /// <summary>Locate the LoadMatch in the currently loaded match scene.</summary>
        public static LoadMatch FindLoadMatch()
        {
            return FindFirstObjectByType<LoadMatch>();
        }

        /// <summary>
        /// Online hook entry (per machine). Ensures a coordinator exists in the scene and drives the online
        /// spawn/setup for the given LoadMatch. Safe to call once from MatchSceneBootstrap.
        /// </summary>
        public static MatchSpawnManager RunForScene(LoadMatch loadMatch)
        {
            if (loadMatch == null)
                loadMatch = FindLoadMatch();

            if (loadMatch == null)
            {
                Debug.LogError("[MatchSpawnManager] No LoadMatch in scene; cannot run online match setup.");
                return null;
            }

            var manager = FindFirstObjectByType<MatchSpawnManager>();
            if (manager == null)
            {
                var go = new GameObject(nameof(MatchSpawnManager));
                manager = go.AddComponent<MatchSpawnManager>();
            }

            manager.Begin(loadMatch);
            return manager;
        }

        private void Begin(LoadMatch loadMatch)
        {
            if (_started)
                return;

            _started = true;
            _loadMatch = loadMatch;
            StartCoroutine(RunRoutine());
        }

        private IEnumerator RunRoutine()
        {
            RegisterSpawnablePrefabs(_loadMatch);

            bool isServer = NetworkServer.active;
            int hostOwnedSlot = -1;
            MatchSettings built = null;

            if (isServer)
            {
                NetworkMatchContext context = NetworkMatchContext.Instance;
                if (context == null || context.BuiltSettings == null)
                {
                    Debug.LogError("[MatchSpawnManager] Server has no NetworkMatchContext plan; cannot spawn robots.");
                    yield break;
                }

                built = context.BuiltSettings.Clone();
                hostOwnedSlot = context.HostOwnedSlot;
            }

            bool fieldReady = false;
            void OnFieldReady()
            {
                fieldReady = true;

                if (isServer && !_fieldReadyHandled)
                {
                    _fieldReadyHandled = true;
                    SpawnAllRobots(built);
                }
            }

            _loadMatch.OnOnlineFieldReady += OnFieldReady;

            // Put LoadMatch online BEFORE re-driving the reset so no offline robots are (re)spawned.
            _loadMatch.SetOnlineMode(true, hostOwnedSlot);

            if (isServer)
                _loadMatch.ApplySettings(built);

            // Re-drive ResetField until it wins the guard against LoadMatch.Start()'s in-flight offline reset.
            const int maxFrames = 180;
            for (int i = 0; i < maxFrames && !fieldReady; i++)
            {
                _loadMatch.ResetField();
                yield return null;
            }

            _loadMatch.OnOnlineFieldReady -= OnFieldReady;

            if (!fieldReady)
                Debug.LogWarning("[MatchSpawnManager] Timed out waiting for online field to become ready.");
        }

        // ---------------------------------------------------------------- Server spawn

        // Server-only (invoked from OnFieldReady only when NetworkServer.active). MatchSpawnManager is a plain
        // MonoBehaviour, so the guard is the explicit isServer check at the call site, not a [Server] attribute.
        private void SpawnAllRobots(MatchSettings built)
        {
            NetworkMatchContext context = NetworkMatchContext.Instance;
            if (context == null || built == null)
                return;

            int blueCount = built.networkBlueCount;
            int redCount = built.networkRedCount;
            int total = Mathf.Clamp(blueCount + redCount, 1, 6);

            for (int slot = 0; slot < total; slot++)
            {
                PlayerMatchSettings player = built.GetPlayer(slot);

                GameObject prefab = _loadMatch.GetNetworkRobotPrefab(player.robotIndex);
                if (prefab == null)
                {
                    Debug.LogError($"[MatchSpawnManager] No robot prefab for slot {slot} (robotIndex {player.robotIndex}).");
                    continue;
                }

                if (prefab.GetComponent<NetworkIdentity>() == null)
                {
                    Debug.LogError($"[MatchSpawnManager] Robot prefab '{prefab.name}' has no NetworkIdentity; cannot server-spawn.");
                    continue;
                }

                if (!_loadMatch.TryGetSpawnForSlot(slot, prefab, out Vector3 pos, out Quaternion rot))
                {
                    Debug.LogWarning($"[MatchSpawnManager] No spawn point for slot {slot}; using origin.");
                    pos = Vector3.zero;
                    rot = Quaternion.identity;
                }

                GameObject robot = Instantiate(prefab, pos, rot);
                robot.name = $"{prefab.name}_Net_S{slot}";

                var rnc = robot.GetComponent<RobotNetworkController>();
                if (rnc == null)
                {
                    Debug.LogError($"[MatchSpawnManager] Robot prefab '{prefab.name}' has no RobotNetworkController; destroying.");
                    Destroy(robot);
                    continue;
                }

                rnc.ServerInit(slot, player.robotIndex, player.view, blueCount, redCount);

                NetworkConnectionToClient owner = ResolveOwnerConnection(context, slot);
                if (owner != null)
                    NetworkServer.Spawn(robot, owner);
                else
                    NetworkServer.Spawn(robot); // unowned (no roster member for this slot)
            }

            context.MarkSpawned();
        }

        private NetworkConnectionToClient ResolveOwnerConnection(NetworkMatchContext context, int slot)
        {
            if (slot == context.HostOwnedSlot)
                return NetworkServer.localConnection;

            int[] map = context.SlotConnectionIds;
            if (map == null || slot < 0 || slot >= map.Length)
                return null;

            int connId = map[slot];
            if (connId < 0)
                return null;

            if (connId == 0 || (NetworkServer.localConnection != null && connId == NetworkServer.localConnection.connectionId))
                return NetworkServer.localConnection;

            return NetworkServer.connections.TryGetValue(connId, out NetworkConnectionToClient conn) ? conn : null;
        }

        // ---------------------------------------------------------------- Prefab registration

        private static void RegisterSpawnablePrefabs(LoadMatch loadMatch)
        {
            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[MatchSpawnManager] No NetworkManager.singleton; cannot register spawn prefabs.");
                return;
            }

            var seen = new HashSet<GameObject>();
            foreach (RobotCatalogEntry entry in loadMatch.GetRobotCatalog())
            {
                GameObject prefab = entry.Prefab;
                if (prefab == null || !seen.Add(prefab))
                    continue;

                if (prefab.GetComponent<NetworkIdentity>() == null)
                    continue; // non-networked catalog entries are simply not spawnable online

                if (!manager.spawnPrefabs.Contains(prefab))
                    manager.spawnPrefabs.Add(prefab);

                // Clients must know the prefab by assetId to instantiate incoming spawns.
                if (NetworkClient.active)
                    NetworkClient.RegisterPrefab(prefab);
            }
        }

        // ---------------------------------------------------------------- Client settings patch

        /// <summary>
        /// Client-side: patch a single slot's alliance/robot/view into LoadMatch so bumper colours + drive mode
        /// resolve correctly for a robot that arrived over the network. Uses ApplySettings (which does NOT reload
        /// the field or destroy robots), so it is safe to call as each networked robot registers. No-op on the
        /// server, which already holds the full built settings.
        /// </summary>
        public static void PatchClientSettingsForSlot(
            LoadMatch loadMatch, int slot, int robotIndex, Cameras view, int blueCount, int redCount)
        {
            if (loadMatch == null || slot < 0)
                return;

            MatchSettings settings = loadMatch.GetSettingsCopy();
            settings.useNetworkCounts = true;
            settings.networkBlueCount = blueCount;
            settings.networkRedCount = redCount;

            PlayerMatchSettings player = settings.GetPlayer(slot);
            player.robotIndex = robotIndex;
            player.view = view;

            loadMatch.ApplySettings(settings);
        }
    }
}
