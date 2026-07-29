// CloSim Online Multiplayer — programmatic native-Unity UI kit for the lobby (A3 Rooms & Modes).
// Namespace: Online.UI.Lobby. Builds uGUI + TMP widgets in code so the whole lobby is 100% native
// in-game UI with NO prefab/scene authoring required (which keeps it compile-safe and avoids Unity-YAML
// merge conflicts across the swarm). Colours mirror the game's RobotSelectDetailPanel alliance tints so
// the lobby matches the existing style.
//
// Every screen builds its hierarchy with these helpers. Keep the API stable — the screen classes depend
// on these exact signatures.
//
// DESIGN SYSTEM (UI polish pass): this file is the single source of truth for spacing, type scale and
// widget "chrome" (a procedurally-generated rounded-rect sprite used for cards/buttons/fields instead of
// flat rectangles). Screens should build their layout with Column/Row/Card/HeaderRow/FieldGroup + the
// LayoutElement helpers (SetSize/FlexibleWidth/FlexibleHeight) rather than hardcoded anchoredPosition, so
// resolution changes and dynamic content (lists, wrapped text) never overlap.

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
        public static readonly Color ButtonNormal   = new Color(0.16f, 0.20f, 0.26f, 1f);      // secondary button
        public static readonly Color ButtonAccent   = new Color(0.12f, 0.35f, 0.18f, 1f);      // primary button
        public static readonly Color ButtonDisabled = new Color(0.14f, 0.15f, 0.17f, 0.6f);
        public static readonly Color Danger         = new Color(0.55f, 0.12f, 0.12f, 1f);      // danger button
        public static readonly Color FieldBg        = new Color(0.03f, 0.04f, 0.06f, 1f);
        public static readonly Color DividerColor   = new Color(1f, 1f, 1f, 0.07f);

        public static Color AllianceColor(Online.Contracts.RoomAlliance alliance) => alliance switch
        {
            Online.Contracts.RoomAlliance.Blue => BlueAlliance,
            Online.Contracts.RoomAlliance.Red => RedAlliance,
            _ => InactiveAlliance
        };

        // ---------------------------------------------------------------- design tokens (spacing / type)

        // Spacing scale — use the smallest tier that reads correctly; SpaceXs pairs a caption with its
        // field, SpaceMd/Lg separate unrelated groups (cards, header vs. body, button rows).
        public const float SpaceXs = 4f;
        public const float SpaceSm = 8f;
        public const float SpaceMd = 14f;
        public const float SpaceLg = 22f;
        public const float SpaceXl = 32f;

        public const int PadSm = 12;
        public const int PadMd = 18;
        public const int PadLg = 26;

        // Type scale — five tiers cover every screen: hero (lobby home headline), title (screen headers /
        // card titles), body (buttons, input text, primary readouts), label (field captions, secondary
        // status), caption (fine print / meta like version strings).
        public const int FontHero    = 44;
        public const int FontTitle   = 34;
        public const int FontBody    = 22;
        public const int FontLabel   = 18;
        public const int FontCaption = 15;

        public const float ButtonHeight = 54f;
        public const float FieldHeight  = 50f;
        public const float HeaderHeight = 60f;

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

        // ---------------------------------------------------------------- chrome (rounded-rect sprite)

        // A single procedurally-generated, 9-sliced rounded-rect sprite used for every card/button/field so
        // the UI reads as one system instead of flat rectangles. Built once and cached; no external assets.
        private const int RoundedTexSize = 32;
        private const float RoundedTexRadius = 9f;
        private const float RoundedTexBorder = 11f;
        private static Sprite _roundedSprite;

        private static Sprite RoundedSprite()
        {
            if (_roundedSprite != null) return _roundedSprite;

            var tex = new Texture2D(RoundedTexSize, RoundedTexSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "LobbyUiKit_RoundedRect_Tex"
            };

            Vector2 half = new Vector2(RoundedTexSize * 0.5f, RoundedTexSize * 0.5f);
            var pixels = new Color32[RoundedTexSize * RoundedTexSize];
            for (int y = 0; y < RoundedTexSize; y++)
            {
                for (int x = 0; x < RoundedTexSize; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - half;
                    float dist = RoundedRectSdf(p, half, RoundedTexRadius);
                    float alpha = Mathf.Clamp01(0.5f - dist); // ~1px antialiasing at the rounded edge
                    pixels[y * RoundedTexSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            _roundedSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, RoundedTexSize, RoundedTexSize),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(RoundedTexBorder, RoundedTexBorder, RoundedTexBorder, RoundedTexBorder));
            _roundedSprite.name = "LobbyUiKit_RoundedRect";
            return _roundedSprite;
        }

        /// <summary>Signed distance to a centered rounded rect (Inigo Quilez's box SDF); negative = inside.</summary>
        private static float RoundedRectSdf(Vector2 p, Vector2 halfSize, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - halfSize + new Vector2(radius, radius);
            float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
            return outside + inside - radius;
        }

        /// <summary>Adds/reuses an Image on <paramref name="go"/> filled with the shared rounded-rect chrome.</summary>
        private static Image RoundedImage(GameObject go, Color color)
        {
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            img.sprite = RoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        // ---------------------------------------------------------------- containers

        /// <summary>A flat, square-cornered filled panel (Image) stretched to its parent. Used for full-bleed
        /// backgrounds and scroll viewports where a rounded mask would clip content unevenly.</summary>
        public static GameObject Panel(Transform parent, string name, Color color)
        {
            GameObject go = NewUiObject(name, parent);
            var img = go.AddComponent<Image>();
            img.color = color;
            Stretch((RectTransform)go.transform);
            return go;
        }

        /// <summary>A vertical layout container. Add children after; they stack top-to-bottom.</summary>
        public static GameObject Column(Transform parent, string name, float spacing = SpaceMd,
            int padding = PadMd, TextAnchor align = TextAnchor.UpperCenter)
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
        public static GameObject Row(Transform parent, string name, float spacing = SpaceSm,
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

        /// <summary>A card (rounded, tinted panel) that also lays its children out vertically.</summary>
        public static GameObject Card(Transform parent, string name, Color color, float spacing = SpaceSm, int padding = PadMd)
        {
            GameObject go = Column(parent, name, spacing, padding);
            RoundedImage(go, color);
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

        /// <summary>Marks a child to grow and fill remaining vertical space inside a Column (VerticalLayoutGroup).</summary>
        public static LayoutElement FlexibleHeight(GameObject go, float weight = 1f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleHeight = weight;
            return le;
        }

        /// <summary>An invisible element that grows to fill remaining space in a Column — pushes trailing
        /// content (e.g. a bottom control bar) to the end without hardcoded offsets.</summary>
        public static GameObject Spacer(Transform parent, float weight = 1f)
        {
            GameObject go = Panel(parent, "Spacer", new Color(0f, 0f, 0f, 0f));
            FlexibleHeight(go, weight);
            return go;
        }

        /// <summary>A thin, low-contrast horizontal rule — separates a header from body content.</summary>
        public static GameObject Divider(Transform parent, float height = 2f)
        {
            GameObject go = Panel(parent, "Divider", DividerColor);
            SetSize(go, -1, height);
            return go;
        }

        // ---------------------------------------------------------------- headers

        /// <summary>
        /// Standard screen header: a title (left, growing) that leaves room for trailing action buttons
        /// (Refresh/Back/Exit/etc.) the caller adds to the same row afterwards. Keeps every screen's header
        /// height/font/alignment consistent.
        /// </summary>
        public static GameObject HeaderRow(Transform parent, string title, out TMP_Text titleLabel, float height = HeaderHeight)
        {
            GameObject header = Row(parent, "Header", SpaceMd, 0, TextAnchor.MiddleLeft);
            SetSize(header, -1, height);

            titleLabel = Label(header.transform, title, FontTitle, TextAlignmentOptions.Left);
            FlexibleWidth(titleLabel.gameObject, 1f);

            return header;
        }

        // ---------------------------------------------------------------- widgets

        public static TMP_Text Label(Transform parent, string text, int fontSize = FontBody,
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

        /// <summary>A muted, small caption label used above an input/dropdown/toggle to name the field.</summary>
        public static TMP_Text FieldLabel(Transform parent, string text)
        {
            return Label(parent, text, FontLabel, TextAlignmentOptions.Left, TextMuted);
        }

        /// <summary>
        /// A tightly-spaced caption+control group (small gap within the pair, normal Column spacing between
        /// groups) so related label/field pairs read as one unit instead of a uniform wall of rows. Add the
        /// control (InputField/Dropdown/Toggle) as a child of the returned transform.
        /// </summary>
        public static GameObject FieldGroup(Transform parent, string caption)
        {
            GameObject group = Column(parent, "Field_" + caption, SpaceXs, 0, TextAnchor.UpperLeft);
            FieldLabel(group.transform, caption);
            return group;
        }

        /// <summary>Convenience: a <see cref="FieldGroup"/> wrapping a single <see cref="InputField"/>.</summary>
        public static TMP_InputField LabeledInputField(Transform parent, string caption, string placeholder, string initial = "")
        {
            GameObject group = FieldGroup(parent, caption);
            return InputField(group.transform, placeholder, initial);
        }

        public static Button Button(Transform parent, string text, out TMP_Text label, int fontSize = FontBody)
        {
            GameObject go = NewUiObject("Button", parent);
            Image img = RoundedImage(go, ButtonNormal);
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

            SetSize(go, -1, ButtonHeight);
            return btn;
        }

        public static Button Button(Transform parent, string text) => Button(parent, text, out _);

        /// <summary>A <see cref="Button"/> pre-tinted as the primary/affirmative action (accent green).</summary>
        public static Button PrimaryButton(Transform parent, string text, out TMP_Text label, int fontSize = FontBody)
        {
            Button b = Button(parent, text, out label, fontSize);
            TintButton(b, ButtonAccent);
            return b;
        }

        public static Button PrimaryButton(Transform parent, string text, int fontSize = FontBody) =>
            PrimaryButton(parent, text, out _, fontSize);

        /// <summary>A <see cref="Button"/> left at the neutral secondary tint (explicit alias for intent).</summary>
        public static Button SecondaryButton(Transform parent, string text, out TMP_Text label, int fontSize = FontBody) =>
            Button(parent, text, out label, fontSize);

        public static Button SecondaryButton(Transform parent, string text, int fontSize = FontBody) =>
            Button(parent, text, out _, fontSize);

        /// <summary>A <see cref="Button"/> pre-tinted as a destructive action (leave/disconnect/back-to-menu).</summary>
        public static Button DangerButton(Transform parent, string text, out TMP_Text label, int fontSize = FontBody)
        {
            Button b = Button(parent, text, out label, fontSize);
            TintButton(b, Danger);
            return b;
        }

        public static Button DangerButton(Transform parent, string text, int fontSize = FontBody) =>
            DangerButton(parent, text, out _, fontSize);

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
            RoundedImage(go, FieldBg);

            var input = go.AddComponent<TMP_InputField>();
            SetSize(go, -1, FieldHeight);

            // Text area (viewport) + text + placeholder.
            GameObject area = NewUiObject("TextArea", go.transform);
            var areaRect = (RectTransform)area.transform;
            Stretch(areaRect, 10f);
            var mask = area.AddComponent<RectMask2D>();

            TMP_Text text = Label(area.transform, "", FontBody, TextAlignmentOptions.Left);
            Stretch((RectTransform)text.transform);
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;

            TMP_Text ph = Label(area.transform, placeholder ?? "", FontBody, TextAlignmentOptions.Left, TextMuted);
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
            GameObject go = Row(parent, "Toggle", SpaceSm, 0, TextAnchor.MiddleLeft);
            SetSize(go, -1, 40);

            GameObject box = NewUiObject("Box", go.transform);
            RoundedImage(box, FieldBg);
            SetSize(box, 32, 32);

            GameObject check = NewUiObject("Check", box.transform);
            Image checkImg = RoundedImage(check, Accent);
            Stretch((RectTransform)check.transform, 6f);

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.graphic = checkImg;
            toggle.isOn = initial;

            label = Label(go.transform, labelText, FontLabel, TextAlignmentOptions.Left);
            FlexibleWidth(label.gameObject);
            return toggle;
        }

        /// <summary>A gamepad-friendly TMP dropdown (uses the game's GamepadDropdown).</summary>
        public static GamepadDropdown Dropdown(Transform parent, IList<string> options, int selected = 0)
        {
            GameObject go = NewUiObject("Dropdown", parent);
            Image bg = RoundedImage(go, ButtonNormal);
            SetSize(go, -1, FieldHeight);

            var dd = go.AddComponent<GamepadDropdown>();
            dd.targetGraphic = bg;

            // Caption label
            TMP_Text caption = Label(go.transform, "", FontBody, TextAlignmentOptions.Left);
            var capRect = (RectTransform)caption.transform;
            capRect.anchorMin = new Vector2(0, 0);
            capRect.anchorMax = new Vector2(1, 1);
            capRect.offsetMin = new Vector2(14, 6);
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
            RoundedImage(template, CardBg);
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

            itemLabel = Label(item.transform, "Option", FontBody, TextAlignmentOptions.Left);
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
