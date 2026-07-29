// CloSim Online Multiplayer — ensures a MatchLauncher exists so a hosted match can actually launch.
// Namespace: Online.Sync (Phase 3 Gameplay Sync).
//
// THE BUG THIS FIXES: RoomService.ServerStartMatch() hands off to an IMatchLauncher it locates via
// ResolveLauncher() — which returns RoomService.Launcher, else scans the scene with FindObjectsByType.
// Nothing in the shipped game ever instantiates the concrete MatchLauncher MonoBehaviour (only the
// editor/inspector could add it, and no scene does), so ResolveLauncher() always returned null and
// ServerStartMatch() logged "No IMatchLauncher available" and spawned NOTHING. Symptom: the host's Start
// button is enabled (all players sided + ready, mode matches) but clicking it does nothing — no scene
// change, no robots.
//
// FIX: create ONE persistent MatchLauncher at startup and register it as RoomService.Launcher. This mirrors
// how RoomNetworkBootstrap ensures the RoomService exists. A MatchLauncher is completely inert until
// LaunchNetworkedMatch() is called, and that path is host-only ([Server] via ServerStartMatch, guarded by
// NetworkServer.active), so a persistent instance is harmless on remote clients and in offline play — it
// never self-acts there.

using Online.Rooms;
using UnityEngine;

namespace Online.Sync
{
    /// <summary>
    /// Creates one persistent <see cref="MatchLauncher"/> at startup and registers it as
    /// <see cref="RoomService.Launcher"/>, so the host's Start button actually launches the networked match.
    /// </summary>
    public static class MatchLauncherBootstrap
    {
        private static MatchLauncher _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureLauncher()
        {
            if (_instance != null)
                return;

            var go = new GameObject("MatchLauncher (runtime)");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<MatchLauncher>();

            // ResolveLauncher() checks RoomService.Launcher first, so wire it directly. This avoids depending
            // on a scene scan and guarantees the launcher is found the instant ServerStartMatch() runs.
            RoomService.Launcher = _instance;
        }
    }
}
