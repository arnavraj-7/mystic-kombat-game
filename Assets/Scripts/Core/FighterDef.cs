using System;
using System.Collections.Generic;

namespace MysticKombat.Core
{
    public enum MoveHeight { Mid, Low, Overhead, Unblockable, Throw }

    /// <summary>Projectile spawned by a move.</summary>
    public sealed class ProjectileDef
    {
        public string Vfx = "ki_blast";
        public float Speed = 10f, W = 0.6f, H = 0.6f, Y = 1.3f, X = 0.8f, VY = 0f, Gravity = 0f, Scale = 1f;
        public int Life = 120, Count = 1, Interval = 8, Hits = 1, HitInterval = 6;
        public float Damage = 30f, KbX = 2f, KbY = 0f;
        public int Hitstun = 14, Blockstun = 10, Hitstop = 5;
        public bool Ground, Pierce, Launch, Knockdown;
        public float SpreadY;        // vertical spread between volley shots
        public float Strength = 1f;  // projectiles of higher strength win clashes
        public MoveHeight Height = MoveHeight.Mid;
    }

    /// <summary>One move from the grimoire. Frame counts are 60 fps simulation frames.</summary>
    public sealed class MoveDef
    {
        public string Id, Name, Pose, Kind = "strike", Sfx, Vfx, Voice;
        public string Slot;                     // light/heavy/... or special1..3 / ultimate
        public int Startup = 6, Active = 3, Recovery = 10;
        public float Damage = 30f, Chip = 0f;
        public int Hitstun = 14, Blockstun = 10, Hitstop = 6;
        public float KbX = 1.5f, KbY = 0f;
        public float[] Hitbox = { 0.7f, 1.2f, 0.9f, 0.5f }; // fwd offset, centre height, width, height
        public MoveHeight Height = MoveHeight.Mid;
        public int Hits = 1, HitInterval = 0;
        public float MoveX, MoveStartupX, MoveY;  // velocities (u/s) applied while startup/active
        public bool Launch, Knockdown, Airborne, TargetOpponent, Afterimages, Cinematic;
        public int InvulnFrom = -1, InvulnTo = -1, ArmorFrom = -1, ArmorTo = -1;
        public int CancelWindow = 12;          // frames after hit/block during which a cancel is allowed
        public int Charges, Recharge;           // ability charges; Recharge 0 = per round only
        public float KiCost;
        public ProjectileDef Projectile;
        public int ProjectileFrame = -1;        // defaults to Startup

        // Behaviour blocks (optional)
        public float HealAmount; public int HealFrom, HealTo;
        public float BuffDamage = 1f, BuffSpeed = 1f; public int BuffDuration; public bool BuffArmor;
        public int ShieldFrom = -1, ShieldTo = -1; public bool ShieldReflect;
        public int CounterFrom = -1, CounterTo = -1; public string CounterInto;
        public string TeleportMode; public float TeleportDist = 2.5f; public int TeleportFrame = -1;
        public float PullX;                     // on hit, drag the victim toward the attacker
        public int TotalFrames => Startup + Active + Recovery;
    }

    public sealed class FighterDef
    {
        public string Id, Name, Title, Description, Color = "#FFFFFF", Archetype;
        public float MaxHp = 1000f, WalkSpeed = 3.4f, BackSpeed = 2.6f, JumpVel = 14f, AirSpeed = 4f;
        public float Height = 2.1f, Width = 0.8f, Weight = 1f, Power = 1f, Defense = 1f, KiGain = 1f;
        public bool Boss;
        public readonly Dictionary<string, MoveDef> Moves = new Dictionary<string, MoveDef>();
        public MoveDef Move(string id) => id != null && Moves.TryGetValue(id, out var m) ? m : null;
    }

    /// <summary>The Grimoire: every fighter and global tuning, loaded from JSON at runtime.</summary>
    public sealed class Grimoire
    {
        public readonly List<FighterDef> Fighters = new List<FighterDef>();
        public readonly Dictionary<string, FighterDef> ById = new Dictionary<string, FighterDef>();
        public readonly List<ArenaDef> Arenas = new List<ArenaDef>();
        public Rules Rules = new Rules();

