using System.Collections.Generic;
using System.Linq;
using MysticKombat.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MysticKombat
{
    public sealed class CharSelectScreen : GameScreen
    {
        readonly MatchConfig cfg;
        List<FighterDef> roster;
        readonly List<RectTransform> tiles = new List<RectTransform>();
        const int Cols = 6;
        readonly int[] cursor = { 0, 1 };
        readonly bool[] locked = new bool[2];
        readonly int[] level = new int[2];
        int step;                 // 0 picking, 1 difficulty, 2 done
        readonly SidePanel[] side = new SidePanel[2];
        readonly Image[] frame = new Image[2];
        Text hint, header;
        float enterTime;
        int RandomIndex => roster.Count;  // the "?" tile

        public CharSelectScreen(MatchConfig cfg) { this.cfg = cfg; }

        sealed class SidePanel
        {
            public RectTransform Root;
            public Image Pose, Glow;
            public Text Name, Title, Info, Level;
            public Image[] Bars = new Image[4];
            public int Shown = -2;
            public float Pop;
        }

        public override void Enter()
        {
            enterTime = Time.unscaledTime;
            App.Audio.PlayMusic("select");
            Backdrop.Build(Root, "UI/select_bg", 0.45f);
            roster = App.Grimoire.Fighters.Where(f => !f.Boss || App.Save.BossUnlocked || cfg.Mode == GameMode.Training || cfg.Mode == GameMode.CpuVsCpu).ToList();
            level[0] = cfg.Level1; level[1] = cfg.Level2;

            header = Ui.Text(Root, Header(), 70, Ui.Gold).Outlined(4);
            header.rectTransform.Place(0.5f, 0.95f, 1600, 100);

            // grid
            int count = roster.Count + 1;
            int rows = (count + Cols - 1) / Cols;
            float tw = 150, th = 150, gap = 10;
            var grid = Ui.Rect(Root, "grid").Place(0.5f, 0.05f, Cols * (tw + gap), rows * (th + gap), 0.5f, 0f);
            for (int i = 0; i < count; i++)
            {
                int r = i / Cols, c = i % Cols;
                int inRow = Mathf.Min(Cols, count - r * Cols);
                float x = (c - (inRow - 1) * 0.5f) * (tw + gap);
                var tile = Ui.Box(grid, new Color(0.08f, 0.05f, 0.15f, 0.95f), "tile").rectTransform;
                tile.Place(0.5f, 1f, tw, th, 0.5f, 1f, x, -r * (th + gap));
                if (i < roster.Count)
                {
                    var f = roster[i];
                    var col = AssetDb.Hex(f.Color, Color.white);
                    var stripe = Ui.Box(tile, new Color(col.r, col.g, col.b, 0.45f));
                    stripe.rectTransform.Fill();
                    var mask = Ui.Rect(tile, "mask").Fill(4, 4, 4, 4);
                    mask.gameObject.AddComponent<RectMask2D>();
                    var portrait = Ui.Image(mask, AssetDb.Portrait(f), Color.white, "p");
                    portrait.preserveAspect = true;
                    portrait.rectTransform.Fill();
                    var name = Ui.Text(tile, f.Name.Split(' ')[0].ToUpper(), 24, Color.white).Outlined(2);
                    name.rectTransform.Place(0.5f, 0f, tw, 30, 0.5f, 0f, 0, 2);
                }
                else
                {
                    var q = Ui.Text(tile, "?", 110, Ui.Gold).Outlined(3);
                    q.rectTransform.Fill();
                    var rn = Ui.Text(tile, "RANDOM", 24, Color.white).Outlined(2);
                    rn.rectTransform.Place(0.5f, 0f, tw, 30, 0.5f, 0f, 0, 2);
                }
                int idx = i;
                Ui.OnClick(tile.gameObject, () => MouseClick(idx), () => MouseHover(idx));
                tiles.Add(tile);
            }
            for (int p = 0; p < 2; p++)
            {
                frame[p] = Ui.Image(Root, Ring(), p == 0 ? new Color(1f, 0.45f, 0.1f) : new Color(0.2f, 0.65f, 1f), "cursor" + p);
                frame[p].type = Image.Type.Sliced;
                frame[p].rectTransform.sizeDelta = new Vector2(166, 166);
                var tag = Ui.Text(frame[p].rectTransform, p == 0 ? "P1" : (cfg.IsHuman(1) ? "P2" : "CPU"), 30, frame[p].color).Outlined(2);
                tag.rectTransform.Place(p == 0 ? 0f : 1f, 1f, 70, 36, p == 0 ? 0f : 1f, 0f, 0, -4);
                side[p] = BuildSide(p);
            }
            if (cfg.Mode == GameMode.Arcade) { cursor[1] = -1; side[1].Root.gameObject.SetActive(false); frame[1].gameObject.SetActive(false); }
            if (cfg.Mode == GameMode.Lan) { }

            hint = Ui.Text(Root, "", 28, new Color(1, 1, 1, 0.8f)).Outlined(2);
            hint.rectTransform.Place(0.5f, 0.012f, 1800, 40, 0.5f, 0f);
            foreach (var p in new[] { 0, 1 })
                if (cursor[p] >= 0) cursor[p] = Mathf.Clamp(cursor[p], 0, roster.Count - 1);
        }

        string Header()
        {
            switch (cfg.Mode)
            {
                case GameMode.Arcade: return "ARCADE - CHOOSE YOUR FIGHTER";
                case GameMode.Training: return "TRAINING - FIGHTER & DUMMY";
                case GameMode.CpuVsCpu: return "CPU VS CPU - PICK BOTH SIDES";
                case GameMode.VsCpu: return "VERSUS CPU";
                default: return "CHOOSE YOUR FIGHTER";
            }
        }

        static Sprite ringSprite;
        static Sprite Ring()
        {
            if (ringSprite != null) return ringSprite;
            int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool edge = x < 6 || y < 6 || x >= n - 6 || y >= n - 6;
                    t.SetPixel(x, y, edge ? Color.white : new Color(1, 1, 1, 0));
                }
            t.Apply();
            ringSprite = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
            return ringSprite;
        }

        SidePanel BuildSide(int p)
        {
            var s = new SidePanel();
            float ax = p == 0 ? 0f : 1f;
            // one column per side, kept clear of the centred fighter grid (grid spans x 480..1440)
            s.Root = Ui.Rect(Root, "side" + p).Place(ax, 0f, 460, 1010, ax, 0f, p == 0 ? 20 : -20, 30);
            s.Glow = Ui.Image(s.Root, AssetDb.SoftCircle, new Color(1, 1, 1, 0.35f), "glow");
            s.Glow.rectTransform.Place(0.5f, 0f, 560, 560, 0.5f, 0f, 0, 440);
            s.Pose = Ui.Image(s.Root, null, Color.white, "pose");
            s.Pose.preserveAspect = true;
            s.Pose.rectTransform.Place(0.5f, 0f, 460, 520, 0.5f, 0f, 0, 480);
            if (p == 1) s.Pose.rectTransform.localScale = new Vector3(-1, 1, 1);
            s.Level = Ui.Text(s.Root, "", 38, Ui.Cyan).Outlined(3);
            s.Level.rectTransform.Place(0.5f, 0f, 460, 48, 0.5f, 0f, 0, 432);
            s.Name = Ui.Text(s.Root, "", 54, Color.white).Outlined(3).Shadowed(5);
            s.Name.rectTransform.Place(0.5f, 0f, 460, 64, 0.5f, 0f, 0, 362);
            s.Title = Ui.Text(s.Root, "", 25, Ui.Gold).Outlined(2);
            s.Title.rectTransform.Place(0.5f, 0f, 460, 34, 0.5f, 0f, 0, 328);
            string[] labels = { "HEALTH", "POWER", "SPEED", "KI GAIN" };
            for (int i = 0; i < 4; i++)
            {
                var lab = Ui.Text(s.Root, labels[i], 21, new Color(0.85f, 0.85f, 1f), TextAnchor.MiddleLeft);
                lab.rectTransform.Place(0f, 0f, 110, 26, 0f, 0.5f, 14, 300 - i * 28);
                var bg = Ui.Box(s.Root, new Color(0, 0, 0, 0.6f));
                bg.rectTransform.Place(0f, 0f, 316, 14, 0f, 0.5f, 130, 300 - i * 28);
                s.Bars[i] = Ui.Fill(bg.rectTransform, Ui.Gold, Image.FillMethod.Horizontal, 0);
                s.Bars[i].rectTransform.Fill();
            }
            var infoBg = Ui.Box(s.Root, new Color(0.03f, 0.02f, 0.08f, 0.7f));
            infoBg.rectTransform.Place(0.5f, 0f, 460, 168, 0.5f, 0f, 0, 0);
            s.Info = Ui.Text(s.Root, "", 20, Color.white, TextAnchor.MiddleLeft).Outlined(1);
            s.Info.rectTransform.Place(0.5f, 0f, 436, 160, 0.5f, 0f, 0, 4);
            return s;
        }

        void ShowSide(int p)
        {
            var s = side[p];
            int idx = cursor[p];
            if (idx < 0) return;
            if (s.Shown != idx)
            {
                s.Shown = idx;
                s.Pop = 1f;
                if (idx >= roster.Count)
                {
                    s.Pose.sprite = null;
                    s.Pose.color = new Color(0, 0, 0, 0);
                    s.Name.text = "RANDOM";
                    s.Title.text = "Let the multiverse decide";
                    s.Info.text = "";
                    foreach (var b in s.Bars) b.fillAmount = 0;
                    s.Glow.color = new Color(1, 0.8f, 0.3f, 0.3f);
                }
                else
                {
                    var f = roster[idx];
                    s.Pose.sprite = AssetDb.Pose(f, "idle_0");
                    s.Pose.color = Color.white;
                    s.Name.text = f.Name.ToUpper();
                    s.Title.text = f.Title + "   |   " + f.Archetype;
                    var col = AssetDb.Hex(f.Color, Color.white);
                    s.Glow.color = new Color(col.r, col.g, col.b, 0.45f);
                    s.Bars[0].fillAmount = Mathf.InverseLerp(800, 1550, f.MaxHp);
                    s.Bars[1].fillAmount = Mathf.InverseLerp(0.9f, 1.2f, f.Power);
                    s.Bars[2].fillAmount = Mathf.InverseLerp(2.8f, 4.0f, f.WalkSpeed);
                    s.Bars[3].fillAmount = Mathf.InverseLerp(0.8f, 1.25f, f.KiGain);
                    var lines = new List<string>();
                    for (int i = 0; i < 3; i++)
                    {
                        var m = f.Move(Fighter.SpecialSlots[i]);
                        if (m == null) continue;
                        string tag = m.Recharge > 0 ? $"{Mathf.Max(1, m.Charges)}x, {m.Recharge / 60f:0.#}s" : $"{Mathf.Max(1, m.Charges)}x per round";
                        if (m.HealAmount > 0) tag += ", heal";
                        lines.Add($"<color=#FFD45A>S{i + 1}</color> {m.Name}  <color=#9FB4FF>({tag})</color>");
                    }
                    var u = f.Move("ultimate");
                    if (u != null) lines.Add($"<color=#FF6A3D>ULT</color> {u.Name}");
                    s.Info.text = string.Join("\n", lines);
                }
            }
            s.Pop = Mathf.MoveTowards(s.Pop, 0, Time.unscaledDeltaTime * 4f);
            float bob = Mathf.Sin(Time.unscaledTime * 2f + p) * 6f;
            s.Pose.rectTransform.anchoredPosition = new Vector2(0, 480 + bob);
            float sc = 1f + s.Pop * 0.08f;
            s.Pose.rectTransform.localScale = new Vector3(p == 1 ? -sc : sc, sc, 1);
            bool cpu = !cfg.IsHuman(p);
            s.Level.text = cpu && (step == 1 || locked[p]) ? "CPU: < " + CpuBrain.LevelNames[level[p]] + " >" : locked[p] ? "READY!" : "";
        }

        int Controller(int slot)
        {
            // which physical player drives this slot's cursor
            if (cfg.Mode == GameMode.Versus) return slot;
            return 0;
        }

        int ActiveSlot()
        {
            if (cfg.Mode == GameMode.Versus) return -1; // both simultaneously
            if (!locked[0]) return 0;
            if (cursor[1] >= 0 && !locked[1]) return 1;
            return -1;
        }

        void MouseHover(int idx)
        {
            int slot = ActiveSlot();
            if (slot < 0) slot = locked[0] ? 1 : 0;
            if (cursor[slot] < 0 || locked[slot]) return;
            if (cursor[slot] != idx) { cursor[slot] = idx; App.Click(); }
        }

        void MouseClick(int idx)
        {
            int slot = ActiveSlot();
            if (slot < 0) slot = locked[0] ? 1 : 0;
            if (cursor[slot] < 0 || locked[slot]) return;
            cursor[slot] = idx;
            Lock(slot);
        }

        void Lock(int slot)
        {
            if (cursor[slot] >= roster.Count) cursor[slot] = Random.Range(0, roster.Count(f => !f.Boss));
            locked[slot] = true;
            var f = roster[cursor[slot]];
            side[slot].Pose.sprite = AssetDb.Pose(f, "victory");
            side[slot].Pop = 1.6f;
            App.Click("ui_confirm");
            App.Audio.Voice("name_" + f.Id);
        }

        void Move(int slot, InputReader.Nav nav)
        {
            int n = roster.Count + 1;
            int c = cursor[slot];
            int dc = nav == InputReader.Nav.Left ? -1 : nav == InputReader.Nav.Right ? 1 : 0;
            int dr = nav == InputReader.Nav.Up ? -1 : nav == InputReader.Nav.Down ? 1 : 0;
            if (dc != 0) c = (c + dc + n) % n;
            if (dr != 0)
            {
                int nc = c + dr * Cols;
                if (nc >= 0 && nc < n) c = nc;
                else if (dr > 0) c = n - 1;
            }
            if (c != cursor[slot]) { cursor[slot] = c; App.Click(); }
        }

        public override void Tick()
        {
            if (Time.unscaledTime - enterTime < 0.15f) return;
            for (int p = 0; p < 2; p++)
            {
                if (cursor[p] < 0) continue;
                var t = tiles[Mathf.Clamp(cursor[p], 0, tiles.Count - 1)];
                // tiles pivot at their top edge; centre the ring on the tile's visual centre
                frame[p].rectTransform.position = t.TransformPoint(t.rect.center);
                float pulse = locked[p] ? 1f : 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f));
                var c = frame[p].color; c.a = pulse; frame[p].color = c;
                frame[p].rectTransform.localScale = Vector3.one * (p == 1 && cursor[0] == cursor[1] ? 1.08f : 1f);
                ShowSide(p);
            }

            if (step == 0)
            {
                if (cfg.Mode == GameMode.Versus)
                {
                    for (int p = 0; p < 2; p++)
                    {
                        var nav = InputReader.Menu(p);
                        if (nav == InputReader.Nav.None) continue;
                        if (!locked[p])
                        {
                            if (nav == InputReader.Nav.Confirm) Lock(p);
                            else if (nav == InputReader.Nav.Back) { if (p == 0 && !locked[1]) { Back(); return; } }
                            else if (nav == InputReader.Nav.Alt) { cursor[p] = roster.Count; Lock(p); }
                            else Move(p, nav);
                        }
                        else if (nav == InputReader.Nav.Back) { locked[p] = false; App.Click("ui_back"); }
                    }
                    if (locked[0] && locked[1]) Proceed();
                }
                else
                {
                    int slot = ActiveSlot();
                    var nav = InputReader.AnyMenu(out _);
                    if (slot < 0) { Proceed(); return; }
                    if (nav == InputReader.Nav.Confirm) Lock(slot);
                    else if (nav == InputReader.Nav.Alt) { cursor[slot] = roster.Count; Lock(slot); }
                    else if (nav == InputReader.Nav.Back)
                    {
                        if (slot == 1 && locked[0]) { locked[0] = false; App.Click("ui_back"); }
                        else { Back(); return; }
                    }
                    else if (nav != InputReader.Nav.None) Move(slot, nav);
                    if (ActiveSlot() < 0) Proceed();
                }
            }
            else if (step == 1)
            {
                // choose CPU difficulty for CPU-controlled sides (left/right), confirm to continue
                var nav = InputReader.AnyMenu(out _);
                int cpuSlot = !cfg.IsHuman(1) ? 1 : 0;
                bool two = !cfg.IsHuman(0) && !cfg.IsHuman(1);
                if (nav == InputReader.Nav.Left || nav == InputReader.Nav.Right)
                {
                    int d = nav == InputReader.Nav.Left ? -1 : 1;
                    level[cpuSlot] = (level[cpuSlot] + d + 4) % 4;
                    if (two) level[0] = level[cpuSlot];
                    App.Click();
                }
                else if (nav == InputReader.Nav.Up || nav == InputReader.Nav.Down)
                {
                    if (two) { level[0] = (level[0] + (nav == InputReader.Nav.Up ? 1 : 3)) % 4; App.Click(); }
                }
                else if (nav == InputReader.Nav.Confirm) Finish();
                else if (nav == InputReader.Nav.Back) { step = 0; locked[1] = false; if (cfg.Mode == GameMode.Arcade) locked[0] = false; }
            }
            hint.text = HintText();
        }

        string HintText()
        {
            if (step == 1)
                return !cfg.IsHuman(0) && !cfg.IsHuman(1)
                    ? "LEFT/RIGHT: P2 CPU level   UP/DOWN: P1 CPU level   F/Enter: FIGHT"
                    : "LEFT/RIGHT: CPU difficulty   F/Enter: continue   G/Esc: back";
            if (cfg.Mode == GameMode.Versus) return "P1: WASD + F select, H random    |    P2: Arrows + Num1 / ',' select, Num3 / '/' random    |    Back: G / Num2";
            return "WASD/Arrows: move   F/Enter: select   H: random   G/Esc: back    |    mouse works too";
        }

        void Proceed()
        {
            bool anyCpu = !cfg.IsHuman(0) || (!cfg.IsHuman(1) && cfg.Mode != GameMode.Training);
            if (cfg.Mode == GameMode.Arcade) anyCpu = true;
            if (anyCpu && step == 0) { step = 1; return; }
            Finish();
        }

        void Finish()
        {
            cfg.P1 = roster[cursor[0]].Id;
            if (cursor[1] >= 0) cfg.P2 = roster[cursor[1]].Id;
            cfg.Level1 = level[0];
            cfg.Level2 = level[1];
            App.Click("ui_confirm");
            if (cfg.Mode == GameMode.Arcade)
            {
                cfg.ArcadeStage = 0;
                cfg.ArcadeScore = 0;
                cfg.ArcadeLadder = ArcadeScreen.BuildLadder(App.Grimoire, cfg.P1, cfg.Seed);
                App.Go(new ArcadeScreen(cfg));
                return;
            }
            if (cfg.Mode == GameMode.Training) { cfg.Arena = "kamar_temple"; App.Go(new BattleScreen(cfg)); return; }
            App.Go(new StageSelectScreen(cfg));
        }

        void Back()
        {
            App.Click("ui_back");
            App.Go(new MainMenuScreen());
        }
    }

    public sealed class StageSelectScreen : GameScreen
    {
        readonly MatchConfig cfg;
        MenuList list;
        Image preview;
        Text name;
        List<ArenaDef> arenas;

        public StageSelectScreen(MatchConfig cfg) { this.cfg = cfg; }

        public override void Enter()
        {
            arenas = App.Grimoire.Arenas.Where(a => !a.BossOnly).ToList();
            var bg = Ui.Box(Root, Color.black);
            bg.rectTransform.Fill();
            preview = Ui.Image(Root, null, Color.white, "preview");
            preview.rectTransform.Fill();
            var shade = Ui.Image(Root, Ui.HGradient(new Color(0, 0, 0, 0.92f), new Color(0, 0, 0, 0.1f)), Color.white);
            shade.rectTransform.Fill();
            var title = Ui.Text(Root, "SELECT STAGE", 84, Ui.Gold, TextAnchor.MiddleLeft).Outlined(4);
            title.rectTransform.Place(0f, 0.9f, 1000, 120, 0f, 0.5f, 80, 0);
            list = new MenuList(Root, 0.2f, 0.76f, 640, 74, 44);
            foreach (var a in arenas) { var ar = a; list.Add(ar.Name.ToUpper(), () => Pick(ar.Id)); }
            list.Add("RANDOM", () => Pick(arenas[Random.Range(0, arenas.Count)].Id));
            list.OnMove = i => App.Click();
            name = Ui.Text(Root, "", 40, Color.white, TextAnchor.MiddleRight).Outlined(3);
            name.rectTransform.Place(1f, 0.06f, 1000, 60, 1f, 0.5f, -60, 0);
        }

        void Pick(string id)
        {
            cfg.Arena = id;
            App.Click("ui_confirm");
            App.Go(new VsScreen(cfg));
        }

        public override void Tick()
        {
            var nav = InputReader.AnyMenu(out _);
            if (nav == InputReader.Nav.Back) { App.Click("ui_back"); App.Go(new CharSelectScreen(cfg)); return; }
            list.Handle(nav);
            list.Refresh();
            if (list.Index < arenas.Count)
            {
                var a = arenas[list.Index];
                preview.sprite = AssetDb.Load("Arenas/" + a.Id + "/sky");
                preview.color = preview.sprite != null ? Color.white : new Color(0.1f, 0.05f, 0.2f);
                name.text = a.Name;
            }
            else { preview.sprite = null; preview.color = new Color(0.15f, 0.08f, 0.25f); name.text = "???"; }
        }
    }

    public sealed class VsScreen : GameScreen
    {
        readonly MatchConfig cfg;
        RectTransform left, right, vs;
        float t0;

        public VsScreen(MatchConfig cfg) { this.cfg = cfg; }

        public override void Enter()
        {
            t0 = Time.unscaledTime;
            var g = App.Grimoire;
            var f1 = g.Get(cfg.P1);
            var f2 = g.Get(cfg.P2);
            Backdrop.Build(Root, "UI/vs_bg", 0.35f);
            left = Half(f1, 0);
            right = Half(f2, 1);
            var vsT = Ui.Text(Root, "VS", 260, Ui.Gold).Outlined(8).Shadowed(12);
            vs = vsT.rectTransform.Place(0.5f, 0.5f, 600, 360);
            var arena = g.Arenas.FirstOrDefault(a => a.Id == cfg.Arena);
            var at = Ui.Text(Root, arena != null ? arena.Name.ToUpper() : "", 36, Color.white).Outlined(2);
            at.rectTransform.Place(0.5f, 0.08f, 1200, 60);
            if (cfg.Mode == GameMode.Arcade)
            {
                var st = Ui.Text(Root, cfg.ArcadeStage == cfg.ArcadeLadder.Length - 1 ? "FINAL BATTLE" : "STAGE " + (cfg.ArcadeStage + 1), 48, Ui.Cyan).Outlined(3);
                st.rectTransform.Place(0.5f, 0.92f, 1000, 70);
            }
            App.Audio.Sfx("ult_flash");
        }

        RectTransform Half(FighterDef f, int side)
        {
            var root = Ui.Rect(Root, "half" + side).Place(side == 0 ? 0f : 1f, 0.5f, 960, 1080, side == 0 ? 0f : 1f, 0.5f);
            var col = AssetDb.Hex(f.Color, Color.white);
            var stripe = Ui.Image(root, Ui.HGradient(side == 0 ? new Color(col.r, col.g, col.b, 0.7f) : new Color(col.r, col.g, col.b, 0f), side == 0 ? new Color(col.r, col.g, col.b, 0f) : new Color(col.r, col.g, col.b, 0.7f)), Color.white);
            stripe.rectTransform.Fill();
            var p = Ui.Image(root, AssetDb.Portrait(f), Color.white, "portrait");
            p.preserveAspect = true;
            p.rectTransform.Place(0.5f, 0.55f, 820, 820);
            if (side == 1) p.rectTransform.localScale = new Vector3(-1, 1, 1);
            var n = Ui.Text(root, f.Name.ToUpper(), 76, Color.white).Outlined(4).Shadowed(6);
            n.rectTransform.Place(0.5f, 0.12f, 900, 100);
            var t = Ui.Text(root, f.Title, 34, Ui.Gold).Outlined(2);
            t.rectTransform.Place(0.5f, 0.05f, 900, 50);
            return root;
        }

        public override void Tick()
        {
            float t = Time.unscaledTime - t0;
            float slide = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.45f), 3f);
            left.anchoredPosition = new Vector2(Mathf.Lerp(-900, 0, slide), 0);
            right.anchoredPosition = new Vector2(Mathf.Lerp(900, 0, slide), 0);
            vs.localScale = Vector3.one * (t < 0.45f ? 0f : Mathf.Lerp(2.5f, 1f, Mathf.Clamp01((t - 0.45f) / 0.18f)));
            if (t > 2.6f || (t > 0.8f && InputReader.AnyKeyDown())) App.Go(new BattleScreen(cfg));
        }
    }
}
