using System.Collections.Generic;
using MysticKombat.Core;
using UnityEngine;

namespace MysticKombat
{
    /// <summary>
    /// Loads generated art from Resources by naming convention (see docs/ASSET_SPEC.md) and builds
    /// procedural stand-ins for anything missing, so the game always runs.
    /// </summary>
    public static class AssetDb
    {
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Dictionary<string, Vector2>> pivots = new Dictionary<string, Dictionary<string, Vector2>>();
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static Font font, mono;

        /// <summary>Monospace font for tables (move list, frame data, stats).</summary>
        public static Font Mono
        {
            get
            {
                if (mono != null) return mono;
                mono = Resources.Load<Font>("Fonts/ShareTechMono-Regular");
                if (mono == null) mono = Font;
                return mono;
            }
        }
        static Sprite white, circle, softCircle;

        public const float StandingPx = 512f;

        public static Font Font
        {
            get
            {
                if (font != null) return font;
                font = Resources.Load<Font>("Fonts/Bangers-Regular");
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Sprite White
        {
            get
            {
                if (white != null) return white;
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color32[16];
                for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                t.SetPixels32(px);
                t.Apply();
                white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
                return white;
            }
        }

        public static Sprite Circle => circle != null ? circle : (circle = MakeRadial(64, 0.92f, false));
        public static Sprite SoftCircle => softCircle != null ? softCircle : (softCircle = MakeRadial(128, 0f, true));

        static Sprite MakeRadial(int size, float edge, bool soft)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r)) / r;
                    float a = soft ? Mathf.Clamp01(1f - d) : (d < edge ? 1f : Mathf.Clamp01((1f - d) / (1f - edge)));
                    if (soft) a = a * a;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        /// <summary>Generic sprite from Resources (pivot centre, 100 ppu).</summary>
        public static Sprite Load(string path, float ppu = 100f)
        {
            string key = path + "@" + ppu;
            if (sprites.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>(path);
            if (tex != null)
            {
                tex.wrapMode = TextureWrapMode.Clamp;
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
            }
            sprites[key] = s;
            return s;
        }

        public static Texture2D Texture(string path) => Resources.Load<Texture2D>(path);

        /// <summary>Fighter pose sprite; pixels-per-unit chosen so a 512 px standing pose is <paramref name="height"/> units tall.</summary>
        public static Sprite Pose(FighterDef f, string pose)
        {
            string key = "F/" + f.Id + "/" + pose;
            if (sprites.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>("Fighters/" + f.Id + "/" + pose);
            if (tex == null && pose != "idle_0")
            {
                // fall back through related poses before using a placeholder
                string alt = pose.StartsWith("idle") ? "idle_0" : pose.StartsWith("walk") ? "idle_0" : pose == "ultimate" ? "special1" : pose.StartsWith("special") ? "heavy" : "idle_0";
                s = Pose(f, alt);
                sprites[key] = s;
                return s;
            }
            if (tex == null)
            {
                s = Placeholder(f);
                sprites[key] = s;
                return s;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            Vector2 pivot = PivotFor(f.Id, pose, tex);
            float ppu = StandingPx / f.Height;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect);
            sprites[key] = s;
            return s;
        }

        public static bool HasArt(FighterDef f) => Resources.Load<Texture2D>("Fighters/" + f.Id + "/idle_0") != null;

        static Vector2 PivotFor(string id, string pose, Texture2D tex)
        {
            if (!pivots.TryGetValue(id, out var map))
            {
                map = new Dictionary<string, Vector2>();
                var ta = Resources.Load<TextAsset>("Fighters/" + id + "/frames");
                if (ta != null)
                {
                    try
                    {
                        var d = MiniJson.Parse(ta.text) as Dictionary<string, object>;
                        foreach (var kv in d)
                        {
                            var o = kv.Value as Dictionary<string, object>;
                            map[kv.Key] = new Vector2(J.F(o, "pivotX", 0.5f), J.F(o, "pivotY", 0.02f));
                        }
                    }
                    catch { /* malformed frames.json: fall back to defaults */ }
                }
                pivots[id] = map;
            }
            if (map.TryGetValue(pose, out var p)) return p;
            return new Vector2(0.5f, 8f / Mathf.Max(16, tex.height));
        }

        public static Sprite Portrait(FighterDef f)
        {
            var s = Load("Fighters/" + f.Id + "/portrait");
            return s != null ? s : Pose(f, "idle_0");
        }

        static Sprite Placeholder(FighterDef f)
        {
            // a simple fighter-coloured silhouette: head + torso + legs
            int w = 160, h = 512;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            ColorUtility.TryParseHtmlString(f.Color, out var c);
            var col = (Color32)c;
            var dark = (Color32)(c * 0.35f);
            dark.a = 255;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float cx = x - w * 0.5f;
                    bool head = (cx * cx + (y - 440) * (y - 440)) < 42 * 42;
                    bool torso = y > 200 && y < 400 && Mathf.Abs(cx) < 52 - (y - 200) * 0.05f;
                    bool legs = y < 210 && (Mathf.Abs(cx - 24) < 18 || Mathf.Abs(cx + 24) < 18);
                    bool arm = y > 260 && y < 380 && cx > 40 && cx < 78;
                    bool on = head || torso || legs || arm;
                    bool edge = on && (x % 40 == 0 || y % 64 == 0);
                    px[y * w + x] = on ? (edge ? dark : col) : new Color32(0, 0, 0, 0);
                }
            t.SetPixels32(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), StandingPx / f.Height);
        }

        public static Sprite Vfx(string name)
        {
            if (string.IsNullOrEmpty(name)) return SoftCircle;
            var s = Load("Vfx/" + name, 256f);
            return s != null ? s : SoftCircle;
        }

        public static bool HasVfx(string name) => !string.IsNullOrEmpty(name) && Load("Vfx/" + name, 256f) != null;

        public static AudioClip Clip(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (clips.TryGetValue(path, out var c)) return c;
            c = Resources.Load<AudioClip>("Audio/" + path);
            clips[path] = c;
            return c;
        }

        public static Color Hex(string hex, Color fallback)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        public static void ClearFighterCache()
        {
            var keys = new List<string>(sprites.Keys);
            foreach (var k in keys) if (k.StartsWith("F/")) sprites.Remove(k);
            pivots.Clear();
        }
    }
}
