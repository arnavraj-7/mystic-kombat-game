using System.Collections.Generic;
using System.Text;
using MysticKombat.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MysticKombat
{
    /// <summary>Battle HUD: health + damage trail, rage, round pips, timer, Ki meter, ability charges, combo counter,
    /// announcer, ultimate banner, training inspector.</summary>
    public sealed class BattleHud
    {
        readonly BattleSim sim;
        readonly RectTransform root;
        readonly Side[] sides = new Side[2];
        readonly Text timer, announce, subAnnounce, netInfo, hints;
        readonly Image flash, ultDim;
        readonly RectTransform ultBanner;
        readonly Text ultName, ultFighter;
        readonly Image ultPortrait;
        float announceT = 10f, ultT = 10f;
        readonly RectTransform trainingPanel;
        readonly Text frameData, inputHist, trainingHelp;
        public bool Training;

        sealed class Side
        {
            public Image Hp, Trail, HpFrame, Portrait, Ki, KiFrame, RageGlow;
            public Text Name, KiText, Rage, Combo, ComboDmg;
            public Image[] Pips = new Image[3];
            public Ability[] Abilities = new Ability[3];
            public float TrailValue = 1f, TrailDelay;
            public float ComboT = 10f;
            public int LastCombo;
            public RectTransform ComboRoot;
        }

        sealed class Ability
        {
            public Image Bg, Cool;
            public Text Key, Name;
            public Image[] Charges = new Image[3];
        }

        public BattleHud(Transform parent, BattleSim sim, MatchConfig cfg)
        {
            this.sim = sim;
            root = Ui.Rect(parent, "hud").Fill();
            for (int i = 0; i < 2; i++) sides[i] = BuildSide(i, cfg);

            var tbg = Ui.Image(root, AssetDb.SoftCircle, new Color(0, 0, 0, 0.6f), "timerBg");
            tbg.rectTransform.Place(0.5f, 1f, 190, 150, 0.5f, 1f, 0, 0);
            timer = Ui.Text(root, "99", 92, Color.white).Outlined(4);
            timer.rectTransform.Place(0.5f, 1f, 200, 120, 0.5f, 1f, 0, -8);

            announce = Ui.Text(root, "", 170, Ui.Gold).Outlined(7).Shadowed(10);
            announce.rectTransform.Place(0.5f, 0.6f, 1900, 240);
            subAnnounce = Ui.Text(root, "", 60, Color.white).Outlined(4);
            subAnnounce.rectTransform.Place(0.5f, 0.47f, 1700, 90);

            ultDim = Ui.Box(root, new Color(0, 0, 0, 0));
            ultDim.rectTransform.Fill();
            ultBanner = Ui.Rect(root, "ultBanner").Place(0.5f, 0.42f, 2400, 230);
            ultBanner.localRotation = Quaternion.Euler(0, 0, 6);
            var ub = Ui.Image(ultBanner, Ui.HGradient(new Color(0.05f, 0.02f, 0.1f, 0.0f), new Color(0.05f, 0.02f, 0.1f, 0.95f)), Color.white);
            ub.rectTransform.Fill();
            ultPortrait = Ui.Image(ultBanner, null, Color.white, "portrait");
            ultPortrait.preserveAspect = true;
            ultPortrait.rectTransform.Place(0.5f, 0.5f, 420, 420, 0.5f, 0.5f, -620, 30);
            ultFighter = Ui.Text(ultBanner, "", 40, Ui.Gold).Outlined(2);
            ultFighter.rectTransform.Place(0.5f, 0.5f, 1200, 60, 0.5f, 0.5f, 220, 56);
            ultName = Ui.Text(ultBanner, "", 110, Color.white).Outlined(5).Shadowed(8);
            ultName.rectTransform.Place(0.5f, 0.5f, 1500, 140, 0.5f, 0.5f, 220, -20);
            ultBanner.gameObject.SetActive(false);

            flash = Ui.Box(root, new Color(1, 1, 1, 0));
            flash.rectTransform.Fill();

            netInfo = Ui.Text(root, "", 24, new Color(0.6f, 1f, 0.8f), TextAnchor.UpperCenter).Outlined();
            netInfo.rectTransform.Place(0.5f, 1f, 600, 60, 0.5f, 1f, 0, -150);

            hints = Ui.Text(root, "", 22, new Color(1, 1, 1, 0.75f)).Outlined();
            hints.rectTransform.Place(0.5f, 0f, 1900, 34, 0.5f, 0f, 0, 6);
            if (GameApp.I.Save.ShowHints)
            {
                if (cfg.Mode == GameMode.Versus)
                    hints.text = "P1: WASD  F light  G heavy  H/R/T specials  Y ult  V throw     |     P2: Arrows  Num1 light  Num2 heavy  Num3/4/5 specials  Num6 ult  Num0 throw     |     Esc pause";
                else if (cfg.Mode != GameMode.CpuVsCpu)
                    hints.text = "WASD/Arrows move   F/J light   G/K heavy   H/L  R/U  T/I specials   Y/O ULT   V/; throw   hold back = block   Esc pause";
            }

            trainingPanel = Ui.Rect(root, "training").Place(1f, 0.5f, 520, 560, 1f, 0.5f, -16, 0);
            var tp = Ui.Box(trainingPanel, new Color(0, 0, 0, 0.62f));
            tp.rectTransform.Fill();
            frameData = Ui.Text(trainingPanel, "", 22, Color.white, TextAnchor.UpperLeft);
            frameData.rectTransform.Fill(16, 250, 16, 14);
            inputHist = Ui.Text(trainingPanel, "", 22, new Color(0.8f, 1f, 0.8f), TextAnchor.UpperLeft);
            inputHist.rectTransform.Fill(16, 14, 16, 320);
            trainingHelp = Ui.Text(root, "", 22, new Color(1f, 0.95f, 0.7f), TextAnchor.LowerLeft).Outlined();
            trainingHelp.rectTransform.Place(0f, 0f, 1100, 120, 0f, 0f, 16, 40);
            trainingPanel.gameObject.SetActive(false);
            trainingHelp.gameObject.SetActive(false);
        }

        Side BuildSide(int i, MatchConfig cfg)
        {
            var s = new Side();
            var f = sim.F[i].Def;
            float dir = i == 0 ? 1 : -1;
            float ax = i == 0 ? 0f : 1f;
            var col = AssetDb.Hex(f.Color, Color.white);

            // portrait
            var pf = Ui.Box(root, new Color(0, 0, 0, 0.7f), "pframe");
            pf.rectTransform.Place(ax, 1f, 132, 132, ax, 1f, dir * 18, -14);
            var mask = Ui.Rect(pf.rectTransform, "mask").Fill(4, 4, 4, 4);
            mask.gameObject.AddComponent<RectMask2D>();
            s.Portrait = Ui.Image(mask, AssetDb.Portrait(f), Color.white, "portrait");
            s.Portrait.preserveAspect = true;
            s.Portrait.rectTransform.Fill();
            if (i == 1) s.Portrait.rectTransform.localScale = new Vector3(-1, 1, 1);
            var edge = Ui.Box(pf.rectTransform, col, "edge");
            edge.rectTransform.Place(0.5f, 0f, 132, 6, 0.5f, 0f);

            // health bar
            float barW = 690, barH = 46, bx = dir * 160;
            s.HpFrame = Ui.Box(root, new Color(0.02f, 0.01f, 0.05f, 0.9f), "hpframe");
            s.HpFrame.rectTransform.Place(ax, 1f, barW + 8, barH + 8, ax, 1f, bx, -26);
            s.RageGlow = Ui.Image(s.HpFrame.rectTransform, AssetDb.SoftCircle, new Color(1, 0.1f, 0.05f, 0), "rage");
            s.RageGlow.rectTransform.Fill(-30, -30, -30, -30);
            s.Trail = Ui.Fill(s.HpFrame.rectTransform, new Color(0.95f, 0.15f, 0.12f), Image.FillMethod.Horizontal, i == 0 ? 0 : 1, "trail");
            s.Trail.rectTransform.Fill(4, 4, 4, 4);
            s.Hp = Ui.Fill(s.HpFrame.rectTransform, Color.white, Image.FillMethod.Horizontal, i == 0 ? 0 : 1, "hp");
            s.Hp.sprite = Ui.HGradient(i == 0 ? new Color(1f, 0.55f, 0.1f) : new Color(1f, 0.9f, 0.25f), i == 0 ? new Color(1f, 0.9f, 0.25f) : new Color(1f, 0.55f, 0.1f));
            s.Hp.rectTransform.Fill(4, 4, 4, 4);

            s.Name = Ui.Text(root, f.Name.ToUpper(), 34, Color.white, i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight).Outlined(2);
            s.Name.rectTransform.Place(ax, 1f, 520, 44, ax, 1f, bx, -80);
            string tag = cfg.Mode == GameMode.Training && i == 1 ? "DUMMY" : cfg.IsHuman(i) ? (i == 0 ? "P1" : "P2") : "CPU " + CpuBrain.LevelNames[i == 0 ? cfg.Level1 : cfg.Level2].ToUpper();
            if (cfg.Mode == GameMode.Lan) tag = cfg.Net != null && cfg.Net.LocalIndex == i ? "YOU" : "RIVAL";
            var tagT = Ui.Text(root, tag, 26, i == 0 ? new Color(1f, 0.6f, 0.2f) : new Color(0.4f, 0.75f, 1f), i == 0 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft).Outlined(2);
            tagT.rectTransform.Place(ax, 1f, 300, 44, i == 0 ? 0f : 1f, 1f, bx + dir * (barW - 300), -80);
            s.Rage = Ui.Text(root, "RAGE", 34, new Color(1f, 0.25f, 0.15f), TextAnchor.MiddleCenter).Outlined(3);
            s.Rage.rectTransform.Place(ax, 1f, 160, 44, ax, 1f, bx + dir * 380, -80);

            for (int p = 0; p < 3; p++)
            {
                s.Pips[p] = Ui.Image(root, AssetDb.Circle, new Color(1, 1, 1, 0.25f), "pip");
                s.Pips[p].rectTransform.Place(ax, 1f, 26, 26, 0.5f, 0.5f, bx + dir * (barW - 20 - p * 34), -128);
            }

            // Ki meter (bottom)
            s.KiFrame = Ui.Box(root, new Color(0.02f, 0.01f, 0.05f, 0.85f), "kiframe");
            s.KiFrame.rectTransform.Place(ax, 0f, 470, 34, ax, 0f, dir * 30, 44);
            s.Ki = Ui.Fill(s.KiFrame.rectTransform, Color.white, Image.FillMethod.Horizontal, i == 0 ? 0 : 1, "ki");
            s.Ki.sprite = Ui.HGradient(new Color(0.25f, 0.5f, 1f), new Color(0.6f, 0.95f, 1f));
            s.Ki.rectTransform.Fill(3, 3, 3, 3);
            for (int k = 1; k < 4; k++)
            {
                var tick = Ui.Box(s.KiFrame.rectTransform, new Color(0, 0, 0, 0.6f), "tick");
                tick.rectTransform.Place(k * 0.25f, 0.5f, 3, 34);
            }
            s.KiText = Ui.Text(root, "KI", 30, Ui.Cyan, i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight).Outlined(2);
            s.KiText.rectTransform.Place(ax, 0f, 470, 40, ax, 0f, dir * 30, 80);

            // ability slots
            string[] keys = cfg.Mode == GameMode.Versus ? (i == 0 ? new[] { "H", "R", "T" } : new[] { "N3", "N4", "N5" }) : new[] { "H/L", "R/U", "T/I" };
            if (!cfg.IsHuman(i) || cfg.Mode == GameMode.Lan && (cfg.Net == null || cfg.Net.LocalIndex != i)) keys = new[] { "S1", "S2", "S3" };
            for (int a = 0; a < 3; a++)
            {
                var ab = new Ability();
                var m = f.Move(Fighter.SpecialSlots[a]);
                float x = dir * (520 + a * 120);
                ab.Bg = Ui.Box(root, new Color(0.06f, 0.04f, 0.12f, 0.9f), "ability");
                ab.Bg.rectTransform.Place(ax, 0f, 110, 70, ax, 0f, x, 44);
                ab.Cool = Ui.Fill(ab.Bg.rectTransform, new Color(0, 0, 0, 0.7f), Image.FillMethod.Radial360, 2, "cool");
                ab.Cool.rectTransform.Fill();
                ab.Key = Ui.Text(ab.Bg.rectTransform, keys[a], 22, Ui.Gold, TextAnchor.UpperLeft).Outlined(1);
                ab.Key.rectTransform.Fill(6, 0, 0, 2);
                ab.Name = Ui.Text(ab.Bg.rectTransform, m != null ? Short(m.Name) : "-", 17, Color.white, TextAnchor.MiddleCenter).Outlined(1);
                ab.Name.rectTransform.Fill(4, 4, 4, 18);
                int max = m == null ? 0 : Mathf.Max(1, m.Charges);
                for (int c = 0; c < 3; c++)
                {
                    ab.Charges[c] = Ui.Image(ab.Bg.rectTransform, AssetDb.Circle, Color.white, "charge");
                    ab.Charges[c].rectTransform.Place(0.5f, 0f, 12, 12, 0.5f, 0f, (c - (max - 1) * 0.5f) * 16, 5);
                    ab.Charges[c].gameObject.SetActive(c < max);
                }
                s.Abilities[a] = ab;
            }

            // combo counter
            s.ComboRoot = Ui.Rect(root, "combo").Place(ax, 0.62f, 420, 200, ax, 0.5f, dir * 40, 0);
            s.Combo = Ui.Text(s.ComboRoot, "", 96, Ui.Gold, i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight).Outlined(5).Shadowed(6);
            s.Combo.rectTransform.Place(0.5f, 0.6f, 420, 120);
            s.ComboDmg = Ui.Text(s.ComboRoot, "", 36, Color.white, i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight).Outlined(2);
            s.ComboDmg.rectTransform.Place(0.5f, 0.2f, 420, 50);
            return s;
        }

        static string Short(string name)
        {
            var w = name.Split(' ');
            if (name.Length <= 12) return name.ToUpper();
            return (w.Length >= 2 ? w[0] + "\n" + string.Join(" ", w, 1, w.Length - 1) : name).ToUpper();
        }

        public void Announce(string text, string sub = "", Color? c = null)
        {
            announce.text = text;
            announce.color = c ?? Ui.Gold;
            subAnnounce.text = sub;
            announceT = 0f;
        }

        public void UltBanner(Fighter f)
        {
            var m = f.Def.Move("ultimate");
            ultName.text = m != null ? m.Name.ToUpper() : "ULTIMATE";
            ultFighter.text = f.Def.Name.ToUpper();
            ultPortrait.sprite = AssetDb.Portrait(f.Def);
            ultBanner.localRotation = Quaternion.Euler(0, 0, f.Index == 0 ? 5 : -5);
            ultT = 0f;
        }

        public void SetNet(string text) => netInfo.text = text;

        public void Render(float dt, float screenFlash, BattleScreen.TrainingState training)
        {
            for (int i = 0; i < 2; i++) RenderSide(i, dt);

            if (sim.R.RoundSeconds <= 0 || sim.TrainingMode) timer.text = "--";
            else
            {
                int secs = Mathf.CeilToInt(sim.TimerFrames / 60f);
                timer.text = secs.ToString();
                timer.color = secs <= 10 && sim.Phase == Phase.Fight ? Color.Lerp(Color.white, Color.red, Mathf.Abs(Mathf.Sin(Time.time * 6f))) : Color.white;
            }

            announceT += dt;
            float aScale = announceT < 0.15f ? Mathf.Lerp(2.2f, 1f, announceT / 0.15f) : 1f;
            float aAlpha = 1f - Mathf.Clamp01((announceT - 1.4f) / 0.35f);
            announce.rectTransform.localScale = Vector3.one * aScale;
            var ac = announce.color; ac.a = aAlpha; announce.color = ac;
            subAnnounce.color = new Color(1, 1, 1, aAlpha);

            ultT += dt;
            bool showUlt = ultT < 0.75f;
            ultBanner.gameObject.SetActive(showUlt);
            if (showUlt)
            {
                float slide = Mathf.Clamp01(ultT / 0.12f);
                ultBanner.anchoredPosition = new Vector2(Mathf.Lerp(-1400, 0, 1f - (1f - slide) * (1f - slide)) + ultT * 60f, 0);
            }
            ultDim.color = new Color(0, 0, 0, sim.Freeze > 0 ? 0.55f : Mathf.MoveTowards(ultDim.color.a, 0, dt * 3f));
            flash.color = new Color(1, 1, 1, screenFlash * 0.55f);

            trainingPanel.gameObject.SetActive(Training);
            trainingHelp.gameObject.SetActive(Training);
            if (Training) RenderTraining(training);
        }

        void RenderSide(int i, float dt)
        {
            var s = sides[i];
            var f = sim.F[i];
            float hp = Mathf.Clamp01(f.Hp / f.Def.MaxHp);
            s.Hp.fillAmount = hp;
            if (hp < s.TrailValue)
            {
                s.TrailDelay += dt;
                if (s.TrailDelay > 0.55f && f.State != FState.Hitstun && f.State != FState.Tumble) s.TrailValue = Mathf.MoveTowards(s.TrailValue, hp, dt * 0.6f);
            }
            else { s.TrailValue = hp; s.TrailDelay = 0; }
            if (f.ComboCount == 0 && s.TrailValue > hp && s.TrailDelay > 0.55f) { }
            s.Trail.fillAmount = s.TrailValue;
            bool rage = f.Raging(sim.R);
            s.Rage.gameObject.SetActive(rage);
            if (rage) s.Rage.color = new Color(1f, 0.25f, 0.15f, 0.6f + 0.4f * Mathf.Sin(Time.time * 10f));
            s.RageGlow.color = new Color(1f, 0.1f, 0.05f, rage ? 0.35f + 0.25f * Mathf.Sin(Time.time * 8f) : 0f);
            if (hp < 0.25f) s.Hp.color = new Color(1f, 0.4f + 0.3f * Mathf.Sin(Time.time * 12f), 0.4f);
            else s.Hp.color = Color.white;

            for (int p = 0; p < 3; p++)
            {
                bool show = p < sim.R.RoundsToWin;
                s.Pips[p].gameObject.SetActive(show);
                s.Pips[p].color = p < f.RoundWins ? Ui.Gold : new Color(1, 1, 1, 0.22f);
            }

            float cost = f.UltCost(sim.R);
            s.Ki.fillAmount = f.Ki / sim.R.KiMax;
            bool ready = f.Ki >= cost;
            s.Ki.color = ready ? Color.Lerp(Color.white, Ui.Gold, 0.5f + 0.5f * Mathf.Sin(Time.time * 9f)) : Color.white;
            s.KiText.text = ready ? "<color=#FFD45A>ULTIMATE READY!</color>" : "KI  " + Mathf.FloorToInt(f.Ki) + (rage ? "  <color=#FF5040>(RAGE: ULT AT 50)</color>" : "");

            for (int a = 0; a < 3; a++)
            {
                var ab = s.Abilities[a];
                var m = f.Def.Move(Fighter.SpecialSlots[a]);
                if (m == null) continue;
                int max = Mathf.Max(1, m.Charges);
                int ch = f.Charges[a];
                for (int c = 0; c < 3; c++) if (c < max) ab.Charges[c].color = c < ch ? Ui.Gold : new Color(1, 1, 1, 0.2f);
                if (ch >= max) ab.Cool.fillAmount = 0;
                else if (m.Recharge > 0) ab.Cool.fillAmount = ch > 0 ? 0f : 1f - f.RechargeTimer[a] / (float)m.Recharge;
                else ab.Cool.fillAmount = ch > 0 ? 0f : 1f;
                ab.Bg.color = ch > 0 ? new Color(0.12f, 0.08f, 0.25f, 0.92f) : new Color(0.04f, 0.03f, 0.06f, 0.9f);
                ab.Name.color = ch > 0 ? Color.white : new Color(1, 1, 1, 0.4f);
            }

            // combo counter shows the combo this side is dealing (the opponent's received count)
            var victim = sim.F[1 - i];
            if (victim.ComboCount >= 2)
            {
                if (victim.ComboCount != s.LastCombo) { s.LastCombo = victim.ComboCount; s.ComboT = 0f; }
                s.Combo.text = victim.ComboCount + " HITS";
                s.ComboDmg.text = Mathf.RoundToInt(victim.ComboDamage) + " DAMAGE";
            }
            s.ComboT += dt;
            float al = victim.ComboCount >= 2 ? 1f : Mathf.Clamp01(1f - (s.ComboT - 0.6f) / 0.4f);
            if (victim.ComboCount < 2 && s.LastCombo < 2) al = 0;
            if (victim.ComboCount < 2 && al <= 0) s.LastCombo = 0;
            s.Combo.color = new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, al);
            s.ComboDmg.color = new Color(1, 1, 1, al);
            s.ComboRoot.localScale = Vector3.one * (s.ComboT < 0.1f ? Mathf.Lerp(1.3f, 1f, s.ComboT / 0.1f) : 1f);
        }

        // ------------------------------------------------------------------ training inspector

        void RenderTraining(BattleScreen.TrainingState t)
        {
            var f = sim.F[0];
            var sb = new StringBuilder();
            sb.AppendLine("<color=#FFD45A>FRAME DATA INSPECTOR</color>");
            var m = f.Move ?? t.LastMove;
            if (m != null)
            {
                sb.AppendLine($"<b>{m.Name}</b>  ({m.Slot})");
                sb.AppendLine($"Startup {m.Startup}   Active {m.Active}   Recovery {m.Recovery}");
                sb.AppendLine($"Damage {m.Damage}{(m.Hits > 1 ? " x" + m.Hits : "")}   Height {m.Height}");
                int onHit = m.Hitstun - (m.Active - 1) - m.Recovery;
                int onBlock = m.Blockstun - (m.Active - 1) - m.Recovery;
                sb.AppendLine($"On hit {(onHit >= 0 ? "+" : "")}{onHit}   On block {(onBlock >= 0 ? "+" : "")}{onBlock}");
                if (m.Charges > 0) sb.AppendLine($"Charges {m.Charges}  Recharge {(m.Recharge > 0 ? (m.Recharge / 60f).ToString("0.#") + "s" : "per round")}");
                if (f.State == FState.Attack) sb.AppendLine($"<color=#9FE0FF>Frame {f.MoveFrame}/{m.TotalFrames}</color>");
            }
            sb.AppendLine($"State {f.State}  Combo dealt {sim.F[1].ComboCount} ({Mathf.RoundToInt(sim.F[1].ComboDamage)} dmg)");
            sb.AppendLine($"Last combo {t.LastComboHits} hits / {t.LastComboDamage} dmg");
            if (GameApp.I.GrimoireError != null) sb.AppendLine("<color=#FF6060>Grimoire: " + GameApp.I.GrimoireError + "</color>");
            frameData.text = sb.ToString();

            var ih = new StringBuilder("<color=#FFD45A>INPUT HISTORY (P1)</color>\n");
            int shown = 0;
            Btn prev = (Btn)0xFFFF;
            int run = 0;
            var lines = new List<string>();
            for (int ago = 0; ago < 60 && shown < 12; ago++)
            {
                var b = f.Input.HeldAgo(ago);
                if (b == prev) { run++; continue; }
                if (prev != (Btn)0xFFFF) { lines.Add(Describe(prev, f.Facing) + "  <color=#888888>" + (run + 1) + "f</color>"); shown++; }
                prev = b;
                run = 0;
            }
            foreach (var l in lines) ih.AppendLine(l);
            inputHist.text = ih.ToString();

            trainingHelp.text = $"F1 hitboxes [{(t.Boxes ? "ON" : "OFF")}]   F2 dummy: <color=#FFD45A>{t.DummyName}</color>   F3 reset   F4 refill [{(t.Refill ? "ON" : "OFF")}]   F5 reload grimoire   Esc pause";
        }

        static string Describe(Btn b, int facing)
        {
            int np = InputHistory.Numpad(b, facing);
            string[] arrows = { "", "DB", "D", "DF", "B", "N", "F", "UB", "U", "UF" };
            var s = arrows[np];
            if (b.Has(Btn.Light)) s += " L";
            if (b.Has(Btn.Heavy)) s += " H";
            if (b.Has(Btn.S1)) s += " S1";
            if (b.Has(Btn.S2)) s += " S2";
            if (b.Has(Btn.S3)) s += " S3";
            if (b.Has(Btn.Ult)) s += " ULT";
            if (b.Has(Btn.Throw)) s += " THROW";
            return s;
        }

        public void SetVisible(bool v) => root.gameObject.SetActive(v);

        public void Destroy() => Object.Destroy(root.gameObject);
    }
}
