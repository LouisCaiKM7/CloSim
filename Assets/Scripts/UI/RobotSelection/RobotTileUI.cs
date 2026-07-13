using System;
using Core;
using TMPro;
using UI.Components;
using UnityEngine;
using UnityEngine.UI;

namespace UI.RobotSelection
{
    public class RobotTileUI : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private Image portraitImage;
        [SerializeField] private TMP_Text numberText;

        [Header("Mouse / UI Selection")]
        [SerializeField] private Button button;

        [Header("Player Indicators")]
        [SerializeField] private RobotTilePlayerIndicator[] playerIndicators = new RobotTilePlayerIndicator[4];

        public RectTransform RectTransform { get; private set; }
        private int RobotIndex { get; set; } = -1;

        public event Action<int> OnClicked;

        private readonly bool[] _hoveringPlayers = new bool[4];
        private readonly bool[] _lockedPlayers = new bool[4];
        private readonly Color[] _playerColors = new Color[4];

        private MenuButtonVisual _buttonVisual;
        
        private void Awake()
        {
            RectTransform = (RectTransform)transform;

            if (button == null)
                button = GetComponent<Button>();

            _buttonVisual = GetComponent<MenuButtonVisual>();
            if (_buttonVisual == null)
                _buttonVisual = GetComponentInChildren<MenuButtonVisual>(true);

            if (button != null)
                button.onClick.AddListener(HandleClicked);
        }

        private void HandleClicked()
        {
            OnClicked?.Invoke(RobotIndex);
        }

        public void SetData(RobotCatalogEntry entry)
        {
            RobotIndex = entry.Index;

            if (portraitImage != null)
            {
                portraitImage.sprite = entry.PreviewSprite;
                portraitImage.enabled = entry.PreviewSprite != null;
            }

            if (numberText != null)
                numberText.text = entry.TeamNumber.ToString();

            ClearAllIndicators();
        }

        public void SetHovering(int playerIndex, bool isHovering, Color playerColor)
        {
            if (!IsValidPlayerIndex(playerIndex))
                return;

            _hoveringPlayers[playerIndex] = isHovering;
            _playerColors[playerIndex] = playerColor;

            RefreshPlayerIndicator(playerIndex);
            RefreshTileVisual();
        }

        public void SetLocked(int playerIndex, bool isLocked, Color playerColor)
        {
            if (!IsValidPlayerIndex(playerIndex))
                return;

            _lockedPlayers[playerIndex] = isLocked;
            _playerColors[playerIndex] = playerColor;

            RefreshPlayerIndicator(playerIndex);
            RefreshTileVisual();
        }

        private void ClearAllIndicators()
        {
            for (int i = 0; i < 4; i++)
            {
                _hoveringPlayers[i] = false;
                _lockedPlayers[i] = false;
                _playerColors[i] = Color.clear;
            }

            if (playerIndicators != null)
            {
                foreach (var t in playerIndicators)
                    t?.SetState(false, false, Color.clear);
            }

            RefreshTileVisual();
        }
        
        private void RefreshPlayerIndicator(int playerIndex)
        {
            RobotTilePlayerIndicator indicator = GetIndicator(playerIndex);
            if (indicator == null)
                return;

            bool locked = _lockedPlayers[playerIndex];
            bool hovering = _hoveringPlayers[playerIndex];

            // Locked state has priority over hover state.
            bool visible = locked || hovering;

            indicator.SetState(
                visible,
                locked,
                _playerColors[playerIndex]
            );
        }

        private void RefreshTileVisual()
        {
            bool anyPlayerOnTile = false;

            for (int i = 0; i < 4; i++)
            {
                if (_hoveringPlayers[i] || _lockedPlayers[i])
                {
                    anyPlayerOnTile = true;
                    break;
                }
            }

            if (_buttonVisual != null)
                _buttonVisual.SetExternalHighlighted(anyPlayerOnTile);
        }

        private static bool IsValidPlayerIndex(int playerIndex)
        {
            return playerIndex is >= 0 and < 4;
        }

        private RobotTilePlayerIndicator GetIndicator(int playerIndex)
        {
            if (playerIndicators == null || playerIndex < 0 || playerIndex >= playerIndicators.Length)
                return null;

            return playerIndicators[playerIndex];
        }
    }
}