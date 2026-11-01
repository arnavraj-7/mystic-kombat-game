using System.Collections.Generic;
using System.IO;
using MysticKombat.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MysticKombat
{
    /// <summary>Animated menu background: key art (if generated) plus rotating mystic mandala rings and rising sparks.</summary>
    public sealed class Backdrop : MonoBehaviour
    {
        readonly List<RectTransform> rings = new List<RectTransform>();
        readonly List<RectTransform> sparks = new List<RectTransform>();
        readonly List<float> sparkSpeed = new List<float>();
        static Sprite ring;

        public static Backdrop Build(Transform parent, string art, float dim = 0.35f, Color? tint = null)
        {
            var root = Ui.Rect(parent, "backdrop").Fill();
            root.SetAsFirstSibling();
            var bg = Ui.Image(root, Ui.Gradient(new Color(0.12f, 0.04f, 0.2f), new Color(0.02f, 0.01f, 0.05f)), Color.white, "grad");
            bg.rectTransform.Fill();
            var s = art != null ? AssetDb.Load(art) : null;
            if (s != null)
            {
                var img = Ui.Image(root, s, tint ?? Color.white, "art");
                img.rectTransform.Fill();
                img.preserveAspect = false;
            }
            var b = root.gameObject.AddComponent<Backdrop>();
            for (int i = 0; i < 3; i++)
            {
                var r = Ui.Image(root, Ring, new Color(1f, 0.7f, 0.25f, 0.18f - i * 0.04f), "ring");
                float size = 900 + i * 380;
                r.rectTransform.Place(0.5f, 0.5f, size, size);
                b.rings.Add(r.rectTransform);
            }
            for (int i = 0; i < 40; i++)
            {
                var sp = Ui.Image(root, AssetDb.SoftCircle, new Color(1f, 0.75f, 0.3f, Random.Range(0.25f, 0.8f)), "spark");
                float sz = Random.Range(6f, 22f);
                sp.rectTransform.Place(Random.value, Random.value, sz, sz);
                b.sparks.Add(sp.rectTransform);
                b.sparkSpeed.Add(Random.Range(0.02f, 0.08f));
            }
            var shade = Ui.Box(root, new Color(0, 0, 0, dim), "dim");
            shade.rectTransform.Fill();
            return b;
        }

        static Sprite Ring
        {
            get
            {
                if (ring != null) return ring;
                int n = 512;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = x - n / 2f + 0.5f, dy = y - n / 2f + 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / (n / 2f);
                        float ang = Mathf.Atan2(dy, dx);
                        float a = 0;
                        a += Band(d, 0.97f, 0.012f) + Band(d, 0.9f, 0.008f) + Band(d, 0.62f, 0.01f) + Band(d, 0.55f, 0.006f);
                        // runic ticks between the outer bands
                        if (d > 0.9f && d < 0.97f) a += Mathf.Clamp01(Mathf.Cos(ang * 48f) * 4f - 3f);
                        // star polygon
                        float star = Mathf.Abs(Mathf.Cos(ang * 4f)) * 0.3f + 0.6f;
                        a += Band(d, star, 0.006f) * 0.8f;
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                    }
                t.SetPixels32(px);
                t.Apply();
                ring = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
                return ring;
            }
        }

        static float Band(float d, float at, float w) => Mathf.Clamp01(1f - Mathf.Abs(d - at) / w);

        void Update()
        {
            float t = Time.unscaledTime;
            for (int i = 0; i < rings.Count; i++)
                rings[i].localRotation = Quaternion.Euler(0, 0, t * (i % 2 == 0 ? 6f : -4f) / (i + 1));
            for (int i = 0; i < sparks.Count; i++)
            {
                var a = sparks[i].anchorMin;
                a.y += sparkSpeed[i] * Time.unscaledDeltaTime;
                a.x += Mathf.Sin(t + i) * 0.0004f;
                if (a.y > 1.05f) { a.y = -0.05f; a.x = Random.value; }
                sparks[i].anchorMin = sparks[i].anchorMax = a;
            }
        }
    }

    /// <summary>Plays the intro cinematic (if generated) then goes to the title.</summary>
    public sealed class IntroScreen : GameScreen
    {
        VideoClipPlayer player;
        float started;

        public override void Enter()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Video", "intro.mp4");
            if (!File.Exists(path)) { App.Go(new TitleScreen()); return; }
            var bg = Ui.Box(Root, Color.black);
            bg.rectTransform.Fill();
            player = VideoClipPlayer.Create(Root, path, false);
            started = Time.unscaledTime;
            var skip = Ui.Text(Root, "PRESS ANY BUTTON TO SKIP", 30, new Color(1, 1, 1, 0.6f)).Outlined();
            skip.rectTransform.Place(1, 0, 600, 60, 1, 0, -30, 30);
        }

        public override void Tick()
        {
            if (player == null) return;
            bool done = player.Finished || Time.unscaledTime - started > 12f;
            if (done || (Time.unscaledTime - started > 0.5f && InputReader.AnyKeyDown()))
            {
                player.Stop();
                App.Go(new TitleScreen());
            }
        }
    }

    public sealed class TitleScreen : GameScreen
    {
        Text press;
        RectTransform logo;
        float t0;

        public override void Enter()
        {
            App.Audio.PlayMusic("menu");
            Backdrop.Build(Root, "UI/title_bg", 0.15f);
            var vignette = Ui.Image(Root, Ui.Gradient(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.85f)), Color.white);
            vignette.rectTransform.Place(0.5f, 0f, 1920, 520, 0.5f, 0f);
            var logoSprite = AssetDb.Load("UI/logo");
            if (logoSprite != null)
            {
                var img = Ui.Image(Root, logoSprite, Color.white, "logo");
                img.preserveAspect = true;
                logo = img.rectTransform.Place(0.5f, 0.62f, 1400, 520);
            }
            else
            {
                var t = Ui.Text(Root, "MYSTIC KOMBAT", 200, Ui.Gold).Outlined(6).Shadowed(10);
                logo = t.rectTransform.Place(0.5f, 0.62f, 1700, 260);
                var sub = Ui.Text(logo, "INCURSION OF LEGENDS", 54, Ui.Cyan).Outlined(3);
                sub.rectTransform.Place(0.5f, 0f, 1200, 80, 0.5f, 1f, 0, 10);
            }
            press = Ui.Text(Root, "PRESS ANY BUTTON", 56, Color.white).Outlined(3);
            press.rectTransform.Place(0.5f, 0.17f, 1200, 90);
            var foot = Ui.Text(Root, "Early access build   |   F11 fullscreen", 24, new Color(1, 1, 1, 0.55f));
            foot.rectTransform.Place(0.5f, 0.03f, 1400, 40);
            if (App.GrimoireError != null)
            {
                var err = Ui.Text(Root, "Grimoire error: " + App.GrimoireError, 26, Color.red).Outlined();
                err.rectTransform.Place(0.5f, 0.09f, 1800, 40);
            }
            t0 = Time.unscaledTime;
        }

        public override void Tick()
        {
            float t = Time.unscaledTime - t0;
            press.color = new Color(1, 1, 1, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(t * 2.4f)));
            if (logo != null) logo.localScale = Vector3.one * (1f + 0.015f * Mathf.Sin(t * 1.6f));
            if (t > 0.4f && InputReader.AnyKeyDown())
            {
                App.Audio.Voice("choose_fighter");
                App.Click("ui_confirm");
                App.Go(new MainMenuScreen());
            }
        }
    }

    public sealed class MainMenuScreen : GameScreen
    {
        MenuList menu;
        Text blurb;
        static int lastIndex;

        static readonly string[] Blurbs =
        {
            "Climb the ladder of rift invaders and face the Void Emperor. High scores are saved.",
            "Two players, one keyboard (or two gamepads). Best of three.",
            "Fight the CPU. Four difficulties from Easy to Mystic (adaptive AI that learns your habits).",
            "Sit back and watch two CPUs duel. Pick each side's fighter and difficulty.",
            "Battle a friend over the local network (lockstep netcode with desync detection).",
            "Training: hitboxes, hurtboxes, input history, frame data inspector, live grimoire reload (F5).",
            "Every fighter's move list with frame data straight from the grimoire.",
            "Controls for keyboard and gamepad.",
            "Volume, round time, cutscenes, CPU difficulty.",
            "Close the portal.",
        };

        public override void Enter()
        {
            App.Audio.PlayMusic("menu");
            Backdrop.Build(Root, "UI/title_bg", 0.55f);
            var title = Ui.Text(Root, "MYSTIC KOMBAT", 110, Ui.Gold).Outlined(5).Shadowed(8);
            title.rectTransform.Place(0.5f, 0.9f, 1600, 150);
            menu = new MenuList(Root, 0.5f, 0.76f, 760, 66, 46);
            menu.Add("ARCADE", () => Start(GameMode.Arcade))
                .Add("VERSUS  (2 PLAYERS)", () => Start(GameMode.Versus))
                .Add("VERSUS CPU", () => Start(GameMode.VsCpu))
                .Add("CPU VS CPU", () => Start(GameMode.CpuVsCpu))
                .Add("LAN BATTLE", () => { App.Click("ui_confirm"); App.Go(new LanScreen()); })
                .Add("TRAINING", () => Start(GameMode.Training))
                .Add("MOVE LIST", () => { App.Click("ui_confirm"); App.Go(new MoveListScreen(null)); })
                .Add("CONTROLS", () => { App.Click("ui_confirm"); App.Go(new ControlsScreen()); })
                .Add("OPTIONS", () => { App.Click("ui_confirm"); App.Go(new OptionsScreen()); })
                .Add("QUIT", Quit);
            menu.Index = lastIndex;
            menu.OnMove = i => App.Click();
            var panel = Ui.Box(Root, Ui.Panel);
            panel.rectTransform.Place(0.5f, 0.045f, 1500, 70);
            blurb = Ui.Text(panel.rectTransform, "", 30, new Color(0.9f, 0.9f, 1f));
            blurb.rectTransform.Fill(20, 0, 20, 0);
        }

        void Start(GameMode mode)
        {
            lastIndex = menu.Index;
            App.Click("ui_confirm");
            var cfg = new MatchConfig { Mode = mode, Level1 = App.Save.CpuLevel, Level2 = App.Save.CpuLevel, Seed = (uint)System.Environment.TickCount };
            switch (mode)
            {
                case GameMode.Versus: break;
                case GameMode.VsCpu: cfg.P2Cpu = true; break;
                case GameMode.CpuVsCpu: cfg.P1Cpu = cfg.P2Cpu = true; break;
                case GameMode.Arcade: cfg.P2Cpu = true; break;
                case GameMode.Training: cfg.P2Cpu = false; break;
            }
            App.Go(new CharSelectScreen(cfg));
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public override void Tick()
        {
            var nav = InputReader.AnyMenu(out _);
            if (nav == InputReader.Nav.Back) { App.Go(new TitleScreen()); return; }
            menu.Handle(nav);
            menu.Refresh();
            blurb.text = Blurbs[Mathf.Clamp(menu.Index, 0, Blurbs.Length - 1)];
        }
    }

    public sealed class OptionsScreen : GameScreen
    {
        MenuList menu;
        static readonly int[] RoundTimes = { 45, 60, 99, 0 };
        static readonly string[] VoiceNames = { "JAPANESE", "ENGLISH", "OFF" };

        public override void Enter()
        {
            Backdrop.Build(Root, "UI/select_bg", 0.6f);
            var title = Ui.Text(Root, "OPTIONS", 96, Ui.Gold).Outlined(4);
            title.rectTransform.Place(0.5f, 0.88f, 1200, 140);
            var s = App.Save;
            menu = new MenuList(Root, 0.5f, 0.74f, 1100, 72, 44);
            menu.Add(() => "MUSIC VOLUME   < " + Mathf.RoundToInt(s.MusicVolume * 10) + " >", null, d => { s.MusicVolume = Mathf.Clamp01(s.MusicVolume + d * 0.1f); App.ApplyVolumes(); })
                .Add(() => "SFX VOLUME   < " + Mathf.RoundToInt(s.SfxVolume * 10) + " >", null, d => { s.SfxVolume = Mathf.Clamp01(s.SfxVolume + d * 0.1f); App.ApplyVolumes(); App.Audio.Sfx("hit_heavy"); })
                .Add(() => "VOICE VOLUME   < " + Mathf.RoundToInt(s.VoiceVolume * 10) + " >", null, d => { s.VoiceVolume = Mathf.Clamp01(s.VoiceVolume + d * 0.1f); App.ApplyVolumes(); App.Audio.Voice("fight"); })
                .Add(() => "CPU DIFFICULTY   < " + CpuBrain.LevelNames[s.CpuLevel] + " >", null, d => s.CpuLevel = (s.CpuLevel + d + 4) % 4)
                .Add(() => "ROUND TIME   < " + (s.RoundSeconds == 0 ? "INFINITE" : s.RoundSeconds + "s") + " >", null, d =>
                {
                    int i = System.Array.IndexOf(RoundTimes, s.RoundSeconds);
                    if (i < 0) i = 2;
                    s.RoundSeconds = RoundTimes[(i + d + RoundTimes.Length) % RoundTimes.Length];
                })
                .Add(() => "ULTIMATE CUTSCENES   < " + (s.Cutscenes ? "ON" : "OFF") + " >", () => s.Cutscenes = !s.Cutscenes, d => s.Cutscenes = !s.Cutscenes)
                .Add(() => "FIGHTER VOICES   < " + VoiceNames[Mathf.Clamp(s.VoiceLang, 0, 2)] + " >", () => { s.VoiceLang = (s.VoiceLang + 1) % 3; App.Audio.FighterVoice(0, "ren", "s3"); },
                     d => { s.VoiceLang = (s.VoiceLang + 3 + d) % 3; App.Audio.FighterVoice(0, "ren", "s3"); })
                .Add(() => "BUTTON HINTS   < " + (s.ShowHints ? "ON" : "OFF") + " >", () => s.ShowHints = !s.ShowHints, d => s.ShowHints = !s.ShowHints)
                .Add(() => "FULLSCREEN   < " + (s.Fullscreen ? "ON" : "OFF") + " >", () => { s.Fullscreen = !s.Fullscreen; UnityEngine.Screen.fullScreen = s.Fullscreen; }, d => { s.Fullscreen = !s.Fullscreen; UnityEngine.Screen.fullScreen = s.Fullscreen; })
                .Add("BACK", Back);
            menu.OnMove = i => App.Click();
        }

        void Back()
        {
            App.Save.Save();
            App.Grimoire.Rules.RoundSeconds = App.Save.RoundSeconds;
            App.Click("ui_back");
            App.Go(new MainMenuScreen());
        }

        public override void Tick()
        {
            var nav = InputReader.AnyMenu(out _);
            if (nav == InputReader.Nav.Back) { Back(); return; }
            menu.Handle(nav);
            menu.Refresh();
        }
    }

    public sealed class ControlsScreen : GameScreen
    {
        public override void Enter()
        {
            Backdrop.Build(Root, "UI/select_bg", 0.65f);
            var title = Ui.Text(Root, "CONTROLS", 96, Ui.Gold).Outlined(4);
            title.rectTransform.Place(0.5f, 0.9f, 1200, 140);
            string[] rows =
            {
                "ACTION|PLAYER 1|PLAYER 2|GAMEPAD",
                "Move / Jump / Crouch|W A S D|Arrow keys|Stick / D-pad",
                "Block|Hold away (crouch = low block)|Hold away|Hold away",
                "Light attack|F|Num1  or  ,|X / Square",
                "Heavy attack|G|Num2  or  .|Y / Triangle",
                "Special 1  (signature)|H   or  236 + Light|Num3  or  /|A / Cross",
                "Special 2|R   or  214 + Light|Num4  or  L|B / Circle",
                "Special 3|T   or  623 + Light|Num5  or  ;|RB",
                "ULTIMATE  (full Ki)|Y   or  236236 + Heavy|Num6  or  '|RT",
                "Throw  (break: same)|V   or  Light+Heavy|Num0  or  R-Shift|LB",
                "Pause|Esc|Esc|Start",
            };
            for (int i = 0; i < rows.Length; i++)
            {
                var cols = rows[i].Split('|');
                var row = Ui.Box(Root, i == 0 ? new Color(0.5f, 0.3f, 1f, 0.5f) : new Color(0.05f, 0.04f, 0.1f, i % 2 == 0 ? 0.75f : 0.6f));
                row.rectTransform.Place(0.5f, 0.8f, 1700, 58, 0.5f, 1f, 0, -i * 60);
                float[] xs = { 0.0f, 0.3f, 0.6f, 0.8f };
                float[] ws = { 0.3f, 0.3f, 0.2f, 0.2f };
                for (int c = 0; c < 4; c++)
                {
                    var t = Ui.Text(row.rectTransform, cols[c], i == 0 ? 34 : 30, i == 0 ? Ui.Gold : Color.white, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
                    t.rectTransform.anchorMin = new Vector2(xs[c], 0);
                    t.rectTransform.anchorMax = new Vector2(xs[c] + ws[c], 1);
                    t.rectTransform.offsetMin = new Vector2(c == 0 ? 24 : 0, 0);
                    t.rectTransform.offsetMax = Vector2.zero;
                }
            }
            string notes = "Solo modes: P1 can also use J K L / U I O (+ ; throw) with the arrow keys.\n" +
                           "Abilities have CHARGES (pips) that recharge over time - some only once per round, like tactical-shooter utility.\n" +
                           "Ki fills by dealing and taking damage and from Ki orbs that drop mid-round. Below 25% HP you enter RAGE: +12% damage and your ultimate costs half.\n" +
                           "Chain Light > Light > Heavy > Special > Ultimate on hit. Throws can be broken by pressing throw right back.";
            var n = Ui.Text(Root, notes, 28, new Color(0.85f, 0.9f, 1f));
            n.rectTransform.Place(0.5f, 0.1f, 1700, 200);
            var back = Ui.Text(Root, "BACK: G / Esc", 30, new Color(1, 1, 1, 0.6f));
            back.rectTransform.Place(1, 0, 400, 50, 1, 0, -30, 20);
        }

        public override void Tick()
        {
            var nav = InputReader.AnyMenu(out _);
            if (nav == InputReader.Nav.Back || nav == InputReader.Nav.Confirm) { App.Click("ui_back"); App.Go(new MainMenuScreen()); }
        }
    }
}
