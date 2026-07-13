using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace UI.Components
{
    public class DropdownAutoScroller : MonoBehaviour, IScrollHandler
    {
        [SerializeField] private float wheelSensitivity = 30f;

        private ScrollRect _scrollRect;
        private RectTransform _viewport;
        private RectTransform _content;
        private Canvas _rootCanvas;
        private Camera _eventCamera;
        private GameObject _lastChecked;

        private readonly Vector3[] _viewportCorners = new Vector3[4];
        private readonly Vector3[] _itemCorners = new Vector3[4];

        private void Awake()
        {
            ResolveScrollRect();
        }

        private void OnEnable()
        {
            ResolveScrollRect();
        }

        private void LateUpdate()
        {
            if (!ResolveIfNeeded())
                return;

            PollMouseWheelFallback();
            FollowSelectedItem();
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!ResolveIfNeeded())
                return;

            _scrollRect.OnScroll(eventData);
        }

        public void EnsureVisible(RectTransform target)
        {
            if (target == null || !ResolveIfNeeded())
                return;

            Canvas.ForceUpdateCanvases();

            target.GetWorldCorners(_itemCorners);
            _viewport.GetWorldCorners(_viewportCorners);

            float itemTop = _itemCorners[1].y;
            float itemBottom = _itemCorners[0].y;
            float viewportTop = _viewportCorners[1].y;
            float viewportBottom = _viewportCorners[0].y;

            float worldDelta = 0f;

            if (itemTop > viewportTop)
                worldDelta = viewportTop - itemTop;
            else if (itemBottom < viewportBottom)
                worldDelta = viewportBottom - itemBottom;

            if (Mathf.Approximately(worldDelta, 0f))
                return;

            MoveContent(worldDelta / _content.lossyScale.y);
        }

        private void FollowSelectedItem()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return;

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null || selected == _lastChecked)
                return;

            _lastChecked = selected;

            if (selected.transform.IsChildOf(_content))
                EnsureVisible((RectTransform)selected.transform);
        }

        private void PollMouseWheelFallback()
        {
            float scrollY = ReadMouseWheelY();
            if (Mathf.Approximately(scrollY, 0f))
                return;

            if (!TryGetPointerPosition(out var pointerPosition))
                return;

            if (!PointerIsOverDropdown(pointerPosition))
                return;

            MoveContent(-scrollY * wheelSensitivity);
        }

        private void MoveContent(float deltaY)
        {
            if (Mathf.Approximately(deltaY, 0f) || _content == null)
                return;

            _scrollRect.StopMovement();

            Vector2 position = _content.anchoredPosition;
            position.y += deltaY;
            _content.anchoredPosition = position;

            ClampContentToViewport();
        }

        private void ClampContentToViewport()
        {
            Canvas.ForceUpdateCanvases();

            if (_viewport == null || _content == null)
                return;

            float viewportHeight = _viewport.rect.height;
            float contentHeight = _content.rect.height;

            if (contentHeight <= viewportHeight)
            {
                Vector2 position = _content.anchoredPosition;
                position.y = 0f;
                _content.anchoredPosition = position;
                return;
            }

            // Assumes the standard TMP_Dropdown template: content pivot at top, y = 0 at top.
            Vector2 clamped = _content.anchoredPosition;
            clamped.y = Mathf.Clamp(clamped.y, 0f, contentHeight - viewportHeight);
            _content.anchoredPosition = clamped;
        }

        private bool PointerIsOverDropdown(Vector2 screenPosition)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(_viewport, screenPosition, _eventCamera) ||
                   RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_scrollRect.transform, screenPosition, _eventCamera);
        }

        private static float ReadMouseWheelY()
        {
            float value = Input.mouseScrollDelta.y;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
                value += Mouse.current.scroll.ReadValue().y / 120f;
#endif

            return value;
        }

        private static bool TryGetPointerPosition(out Vector2 position)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                position = Mouse.current.position.ReadValue();
                return true;
            }
#endif
            position = Input.mousePosition;
            return true;
        }

        private bool ResolveIfNeeded()
        {
            if (_scrollRect == null || _viewport == null || _content == null)
                ResolveScrollRect();

            return _scrollRect != null && _viewport != null && _content != null;
        }

        private void ResolveScrollRect()
        {
            _scrollRect = GetComponent<ScrollRect>();
            if (_scrollRect == null)
                _scrollRect = GetComponentInChildren<ScrollRect>(true);
            if (_scrollRect == null)
                _scrollRect = GetComponentInParent<ScrollRect>(true);

            if (_scrollRect == null)
                return;

            _viewport = _scrollRect.viewport != null ? _scrollRect.viewport : _scrollRect.GetComponent<RectTransform>();
            _content = _scrollRect.content;

            _rootCanvas = _scrollRect.GetComponentInParent<Canvas>();
            _eventCamera = _rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _rootCanvas.worldCamera
                : null;
        }
    }
}