        public FighterDef Get(string id) => ById.TryGetValue(id, out var f) ? f : null;

        public static Grimoire Load(string fightersJson, string arenasJson = null)
        {
            var g = new Grimoire();
            var root = MiniJson.Parse(fightersJson) as Dictionary<string, object>;
            var rules = J.Obj(root, "rules");
            if (rules != null) g.Rules = Rules.FromJson(rules);
            var defaults = J.Obj(root, "defaultMoves") ?? new Dictionary<string, object>();
            foreach (var fo in J.Arr(root, "fighters"))
            {
                var fd = fo as Dictionary<string, object>;
                var f = new FighterDef
                {
                    Id = J.Str(fd, "id"),
                    Name = J.Str(fd, "name"),
                    Title = J.Str(fd, "title", ""),
                    Description = J.Str(fd, "description", ""),
                    Archetype = J.Str(fd, "archetype", ""),
                    Color = J.Str(fd, "color", "#FFFFFF"),
                    MaxHp = J.F(fd, "hp", 1000),
                    WalkSpeed = J.F(fd, "walkSpeed", 3.4f),
                    BackSpeed = J.F(fd, "backSpeed", 2.6f),
                    JumpVel = J.F(fd, "jumpVel", 14f),
                    AirSpeed = J.F(fd, "airSpeed", 4f),
                    Height = J.F(fd, "height", 2.1f),
                    Width = J.F(fd, "width", 0.8f),
                    Weight = J.F(fd, "weight", 1f),
                    Power = J.F(fd, "power", 1f),
                    Defense = J.F(fd, "defense", 1f),
                    KiGain = J.F(fd, "kiGain", 1f),
                    Boss = J.B(fd, "boss"),
                };
                // shared normals first, then the fighter's own moves override/extend them
                foreach (var kv in defaults)
                {
                    var m = ParseMove(kv.Key, kv.Value as Dictionary<string, object>, null);
                    f.Moves[m.Id] = m;
                }
                var moves = J.Obj(fd, "moves");
                if (moves != null)
                    foreach (var kv in moves)
                    {
                        f.Moves.TryGetValue(kv.Key, out var baseMove);
                        var m = ParseMove(kv.Key, kv.Value as Dictionary<string, object>, defaults.TryGetValue(kv.Key, out var dv) ? dv as Dictionary<string, object> : null);
                        f.Moves[m.Id] = m;
                    }
                g.Fighters.Add(f);
                g.ById[f.Id] = f;
            }
            if (arenasJson != null)
            {
                var ar = MiniJson.Parse(arenasJson) as Dictionary<string, object>;
                foreach (var ao in J.Arr(ar, "arenas"))
                {
                    var a = ao as Dictionary<string, object>;
                    g.Arenas.Add(new ArenaDef
                    {
                        Id = J.Str(a, "id"),
                        Name = J.Str(a, "name"),
                        FloorY = J.F(a, "floorY", 0.16f),
                        Particles = J.Str(a, "particles", "dust"),
                        Music = J.Str(a, "music", "battle_1"),
                        Tint = J.Str(a, "tint", "#FFFFFF"),
                        BossOnly = J.B(a, "bossOnly"),
                    });
                }
            }
            return g;
        }

