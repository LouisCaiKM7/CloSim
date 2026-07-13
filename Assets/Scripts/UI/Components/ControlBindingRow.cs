using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Components
{
    public class ControlBindingRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text commandText;
        [SerializeField] private TMP_Dropdown bindingDropdown;
        [SerializeField] private Button defaultButton;

        private Action<int> _onDropdownChanged;
        private Action _onDefaultClicked;
        private bool _isRefreshing;
        
        public string ActionName { get; private set; }

        public GameObject DropdownGameObject => bindingDropdown != null ? bindingDropdown.gameObject : null;
        public GameObject DefaultButtonGameObject => defaultButton != null ? defaultButton.gameObject : null;

        private void Awake()
        {
            if (bindingDropdown != null)
                bindingDropdown.onValueChanged.AddListener(OnDropdownChanged);

            if (defaultButton != null)
                defaultButton.onClick.AddListener(OnDefaultClicked);
        }

        public void Configure(
            string actionName,
            string label,
            System.Collections.Generic.List<string> options,
            int selectedIndex,
            Action<int> onDropdownChanged,
            Action onDefaultClicked)
        {
            ActionName = actionName;
            _onDropdownChanged = onDropdownChanged;
            _onDefaultClicked = onDefaultClicked;

            if (commandText != null)
                commandText.text = label;

            if (bindingDropdown == null)
                return;

            _isRefreshing = true;

            bindingDropdown.ClearOptions();
            bindingDropdown.AddOptions(options ?? new System.Collections.Generic.List<string>());

            if (bindingDropdown.options.Count > 0)
            {
                selectedIndex = Mathf.Clamp(selectedIndex, 0, bindingDropdown.options.Count - 1);
                bindingDropdown.SetValueWithoutNotify(selectedIndex);
            }
            else
            {
                bindingDropdown.SetValueWithoutNotify(0);
            }

            bindingDropdown.RefreshShownValue();
            _isRefreshing = false;
        }

        private void OnDropdownChanged(int index)
        {
            if (_isRefreshing)
                return;

            _onDropdownChanged?.Invoke(index);
        }

        private void OnDefaultClicked()
        {
            _onDefaultClicked?.Invoke();
        }
    }
}