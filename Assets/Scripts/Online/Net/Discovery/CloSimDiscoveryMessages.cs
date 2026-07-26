// CloSim Online Multiplayer — LAN discovery wire messages.
// Module: LAN discovery + direct-IP connect (A2 Netcode Foundation, discovery helper).
// Namespace: Online.Net.Discovery. Additive only; consumes Online.Contracts DTOs.
//
// These are the small UDP-broadcast payloads exchanged by CloSimNetworkDiscovery:
//   - Client broadcasts a CloSimDiscoveryRequest on the subnet.
//   - Each CloSim host on the subnet answers with a CloSimDiscoveryResponse describing its room.
// Keep the payload SMALL (Mirror serializes these into a single UDP datagram): room name,
// mode counts, player count, version, port, token-required flag. The join token itself is
// NEVER advertised (matches RoomInfo.requiresToken contract in architecture.md §5).

using Mirror;

namespace Online.Net.Discovery
{
    /// <summary>
    /// Client -> host LAN probe. Carries the client protocol version so a host MAY choose to
    /// ignore mismatched clients (kept as a field, not enforced here — that is the host's call).
    /// </summary>
    public struct CloSimDiscoveryRequest : NetworkMessage
    {
        public string version; // client build/protocol version ("" = unspecified)
    }

    /// <summary>
    /// Host -> client LAN advertisement. A flattened, minimal projection of RoomInfo suitable for
    /// a single UDP datagram. CloSimNetworkDiscovery rebuilds an Online.Contracts.RoomInfo from this
    /// on the client side (filling <c>address</c> from the packet's source endpoint).
    /// </summary>
    public struct CloSimDiscoveryResponse : NetworkMessage
    {
        public long serverId;      // unique-ish host id (dedupe multiple NICs / repeated broadcasts)
        public string roomName;    // host-chosen display name
        public string hostName;    // host player display name
        public string gameId;      // "Rebuilt" | "Reefscape"
        public string version;     // host build/protocol version
        public ushort gamePort;    // the Mirror TRANSPORT port to connect to (NOT the discovery port)
        public int blueCount;      // advertised match shape (0..3)
        public int redCount;       // advertised match shape (0..3)
        public int playerCount;    // current occupied player slots
        public int capacity;       // 6 for now
        public bool requiresToken; // true if a join token/password gates the room (token never sent)
    }
}
