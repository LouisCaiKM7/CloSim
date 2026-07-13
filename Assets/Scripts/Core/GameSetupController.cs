using System;
using System.Collections.Generic;
using TMPro;
using UI.Transitions;
using UnityEngine;
using UnityEngine.UI;

namespace Core
{
    [Serializable]
    public class FrcGameOption
    {
        [Header("Scene")]
        public string displayName;
        public string sceneName;

        [Header("Button UI")]
        public Button button;
        public TMP_Text nameText;
    }

    public class GameSetupController : MonoBehaviour
    {
        [Header("Game Buttons")]
        [SerializeField] private List<FrcGameOption> games = new();
        [SerializeField] private bool loadSceneWhenGameButtonClicked = true;
        [SerializeField] private int defaultSelectedGameIndex;

        private int _selectedGameIndex;

        public event Action OnBackRequested;

        private void Awake()
        {
            _selectedGameIndex = Mathf.Clamp(defaultSelectedGameIndex, 0, Mathf.Max(0, games.Count - 1));

            WireGameButtons();
        }

        private void WireGameButtons()
        {
            for (int i = 0; i < games.Count; i++)
            {
                int capturedIndex = i;
                FrcGameOption game = games[i];

                if (game == null)
                    continue;

                if (game.button != null)
                    game.button.onClick.AddListener(() => OnGameButtonClicked(capturedIndex));

                if (game.nameText != null)
                    game.nameText.text = string.IsNullOrWhiteSpace(game.displayName) ? "Unnamed Game" : game.displayName;
            }
        }

        private void OnGameButtonClicked(int index)
        {
            SelectGame(index);

            if (loadSceneWhenGameButtonClicked)
                PlaySelectedGame();
        }

        private void SelectGame(int index)
        {
            if (games.Count == 0)
                return;

            _selectedGameIndex = Mathf.Clamp(index, 0, games.Count - 1);
        }

        private void PlaySelectedGame()
        {
            if (games.Count == 0)
                return;

            FrcGameOption game = games[Mathf.Clamp(_selectedGameIndex, 0, games.Count - 1)];
            if (game == null || string.IsNullOrWhiteSpace(game.sceneName))
                return;

            GameSessionManager.EnsureExists().SetLaunchData(
                new MatchLaunchData(
                    game.displayName,
                    game.sceneName,
                    new MatchSettings(),
                    HumanPlayerType.Bucket
                )
            );

            Time.timeScale = 1f;
            SceneTransitionManager.EnsureExists().LoadScene(game.sceneName);
        }

        public void GoBack()
        {
            OnBackRequested?.Invoke();
        }
    }
}