// CloSim Online Multiplayer — Create Room screen (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. Host-a-room form built 100% programmatically with LobbyUiKit.
// On submit it seeds a RoomInfo + NetworkMatchConfig and opens a listen-server via LobbyServices.HostRoom,
// then navigates to the Room screen. No prefabs/scenes; additive only.

using System.Collections.Generic;
using Online.Contracts;
using Online.Net;
using Online.Rooms;
using TMPro;
using UI.Components;
using UnityEngine;
using UnityEngine.UI;

namespace Online.UI.Lobby
{
    /// <summary>Native lobby screen where the host configures and opens a new room.</summary>
    public class CreateRoomScreen : LobbyScreen
    {
        private TMP_InputField _roomNameInput;
        private TMP_InputField _yourNameInput;
        private GamepadDropdown _gameDropdown;
        private GamepadDropdown _visibilityDropdown;
        private TMP_InputField _tokenInput;
        private Toggle _spectatorsToggle;
        private TMP_InputField _portInput;
        private TMP_Text _statusLabel;

        /// <inheritdoc />
        public override string Key => LobbyScreenKeys.Create;

        /// <inheritdoc />
        protected override void BuildUi()
        {
            // Full-screen dim background behind the centered card.
            LobbyUiKit.Panel(transform, "Background", LobbyUiKit.PanelBg);

            // Centered card, fixed width, auto height to fit its content.
            GameObject card = LobbyUiKit.Card(transform, "CreateRoomCard", LobbyUiKit.CardBg);
            LobbyUiKit.SetSize(card, 640, -1);
            var cardRect = LobbyUiKit.RectOf(card);
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(640, 0);
            var fitter = card.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LobbyUiKit.Label(card.transform, "Create Room", 34);

            string localName = Services.LocalPlayerName;

            LobbyUiKit.Label(card.transform, "Room Name", 20, TextAlignmentOptions.Left, LobbyUiKit.TextMuted);
            _roomNameInput = LobbyUiKit.InputField(card.transform, "Room name", localName + "'s Room");

            LobbyUiKit.Label(card.transform, "Your Name", 20, TextAlignmentOptions.Left, LobbyUiKit.TextMuted);
            _yourNameInput = LobbyUiKit.InputField(card.transform, "Your name", localName);

            LobbyUiKit.Label(card.transform, "Game", 20, TextAlignmentOptions.Left, LobbyUiKit.TextMuted);
            _gameDropdown = LobbyUiKit.Dropdown(card.transform, BuildGameOptions());

            LobbyUiKit.Label(card.transform, "Visibility", 20, TextAlignmentOptions.Left, LobbyUiKit.TextMuted);
            _visibilityDropdown = LobbyUiKit.Dropdown(card.transform, new List<string> { "Public", "Private" });

            LobbyUiKit.Label(card.transform, "Join Token", 20, TextAlignmentOptions.Left, LobbyUiKit.TextMuted);
            _tokenInput = LobbyUiKit.InputField(card.transform, "Join token (optional)");
            LobbyUiKit.Label(card.transform, "Private rooms are not listed on the Server List.",
                16, TextAlignmentOptions.Left, LobbyUiKit.TextMuted);

            _spectatorsToggle = LobbyUiKit.Toggle(card.transform, "Allow spectators", true, out _);

            LobbyUiKit.Label(card.transform, "Port", 20, TextAlignmentOptions.Left, LobbyUiKit.TextMuted);
            _portInput = LobbyUiKit.InputField(card.transform, "Port (0 = default)", "0");

            _statusLabel = LobbyUiKit.Label(card.transform, "", 18, TextAlignmentOptions.Center, LobbyUiKit.TextMuted);

            // Buttons row.
            GameObject row = LobbyUiKit.Row(card.transform, "Buttons", 10, 0, TextAnchor.MiddleCenter);
            LobbyUiKit.SetSize(row, -1, 52);

            Button createButton = LobbyUiKit.Button(row.transform, "Create");
            LobbyUiKit.FlexibleWidth(createButton.gameObject);
            LobbyUiKit.TintButton(createButton, LobbyUiKit.ButtonAccent);
            createButton.onClick.AddListener(OnCreate);

            Button backButton = LobbyUiKit.Button(row.transform, "Back");
            LobbyUiKit.FlexibleWidth(backButton.gameObject);
            backButton.onClick.AddListener(Back);

            FirstSelected = _roomNameInput.gameObject;
        }

        /// <summary>Display names of every selectable game in <see cref="LobbyGameCatalog.Default"/>.</summary>
        private static List<string> BuildGameOptions()
        {
            var options = new List<string>();
            IReadOnlyList<LobbyGame> games = LobbyGameCatalog.Default;
            for (int i = 0; i < games.Count; i++)
                options.Add(games[i].displayName);
            return options;
        }

        /// <summary>Validates the form, opens a listen-server, and navigates to the Room screen.</summary>
        private void OnCreate()
        {
            IReadOnlyList<LobbyGame> games = LobbyGameCatalog.Default;
            if (games.Count == 0)
            {
                _statusLabel.text = "No games available.";
                return;
            }

            int gameIndex = Mathf.Clamp(_gameDropdown.value, 0, games.Count - 1);
            LobbyGame game = games[gameIndex];

            RoomVisibility visibility = _visibilityDropdown.value == 1
                ? RoomVisibility.Private
                : RoomVisibility.Public;

            string token = (_tokenInput.text ?? "").Trim();

            string playerName = (_yourNameInput.text ?? "").Trim();
            if (!string.IsNullOrEmpty(playerName))
                Services.LocalPlayerName = playerName;

            string roomName = (_roomNameInput.text ?? "").Trim();
            if (string.IsNullOrEmpty(roomName))
                roomName = Services.LocalPlayerName + "'s Room";

            if (!int.TryParse(_portInput.text, out int port))
                port = 0;

            var info = new RoomInfo
            {
                name = roomName,
                hostName = Services.LocalPlayerName,
                gameId = game.gameId,
                port = port,
                capacity = RoomValidation.MaxMembers,
                visibility = visibility,
                requiresToken = !string.IsNullOrEmpty(token),
                state = RoomState.Lobby,
                version = NetcodeProtocol.Version
            };

            var config = new NetworkMatchConfig
            {
                blueCount = 1,
                redCount = 1,
                gameId = game.gameId,
                sceneName = game.sceneName,
                humanPlayerType = 0,
                allowSpectators = _spectatorsToggle.isOn
            };

            _statusLabel.text = "Creating room...";
            Services.HostRoom(info, config, token);
            Go(LobbyScreenKeys.Room);
        }
    }
}
