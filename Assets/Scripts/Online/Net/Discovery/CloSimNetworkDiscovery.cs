// CloSim Online Multiplayer — LAN discovery (advertise + find).
// Module: LAN discovery + direct-IP connect (A2 Netcode Foundation, discovery helper).
// Namespace: Online.Net.Discovery. Additive only.
//
// WHAT THIS IS
//   The LAN half of the "join your own host / private-room" path (architecture.md §4.2 (a)).
//   - HOST: AdvertiseRoom(RoomInfo) -> broadcasts its room to the local subnet over UDP.
//   - CLIENT: StartFinding() -> broadcasts a probe and raises OnRoomDiscovered per host found.
//   The discovered RoomInfo feeds a native in-game "LAN rooms" list (A5) or a direct
//   DirectConnect.Join(room.address, (ushort)room.port) (A3/A5).
//
// WHY IT SUBCLASSES NetworkDiscoveryBase<,> RATHER THAN Mirror.NetworkDiscovery
//   Mirror's concrete `NetworkDiscovery` is hard-wired to Mirror's own `ServerRequest`/`ServerResponse`,
//   whose response only carries a Uri + serverId. We need to advertise room metadata (name, mode,
//   player count, token-required, version). The clean, idiomatic Mirror way to do that is to subclass
//   the SAME base Mirror.NetworkDiscovery uses — NetworkDiscoveryBase<Request, Response> — with our own
//   message types. Everything else (UDP broadcast, listen sockets, secret-handshake gating) is inherited
//   from Mirror unchanged. This satisfies the "wrapper around Mirror's NetworkDiscovery if subclassing
//   isn't clean" allowance in the brief while staying 100% Mirror-native.
//
// TOKEN NOTE (transport/auth boundary only)
//   Discovery advertises ONLY `requiresToken` — never the token. The actual token check happens at
//   connect time in the separate `CloSimNetworkAuthenticator` helper (owned elsewhere in A2); the
//   token to send is staged via DirectConnect.PendingJoinToken. This class does no auth.

using System;
using System.Net;
using Mirror;
using Online.Contracts;
using UnityEngine;

namespace Online.Net.Discovery
{
    /// <summary>
    /// CloSim LAN discovery component. Attach to the same GameObject as the CloSimNetworkManager
    /// (the core agent wires this up). Hosts advertise their room; clients find hosts on the subnet.
    /// </summary>
    [DisallowMultipleComponent]
    public class CloSimNetworkDiscovery : NetworkDiscoveryBase<CloSimDiscoveryRequest, CloSimDiscoveryResponse>
    {
        // A fixed CloSim-wide magic value so only CloSim builds discover each other on a shared LAN
        // (and so two independently-produced builds match, unlike Mirror's per-prefab random default).
        // Bump this if the discovery wire format changes incompatibly.
        private const long CloSimHandshake = 0x_C105_1_10AD_0001; // "CLoSIM LOAD 0001"

        /// <summary>Raised once per host discovered on the LAN (client side). May fire repeatedly as
        /// broadcasts repeat; the payload's <see cref="RoomInfo.roomId"/> is a stable synthetic key
        /// ("lan:ip:port") so subscribers can de-duplicate.</summary>
        public event Action<RoomInfo> OnRoomDiscovered;

        // The room this host currently advertises. Updated live via AdvertiseRoom / UpdateAdvertisedRoom.
        private RoomInfo _advertisedRoom;
        private bool _hasAdvertisedRoom;

        // A per-process id so clients can dedupe a host reachable via multiple NICs.
        private long _serverId;

        // Ensure the CloSim-wide handshake + server id are set before any UDP socket work. Called from
        // the advertise/find entry points rather than an overridden Unity lifecycle method, so we do not
        // depend on Start()/Awake()/OnValidate() virtual-ness (which varies across Mirror versions).
        private void EnsureInitialized()
        {
            secretHandshake = CloSimHandshake; // stable across all CloSim builds (Mirror default is random).
            if (_serverId == 0)
                _serverId = ((long)Guid.NewGuid().GetHashCode() << 32) ^ (DateTime.UtcNow.Ticks & 0xFFFFFFFF);
        }

        // ---------------------------------------------------------------------------------------------
        // HOST (advertise) — CloSim-friendly wrappers over Mirror's AdvertiseServer()/StopDiscovery().
        // ---------------------------------------------------------------------------------------------

