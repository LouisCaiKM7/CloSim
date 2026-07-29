// CloSim Online Multiplayer — the in-room lobby screen (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. The heart of the lobby: shows the roster, the host-picked mode, and the
// local player's alliance / ready / start controls. Built 100% programmatically with LobbyUiKit.
//
// The room facade (Services.Room) may be null for a frame or two after Create/Join while the networked
// RoomService spawns (or while the client is still connecting), so this screen polls for it in Update
// and subscribes/seats the local member the moment it appears — then unsubscribes symmetrically.

using System.Collections.Generic;
using Online.Contracts;
using Online.Rooms;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Online.UI.Lobby
{
    /// <summary>The in-room lobby: roster, mode selector, and local alliance/ready/start controls.</summary>
    public class RoomScreen : LobbyScreen
    {
        public override string Key => LobbyScreenKeys.Room;

        // Cached room reference (Services.Room can flip null <-> non-null as the network spawns/drops).
        private ILobbyRoom _room;
        private bool _joinRequested;

        // Header / status.
        private TMP_Text _roomNameLabel;
        private TMP_Text _statusLabel;

        // Mode.
        private ModeSelectorUI _modeSelector;

        // Members.
        private Transform _membersContainer;
        private readonly List<RoomMemberRowUI> _rows = new();

        // Local controls.
        private Button _blueButton;
        private Button _redButton;
        private Button _spectateButton;
        private Button _readyButton;
        private TMP_Text _readyLabel;
        private Button _startButton;

        // ---------------------------------------------------------------- build

        protected override void BuildUi()
        {
            LobbyUiKit.Panel(transform, "bg", LobbyUiKit.PanelBg);

            GameObject root = LobbyUiKit.Column(transform, "root", LobbyUiKit.SpaceMd, LobbyUiKit.PadLg, TextAnchor.UpperCenter);
            LobbyUiKit.Stretch(LobbyUiKit.RectOf(root));

            // Header: room name (left, growing) + Leave (right).
            GameObject header = LobbyUiKit.HeaderRow(root.transform, "Room", out _roomNameLabel);
            Button leave = LobbyUiKit.DangerButton(header.transform, "Leave");
            LobbyUiKit.SetSize(leave.gameObject, 160, -1);
            leave.onClick.AddListener(OnLeaveClicked);

            LobbyUiKit.Divider(root.transform);

            // Status line (muted).
            _statusLabel = LobbyUiKit.Label(root.transform, "Connecting…", LobbyUiKit.FontLabel,
                TextAlignmentOptions.Left, LobbyUiKit.TextMuted);
            LobbyUiKit.SetSize(_statusLabel.gameObject, -1, 28);

            // Mode selector (host-only interactive).
            _modeSelector = ModeSelectorUI.Create(root.transform);
            _modeSelector.OnShapeChosen += OnShapeChosen;

            // Members area — grows to fill remaining vertical space so the roster list has room without
            // pushing the controls row off-screen or overlapping it.
            GameObject membersCard = LobbyUiKit.Card(root.transform, "Members", LobbyUiKit.CardBg, LobbyUiKit.SpaceSm, LobbyUiKit.PadSm);
            LobbyUiKit.FlexibleHeight(membersCard);
            _membersContainer = membersCard.transform;

            // Local controls: alliance + ready + (host) start.
            GameObject controls = LobbyUiKit.Row(root.transform, "controls", LobbyUiKit.SpaceSm, 0, TextAnchor.MiddleCenter);
            LobbyUiKit.SetSize(controls, -1, LobbyUiKit.ButtonHeight + 8f);

            _blueButton = LobbyUiKit.Button(controls.transform, "Blue");
            LobbyUiKit.SetSize(_blueButton.gameObject, 140, -1);
            LobbyUiKit.TintButton(_blueButton, LobbyUiKit.BlueAlliance);
            _blueButton.onClick.AddListener(OnBlueClicked);

            _redButton = LobbyUiKit.Button(controls.transform, "Red");
            LobbyUiKit.SetSize(_redButton.gameObject, 140, -1);
            LobbyUiKit.TintButton(_redButton, LobbyUiKit.RedAlliance);
            _redButton.onClick.AddListener(OnRedClicked);

            _spectateButton = LobbyUiKit.SecondaryButton(controls.transform, "Spectate");
            LobbyUiKit.SetSize(_spectateButton.gameObject, 160, -1);
            _spectateButton.onClick.AddListener(OnSpectateClicked);

            _readyButton = LobbyUiKit.PrimaryButton(controls.transform, "Ready", out _readyLabel);
            LobbyUiKit.SetSize(_readyButton.gameObject, 180, -1);
            _readyButton.onClick.AddListener(OnReadyClicked);

            _startButton = LobbyUiKit.PrimaryButton(controls.transform, "Start Match");
            LobbyUiKit.SetSize(_startButton.gameObject, 200, -1);
            _startButton.onClick.AddListener(OnStartClicked);
            _startButton.gameObject.SetActive(false);

            FirstSelected = _readyButton.gameObject;
        }

        // ---------------------------------------------------------------- lifecycle

        public override void OnShow()
        {
            SyncRoomRef();
            RefreshRoomName();
            RefreshControls();
        }

        public override void OnHide()
        {
            if (_room != null)
            {
                Unsubscribe(_room);
                _room = null;
            }
        }

        private void OnDestroy()
        {
            if (_room != null)
            {
                Unsubscribe(_room);
                _room = null;
            }
        }

        private void Update()
        {
            SyncRoomRef();

            if (_room != null)
            {
                // Seat the local member once, if we are a joining client not yet in the roster.
                if (!_joinRequested && !_room.IsLocalHost && !_room.TryGetLocalMember(out _))
                {
                    _room.JoinAsLocalMember(Services.LocalPlayerName);
                    _joinRequested = true;
                }

                RefreshControls();
            }
        }

        /// <summary>Reconciles the cached room ref with Services.Room, (un)subscribing as it changes.</summary>
        private void SyncRoomRef()
        {
            ILobbyRoom current = Room;
            if (ReferenceEquals(current, _room))
                return;

            if (_room != null)
                Unsubscribe(_room);

            _room = current;

            if (_room != null)
            {
                Subscribe(_room);
                _joinRequested = false;
                RefreshRoomName();
                RefreshMembers(_room.Members);
                _modeSelector.Reflect(_room.MatchConfig);
                RefreshControls();
            }
            else
            {
                _statusLabel.text = "Disconnected";
                RefreshMembers(null);
                RefreshControls();
            }
        }

        private void Subscribe(ILobbyRoom room)
        {
            room.OnRoomChanged += HandleRoomChanged;
            room.OnMembersChanged += HandleMembersChanged;
            room.OnMatchConfigChanged += HandleMatchConfigChanged;
            room.OnMatchStarting += HandleMatchStarting;
        }

        private void Unsubscribe(ILobbyRoom room)
        {
            room.OnRoomChanged -= HandleRoomChanged;
            room.OnMembersChanged -= HandleMembersChanged;
            room.OnMatchConfigChanged -= HandleMatchConfigChanged;
            room.OnMatchStarting -= HandleMatchStarting;
        }

        // ---------------------------------------------------------------- room events

        private void HandleRoomChanged(RoomInfo info)
        {
            RefreshRoomName();
            RefreshControls();
        }

        private void HandleMembersChanged(IReadOnlyList<RoomMemberSlot> members)
        {
            RefreshMembers(members);
            RefreshControls();
        }

        private void HandleMatchConfigChanged(NetworkMatchConfig config)
        {
            _modeSelector.Reflect(config);
            RefreshControls();
        }

        private void HandleMatchStarting()
        {
            _statusLabel.text = "Starting match…";
        }

        // ---------------------------------------------------------------- refresh

        private void RefreshRoomName()
        {
            if (_roomNameLabel == null) return;
            string roomName = _room != null ? _room.CurrentRoom.name : null;
            _roomNameLabel.text = string.IsNullOrEmpty(roomName) ? "Room" : roomName;
        }

        /// <summary>Rebuilds the row pool when the count changes; otherwise re-binds in place.</summary>
        private void RefreshMembers(IReadOnlyList<RoomMemberSlot> members)
        {
            if (_membersContainer == null) return;

            int count = members?.Count ?? 0;

            if (count != _rows.Count)
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i] != null)
                        Destroy(_rows[i].gameObject);
                }
                _rows.Clear();

                for (int i = 0; i < count; i++)
                    _rows.Add(RoomMemberRowUI.Create(_membersContainer));
            }

            int localConn = _room != null ? _room.LocalConnectionId : -1;
            for (int i = 0; i < count; i++)
            {
                RoomMemberSlot slot = members[i];
                bool isLocal = _room != null && slot.connectionId == localConn;
                _rows[i].Bind(slot, isLocal);
            }
        }

        /// <summary>Updates status line, the ready-button label, and host-only start-button gating.</summary>
        private void RefreshControls()
        {
            if (_room == null)
            {
                if (_statusLabel != null) _statusLabel.text = "Disconnected";
                if (_readyLabel != null) _readyLabel.text = "Ready";
                _modeSelector?.SetInteractable(false);
                if (_startButton != null)
                {
                    _startButton.gameObject.SetActive(false);
                    LobbyUiKit.SetButtonInteractable(_startButton, false);
                }
                return;
            }

            bool isHost = _room.IsLocalHost;
            _modeSelector?.SetInteractable(isHost);
            if (_startButton != null) _startButton.gameObject.SetActive(isHost);

            bool seated = _room.TryGetLocalMember(out RoomMemberSlot me);
            if (_readyLabel != null)
                _readyLabel.text = seated && me.isReady ? "Ready ✓" : "Ready";

            bool canStart = _room.CanStartMatch(out string reason);
            if (isHost && _startButton != null)
                LobbyUiKit.SetButtonInteractable(_startButton, canStart);

            if (_statusLabel == null) return;

            if (_room.State == RoomState.Starting || _room.State == RoomState.InMatch)
                _statusLabel.text = "Starting match…";
            else if (isHost)
                _statusLabel.text = canStart ? "Ready to start." : reason;
            else
                _statusLabel.text = seated ? "In lobby" : "Connecting…";
        }

        // ---------------------------------------------------------------- control handlers

        private void OnLeaveClicked()
        {
            Services.Disconnect();
            Back();
        }

        private void OnBlueClicked()
        {
            if (_room == null) return;
            _room.LocalSetRole(MemberRole.Player);
            _room.LocalSetAlliance(RoomAlliance.Blue);
        }

        private void OnRedClicked()
        {
            if (_room == null) return;
            _room.LocalSetRole(MemberRole.Player);
            _room.LocalSetAlliance(RoomAlliance.Red);
        }

        private void OnSpectateClicked()
        {
            if (_room == null) return;
            _room.LocalSetRole(MemberRole.Spectator);
        }

        private void OnReadyClicked()
        {
            if (_room == null) return;
            bool current = _room.TryGetLocalMember(out RoomMemberSlot me) && me.isReady;
            _room.LocalSetReady(!current);
        }

        private void OnStartClicked()
        {
            if (_room == null || !_room.IsLocalHost) return;
            _room.HostStartMatch();
        }

        private void OnShapeChosen(int blueCount, int redCount)
        {
            if (_room == null || !_room.IsLocalHost) return;

            NetworkMatchConfig current = _room.MatchConfig;
            var config = new NetworkMatchConfig
            {
                blueCount = blueCount,
                redCount = redCount,
                gameId = current.gameId,
                sceneName = current.sceneName,
                humanPlayerType = current.humanPlayerType,
                allowSpectators = current.allowSpectators
            };
            _room.HostSetMatchConfig(config);
        }
    }
}
