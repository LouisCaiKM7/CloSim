// CloSim Online Multiplayer — in-game Replays list screen. Namespace: Online.Replay.UI.
// Native-Unity, in-game "Replays" list (NEVER a web page — golden rule 3, same reasoning that renamed
// "Server Browser" to "Server List"): lists stored replays via IReplayService.ListAsync and lets the
// player pick one to watch. Built with the same Online.UI.Lobby.LobbyUiKit toolkit and the same
// LobbyScreen/LobbyNavigator base used by the multiplayer lobby, so it reads as one consistent UI system
// even though it lives on its own overlay (see ReplaysMenuController) reachable from the main menu.

using System;
using System.Collections.Generic;
using Online.Contracts.Replay;
using Online.Replay.Service;
using Online.UI.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Online.Replay.UI
{
    /// <summary>Well-known screen keys for the Replays overlay (kept local — decoupled from the lobby's).</summary>
    public static class ReplayScreenKeys
    {
        public const string List = "replays";
        public const string Playback = "replayPlayback";
    }

    /// <summary>Lists stored replays (metadata only) and routes Play clicks to the playback screen.</summary>
    public class ReplaysScreen : LobbyScreen
    {
        /// <inheritdoc/>
        public override string Key => ReplayScreenKeys.List;

        private readonly List<ReplayMetadata> _replays = new();
        private readonly List<ReplayRowUI> _rows = new();

        private Transform _listContainer;
        private TMP_Text _statusLabel;
        private Button _refreshButton;
        private bool _destroyed;

        // ADDITIVE: CompositeReplayService always includes the local on-disk store (LocalReplayService)
        // so offline/single-player replays show up with zero AWS config, in addition to the remote
        // AWS-backed store when configured. Always "configured" (see CompositeReplayService.IsConfigured).
        private IReplayService _service;
        private IReplayService Service => _service ??= new CompositeReplayService();

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

            BuildListArea(col.transform);

            FirstSelected = _refreshButton.gameObject;
        }

        private void BuildHeader(Transform parent)
        {
            GameObject header = LobbyUiKit.Row(parent, "header", 12f, 0, TextAnchor.MiddleLeft);
            LobbyUiKit.SetSize(header, -1, 56);

            TMP_Text title = LobbyUiKit.Label(header.transform, "Replays", 34, TextAlignmentOptions.Left);
            LobbyUiKit.FlexibleWidth(title.gameObject, 1f);

            _refreshButton = LobbyUiKit.Button(header.transform, "Refresh", out _, 22);
            LobbyUiKit.SetSize(_refreshButton.gameObject, 160, 52);
            _refreshButton.onClick.AddListener(Refresh);

            Button backButton = LobbyUiKit.Button(header.transform, "Back", out _, 22);
            LobbyUiKit.SetSize(backButton.gameObject, 140, 52);
            backButton.onClick.AddListener(Back);
        }

        /// <summary>A vertically-scrolling area holding one <see cref="ReplayRowUI"/> per replay.</summary>
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
        public override void OnShow() => Refresh();

        private void OnDestroy() => _destroyed = true;

        // ---------------------------------------------------------------- data

        /// <summary>
        /// Queries the replay backend and rebuilds the list. Safe to call as a UI callback: it is an
        /// <c>async void</c> that swallows client errors (the client degrades to an empty list when
        /// unconfigured) and guards every post-await UI touch with <see cref="_destroyed"/>.
        /// </summary>
        private async void Refresh()
        {
            if (!Service.IsConfigured)
            {
                SetStatus("Replay service not configured — no replays available yet.");
                _replays.Clear();
                RebuildRows();
                return;
            }

            SetStatus("Loading…");

            IReadOnlyList<ReplayMetadata> results = null;
            try
            {
                results = await Service.ListAsync(new ReplayQuery { limit = 50 });
            }
            catch (Exception)
            {
                results = null; // Defensive: the façade already degrades to empty, but never let a throw escape.
            }

            if (_destroyed) return;

            _replays.Clear();
            if (results != null)
                _replays.AddRange(results);

            RebuildRows();
        }

        private void RebuildRows()
        {
            if (_destroyed || _listContainer == null) return;

            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                    Destroy(_rows[i].gameObject);
            }
            _rows.Clear();

            foreach (ReplayMetadata replay in _replays)
            {
                ReplayRowUI row = ReplayRowUI.Create(_listContainer);
                row.OnPlayClicked += HandlePlay;
                row.Bind(replay);
                _rows.Add(row);
            }

            UpdateStatus();
        }

        private void HandlePlay(ReplayMetadata replay)
        {
            if (_destroyed || string.IsNullOrEmpty(replay.replayId)) return;

            if (Navigator?.Get(ReplayScreenKeys.Playback) is ReplayPlaybackScreen playback)
                playback.SetPendingReplay(replay.replayId);

            Go(ReplayScreenKeys.Playback);
        }

        // ---------------------------------------------------------------- status

        private void SetStatus(string text)
        {
            if (_destroyed || _statusLabel == null) return;
            _statusLabel.text = text;
        }

        private void UpdateStatus()
        {
            if (_destroyed || _statusLabel == null) return;
            int count = _replays.Count;
            _statusLabel.text = count + (count == 1 ? " replay found." : " replays found.");
        }
    }
}