        /// <summary>Begin advertising <paramref name="room"/> to the local subnet. Call after StartHost().
        /// The advertised port is <see cref="RoomInfo.port"/> (the game transport port to connect to),
        /// which is independent of the UDP <c>serverBroadcastListenPort</c> used for discovery itself.</summary>
        public void AdvertiseRoom(RoomInfo room)
        {
            EnsureInitialized();
            _advertisedRoom = room;
            _hasAdvertisedRoom = true;
            AdvertiseServer(); // Mirror: opens the UDP listen socket and answers client probes.
        }

        /// <summary>Update the advertised room payload in place (e.g. player count changed) without
        /// restarting the broadcast. Subsequent probe responses reflect the new values.</summary>
        public void UpdateAdvertisedRoom(RoomInfo room)
        {
            _advertisedRoom = room;
            _hasAdvertisedRoom = true;
        }

        /// <summary>Stop advertising (host). Alias over Mirror's StopDiscovery().</summary>
        public void StopAdvertising()
        {
            _hasAdvertisedRoom = false;
            StopDiscovery();
        }

        // ---------------------------------------------------------------------------------------------
        // CLIENT (find) — CloSim-friendly wrappers over Mirror's StartDiscovery()/StopDiscovery().
        // ---------------------------------------------------------------------------------------------

        /// <summary>Begin probing the subnet for CloSim hosts. Discovered rooms arrive via
        /// <see cref="OnRoomDiscovered"/>. Optionally subscribe <paramref name="onFound"/> for a
        /// one-shot callback (convenience for UI that does not want to manage the event directly).</summary>
        public void StartFinding(Action<RoomInfo> onFound = null)
        {
            EnsureInitialized();
            if (onFound != null)
                OnRoomDiscovered += onFound;
            StartDiscovery(); // Mirror: begins broadcasting CloSimDiscoveryRequest at ActiveDiscoveryInterval.
        }

        /// <summary>Stop probing (client). Alias over Mirror's StopDiscovery().</summary>
        public void StopFinding()
        {
            StopDiscovery();
        }

        // ---------------------------------------------------------------------------------------------
        // Mirror abstract hooks.
        // ---------------------------------------------------------------------------------------------

        /// <summary>CLIENT: builds the probe datagram Mirror broadcasts.</summary>
        protected override CloSimDiscoveryRequest GetRequest() => new CloSimDiscoveryRequest
        {
            version = Application.version
        };

        /// <summary>HOST: given a client's probe, produce the room advertisement to send back.
        /// Returning a default (empty) response is harmless if we are not currently advertising.</summary>
        protected override CloSimDiscoveryResponse ProcessRequest(CloSimDiscoveryRequest request, IPEndPoint endpoint)
        {
            if (!_hasAdvertisedRoom)
                return default; // not hosting a room right now; nothing meaningful to advertise.

            RoomInfo r = _advertisedRoom;
            return new CloSimDiscoveryResponse
            {
                serverId = _serverId,
                roomName = r.name ?? string.Empty,
                hostName = r.hostName ?? string.Empty,
                gameId = r.gameId ?? string.Empty,
                version = string.IsNullOrEmpty(r.version) ? Application.version : r.version,
                gamePort = (ushort)r.port,
                // RoomInfo does not carry the raw counts; a LAN room may advertise 0/0 until the host
                // picks a mode. Fall back to playerCount/capacity for a sensible display either way.
                blueCount = 0,
                redCount = 0,
                playerCount = r.playerCount,
                capacity = r.capacity <= 0 ? 6 : r.capacity,
                requiresToken = r.requiresToken
            };
        }

        /// <summary>CLIENT: a host answered our probe. Rebuild a RoomInfo (filling the address from the
        /// packet source, since the host cannot reliably know its own LAN IP) and surface it.</summary>
        protected override void ProcessResponse(CloSimDiscoveryResponse response, IPEndPoint endpoint)
        {
            // The authoritative address is where the datagram actually came from.
            string address = endpoint != null ? endpoint.Address.ToString() : string.Empty;
            int port = response.gamePort;

            var room = new RoomInfo
            {
                roomId = $"lan:{address}:{port}",   // synthetic; LAN rooms are not master-server registered.
                name = string.IsNullOrEmpty(response.roomName) ? "LAN Room" : response.roomName,
                hostName = response.hostName ?? string.Empty,
                address = address,
                port = port,
                gameId = response.gameId ?? string.Empty,
                region = "LAN",
                playerCount = response.playerCount,
                capacity = response.capacity <= 0 ? 6 : response.capacity,
                visibility = RoomVisibility.Private, // LAN-discovered rooms are the not-listed (private) path.
                requiresToken = response.requiresToken,
                state = RoomState.Lobby,
                version = response.version ?? string.Empty
            };

            OnRoomDiscovered?.Invoke(room);
        }
    }
}
