// CloSim Online Multiplayer — Join Room screen (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. Direct IP/port connect plus a gateway to the Server List, built 100%
// programmatically with LobbyUiKit. On Connect it calls LobbyServices.JoinDirect and opens the Room
// screen. No prefabs/scenes; additive only.

using Online.Rooms;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Online.UI.Lobby
{
    /// <summary>Native lobby screen for direct-connect and entry to the Server List.</summary>
    public class JoinScreen : LobbyScreen
    {
        private TMP_InputField _yourNameInput;
        private TMP_InputField _addressInput;
        private TMP_InputField _portInput;
        private TMP_InputField _tokenInput;
        private TMP_Text _statusLabel;

        /// <inheritdoc />
        public override string Key => LobbyScreenKeys.Join;

        /// <inheritdoc />
        protected override void BuildUi()
        {
            LobbyUiKit.Panel(transform, "Background", LobbyUiKit.PanelBg);

            GameObject card = LobbyUiKit.Card(transform, "JoinCard", LobbyUiKit.CardBg, LobbyUiKit.SpaceMd, LobbyUiKit.PadLg);
            LobbyUiKit.SetSize(card, 640, -1);
            var cardRect = LobbyUiKit.RectOf(card);
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(640, 0);
            var fitter = card.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LobbyUiKit.Label(card.transform, "Join Room", LobbyUiKit.FontTitle);
            LobbyUiKit.Divider(card.transform);

            _yourNameInput = LobbyUiKit.LabeledInputField(card.transform, "Your Name", "Your name", Services.LocalPlayerName);
            _yourNameInput.onValueChanged.AddListener(OnNameChanged);

            _addressInput = LobbyUiKit.LabeledInputField(card.transform, "Address", "Host IP or hostname", "127.0.0.1");
            _portInput = LobbyUiKit.LabeledInputField(card.transform, "Port", "Port", "7777");
            _tokenInput = LobbyUiKit.LabeledInputField(card.transform, "Join Token", "Join token (if required)");

            _statusLabel = LobbyUiKit.Label(card.transform, "", LobbyUiKit.FontLabel, TextAlignmentOptions.Center, LobbyUiKit.TextMuted);

            // Buttons row — secondary actions on the left, primary/affirmative action on the right.
            GameObject row = LobbyUiKit.Row(card.transform, "Buttons", LobbyUiKit.SpaceSm, 0, TextAnchor.MiddleCenter);
            LobbyUiKit.SetSize(row, -1, LobbyUiKit.ButtonHeight);

            Button backButton = LobbyUiKit.SecondaryButton(row.transform, "Back");
            LobbyUiKit.FlexibleWidth(backButton.gameObject);
            backButton.onClick.AddListener(Back);

            Button serverListButton = LobbyUiKit.SecondaryButton(row.transform, "Server List");
            LobbyUiKit.FlexibleWidth(serverListButton.gameObject);
            serverListButton.onClick.AddListener(() => Go(LobbyScreenKeys.ServerList));

            Button connectButton = LobbyUiKit.PrimaryButton(row.transform, "Connect");
            LobbyUiKit.FlexibleWidth(connectButton.gameObject);
            connectButton.onClick.AddListener(OnConnect);

            FirstSelected = _addressInput.gameObject;
        }

        /// <summary>Writes the edited display name straight back to the shared services state.</summary>
        private void OnNameChanged(string value)
        {
            string name = (value ?? "").Trim();
            if (!string.IsNullOrEmpty(name))
                Services.LocalPlayerName = name;
        }

        /// <summary>Validates the address and starts a direct client connection.</summary>
        private void OnConnect()
        {
            string address = (_addressInput.text ?? "").Trim();
            if (string.IsNullOrEmpty(address))
            {
                _statusLabel.text = "Enter an address";
                return;
            }

            string name = (_yourNameInput.text ?? "").Trim();
            if (!string.IsNullOrEmpty(name))
                Services.LocalPlayerName = name;

            if (!int.TryParse(_portInput.text, out int port))
                port = 0;

            string token = (_tokenInput.text ?? "").Trim();

            _statusLabel.text = "Connecting...";
            Services.JoinDirect(address, port, token);
            Go(LobbyScreenKeys.Room);
        }
    }
}
