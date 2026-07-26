using Audio;
using DG.Tweening;
using InputController;
using UI.Components;
using UI.Transitions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UI.MainMenu
{
    public class MainMenuController : MonoBehaviour
    {
        [Header("Menu Roots")]
        [SerializeField] private GameObject mainRoot;
        [SerializeField] private GameObject gameSelectRoot;
        [SerializeField] private GameObject settingsRoot;
        [SerializeField] private GameObject creditsRoot;

        [Header("Main Buttons")]
        [SerializeField] private Button playButton;
        [Tooltip("Optional. Opens the online multiplayer lobby (native in-game overlay). " +
                 "Leave unassigned to hide multiplayer.")]
        [SerializeField] private Button multiplayerButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button creditsButton;
        [SerializeField] private Button quitButton;

        [Header("Back Buttons")]
        [SerializeField] private Button gameSelectBackButton;
        [SerializeField] private Button settingsBackButton;
        [SerializeField] private Button creditsBackButton;

        [Header("Controller Navigation")]
        [Tooltip("The button that should be highlighted/selected first when each screen appears. " +
                 "Required for gamepad navigation to work without a mouse click first.")]
        [SerializeField] private GameObject mainFirstSelected;
        [SerializeField] private GameObject gameSelectFirstSelected;
        [SerializeField] private GameObject settingsFirstSelected;
        [SerializeField] private GameObject creditsFirstSelected;

        [Header("Transition")]
        [SerializeField] private float transitionDuration = 0.35f;
        [SerializeField] private float slideDistance = 500f;
        [SerializeField] private Ease transitionEase = Ease.OutCubic;

        [Header("Optional Startup")]
        [SerializeField] private bool showMainOnStart = true;

        private GameObject _currentRoot;
        private GameObject _currentFirstSelected;

        private readonly System.Collections.Generic.Stack<GameObject> _history = new();

        private InputAction _cancelAction;
        private Sequence _activeTransition;

        private void Awake()
        {
            SceneTransitionManager.EnsureExists();

            WireButtons();
        
            _cancelAction = new InputAction("UI_Cancel", InputActionType.Button);
            _cancelAction.AddBinding("<Gamepad>/buttonEast");
            _cancelAction.AddBinding("<Keyboard>/escape");
            _cancelAction.performed += OnCancelPerformed;
        }
    
        private void Start()
        {
            Time.timeScale = 1f;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (showMainOnStart)
                ShowMain();

            StartCoroutine(SelectFirstButtonNextFrame());
        }

        private System.Collections.IEnumerator SelectFirstButtonNextFrame()
        {
            yield return null;

            if (EventSystem.current == null)
                yield break;

            EventSystem.current.SetSelectedGameObject(null);
            yield return null;

            SelectImmediate(_currentFirstSelected);
        }

        private void OnEnable()
        {
            _cancelAction?.Enable();

            if (InputDeviceTracker.Instance != null)
                InputDeviceTracker.Instance.OnDeviceModeChanged += OnDeviceModeChanged;
        }

        private void OnDisable()
        {
            _cancelAction?.Disable();

            if (InputDeviceTracker.Instance != null)
                InputDeviceTracker.Instance.OnDeviceModeChanged -= OnDeviceModeChanged;
        }

        private void OnDestroy()
        {
            _cancelAction?.Dispose();
            _activeTransition?.Kill();
        }

        private void WireButtons()
        {
            if (playButton != null)
                playButton.onClick.AddListener(ShowGameSelect);

            if (multiplayerButton != null)
                multiplayerButton.onClick.AddListener(OpenMultiplayer);

            if (settingsButton != null)
                settingsButton.onClick.AddListener(ShowSettings);

            if (creditsButton != null)
                creditsButton.onClick.AddListener(ShowCredits);

            if (quitButton != null)
                quitButton.onClick.AddListener(QuitGame);

            if (gameSelectBackButton != null)
                gameSelectBackButton.onClick.AddListener(GoBack);

            if (settingsBackButton != null)
                settingsBackButton.onClick.AddListener(GoBack);

            if (creditsBackButton != null)
                creditsBackButton.onClick.AddListener(GoBack);
        }

        private void ShowMain()
        {
            _activeTransition?.Kill();

            if (mainRoot != null)
                mainRoot.SetActive(true);

            if (gameSelectRoot != null)
                gameSelectRoot.SetActive(false);

            if (settingsRoot != null)
                settingsRoot.SetActive(false);

            if (creditsRoot != null)
                creditsRoot.SetActive(false);

            _currentRoot = null;
            _history.Clear();

            Navigate(mainRoot, mainFirstSelected, pushHistory: false);
        }

        private void ShowGameSelect() => Navigate(gameSelectRoot, gameSelectFirstSelected);

        // Online multiplayer entry point. The lobby is a self-contained native-Unity overlay canvas
        // built entirely in code (Online.UI.Lobby.OnlineLobbyMenuController), so it needs no scene root;
        // when the user backs out of the lobby home screen we simply restore main-menu focus.
        private void OpenMultiplayer()
        {
            Online.UI.Lobby.OnlineLobbyMenuController.OpenLobby(
                onClosed: () => SelectImmediate(_currentFirstSelected));
        }

        private void ShowSettings() => Navigate(settingsRoot, settingsFirstSelected);

        private void ShowCredits() => Navigate(creditsRoot, creditsFirstSelected);

        private void GoBack()
        {
            AudioManager.Instance?.PlayBack();

            if (_history.Count > 0)
            {
                GameObject previous = _history.Pop();
                Navigate(previous, GetFirstSelectedFor(previous), pushHistory: false, goingBack: true);
            }
            else
            {
                ShowMain();
            }
        }

        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return;

            SceneManager.LoadScene(sceneName);
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnCancelPerformed(InputAction.CallbackContext context)
        {
            if (GamepadDropdown.HideAnyOpenDropdown())
                return;

            if (_currentRoot == mainRoot)
                return;

            GoBack();
        }

        private void OnDeviceModeChanged(InputDeviceTracker.DeviceMode mode)
        {
            if (mode != InputDeviceTracker.DeviceMode.Gamepad)
                return;

            GameObject selected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;

            bool selectedIsOnCurrentScreen = selected != null && _currentRoot != null && selected.transform.IsChildOf(_currentRoot.transform);

            if (!selectedIsOnCurrentScreen)
                SelectImmediate(_currentFirstSelected);
        }

        private void Navigate(GameObject targetRoot, GameObject targetFirstSelected, bool pushHistory = true, bool goingBack = false)
        {
            if (targetRoot == null || targetRoot == _currentRoot)
                return;

            if (pushHistory && _currentRoot != null)
                _history.Push(_currentRoot);

            GameObject previousRoot = _currentRoot;
            _currentRoot = targetRoot;
            _currentFirstSelected = targetFirstSelected;

            TransitionTo(previousRoot, targetRoot, goingBack);
        }
        
        private void TransitionTo(GameObject previousRoot, GameObject targetRoot, bool goingBack)
        {
            _activeTransition?.Kill();

            CanvasGroup targetGroup = GetOrAddCanvasGroup(targetRoot);
            RectTransform targetRect = (RectTransform)targetRoot.transform;

            if (previousRoot == null)
            {
                targetRoot.SetActive(true);
                targetRect.anchoredPosition = Vector2.zero;
                targetGroup.alpha = 1f;
                targetGroup.interactable = true;
                targetGroup.blocksRaycasts = true;
                SelectImmediate(_currentFirstSelected);
                return;
            }

            float direction = goingBack ? -1f : 1f;

            AudioManager.Instance?.PlayTransition();

            CanvasGroup previousGroup = GetOrAddCanvasGroup(previousRoot);
            RectTransform previousRect = (RectTransform)previousRoot.transform;
            
            previousGroup.interactable = false;
            previousGroup.blocksRaycasts = false;
            targetGroup.interactable = false;
            targetGroup.blocksRaycasts = false;

            targetRoot.SetActive(true);
            targetRect.anchoredPosition = new Vector2(direction * slideDistance, 0f);
            targetGroup.alpha = 0f;

            _activeTransition = DOTween.Sequence().SetUpdate(true);
            _activeTransition.Join(previousRect.DOAnchorPosX(-direction * slideDistance, transitionDuration).SetEase(transitionEase));
            _activeTransition.Join(previousGroup.DOFade(0f, transitionDuration));
            _activeTransition.Join(targetRect.DOAnchorPosX(0f, transitionDuration).SetEase(transitionEase));
            _activeTransition.Join(targetGroup.DOFade(1f, transitionDuration));

            _activeTransition.OnComplete(() =>
            {
                previousRoot.SetActive(false);
                previousRect.anchoredPosition = Vector2.zero; // reset so it's ready for next time

                targetGroup.interactable = true;
                targetGroup.blocksRaycasts = true;

                SelectImmediate(_currentFirstSelected);
            });
        }

        private static CanvasGroup GetOrAddCanvasGroup(GameObject target)
        {
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            return group != null ? group : target.AddComponent<CanvasGroup>();
        }

        private GameObject GetFirstSelectedFor(GameObject root)
        {
            if (root == mainRoot) return mainFirstSelected;
            if (root == gameSelectRoot) return gameSelectFirstSelected;
            if (root == settingsRoot) return settingsFirstSelected;
            if (root == creditsRoot) return creditsFirstSelected;
            return null;
        }
        
        private void SelectImmediate(GameObject target)
        {
            if (target == null || EventSystem.current == null)
                return;

            StartCoroutine(SelectNextFrame(target));
        }

        private System.Collections.IEnumerator SelectNextFrame(GameObject target)
        {
            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            EventSystem.current.SetSelectedGameObject(target);
        }
    }
}