        /// <summary>Parse a move; fields missing in <paramref name="o"/> fall back to <paramref name="baseObj"/>.</summary>
        static MoveDef ParseMove(string id, Dictionary<string, object> o, Dictionary<string, object> baseObj)
        {
            var d = new Dictionary<string, object>();
            if (baseObj != null) foreach (var kv in baseObj) d[kv.Key] = kv.Value;
            if (o != null) foreach (var kv in o) d[kv.Key] = kv.Value;

            var m = new MoveDef
            {
                Id = id,
                Name = J.Str(d, "name", id),
                Pose = J.Str(d, "pose", id),
                Kind = J.Str(d, "kind", "strike"),
                Slot = J.Str(d, "slot", id),
                Sfx = J.Str(d, "sfx"),
                Vfx = J.Str(d, "vfx"),
                Voice = J.Str(d, "voice"),
                Startup = J.I(d, "startup", 6),
                Active = J.I(d, "active", 3),
                Recovery = J.I(d, "recovery", 10),
                Damage = J.F(d, "damage", 30),
                Chip = J.F(d, "chip", 0),
                Hitstun = J.I(d, "hitstun", 14),
                Blockstun = J.I(d, "blockstun", 10),
                Hitstop = J.I(d, "hitstop", 6),
                KbX = J.F(d, "kbX", 1.5f),
                KbY = J.F(d, "kbY", 0f),
                Hitbox = J.Floats(d, "hitbox") ?? new[] { 0.7f, 1.2f, 0.9f, 0.5f },
                Height = ParseHeight(J.Str(d, "height", "mid")),
                Hits = Math.Max(1, J.I(d, "hits", 1)),
                HitInterval = J.I(d, "hitInterval", 0),
                MoveX = J.F(d, "moveX"),
                MoveStartupX = J.F(d, "moveStartupX"),
                MoveY = J.F(d, "moveY"),
                Launch = J.B(d, "launch"),
                Knockdown = J.B(d, "knockdown"),
                Airborne = J.B(d, "airborne"),
                TargetOpponent = J.B(d, "targetOpponent"),
                Afterimages = J.B(d, "afterimages"),
                Cinematic = J.B(d, "cinematic"),
                CancelWindow = J.I(d, "cancelWindow", 12),
                Charges = J.I(d, "charges", 0),
                Recharge = J.I(d, "recharge", 0),
                KiCost = J.F(d, "kiCost", 0),
                ProjectileFrame = J.I(d, "projectileFrame", -1),
                PullX = J.F(d, "pullX"),
            };
            var inv = J.Floats(d, "invuln");
            if (inv != null && inv.Length == 2) { m.InvulnFrom = (int)inv[0]; m.InvulnTo = (int)inv[1]; }
            var arm = J.Floats(d, "armor");
            if (arm != null && arm.Length == 2) { m.ArmorFrom = (int)arm[0]; m.ArmorTo = (int)arm[1]; }

            var heal = J.Obj(d, "heal");
            if (heal != null) { m.HealAmount = J.F(heal, "amount"); m.HealFrom = J.I(heal, "from", m.Startup); m.HealTo = J.I(heal, "to", m.Startup + m.Active); }
            var buff = J.Obj(d, "buff");
            if (buff != null) { m.BuffDamage = J.F(buff, "damage", 1f); m.BuffSpeed = J.F(buff, "speed", 1f); m.BuffDuration = J.I(buff, "duration", 300); m.BuffArmor = J.B(buff, "armor"); }
            var shield = J.Obj(d, "shield");
            if (shield != null) { m.ShieldFrom = J.I(shield, "from", 2); m.ShieldTo = J.I(shield, "to", 40); m.ShieldReflect = J.B(shield, "reflect"); }
            var counter = J.Obj(d, "counter");
            if (counter != null) { m.CounterFrom = J.I(counter, "from", 2); m.CounterTo = J.I(counter, "to", 30); m.CounterInto = J.Str(counter, "into"); }
            var tp = J.Obj(d, "teleport");
            if (tp != null) { m.TeleportMode = J.Str(tp, "mode", "behind"); m.TeleportDist = J.F(tp, "dist", 2.5f); m.TeleportFrame = J.I(tp, "frame", m.Startup - 1); }
            var pj = J.Obj(d, "projectile");
            if (pj != null)
            {
                m.Projectile = new ProjectileDef
                {
                    Vfx = J.Str(pj, "vfx", "ki_blast"),
                    Speed = J.F(pj, "speed", 10f),
                    W = J.F(pj, "w", 0.6f),
                    H = J.F(pj, "h", 0.6f),
                    X = J.F(pj, "x", 0.8f),
                    Y = J.F(pj, "y", 1.3f),
                    VY = J.F(pj, "vy", 0f),
                    Gravity = J.F(pj, "gravity", 0f),
                    Scale = J.F(pj, "scale", 1f),
                    Life = J.I(pj, "life", 120),
                    Count = Math.Max(1, J.I(pj, "count", 1)),
                    Interval = J.I(pj, "interval", 8),
                    Hits = Math.Max(1, J.I(pj, "hits", 1)),
                    HitInterval = J.I(pj, "hitInterval", 6),
                    Damage = J.F(pj, "damage", 30f),
                    KbX = J.F(pj, "kbX", 2f),
                    KbY = J.F(pj, "kbY", 0f),
                    Hitstun = J.I(pj, "hitstun", 14),
                    Blockstun = J.I(pj, "blockstun", 10),
                    Hitstop = J.I(pj, "hitstop", 5),
                    Ground = J.B(pj, "ground"),
                    Pierce = J.B(pj, "pierce"),
                    Launch = J.B(pj, "launch"),
                    Knockdown = J.B(pj, "knockdown"),
                    SpreadY = J.F(pj, "spreadY"),
                    Strength = J.F(pj, "strength", 1f),
                    Height = ParseHeight(J.Str(pj, "height", "mid")),
                };
                if (m.ProjectileFrame < 0) m.ProjectileFrame = m.Startup;
            }
            return m;
        }

