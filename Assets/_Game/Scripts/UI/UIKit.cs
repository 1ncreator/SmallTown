using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SmallTown.UI
{
    /// <summary>Small factory for the light, rounded, minimal uGUI look (everything built from code).</summary>
    public static class UIKit
    {
        public static readonly Color Ink = new Color(0.14f, 0.16f, 0.21f);
        public static readonly Color Muted = new Color(0.38f, 0.41f, 0.48f);
        public static readonly Color Accent = new Color(0.24f, 0.49f, 0.95f);
        public static readonly Color AccentSoft = new Color(0.90f, 0.94f, 1f);
        public static readonly Color PanelColor = new Color(1f, 1f, 1f, 0.97f);
        public static readonly Color Chip = new Color(0.95f, 0.96f, 0.975f, 1f);
        public static readonly Color ChipHover = new Color(0.90f, 0.93f, 0.98f, 1f);
        public static readonly Color Danger = new Color(0.86f, 0.28f, 0.25f);
        public static readonly Color Good = new Color(0.20f, 0.62f, 0.38f);
        public static readonly Color DividerColor = new Color(0.9f, 0.91f, 0.93f);

        public static Font Font { get; private set; }
        public static Sprite Rounded { get; private set; }
        public static Sprite Shadow { get; private set; }
        public static Sprite Circle { get; private set; }

        public static void Init()
        {
            if (Font != null) return;
            try
            {
                Font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Segoe UI Symbol", "Arial", "Helvetica", "Roboto", "Noto Sans" }, 32);
            }
            catch (Exception)
            {
                Font = null;
            }
            if (Font == null) Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Rounded = MakeRounded(64, 24f, 0f, 28);
            Shadow = MakeRounded(128, 30f, 26f, 60);
            Circle = MakeRounded(64, 32f, 0f, 31);
        }

        private static Sprite MakeRounded(int size, float radius, float blur, int border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UIRounded" };
            var px = new Color32[size * size];
            float inset = blur;
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f - half, fy = y + 0.5f - half;
                    float bx = half - inset - radius, by = half - inset - radius;
                    float qx = Mathf.Abs(fx) - bx, qy = Mathf.Abs(fy) - by;
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float a;
                    if (blur > 0f)
                    {
                        float t = Mathf.Clamp01(1f - (outside + 2f) / blur);
                        a = t * t * (3f - 2f * t);
                    }
                    else a = Mathf.Clamp01(0.5f - outside);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static void Stretch(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>White rounded panel with a soft shadow.</summary>
        public static Image Panel(Transform parent, string name, bool shadow = true, float cornerScale = 2f)
        {
            var rt = Rect(name, parent);
            if (shadow)
            {
                var sh = Rect("Shadow", rt);
                sh.anchorMin = Vector2.zero;
                sh.anchorMax = Vector2.one;
                sh.offsetMin = new Vector2(-22f, -28f);
                sh.offsetMax = new Vector2(22f, 16f);
                var si = sh.gameObject.AddComponent<Image>();
                si.sprite = Shadow;
                si.type = Image.Type.Sliced;
                si.pixelsPerUnitMultiplier = 1.4f;
                si.color = new Color(0.1f, 0.13f, 0.2f, 0.16f);
                si.raycastTarget = false;
                sh.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = cornerScale;
            img.color = PanelColor;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static Button Button(Transform parent, string text, UnityAction onClick, int fontSize = 16, Color? bg = null, Color? fg = null, float minWidth = 0f, float height = 40f)
        {
            var rt = Rect("Button_" + text, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 2.4f;
            img.color = bg ?? Chip;
            var b = rt.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 0.96f, 1f);
            colors.pressedColor = new Color(0.82f, 0.87f, 0.96f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            var label = Label(rt, text, fontSize, fg ?? Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(12f, 0f);
            label.rectTransform.offsetMax = new Vector2(-12f, 0f);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            float w = Mathf.Max(minWidth, label.preferredWidth + 28f);
            le.minWidth = w;
            le.preferredWidth = w;
            return b;
        }

        public static void SetButtonText(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
            var le = b.GetComponent<LayoutElement>();
            if (le != null && t != null)
            {
                float w = t.preferredWidth + 28f;
                le.preferredWidth = Mathf.Max(le.minWidth, w);
            }
        }

        public static HorizontalLayoutGroup HLayout(GameObject go, float spacing, RectOffset padding, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding;
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return h;
        }

        public static VerticalLayoutGroup VLayout(GameObject go, float spacing, RectOffset padding, TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding;
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static LayoutElement Size(Component c, float w = -1f, float h = -1f)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0f) { le.preferredWidth = w; le.minWidth = w; }
            if (h >= 0f) { le.preferredHeight = h; le.minHeight = h; }
            return le;
        }

        public static Image Divider(Transform parent)
        {
            var rt = Rect("Divider", parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = DividerColor;
            img.raycastTarget = false;
            Size(img, -1f, 1f);
            return img;
        }

        public static Slider Slider(Transform parent, float min, float max, float value, UnityAction<float> onChange)
        {
            var rt = Rect("Slider", parent);
            Size(rt, -1f, 28f);
            var bg = Rect("Track", rt);
            bg.anchorMin = new Vector2(0f, 0.5f);
            bg.anchorMax = new Vector2(1f, 0.5f);
            bg.sizeDelta = new Vector2(0f, 8f);
            var bgi = bg.gameObject.AddComponent<Image>();
            bgi.sprite = Rounded;
            bgi.type = Image.Type.Sliced;
            bgi.pixelsPerUnitMultiplier = 6f;
            bgi.color = new Color(0.88f, 0.9f, 0.93f);
            var fillArea = Rect("FillArea", rt);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(-16f, 8f);
            var fill = Rect("Fill", fillArea);
            fill.sizeDelta = Vector2.zero;
            var fi = fill.gameObject.AddComponent<Image>();
            fi.sprite = Rounded;
            fi.type = Image.Type.Sliced;
            fi.pixelsPerUnitMultiplier = 6f;
            fi.color = Accent;
            var handleArea = Rect("HandleArea", rt);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            var handle = Rect("Handle", handleArea);
            handle.sizeDelta = new Vector2(22f, 22f);
            var hi = handle.gameObject.AddComponent<Image>();
            hi.sprite = Circle;
            hi.color = Color.white;
            var shadow = handle.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.25f);
            shadow.effectDistance = new Vector2(0f, -1.5f);
            var s = rt.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = hi;
            s.minValue = min;
            s.maxValue = max;
            s.SetValueWithoutNotify(value);
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            return s;
        }

        public static InputField Input(Transform parent, string placeholder, int fontSize)
        {
            var rt = Rect("Input", parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 2f;
            img.color = new Color(0.965f, 0.97f, 0.98f);
            var text = Label(rt, "", fontSize, Ink);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(18f, 4f);
            text.rectTransform.offsetMax = new Vector2(-18f, -4f);
            var ph = Label(rt, placeholder, fontSize, new Color(0.6f, 0.63f, 0.68f));
            ph.fontStyle = FontStyle.Italic;
            Stretch(ph.rectTransform);
            ph.rectTransform.offsetMin = new Vector2(18f, 4f);
            ph.rectTransform.offsetMax = new Vector2(-18f, -4f);
            var input = rt.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = img;
            input.lineType = InputField.LineType.SingleLine;
            input.caretColor = Accent;
            input.selectionColor = new Color(0.24f, 0.49f, 0.95f, 0.25f);
            input.customCaretColor = true;
            input.caretWidth = 2;
            return input;
        }
    }
}
