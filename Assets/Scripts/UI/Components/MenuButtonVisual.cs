using System.Collections;
using Audio;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.Components
{
    [RequireComponent(typeof(RectTransform))]
    public class MenuButtonVisual : MonoBehaviour,
        ISelectHandler, IDeselectHandler,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerClickHandler, ISubmitHandler
    {
        [Header("References")]
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image glowPanel;

        [Header("Colors")]
        [SerializeField] private Color normalTextColor = new Color(0.847f, 1f, 0.847f);   // #D8FFD8
        [SerializeField] private Color highlightTextColor = new Color(0.2f, 1f, 0.282f);  // #33FF48
        [SerializeField] private float glowMaxAlpha = 0.3f;

        [Header("Motion")]
        [SerializeField] private float scaleUp = 1.06f;
        [SerializeField] private float tweenDuration = 0.15f;
        [SerializeField] private float clickPunchScale = 1.1f;
        [SerializeField] private float clickPunchDuration = 0.08f;

        private RectTransform _rt;
        private Vector3 _baseScale;
        private Coroutine _activeRoutine;
        private bool _isActive;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _baseScale = _rt.localScale;
            ApplyInstant(false);
        }

        private void OnDisable()
        {
            StopActiveRoutine();
            _isActive = false;
            ApplyInstant(false);
        }

        public void OnSelect(BaseEventData eventData) => SetHighlighted(true);
        public void OnDeselect(BaseEventData eventData) => SetHighlighted(false);

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnPointerExit(PointerEventData eventData)
        {

        }

        public void OnPointerClick(PointerEventData eventData)
        {
            PlayClickPunch();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            PlayClickPunch();
        }

        private void PlayClickPunch()
        {
            AudioManager.Instance?.PlayConfirm();

            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
                return;

            StopActiveRoutine();
            _activeRoutine = StartCoroutine(ClickPunch());
        }

        private void SetHighlighted(bool active)
        {
            if (_isActive == active)
                return;

            _isActive = active;
            StopActiveRoutine();

            if (isActiveAndEnabled && gameObject.activeInHierarchy)
                _activeRoutine = StartCoroutine(AnimateTo(active, tweenDuration));
            else
                ApplyInstant(active);

            if (active)
                AudioManager.Instance?.PlayHover();
        }

        public void SetExternalHighlighted(bool active, bool playSound = false)
        {
            if (_isActive == active)
                return;

            _isActive = active;
            StopActiveRoutine();

            if (isActiveAndEnabled && gameObject.activeInHierarchy)
                _activeRoutine = StartCoroutine(AnimateTo(active, tweenDuration));
            else
                ApplyInstant(active);

            if (active && playSound)
                AudioManager.Instance?.PlayHover();
        }

        private void StopActiveRoutine()
        {
            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
                _activeRoutine = null;
            }
        }

        private IEnumerator AnimateTo(bool active, float duration)
        {
            float startGlow = glowPanel != null ? glowPanel.color.a : 0f;
            float targetGlow = active ? glowMaxAlpha : 0f;

            Color startTextColor = label != null ? label.color : normalTextColor;
            Color targetTextColor = active ? highlightTextColor : normalTextColor;

            Vector3 startScale = _rt.localScale;
            Vector3 targetScale = active ? _baseScale * scaleUp : _baseScale;

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                float eased = 1f - Mathf.Pow(1f - p, 3f); // ease-out cubic

                _rt.localScale = Vector3.LerpUnclamped(startScale, targetScale, eased);

                if (label != null)
                    label.color = Color.Lerp(startTextColor, targetTextColor, eased);

                if (glowPanel != null)
                    SetAlpha(glowPanel, Mathf.Lerp(startGlow, targetGlow, eased));

                yield return null;
            }

            ApplyInstant(active);
        }

        private IEnumerator ClickPunch()
        {
            Vector3 from = _rt.localScale;
            Vector3 peak = _baseScale * clickPunchScale;

            yield return Lerp(from, peak, clickPunchDuration * 0.4f);
            yield return Lerp(peak, _isActive ? _baseScale * scaleUp : _baseScale, clickPunchDuration * 0.6f);
        }

        private IEnumerator Lerp(Vector3 from, Vector3 to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _rt.localScale = Vector3.LerpUnclamped(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            _rt.localScale = to;
        }

        private void ApplyInstant(bool active)
        {
            _rt.localScale = active ? _baseScale * scaleUp : _baseScale;

            if (label != null)
                label.color = active ? highlightTextColor : normalTextColor;

            if (glowPanel != null)
                SetAlpha(glowPanel, active ? glowMaxAlpha : 0f);
        }

        private static void SetAlpha(Image image, float alpha)
        {
            Color c = image.color;
            c.a = alpha;
            image.color = c;
        }
    }
}