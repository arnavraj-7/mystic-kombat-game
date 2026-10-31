using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MysticKombat.Core;

// Headless harness for the pure-C# battle simulation:
//   dotnet run -- balance [matchesPerPair] [level]   round-robin CPU vs CPU, win-rate matrix + stats
//   dotnet run -- determinism                        same seed twice -> identical checksums
//   dotnet run -- trace kazu goro [level]            one match with a per-second state trace
static class Program
{
    static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    static Grimoire LoadGrimoire()
    {
        string dir = Path.Combine(Root, "Assets/StreamingAssets/grimoire");
        return Grimoire.Load(File.ReadAllText(Path.Combine(dir, "fighters.json")), File.ReadAllText(Path.Combine(dir, "arenas.json")));
    }

    static int Main(string[] args)
    {
        var g = LoadGrimoire();
        string cmd = args.Length > 0 ? args[0] : "balance";
        switch (cmd)
        {
            case "determinism": return Determinism(g);
            case "trace": Trace(g, args.Length > 1 ? args[1] : "kazu", args.Length > 2 ? args[2] : "goro", args.Length > 3 ? int.Parse(args[3]) : 3); return 0;
            default: Balance(g, args.Length > 1 ? int.Parse(args[1]) : 10, args.Length > 2 ? int.Parse(args[2]) : 3); return 0;
        }
    }

    sealed class Result { public int Winner; public int Frames; public BattleSim Sim; }

    static Result RunMatch(Grimoire g, string a, string b, int la, int lb, uint seed, int maxFrames = 60 * 60 * 8, Action<BattleSim> perFrame = null)
    {
        var sim = new BattleSim(g, a, b);
        var c0 = new CpuBrain(0, la, seed);
        var c1 = new CpuBrain(1, lb, seed + 7777);
        int frames = 0;
        while (!sim.IsMatchOver && frames < maxFrames)
        {
            var i0 = c0.Think(sim);
            var i1 = c1.Think(sim);
            sim.Step(i0, i1);
            perFrame?.Invoke(sim);
            frames++;
        }
        return new Result { Winner = sim.IsMatchOver ? sim.Winner : -1, Frames = frames, Sim = sim };
    }

    static void Balance(Grimoire g, int n, int level)
    {
        var ids = g.Fighters.Where(f => !f.Boss).Select(f => f.Id).ToList();
        var wins = new Dictionary<string, int>();
        var games = new Dictionary<string, int>();
        var matrix = new Dictionary<(string, string), int>();
        var dmg = new Dictionary<string, float>();
        var stats = new Dictionary<string, MatchStats>();
        foreach (var id in ids) { wins[id] = 0; games[id] = 0; dmg[id] = 0; stats[id] = new MatchStats(); }
        long totalFrames = 0; int matches = 0, timeouts = 0, draws = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        uint seed = 1;
        foreach (var a in ids)
            foreach (var b in ids)
            {
                if (a == b) continue;
                for (int k = 0; k < n; k++)
                {
                    var r = RunMatch(g, a, b, level, level, seed++);
                    matches++;
                    totalFrames += r.Frames;
                    games[a]++; games[b]++;
                    if (r.Winner == 0) { wins[a]++; matrix[(a, b)] = matrix.GetValueOrDefault((a, b)) + 1; }
                    else if (r.Winner == 1) { wins[b]++; matrix[(b, a)] = matrix.GetValueOrDefault((b, a)) + 1; }
                    else if (r.Winner == 2) draws++;
                    else timeouts++;
                    Acc(stats[a], r.Sim.F[0].Stats);
                    Acc(stats[b], r.Sim.F[1].Stats);
                }
            }
        Console.WriteLine($"{matches} matches at level {CpuBrain.LevelNames[level]} in {sw.Elapsed.TotalSeconds:F1}s, avg match {totalFrames / (float)matches / 60f:F1}s game time, draws {draws}, stuck {timeouts}");
        Console.WriteLine("fighter      win%   dmg/match hits  blocks spec ults ultHit heal  throws brk  counters maxCombo orbs");
        foreach (var id in ids.OrderByDescending(i => wins[i] / (float)games[i]))
        {
            var s = stats[id];
            float gm = games[id];
            Console.WriteLine($"{id,-10} {100f * wins[id] / gm,6:F1} {s.DamageDealt / gm,9:F0} {s.Hits / gm,5:F1} {s.Blocks / gm,6:F1} {s.Specials / gm,4:F1} {s.Ults / gm,4:F2} {s.UltHits / gm,6:F2} {s.Healed / gm,5:F0} {s.Throws / gm,6:F2} {s.ThrowBreaks / gm,4:F2} {s.Counters / gm,8:F2} {s.MaxCombo,8} {s.OrbsTaken / gm,4:F2}");
        }
        Console.WriteLine();
        Console.Write("row beats col ".PadRight(10));
        foreach (var b in ids) Console.Write(b.Substring(0, Math.Min(4, b.Length)).PadLeft(5));
        Console.WriteLine();
        foreach (var a in ids)
        {
            Console.Write(a.PadRight(10));
            foreach (var b in ids)
            {
                if (a == b) { Console.Write("    -"); continue; }
                int w = matrix.GetValueOrDefault((a, b));
                int l = matrix.GetValueOrDefault((b, a));
                Console.Write($"{(w + l == 0 ? 0 : 100 * w / (w + l)),5}");
            }
            Console.WriteLine();
        }
        // difficulty ladder sanity: higher level should beat lower level
        Console.WriteLine();
        for (int lo = 0; lo < 3; lo++)
        {
            int hw = 0, tot = 0;
            for (int k = 0; k < 40; k++)
            {
                string a = ids[k % ids.Count], b = ids[(k * 7 + 3) % ids.Count];
                var r = RunMatch(g, a, b, lo + 1, lo, 1000u + (uint)k);
                if (r.Winner == 0) hw++;
                if (r.Winner >= 0) tot++;
            }
            Console.WriteLine($"{CpuBrain.LevelNames[lo + 1]} beats {CpuBrain.LevelNames[lo]}: {hw}/{tot}");
        }
        {
            int bw = 0;
            foreach (var a in ids) { var r = RunMatch(g, a, "malgrath", 3, 2, 55u); if (r.Winner == 1) bw++; }
            Console.WriteLine($"Boss (Hard AI) beats Mystic-AI roster: {bw}/{ids.Count}");
        }
    }

