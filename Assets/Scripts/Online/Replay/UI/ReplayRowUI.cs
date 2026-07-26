// CloSim Online Multiplayer — Replays list row widget. Namespace: Online.Replay.UI.
// A single reusable native-Unity row describing one stored replay (game/mode/date/duration/score + a
// Play button). Built 100% programmatically with Online.UI.Lobby.LobbyUiKit — no prefabs, no web page,
// mirrors Online.UI.Lobby.ServerListRowUI's structure so the whole in-game UI reads as one system.

using System;
using Online.Contracts.Replay;
using Online.UI.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Online.Replay.UI
{
    /// <summary>One row in the Replays list. Shows a stored replay's summary and exposes a Play button.</summary>
    public class ReplayRowUI : MonoBehaviour
    {
        /// <summary>Raised when the user clicks Play, with the bound replay's metadata.</summary>
        public event Action<ReplayMetadata> OnPlayClicked;

        private TMP_Text _titleLabel;
        private TMP_Text _modeLabel;
        private TMP_Text _dateLabel;
        private TMP_Text _durationLabel;
        private TMP_Text _scoreLabel;
        private Button _playButton;

        private ReplayMetadata _replay;

        /// <summary>Builds the row hierarchy under <paramref name="parent"/> and returns the wired component.</summary>
        public static ReplayRowUI Create(Transform parent)
        {
            GameObject cardGo = LobbyUiKit.Card(parent, "ReplayRow", LobbyUiKit.CardBg, 6f, 10);
            LobbyUiKit.SetSize(cardGo, -1, -1);
            var row = cardGo.AddComponent<ReplayRowUI>();
            row.Build(cardGo.transform);
            return row;
        }

        private void Build(Transform root)
        {
            GameObject line = LobbyUiKit.Row(root, "Line", 10f, 0, TextAnchor.MiddleLeft);
            LobbyUiKit.SetSize(line, -1, 48);

            _titleLabel = LobbyUiKit.Label(line.transform, "", 22, TextAlignmentOptions.Left);
            LobbyUiKit.FlexibleWidth(_titleLabel.gameObject, 2f);

            _modeLabel = LobbyUiKit.Label(line.transform, "", 18, TextAlignmentOptions.Center);
            LobbyUiKit.SetSize(_modeLabel.gameObject, 90, -1);

            _dateLabel = LobbyUiKit.Label(line.transform, "", 16, TextAlignmentOptions.Center, LobbyUiKit.TextMuted);
            LobbyUiKit.SetSize(_dateLabel.gameObject, 170, -1);

            _durationLabel = LobbyUiKit.Label(line.transform, "", 16, TextAlignmentOptions.Center, LobbyUiKit.TextMuted);
            LobbyUiKit.SetSize(_durationLabel.gameObject, 90, -1);

            _scoreLabel = LobbyUiKit.Label(line.transform, "", 20, TextAlignmentOptions.Center);
            LobbyUiKit.SetSize(_scoreLabel.gameObject, 110, -1);

            _playButton = LobbyUiKit.Button(line.transform, "Play", out _, 20);
            LobbyUiKit.SetSize(_playButton.gameObject, 110, 44);
            _playButton.onClick.AddListener(RaisePlay);
        }

        private void RaisePlay() => OnPlayClicked?.Invoke(_replay);

        /// <summary>Populates the row from <paramref name="replay"/>.</summary>
        public void Bind(ReplayMetadata replay)
        {
            _replay = replay;

            _titleLabel.text = string.IsNullOrEmpty(replay.gameId) ? "(unknown game)" : replay.gameId;
            _modeLabel.text = replay.mode.blue + "v" + replay.mode.red;
            _dateLabel.text = FormatDate(replay.createdAt);
            _durationLabel.text = FormatDuration(replay.durationSec);
            _scoreLabel.text = replay.finalScore.blue + " - " + replay.finalScore.red;

            LobbyUiKit.SetButtonInteractable(_playButton, !string.IsNullOrEmpty(replay.replayId));
        }

        private static string FormatDate(long unixMs)
        {
            if (unixMs <= 0) return "—";
            try
            {
                DateTimeOffset dto = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
                return dto.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch (Exception)
            {
                return "—";
            }
        }

        private static string FormatDuration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int m = total / 60;
            int s = total % 60;
            return $"{m:00}:{s:00}";
        }
    }
}
