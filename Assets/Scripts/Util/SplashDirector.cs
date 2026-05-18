using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

public class SplashDirector : MonoBehaviour
{
    [Tooltip("Parent RectTransform for spawned robot preview images. Usually a full-screen UI panel.")]
    [SerializeField] private RectTransform robotFlashParent;

    [Tooltip("How long the robot flash section lasts.")]
    [SerializeField] private float flashDuration = 4f;

    [Tooltip("How often a new robot image is spawned.")]
    [SerializeField] private float spawnInterval = 0.08f;

    [Tooltip("Lifetime of each robot image.")]
    [SerializeField] private float robotLifetime = 1.15f;

    [Tooltip("Starting size of each robot image in UI pixels.")]
    [SerializeField] private Vector2 startSize = new Vector2(120f, 120f);

    [Tooltip("Ending size multiplier.")]
    [SerializeField] private float endScaleMultiplier = 3.25f;

    [Tooltip("Extra distance past the screen edge.")]
    [SerializeField] private float outOfFramePadding = 300f;

    [Tooltip("Random rotation added to each robot image.")]
    [SerializeField] private float randomRotationDegrees = 18f;

    [Tooltip("Maximum alpha for each robot image.")]
    [Range(0f, 1f)]
    [SerializeField] private float robotMaxAlpha = 0.9f;

    [Header("Fade To Black")]
    [SerializeField] private CanvasGroup blackFade;
    [SerializeField] private float fadeToBlackDuration = 1.2f;
    [SerializeField] private float blackHoldDuration = 0.5f;

    [Header("Logo Video")]
    [SerializeField] private CanvasGroup yourLogo;
    [SerializeField] private VideoPlayer logoVideoPlayer;

    [Tooltip("File must be placed in Assets/StreamingAssets.")]
    [SerializeField] private string logoVideoFileName = "CloSimLogoAnimation.mp4";

    [Tooltip("How long to wait for the video to prepare before falling back.")]
    [SerializeField] private float videoPrepareTimeout = 5f;

    [Tooltip("How long to wait for playback to start before falling back.")]
    [SerializeField] private float videoStartTimeout = 2f;

    [Tooltip("Maximum allowed playback wait. Prevents infinite stalls if VideoPlayer never reaches loopPointReached.")]
    [SerializeField] private float videoPlaybackTimeout = 15f;

    [SerializeField] private float yourLogoFadeInDuration = 1f;
    [SerializeField] private float yourLogoHoldDuration = 0.25f;
    [SerializeField] private float yourLogoFadeOutDuration = 0.8f;

    [Header("Next Scene")]
    [SerializeField] private string nextSceneName = "MainMenu";

    private Sprite[] robotSprites;
    private bool logoVideoReachedEnd;
    private bool logoVideoError;

