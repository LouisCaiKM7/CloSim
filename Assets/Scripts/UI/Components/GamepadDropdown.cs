using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.Components
{
    public class GamepadDropdown : TMP_Dropdown
    {
        private static readonly System.Reflection.FieldInfo DropdownListField =
            typeof(TMP_Dropdown).GetField("m_Dropdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        public static bool HideAnyOpenDropdown()
        {
            GamepadDropdown[] dropdowns = FindObjectsByType<GamepadDropdown>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            foreach (GamepadDropdown dropdown in dropdowns)
            {
                if (dropdown == null || dropdown.GetThisDropdownsListTransform() == null)
                    continue;

                dropdown.Hide();

                EventSystem eventSystem = EventSystem.current;
                if (eventSystem != null && dropdown.gameObject.activeInHierarchy)
                    eventSystem.SetSelectedGameObject(dropdown.gameObject);

                return true;
            }

            return false;
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            base.OnPointerClick(eventData);
            StartCoroutine(PrepareOpenList());
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            base.OnSubmit(eventData);
            StartCoroutine(PrepareOpenList());
        }

        private IEnumerator PrepareOpenList()
        {
            // TMP creates and lays out the popup after Show(); wait until the hierarchy and
            // layout have settled before selecting and scrolling.
            yield return null;
            yield return null;

            Transform dropdownList = GetThisDropdownsListTransform();
            if (dropdownList == null)
            {
                Debug.LogWarning($"[{nameof(GamepadDropdown)}] Could not resolve this dropdown's list instance on '{name}'.", this);
                yield break;
            }

            ScrollRect scrollRect = dropdownList.GetComponentInChildren<ScrollRect>(true);
            RectTransform content = scrollRect != null ? scrollRect.content : FindDeep(dropdownList, "Content") as RectTransform;
            if (content == null)
                yield break;

            Toggle target = GetSelectedToggle(content);
            if (target == null)
                target = GetFirstActiveToggle(content);

            DropdownAutoScroller scroller = dropdownList.GetComponent<DropdownAutoScroller>();
            if (scroller == null)
                scroller = dropdownList.gameObject.AddComponent<DropdownAutoScroller>();
            else
                scroller.enabled = true;

            if (target == null)
                yield break;

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
                eventSystem.SetSelectedGameObject(target.gameObject);

            Canvas.ForceUpdateCanvases();
            scroller.EnsureVisible((RectTransform)target.transform);
        }

        private static Toggle GetSelectedToggle(RectTransform content)
        {
            Toggle[] toggles = content.GetComponentsInChildren<Toggle>(false);
            foreach (var t in toggles)
            {
                if (t.isOn)
                    return t;
            }

            return null;
        }

        private static Toggle GetFirstActiveToggle(RectTransform content)
        {
            Toggle[] toggles = content.GetComponentsInChildren<Toggle>(false);
            return toggles.Length > 0 ? toggles[0] : null;
        }
        
        private Transform GetThisDropdownsListTransform()
        {
            if (DropdownListField == null)
                return null;

            GameObject listInstance = DropdownListField.GetValue(this) as GameObject;
            return listInstance != null ? listInstance.transform : null;
        }

        private static Transform FindDeep(Transform root, string targetName)
        {
            if (root == null)
                return null;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);

                if (child.name == targetName)
                    return child;

                Transform found = FindDeep(child, targetName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
