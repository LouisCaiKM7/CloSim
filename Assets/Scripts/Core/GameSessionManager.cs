using UnityEngine;

namespace Core
{
    public sealed class GameSessionManager : MonoBehaviour
    {
        public static GameSessionManager Instance { get; private set; }

        public MatchLaunchData CurrentLaunchData { get; private set; }

        public static GameSessionManager EnsureExists()
        {
            if (Instance != null)
                return Instance;

            GameObject obj = new GameObject("GameSessionManager");
            Instance = obj.AddComponent<GameSessionManager>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void SetLaunchData(MatchLaunchData launchData)
        {
            CurrentLaunchData = launchData;
        }

        public void ClearLaunchData()
        {
            CurrentLaunchData = null;
        }
    }

    [System.Serializable]
    public sealed class MatchLaunchData
    {
        public string gameDisplayName;
        public string sceneName;
        public MatchSettings matchSettings;
        public HumanPlayerType humanPlayerType;

        public MatchLaunchData(
            string gameDisplayName,
            string sceneName,
            MatchSettings matchSettings,
            HumanPlayerType humanPlayerType)
        {
            this.gameDisplayName = gameDisplayName;
            this.sceneName = sceneName;
            this.matchSettings = matchSettings != null ? matchSettings.Clone() : new MatchSettings();
            this.humanPlayerType = humanPlayerType;
        }

        public MatchSettings GetSettingsCopy()
        {
            return matchSettings != null ? matchSettings.Clone() : new MatchSettings();
        }
    }
}