    private void OnEnable()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void OnDisable()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (logoVideoPlayer != null)
        {
            logoVideoPlayer.loopPointReached -= OnLogoVideoFinished;
            logoVideoPlayer.errorReceived -= OnLogoVideoError;
        }
    }

    private void Start()
    {
        robotSprites = Resources.LoadAll<Sprite>("RobotPreviews");

        if (robotSprites == null || robotSprites.Length == 0)
        {
            Debug.LogWarning("No robot preview sprites found in Assets/Resources/RobotPreviews.");
        }

        StartCoroutine(PlaySplashSequence());
    }

    private IEnumerator PlaySplashSequence()
    {
        if (blackFade != null)
            blackFade.alpha = 0f;

        if (yourLogo != null)
            yourLogo.alpha = 0f;

        if (logoVideoPlayer != null)
        {
            logoVideoPlayer.playOnAwake = false;
            logoVideoPlayer.isLooping = false;
            logoVideoPlayer.Stop();
        }

        yield return FadeCanvasGroup(blackFade, 1f, 0f, fadeToBlackDuration);
        yield return new WaitForSecondsRealtime(blackHoldDuration);

        yield return PlayRobotFlashes();

        yield return FadeCanvasGroup(blackFade, 0f, 1f, fadeToBlackDuration);
        yield return new WaitForSecondsRealtime(blackHoldDuration);

        yield return PlayLogoVideo();

        SceneManager.LoadScene(nextSceneName);
    }

    private IEnumerator PlayLogoVideo()
    {
        if (yourLogo == null)
            yield break;

        if (logoVideoPlayer == null)
        {
            Debug.LogWarning("SplashDirector is missing logoVideoPlayer.");
            yield return PlayLogoFallback();
            yield break;
        }

        logoVideoReachedEnd = false;
        logoVideoError = false;

        logoVideoPlayer.loopPointReached -= OnLogoVideoFinished;
        logoVideoPlayer.errorReceived -= OnLogoVideoError;

        logoVideoPlayer.loopPointReached += OnLogoVideoFinished;
        logoVideoPlayer.errorReceived += OnLogoVideoError;

        string videoPath = Path.Combine(Application.streamingAssetsPath, logoVideoFileName);

#if !UNITY_ANDROID && !UNITY_WEBGL
        if (!File.Exists(videoPath))
        {
            Debug.LogError("Logo video file not found at: " + videoPath);
            yield return PlayLogoFallback();
            CleanupLogoVideoEvents();
            yield break;
        }
#endif

        logoVideoPlayer.playOnAwake = false;
        logoVideoPlayer.isLooping = false;
        logoVideoPlayer.waitForFirstFrame = true;
        logoVideoPlayer.skipOnDrop = false;
        logoVideoPlayer.source = VideoSource.Url;
        logoVideoPlayer.url = videoPath;

        logoVideoPlayer.Stop();
        logoVideoPlayer.time = 0;
        logoVideoPlayer.Prepare();

        float prepareElapsed = 0f;

        while (!logoVideoPlayer.isPrepared && !logoVideoError && prepareElapsed < videoPrepareTimeout)
        {
            prepareElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!logoVideoPlayer.isPrepared || logoVideoError)
        {
            Debug.LogWarning("Logo video failed to prepare: " + videoPath);
            yield return PlayLogoFallback();
            CleanupLogoVideoEvents();
            yield break;
        }

        yield return FadeCanvasGroup(yourLogo, 0f, 1f, yourLogoFadeInDuration);

        logoVideoPlayer.Play();

        float startElapsed = 0f;

        while (!logoVideoPlayer.isPlaying && !logoVideoError && startElapsed < videoStartTimeout)
        {
            startElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!logoVideoPlayer.isPlaying || logoVideoError)
        {
            Debug.LogWarning("Logo video failed to start: " + videoPath);
            yield return new WaitForSecondsRealtime(yourLogoHoldDuration);
            yield return FadeCanvasGroup(yourLogo, 1f, 0f, yourLogoFadeOutDuration);
            logoVideoPlayer.Stop();
            CleanupLogoVideoEvents();
            yield break;
        }

        float playbackElapsed = 0f;

        while (!logoVideoReachedEnd && !logoVideoError && playbackElapsed < videoPlaybackTimeout)
        {
            playbackElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (logoVideoError)
        {
            Debug.LogWarning("Logo video stopped because of an error.");
        }
        else if (!logoVideoReachedEnd)
        {
            Debug.LogWarning("Logo video playback timed out before reaching the end.");
        }

        yield return new WaitForSecondsRealtime(yourLogoHoldDuration);
        yield return FadeCanvasGroup(yourLogo, 1f, 0f, yourLogoFadeOutDuration);

        logoVideoPlayer.Stop();
        CleanupLogoVideoEvents();
    }

    private IEnumerator PlayLogoFallback()
    {
        yield return FadeCanvasGroup(yourLogo, 0f, 1f, yourLogoFadeInDuration);
        yield return new WaitForSecondsRealtime(yourLogoHoldDuration);
        yield return FadeCanvasGroup(yourLogo, 1f, 0f, yourLogoFadeOutDuration);
    }

    private void CleanupLogoVideoEvents()
    {
        if (logoVideoPlayer == null)
            return;

        logoVideoPlayer.loopPointReached -= OnLogoVideoFinished;
        logoVideoPlayer.errorReceived -= OnLogoVideoError;
    }

    private IEnumerator PlayRobotFlashes()
    {
        if (robotFlashParent == null)
        {
            Debug.LogWarning("SplashDirector is missing robotFlashParent.");
            yield return new WaitForSecondsRealtime(flashDuration);
            yield break;
        }

        float elapsed = 0f;
        float spawnTimer = 0f;

        while (elapsed < flashDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            spawnTimer += Time.unscaledDeltaTime;

            while (spawnTimer >= spawnInterval)
            {
                spawnTimer -= spawnInterval;
                SpawnRobotFlash();
            }

            yield return null;
        }
    }

    private void SpawnRobotFlash()
    {
        if (robotSprites == null || robotSprites.Length == 0)
            return;

        Sprite sprite = robotSprites[Random.Range(0, robotSprites.Length)];

        GameObject obj = new GameObject("Robot Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(robotFlashParent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        Image image = obj.GetComponent<Image>();

        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = new Color(1f, 1f, 1f, 0f);

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = startSize;
        rect.localScale = Vector3.one;

        Vector2 direction = Random.insideUnitCircle.normalized;

        if (direction.sqrMagnitude < 0.01f)
            direction = Vector2.right;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        rect.localRotation = Quaternion.Euler(
            0f,
            0f,
            angle - 90f + Random.Range(-randomRotationDegrees, randomRotationDegrees)
        );

        float distance = GetOutOfFrameDistance(direction);
        Vector2 endPosition = direction * distance;

        StartCoroutine(AnimateRobotFlash(rect, image, endPosition));
    }

    private float GetOutOfFrameDistance(Vector2 direction)
    {
        Rect rect = robotFlashParent.rect;

        float halfWidth = rect.width * 0.5f;
        float halfHeight = rect.height * 0.5f;

        float xDistance = Mathf.Abs(direction.x) > 0.001f
            ? halfWidth / Mathf.Abs(direction.x)
            : 0f;

        float yDistance = Mathf.Abs(direction.y) > 0.001f
            ? halfHeight / Mathf.Abs(direction.y)
            : 0f;

        return Mathf.Max(xDistance, yDistance) + outOfFramePadding;
    }

    private IEnumerator AnimateRobotFlash(RectTransform rect, Image image, Vector2 endPosition)
    {
        float elapsed = 0f;

        Vector2 startPosition = Vector2.zero;
        Vector3 startScale = Vector3.one;
        Vector3 endScale = Vector3.one * endScaleMultiplier;

        while (elapsed < robotLifetime)
        {
            if (rect == null || image == null)
                yield break;

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / robotLifetime);

            float moveT = EaseOutCubic(t);
            float scaleT = Mathf.SmoothStep(0f, 1f, t);

            rect.anchoredPosition = Vector2.LerpUnclamped(startPosition, endPosition, moveT);
            rect.localScale = Vector3.LerpUnclamped(startScale, endScale, scaleT);

            float alpha;

            if (t < 0.18f)
            {
                alpha = Mathf.Lerp(0f, robotMaxAlpha, t / 0.18f);
            }
            else
            {
                alpha = Mathf.Lerp(robotMaxAlpha, 0f, (t - 0.18f) / 0.82f);
            }

            Color c = image.color;
            c.a = alpha;
            image.color = c;

            yield return null;
        }

        if (rect != null)
            Destroy(rect.gameObject);
    }

    private float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

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

    private void OnLogoVideoFinished(VideoPlayer source)
    {
        logoVideoReachedEnd = true;
    }

    private void OnLogoVideoError(VideoPlayer source, string message)
    {
        logoVideoError = true;
        Debug.LogError("Logo video error: " + message);
    }
}