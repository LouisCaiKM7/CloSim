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
                StopCoroutine(_transitionCoroutine);

            _canvasGroup.alpha = black ? 1f : 0f;
            _canvasGroup.blocksRaycasts = black;
        }

        public void FadeToBlack(Action onBlackReached)
        {
            StartTransition(FadeToBlackRoutine(onBlackReached));
        }

        public void FadeFromBlack(Action onFinished = null)
        {
            StartTransition(FadeFromBlackRoutine(onFinished));
        }

        public void FadeToBlackThen(Action onBlackReached, bool fadeBackAfter = false, Action onFinished = null)
        {
            StartTransition(FadeToBlackThenRoutine(onBlackReached, fadeBackAfter, onFinished));
        }

        private void StartTransition(IEnumerator routine)
        {
            if (_transitionCoroutine != null)
                StopCoroutine(_transitionCoroutine);

            _transitionCoroutine = StartCoroutine(routine);
        }

        private IEnumerator FadeToBlackRoutine(Action onBlackReached)
        {
            yield return FadeRoutine(_canvasGroup.alpha, 1f);
            onBlackReached?.Invoke();
            _transitionCoroutine = null;
        }

        private IEnumerator FadeFromBlackRoutine(Action onFinished)
        {
            yield return FadeRoutine(_canvasGroup.alpha, 0f);
            onFinished?.Invoke();
            _transitionCoroutine = null;
        }

        private IEnumerator FadeToBlackThenRoutine(Action onBlackReached, bool fadeBackAfter, Action onFinished)
        {
            yield return FadeRoutine(_canvasGroup.alpha, 1f);

            onBlackReached?.Invoke();

            if (fadeBackAfter)
            {
                yield return FadeRoutine(_canvasGroup.alpha, 0f);
            }

            onFinished?.Invoke();
            _transitionCoroutine = null;
        }

        private IEnumerator FadeRoutine(float startAlpha, float endAlpha)
        {
            float elapsed = 0f;

            _canvasGroup.blocksRaycasts = true;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
                yield return null;
            }

            _canvasGroup.alpha = endAlpha;
            _canvasGroup.blocksRaycasts = endAlpha > 0f;
        }
    }
}