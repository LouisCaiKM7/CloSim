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
