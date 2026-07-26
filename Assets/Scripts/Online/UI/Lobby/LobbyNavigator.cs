// CloSim Online Multiplayer — screen navigation for the lobby (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. Mirrors MainMenuController's root-swap + history + cancel-action pattern
// (Documentation reference: UI/MainMenu/MainMenuController.cs ~227-292, 201-210) but as a standalone,
// screen-key-driven navigator so lobby screens never reference each other's types.

using System;
using System.Collections;
using System.Collections.Generic;
using UI.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Online.UI.Lobby
{
    [AddComponentMenu("CloSim/Rooms/Lobby Navigator")]
    public class LobbyNavigator : MonoBehaviour
    {
        private readonly Dictionary<string, LobbyScreen> _screens = new();
        private readonly Stack<string> _history = new();
        private LobbyScreen _current;
        private InputAction _cancelAction;

        /// <summary>Raised when the user backs out past the root screen (return to the main menu).</summary>
        public event Action OnExitRequested;

        private void Awake()
        {
            _cancelAction = new InputAction("Lobby_Cancel", InputActionType.Button);
            _cancelAction.AddBinding("<Gamepad>/buttonEast");
            _cancelAction.AddBinding("<Keyboard>/escape");
            _cancelAction.performed += OnCancelPerformed;
        }

        private void OnEnable() => _cancelAction?.Enable();
        private void OnDisable() => _cancelAction?.Disable();

        private void OnDestroy()
        {
            if (_cancelAction != null)
            {
                _cancelAction.performed -= OnCancelPerformed;
                _cancelAction.Dispose();
            }
        }

        public void Register(LobbyScreen screen)
        {
            if (screen == null) return;
            _screens[screen.Key] = screen;
            screen.gameObject.SetActive(false);
        }

        public LobbyScreen Get(string key) => _screens.TryGetValue(key, out LobbyScreen s) ? s : null;

        /// <summary>Shows a screen, pushing the current one onto the back-history.</summary>
        public void Show(string key)
        {
            if (!_screens.TryGetValue(key, out LobbyScreen target) || target == _current)
                return;

            if (_current != null)
            {
                _history.Push(_current.Key);
                _current.OnHide();
                _current.gameObject.SetActive(false);
            }

            _current = target;
            target.gameObject.SetActive(true);
            target.OnShow();
            SelectFirst(target);
        }

        /// <summary>Shows a screen as the ROOT of a fresh history (used when entering the lobby).</summary>
        public void ShowRoot(string key)
        {
            _history.Clear();
            if (_current != null)
            {
                _current.OnHide();
                _current.gameObject.SetActive(false);
                _current = null;
            }
            Show(key);
        }

        public void Back()
        {
            if (_history.Count == 0)
            {
                OnExitRequested?.Invoke();
                return;
            }

            string previousKey = _history.Pop();
            if (!_screens.TryGetValue(previousKey, out LobbyScreen previous))
            {
                OnExitRequested?.Invoke();
                return;
            }

            if (_current != null)
            {
                _current.OnHide();
                _current.gameObject.SetActive(false);
            }

            _current = previous;
            previous.gameObject.SetActive(true);
            previous.OnShow();
            SelectFirst(previous);
        }

        private void OnCancelPerformed(InputAction.CallbackContext ctx)
        {
            if (GamepadDropdown.HideAnyOpenDropdown())
                return;
            Back();
        }

        private void SelectFirst(LobbyScreen screen)
        {
            if (screen == null || screen.FirstSelected == null || EventSystem.current == null)
                return;
            StartCoroutine(SelectNextFrame(screen.FirstSelected));
        }

        private IEnumerator SelectNextFrame(GameObject target)
        {
            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            if (target != null && target.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(target);
        }
    }
}
