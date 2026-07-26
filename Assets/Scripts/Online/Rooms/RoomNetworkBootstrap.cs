// CloSim Online Multiplayer — spawns the networked RoomService on host start (A3 Rooms & Modes).
// Namespace: Online.Rooms. Bridges A2's IOnlineConnection lifecycle to the room object's lifecycle
// WITHOUT either layer referencing the other's internals.
//
// EDITOR SETUP (documented; agents cannot compile Unity here):
//   Create a prefab with a NetworkIdentity + RoomService component and assign it to `roomServicePrefab`.
//   Place a LobbyServices + RoomNetworkBootstrap in the online lobby scene (both host and client run it).
//   On host start this spawns the room; on clients the prefab is registered so the room replicates in.
//
// If no prefab is assigned, a runtime host-only room is created as a fallback so the HOST's lobby logic
// is still testable (it will NOT replicate to remote clients — a prefab with a stable assetId is
// required for real multi-client rooms).

using Mirror;
using Online.Contracts;
using UnityEngine;

namespace Online.Rooms
{
    [AddComponentMenu("CloSim/Rooms/Room Network Bootstrap")]
    [DisallowMultipleComponent]
    public class RoomNetworkBootstrap : MonoBehaviour
    {
        [Tooltip("Prefab with a NetworkIdentity + RoomService. Required for the room to replicate to " +
                 "remote clients. Leave empty only for host-only local testing.")]
        [SerializeField] private GameObject roomServicePrefab;

        private IOnlineConnection _connection;
        private bool _spawned;

        private void Start()
        {
            _connection = LobbyServices.EnsureExists().Connection;
            _connection.OnHostStarted += HandleHostStarted;
            _connection.OnHostStopped += HandleHostStopped;

            // Register the room prefab client-side so a server-spawned room replicates in.
            if (roomServicePrefab != null && roomServicePrefab.GetComponent<NetworkIdentity>() != null)
                NetworkClient.RegisterPrefab(roomServicePrefab);
        }

        private void OnDestroy()
        {
            if (_connection == null) return;
            _connection.OnHostStarted -= HandleHostStarted;
            _connection.OnHostStopped -= HandleHostStopped;
        }

        private void HandleHostStarted()
        {
            if (_spawned || !NetworkServer.active) return;
            _spawned = true;

            GameObject instance;
            if (roomServicePrefab != null)
            {
                instance = Instantiate(roomServicePrefab);
            }
            else
            {
                Debug.LogWarning("[RoomNetworkBootstrap] No roomServicePrefab assigned — creating a " +
                                 "host-only room that will NOT replicate to remote clients.");
                instance = new GameObject("RoomService (runtime)");
                instance.AddComponent<NetworkIdentity>();
                instance.AddComponent<RoomService>();
            }

            var room = instance.GetComponent<RoomService>();
            if (room == null)
            {
                Debug.LogError("[RoomNetworkBootstrap] roomServicePrefab has no RoomService component.");
                Destroy(instance);
                _spawned = false;
                return;
            }

            NetworkServer.Spawn(instance);
            ApplySeed(room);
        }

        private void ApplySeed(RoomService room)
        {
            LobbyServices services = LobbyServices.EnsureExists();
            RoomSeed seed = services.ConsumePendingSeed();

            if (seed.valid)
            {
                RoomInfo info = seed.info;
                info.hostName = seed.hostDisplayName;
                info.address = _connection.LocalEndpointAddress;
                room.ServerInitialize(info, seed.config, seed.hostDisplayName);
            }
            else
            {
                // Direct-hosting without going through Create Room: seed a sensible default.
                var info = new RoomInfo
                {
                    name = "CloSim Room",
                    hostName = services.LocalPlayerName,
                    address = _connection.LocalEndpointAddress,
                    capacity = RoomValidation.MaxMembers,
                    visibility = RoomVisibility.Public,
                    state = RoomState.Lobby
                };
                var config = new NetworkMatchConfig { blueCount = 1, redCount = 1, allowSpectators = true };
                room.ServerInitialize(info, config, services.LocalPlayerName);
            }
        }

        private void HandleHostStopped()
        {
            _spawned = false;
        }
    }
}
