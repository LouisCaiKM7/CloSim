using System.Collections.Generic;
using Core;
using UnityEngine;
using UnityEngine.UI;

namespace UI.RobotSelection
{
    public class RobotGridUI : MonoBehaviour
    {
        [SerializeField] private LoadMatch loadMatch;

        [Tooltip("Parent with a GridLayoutGroup (and optionally a ScrollRect above it).")] [SerializeField]
        private RectTransform gridContent;

        [SerializeField] private RobotTileUI tilePrefab;

        [Header("Scrolling")] [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private float scrollPadding = 30f;

        private readonly List<RobotTileUI> _tiles = new();

        public event System.Action<int> OnTileClicked;

        public IReadOnlyList<RobotTileUI> Tiles => _tiles;
        public int RobotCount => _tiles.Count;

        private int _pendingEnsureVisibleIndex = -1;

        public void BuildGrid()
        {
            if (loadMatch == null)
                loadMatch = FindFirstObjectByType<LoadMatch>();

            if (loadMatch == null || gridContent == null || tilePrefab == null)
            {
                Debug.LogWarning($"[RobotGridUI] BuildGrid missing references — " +
                                 $"loadMatch={loadMatch != null}, gridContent={gridContent != null}, tilePrefab={tilePrefab != null}");
                return;
            }

            loadMatch.CheckRobots();
            IReadOnlyList<RobotCatalogEntry> catalog = loadMatch.GetRobotCatalog();

            ClearGrid();

            foreach (var t in catalog)
            {
                RobotTileUI tile = Instantiate(tilePrefab, gridContent);
                tile.gameObject.SetActive(true);
                tile.SetData(t);
                tile.OnClicked += HandleTileClicked;
                _tiles.Add(tile);
            }
            
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(gridContent);
            Canvas.ForceUpdateCanvases();
        }

        private void ClearGrid()
        {
            foreach (var t in _tiles)
            {
                if (t != null)
                {
                    t.OnClicked -= HandleTileClicked;
                    Destroy(t.gameObject);
                }
            }

            _tiles.Clear();
        }

        private void HandleTileClicked(int robotIndex)
        {
            OnTileClicked?.Invoke(robotIndex);
        }

        public void EnsureVisible(int robotIndex)
        {
            if (robotIndex < 0 || robotIndex >= _tiles.Count)
                return;

            _pendingEnsureVisibleIndex = robotIndex;
        }

        private void LateUpdate()
        {
            if (_pendingEnsureVisibleIndex < 0)
                return;

            int index = _pendingEnsureVisibleIndex;
            _pendingEnsureVisibleIndex = -1;

            EnsureVisibleImmediate(index);
        }

        private void EnsureVisibleImmediate(int robotIndex)
        {
            if (robotIndex < 0 || robotIndex >= _tiles.Count)
                return;

            if (scrollRect == null)
                scrollRect = GetComponentInParent<ScrollRect>(true);

            if (scrollRect == null)
            {
                Debug.LogWarning("[RobotGridUI] EnsureVisible failed: no ScrollRect assigned or found.", this);
                return;
            }

            RectTransform viewport = scrollRect.viewport != null
                ? scrollRect.viewport
                : scrollRect.GetComponent<RectTransform>();

            RectTransform content = scrollRect.content != null
                ? scrollRect.content
                : gridContent;

            RobotTileUI tile = _tiles[robotIndex];

            if (viewport == null || content == null || tile == null || tile.RectTransform == null)
                return;

            scrollRect.StopMovement();

            RectTransform target = tile.RectTransform;

            Vector3[] targetWorldCorners = new Vector3[4];
            Vector3[] viewportWorldCorners = new Vector3[4];

            target.GetWorldCorners(targetWorldCorners);
            viewport.GetWorldCorners(viewportWorldCorners);

            float targetTop = targetWorldCorners[1].y;
            float targetBottom = targetWorldCorners[0].y;

            float viewportTop = viewportWorldCorners[1].y - scrollPadding;
            float viewportBottom = viewportWorldCorners[0].y + scrollPadding;

            float worldDeltaY;

            if (targetTop > viewportTop)
            {
                // Tile is above the visible area.
                worldDeltaY = viewportTop - targetTop;
            }
            else if (targetBottom < viewportBottom)
            {
                // Tile is below the visible area.
                worldDeltaY = viewportBottom - targetBottom;
            }
            else
            {
                return;
            }

            Vector2 position = content.anchoredPosition;
            position.y += worldDeltaY / content.lossyScale.y;
            content.anchoredPosition = position;

            ClampScrollContent(content, viewport);
        }

        private static void ClampScrollContent(RectTransform content, RectTransform viewport)
        {
            if (content == null || viewport == null)
                return;

            float viewportHeight = viewport.rect.height;
            float contentHeight = content.rect.height;

            Vector2 position = content.anchoredPosition;

            position.y = contentHeight <= viewportHeight ? 0f : Mathf.Clamp(position.y, 0f, contentHeight - viewportHeight);

            content.anchoredPosition = position;
        }

        private readonly int[] _hoveredByPlayer = { -1, -1, -1, -1 };

        public void SetHoverForPlayer(int playerIndex, int robotIndex, Color playerColor)
        {
            int oldIndex = _hoveredByPlayer[playerIndex];

            if (oldIndex == robotIndex)
                return;

            if (oldIndex >= 0 && oldIndex < _tiles.Count)
                _tiles[oldIndex].SetHovering(playerIndex, false, playerColor);

            if (robotIndex >= 0 && robotIndex < _tiles.Count)
                _tiles[robotIndex].SetHovering(playerIndex, true, playerColor);

            _hoveredByPlayer[playerIndex] = robotIndex;
        }

        private readonly int[] _lockedByPlayer = { -1, -1, -1, -1 };

        public void SetLockForPlayer(int playerIndex, int robotIndex, Color playerColor)
        {
            int oldIndex = _lockedByPlayer[playerIndex];

            if (oldIndex == robotIndex)
                return;

            if (oldIndex >= 0 && oldIndex < _tiles.Count)
                _tiles[oldIndex].SetLocked(playerIndex, false, playerColor);

            if (robotIndex >= 0 && robotIndex < _tiles.Count)
                _tiles[robotIndex].SetLocked(playerIndex, true, playerColor);

            _lockedByPlayer[playerIndex] = robotIndex;
        }

        public void ClearPlayer(int playerIndex, Color playerColor)
        {
            if (playerIndex < 0 || playerIndex >= _hoveredByPlayer.Length)
                return;

            foreach (var t in _tiles)
            {
                if (t == null)
                    continue;

                t.SetHovering(playerIndex, false, playerColor);
                t.SetLocked(playerIndex, false, playerColor);
            }

            _hoveredByPlayer[playerIndex] = -1;
            _lockedByPlayer[playerIndex] = -1;
        }
    }
}