using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using MysticKombat.Core;
using UnityEngine;

namespace MysticKombat
{
    /// <summary>
    /// Self-test driver for the real game build. Command line:
    ///   -mkautotest menus            visit every screen and screenshot it
    ///   -mkautotest cpu [N]          N CPU-vs-CPU matches through the full renderer, screenshots + FPS report
    ///   -mkautotest lanhost|lanclient  CPU-driven LAN match over localhost (run both), reports desyncs
    /// Output goes to -mkout &lt;dir&gt; (default: ./autotest next to the executable).
    /// </summary>
    public sealed class AutoTest : MonoBehaviour
    {
        public static bool Running { get; private set; }
        string mode = "cpu", outDir;
        int count = 3;
        readonly StringBuilder log = new StringBuilder();
        int errors;
        int shot;

        public static bool Requested() => Environment.GetCommandLineArgs().Contains("-mkautotest");

        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : def;
        }

        void Start()
        {
            Running = true;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-mkautotest");
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) mode = args[i + 1];
            if (i + 2 < args.Length && int.TryParse(args[i + 2], out int n)) count = n;
            outDir = Arg("-mkout", Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "autotest"));
            Directory.CreateDirectory(outDir);
            Application.logMessageReceived += OnLog;
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            Log($"autotest {mode} x{count} on {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
            GameApp.I.Save.Cutscenes = Arg("-mkcutscenes", "1") == "1";
            StartCoroutine(Run());
        }

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errors++;
                Log("ERROR " + type + ": " + msg + "\n" + stack);
            }
        }

        void Log(string s)
        {
            string line = $"[{Time.realtimeSinceStartup,7:F1}] {s}";
            log.AppendLine(line);
            Debug.Log("[autotest] " + s);
            File.WriteAllText(Path.Combine(outDir, "report.txt"), log.ToString());
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(outDir, $"{shot++:00}_{name}.png");
            ScreenCapture.CaptureScreenshot(file);
            yield return null;
            Log("shot " + Path.GetFileName(file));
        }

        IEnumerator Run()
        {
            yield return null;
            yield return null;
            switch (mode)
            {
                case "menus": yield return Menus(); break;
                case "lanhost": yield return Lan(true); break;
                case "lanclient": yield return Lan(false); break;
                default: yield return CpuMatches(); break;
            }
            Log($"done, {errors} errors");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        IEnumerator Menus()
        {
            var app = GameApp.I;
            app.Go(new TitleScreen()); yield return Wait(1.2f); yield return Shot("title");
            app.Go(new MainMenuScreen()); yield return Wait(0.8f); yield return Shot("mainmenu");
            var cfg = new MatchConfig { Mode = GameMode.VsCpu, P2Cpu = true, P1 = "kazu", P2 = "goro" };
            app.Go(new CharSelectScreen(cfg)); yield return Wait(0.8f); yield return Shot("charselect");
            app.Go(new StageSelectScreen(cfg)); yield return Wait(0.8f); yield return Shot("stageselect");
            app.Go(new VsScreen(cfg)); yield return Wait(1.0f); yield return Shot("vs");
            app.Go(new OptionsScreen()); yield return Wait(0.5f); yield return Shot("options");
            app.Go(new ControlsScreen()); yield return Wait(0.5f); yield return Shot("controls");
            app.Go(new MoveListScreen(null)); yield return Wait(0.5f); yield return Shot("movelist");
            app.Go(new LanScreen()); yield return Wait(0.5f); yield return Shot("lan");
            var arcade = new MatchConfig { Mode = GameMode.Arcade, P2Cpu = true, P1 = "shin", Level2 = 2 };
            arcade.ArcadeLadder = ArcadeScreen.BuildLadder(app.Grimoire, "shin", 7);
            app.Go(new ArcadeScreen(arcade)); yield return Wait(0.6f); yield return Shot("arcade");
            var tr = new MatchConfig { Mode = GameMode.Training, P1 = "ren", P2 = "kuro", Arena = "kamar_temple" };
            app.Go(new BattleScreen(tr)); yield return Wait(2.5f); yield return Shot("training");
            foreach (var f in app.Grimoire.Fighters)
            {
                var c = new MatchConfig { Mode = GameMode.CpuVsCpu, P1Cpu = true, P2Cpu = true, P1 = f.Id, P2 = app.Grimoire.Fighters[(app.Grimoire.Fighters.IndexOf(f) + 3) % app.Grimoire.Fighters.Count].Id, Arena = app.Grimoire.Arenas[app.Grimoire.Fighters.IndexOf(f) % app.Grimoire.Arenas.Count].Id, Level1 = 3, Level2 = 3 };
                app.Go(new BattleScreen(c));
                yield return Wait(4.5f);
                yield return Shot("battle_" + f.Id);
            }
        }

        IEnumerator CpuMatches()
        {
            var app = GameApp.I;
            var g = app.Grimoire;
            var rng = new System.Random(Environment.TickCount);
            for (int m = 0; m < count; m++)
            {
                var ids = g.Fighters.Select(f => f.Id).ToList();
                var cfg = new MatchConfig
                {
                    Mode = GameMode.CpuVsCpu, P1Cpu = true, P2Cpu = true,
                    P1 = ids[rng.Next(ids.Count)], P2 = ids[rng.Next(ids.Count)],
                    Arena = g.Arenas[rng.Next(g.Arenas.Count)].Id,
                    Level1 = rng.Next(4), Level2 = rng.Next(4), Seed = (uint)rng.Next(),
                };
                Log($"match {m + 1}: {cfg.P1} (L{cfg.Level1}) vs {cfg.P2} (L{cfg.Level2}) on {cfg.Arena}");
                var bs = new BattleScreen(cfg);
                app.Go(bs);
                float start = Time.realtimeSinceStartup, nextShot = 3f;
                int frames = 0;
                float worst = 0;
                while (!bs.Finished && Time.realtimeSinceStartup - start < 240f)
                {
                    frames++;
                    worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                    float el = Time.realtimeSinceStartup - start;
                    if (el > nextShot) { nextShot += 9f; yield return Shot($"m{m + 1}_{cfg.P1}_vs_{cfg.P2}_{(int)el}s"); }
                    yield return null;
                }
                float dur = Time.realtimeSinceStartup - start;
                var sim = bs.Sim;
                Log($"  result winner={sim.Winner} rounds {sim.F[0].RoundWins}-{sim.F[1].RoundWins} in {dur:F1}s, avg fps {frames / dur:F1}, worst frame {worst * 1000:F0} ms, ults {sim.F[0].Stats.Ults}/{sim.F[1].Stats.Ults}");
                yield return Wait(2.5f);
                yield return Shot($"m{m + 1}_end");
            }
        }

        IEnumerator Lan(bool host)
        {
            var app = GameApp.I;
            var net = host ? Net.LanSession.Host() : Net.LanSession.Client();
            if (!host) net.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, Net.LanSession.GamePort));
            float t0 = Time.realtimeSinceStartup;
            while (!net.Connected && Time.realtimeSinceStartup - t0 < 60f) { net.Poll(); if (!host && (int)(Time.realtimeSinceStartup * 2) % 2 == 0) net.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, Net.LanSession.GamePort)); yield return null; }
            Log("connected: " + net.Connected);
            net.ResetMatch();
            net.SendPick(host ? "goro" : "kuro");
            while (!net.StartReceived && Time.realtimeSinceStartup - t0 < 90f)
            {
                net.Poll();
                if (host && net.RemotePick != null) net.SendStart("goro", net.RemotePick, "tournament_isle", 4242);
                else if (net.LocalPick != null && (int)(Time.realtimeSinceStartup * 4) % 2 == 0) net.SendPick(net.LocalPick);
                yield return null;
            }
            Log($"start: {net.StartP1} vs {net.StartP2}");
            var cfg = new MatchConfig { Mode = GameMode.Lan, P1 = net.StartP1, P2 = net.StartP2, Arena = net.StartArena, Seed = net.StartSeed, Net = net };
            var bs = new BattleScreen(cfg);
            app.Go(bs);
            var brain = new CpuBrain(net.LocalIndex, 3, host ? 1u : 2u);
            net.InputOverride = () => brain.Think(bs.Sim);
            float st = Time.realtimeSinceStartup;
            float nextShot = 5f;
            while (!bs.Finished && Time.realtimeSinceStartup - st < 300f)
            {
                if (Time.realtimeSinceStartup - st > nextShot) { nextShot += 15f; yield return Shot((host ? "host" : "client") + "_" + (int)nextShot); }
                yield return null;
            }
            Log($"LAN match over: winner={bs.Sim.Winner} frame={bs.Sim.Frame} checksum={bs.Sim.Checksum():X8} desync={net.Desynced} at {net.DesyncFrame}");
            net.Close();
        }

        static IEnumerator Wait(float s)
        {
            float end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
