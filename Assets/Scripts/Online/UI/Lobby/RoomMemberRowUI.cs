// CloSim Online Multiplayer — a single reusable room-member row (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. Built 100% programmatically with LobbyUiKit so no prefab/scene authoring
// is required. Visual language (alliance tint chip + green "Ready" text + local highlight) mirrors the
// game's RobotSelectDetailPanel so the lobby matches the existing style.

using Online.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Online.UI.Lobby
{
    /// <summary>
    /// One row in the room's member list: an alliance-tinted chip, the member's name, a host crown,
    /// and a role/ready status label. Pooled and re-bound by <see cref="RoomScreen"/>.
    /// </summary>
    public class RoomMemberRowUI : MonoBehaviour
    {
        private Image _rowBg;
        private Image _chip;
        private TMP_Text _nameLabel;
        private TMP_Text _crownLabel;
        private TMP_Text _statusLabel;

        /// <summary>Builds a new member row under <paramref name="parent"/> and returns its component.</summary>
        public static RoomMemberRowUI Create(Transform parent)
        {
            GameObject rowGo = LobbyUiKit.Row(parent, "MemberRow", LobbyUiKit.SpaceSm, LobbyUiKit.PadSm, TextAnchor.MiddleLeft);
            LobbyUiKit.SetSize(rowGo, -1, 48);

            // Row's own background image (Row itself only carries a layout group by default).
            var rowBg = rowGo.AddComponent<Image>();
            rowBg.color = LobbyUiKit.CardBg;

            var row = rowGo.AddComponent<RoomMemberRowUI>();
            row._rowBg = rowBg;

            // Alliance chip — a small colored square (Card gives us a tinted Image box).
            GameObject chipGo = LobbyUiKit.Card(rowGo.transform, "AllianceChip", LobbyUiKit.InactiveAlliance, 0f, 0);
            LobbyUiKit.SetSize(chipGo, 20, 20);
            row._chip = chipGo.GetComponent<Image>();

            // Name (takes the remaining width).
            row._nameLabel = LobbyUiKit.Label(rowGo.transform, "", LobbyUiKit.FontBody, TextAlignmentOptions.Left);
            LobbyUiKit.FlexibleWidth(row._nameLabel.gameObject);

            // Host crown.
            row._crownLabel = LobbyUiKit.Label(rowGo.transform, "", LobbyUiKit.FontLabel, TextAlignmentOptions.Right, LobbyUiKit.Accent);
            LobbyUiKit.SetSize(row._crownLabel.gameObject, 100, -1);

            // Role / ready status.
            row._statusLabel = LobbyUiKit.Label(rowGo.transform, "", LobbyUiKit.FontLabel, TextAlignmentOptions.Right, LobbyUiKit.TextMuted);
            LobbyUiKit.SetSize(row._statusLabel.gameObject, 130, -1);

            return row;
        }

        /// <summary>Updates every visual on this row from a member slot.</summary>
        public void Bind(RoomMemberSlot slot, bool isLocal)
        {
            if (_chip != null)
                _chip.color = LobbyUiKit.AllianceColor(slot.alliance);

            if (_nameLabel != null)
                _nameLabel.text = string.IsNullOrEmpty(slot.displayName) ? "Player" : slot.displayName;

            if (_crownLabel != null)
                _crownLabel.text = slot.isHost ? "★ HOST" : "";

            if (_statusLabel != null)
            {
                if (slot.role == MemberRole.Spectator)
                {
                    _statusLabel.text = "Spectator";
                    _statusLabel.color = LobbyUiKit.TextMuted;
                }
                else if (slot.isReady)
                {
                    _statusLabel.text = "Ready";
                    _statusLabel.color = LobbyUiKit.Accent;
                }
                else
                {
                    _statusLabel.text = "Not ready";
                    _statusLabel.color = LobbyUiKit.TextMuted;
                }
            }

            if (_rowBg != null)
                _rowBg.color = isLocal ? LobbyUiKit.CardBgAlt : LobbyUiKit.CardBg;
        }
    }
}
