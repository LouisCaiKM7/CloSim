using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace UI.Transitions
{
    public class SplashDirector : MonoBehaviour
    {
        [Header("Robot Rain")]
        [FormerlySerializedAs("robotFlashParent")]
        [Tooltip("Parent RectTransform robots rain through. Usually a full-screen UI panel.")]
        [SerializeField] private RectTransform robotRainParent;

        [Tooltip("How often a new robot image is spawned, at the rain's normal (non-thinned) rate.")]
        [SerializeField] private float spawnInterval = 0.08f;

        [FormerlySerializedAs("robotLifetime")]
        [Tooltip("How long it takes a single robot to fall from above the top edge to below the bottom edge.")]
        [SerializeField] private float robotFallDuration = 2.2f;

        [Tooltip("Size of each robot image in UI pixels.")]
        [SerializeField] private Vector2 startSize = new Vector2(120f, 120f);

        [Tooltip("Size multiplier applied by the time a robot reaches the bottom (subtle depth effect). Use 1 to disable.")]
        [SerializeField] private float endScaleMultiplier = 1.15f;

        [Tooltip("Extra distance past the top/bottom edges where robots spawn/despawn, so the pop-in/out is never visible.")]
        [SerializeField] private float outOfFramePadding = 150f;

        [Tooltip("Random rotation added to each robot image.")]
        [SerializeField] private float randomRotationDegrees = 12f;

        [Tooltip("Maximum sideways drift over a robot's fall, for a less mechanical rain pattern. Set to 0 for a perfectly straight fall.")]
        [SerializeField] private float maxHorizontalDrift = 60f;

        [Tooltip("Maximum alpha for each robot image.")]
        [Range(0f, 1f)]
        [SerializeField] private float robotMaxAlpha = 0.9f;

        [Tooltip("How sparse the rain gets right before it fully stops, as a multiplier on spawnInterval. Higher = thins out more dramatically before stopping.")]
        [SerializeField] private float thinOutMaxIntervalMultiplier = 6f;

        [Header("Fade To Black")]
        [SerializeField] private CanvasGroup blackFade;
        [SerializeField] private float fadeToBlackDuration = 1.2f;
        [SerializeField] private float blackHoldDuration = 0.5f;

        [Header("Rain Timing")]
        [Tooltip("How long robots rain by themselves before the logos start flashing in.")]
        [SerializeField] private float preLogoRainDuration = 1.5f;

        [Header("Brand Logos (Bottom Corners)")]
        [SerializeField] private Image brandLogoLeft;
        [SerializeField] private Image brandLogoRight;
        [SerializeField] private float brandLogoFadeInDuration = 0.6f;
        [Tooltip("Delay between the brand logos starting their fade-in and the word row starting its own.")]
        [SerializeField] private float brandLogoToWordRowDelay = 0.3f;

        [Header("Word Row (6 images)")]
        [Tooltip("The 6 images that form the word. They flash in, in array order, with a stagger between each.")]
        [SerializeField] private Image[] wordRowImages = new Image[6];
        [SerializeField] private float wordRowStagger = 0.1f;
        [SerializeField] private float wordRowFadeInDuration = 0.5f;

        [Header("Logo Hold & Flash Out")]
        [Tooltip("How long all 8 images hold at full visibility before flashing out.")]
        [SerializeField] private float logoHoldDuration = 3f;
        [SerializeField] private float logoFadeOutDuration = 0.6f;

        [Header("Next Scene")]
        [SerializeField] private string nextSceneName = "Main_Menu";

        private Sprite[] _robotSprites;
        private bool _rainSpawningEnabled;
        private float _spawnIntervalMultiplier = 1f;
        private readonly List<GameObject> _activeRainRobots = new List<GameObject>();

        private void OnEnable()
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void OnDisable()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void Start()
        {
            _robotSprites = Resources.LoadAll<Sprite>("RobotPreviews");

            if (_robotSprites == null || _robotSprites.Length == 0)
            {
                Debug.LogWarning("No robot preview sprites found in Assets/Resources/RobotPreviews.");
            }

            StartCoroutine(PlaySplashSequence());
        }

        private IEnumerator PlaySplashSequence()
        {
            InitializeVisualState();

            yield return FadeCanvasGroup(blackFade, 1f, 0f, fadeToBlackDuration);
            yield return new WaitForSecondsRealtime(blackHoldDuration);

            StartCoroutine(RainRobots());

            yield return new WaitForSecondsRealtime(preLogoRainDuration);

            float wordRowSpan = wordRowImages is { Length: > 1 }
                ? wordRowStagger * (wordRowImages.Length - 1)
                : 0f;
            float flashInTotalDuration = brandLogoFadeInDuration + brandLogoToWordRowDelay + wordRowSpan + wordRowFadeInDuration;

            StartCoroutine(ThinOutRainSpawning(flashInTotalDuration));

            yield return PlayLogoFlashIn();

            yield return new WaitForSecondsRealtime(logoHoldDuration);

            yield return PlayLogoFlashOut();

            ClearAllRainRobots();

            yield return FadeCanvasGroup(blackFade, 0f, 1f, fadeToBlackDuration);
            yield return new WaitForSecondsRealtime(blackHoldDuration);

            SceneTransitionManager.EnsureExists().LoadScene(nextSceneName);
        }

        private void InitializeVisualState()
        {
            if (blackFade != null)
                blackFade.alpha = 1f;

            SetImageAlpha(brandLogoLeft, 0f);
            SetImageAlpha(brandLogoRight, 0f);

            if (wordRowImages != null)
            {
                foreach (var img in wordRowImages)
                    SetImageAlpha(img, 0f);
            }

            _rainSpawningEnabled = true;
            _spawnIntervalMultiplier = 1f;
        }

        // ---------------- Robot Rain ----------------

        private IEnumerator RainRobots()
        {
            if (robotRainParent == null)
            {
                Debug.LogWarning("SplashDirector is missing robotRainParent; no rain will spawn.");
                yield break;
            }

            float spawnTimer = 0f;

            while (_rainSpawningEnabled)
            {
                spawnTimer += Time.unscaledDeltaTime;
                float currentInterval = spawnInterval * _spawnIntervalMultiplier;

                while (spawnTimer >= currentInterval)
                {
                    spawnTimer -= currentInterval;
                    SpawnRainRobot();
                }

                yield return null;
            }
        }

        private IEnumerator ThinOutRainSpawning(float duration)
        {
            if (duration <= 0f)
            {
                _rainSpawningEnabled = false;
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _spawnIntervalMultiplier = Mathf.Lerp(1f, thinOutMaxIntervalMultiplier, t);
                yield return null;
            }

            _rainSpawningEnabled = false;
        }

        private void SpawnRainRobot()
        {
            if (_robotSprites == null || _robotSprites.Length == 0)
                return;

            Sprite sprite = _robotSprites[Random.Range(0, _robotSprites.Length)];

            GameObject obj = new GameObject("Robot Rain", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.transform.SetParent(robotRainParent, false);

            RectTransform rect = obj.GetComponent<RectTransform>();
            Image image = obj.GetComponent<Image>();

            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, 0f);

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = startSize;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-randomRotationDegrees, randomRotationDegrees));

            GetRainSpawnAndEndPositions(out Vector2 startPosition, out Vector2 endPosition);
            rect.anchoredPosition = startPosition;

            _activeRainRobots.Add(obj);

            // Fade in/out exactly at the padding buffer, so the robot is at full alpha for the
            // entire time it's actually within the visible area, regardless of fall duration/easing.
            float totalVerticalTravel = Mathf.Max(startPosition.y - endPosition.y, 0.01f);
            float fadeInEndFraction = Mathf.Clamp01(outOfFramePadding / totalVerticalTravel);
            float fadeOutStartFraction = 1f - fadeInEndFraction;

            StartCoroutine(AnimateRainRobot(rect, image, startPosition, endPosition, fadeInEndFraction, fadeOutStartFraction, obj));
        }

        private void GetRainSpawnAndEndPositions(out Vector2 startPosition, out Vector2 endPosition)
        {
            Rect rect = robotRainParent.rect;

            float halfWidth = rect.width * 0.5f;
            float halfHeight = rect.height * 0.5f;

            float spawnX = Random.Range(-halfWidth, halfWidth);
            float driftX = Random.Range(-maxHorizontalDrift, maxHorizontalDrift);

            startPosition = new Vector2(spawnX, halfHeight + outOfFramePadding);
            endPosition = new Vector2(spawnX + driftX, -halfHeight - outOfFramePadding);
        }

        private IEnumerator AnimateRainRobot(RectTransform rect, Image image, Vector2 startPosition, Vector2 endPosition, float fadeInEndFraction, float fadeOutStartFraction, GameObject obj)
        {
            float elapsed = 0f;

            Vector3 startScale = Vector3.one;
            Vector3 endScale = Vector3.one * endScaleMultiplier;

            while (elapsed < robotFallDuration)
            {
                if (rect == null || image == null)
                {
                    _activeRainRobots.Remove(obj);
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / robotFallDuration);

                // Ease-in on position so robots accelerate like they're falling under gravity.
                float moveT = t * t;
                rect.anchoredPosition = Vector2.LerpUnclamped(startPosition, endPosition, moveT);
                rect.localScale = Vector3.LerpUnclamped(startScale, endScale, t);

                // Fade based on actual distance traveled (moveT), not raw time, so the robot is
                // fully visible for its entire time within the visible area and only fades while
                // still inside the offscreen padding buffer above/below the frame.
                float alpha;
                if (moveT < fadeInEndFraction)
                    alpha = Mathf.Lerp(0f, robotMaxAlpha, moveT / fadeInEndFraction);
                else if (moveT > fadeOutStartFraction)
                    alpha = Mathf.Lerp(robotMaxAlpha, 0f, (moveT - fadeOutStartFraction) / (1f - fadeOutStartFraction));
                else
                    alpha = robotMaxAlpha;

                Color c = image.color;
                c.a = alpha;
                image.color = c;

                yield return null;
            }

            _activeRainRobots.Remove(obj);

            if (rect != null)
                Destroy(rect.gameObject);
        }

        private void ClearAllRainRobots()
        {
            _rainSpawningEnabled = false;

            for (int i = _activeRainRobots.Count - 1; i >= 0; i--)
            {
                if (_activeRainRobots[i] != null)
                    Destroy(_activeRainRobots[i]);
            }

            _activeRainRobots.Clear();
        }

        // ---------------- Logo Flash ----------------

        private IEnumerator PlayLogoFlashIn()
        {
            yield return FadeImageGroup(new[] { brandLogoLeft, brandLogoRight }, 0f, 1f, brandLogoFadeInDuration);
            yield return new WaitForSecondsRealtime(brandLogoToWordRowDelay);
            yield return StaggerFadeImages(wordRowImages, 0f, 1f, wordRowFadeInDuration, wordRowStagger);
        }

        private IEnumerator PlayLogoFlashOut()
        {
            Image[] allImages = GetAllLogoImages();
            yield return FadeImageGroup(allImages, 1f, 0f, logoFadeOutDuration);
        }

        private Image[] GetAllLogoImages()
        {
            var list = new List<Image>();

            if (brandLogoLeft != null) list.Add(brandLogoLeft);
            if (brandLogoRight != null) list.Add(brandLogoRight);

            if (wordRowImages != null)
            {
                foreach (var img in wordRowImages)
                    if (img != null) list.Add(img);
            }

            return list.ToArray();
        }

        private IEnumerator FadeImageGroup(Image[] images, float from, float to, float duration)
        {
            if (images == null || images.Length == 0)
                yield break;

            foreach (var img in images)
                SetImageAlpha(img, from);

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = Mathf.Lerp(from, to, t);

                foreach (var img in images)
                    SetImageAlpha(img, alpha);

                yield return null;
            }

            foreach (var img in images)
                SetImageAlpha(img, to);
        }

        private IEnumerator StaggerFadeImages(Image[] images, float from, float to, float duration, float stagger)
        {
            if (images == null || images.Length == 0)
                yield break;

            foreach (var img in images)
                SetImageAlpha(img, from);

            for (int i = 0; i < images.Length; i++)
            {
                StartCoroutine(FadeSingleImage(images[i], from, to, duration));

                if (i < images.Length - 1)
                    yield return new WaitForSecondsRealtime(stagger);
            }

            // Wait for the last image's own fade to finish before returning.
            yield return new WaitForSecondsRealtime(duration);
        }

        private IEnumerator FadeSingleImage(Image img, float from, float to, float duration)
        {
            if (img == null)
                yield break;

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                SetImageAlpha(img, Mathf.Lerp(from, to, t));
                yield return null;
            }

            SetImageAlpha(img, to);
        }

        private void SetImageAlpha(Image img, float alpha)
        {
            if (img == null)
                return;

            Color c = img.color;
            c.a = alpha;
            img.color = c;
        }

        // ---------------- Shared ----------------

        private IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
        {
            if (group == null)
                yield break;

            float elapsed = 0f;
            group.alpha = from;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                group.alpha = Mathf.Lerp(from, to, t);

                yield return null;
            }

            group.alpha = to;
        }
    }
}