    static void Acc(MatchStats t, MatchStats s)
    {
        t.DamageDealt += s.DamageDealt; t.Hits += s.Hits; t.Blocks += s.Blocks; t.Specials += s.Specials; t.Ults += s.Ults;
        t.UltHits += s.UltHits; t.Healed += s.Healed; t.Throws += s.Throws; t.ThrowBreaks += s.ThrowBreaks; t.Counters += s.Counters;
        t.MaxCombo = Math.Max(t.MaxCombo, s.MaxCombo); t.OrbsTaken += s.OrbsTaken;
    }

    static int Determinism(Grimoire g)
    {
        var sums1 = new List<uint>();
        var sums2 = new List<uint>();
        RunMatch(g, "kazu", "ren", 3, 2, 42, perFrame: s => { if (s.Frame % 60 == 0) sums1.Add(s.Checksum()); });
        RunMatch(g, "kazu", "ren", 3, 2, 42, perFrame: s => { if (s.Frame % 60 == 0) sums2.Add(s.Checksum()); });
        bool same = sums1.SequenceEqual(sums2);
        Console.WriteLine($"determinism: {(same ? "OK" : "MISMATCH")} ({sums1.Count} checkpoints)");
        return same ? 0 : 1;
    }

    static void Trace(Grimoire g, string a, string b, int level)
    {
        var r = RunMatch(g, a, b, level, level, 7, perFrame: s =>
        {
            foreach (var e in s.Events)
                if (e.Type != Ev.Whiff && e.Type != Ev.MoveStart && e.Type != Ev.Jump && e.Type != Ev.Land)
                    Console.WriteLine($"f{s.Frame,6} r{s.Round} {e.Type,-14} who={e.Who} v={e.Value,6:F1} {e.Str} combo={e.Combo}");
            if (s.Frame % 120 == 0)
            {
                var f0 = s.F[0]; var f1 = s.F[1];
                Console.WriteLine($"f{s.Frame,6} t={s.TimerFrames / 60} | {f0.Def.Id} x={f0.X,5:F1} hp={f0.Hp,5:F0} ki={f0.Ki,4:F0} {f0.State} | {f1.Def.Id} x={f1.X,5:F1} hp={f1.Hp,5:F0} ki={f1.Ki,4:F0} {f1.State}");
            }
        });
        Console.WriteLine($"winner {r.Winner} after {r.Frames / 60f:F1}s");
    }
}
