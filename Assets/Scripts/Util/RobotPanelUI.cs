using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Util
{
    public class RobotPanelUI : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] private TMP_Text sideLabelText;
        
        [Header("Player Header")]
        [SerializeField] private TMP_Text playerNumberText;

        [Header("Camera Controls")]
        [SerializeField] private GameObject cameraRoot;
        [SerializeField] private TMP_Dropdown cameraDropdown;

        [Header("Robot Controls")]
        [SerializeField] private Button previousRobotButton;
        [SerializeField] private Button nextRobotButton;
        [SerializeField] private TMP_Text robotNameText;
        [SerializeField] private Image robotPreviewImage;
        [SerializeField] private GameObject noImagePlaceholder;

        [Header("Spawn Controls")]
        [SerializeField] private TMP_Dropdown spawnDropdown;
        
        [Header("Bumper Controls")]
        [SerializeField] private GameObject vanityBumperRoot;
        [SerializeField] private Toggle vanityBumperToggle;
        
        [Header("Driver Station Controls")]
        [SerializeField] private GameObject driverStationRoot;
        [SerializeField] private TMP_Dropdown driverStationDropdown;

        public event Action OnPreviousRobot;
        public event Action OnNextRobot;
        public event Action<int> OnSpawnChanged;
        public event Action<bool> OnVanityBumperChanged;
        public event Action<int> OnDriverStationChanged;
        public event Action<int> OnCameraChanged;
        
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
            
            if (vanityBumperToggle != null)
                vanityBumperToggle.onValueChanged.AddListener(value => OnVanityBumperChanged?.Invoke(value));
            
            if (driverStationDropdown != null)
                driverStationDropdown.onValueChanged.AddListener(value => OnDriverStationChanged?.Invoke(value));
            
            if (cameraDropdown != null)
                cameraDropdown.onValueChanged.AddListener(value => OnCameraChanged?.Invoke(value));
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
        
        public void SetVanityBumperToggle(bool enabled, bool interactable = true)
        {
            GameObject targetRoot = vanityBumperRoot != null
                ? vanityBumperRoot
                : vanityBumperToggle != null
                    ? vanityBumperToggle.gameObject
                    : null;

            if (targetRoot != null)
                targetRoot.SetActive(interactable);

            if (vanityBumperToggle == null)
                return;

            vanityBumperToggle.SetIsOnWithoutNotify(enabled && interactable);
            vanityBumperToggle.interactable = interactable;
        }
        
        public void SetDriverStationOptions(List<string> options, int selectedIndex, bool visible, bool interactable = true)
        {
            GameObject targetRoot = driverStationRoot != null
                ? driverStationRoot
                : driverStationDropdown != null
                    ? driverStationDropdown.gameObject
                    : null;

            if (targetRoot != null)
                targetRoot.SetActive(visible);

            if (driverStationDropdown == null)
                return;

            driverStationDropdown.ClearOptions();
            driverStationDropdown.AddOptions(options ?? new List<string>());

            int clampedValue = driverStationDropdown.options.Count > 0
                ? Mathf.Clamp(selectedIndex, 0, driverStationDropdown.options.Count - 1)
                : 0;

            driverStationDropdown.SetValueWithoutNotify(clampedValue);
            driverStationDropdown.interactable = visible && interactable;
            driverStationDropdown.RefreshShownValue();
        }
        
        public void SetPlayerNumber(int playerNumber, bool visible)
        {
            if (playerNumberText == null)
                return;

            playerNumberText.gameObject.SetActive(visible);
            playerNumberText.text = $"Player {playerNumber}";
        }

        public void SetCameraOptions(List<string> options, int selectedIndex, bool visible = true, bool interactable = true)
        {
            GameObject targetRoot = cameraRoot != null
                ? cameraRoot
                : cameraDropdown != null
                    ? cameraDropdown.gameObject
                    : null;

            if (targetRoot != null)
                targetRoot.SetActive(visible);

            if (cameraDropdown == null)
                return;

            cameraDropdown.ClearOptions();
            cameraDropdown.AddOptions(options ?? new List<string>());

            int clampedValue = cameraDropdown.options.Count > 0
                ? Mathf.Clamp(selectedIndex, 0, cameraDropdown.options.Count - 1)
                : 0;

            cameraDropdown.SetValueWithoutNotify(clampedValue);
            cameraDropdown.interactable = visible && interactable;
            cameraDropdown.RefreshShownValue();
        }

        private void ApplyAllianceColors(bool isBlue)
        {
            Color allianceColor = isBlue ? BlueAlliance : RedAlliance;
            Color glowColor = WithAlpha(allianceColor, 0.25f);

            if (sideLabelText != null)
                sideLabelText.color = allianceColor;
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