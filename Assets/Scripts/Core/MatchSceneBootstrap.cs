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
        }
    }
}
