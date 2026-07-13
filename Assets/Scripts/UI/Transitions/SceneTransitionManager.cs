using System.Collections;
using Audio;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UI.Transitions
{
    public sealed class SceneTransitionManager : MonoBehaviour
    {
        private static SceneTransitionManager Instance { get; set; }

        [Header("Fade")]
        [SerializeField] private float fadeOutDuration = 0.45f;
        [SerializeField] private float fadeInDuration = 0.45f;
        [SerializeField] private Ease fadeEase = Ease.InOutSine;
        [SerializeField] private Color fadeColor = Color.black;

        [Header("Startup")]
        [SerializeField] private bool fadeInOnSceneLoaded = true;

        private CanvasGroup _canvasGroup;
        private Image _fadeImage;
        private bool _isTransitioning;
        private Tween _activeTween;

        public bool IsTransitioning => _isTransitioning;

        public static SceneTransitionManager EnsureExists()
        {
            if (Instance != null)
                return Instance;

            GameObject root = new GameObject(nameof(SceneTransitionManager));
            return root.AddComponent<SceneTransitionManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            BuildOverlay();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                SceneManager.sceneLoaded -= OnSceneLoaded;

            _activeTween?.Kill();
        }

        private void BuildOverlay()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject imageObj = new GameObject("FadeOverlay", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            imageObj.transform.SetParent(transform, false);

            RectTransform rect = imageObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _canvasGroup = imageObj.GetComponent<CanvasGroup>();
            _fadeImage = imageObj.GetComponent<Image>();

            _fadeImage.color = fadeColor;
            _fadeImage.raycastTarget = true;

            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _fadeImage.raycastTarget = false;
        }

        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || _isTransitioning)
                return;

            StartCoroutine(LoadSceneRoutine(sceneName));
        }

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            if (_isTransitioning)
                yield break;

            _isTransitioning = true;

            Time.timeScale = 1f;
            SetBlocking(true);

            AudioManager.Instance?.PlayTransition();

            yield return FadeTo(1f, fadeOutDuration);

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            if (operation == null)
            {
                ForceUnlockOverlay();
                _isTransitioning = false;
                yield break;
            }

            while (!operation.isDone)
                yield return null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_isTransitioning || !fadeInOnSceneLoaded)
                return;

            StartCoroutine(FadeInAfterSceneLoadedRoutine());
        }

        private IEnumerator FadeInAfterSceneLoadedRoutine()
        {
            yield return null;

            yield return FadeTo(0f, fadeInDuration);

            ForceUnlockOverlay();
            _isTransitioning = false;
        }

        private IEnumerator FadeTo(float targetAlpha, float duration)
        {
            _activeTween?.Kill();

            duration = Mathf.Max(0.01f, duration);

            _activeTween = _canvasGroup
                .DOFade(targetAlpha, duration)
                .SetEase(fadeEase)
                .SetUpdate(true);

            yield return _activeTween.WaitForCompletion();
        }

        private void SetBlocking(bool blocking)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = blocking;
                _canvasGroup.blocksRaycasts = blocking;
            }

            if (_fadeImage != null)
                _fadeImage.raycastTarget = blocking;
        }
        
        private void ForceUnlockOverlay()
        {
            _activeTween?.Kill();

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            if (_fadeImage != null)
                _fadeImage.raycastTarget = false;
        }
    }
}