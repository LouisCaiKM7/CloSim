// CloSim Online Multiplayer — shared enums (contract stubs).
// SOURCE OF TRUTH: Documentation/online/architecture.md §5–§6.
// Owned by A1 (Architect). Do not edit in feature branches — request changes via PR to A1.
// These are dependency-free (no UnityEngine / no Mirror) so every agent can reference them.

namespace Online.Contracts
{
    /// <summary>Room visibility in the discovery system. Public = listed on the master server; Private = not listed.</summary>
    public enum RoomVisibility
    {
        Public,
        Private
    }

    /// <summary>A network member's role within a room.</summary>
    public enum MemberRole
    {
        Player,
        Spectator
    }

    /// <summary>Alliance selection. Mirrors Core.AllianceColor but kept here so the backend DTO stays Core-independent.</summary>
    public enum RoomAlliance
    {
        Unassigned,
        Blue,
        Red
    }

    /// <summary>Lifecycle state of a room.</summary>
    public enum RoomState
    {
        Lobby,
        Starting,
        InMatch,
        Ended
    }

    /// <summary>Local connection role.</summary>
    public enum ConnectionRole
    {
        None,
        Host,
        Client
    }

    /// <summary>Result of a connection attempt, surfaced to UI.</summary>
    public enum ConnectResult
    {
        Success,
        Rejected_BadToken,
        Rejected_Full,
        Rejected_Version,
        Timeout,
        TransportError
    }
}
