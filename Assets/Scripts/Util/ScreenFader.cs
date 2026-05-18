using System;
using System.Collections;
using UnityEngine;

namespace Util
{
    [RequireComponent(typeof(CanvasGroup))]
    public class ScreenFader : MonoBehaviour
    {
        [SerializeField] private float fadeDuration = 1.5f;
        [SerializeField] private bool startTransparent = true;

        private CanvasGroup _canvasGroup;
        private Coroutine _transitionCoroutine;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();

            _canvasGroup.alpha = startTransparent ? 0f : 1f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        public void SetBlackImmediate(bool black)
        {
            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            _canvasGroup.alpha = black ? 1f : 0f;
            _canvasGroup.blocksRaycasts = black;
        }

        public void FadeToBlack(Action onBlackReached)
        {
            StartTransition(FadeToBlackRoutine(onBlackReached, fadeDuration));
        }

        public void FadeFromBlack(Action onFinished = null)
        {
            FadeFromBlack(fadeDuration, onFinished);
        }

        public void FadeFromBlack(float duration, Action onFinished = null)
        {
            StartTransition(FadeFromBlackRoutine(onFinished, duration));
        }

        public void FadeToBlackThen(Action onBlackReached, bool fadeBackAfter = false, Action onFinished = null)
        {
            StartTransition(FadeToBlackThenRoutine(onBlackReached, fadeBackAfter, onFinished, fadeDuration));
        }

        private void StartTransition(IEnumerator routine)
        {
            if (_transitionCoroutine != null)
                StopCoroutine(_transitionCoroutine);

            _transitionCoroutine = StartCoroutine(routine);
        }

        private IEnumerator FadeToBlackRoutine(Action onBlackReached, float duration)
        {
            yield return FadeRoutine(_canvasGroup.alpha, 1f, duration);

            onBlackReached?.Invoke();
            _transitionCoroutine = null;
        }

        private IEnumerator FadeFromBlackRoutine(Action onFinished, float duration)
        {
            yield return FadeRoutine(_canvasGroup.alpha, 0f, duration);

            onFinished?.Invoke();
            _transitionCoroutine = null;
        }

        private IEnumerator FadeToBlackThenRoutine(
            Action onBlackReached,
            bool fadeBackAfter,
            Action onFinished,
            float duration)
        {
            yield return FadeRoutine(_canvasGroup.alpha, 1f, duration);

            onBlackReached?.Invoke();

            if (fadeBackAfter)
            {
                yield return FadeRoutine(_canvasGroup.alpha, 0f, duration);
            }

            onFinished?.Invoke();
            _transitionCoroutine = null;
        }

        private IEnumerator FadeRoutine(float startAlpha, float endAlpha, float duration)
        {
            duration = Mathf.Max(0.01f, duration);

            float elapsed = 0f;
            _canvasGroup.blocksRaycasts = true;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
                yield return null;
            }

            _canvasGroup.alpha = endAlpha;
            _canvasGroup.blocksRaycasts = endAlpha > 0f;
        }
    }
}