        static MoveHeight ParseHeight(string s)
        {
            switch (s)
            {
                case "low": return MoveHeight.Low;
                case "overhead": return MoveHeight.Overhead;
                case "unblockable": return MoveHeight.Unblockable;
                case "throw": return MoveHeight.Throw;
                default: return MoveHeight.Mid;
            }
        }
    }

    public sealed class ArenaDef
    {
        public string Id, Name, Particles, Music, Tint;
        public float FloorY;
        public bool BossOnly;
    }

    /// <summary>Global battle rules (also from the grimoire so they can be tuned without a rebuild).</summary>
    public sealed class Rules
    {
        public int RoundSeconds = 99, RoundsToWin = 2, MaxRounds = 5;
        public float StageHalfWidth = 9f, MaxSeparation = 11f, Gravity = 40f;
        public float KiMax = 100f, KiOnDeal = 0.10f, KiOnTake = 0.07f, KiOnBlockDeal = 2f, KiOnBlockTake = 1f;
        public float RageThreshold = 0.25f, RageDamage = 1.12f, RageUltCost = 50f;
        public int BufferFrames = 8, ThrowBreakWindow = 14;
        public int[] OrbTimes = { 70, 35 };    // round-clock seconds at which a Ki orb spawns
        public float OrbKi = 30f;
        public float PushScale = 1.4f;        // pushbox width relative to the hurtbox width (bodies never sink into each other)

        public static Rules FromJson(Dictionary<string, object> d)
        {
            var r = new Rules
            {
                RoundSeconds = J.I(d, "roundSeconds", 99),
                RoundsToWin = J.I(d, "roundsToWin", 2),
                MaxRounds = J.I(d, "maxRounds", 5),
                StageHalfWidth = J.F(d, "stageHalfWidth", 9f),
                MaxSeparation = J.F(d, "maxSeparation", 11f),
                Gravity = J.F(d, "gravity", 40f),
                KiMax = J.F(d, "kiMax", 100f),
                KiOnDeal = J.F(d, "kiOnDeal", 0.10f),
                KiOnTake = J.F(d, "kiOnTake", 0.07f),
                RageThreshold = J.F(d, "rageThreshold", 0.25f),
                RageDamage = J.F(d, "rageDamage", 1.12f),
                RageUltCost = J.F(d, "rageUltCost", 50f),
                BufferFrames = J.I(d, "bufferFrames", 8),
                ThrowBreakWindow = J.I(d, "throwBreakWindow", 14),
                OrbKi = J.F(d, "orbKi", 30f),
                PushScale = J.F(d, "pushScale", 1.4f),
            };
            var ot = J.Floats(d, "orbTimes");
            if (ot != null) { r.OrbTimes = new int[ot.Length]; for (int i = 0; i < ot.Length; i++) r.OrbTimes[i] = (int)ot[i]; }
            return r;
        }
    }
}
