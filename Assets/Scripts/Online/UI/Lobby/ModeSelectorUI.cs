// CloSim Online Multiplayer — host-only match-mode picker (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. Lists every supported (blue, red) match shape from RoomValidation and
// labels them via PlayModeAdapter.ShapeLabel. Only the host may change it; non-hosts see it read-only.
// Built 100% programmatically with LobbyUiKit — no prefab/scene authoring.

using System.Collections.Generic;
using Online.Contracts;
using Online.Rooms;
using TMPro;
using UI.Components;
using UnityEngine;

namespace Online.UI.Lobby
{
    /// <summary>
    /// A <see cref="GamepadDropdown"/> of match shapes. Raises <see cref="OnShapeChosen"/> when the host
    /// changes the selection; <see cref="Reflect"/> syncs the dropdown to a config without re-firing.
    /// </summary>
    public class ModeSelectorUI : MonoBehaviour
    {
        private readonly List<(int blue, int red)> _shapes = new();
        private GamepadDropdown _dropdown;
        private bool _suppress;

        /// <summary>Raised with (blueCount, redCount) when the host picks a different shape.</summary>
        public event System.Action<int, int> OnShapeChosen;

        /// <summary>Builds the mode selector row under <paramref name="parent"/>.</summary>
        public static ModeSelectorUI Create(Transform parent)
        {
            GameObject row = LobbyUiKit.Row(parent, "ModeSelector", 12f, 0, TextAnchor.MiddleLeft);
            LobbyUiKit.SetSize(row, -1, 48);

            var self = row.AddComponent<ModeSelectorUI>();

            TMP_Text caption = LobbyUiKit.Label(row.transform, "Mode", 22, TextAlignmentOptions.Left);
            LobbyUiKit.SetSize(caption.gameObject, 90, -1);

            var options = new List<string>();
            IReadOnlyList<(int blue, int red)> shapes = RoomValidation.SupportedShapes();
            for (int i = 0; i < shapes.Count; i++)
            {
                (int blue, int red) = shapes[i];
                self._shapes.Add((blue, red));
                options.Add(PlayModeAdapter.ShapeLabel(blue, red));
            }

            self._dropdown = LobbyUiKit.Dropdown(row.transform, options, 0);
            LobbyUiKit.FlexibleWidth(self._dropdown.gameObject);
            self._dropdown.onValueChanged.AddListener(self.HandleValueChanged);

            return self;
        }

        private void HandleValueChanged(int index)
        {
            if (_suppress) return;
            if (index < 0 || index >= _shapes.Count) return;
            (int blue, int red) = _shapes[index];
            OnShapeChosen?.Invoke(blue, red);
        }

        /// <summary>Enables the dropdown for the host; disables it (read-only) for everyone else.</summary>
        public void SetInteractable(bool host)
        {
            if (_dropdown != null) _dropdown.interactable = host;
        }

        /// <summary>Selects the shape matching <paramref name="config"/> WITHOUT firing OnShapeChosen.</summary>
        public void Reflect(NetworkMatchConfig config)
        {
            if (_dropdown == null) return;

            int index = -1;
            for (int i = 0; i < _shapes.Count; i++)
            {
                if (_shapes[i].blue == config.blueCount && _shapes[i].red == config.redCount)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0) return;

            _suppress = true;
            _dropdown.value = index;
            _dropdown.RefreshShownValue();
            _suppress = false;
        }
    }
}
