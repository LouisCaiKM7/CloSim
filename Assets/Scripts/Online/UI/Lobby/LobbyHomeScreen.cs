// CloSim Online Multiplayer — lobby home/hub screen (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. The root screen of the online lobby: pick a player name and choose
// Create Room / Join by IP / Server List. Backing out from here exits the lobby to the main menu.

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Online.UI.Lobby
{
    public class LobbyHomeScreen : LobbyScreen
    {
        public override string Key => LobbyScreenKeys.Home;

        private TMP_InputField _nameField;

        protected override void BuildUi()
        {
            LobbyUiKit.Panel(transform, "bg", LobbyUiKit.PanelBg);

            GameObject column = LobbyUiKit.Column(transform, "content", LobbyUiKit.SpaceLg, LobbyUiKit.PadLg, TextAnchor.MiddleCenter);
            LobbyUiKit.Stretch(LobbyUiKit.RectOf(column));

            LobbyUiKit.Label(column.transform, "Online Multiplayer", LobbyUiKit.FontHero, TextAlignmentOptions.Center, LobbyUiKit.Accent);
            LobbyUiKit.Label(column.transform,
                "Host a listen-server or join another player. Up to 6 players, max 3 per alliance.",
                LobbyUiKit.FontLabel, TextAlignmentOptions.Center, LobbyUiKit.TextMuted);

            GameObject card = LobbyUiKit.Card(column.transform, "menu", LobbyUiKit.CardBg, LobbyUiKit.SpaceMd, LobbyUiKit.PadLg);
            LobbyUiKit.SetSize(card, 520, -1);

            _nameField = LobbyUiKit.LabeledInputField(card.transform, "Player Name", "Your name", Services.LocalPlayerName);
            _nameField.onEndEdit.AddListener(OnNameChanged);

            LobbyUiKit.Divider(card.transform);

            Button createBtn = LobbyUiKit.PrimaryButton(card.transform, "Create Room");
            createBtn.onClick.AddListener(() => Go(LobbyScreenKeys.Create));

            Button joinBtn = LobbyUiKit.SecondaryButton(card.transform, "Join by IP");
            joinBtn.onClick.AddListener(() => Go(LobbyScreenKeys.Join));

            Button listBtn = LobbyUiKit.SecondaryButton(card.transform, "Server List");
            listBtn.onClick.AddListener(() => Go(LobbyScreenKeys.ServerList));

            Button backBtn = LobbyUiKit.DangerButton(card.transform, "Back to Menu");
            backBtn.onClick.AddListener(Back);

            FirstSelected = createBtn.gameObject;
        }

        public override void OnShow()
        {
            if (_nameField != null)
                _nameField.text = Services.LocalPlayerName;
        }

        private void OnNameChanged(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                Services.LocalPlayerName = value.Trim();
        }
    }
}
