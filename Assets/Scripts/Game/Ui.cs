using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MysticKombat
{
    /// <summary>Code-only uGUI builders. All screens are assembled at runtime from these.</summary>
    public static class Ui
    {
        public static readonly Color Gold = new Color(1f, 0.78f, 0.25f);
        public static readonly Color Ink = new Color(0.04f, 0.03f, 0.08f, 0.92f);
        public static readonly Color Panel = new Color(0.07f, 0.05f, 0.14f, 0.82f);
        public static readonly Color Mystic = new Color(0.55f, 0.35f, 1f);
        public static readonly Color Cyan = new Color(0.3f, 0.9f, 1f);

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Anchor a rect: anchors as fractions of the parent, size/offset in reference pixels (1920x1080).</summary>
        public static RectTransform Place(this RectTransform rt, float ax, float ay, float w, float h, float px = 0.5f, float py = 0.5f, float ox = 0, float oy = 0)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(px, py);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(ox, oy);
            return rt;
        }

        public static RectTransform Fill(this RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static Image Image(Transform parent, Sprite sprite, Color color, string name = "img")
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image Box(Transform parent, Color color, string name = "box") => Image(parent, AssetDb.White, color, name);

        public static Text Text(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, string name = "txt")
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = AssetDb.Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static Text Outlined(this Text t, float px = 2f, Color? c = null)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = c ?? new Color(0, 0, 0, 0.9f);
            o.effectDistance = new Vector2(px, -px);
            return t;
        }

        public static Text Mono(this Text t)
        {
            t.font = AssetDb.Mono;
            return t;
        }

        public static Text Shadowed(this Text t, float px = 4f)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, 0.75f);
            s.effectDistance = new Vector2(px, -px);
            return t;
        }

        public static Image Fill(Transform parent, Color color, Image.FillMethod method, int origin, string name = "fill")
        {
            var img = Image(parent, AssetDb.White, color, name);
            img.type = UnityEngine.UI.Image.Type.Filled;
            img.fillMethod = method;
            img.fillOrigin = origin;
            img.fillAmount = 1f;
            return img;
        }

        public static Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        /// <summary>Make a rect clickable (mouse) without stealing keyboard focus.</summary>
        public static void OnClick(GameObject go, Action click, Action hover = null)
        {
            var g = go.GetComponent<Graphic>();
            if (g != null) g.raycastTarget = true;
            var h = go.AddComponent<ClickHandler>();
            h.Click = click;
            h.Hover = hover;
        }

        public static Sprite Gradient(Color top, Color bottom)
        {
            var t = new Texture2D(1, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++) t.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(t, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f), 64);
        }

        public static Sprite HGradient(Color left, Color right)
        {
            var t = new Texture2D(64, 1, TextureFormat.RGBA32, false);
            for (int x = 0; x < 64; x++) t.SetPixel(x, 0, Color.Lerp(left, right, x / 63f));
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(t, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f), 64);
        }
    }

    public sealed class ClickHandler : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
    {
        public Action Click, Hover;
        public void OnPointerClick(PointerEventData e) { Click?.Invoke(); }
        public void OnPointerEnter(PointerEventData e) { Hover?.Invoke(); }
    }

    /// <summary>Vertical (or horizontal) list of options driven by keyboard/gamepad nav and the mouse.</summary>
    public sealed class MenuList
    {
        public readonly RectTransform Root;
        readonly List<Text> items = new List<Text>();
        readonly List<Image> bars = new List<Image>();
        readonly List<Action> actions = new List<Action>();
        readonly List<Func<string>> labels = new List<Func<string>>();
        readonly List<Action<int>> adjust = new List<Action<int>>();
        public int Index;
        public Action<int> OnMove;
        readonly int fontSize;
        readonly float rowH;

        public MenuList(Transform parent, float ax, float ay, float width, float rowHeight = 74, int font = 50)
        {
            Root = Ui.Rect(parent, "menu").Place(ax, ay, width, 10, 0.5f, 1f);
            fontSize = font;
            rowH = rowHeight;
        }

        public MenuList Add(string label, Action action, Action<int> onAdjust = null) => Add(() => label, action, onAdjust);

        public MenuList Add(Func<string> label, Action action, Action<int> onAdjust = null)
        {
            int i = items.Count;
            var bar = Ui.Image(Root, Ui.HGradient(new Color(1f, 0.6f, 0.15f, 0.0f), new Color(1f, 0.55f, 0.1f, 0.0f)), Color.white, "bar");
            bar.rectTransform.Place(0.5f, 1f, Root.sizeDelta.x, rowH - 8, 0.5f, 1f, 0, -i * rowH);
            var t = Ui.Text(Root, label(), fontSize, Color.white, TextAnchor.MiddleCenter, "item").Outlined(3);
            t.rectTransform.Place(0.5f, 1f, Root.sizeDelta.x, rowH, 0.5f, 1f, 0, -i * rowH + 4);
            Ui.OnClick(bar.gameObject, () => { Index = i; actions[i]?.Invoke(); }, () => { if (Index != i) { Index = i; OnMove?.Invoke(i); } });
            items.Add(t);
            bars.Add(bar);
            actions.Add(action);
            labels.Add(label);
            adjust.Add(onAdjust);
            Root.sizeDelta = new Vector2(Root.sizeDelta.x, items.Count * rowH);
            return this;
        }

        public int Count => items.Count;

        /// <summary>Handle a nav event; returns true if consumed.</summary>
        public bool Handle(InputReader.Nav nav)
        {
            if (items.Count == 0) return false;
            switch (nav)
            {
                case InputReader.Nav.Up: Index = (Index - 1 + items.Count) % items.Count; OnMove?.Invoke(Index); return true;
                case InputReader.Nav.Down: Index = (Index + 1) % items.Count; OnMove?.Invoke(Index); return true;
                case InputReader.Nav.Left: if (adjust[Index] != null) { adjust[Index](-1); OnMove?.Invoke(Index); return true; } return false;
                case InputReader.Nav.Right: if (adjust[Index] != null) { adjust[Index](1); OnMove?.Invoke(Index); return true; } return false;
                case InputReader.Nav.Confirm: actions[Index]?.Invoke(); return true;
            }
            return false;
        }

        public void Refresh()
        {
            float t = Time.unscaledTime;
            for (int i = 0; i < items.Count; i++)
            {
                bool sel = i == Index;
                items[i].text = sel ? "<color=#FFD45A>" + labels[i]() + "</color>" : labels[i]();
                float pulse = 0.55f + 0.25f * Mathf.Sin(t * 6f);
                bars[i].color = sel ? new Color(1, 1, 1, pulse) : new Color(1, 1, 1, 0);
                bars[i].sprite = sel ? SelSprite : bars[i].sprite;
                items[i].rectTransform.localScale = Vector3.one * (sel ? 1.08f : 1f);
            }
        }

        static Sprite selSprite;
        static Sprite SelSprite => selSprite != null ? selSprite : (selSprite = MakeSel());

        static Sprite MakeSel()
        {
            var t = new Texture2D(128, 1, TextureFormat.RGBA32, false);
            for (int x = 0; x < 128; x++)
            {
                float d = Mathf.Abs(x - 63.5f) / 63.5f;
                t.SetPixel(x, 0, new Color(1f, 0.55f, 0.12f, Mathf.Clamp01(1f - d * d) * 0.85f));
            }
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(t, new Rect(0, 0, 128, 1), new Vector2(0.5f, 0.5f), 128);
        }
    }
}
