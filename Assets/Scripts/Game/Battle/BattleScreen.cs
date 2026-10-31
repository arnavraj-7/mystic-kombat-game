using System.Collections.Generic;
using System.IO;
using System.Text;
using MysticKombat.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MysticKombat
{
    /// <summary>Runs a match: fixed 60 Hz stepping, input sources (human / CPU / dummy / LAN), cinematics, pause.</summary>
    public sealed class BattleScreen : GameScreen
    {
        public static int SpeedMultiplier = 1;      // autotest fast-forward
        public static bool ForceNoCinematics;

        public sealed class TrainingState
        {
            public bool Boxes = true, Refill = true;
            public int Dummy;
            public MoveDef LastMove;
            public int LastComboHits, LastComboDamage;
            public static readonly string[] DummyNames = { "STAND", "CROUCH", "JUMP", "BLOCK ALL", "CPU EASY", "CPU NORMAL", "CPU HARD", "CPU MYSTIC" };
            public string DummyName => DummyNames[Dummy];
        }

        readonly MatchConfig cfg;
        public BattleSim Sim { get; private set; }
        BattleView view;
        BattleHud hud;
        readonly CpuBrain[] brains = new CpuBrain[2];
        double acc;
        bool paused;
        RectTransform pauseRoot;
        MenuList pauseMenu;
        Text moveListText;
        readonly TrainingState training = new TrainingState();
        readonly Dictionary<string, VideoClipPlayer> ultVideos = new Dictionary<string, VideoClipPlayer>();
        VideoClipPlayer playing;
        RectTransform videoLayer;
        float cineT;
        int slowmo;
        bool ended;
        float endT;
        bool roundAnnounced;
        Net.LanSession Net => cfg.Net;
        public bool Finished => ended;

        public BattleScreen(MatchConfig cfg) { this.cfg = cfg; }

        public override void Enter()
        {
            InputReader.SoloLayout = !(cfg.Mode == GameMode.Versus);
            Net?.ResetMatch();
            Sim = NewSim();
            view = new BattleView(Sim, cfg.Arena, App.Cam);
            hud = new BattleHud(Root, Sim, cfg) { Training = cfg.Mode == GameMode.Training };
            view.ShowBoxes = cfg.Mode == GameMode.Training && training.Boxes;
            for (int i = 0; i < 2; i++)
                if (!cfg.IsHuman(i) && cfg.Mode != GameMode.Lan)
                    brains[i] = new CpuBrain(i, i == 0 ? cfg.Level1 : cfg.Level2, cfg.Seed + (uint)i * 31u);

            var arena = App.Grimoire.Arenas.Find(a => a.Id == cfg.Arena);
            bool boss = cfg.Mode == GameMode.Arcade && Sim.F[1].Def.Boss;
            App.Audio.PlayMusic(boss ? "boss" : arena != null ? arena.Music : "battle_1");

            videoLayer = Ui.Rect(Root, "videos").Fill();
            if (App.Save.Cutscenes && !ForceNoCinematics)
                foreach (var f in Sim.F)
                {
                    if (ultVideos.ContainsKey(f.Def.Id)) continue;
                    string path = Path.Combine(Application.streamingAssetsPath, "Video", "ult_" + f.Def.Id + ".mp4");
                    if (!File.Exists(path)) continue;
                    var v = VideoClipPlayer.Create(videoLayer, path, false, false);
                    v.gameObject.SetActive(true);
                    v.GetComponent<RawImage>().color = new Color(0, 0, 0, 0);
                    ultVideos[f.Def.Id] = v;
                }
            BuildPause();
        }

        BattleSim NewSim()
        {
            var s = new BattleSim(App.Grimoire, cfg.P1, cfg.P2);
            s.TrainingMode = cfg.Mode == GameMode.Training;
            return s;
        }

        public override void Exit()
        {
            view?.Destroy();
            App.Cam.transform.position = new Vector3(0, 0, -10);
            App.Cam.orthographicSize = 5.4f;
            App.Audio.MusicDuck = 1f;
            foreach (var v in ultVideos.Values) if (v != null) v.Stop();
        }

        // ------------------------------------------------------------------ inputs

        Btn InputFor(int i)
        {
            if (cfg.Mode == GameMode.Training && i == 1) return Dummy();
            if (brains[i] != null) return brains[i].Think(Sim);
            int player = cfg.Mode == GameMode.Versus ? i : 0;
            return InputReader.Read(player);
        }

        Btn Dummy()
        {
            var me = Sim.F[1];
            var op = Sim.F[0];
            Btn back = op.X > me.X ? Btn.Left : Btn.Right;
            switch (training.Dummy)
            {
                case 1: return Btn.Down;
                case 2: return me.Grounded && (Sim.Frame % 50 == 0) ? Btn.Up : Btn.None;
                case 3:
                {
                    bool low = op.State == FState.Attack && op.Move != null && op.Move.Height == MoveHeight.Low;
                    foreach (var p in Sim.Projectiles) if (p.Owner == 0 && p.Def.Ground) low = true;
                    return back | (low ? Btn.Down : Btn.None);
                }
                case 4: case 5: case 6: case 7:
                    if (brains[1] == null || brains[1].Level != training.Dummy - 4) brains[1] = new CpuBrain(1, training.Dummy - 4, 99);
                    return brains[1].Think(Sim);
                default: return Btn.None;
            }
        }

        // ------------------------------------------------------------------ loop

        public override void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            if (cfg.Mode == GameMode.Training) TrainingKeys();

            if (InputReader.PausePressed() && !ended && playing == null)
            {
                SetPaused(!paused);
                return;
            }
            if (paused)
            {
                var nav = InputReader.AnyMenu(out _);
                if (nav == InputReader.Nav.Back) SetPaused(false);
                else pauseMenu.Handle(nav);
                pauseMenu?.Refresh();
                if (Net != null) NetStep(dt);   // keep the lockstep alive over the network
                view.Render(dt);
                hud.Render(dt, view.ScreenFlash, training);
                return;
            }

            if (playing != null) { TickCinematic(dt); view.Render(0); hud.Render(dt, view.ScreenFlash, training); return; }

            if (Net != null) NetStep(dt);
            else
            {
                acc += dt * SpeedMultiplier;
                int steps = 0;
                int maxSteps = 4 * SpeedMultiplier;
                while (acc >= BattleSim.Dt && steps < maxSteps && playing == null)
                {
                    acc -= BattleSim.Dt;
                    if (slowmo > 0 && (slowmo-- & 1) == 1) continue; // in-engine ultimate slow motion
                    StepOnce(InputFor(0), InputFor(1));
                    steps++;
                }
                if (steps >= maxSteps) acc = 0;
            }

            view.Render(dt);
            hud.Render(dt, view.ScreenFlash, training);
            App.Audio.MusicDuck = Mathf.MoveTowards(App.Audio.MusicDuck, Sim.Freeze > 0 ? 0.35f : 1f, dt * 2f);

            if (ended)
            {
                endT += dt * SpeedMultiplier;
                if (endT > 3.2f) Finish();
            }
        }

        void NetStep(float dt)
        {
            Net.Poll();
            acc += dt;
            int steps = 0;
            while (acc >= BattleSim.Dt && steps < 4)
            {
                int next = Sim.Frame + 1;
                Net.SendLocalUpTo(next, () => InputReader.Read(0));
                if (!Net.TryGet(next, out Btn a, out Btn b)) break; // waiting for the remote input
                acc -= BattleSim.Dt;
                StepOnce(a, b);
                if (Sim.Frame % 30 == 0) Net.SendChecksum(Sim.Frame, Sim.Checksum());
                Net.CheckChecksum(Sim.Frame, Sim.Checksum());
                steps++;
            }
            if (acc > 0.25) acc = 0.25;
            string status = Net.Status;
            if (Net.Desynced) status += "   <color=#FF5050>DESYNC DETECTED at frame " + Net.DesyncFrame + "</color>";
            hud.SetNet(status);
            if (Net.Disconnected && !ended)
            {
                hud.Announce("DISCONNECTED", "Opponent left the rift");
                ended = true;
            }
        }

        void StepOnce(Btn a, Btn b)
        {
            Sim.Step(a, b);
            view.OnEvents(Sim.Events, App.Audio);
            foreach (var e in Sim.Events) HandleEvent(e);
            if (cfg.Mode == GameMode.Training)
            {
                var f = Sim.F[0];
                if (f.Move != null) training.LastMove = f.Move;
                if (Sim.F[1].ComboCount > 0) { training.LastComboHits = Sim.F[1].ComboCount; training.LastComboDamage = Mathf.RoundToInt(Sim.F[1].ComboDamage); }
            }
        }

        void HandleEvent(SimEvent e)
        {
            switch (e.Type)
            {
                case Ev.RoundIntro:
                {
                    bool final = Sim.F[0].RoundWins == Sim.R.RoundsToWin - 1 && Sim.F[1].RoundWins == Sim.R.RoundsToWin - 1;
                    if (cfg.Mode == GameMode.Training) { hud.Announce("TRAINING", "F1-F5 for tools"); break; }
                    hud.Announce(final ? "FINAL ROUND" : "ROUND " + Sim.Round, Sim.F[0].Def.Name + "  vs  " + Sim.F[1].Def.Name);
                    App.Audio.Voice(final ? "final_round" : "round_" + Mathf.Min(3, Sim.Round));
                    App.Audio.Sfx("round_bell");
                    break;
                }
                case Ev.Fight:
                    if (cfg.Mode != GameMode.Training) { hud.Announce("FIGHT!", "", Color.white); App.Audio.Voice("fight"); }
                    break;
                case Ev.KO:
                    hud.Announce("K.O.", Sim.Perfect ? "PERFECT!" : "", new Color(1f, 0.25f, 0.2f));
                    App.Audio.Voice(Sim.Perfect ? "perfect" : "ko");
                    break;
                case Ev.TimeUp:
                    hud.Announce("TIME!", e.Who == 2 ? "DRAW" : Sim.F[e.Who].Def.Name + " wins the round");
                    App.Audio.Voice("time_up");
                    break;
                case Ev.MatchOver:
                {
                    string w = e.Who == 2 ? "DRAW" : WinnerLabel(e.Who) + " WINS";
                    hud.Announce(w, e.Who < 2 ? Sim.F[e.Who].Def.Name : "", Ui.Gold);
                    App.Audio.Voice(e.Who == 2 ? "draw" : e.Who == 0 ? "p1_wins" : "p2_wins");
                    App.Audio.PlayMusic("victory");
                    ended = true;
                    endT = 0;
                    break;
                }
                case Ev.UltStart:
                    hud.UltBanner(Sim.F[e.Who]);
                    break;
                case Ev.UltConnect:
                    StartCinematic(Sim.F[e.Who]);
                    break;
                case Ev.OrbSpawn:
                    hud.Announce("", "KI ORB INCOMING!");
                    break;
            }
        }

        string WinnerLabel(int who)
        {
            if (cfg.Mode == GameMode.Lan && Net != null) return who == Net.LocalIndex ? "YOU" : "RIVAL";
            if (cfg.IsHuman(who) && cfg.Mode == GameMode.Versus) return who == 0 ? "P1" : "P2";
            if (cfg.IsHuman(who)) return "YOU";
            return "CPU";
        }

        // ------------------------------------------------------------------ cinematics

        void StartCinematic(Fighter f)
        {
            if (ultVideos.TryGetValue(f.Def.Id, out var v) && v != null && v.Ready && !ForceNoCinematics && SpeedMultiplier == 1)
            {
                playing = v;
                playing.transform.SetAsLastSibling();
                playing.GetComponent<RawImage>().color = Color.white;
                playing.Play();
                cineT = 0;
                App.Audio.MusicDuck = 0.2f;
                return;
            }
            slowmo = 50;
            view.Shake(0.3f);
        }

        void TickCinematic(float dt)
        {
            cineT += dt;
            bool skip = cineT > 0.5f && (InputReader.Read(0) & Btn.Attacks) != 0 && cfg.Mode != GameMode.Lan;
            if (playing.Finished || cineT > 4.5f || skip)
            {
                playing.Stop();
                playing.GetComponent<RawImage>().color = new Color(0, 0, 0, 0);
                playing = null;
                acc = 0;
                App.Audio.MusicDuck = 1f;
                view.Shake(0.4f);
            }
        }

        // ------------------------------------------------------------------ pause

        void BuildPause()
        {
            pauseRoot = Ui.Rect(Root, "pause").Fill();
            var dim = Ui.Box(pauseRoot, new Color(0, 0, 0, 0.72f));
            dim.rectTransform.Fill();
            var t = Ui.Text(pauseRoot, "PAUSED", 110, Ui.Gold).Outlined(5);
            t.rectTransform.Place(0.3f, 0.85f, 900, 140);
            pauseMenu = new MenuList(pauseRoot, 0.3f, 0.72f, 700, 70, 46);
            pauseMenu.Add("RESUME", () => SetPaused(false));
            if (cfg.Mode != GameMode.Lan)
            {
                pauseMenu.Add("RESTART MATCH", () => { App.Go(new BattleScreen(cfg)); });
                pauseMenu.Add("CHARACTER SELECT", () => { App.Go(new CharSelectScreen(cfg)); });
            }
            pauseMenu.Add("MAIN MENU", () => { Net?.Close(); App.Go(new MainMenuScreen()); });
            pauseMenu.OnMove = i => App.Click();
            var panel = Ui.Box(pauseRoot, Ui.Panel);
            panel.rectTransform.Place(0.75f, 0.5f, 860, 900);
            moveListText = Ui.Text(panel.rectTransform, MoveListScreen.Describe(Sim.F[0].Def, true), 24, Color.white, TextAnchor.UpperLeft);
            moveListText.rectTransform.Fill(28, 20, 28, 24);
            pauseRoot.gameObject.SetActive(false);
        }

        void SetPaused(bool p)
        {
            if (cfg.Mode == GameMode.Lan && p == false && pauseRoot.gameObject.activeSelf == false) return;
            paused = p;
            pauseRoot.gameObject.SetActive(p);
            pauseRoot.SetAsLastSibling();
            App.Audio.MusicDuck = p ? 0.4f : 1f;
            App.Click(p ? "ui_confirm" : "ui_back");
        }

        // ------------------------------------------------------------------ training tools

        void TrainingKeys()
        {
            if (InputReader.KeyDown(Key.F1)) { training.Boxes = !training.Boxes; view.ShowBoxes = training.Boxes; }
            if (InputReader.KeyDown(Key.F2)) { training.Dummy = (training.Dummy + 1) % TrainingState.DummyNames.Length; brains[1] = null; }
            if (InputReader.KeyDown(Key.F4)) { training.Refill = !training.Refill; Sim.TrainingMode = training.Refill; }
            if (InputReader.KeyDown(Key.F3) || InputReader.KeyDown(Key.F5))
            {
                if (InputReader.KeyDown(Key.F5)) { App.LoadGrimoire(); AssetDb.ClearFighterCache(); }
                view.Destroy();
                Sim = NewSim();
                Sim.TrainingMode = training.Refill;
                view = new BattleView(Sim, cfg.Arena, App.Cam) { ShowBoxes = training.Boxes };
                hud.Destroy();
                hud = new BattleHud(Root, Sim, cfg) { Training = true };
                pauseRoot.SetAsLastSibling();
                hud.Announce(InputReader.KeyDown(Key.F5) ? "GRIMOIRE RELOADED" : "RESET", "");
            }
        }

        // ------------------------------------------------------------------ end of match

        void Finish()
        {
            if (cfg.Mode == GameMode.Training) { App.Go(new MainMenuScreen()); return; }
            if (AutoTest.Running) return; // the autotest drives what happens next
            int w = Sim.Winner;
            if (cfg.Mode == GameMode.Arcade)
            {
                if (w == 0)
                {
                    int score = ArcadeScreen.ScoreFor(Sim, cfg);
                    cfg.ArcadeScore += score;
                    cfg.ArcadeStage++;
                    if (cfg.ArcadeStage >= cfg.ArcadeLadder.Length)
                    {
                        App.Save.BossUnlocked = true;
                        int rank = App.Save.AddScore(cfg.P1, cfg.ArcadeScore, cfg.ArcadeStage, cfg.Level2);
                        App.Go(new ResultScreen(cfg, Sim, "ARCADE CLEARED!", rank));
                    }
                    else App.Go(new ArcadeScreen(cfg));
                }
                else
                {
                    int rank = App.Save.AddScore(cfg.P1, cfg.ArcadeScore, cfg.ArcadeStage, cfg.Level2);
                    App.Go(new ResultScreen(cfg, Sim, "GAME OVER", rank));
                }
                return;
            }
            App.Go(new ResultScreen(cfg, Sim, null, -1));
        }
    }
}
