using UnityEngine;
using UnityEngine.UI;

namespace UI.RobotSelection
{
    public class RobotTilePlayerIndicator : MonoBehaviour
    {
        [Header("Hierarchy Versions")] [SerializeField]
        private GameObject unselectedRoot;

        [SerializeField] private GameObject selectedRoot;

        [Header("Tint Targets")] [SerializeField]
        private Graphic[] unselectedTintTargets;

        [SerializeField] private Graphic[] selectedTintTargets;

        [Header("Alpha")] [SerializeField, Range(0f, 1f)]
        private float hoverAlpha = 0.6f;

        [SerializeField, Range(0f, 1f)] private float lockedAlpha = 1f;

        private bool _hasReceivedState;

        private void Awake()
        {
            if (!_hasReceivedState)
                ApplyVisuals(false, false, Color.clear);
        }

        public void SetState(bool visible, bool locked, Color color)
        {
            _hasReceivedState = true;

            gameObject.SetActive(visible);

            ApplyVisuals(visible, locked, color);
        }

        private void ApplyVisuals(bool visible, bool locked, Color color)
        {
            if (!visible)
            {
                SetRootActive(unselectedRoot, false);
                SetRootActive(selectedRoot, false);

                return;
            }

            SetRootActive(unselectedRoot, !locked);
            SetRootActive(selectedRoot, locked);

            ApplyColor(
                locked ? selectedTintTargets : unselectedTintTargets,
                color,
                locked ? lockedAlpha : hoverAlpha
            );
        }

        private static void SetRootActive(GameObject root, bool active)
        {
            if (root != null)
                root.SetActive(active);
        }

        private static void ApplyColor(Graphic[] targets, Color color, float alpha)
        {
            if (targets == null)
                return;

            color.a = alpha;

            foreach (var t in targets)
            {
                if (t != null)
                    t.color = color;
            }
        }
    }
}