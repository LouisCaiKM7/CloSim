// CloSim Online Multiplayer — programmatic native-Unity UI kit for the lobby (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. Builds uGUI + TMP widgets in code so the whole lobby is 100% native
// in-game UI with NO prefab/scene authoring required (which keeps it compile-safe and avoids Unity-YAML
// merge conflicts across the swarm). Colours mirror the game's RobotSelectDetailPanel alliance tints so
// the lobby matches the existing style.
//
// Every screen builds its hierarchy with these helpers. Keep the API stable — the screen classes depend
// on these exact signatures.

using System.Collections.Generic;
using TMPro;
using UI.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Online.UI.Lobby
{
    /// <summary>Static builders + theme for the native lobby UI.</summary>
    public static class LobbyUiKit
    {
        // ---------------------------------------------------------------- theme (matches game style)

        public static readonly Color PanelBg        = new Color(0.06f, 0.07f, 0.10f, 0.96f);
        public static readonly Color CardBg         = new Color(0.10f, 0.12f, 0.16f, 0.95f);
        public static readonly Color CardBgAlt      = new Color(0.13f, 0.15f, 0.20f, 0.95f);
        public static readonly Color Accent         = new Color(0.20f, 1f, 0.282f, 1f);       // CloSim green
        public static readonly Color BlueAlliance   = new Color(0.05f, 0.18f, 0.45f, 0.85f);
        public static readonly Color RedAlliance    = new Color(0.45f, 0.05f, 0.05f, 0.85f);
        public static readonly Color InactiveAlliance = new Color(0.08f, 0.08f, 0.08f, 0.85f);
        public static readonly Color TextPrimary    = new Color(0.90f, 0.95f, 0.92f, 1f);
        public static readonly Color TextMuted      = new Color(0.60f, 0.66f, 0.63f, 1f);
        public static readonly Color ButtonNormal   = new Color(0.16f, 0.20f, 0.26f, 1f);
        public static readonly Color ButtonAccent   = new Color(0.12f, 0.35f, 0.18f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.14f, 0.15f, 0.17f, 0.6f);
        public static readonly Color Danger         = new Color(0.55f, 0.12f, 0.12f, 1f);

        public static Color AllianceColor(Online.Contracts.RoomAlliance alliance) => alliance switch
        {
            Online.Contracts.RoomAlliance.Blue => BlueAlliance,
            Online.Contracts.RoomAlliance.Red => RedAlliance,
            _ => InactiveAlliance
        };

        // ---------------------------------------------------------------- canvas / event system

        /// <summary>Ensures a screen-space-overlay Canvas + scaler + raycaster and an EventSystem exist.</summary>
        public static Canvas CreateOverlayCanvas(string name)
        {
            EnsureEventSystem();

            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ---------------------------------------------------------------- rect helpers

        /// <summary>Stretches a RectTransform to fill its parent with an optional uniform inset.</summary>
        public static RectTransform Stretch(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
            return rt;
        }

        public static RectTransform RectOf(GameObject go) => (RectTransform)go.transform;

        private static GameObject NewUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        // ---------------------------------------------------------------- containers

        /// <summary>A filled panel (Image) stretched to its parent.</summary>
        public static GameObject Panel(Transform parent, string name, Color color)
        {
            GameObject go = NewUiObject(name, parent);
            var img = go.AddComponent<Image>();
            img.color = color;
            Stretch((RectTransform)go.transform);
            return go;
        }

        /// <summary>A vertical layout container. Add children after; they stack top-to-bottom.</summary>
        public static GameObject Column(Transform parent, string name, float spacing = 10f,
            int padding = 16, TextAnchor align = TextAnchor.UpperCenter)
        {
            GameObject go = NewUiObject(name, parent);
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return go;
        }

        /// <summary>A horizontal layout container.</summary>
        public static GameObject Row(Transform parent, string name, float spacing = 10f,
            int padding = 0, TextAnchor align = TextAnchor.MiddleLeft)
        {
            GameObject go = NewUiObject(name, parent);
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padding, padding, padding, padding);
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return go;
        }

        /// <summary>A card (tinted panel) that also lays its children out vertically.</summary>
        public static GameObject Card(Transform parent, string name, Color color, float spacing = 10f, int padding = 16)
        {
            GameObject go = Column(parent, name, spacing, padding);
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        /// <summary>Adds/updates a LayoutElement with a preferred size (use -1 to leave a dimension unset).</summary>
        public static LayoutElement SetSize(GameObject go, float preferredWidth, float preferredHeight)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (preferredWidth >= 0) le.preferredWidth = preferredWidth;
            if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
            return le;
        }

        public static LayoutElement FlexibleWidth(GameObject go, float weight = 1f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleWidth = weight;
            return le;
        }

        // ---------------------------------------------------------------- widgets

        public static TMP_Text Label(Transform parent, string text, int fontSize = 24,
            TextAlignmentOptions align = TextAlignmentOptions.Center, Color? color = null)
        {
            GameObject go = NewUiObject("Label", parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text ?? "";
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color ?? TextPrimary;
            t.enableWordWrapping = true;
            return t;
        }

        public static Button Button(Transform parent, string text, out TMP_Text label, int fontSize = 24)
        {
            GameObject go = NewUiObject("Button", parent);
            var img = go.AddComponent<Image>();
            img.color = ButtonNormal;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.selectedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor = ButtonDisabled;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            label = Label(go.transform, text, fontSize, TextAlignmentOptions.Center);
            Stretch((RectTransform)label.transform, 6f);

            SetSize(go, -1, 52);
            return btn;
        }

        public static Button Button(Transform parent, string text) => Button(parent, text, out _);

        public static void SetButtonInteractable(Button button, bool interactable)
        {
            if (button == null) return;
            button.interactable = interactable;
        }

        public static void TintButton(Button button, Color color)
        {
            if (button != null && button.targetGraphic is Image img) img.color = color;
        }

        public static TMP_InputField InputField(Transform parent, string placeholder, string initial = "")
        {
            GameObject go = NewUiObject("InputField", parent);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.04f, 0.06f, 1f);

            var input = go.AddComponent<TMP_InputField>();
            SetSize(go, -1, 48);

            // Text area (viewport) + text + placeholder.
            GameObject area = NewUiObject("TextArea", go.transform);
            var areaRect = (RectTransform)area.transform;
            Stretch(areaRect, 8f);
            var mask = area.AddComponent<RectMask2D>();

            TMP_Text text = Label(area.transform, "", 22, TextAlignmentOptions.Left);
            Stretch((RectTransform)text.transform);
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;

            TMP_Text ph = Label(area.transform, placeholder ?? "", 22, TextAlignmentOptions.Left, TextMuted);
            Stretch((RectTransform)ph.transform);
            ph.fontStyle = FontStyles.Italic;

            input.textViewport = areaRect;
            input.textComponent = text;
            input.placeholder = ph;
            input.text = initial ?? "";
            return input;
        }

        public static Toggle Toggle(Transform parent, string labelText, bool initial, out TMP_Text label)
        {
            GameObject go = Row(parent, "Toggle", 8f, 0, TextAnchor.MiddleLeft);
            SetSize(go, -1, 40);

            GameObject box = NewUiObject("Box", go.transform);
            var boxImg = box.AddComponent<Image>();
            boxImg.color = new Color(0.03f, 0.04f, 0.06f, 1f);
            SetSize(box, 32, 32);

            GameObject check = NewUiObject("Check", box.transform);
            var checkImg = check.AddComponent<Image>();
            checkImg.color = Accent;
            Stretch((RectTransform)check.transform, 6f);

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = boxImg;
            toggle.graphic = checkImg;
            toggle.isOn = initial;

            label = Label(go.transform, labelText, 22, TextAlignmentOptions.Left);
            FlexibleWidth(label.gameObject);
            return toggle;
        }

        /// <summary>A gamepad-friendly TMP dropdown (uses the game's GamepadDropdown).</summary>
        public static GamepadDropdown Dropdown(Transform parent, IList<string> options, int selected = 0)
        {
            GameObject go = NewUiObject("Dropdown", parent);
            var bg = go.AddComponent<Image>();
            bg.color = ButtonNormal;
            SetSize(go, -1, 48);

            var dd = go.AddComponent<GamepadDropdown>();
            dd.targetGraphic = bg;

            // Caption label
            TMP_Text caption = Label(go.transform, "", 22, TextAlignmentOptions.Left);
            var capRect = (RectTransform)caption.transform;
            capRect.anchorMin = new Vector2(0, 0);
            capRect.anchorMax = new Vector2(1, 1);
            capRect.offsetMin = new Vector2(12, 6);
            capRect.offsetMax = new Vector2(-30, -6);
            dd.captionText = caption;

            // Template (required for TMP_Dropdown to open a list).
            GameObject template = BuildDropdownTemplate(go.transform, out TMP_Text itemLabel, out Toggle itemToggle);
            dd.template = (RectTransform)template.transform;
            dd.itemText = itemLabel;

            var opts = new List<TMP_Dropdown.OptionData>();
            if (options != null)
                foreach (string o in options) opts.Add(new TMP_Dropdown.OptionData(o));
            dd.options = opts;
            dd.value = Mathf.Clamp(selected, 0, Mathf.Max(0, opts.Count - 1));
            dd.RefreshShownValue();
            return dd;
        }

        private static GameObject BuildDropdownTemplate(Transform parent, out TMP_Text itemLabel, out Toggle itemToggle)
        {
            GameObject template = NewUiObject("Template", parent);
            template.SetActive(false);
            var tRect = (RectTransform)template.transform;
            tRect.anchorMin = new Vector2(0, 0);
            tRect.anchorMax = new Vector2(1, 0);
            tRect.pivot = new Vector2(0.5f, 1f);
            tRect.anchoredPosition = new Vector2(0, 2);
            tRect.sizeDelta = new Vector2(0, 200);
            var tImg = template.AddComponent<Image>();
            tImg.color = CardBg;
            var scroll = template.AddComponent<ScrollRect>();

            GameObject viewport = NewUiObject("Viewport", template.transform);
            var vRect = (RectTransform)viewport.transform;
            vRect.anchorMin = Vector2.zero;
            vRect.anchorMax = Vector2.one;
            vRect.sizeDelta = Vector2.zero;
            vRect.pivot = new Vector2(0, 1);
            viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            GameObject content = NewUiObject("Content", viewport.transform);
            var cRect = (RectTransform)content.transform;
            cRect.anchorMin = new Vector2(0, 1);
            cRect.anchorMax = new Vector2(1, 1);
            cRect.pivot = new Vector2(0.5f, 1f);
            cRect.sizeDelta = new Vector2(0, 48);

            GameObject item = NewUiObject("Item", content.transform);
            var iRect = (RectTransform)item.transform;
            iRect.anchorMin = new Vector2(0, 0.5f);
            iRect.anchorMax = new Vector2(1, 0.5f);
            iRect.sizeDelta = new Vector2(0, 44);
            itemToggle = item.AddComponent<Toggle>();

            GameObject itemBg = NewUiObject("Item Background", item.transform);
            var ibImg = itemBg.AddComponent<Image>();
            ibImg.color = CardBgAlt;
            Stretch((RectTransform)itemBg.transform);
            itemToggle.targetGraphic = ibImg;

            GameObject itemChecked = NewUiObject("Item Checkmark", item.transform);
            var icImg = itemChecked.AddComponent<Image>();
            icImg.color = Accent;
            var icRect = (RectTransform)itemChecked.transform;
            icRect.anchorMin = new Vector2(0, 0);
            icRect.anchorMax = new Vector2(0, 1);
            icRect.sizeDelta = new Vector2(6, 0);
            icRect.pivot = new Vector2(0, 0.5f);
            itemToggle.graphic = icImg;

            itemLabel = Label(item.transform, "Option", 22, TextAlignmentOptions.Left);
            var lRect = (RectTransform)itemLabel.transform;
            lRect.anchorMin = Vector2.zero;
            lRect.anchorMax = Vector2.one;
            lRect.offsetMin = new Vector2(16, 2);
            lRect.offsetMax = new Vector2(-8, -2);

            scroll.content = cRect;
            scroll.viewport = vRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return template;
        }
    }
}
