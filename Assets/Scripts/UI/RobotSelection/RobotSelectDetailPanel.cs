using System.Collections.Generic;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.RobotSelection
{
    public class RobotSelectDetailPanel : MonoBehaviour
    {
        [Header("Identity")] [SerializeField] private TMP_Text playerLabel;
        [SerializeField] private Image portraitImage;
        [SerializeField] private TMP_Text robotNameText;

        [Header("Status")] [SerializeField] private TMP_Text statusText;
        [SerializeField] private GameObject statusRoot;
        [SerializeField] private GameObject detailsRoot;

        [Header("Alliance")]
        [Tooltip("Assign a background image to tint this panel red or blue based on the player's alliance.")]
        [SerializeField]
        private Image allianceBackgroundImage;

        [SerializeField] private Color blueAllianceColor = new Color(0.05f, 0.18f, 0.45f, 0.75f);
        [SerializeField] private Color redAllianceColor = new Color(0.45f, 0.05f, 0.05f, 0.75f);
        [SerializeField] private Color inactiveAllianceColor = new Color(0.08f, 0.08f, 0.08f, 0.75f);

        [Header("Spawn")] [Tooltip("Assign 5 buttons for quick selection.")] [SerializeField]
        private Button[] spawnButtons;

        [SerializeField] private TMP_Text[] spawnButtonLabels;

        [Header("Vanity Bumpers")]
        [SerializeField] private GameObject vanityBumperRoot;

        [SerializeField] private Button vanityBumperOnButton;
        [SerializeField] private Button vanityBumperOffButton;

        [SerializeField] private TMP_Text vanityBumperOnLabel;
        [SerializeField] private TMP_Text vanityBumperOffLabel;

        private bool _vanityBumperValue;
        
        [Header("Camera")] [SerializeField] private GameObject cameraRoot;
        [SerializeField] private Button firstPersonCameraButton;
        [SerializeField] private Button thirdPersonCameraButton;
        [SerializeField] private Button driverStationCameraButton;

        [Header("Camera Labels")]
        [SerializeField] private TMP_Text firstPersonCameraLabel;
        [SerializeField] private TMP_Text thirdPersonCameraLabel;
        [SerializeField] private TMP_Text driverStationCameraLabel;

        
        [Header("Driver Station Number")]
        [Tooltip("Only visible when the player's camera mode is Driver Station.")]
        [SerializeField]
        private GameObject driverStationRoot;

        [SerializeField] private Button dsButton1;
        [SerializeField] private Button dsButton2;
        [SerializeField] private Button dsButton3;

        [Header("Driver Station Labels")]
        [SerializeField] private TMP_Text dsLabel1;
        [SerializeField] private TMP_Text dsLabel2;
        [SerializeField] private TMP_Text dsLabel3;
        
        [Header("Ready")] [SerializeField] private Button readyButton;

        [Header("Player Color")] [SerializeField]
        private Image colorAccent;
        
        [Header("Selected Option Label Colors")]
        [SerializeField] private Color selectedOptionLabelColor = new Color(0.2f, 1f, 0.282f, 1f);
        [SerializeField] private Color normalOptionLabelColor = new Color(0.847f, 1f, 0.847f, 1f);
        
        public event System.Action<int> OnSpawnChanged;
        public event System.Action<bool> OnVanityBumperChanged;
        public event System.Action<int> OnCameraChanged;
        public event System.Action<StationNum> OnDriverStationChanged;
        public event System.Action OnReadyClicked;

        private readonly List<Selectable> _panelSelectables = new();
        private bool _isRefreshing;
        private bool _panelInputActive;
        private int _selectedControlIndex;
        private DetailPanelMode _mode = DetailPanelMode.Inactive;
        private bool _detailsVisible = true;

        [Header("Panel Navigation")] [SerializeField]
        private float panelNavigationRepeatInterval = 0.18f;

        private int _lastPanelMoveDirection;
        private float _nextPanelMoveTime;

        private void Awake()
        {
            if (spawnButtons != null)
            {
                for (int i = 0; i < spawnButtons.Length; i++)
                {
                    int capturedIndex = i;
                    if (spawnButtons[i] != null)
                        spawnButtons[i].onClick.AddListener(() => OnSpawnButtonClicked(capturedIndex));
                }
            }

            if (vanityBumperOnButton != null)
                vanityBumperOnButton.onClick.AddListener(() => OnVanityBumperButtonClicked(true));

            if (vanityBumperOffButton != null)
                vanityBumperOffButton.onClick.AddListener(() => OnVanityBumperButtonClicked(false));
            
            if (firstPersonCameraButton != null)
                firstPersonCameraButton.onClick.AddListener(() => OnCameraButtonClicked(1));
            if (thirdPersonCameraButton != null)
                thirdPersonCameraButton.onClick.AddListener(() => OnCameraButtonClicked(0));
            if (driverStationCameraButton != null)
                driverStationCameraButton.onClick.AddListener(() => OnCameraButtonClicked(2));

            if (dsButton1 != null) dsButton1.onClick.AddListener(() => OnDsButtonClicked(StationNum.One));
            if (dsButton2 != null) dsButton2.onClick.AddListener(() => OnDsButtonClicked(StationNum.Two));
            if (dsButton3 != null) dsButton3.onClick.AddListener(() => OnDsButtonClicked(StationNum.Three));

            if (readyButton != null)
                readyButton.onClick.AddListener(() => OnReadyClicked?.Invoke());

            gameObject.SetActive(true);
        }

        public void ShowInactive(int playerIndex)
        {
            _mode = DetailPanelMode.Inactive;
            gameObject.SetActive(true);
            SetPanelInputActive(false);
            SetPlayerLabel(playerIndex);
            SetAllianceBackground(false, false);
            SetStatus($"Player {playerIndex + 1}\nNot used in this mode");
            SetIdentity(null, "");
            SetDetailsVisible(false);
        }

        public void ShowJoinPrompt(int playerIndex, DeviceKind deviceKind, bool isBlueAlliance)
        {
            _mode = DetailPanelMode.JoinPrompt;
            gameObject.SetActive(true);
            SetPanelInputActive(false);
            SetPlayerLabel(playerIndex);
            SetAllianceBackground(true, isBlueAlliance);
            SetStatus(deviceKind == DeviceKind.Keyboard ? "Press Enter to Begin" : "Press A to Begin");
            SetIdentity(null, "");
            SetDetailsVisible(false);
        }

        public void ShowSelectingRobot(int playerIndex, Color playerColor, bool isBlueAlliance, Sprite portrait,
            string robotName)
        {
            _mode = DetailPanelMode.SelectingRobot;
            gameObject.SetActive(true);
            SetPanelInputActive(false);
            SetPlayerLabel(playerIndex);
            SetAllianceBackground(true, isBlueAlliance);
            SetPlayerColor(playerColor);
            SetStatus("Choose Robot");
            SetIdentity(portrait, robotName);
            SetDetailsVisible(false);
        }

        public void ShowReady(int playerIndex, Color playerColor, bool isBlueAlliance, Sprite portrait,
            string robotName)
        {
            _mode = DetailPanelMode.Ready;
            gameObject.SetActive(true);
            SetPanelInputActive(false);
            SetPlayerLabel(playerIndex);
            SetAllianceBackground(true, isBlueAlliance);
            SetPlayerColor(playerColor);
            SetStatus("Ready");
            SetIdentity(portrait, robotName);
            SetDetailsVisible(false);
        }

        public void Show(
            int playerIndex,
            Color playerColor,
            bool isBlueAlliance,
            Sprite portrait,
            string robotName,
            List<string> spawnOptions,
            int selectedSpawnIndex,
            bool hasVanityBumper,
            bool vanityBumperOn,
            List<string> cameraOptions,
            int selectedCameraIndex,
            bool showDriverStation,
            StationNum selectedStation)
        {
            _mode = DetailPanelMode.EditingDetails;
            gameObject.SetActive(true);
            SetPanelInputActive(true);
            SetPlayerLabel(playerIndex);
            SetAllianceBackground(true, isBlueAlliance);
            SetPlayerColor(playerColor);
            SetStatus("Configure Robot");
            SetIdentity(portrait, robotName);
            SetDetailsVisible(true);

            _isRefreshing = true;

            RefreshSpawnControls(spawnOptions, selectedSpawnIndex);
            RefreshCameraControls(cameraOptions, selectedCameraIndex);
            RefreshDriverStationControls(selectedStation);

            RefreshVanityBumperControls(hasVanityBumper, vanityBumperOn);

            if (driverStationRoot != null)
                driverStationRoot.SetActive(showDriverStation);
            
            _isRefreshing = false;

            RebuildPanelSelectables();
            ClampSelection();
            ApplySelectionVisuals();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void SetPanelInputActive(bool active)
        {
            if (_panelInputActive == active)
                return;

            _panelInputActive = active;
            _lastPanelMoveDirection = 0;
            _nextPanelMoveTime = 0f;
        }

        public void SelectThirdPersonCameraButton()
        {
            if (_mode != DetailPanelMode.EditingDetails)
                return;

            RebuildPanelSelectables();

            if (thirdPersonCameraButton != null)
            {
                int index = _panelSelectables.IndexOf(thirdPersonCameraButton);
                if (index >= 0)
                    _selectedControlIndex = index;
            }

            ClampSelection();
            ApplySelectionVisuals();
        }

        public void MoveSelection(Vector2 nav)
        {
            if (!_panelInputActive || _mode != DetailPanelMode.EditingDetails)
                return;

            Vector2 direction = GetCardinalDirection(nav);
            if (direction == Vector2.zero)
            {
                _lastPanelMoveDirection = 0;
                return;
            }

            int directionKey = GetDirectionKey(direction);
            float now = Time.unscaledTime;

            if (directionKey == _lastPanelMoveDirection && now < _nextPanelMoveTime)
                return;

            _lastPanelMoveDirection = directionKey;
            _nextPanelMoveTime = now + Mathf.Max(0.05f, panelNavigationRepeatInterval);

            RebuildPanelSelectables();
            if (_panelSelectables.Count == 0)
                return;

            ClampSelection();

            int nextIndex = FindNearestSelectableInDirection(_selectedControlIndex, direction);
            if (nextIndex < 0 || nextIndex == _selectedControlIndex)
                return;

            _selectedControlIndex = nextIndex;
            ApplySelectionVisuals();
        }

        private static Vector2 GetCardinalDirection(Vector2 nav)
        {
            const float deadZone = 0.5f;

            if (nav.sqrMagnitude < deadZone * deadZone)
                return Vector2.zero;

            if (Mathf.Abs(nav.x) > Mathf.Abs(nav.y))
                return nav.x > 0f ? Vector2.right : Vector2.left;

            return nav.y > 0f ? Vector2.up : Vector2.down;
        }

        private static int GetDirectionKey(Vector2 direction)
        {
            if (direction == Vector2.up) return 1;
            if (direction == Vector2.down) return 2;
            if (direction == Vector2.left) return 3;
            if (direction == Vector2.right) return 4;
            return 0;
        }

        private int FindNearestSelectableInDirection(int currentIndex, Vector2 direction)
        {
            if (currentIndex < 0 || currentIndex >= _panelSelectables.Count)
                return -1;

            Selectable current = _panelSelectables[currentIndex];
            if (current == null)
                return -1;

            RectTransform currentRect = current.transform as RectTransform;
            if (currentRect == null)
                return -1;

            Vector2 currentCenter = GetWorldCenter(currentRect);

            int bestIndex = -1;
            float bestScore = float.PositiveInfinity;

            for (int i = 0; i < _panelSelectables.Count; i++)
            {
                if (i == currentIndex)
                    continue;

                Selectable candidate = _panelSelectables[i];
                if (candidate == null || !candidate.gameObject.activeInHierarchy || !candidate.interactable)
                    continue;

                RectTransform candidateRect = candidate.transform as RectTransform;
                if (candidateRect == null)
                    continue;

                Vector2 candidateCenter = GetWorldCenter(candidateRect);
                Vector2 offset = candidateCenter - currentCenter;

                float directionalDistance = Vector2.Dot(offset, direction);

                // Candidate must actually be in the requested direction.
                if (directionalDistance <= 0f)
                    continue;

                float perpendicularDistance = Mathf.Abs(Vector2.Dot(offset, new Vector2(-direction.y, direction.x)));

                // Prefer things aligned in the requested direction.
                // The perpendicular weight prevents jumps to visually unrelated controls.
                float score = directionalDistance + perpendicularDistance * 2.5f;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static Vector2 GetWorldCenter(RectTransform rectTransform)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            Vector3 center = (corners[0] + corners[2]) * 0.5f;
            return new Vector2(center.x, center.y);
        }

        public void SubmitSelection()
        {
            if (!_panelInputActive || _mode != DetailPanelMode.EditingDetails)
                return;

            Selectable selected = GetCurrentPanelSelectable();

            if (selected == null || !selected.IsInteractable())
                return;
            
            if (EventSystem.current != null)
            {
                GameObject selectedObject = selected.gameObject;

                if (EventSystem.current.currentSelectedGameObject != selectedObject)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                    EventSystem.current.SetSelectedGameObject(selectedObject);
                }
            }

            switch (selected)
            {
                case Button button:
                    if (button == vanityBumperOnButton)
                    {
                        SetVanityBumpers(true);
                        return;
                    }

                    if (button == vanityBumperOffButton)
                    {
                        SetVanityBumpers(false);
                        return;
                    }

                    button.onClick.Invoke();
                    break;
            }
        }

        private void SetPlayerLabel(int playerIndex)
        {
            if (playerLabel != null)
                playerLabel.text = $"Player {playerIndex + 1}";
        }

        private void SetPlayerColor(Color playerColor)
        {
            if (colorAccent != null)
                colorAccent.color = playerColor;
        }

        private void SetAllianceBackground(bool activePlayer, bool isBlueAlliance)
        {
            if (allianceBackgroundImage == null)
                return;

            allianceBackgroundImage.color = !activePlayer
                ? inactiveAllianceColor
                : isBlueAlliance
                    ? blueAllianceColor
                    : redAllianceColor;
        }

        private void SetStatus(string message)
        {
            if (statusRoot != null)
                statusRoot.SetActive(!string.IsNullOrWhiteSpace(message));

            if (statusText != null)
                statusText.text = message ?? string.Empty;
        }

        private void SetIdentity(Sprite portrait, string robotName)
        {
            if (portraitImage != null)
            {
                portraitImage.sprite = portrait;
                portraitImage.enabled = portrait != null;
            }

            if (robotNameText != null)
                robotNameText.text = robotName ?? string.Empty;
        }

        private void SetDetailsVisible(bool visible)
        {
            _detailsVisible = visible;

            if (detailsRoot != null)
            {
                detailsRoot.SetActive(visible);
            }
            else
            {
                SetSpawnButtonsVisible(visible);
                if (vanityBumperRoot != null) vanityBumperRoot.SetActive(visible);
                if (cameraRoot != null) cameraRoot.SetActive(visible);
                if (driverStationRoot != null) driverStationRoot.SetActive(visible);
                if (readyButton != null) readyButton.gameObject.SetActive(visible);
            }

            RefreshStatusVisibility();
        }
        
        private void SetCurrentSelectionTo(Selectable selectable)
        {
            if (selectable == null)
                return;

            RebuildPanelSelectables();

            int index = _panelSelectables.IndexOf(selectable);
            if (index < 0)
                return;

            _selectedControlIndex = index;
            ApplySelectionVisuals();
        }

        private void RefreshStatusVisibility()
        {
            bool detailsAreActive = detailsRoot != null
                ? detailsRoot.activeSelf
                : _detailsVisible;

            bool hasStatus = statusText == null || !string.IsNullOrWhiteSpace(statusText.text);
            bool showStatus = !detailsAreActive && hasStatus;

            if (statusRoot != null)
                statusRoot.SetActive(showStatus);

            if (statusText != null)
                statusText.gameObject.SetActive(showStatus);
        }

        private void SetSpawnButtonsVisible(bool visible)
        {
            if (spawnButtons == null)
                return;

            foreach (var t in spawnButtons)
            {
                if (t != null)
                    t.gameObject.SetActive(visible);
            }
        }

        private void RefreshSpawnControls(List<string> spawnOptions, int selectedSpawnIndex)
        {
            int optionCount = spawnOptions?.Count ?? 0;
            bool useButtons = spawnButtons is { Length: > 0 };

            if (!useButtons)
                return;

            for (int i = 0; i < spawnButtons.Length; i++)
            {
                Button button = spawnButtons[i];
                if (button == null)
                    continue;

                bool valid = i < optionCount;
                button.gameObject.SetActive(valid);
                button.interactable = valid;

                if (!valid)
                {
                    TMP_Text invalidLabel = GetSpawnButtonLabel(i);
                    if (invalidLabel != null)
                        SetOptionLabelSelected(invalidLabel, false);

                    continue;
                }

                TMP_Text label = GetSpawnButtonLabel(i);
                if (label != null)
                {
                    if (spawnOptions != null) label.text = GetShortSpawnLabel(spawnOptions[i], i);
                    SetOptionLabelSelected(label, i == selectedSpawnIndex);
                }
            }
        }
        
        private void RefreshVanityBumperControls(bool hasVanityBumper, bool vanityBumperOn)
        {
            _vanityBumperValue = vanityBumperOn && hasVanityBumper;

            if (vanityBumperRoot != null)
                vanityBumperRoot.SetActive(hasVanityBumper);

            if (vanityBumperOnButton != null)
            {
                vanityBumperOnButton.gameObject.SetActive(hasVanityBumper);
                vanityBumperOnButton.interactable = hasVanityBumper;
            }

            if (vanityBumperOffButton != null)
            {
                vanityBumperOffButton.gameObject.SetActive(hasVanityBumper);
                vanityBumperOffButton.interactable = hasVanityBumper;
            }

            RefreshVanityBumperLabels();
        }
        
        private TMP_Text GetVanityLabel(Button button, TMP_Text assignedLabel)
        {
            if (assignedLabel != null)
                return assignedLabel;

            return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        }

        private void RefreshVanityBumperLabels()
        {
            SetOptionLabelSelected(
                GetVanityLabel(vanityBumperOnButton, vanityBumperOnLabel),
                _vanityBumperValue
            );

            SetOptionLabelSelected(
                GetVanityLabel(vanityBumperOffButton, vanityBumperOffLabel),
                !_vanityBumperValue
            );
        }

        private void RefreshCameraControls(List<string> cameraOptions, int selectedCameraIndex)
        {
            bool hasOptions = cameraOptions is { Count: > 0 };

            if (cameraRoot != null)
                cameraRoot.SetActive(hasOptions);

            SetOptionLabelSelected(GetCameraLabel(thirdPersonCameraButton, thirdPersonCameraLabel), selectedCameraIndex == 0);
            SetOptionLabelSelected(GetCameraLabel(firstPersonCameraButton, firstPersonCameraLabel), selectedCameraIndex == 1);
            SetOptionLabelSelected(GetCameraLabel(driverStationCameraButton, driverStationCameraLabel), selectedCameraIndex == 2);
        }
        
        private void RefreshDriverStationControls(StationNum selectedStation)
        {
            SetOptionLabelSelected(GetDsLabel(dsButton1, dsLabel1), selectedStation == StationNum.One);
            SetOptionLabelSelected(GetDsLabel(dsButton2, dsLabel2), selectedStation == StationNum.Two);
            SetOptionLabelSelected(GetDsLabel(dsButton3, dsLabel3), selectedStation == StationNum.Three);
        }

        private TMP_Text GetSpawnButtonLabel(int index)
        {
            if (spawnButtonLabels != null && index >= 0 && index < spawnButtonLabels.Length &&
                spawnButtonLabels[index] != null)
                return spawnButtonLabels[index];

            if (spawnButtons == null || index < 0 || index >= spawnButtons.Length || spawnButtons[index] == null)
                return null;

            return spawnButtons[index].GetComponentInChildren<TMP_Text>(true);
        }
        
        private TMP_Text GetCameraLabel(Button button, TMP_Text assignedLabel)
        {
            if (assignedLabel != null)
                return assignedLabel;

            return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        }

        private TMP_Text GetDsLabel(Button button, TMP_Text assignedLabel)
        {
            if (assignedLabel != null)
                return assignedLabel;

            return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        }

        private void SetOptionLabelSelected(TMP_Text label, bool selected)
        {
            if (label == null)
                return;

            label.color = selected ? selectedOptionLabelColor : normalOptionLabelColor;
        }

        private static string GetShortSpawnLabel(string rawLabel, int index)
        {
            if (string.IsNullOrWhiteSpace(rawLabel))
                return (index + 1).ToString();

            string label = rawLabel.Trim();
            return label.Length <= 7 ? label : (index + 1).ToString();
        }

        private void RebuildPanelSelectables()
        {
            _panelSelectables.Clear();

            AddSelectable(thirdPersonCameraButton);
            AddSelectable(firstPersonCameraButton);
            AddSelectable(driverStationCameraButton);

            if (spawnButtons != null)
            {
                foreach (var t in spawnButtons)
                    AddSelectable(t);
            }

            AddSelectable(vanityBumperOnButton);
            AddSelectable(vanityBumperOffButton);
            AddSelectable(dsButton1);
            AddSelectable(dsButton2);
            AddSelectable(dsButton3);

            AddSelectable(readyButton);

            ClampSelection();
        }

        private void AddSelectable(Selectable selectable)
        {
            if (selectable == null)
                return;

            if (!selectable.gameObject.activeInHierarchy || !selectable.IsInteractable())
                return;

            _panelSelectables.Add(selectable);
        }

        private void ClampSelection()
        {
            if (_panelSelectables.Count == 0)
            {
                _selectedControlIndex = 0;
                return;
            }

            _selectedControlIndex = Mathf.Clamp(_selectedControlIndex, 0, _panelSelectables.Count - 1);
        }
        
        private Selectable GetCurrentPanelSelectable()
        {
            RebuildPanelSelectables();

            if (_panelSelectables.Count == 0)
                return null;

            ClampSelection();

            return _panelSelectables[_selectedControlIndex];
        }

        private void ApplySelectionVisuals()
        {
            if (!_panelInputActive || _mode != DetailPanelMode.EditingDetails)
                return;

            RebuildPanelSelectables();

            if (_panelSelectables.Count == 0)
                return;

            ClampSelection();

            Selectable selected = _panelSelectables[_selectedControlIndex];
            if (selected == null)
                return;

            if (EventSystem.current != null)
            {
                GameObject selectedObject = selected.gameObject;

                if (EventSystem.current.currentSelectedGameObject != selectedObject)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                    EventSystem.current.SetSelectedGameObject(selectedObject);
                }
            }
        }

        private void OnSpawnButtonClicked(int index)
        {
            if (_isRefreshing) return;

            if (spawnButtons != null && index >= 0 && index < spawnButtons.Length)
                SetCurrentSelectionTo(spawnButtons[index]);

            if (spawnButtons != null)
            {
                for (int i = 0; i < spawnButtons.Length; i++)
                    SetOptionLabelSelected(GetSpawnButtonLabel(i), i == index);
            }

            OnSpawnChanged?.Invoke(index);
        }
        
        private void OnVanityBumperButtonClicked(bool value)
        {
            SetCurrentSelectionTo(value ? vanityBumperOnButton : vanityBumperOffButton);
            SetVanityBumpers(value);
        }

        private void SetVanityBumpers(bool value)
        {
            if (_isRefreshing)
                return;

            if (_vanityBumperValue == value)
            {
                RefreshVanityBumperLabels();
                return;
            }

            _vanityBumperValue = value;
            RefreshVanityBumperLabels();
            OnVanityBumperChanged?.Invoke(_vanityBumperValue);
        }

        private void OnCameraButtonClicked(int cameraIndex)
        {
            if (_isRefreshing) return;

            if (cameraIndex == 0)
                SetCurrentSelectionTo(thirdPersonCameraButton);
            else if (cameraIndex == 1)
                SetCurrentSelectionTo(firstPersonCameraButton);
            else if (cameraIndex == 2)
                SetCurrentSelectionTo(driverStationCameraButton);

            SetOptionLabelSelected(GetCameraLabel(thirdPersonCameraButton, thirdPersonCameraLabel), cameraIndex == 0);
            SetOptionLabelSelected(GetCameraLabel(firstPersonCameraButton, firstPersonCameraLabel), cameraIndex == 1);
            SetOptionLabelSelected(GetCameraLabel(driverStationCameraButton, driverStationCameraLabel), cameraIndex == 2);

            OnCameraChanged?.Invoke(cameraIndex);
        }

        private void OnDsButtonClicked(StationNum station)
        {
            if (_isRefreshing) return;

            switch (station)
            {
                case StationNum.One:
                    SetCurrentSelectionTo(dsButton1);
                    break;
                case StationNum.Two:
                    SetCurrentSelectionTo(dsButton2);
                    break;
                case StationNum.Three:
                    SetCurrentSelectionTo(dsButton3);
                    break;
            }

            RefreshDriverStationControls(station);
            OnDriverStationChanged?.Invoke(station);
        }
    }
}