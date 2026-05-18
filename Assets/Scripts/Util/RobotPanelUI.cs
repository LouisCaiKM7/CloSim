using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Util
{
    public class RobotPanelUI : MonoBehaviour
    {
        [Header("Panel Visuals")]
        [SerializeField] private Image panelBackground;
        [SerializeField] private Image previewBackground;
        [SerializeField] private Image glowBorder;

        [Header("Header")]
        [SerializeField] private TMP_Text sideLabelText;

        [Header("Robot Controls")]
        [SerializeField] private Button previousRobotButton;
        [SerializeField] private Button nextRobotButton;
        [SerializeField] private TMP_Text robotNameText;
        [SerializeField] private Image robotPreviewImage;
        [SerializeField] private GameObject noImagePlaceholder;

        [Header("Spawn Controls")]
        [SerializeField] private TMP_Dropdown spawnDropdown;

        public event Action OnPreviousRobot;
        public event Action OnNextRobot;
        public event Action<int> OnSpawnChanged;
        
        private static readonly Color BlueAlliance = Hex("#00B7FF");
        private static readonly Color RedAlliance = Hex("#FF3131");

        private void Awake()
        {
            if (previousRobotButton != null)
                previousRobotButton.onClick.AddListener(() => OnPreviousRobot?.Invoke());

            if (nextRobotButton != null)
                nextRobotButton.onClick.AddListener(() => OnNextRobot?.Invoke());

            if (spawnDropdown != null)
                spawnDropdown.onValueChanged.AddListener(value => OnSpawnChanged?.Invoke(value));
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        public void SetSideLabel(string label)
        {
            if (sideLabelText != null)
                sideLabelText.text = label;

            bool isBlue = label != null && label.ToLowerInvariant().Contains("blue");
            ApplyAllianceColors(isBlue);
        }

        public void SetRobotName(string value)
        {
            if (robotNameText != null)
                robotNameText.text = value;
        }

        public void SetRobotPreview(Sprite sprite)
        {
            if (robotPreviewImage != null)
            {
                robotPreviewImage.sprite = sprite;
                robotPreviewImage.enabled = sprite != null;
            }

            if (noImagePlaceholder != null)
                noImagePlaceholder.SetActive(sprite == null);
        }

        public void SetSpawnOptions(List<string> options, int selectedIndex, bool interactable = true)
        {
            if (spawnDropdown == null)
                return;

            spawnDropdown.ClearOptions();
            spawnDropdown.AddOptions(options ?? new List<string>());

            int clampedValue = spawnDropdown.options.Count > 0
                ? Mathf.Clamp(selectedIndex, 0, spawnDropdown.options.Count - 1)
                : 0;

            spawnDropdown.SetValueWithoutNotify(clampedValue);
            spawnDropdown.interactable = interactable;
            spawnDropdown.RefreshShownValue();
        }

        private void ApplyAllianceColors(bool isBlue)
        {
            Color allianceColor = isBlue ? BlueAlliance : RedAlliance;
            Color glowColor = WithAlpha(allianceColor, 0.25f);

            if (sideLabelText != null)
                sideLabelText.color = allianceColor;

            if (glowBorder != null)
                glowBorder.color = glowColor;
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return color;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}