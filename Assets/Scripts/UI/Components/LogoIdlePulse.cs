using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Components
{
    public class LogoIdlePulse : MonoBehaviour
    {
        [Header("Bounce")]
        [SerializeField] private float bounceScale = 1.05f;
        [SerializeField] private float bounceDuration = 1.2f;
        [SerializeField] private Ease bounceEase = Ease.InOutSine;

        [Header("Glow (optional)")]
        [Tooltip("Leave empty to skip the color-glow loop and only bounce.")]
        [SerializeField] private Graphic glowTarget;
        [SerializeField] private Color glowColor = new Color(0.2f, 1f, 0.282f); // #33FF48
        [SerializeField] private float glowPulseDuration = 1.5f;

        private Vector3 _baseScale;
        private Color _baseGlowColor;
        private Sequence _bounceSequence;
        private Tween _glowTween;

        private void Awake()
        {
            _baseScale = transform.localScale;

            if (glowTarget != null)
                _baseGlowColor = glowTarget.color;
        }

        private void OnEnable()
        {
            _bounceSequence = DOTween.Sequence().SetUpdate(true);
            _bounceSequence.Append(transform.DOScale(_baseScale * bounceScale, bounceDuration).SetEase(bounceEase));
            _bounceSequence.Append(transform.DOScale(_baseScale, bounceDuration).SetEase(bounceEase));
            _bounceSequence.SetLoops(-1);

            if (glowTarget != null)
            {
                _glowTween = glowTarget
                    .DOColor(glowColor, glowPulseDuration)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }
        }

        private void OnDisable()
        {
            _bounceSequence?.Kill();
            _glowTween?.Kill();
            
            transform.localScale = _baseScale;

            if (glowTarget != null)
                glowTarget.color = _baseGlowColor;
        }
    }
}