// CloSim Online Multiplayer — protocol / build-version identity (CORE, A2-owned).
// Namespace root: Online.Net. Files under Assets/Scripts/Online/Net/Core/.
//
// Single source of truth for the netcode protocol/build version. This is the compatibility key
// that the connect handshake exchanges (validated by the helper `CloSimNetworkAuthenticator`) and
// that gets stamped onto RoomInfo.version so A3 (rooms) and A5 (Server List) can label / filter
// mismatched rooms. On connect, the host HARD-REJECTS any client whose protocol version does not
// match -> ConnectResult.Rejected_Version.

namespace Online.Net
{
    /// <summary>
    /// Single source of truth for the netcode protocol/build version. Bump <see cref="Version"/>
    /// whenever a wire-breaking change is made (message shapes, spawn contract, sync layout).
    /// </summary>
    public static class NetcodeProtocol
    {
        /// <summary>Current protocol/build version string. Kept short — it travels in the auth handshake.</summary>
        public const string Version = "closim-net-1";

        /// <summary>An empty/unspecified requested version falls back to the local <see cref="Version"/>.</summary>
        public static string Resolve(string requested) =>
            string.IsNullOrEmpty(requested) ? Version : requested;

        /// <summary>True when two (possibly empty) version strings are compatible after resolution.</summary>
        public static bool IsCompatible(string a, string b) =>
            string.Equals(Resolve(a), Resolve(b), System.StringComparison.Ordinal);
    }
}
