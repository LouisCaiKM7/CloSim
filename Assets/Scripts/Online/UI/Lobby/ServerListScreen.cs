// CloSim Online Multiplayer — in-game Server List screen (A5b, sub-worker of A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. This is the native-Unity, in-game "Server List" (never a web page):
// it browses PUBLIC rooms from the master directory (Services.Master) and LAN rooms discovered through
// the connection façade (Services.Connection), rendering one ServerListRowUI per room.
//
// Version-mismatched rooms are greyed out and non-joinable (row disables its own Join button). Joining
// always routes through the LobbyServices façade (Services.JoinRoom) — this screen NEVER touches Mirror.

using System.Collections.Generic;
using Online.Contracts;
using Online.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Online.UI.Lobby
{
    /// <summary>
    /// Browsable Server List. Merges master-directory rooms (on demand via <see cref="Refresh"/>) and
    /// LAN-discovered rooms (streamed in while shown), deduped by address:port. Client-side filters can
    /// hide full and/or incompatible rooms.
    /// </summary>
    public class ServerListScreen : LobbyScreen
    {
        /// <inheritdoc/>
        public override string Key => LobbyScreenKeys.ServerList;

        private readonly List<RoomInfo> _rooms = new();
        private readonly List<ServerListRowUI> _rows = new();

        private Transform _listContainer;
        private TMP_Text _statusLabel;
        private Toggle _hideFullToggle;
        private Toggle _hideIncompatibleToggle;
        private Button _refreshButton;

        private bool _lanSubscribed;
        private bool _destroyed;

        // ---------------------------------------------------------------- build

        /// <inheritdoc/>
        protected override void BuildUi()
        {
            LobbyUiKit.Panel(transform, "bg", LobbyUiKit.PanelBg);

            GameObject col = LobbyUiKit.Column(transform, "root", 12f, 24, TextAnchor.UpperCenter);
            LobbyUiKit.Stretch(LobbyUiKit.RectOf(col));

            BuildHeader(col.transform);

            _statusLabel = LobbyUiKit.Label(col.transform, "", 20, TextAlignmentOptions.Left, LobbyUiKit.TextMuted);
            LobbyUiKit.SetSize(_statusLabel.gameObject, -1, 28);

            BuildFilters(col.transform);
            BuildListArea(col.transform);

            FirstSelected = _refreshButton.gameObject;
        }

        /// <summary>Title (left) + Refresh + Back.</summary>
        private void BuildHeader(Transform parent)
        {
            GameObject header = LobbyUiKit.Row(parent, "header", 12f, 0, TextAnchor.MiddleLeft);
            LobbyUiKit.SetSize(header, -1, 56);

            TMP_Text title = LobbyUiKit.Label(header.transform, "Server List", 34, TextAlignmentOptions.Left);
            LobbyUiKit.FlexibleWidth(title.gameObject, 1f);

            _refreshButton = LobbyUiKit.Button(header.transform, "Refresh", out _, 22);
            LobbyUiKit.SetSize(_refreshButton.gameObject, 160, 52);
            _refreshButton.onClick.AddListener(Refresh);

            Button backButton = LobbyUiKit.Button(header.transform, "Back", out _, 22);
            LobbyUiKit.SetSize(backButton.gameObject, 140, 52);
            backButton.onClick.AddListener(Back);
        }

        /// <summary>Client-side "Hide full" and "Hide incompatible" toggles (default off).</summary>
        private void BuildFilters(Transform parent)
        {
            GameObject filters = LobbyUiKit.Row(parent, "filters", 24f, 0, TextAnchor.MiddleLeft);
            LobbyUiKit.SetSize(filters, -1, 44);

            _hideFullToggle = LobbyUiKit.Toggle(filters.transform, "Hide full", false, out _);
            LobbyUiKit.SetSize(_hideFullToggle.gameObject, 220, 40);
            _hideFullToggle.onValueChanged.AddListener(_ => RebuildRows());

            _hideIncompatibleToggle = LobbyUiKit.Toggle(filters.transform, "Hide incompatible", false, out _);
            LobbyUiKit.SetSize(_hideIncompatibleToggle.gameObject, 300, 40);
            _hideIncompatibleToggle.onValueChanged.AddListener(_ => RebuildRows());
        }

        /// <summary>A vertically-scrolling area holding one <see cref="ServerListRowUI"/> per room.</summary>
        private void BuildListArea(Transform parent)
        {
            GameObject viewport = LobbyUiKit.Panel(parent, "ListViewport", LobbyUiKit.CardBgAlt);
            LayoutElement viewportLe = viewport.GetComponent<LayoutElement>() ?? viewport.AddComponent<LayoutElement>();
            viewportLe.flexibleHeight = 1f;

            viewport.AddComponent<RectMask2D>();
            var scroll = viewport.AddComponent<ScrollRect>();

            GameObject content = LobbyUiKit.Column(viewport.transform, "ListContent", 8f, 8, TextAnchor.UpperCenter);
            RectTransform contentRect = LobbyUiKit.RectOf(content);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = contentRect;
            scroll.viewport = LobbyUiKit.RectOf(viewport);
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            _listContainer = content.transform;
        }

        // ---------------------------------------------------------------- lifecycle

        /// <inheritdoc/>
        public override void OnShow()
        {
            SubscribeLan();
            Services.Connection.StartLanDiscovery();
            Refresh();
        }

        /// <inheritdoc/>
        public override void OnHide()
        {
            Services.Connection.StopLanDiscovery();
            UnsubscribeLan();
        }

        private void OnDestroy()
        {
            _destroyed = true;
            UnsubscribeLan();
        }

        private void SubscribeLan()
        {
            if (_lanSubscribed) return;
            Services.Connection.OnLanRoomDiscovered += HandleLanRoom;
            _lanSubscribed = true;
        }

        private void UnsubscribeLan()
        {
            if (!_lanSubscribed) return;
            // Use the existing singleton (never resurrect the service during teardown).
            LobbyServicesUnsubscribe();
            _lanSubscribed = false;
        }

        /// <summary>Detaches the LAN handler using the live singleton, guarding against teardown resurrection.</summary>
        private void LobbyServicesUnsubscribe()
        {
            var services = Online.Rooms.LobbyServices.Instance;
            if (services != null && services.Connection != null)
                services.Connection.OnLanRoomDiscovered -= HandleLanRoom;
        }

        // ---------------------------------------------------------------- data

        /// <summary>
        /// Queries the master directory and rebuilds the list. Safe to call as a UI callback: it is an
        /// <c>async void</c> that swallows client errors (the client degrades to an empty list when
        /// unconfigured) and guards every post-await UI touch with <see cref="_destroyed"/>.
        /// </summary>
        private async void Refresh()
        {
            SetStatus(Services.Master.IsConfigured
                ? "Refreshing…"
                : "Master server not configured — showing LAN rooms only.");

            var query = new RoomQuery
            {
                gameId = "",
                region = "",
                hideFull = false,
                hidePrivate = true,
                version = ""
            };

            IReadOnlyList<RoomInfo> results = null;
            try
            {
                results = await Services.Master.ListRoomsAsync(query);
            }
            catch (System.Exception)
            {
                results = null; // Defensive: the façade already degrades to empty, but never let a throw escape.
            }

            if (_destroyed) return;

            _rooms.Clear();
            if (results != null)
            {
                foreach (RoomInfo room in results)
                    MergeRoom(room);
            }

            RebuildRows();
        }

        /// <summary>Merges a LAN-discovered room into the model as it arrives (dedup by address:port).</summary>
        private void HandleLanRoom(RoomInfo room)
        {
            if (_destroyed) return;
            MergeRoom(room);
            RebuildRows();
        }

        /// <summary>Inserts or replaces a room in the model, keyed by address:port.</summary>
        private void MergeRoom(RoomInfo room)
        {
            string key = RoomKey(room);
            for (int i = 0; i < _rooms.Count; i++)
            {
                if (RoomKey(_rooms[i]) == key)
                {
                    _rooms[i] = room;
                    return;
                }
            }
            _rooms.Add(room);
        }

        private static string RoomKey(RoomInfo room) => (room.address ?? "") + ":" + room.port;

        // ---------------------------------------------------------------- rows

        /// <summary>Destroys the current rows and rebuilds them from the filtered model.</summary>
        private void RebuildRows()
        {
            if (_destroyed || _listContainer == null) return;

            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                    Destroy(_rows[i].gameObject);
            }
            _rows.Clear();

            bool hideFull = _hideFullToggle != null && _hideFullToggle.isOn;
            bool hideIncompatible = _hideIncompatibleToggle != null && _hideIncompatibleToggle.isOn;

            int shown = 0;
            foreach (RoomInfo room in _rooms)
            {
                bool compatible = NetcodeProtocol.IsCompatible(room.version, NetcodeProtocol.Version);
                if (hideIncompatible && !compatible) continue;
                if (hideFull && room.playerCount >= room.capacity) continue;

                ServerListRowUI row = ServerListRowUI.Create(_listContainer);
                row.OnJoinClicked += HandleJoin;
                row.Bind(room, compatible);
                _rows.Add(row);
                shown++;
            }

            UpdateStatus(shown);
        }

        /// <summary>Join click from a row: ignore incompatible rooms; otherwise connect and open the Room screen.</summary>
        private void HandleJoin(RoomInfo room, string token)
        {
            if (_destroyed) return;

            // Incompatible rooms are non-joinable (the row already disables its button; double-guard here).
            if (!NetcodeProtocol.IsCompatible(room.version, NetcodeProtocol.Version)) return;

            string joinToken = room.requiresToken ? (token ?? "") : "";
            Services.JoinRoom(room, joinToken);
            Go(LobbyScreenKeys.Room);
        }

        // ---------------------------------------------------------------- status

        private void SetStatus(string text)
        {
            if (_destroyed || _statusLabel == null) return;
            _statusLabel.text = text;
        }

        private void UpdateStatus(int shownCount)
        {
            if (_destroyed || _statusLabel == null) return;

            string prefix = Services.Master.IsConfigured
                ? ""
                : "Master server not configured — showing LAN rooms only. ";
            string noun = shownCount == 1 ? "room" : "rooms";
            _statusLabel.text = prefix + shownCount + " " + noun + " found.";
        }
    }
}
