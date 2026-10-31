using System.Collections.Generic;
using System.Linq;
using System.Text;
using MysticKombat.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MysticKombat
{
    public sealed class ResultScreen : GameScreen
    {
        readonly MatchConfig cfg;
        readonly BattleSim sim;
        readonly string banner;
        readonly int rank;
        MenuList menu;
        RectTransform pose;
        float t0;

        public ResultScreen(MatchConfig cfg, BattleSim sim, string banner, int rank)
        {
            this.cfg = cfg;
            this.sim = sim;
            this.banner = banner;
            this.rank = rank;
        }

        public override void Enter()
        {
            t0 = Time.unscaledTime;
            InputReader.SoloLayout = true;
            Backdrop.Build(Root, "UI/result_bg", 0.5f);
            int w = sim.Winner;
            var winner = w >= 0 && w < 2 ? sim.F[w] : null;
            if (winner != null)
            {
                var col = AssetDb.Hex(winner.Def.Color, Color.white);
                var glow = Ui.Image(Root, AssetDb.SoftCircle, new Color(col.r, col.g, col.b, 0.5f));
                glow.rectTransform.Place(0.27f, 0.5f, 900, 900);
                var img = Ui.Image(Root, AssetDb.Pose(winner.Def, "victory"), Color.white, "victory");
                img.preserveAspect = true;
                pose = img.rectTransform.Place(0.27f, 0.1f, 700, 760, 0.5f, 0f);
                var quote = Ui.Text(Root, Quote(winner.Def.Id), 34, Color.white).Outlined(2);
                quote.rectTransform.Place(0.27f, 0.06f, 900, 80);
            }
            string head = banner ?? (w == 2 || w < 0 ? "DRAW" : winner.Def.Name.ToUpper() + " WINS");
            var title = Ui.Text(Root, head, 100, Ui.Gold).Outlined(5).Shadowed(8);
            title.rectTransform.Place(0.68f, 0.88f, 1150, 140);

            var panel = Ui.Box(Root, Ui.Panel);
            panel.rectTransform.Place(0.68f, 0.55f, 1050, 420);
            var sb = new StringBuilder();
            sb.AppendLine("<color=#FFD45A>                         " + Pad(sim.F[0].Def.Name, 18) + Pad(sim.F[1].Def.Name, 18) + "</color>");
            Row(sb, "Rounds won", sim.F[0].RoundWins, sim.F[1].RoundWins);
            Row(sb, "Damage dealt", Mathf.RoundToInt(sim.F[0].Stats.DamageDealt), Mathf.RoundToInt(sim.F[1].Stats.DamageDealt));
            Row(sb, "Hits landed", sim.F[0].Stats.Hits, sim.F[1].Stats.Hits);
            Row(sb, "Max combo", sim.F[0].Stats.MaxCombo, sim.F[1].Stats.MaxCombo);
            Row(sb, "Ultimates (hit)", sim.F[0].Stats.Ults + " (" + sim.F[0].Stats.UltHits + ")", sim.F[1].Stats.Ults + " (" + sim.F[1].Stats.UltHits + ")");
            Row(sb, "Healed", Mathf.RoundToInt(sim.F[0].Stats.Healed), Mathf.RoundToInt(sim.F[1].Stats.Healed));
            Row(sb, "Blocks / Breaks", sim.F[0].Stats.Blocks + " / " + sim.F[0].Stats.ThrowBreaks, sim.F[1].Stats.Blocks + " / " + sim.F[1].Stats.ThrowBreaks);
            Row(sb, "Ki orbs", sim.F[0].Stats.OrbsTaken, sim.F[1].Stats.OrbsTaken);
            var st = Ui.Text(panel.rectTransform, sb.ToString(), 30, Color.white, TextAnchor.UpperLeft);
            st.rectTransform.Fill(30, 20, 30, 24);

            if (cfg.Mode == GameMode.Arcade)
            {
                var sc = Ui.Text(Root, "SCORE  " + cfg.ArcadeScore.ToString("N0") + (rank >= 0 && rank < 10 ? "   <color=#FFD45A>HIGH SCORE #" + (rank + 1) + "</color>" : ""), 46, Color.white).Outlined(3);
                sc.rectTransform.Place(0.68f, 0.28f, 1100, 70);
                if (banner == "ARCADE CLEARED!")
                {
                    var u = Ui.Text(Root, "MALGRATH THE VOID EMPEROR IS NOW PLAYABLE!", 34, Ui.Cyan).Outlined(2);
                    u.rectTransform.Place(0.68f, 0.22f, 1100, 50);
                }
            }

            menu = new MenuList(Root, 0.68f, 0.19f, 700, 64, 42);
            if (cfg.Mode == GameMode.Arcade)
            {
                menu.Add("MAIN MENU", () => App.Go(new MainMenuScreen()));
                menu.Add("PLAY ARCADE AGAIN", () => { var c = cfg.Clone(); c.Mode = GameMode.Arcade; App.Go(new CharSelectScreen(c)); });
            }
            else if (cfg.Mode == GameMode.Lan)
            {
                menu.Add("REMATCH", () => App.Go(new LanScreen(cfg.Net)));
                menu.Add("MAIN MENU", () => { cfg.Net?.Close(); App.Go(new MainMenuScreen()); });
            }
            else
            {
                menu.Add("REMATCH", () => { cfg.Seed++; App.Go(new BattleScreen(cfg)); });
                menu.Add("CHARACTER SELECT", () => App.Go(new CharSelectScreen(cfg)));
                menu.Add("MAIN MENU", () => App.Go(new MainMenuScreen()));
            }
            menu.OnMove = i => App.Click();
            if (winner != null) App.Audio.Voice("name_" + winner.Def.Id);
        }

        static string Pad(string s, int n) => s.Length >= n ? s.Substring(0, n) : s.PadRight(n);

        static void Row(StringBuilder sb, string label, object a, object b)
        {
            sb.AppendLine(Pad(label, 24) + " " + Pad(a.ToString(), 18) + Pad(b.ToString(), 18));
        }

        static string Quote(string id)
        {
            switch (id)
            {
                case "kazu": return "\"I never go back on my word - that's my ninja way!\"";
                case "goro": return "\"That was fun! Let's go again when you're stronger!\"";
                case "shin": return "\"...was that a serious fight? Oh well. Sale at the market.\"";
                case "zevrin": return "\"Two legends, one body. You never stood a chance.\"";
                case "rio": return "\"I'm hungry! Somebody get me some meat!\"";
                case "kuro": return "\"I fight to protect. That's all there is to it.\"";
                case "sho": return "\"You're not worth my hatred.\"";
                case "ren": return "\"Throughout heaven and earth, I alone am the honored one.\"";
                case "kai": return "\"I'll keep moving forward, no matter what.\"";
                case "hana": return "\"Don't worry, I'll patch you up. After that, I'll hit you again.\"";
                case "malgrath": return "\"Every reality will kneel to the Void.\"";
            }
            return "";
        }

        public override void Tick()
        {
            if (pose != null) pose.anchoredPosition = new Vector2(0, Mathf.Sin(Time.unscaledTime * 2f) * 8f);
            if (Time.unscaledTime - t0 < 0.6f) return;
            var nav = InputReader.AnyMenu(out _);
            menu.Handle(nav);
            menu.Refresh();
        }
    }

    /// <summary>Arcade ladder: four rift invaders, a mirror match, then the Void Emperor. Local high scores.</summary>
    public sealed class ArcadeScreen : GameScreen
    {
        readonly MatchConfig cfg;
        float t0;

        public ArcadeScreen(MatchConfig cfg) { this.cfg = cfg; }

        public static string[] BuildLadder(Grimoire g, string player, uint seed)
        {
            var rng = new Rng(seed | 1);
            var pool = g.Fighters.Where(f => !f.Boss && f.Id != player).Select(f => f.Id).ToList();
            for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Range(0, i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            var ladder = pool.Take(4).ToList();
            ladder.Add(player);       // mirror match: your shadow self from another reality
            var boss = g.Fighters.FirstOrDefault(f => f.Boss);
            if (boss != null) ladder.Add(boss.Id);
            return ladder.ToArray();
        }

        public static int ScoreFor(BattleSim sim, MatchConfig cfg)
        {
            var me = sim.F[0];
            float hp = me.Hp / me.Def.MaxHp;
            int score = 1000 + Mathf.RoundToInt(hp * 2000) + sim.TimerFrames / 60 * 20 + me.Stats.MaxCombo * 120 + me.Stats.UltHits * 500;
            if (sim.F[1].RoundWins == 0) score += 1500;
            if (sim.Perfect) score += 3000;
            return Mathf.RoundToInt(score * (0.75f + 0.25f * cfg.Level2));
        }

        public override void Enter()
        {
            t0 = Time.unscaledTime;
            App.Audio.PlayMusic("select");
            Backdrop.Build(Root, "UI/vs_bg", 0.5f);
            var title = Ui.Text(Root, "ARCADE LADDER", 96, Ui.Gold).Outlined(5);
            title.rectTransform.Place(0.5f, 0.9f, 1600, 130);
            var sub = Ui.Text(Root, "Difficulty: " + CpuBrain.LevelNames[cfg.Level2] + "     Score: " + cfg.ArcadeScore.ToString("N0"), 36, Color.white).Outlined(2);
            sub.rectTransform.Place(0.5f, 0.82f, 1600, 60);
            int n = cfg.ArcadeLadder.Length;
            for (int i = 0; i < n; i++)
            {
                var f = App.Grimoire.Get(cfg.ArcadeLadder[i]);
                bool done = i < cfg.ArcadeStage, current = i == cfg.ArcadeStage;
                float x = (i - (n - 1) * 0.5f) * 270;
                var card = Ui.Box(Root, current ? new Color(1f, 0.6f, 0.15f, 0.9f) : new Color(0.08f, 0.05f, 0.15f, 0.9f));
                card.rectTransform.Place(0.5f, 0.5f, 240, 330, 0.5f, 0.5f, x, current ? 20 : 0);
                var mask = Ui.Rect(card.rectTransform, "mask").Fill(6, 60, 6, 6);
                mask.gameObject.AddComponent<RectMask2D>();
                var p = Ui.Image(mask, AssetDb.Portrait(f), done ? new Color(0.4f, 0.4f, 0.4f) : Color.white);
                p.preserveAspect = true;
                p.rectTransform.Fill();
                string label = i == n - 1 && f.Boss ? "BOSS" : i == n - 2 && f.Id == cfg.P1 ? "MIRROR" : "STAGE " + (i + 1);
                var t = Ui.Text(card.rectTransform, label + "\n" + f.Name.Split(' ')[0].ToUpper(), 26, Color.white).Outlined(2);
                t.rectTransform.Place(0.5f, 0f, 240, 60, 0.5f, 0f, 0, 2);
                if (done)
                {
                    var x2 = Ui.Text(card.rectTransform, "X", 200, new Color(1f, 0.2f, 0.2f, 0.85f)).Outlined(4);
                    x2.rectTransform.Fill();
                }
            }
            var scores = new StringBuilder("<color=#FFD45A>HIGH SCORES</color>\n");
            for (int i = 0; i < Mathf.Min(5, App.Save.Scores.Count); i++)
            {
                var h = App.Save.Scores[i];
                var fd = App.Grimoire.Get(h.Fighter);
                scores.AppendLine($"{i + 1}. {h.Score:N0}  {(fd != null ? fd.Name : h.Fighter)}  ({CpuBrain.LevelNames[Mathf.Clamp(h.Difficulty, 0, 3)]})");
            }
            var st = Ui.Text(Root, scores.ToString(), 28, Color.white, TextAnchor.UpperCenter).Outlined(2);
            st.rectTransform.Place(0.5f, 0.2f, 1200, 220, 0.5f, 1f);
            var go = Ui.Text(Root, "PRESS CONFIRM TO FIGHT", 40, Ui.Cyan).Outlined(3);
            go.rectTransform.Place(0.5f, 0.04f, 1000, 60);
        }

        public override void Tick()
        {
            if (Time.unscaledTime - t0 < 0.5f) return;
            var nav = InputReader.AnyMenu(out _);
            if (nav == InputReader.Nav.Confirm || Time.unscaledTime - t0 > 6f)
            {
                cfg.P2 = cfg.ArcadeLadder[cfg.ArcadeStage];
                var opp = App.Grimoire.Get(cfg.P2);
                var arenas = App.Grimoire.Arenas;
                cfg.Arena = opp.Boss ? "rift_void" : arenas[(cfg.ArcadeStage * 2 + (int)(cfg.Seed % 3)) % Mathf.Max(1, arenas.Count - 1)].Id;
                App.Go(new VsScreen(cfg));
            }
            else if (nav == InputReader.Nav.Back) App.Go(new MainMenuScreen());
        }
    }

    /// <summary>Move list + frame data for every fighter, generated from the grimoire.</summary>
    public sealed class MoveListScreen : GameScreen
    {
        int index;
        Text body, name;
        Image portrait;
        readonly GameScreen back;

        public MoveListScreen(GameScreen back) { this.back = back; }

        public override void Enter()
        {
            Backdrop.Build(Root, "UI/select_bg", 0.7f);
            var title = Ui.Text(Root, "MOVE LIST & FRAME DATA", 72, Ui.Gold).Outlined(4);
            title.rectTransform.Place(0.5f, 0.94f, 1600, 100);
            portrait = Ui.Image(Root, null, Color.white);
            portrait.preserveAspect = true;
            portrait.rectTransform.Place(0.15f, 0.45f, 520, 640);
            name = Ui.Text(Root, "", 52, Color.white).Outlined(3);
            name.rectTransform.Place(0.15f, 0.1f, 600, 80);
            var panel = Ui.Box(Root, Ui.Panel);
            panel.rectTransform.Place(0.63f, 0.47f, 1300, 860);
            body = Ui.Text(panel.rectTransform, "", 25, Color.white, TextAnchor.UpperLeft);
            body.rectTransform.Fill(30, 20, 30, 20);
            var hint = Ui.Text(Root, "LEFT/RIGHT: fighter    G/Esc: back", 28, new Color(1, 1, 1, 0.7f));
            hint.rectTransform.Place(0.5f, 0.02f, 1000, 40);
            Show();
        }

        void Show()
        {
            var f = App.Grimoire.Fighters[index];
            portrait.sprite = AssetDb.Pose(f, "ultimate");
            name.text = f.Name.ToUpper();
            body.text = Describe(f, false);
        }

        public static string Describe(FighterDef f, bool compact)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<color=#FFD45A>{f.Name}</color> - {f.Title}  ({f.Archetype})");
            if (!compact) sb.AppendLine("<color=#BBBBFF>" + f.Description + "</color>");
            sb.AppendLine($"HP {f.MaxHp}   Power x{f.Power:0.00}   Speed {f.WalkSpeed:0.0}   Ki gain x{f.KiGain:0.00}");
            sb.AppendLine();
            sb.AppendLine("<color=#FFD45A>MOVE                        INPUT              STARTUP ACT REC  DMG   ON-BLOCK</color>");
            void Line(string slot, string input)
            {
                var m = f.Move(slot);
                if (m == null) return;
                int onBlock = m.Blockstun - (m.Active - 1) - m.Recovery;
                string dmg = m.Projectile != null ? (m.Projectile.Damage * m.Projectile.Count * m.Projectile.Hits).ToString("0") : (m.Damage * m.Hits).ToString("0");
                string extra = "";
                if (m.Charges > 0) extra = $"  [{Mathf.Max(1, m.Charges)}x {(m.Recharge > 0 ? (m.Recharge / 60f).ToString("0.#") + "s" : "/round")}]";
                if (m.HealAmount > 0) extra += $"  heal {m.HealAmount:0}";
                if (m.Kind == "none") dmg = "-";
                sb.AppendLine($"{m.Name,-27} {input,-18} {m.Startup,5} {m.Active,4} {m.Recovery,4} {dmg,5} {(m.Kind == "none" ? "-" : (onBlock >= 0 ? "+" : "") + onBlock),8}{extra}");
            }
            Line("light", "Light");
            Line("heavy", "Heavy");
            Line("crouch_light", "Down + Light");
            Line("crouch_heavy", "Down + Heavy");
            Line("air_light", "Air Light");
            Line("air_heavy", "Air Heavy");
            Line("throw", "Throw / L+H");
            Line("special1", "S1 / 236+L");
            Line("special2", "S2 / 214+L");
            Line("special3", "S3 / 623+L");
            Line("ultimate", "ULT / 236236+H");
            if (!compact)
            {
                sb.AppendLine();
                sb.AppendLine("<color=#9FE0FF>Combos:</color> Light > Light > Heavy > any Special > ULTIMATE (on hit).  Specials use charges; ultimate needs a full Ki meter (half in RAGE).");
            }
            return sb.ToString();
        }

        public override void Tick()
        {
            var nav = InputReader.AnyMenu(out _);
            int n = App.Grimoire.Fighters.Count;
            if (nav == InputReader.Nav.Left || nav == InputReader.Nav.Up) { index = (index - 1 + n) % n; Show(); App.Click(); }
            if (nav == InputReader.Nav.Right || nav == InputReader.Nav.Down) { index = (index + 1) % n; Show(); App.Click(); }
            if (nav == InputReader.Nav.Back || nav == InputReader.Nav.Confirm) App.Go(back ?? new MainMenuScreen());
        }
    }
}
