// CloSim Online Multiplayer — Server List row widget (A5b, sub-worker of A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. A single reusable native-Unity row describing one browsable room
// (public master-listed OR LAN-discovered). Built 100% programmatically with LobbyUiKit — no prefabs.
//
// The row is version-aware: an incompatible room is greyed and its Join button disabled by Bind().
// It NEVER touches Mirror — joining is delegated back to the screen via OnJoinClicked, which routes
// through the LobbyServices connection façade.

using System;
using Online.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Online.UI.Lobby
{
    /// <summary>
    /// One row in the Server List. Shows a room's identity + occupancy + version and exposes a Join
    /// button. When the room requires a token it reveals an inline token input; the Join click reports
    /// the room and the (possibly empty) token back through <see cref="OnJoinClicked"/>.
    /// </summary>
    public class ServerListRowUI : MonoBehaviour
    {
        /// <summary>Raised when the user clicks Join. Args: the bound room and the inline token text ("" if none).</summary>
        public event Action<RoomInfo, string> OnJoinClicked;

        private TMP_Text _nameLabel;
        private TMP_Text _hostLabel;
        private TMP_Text _gameLabel;
        private TMP_Text _playersLabel;
        private TMP_Text _regionLabel;
        private TMP_Text _versionLabel;
        private TMP_Text _lockLabel;
        private TMP_InputField _tokenInput;
        private Button _joinButton;
        private TMP_Text _joinLabel;

        private RoomInfo _room;

        /// <summary>Builds the row hierarchy under <paramref name="parent"/> and returns the wired component.</summary>
        public static ServerListRowUI Create(Transform parent)
        {
            GameObject cardGo = LobbyUiKit.Card(parent, "ServerRow", LobbyUiKit.CardBg, LobbyUiKit.SpaceSm, LobbyUiKit.PadSm);
            LobbyUiKit.SetSize(cardGo, -1, -1);
            var row = cardGo.AddComponent<ServerListRowUI>();
            row.Build(cardGo.transform);
            return row;
        }

        /// <summary>Lays out the labels + Join button (top line) and the hidden inline token input.</summary>
        private void Build(Transform root)
        {
            GameObject line = LobbyUiKit.Row(root, "Line", LobbyUiKit.SpaceSm, 0, TextAnchor.MiddleLeft);
            LobbyUiKit.SetSize(line, -1, 48);

            _nameLabel = LobbyUiKit.Label(line.transform, "", LobbyUiKit.FontBody, TextAlignmentOptions.Left);
            LobbyUiKit.FlexibleWidth(_nameLabel.gameObject, 2f);

            _hostLabel = LobbyUiKit.Label(line.transform, "", LobbyUiKit.FontLabel, TextAlignmentOptions.Left);
            LobbyUiKit.FlexibleWidth(_hostLabel.gameObject, 1.5f);

            _gameLabel = LobbyUiKit.Label(line.transform, "", LobbyUiKit.FontLabel, TextAlignmentOptions.Left);
            LobbyUiKit.SetSize(_gameLabel.gameObject, 120, -1);

            _playersLabel = LobbyUiKit.Label(line.transform, "", LobbyUiKit.FontLabel, TextAlignmentOptions.Center);
            LobbyUiKit.SetSize(_playersLabel.gameObject, 70, -1);

            _regionLabel = LobbyUiKit.Label(line.transform, "", LobbyUiKit.FontLabel, TextAlignmentOptions.Center);
            LobbyUiKit.SetSize(_regionLabel.gameObject, 110, -1);

            _lockLabel = LobbyUiKit.Label(line.transform, "", LobbyUiKit.FontLabel, TextAlignmentOptions.Center, LobbyUiKit.Accent);
            LobbyUiKit.SetSize(_lockLabel.gameObject, 70, -1);

            _versionLabel = LobbyUiKit.Label(line.transform, "", LobbyUiKit.FontCaption, TextAlignmentOptions.Center, LobbyUiKit.TextMuted);
            LobbyUiKit.SetSize(_versionLabel.gameObject, 200, -1);

            _joinButton = LobbyUiKit.SecondaryButton(line.transform, "Join", out _joinLabel, LobbyUiKit.FontLabel);
            LobbyUiKit.SetSize(_joinButton.gameObject, 120, 44);
            _joinButton.onClick.AddListener(RaiseJoin);

            // Inline token entry — hidden unless the bound room requires a token.
            _tokenInput = LobbyUiKit.InputField(root, "Join token…");
            _tokenInput.gameObject.SetActive(false);
        }

        /// <summary>Reports the Join click with the room and the current inline token text (or "").</summary>
        private void RaiseJoin()
        {
            string token = _tokenInput != null && _tokenInput.gameObject.activeSelf
                ? _tokenInput.text
                : "";
            OnJoinClicked?.Invoke(_room, token ?? "");
        }

        /// <summary>
        /// Populates the row from <paramref name="room"/>. When <paramref name="compatible"/> is false the
        /// row is greyed and its Join button disabled; the inline token input appears only for token rooms.
        /// </summary>
        public void Bind(RoomInfo room, bool compatible)
        {
            _room = room;

            _nameLabel.text = string.IsNullOrEmpty(room.name) ? "(unnamed room)" : room.name;
            _hostLabel.text = string.IsNullOrEmpty(room.hostName) ? "" : "host: " + room.hostName;
            _gameLabel.text = room.gameId ?? "";
            _playersLabel.text = room.playerCount + "/" + room.capacity;
            _regionLabel.text = string.IsNullOrEmpty(room.region) ? "—" : room.region;
            _lockLabel.text = room.requiresToken ? "LOCKED" : "";

            string version = room.version ?? "";
            _versionLabel.text = compatible ? version : version + " (version mismatch)";

            // Grey every text element when the room is incompatible with this build.
            Color textColor = compatible ? LobbyUiKit.TextPrimary : LobbyUiKit.TextMuted;
            _nameLabel.color = textColor;
            _hostLabel.color = textColor;
            _gameLabel.color = textColor;
            _playersLabel.color = textColor;
            _regionLabel.color = textColor;
            _versionLabel.color = LobbyUiKit.TextMuted;
            _lockLabel.color = compatible ? LobbyUiKit.Accent : LobbyUiKit.TextMuted;

            _tokenInput.gameObject.SetActive(room.requiresToken);

            LobbyUiKit.SetButtonInteractable(_joinButton, compatible);
        }
    }
}
