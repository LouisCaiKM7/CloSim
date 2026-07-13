using System.Collections;
using System.Collections.Generic;
using Audio;
using Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace UI.RobotSelection
{
    public class RobotSelectCursorController : MonoBehaviour
    {
        private static readonly Color[] PlayerColors =
        {
            new Color(0.25f, 0.6f, 1f),
            new Color(1f, 0.25f, 0.25f),
            new Color(0.3f, 1f, 0.4f),
            new Color(1f, 0.9f, 0.2f),
        };

        private const string DevicePrefsPrefix = "Controls_PlayerDevice_";
        private const string GamepadPrefsPrefix = "Controls_PlayerGamepadIndex_";

        [Header("References")] [SerializeField]
        private RobotGridUI grid;

        [SerializeField] private LoadMatch loadMatch;

        [Header("Top Bar Focus")]
        [Tooltip(
            "The first selectable in the top bar. Player 1 starts here and uses normal Unity UI navigation while focused here.")]
        [SerializeField]
        private Selectable topBarFirstSelectable;

        [Header("Grid Layout (must match GridLayoutGroup)")]
        [Tooltip("How many columns the GridLayoutGroup uses (Constraint Count).")]
        [SerializeField]
        private int columnCount = 9;

        [Header("Navigation")] [Tooltip("Seconds before held-direction starts repeating.")] [SerializeField]
        private float repeatDelay = 0.15f;

        [Tooltip("Seconds between repeats while direction is held.")] [SerializeField]
        private float repeatInterval = 0.08f;

        [FormerlySerializedAs("stickDeadzone")] [Tooltip("Analog stick dead-zone (0-1).")] [SerializeField]
        private float stickDeadZone = 0.5f;

        private readonly List<PlayerCursorState> _cursors = new();
        private readonly DeviceKind[] _preferredKinds = new DeviceKind[4];
        private readonly int[] _preferredGamepadIndexes = new int[4];

        private bool _active;
        private int _requiredPlayerCount = 1;

        public event System.Action<int> OnPlayerJoined;
        public event System.Action<int> OnPlayerUnjoined;
        public event System.Action<int, int> OnPlayerHoverChanged;
        public event System.Action<int, int> OnPlayerLockedIn;
        public event System.Action<int> OnPlayerUnlocked;
        public event System.Action<int> OnPlayerReadyChanged;
        public event System.Action<int, Vector2, bool, bool> OnPlayerDetailInput;
        
        public void ActivateSelectScreen(int requiredPlayerCount)
        {
            Deactivate();
            _active = true;
            _requiredPlayerCount = Mathf.Clamp(requiredPlayerCount, 1, 4);

            CachePreferredDevices();

            if (grid != null)
            {
                grid.OnTileClicked -= OnGridTileClicked;
                grid.OnTileClicked += OnGridTileClicked;
            }

            JoinPlayer(0, ResolveConfiguredDeviceForPlayer(0), focusTopBar: topBarFirstSelectable != null);
        }

        public void Activate(int playerCount)
        {
            ActivateSelectScreen(playerCount);
        }

        public void Deactivate()
        {
            _active = false;

            if (grid != null)
            {
                grid.OnTileClicked -= OnGridTileClicked;

                foreach (var cursor in _cursors)
                {
                    if (cursor != null)
                        grid.ClearPlayer(cursor.PlayerIndex, cursor.Color);
                }
            }

            foreach (var t in _cursors)
                t.Dispose();

            _cursors.Clear();
        }

        public bool IsPlayerJoined(int playerIndex) => GetCursor(playerIndex) != null;
        public bool IsPlayerLocked(int playerIndex) => GetCursor(playerIndex)?.IsLocked == true;
        public bool IsPlayerReady(int playerIndex) => GetCursor(playerIndex)?.IsReady == true;
        public bool IsPlayerFocusedOnTopBar(int playerIndex) => GetCursor(playerIndex)?.FocusOnTopBar == true;

        public DeviceKind GetPreferredDeviceKind(int playerIndex)
        {
            playerIndex = Mathf.Clamp(playerIndex, 0, 3);
            return _preferredKinds[playerIndex];
        }

        public int GetHoverRobotIndex(int playerIndex)
        {
            PlayerCursorState cursor = GetCursor(playerIndex);
            return cursor?.RobotIndex ?? 0;
        }

        public int GetLockedRobotIndex(int playerIndex)
        {
            PlayerCursorState cursor = GetCursor(playerIndex);
            return cursor is { IsLocked: true } ? cursor.RobotIndex : -1;
        }

        public void SetPlayerReady(int playerIndex, bool ready)
        {
            PlayerCursorState cursor = GetCursor(playerIndex);
            if (cursor == null || !cursor.IsLocked)
                return;

            if (cursor.IsReady == ready)
                return;

            cursor.IsReady = ready;

            if (!ready)
                cursor.FocusOnTopBar = false;

            OnPlayerReadyChanged?.Invoke(playerIndex);
        }
        
        public void FocusPlayerOneOnTopBar(Selectable targetSelectable = null)
        {
            PlayerCursorState cursor = GetCursor(0);
            if (cursor == null)
                return;

            cursor.FocusOnTopBar = true;
            cursor.ResetRepeat();
            
            if (grid != null)
                grid.SetHoverForPlayer(cursor.PlayerIndex, -1, cursor.Color);

            Selectable target = targetSelectable != null
                ? targetSelectable
                : topBarFirstSelectable;

            StartCoroutine(SelectTopBarNextFrame(target));
        }

        public bool AreAllRequiredPlayersReady()
        {
            for (int i = 0; i < _requiredPlayerCount; i++)
            {
                PlayerCursorState cursor = GetCursor(i);
                if (cursor == null || !cursor.IsLocked || !cursor.IsReady)
                    return false;
            }

            return _requiredPlayerCount > 0;
        }

        private void Update()
        {
            if (!_active)
                return;

            PollPendingPlayerBegins();

            foreach (var t in _cursors)
                TickCursor(t);
        }

        private void CachePreferredDevices()
        {
            for (int i = 0; i < 4; i++)
            {
                _preferredKinds[i] = ReadPreferredDeviceKind(i);
                _preferredGamepadIndexes[i] = ReadPreferredGamepadIndex(i);
            }
        }

        private DeviceKind ReadPreferredDeviceKind(int playerIndex)
        {
            if (loadMatch != null)
                return loadMatch.GetPlayerPreferredDeviceKind(playerIndex);

            int value = PlayerPrefs.GetInt(GetDevicePrefsKey(playerIndex), 2);
            value = Mathf.Clamp(value, 1, 2);
            return value == 2 ? DeviceKind.Gamepad : DeviceKind.Keyboard;
        }

        private int ReadPreferredGamepadIndex(int playerIndex)
        {
            if (loadMatch != null)
                return loadMatch.GetPlayerPreferredGamepadIndex(playerIndex);

            int maxIndex = Mathf.Max(0, Gamepad.all.Count - 1);
            return Mathf.Clamp(PlayerPrefs.GetInt(GetGamepadPrefsKey(playerIndex), playerIndex), 0, maxIndex);
        }

        private string GetDevicePrefsKey(int playerIndex)
        {
            return $"{DevicePrefsPrefix}{Mathf.Clamp(playerIndex, 0, 3)}";
        }

        private string GetGamepadPrefsKey(int playerIndex)
        {
            return $"{GamepadPrefsPrefix}{Mathf.Clamp(playerIndex, 0, 3)}";
        }

        private InputDevice ResolveConfiguredDeviceForPlayer(int playerIndex)
        {
            playerIndex = Mathf.Clamp(playerIndex, 0, 3);

            if (_preferredKinds[playerIndex] == DeviceKind.Gamepad)
            {
                int gamepadIndex = _preferredGamepadIndexes[playerIndex];
                if (gamepadIndex >= 0 && gamepadIndex < Gamepad.all.Count)
                    return Gamepad.all[gamepadIndex];

                Debug.LogWarning(
                    $"Player {playerIndex + 1} is configured for Gamepad {gamepadIndex + 1}, but it is not connected.",
                    this);
                return null;
            }

            return Keyboard.current;
        }

        private void PollPendingPlayerBegins()
        {
            if (_requiredPlayerCount <= 1)
                return;
            
            for (int playerIndex = 1; playerIndex < _requiredPlayerCount; playerIndex++)
            {
                if (IsPlayerJoined(playerIndex))
                    continue;

                InputDevice device = ResolveConfiguredDeviceForPlayer(playerIndex);
                if (device == null)
                    continue;

                if (!ReadConfirmFromDevice(device))
                    continue;

                if (DeviceAlreadyJoined(device))
                {
                    Debug.LogWarning(
                        $"Player {playerIndex + 1} cannot join because {device.displayName} is already bound to another player.",
                        this);
                    AudioManager.Instance?.PlayError();
                    continue;
                }

                JoinPlayer(playerIndex, device, focusTopBar: false);
            }
        }

        private bool DeviceAlreadyJoined(InputDevice device)
        {
            if (device == null)
                return false;

            foreach (var t in _cursors)
            {
                if (t.Device == device)
                    return true;
            }

            return false;
        }

        private void JoinPlayer(int playerIndex, InputDevice device, bool focusTopBar)
        {
            playerIndex = Mathf.Clamp(playerIndex, 0, 3);

            if (GetCursor(playerIndex) != null)
                return;

            device ??= ResolveConfiguredDeviceForPlayer(playerIndex);

            PlayerCursorState cursor = new PlayerCursorState(playerIndex, device, PlayerColors[playerIndex])
            {
                FocusOnTopBar = playerIndex == 0 && focusTopBar,
                WaitForConfirmRelease = true
            };

            _cursors.Add(cursor);

            if (grid != null)
            {
                if (cursor.FocusOnTopBar)
                    grid.SetHoverForPlayer(playerIndex, -1, cursor.Color);
                else
                    grid.SetHoverForPlayer(playerIndex, cursor.RobotIndex, cursor.Color);
            }

            OnPlayerJoined?.Invoke(playerIndex);
            OnPlayerHoverChanged?.Invoke(playerIndex, cursor.RobotIndex);

            if (cursor.FocusOnTopBar)
                StartCoroutine(SelectTopBarNextFrame());
            else
                ScrollToCursor(cursor);

            AudioManager.Instance?.PlayConfirm();
        }

        private void UnjoinPlayer(PlayerCursorState cursor)
        {
            if (cursor == null || cursor.PlayerIndex == 0)
                return;

            if (grid != null)
                grid.ClearPlayer(cursor.PlayerIndex, cursor.Color);

            int playerIndex = cursor.PlayerIndex;
            cursor.Dispose();
            _cursors.Remove(cursor);

            OnPlayerUnjoined?.Invoke(playerIndex);
            AudioManager.Instance?.PlayBack();
        }

        private IEnumerator SelectTopBarNextFrame()
        {
            yield return SelectTopBarNextFrame(topBarFirstSelectable);
        }

        private IEnumerator SelectTopBarNextFrame(Selectable targetSelectable)
        {
            yield return null;

            if (!_active || targetSelectable == null)
                yield break;

            EventSystem es = EventSystem.current;
            if (es == null)
                yield break;

            es.SetSelectedGameObject(null);
            yield return null;
            es.SetSelectedGameObject(targetSelectable.gameObject);
        }

        private void OnGridTileClicked(int robotIndex)
        {
            if (!_active || grid == null)
                return;

            PlayerCursorState cursor = GetCursor(0);
            if (cursor == null || cursor.IsReady)
                return;

            robotIndex = Mathf.Clamp(robotIndex, 0, grid.RobotCount - 1);

            cursor.FocusOnTopBar = false;
            cursor.IsLocked = false;
            cursor.IsReady = false;

            EventSystem es = EventSystem.current;
            if (es != null)
                es.SetSelectedGameObject(null);

            cursor.RobotIndex = robotIndex;
            grid.SetHoverForPlayer(cursor.PlayerIndex, cursor.RobotIndex, cursor.Color);
            OnPlayerHoverChanged?.Invoke(cursor.PlayerIndex, cursor.RobotIndex);

            ScrollToCursor(cursor);
            LockCursor(cursor);
        }

        private void ScrollToCursor(PlayerCursorState cursor)
        {
            if (cursor == null || grid == null || cursor.FocusOnTopBar)
                return;

            grid.EnsureVisible(cursor.RobotIndex);
        }

        private void TickCursor(PlayerCursorState cursor)
        {
            if (cursor == null)
                return;

            if (cursor.Device == null)
            {
                cursor.Device = ResolveConfiguredDeviceForPlayer(cursor.PlayerIndex);
                if (cursor.Device == null)
                    return;
            }
            
            if (cursor.WaitForConfirmRelease)
            {
                if (IsConfirmHeld(cursor.Device))
                    return;

                cursor.WaitForConfirmRelease = false;
            }

            Vector2 nav = ReadNavigationVector(cursor);
            bool confirmPressed = ReadConfirm(cursor);
            bool cancelPressed = ReadCancel(cursor);

            if (cancelPressed)
            {
                HandleCancel(cursor);
                return;
            }

            if (cursor.IsReady)
                return;

            if (cursor.IsLocked)
            {
                OnPlayerDetailInput?.Invoke(cursor.PlayerIndex, nav, confirmPressed, false);
                return;
            }

            if (cursor.FocusOnTopBar)
            {
                if (IsDropdownPopupNavigationActive())
                {
                    cursor.ResetRepeat();
                    return;
                }

                bool wantsGrid = nav.y < -0.5f;

                if (wantsGrid && cursor.ConsumeRepeat(Time.unscaledDeltaTime, repeatDelay, repeatInterval))
                {
                    cursor.FocusOnTopBar = false;

                    EventSystem es = EventSystem.current;
                    if (es != null)
                        es.SetSelectedGameObject(null);

                    grid.SetHoverForPlayer(cursor.PlayerIndex, cursor.RobotIndex, cursor.Color);
                    ScrollToCursor(cursor);
                    AudioManager.Instance?.PlayHover();
                }

                if (!wantsGrid)
                    cursor.ResetRepeat();

                return;
            }

            if (confirmPressed)
            {
                LockCursor(cursor);
                return;
            }

            HandleGridNavigation(cursor, nav);
        }

        private void HandleCancel(PlayerCursorState cursor)
        {
            if (cursor == null)
                return;

            if (cursor.IsReady)
            {
                cursor.IsReady = false;
                cursor.FocusOnTopBar = false;

                if (grid != null)
                {
                    grid.SetHoverForPlayer(cursor.PlayerIndex, cursor.RobotIndex, cursor.Color);
                    grid.SetLockForPlayer(cursor.PlayerIndex, cursor.RobotIndex, cursor.Color);
                }

                OnPlayerReadyChanged?.Invoke(cursor.PlayerIndex);
                AudioManager.Instance?.PlayBack();
                return;
            }

            if (cursor.FocusOnTopBar)
                return;
            if (cursor.IsReady)
            {
                cursor.IsReady = false;
                OnPlayerReadyChanged?.Invoke(cursor.PlayerIndex);
                AudioManager.Instance?.PlayBack();
                return;
            }

            if (cursor.IsLocked)
            {
                cursor.IsLocked = false;
                cursor.IsReady = false;

                if (grid != null)
                {
                    grid.SetLockForPlayer(cursor.PlayerIndex, -1, cursor.Color);
                    grid.SetHoverForPlayer(cursor.PlayerIndex, cursor.RobotIndex, cursor.Color);
                }

                OnPlayerUnlocked?.Invoke(cursor.PlayerIndex);
                AudioManager.Instance?.PlayBack();
                return;
            }

            if (cursor.PlayerIndex == 0)
            {
                cursor.FocusOnTopBar = true;
                cursor.ResetRepeat();

                if (grid != null)
                {
                    grid.SetHoverForPlayer(cursor.PlayerIndex, -1, cursor.Color);
                    grid.SetLockForPlayer(cursor.PlayerIndex, -1, cursor.Color);
                }

                StartCoroutine(SelectTopBarNextFrame());
                AudioManager.Instance?.PlayBack();
                return;
            }

            UnjoinPlayer(cursor);
        }

        private void HandleGridNavigation(PlayerCursorState cursor, Vector2 nav)
        {
            int moveX = Mathf.RoundToInt(Mathf.Clamp(nav.x, -1f, 1f));
            int moveY = Mathf.RoundToInt(Mathf.Clamp(nav.y, -1f, 1f));
            bool wantsMove = moveX != 0 || moveY != 0;

            if (!wantsMove)
            {
                cursor.ResetRepeat();
                return;
            }

            if (!cursor.ConsumeRepeat(Time.unscaledDeltaTime, repeatDelay, repeatInterval))
                return;

            int current = cursor.RobotIndex;
            int total = grid != null ? grid.RobotCount : 0;
            if (total <= 0)
                return;

            int cols = Mathf.Max(1, columnCount);
            int next = current;

            if (moveX != 0)
            {
                next = Mathf.Clamp(current + moveX, 0, total - 1);
            }
            else if (moveY != 0)
            {
                int candidate = current - moveY * cols;

                if (candidate < 0 && moveY > 0)
                {
                    if (cursor.PlayerIndex != 0)
                    {
                        cursor.ResetRepeat();
                        AudioManager.Instance?.PlayError();
                        return;
                    }

                    cursor.FocusOnTopBar = true;
                    cursor.ResetRepeat();
                    grid.SetHoverForPlayer(cursor.PlayerIndex, -1, cursor.Color);
                    StartCoroutine(SelectTopBarNextFrame());
                    AudioManager.Instance?.PlayHover();
                    return;
                }

                next = Mathf.Clamp(candidate, 0, total - 1);
            }

            if (next == current)
                return;

            cursor.RobotIndex = next;
            grid.SetHoverForPlayer(cursor.PlayerIndex, next, cursor.Color);
            OnPlayerHoverChanged?.Invoke(cursor.PlayerIndex, next);
            ScrollToCursor(cursor);
            AudioManager.Instance?.PlayHover();
        }

        private static bool IsDropdownPopupNavigationActive()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
                return false;

            GameObject selected = es.currentSelectedGameObject;
            if (selected == null)
                return false;

            if (selected.GetComponentInParent<Toggle>(true) != null &&
                HasParentNamed(selected.transform, "Dropdown List"))
            {
                return true;
            }
            
            return HasParentNamed(selected.transform, "Dropdown List");
        }
        
        private static bool IsConfirmHeld(InputDevice device)
        {
            switch (device)
            {
                case Gamepad gamepad:
                    return gamepad.buttonSouth.isPressed;

                case Keyboard keyboard:
                    return keyboard.enterKey.isPressed || keyboard.spaceKey.isPressed;
            }

            return false;
        }

        private static bool HasParentNamed(Transform transform, string targetName)
        {
            while (transform != null)
            {
                if (transform.name.Contains(targetName))
                    return true;

                transform = transform.parent;
            }

            return false;
        }

        private void LockCursor(PlayerCursorState cursor)
        {
            if (cursor == null || cursor.IsLocked || grid == null || grid.RobotCount <= 0)
                return;

            cursor.RobotIndex = Mathf.Clamp(cursor.RobotIndex, 0, grid.RobotCount - 1);
            cursor.IsLocked = true;
            cursor.IsReady = false;
            cursor.FocusOnTopBar = false;

            grid.SetLockForPlayer(cursor.PlayerIndex, cursor.RobotIndex, cursor.Color);
            OnPlayerLockedIn?.Invoke(cursor.PlayerIndex, cursor.RobotIndex);
            AudioManager.Instance?.PlayConfirm();
        }

        private Vector2 ReadNavigationVector(PlayerCursorState cursor)
        {
            if (cursor == null || cursor.Device == null)
                return Vector2.zero;

            switch (cursor.Device)
            {
                case Gamepad gamepad:
                {
                    Vector2 rightStick = gamepad.rightStick.ReadValue();
                    Vector2 leftStick = gamepad.leftStick.ReadValue();
                    Vector2 dpad = gamepad.dpad.ReadValue();

                    Vector2 result = dpad.sqrMagnitude > 0.01f
                        ? dpad
                        : rightStick.sqrMagnitude > 0.01f
                            ? rightStick
                            : leftStick;

                    return result.magnitude < stickDeadZone ? Vector2.zero : result;
                }

                case Keyboard keyboard:
                {
                    float kx = 0f;
                    float ky = 0f;

                    if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed)
                        kx -= 1f;
                    if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed)
                        kx += 1f;
                    if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed)
                        ky += 1f;
                    if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed)
                        ky -= 1f;

                    return new Vector2(kx, ky);
                }
            }

            return Vector2.zero;
        }

        private bool ReadConfirm(PlayerCursorState cursor)
        {
            return cursor != null && ReadConfirmFromDevice(cursor.Device);
        }

        private bool ReadCancel(PlayerCursorState cursor)
        {
            if (cursor?.Device == null)
                return false;

            switch (cursor.Device)
            {
                case Gamepad gamepad:
                    return gamepad.buttonEast.wasPressedThisFrame;

                case Keyboard keyboard:
                    return keyboard.escapeKey.wasPressedThisFrame;
            }

            return false;
        }

        private static bool ReadConfirmFromDevice(InputDevice device)
        {
            switch (device)
            {
                case Gamepad gamepad:
                    return gamepad.buttonSouth.wasPressedThisFrame;

                case Keyboard keyboard:
                    return keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame;
            }

            return false;
        }

        private PlayerCursorState GetCursor(int playerIndex)
        {
            foreach (var t in _cursors)
            {
                if (t.PlayerIndex == playerIndex)
                    return t;
            }

            return null;
        }

        private sealed class PlayerCursorState
        {
            public readonly int PlayerIndex;
            public readonly Color Color;
            public InputDevice Device;
            public int RobotIndex;
            public bool IsLocked;
            public bool IsReady;
            public bool FocusOnTopBar;
            public bool WaitForConfirmRelease;
            

            private InputActionAsset _actions;

            private float _repeatTimer;
            private bool _repeatFired;

            public PlayerCursorState(int playerIndex, InputDevice device, Color color)
            {
                PlayerIndex = playerIndex;
                Device = device;
                Color = color;
                RobotIndex = 0;
                IsLocked = false;
                IsReady = false;
            }

            public bool ConsumeRepeat(float deltaTime, float delay, float interval)
            {
                if (!_repeatFired)
                {
                    _repeatFired = true;
                    _repeatTimer = 0f;
                    return true;
                }

                _repeatTimer += deltaTime;

                float threshold = _repeatTimer <= delay ? delay : interval;

                if (_repeatTimer >= threshold)
                {
                    _repeatTimer -= threshold;
                    return true;
                }

                return false;
            }

            public void ResetRepeat()
            {
                _repeatFired = false;
                _repeatTimer = 0f;
            }

            public void Dispose()
            {
                if (_actions != null)
                {
                    _actions.Disable();
                    Destroy(_actions);
                }

                _actions = null;
            }
        }
    }
}