using System.Collections;
using UnityEngine;

namespace Core
{
    public class MatchSceneBootstrap : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LoadMatch loadMatch;

        [Header("Behavior")]
        [SerializeField] private bool resetFieldAfterApplyingLaunchData = true;
        [SerializeField] private bool clearLaunchDataAfterApply;

        private IEnumerator Start()
        {
            if (loadMatch == null)
                loadMatch = FindFirstObjectByType<LoadMatch>();
            
            yield return null;

            ApplyLaunchData();
        }

        private void ApplyLaunchData()
        {
            if (loadMatch == null)
                return;

            // ONLINE (ADDITIVE, T2): when a networked session is live, the server-authoritative launcher
            // (Online.Sync.MatchLauncher / MatchSpawnManager) drives online mode + robot spawn instead of the
            // local GameSessionManager path. Clients must NOT apply local launch settings. Offline play (no
            // connection) is 100% unchanged and falls through to the original path below.
            if (Mirror.NetworkServer.active || Mirror.NetworkClient.active)
            {
                Online.Sync.MatchSpawnManager.RunForScene(loadMatch);

                // ADDITIVE: gives any client that ends up owning no robot (spectator, or otherwise
                // robot-less) a field-overview camera once spawning settles. No-op for a client that
                // owns a robot (RobotNetworkController.SetupLocalOwner's AddOnlineCamera already covers
                // it) and never runs offline — see SpectatorCameraController's own online-state guard.
                Online.Sync.SpectatorCameraController.RunForScene(loadMatch);

                // Replay recording (ADDITIVE): host-only, no-ops on pure clients (checks NetworkServer.active
                // internally) and is never reached at all offline, since we're already inside the online branch.
                Online.Replay.Recorder.MatchReplayRecorderRunner.RunForScene(loadMatch);
                return;
            }

            GameSessionManager session = GameSessionManager.Instance;
            MatchLaunchData launchData = session != null ? session.CurrentLaunchData : null;

            if (launchData == null)
                return;

            loadMatch.ApplySettings(launchData.GetSettingsCopy());
            loadMatch.SetHumanPlayerType(launchData.humanPlayerType);

            if (resetFieldAfterApplyingLaunchData)
                loadMatch.ResetField();

            if (clearLaunchDataAfterApply)
                session.ClearLaunchData();

            // Offline replay recording (ADDITIVE): makes single-player / local split-screen matches
            // produce a watchable replay too, saved locally (Application.persistentDataPath) with zero
            // AWS config — mirrors the online-host recorder above but reads robots straight off LoadMatch
            // instead of a networked roster. No-op if a LoadMatch wasn't found; never touches game content.
            Online.Replay.Recorder.OfflineMatchReplayRecorderRunner.RunForScene(loadMatch);
        }
    }
}
