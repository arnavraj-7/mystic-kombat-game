using System;
using System.Collections.Generic;

namespace MysticKombat.Core
{
    public enum Ev
    {
        RoundIntro, Fight, Hit, Block, ShieldBlock, Armor, Whiff, MoveStart, ProjectileSpawn, Clash,
        Jump, Land, Knockdown, KO, RoundOver, MatchOver, TimeUp, UltStart, UltConnect, Heal, Buff,
        Counter, Throw, ThrowBreak, OrbSpawn, OrbTaken, Teleport, Rage, Reflect, ChargeReady
    }

    public struct SimEvent
    {
        public Ev Type;
        public int Who;          // fighter index the event belongs to (attacker for hits)
        public float X, Y, Value;
        public string Str;       // move id / vfx / sfx
        public bool Heavy;
        public int Combo;
    }

    public enum Phase { Intro, Fight, RoundEnd, MatchOver }

    /// <summary>
    /// Deterministic 60 Hz fighting simulation. Pure C# (no Unity) so it runs identically in the game,
    /// in lockstep LAN play, and in the headless balance harness. Inputs in, state + events out.
    /// </summary>
    public sealed class BattleSim
    {
        public const float Dt = 1f / 60f;
        public const int IntroFrames = 110, RoundEndFrames = 170, SlowmoFrames = 70;

        public readonly Grimoire G;
        public readonly Rules R;
        public readonly Fighter[] F = new Fighter[2];
        public readonly List<Projectile> Projectiles = new List<Projectile>();
        public readonly List<KiOrb> Orbs = new List<KiOrb>();
        public readonly List<SimEvent> Events = new List<SimEvent>();

        public Phase Phase { get; private set; } = Phase.Intro;
        public int PhaseFrame { get; private set; }
        public int Round { get; private set; }
        public int TimerFrames { get; private set; }
        public int Frame { get; private set; }
        public int Freeze { get; private set; }          // super-freeze while an ultimate starts
        public int Winner { get; private set; } = -1;     // match winner, -1 none, 2 draw
        public int RoundWinner { get; private set; } = -1;
        public bool Perfect { get; private set; }
        public bool TrainingMode;                         // infinite hp/ki refill + no timer
        public int ActiveUltOwner = -1;                   // set while an ultimate is in flight (for cinematics)
        int nextProjId;
        int orbIndex;

        public BattleSim(Grimoire g, string p1, string p2)
        {
            G = g;
            R = g.Rules;
            F[0] = new Fighter(0, g.Get(p1) ?? throw new ArgumentException("unknown fighter " + p1));
            F[1] = new Fighter(1, g.Get(p2) ?? throw new ArgumentException("unknown fighter " + p2));
            StartRound();
        }

        void StartRound()
        {
            Round++;
            Phase = Phase.Intro;
            PhaseFrame = 0;
            TimerFrames = R.RoundSeconds * 60;
            Projectiles.Clear();
            Orbs.Clear();
            orbIndex = 0;
            Freeze = 0;
            RoundWinner = -1;
            Perfect = false;
            ActiveUltOwner = -1;
            F[0].ResetForRound(-2.6f, 1);
            F[1].ResetForRound(2.6f, -1);
            Emit(Ev.RoundIntro, -1, 0, 0, Round);
        }

        void Emit(Ev t, int who, float x, float y, float value = 0, string str = null, bool heavy = false, int combo = 0)
            => Events.Add(new SimEvent { Type = t, Who = who, X = x, Y = y, Value = value, Str = str, Heavy = heavy, Combo = combo });

        public bool IsMatchOver => Phase == Phase.MatchOver;

        /// <summary>Advance one frame. Events from this frame are in <see cref="Events"/>.</summary>
        public void Step(Btn p1, Btn p2)
        {
            Events.Clear();
            Frame++;
            if (Phase == Phase.MatchOver) { PhaseFrame++; return; }

            // inputs are always recorded so buffers/motions stay continuous
            F[0].Input.Push(p1);
            F[1].Input.Push(p2);

            if (Freeze > 0) { Freeze--; return; }

            PhaseFrame++;
            switch (Phase)
            {
                case Phase.Intro:
                    if (PhaseFrame >= IntroFrames)
                    {
                        Phase = Phase.Fight;
                        PhaseFrame = 0;
                        F[0].SetState(FState.Idle);
                        F[1].SetState(FState.Idle);
                        Emit(Ev.Fight, -1, 0, 0);
                    }
                    return;
                case Phase.RoundEnd:
                    // KO slow motion: physics runs every other frame for the first part
                    if (PhaseFrame < SlowmoFrames && (PhaseFrame & 1) == 1) return;
                    TickWorld(false);
                    if (PhaseFrame >= RoundEndFrames) EndRound();
                    return;
            }

            // fight
            if (!TrainingMode && TimerFrames > 0)
            {
                TimerFrames--;
                int secs = TimerFrames / 60;
                if (TimerFrames % 60 == 0 && orbIndex < R.OrbTimes.Length && secs == R.OrbTimes[orbIndex])
                {
                    orbIndex++;
                    Orbs.Add(new KiOrb { X = 0, Y = 2.6f });
                    Emit(Ev.OrbSpawn, -1, 0, 2.6f);
                }
                if (TimerFrames == 0) { TimeUp(); return; }
            }
            TickWorld(true);
        }

        void TickWorld(bool fighting)
        {
            for (int i = 0; i < 2; i++) TickFighter(F[i], F[1 - i], fighting);
            ResolvePush();
            for (int i = 0; i < 2; i++) ResolveStrikes(F[i], F[1 - i]);
            TickProjectiles();
            TickOrbs();
            if (fighting) CheckKO();
            if (TrainingMode) TrainingRefill();
        }

        // ------------------------------------------------------------------ fighters

        void TickFighter(Fighter f, Fighter o, bool fighting)
        {
            if (f.Hitstop > 0) { f.Hitstop--; return; }
            f.StateFrame++;
            if (f.Invuln > 0) f.Invuln--;
            TickAbilities(f);

            var inp = f.Input;
            switch (f.State)
            {
                case FState.Intro:
                case FState.Victory:
                    Friction(f, 30f);
                    break;
                case FState.KO:
                    Friction(f, 30f);
                    break;
                case FState.Idle:
                case FState.Walk:
                case FState.Crouch:
                    if (!fighting) { Friction(f, 30f); break; }
                    FaceOpponent(f, o);
                    f.ComboCount = 0; f.JuggleCount = 0; f.ComboDamage = 0; f.JuggleLocked = false; f.ChainCount = 0;
                    if (TryStartMove(f, o, false)) break;
                    int np = InputHistory.Numpad(inp.Current, f.Facing);
                    if (np >= 7)
                    {
                        f.SetState(FState.PreJump);
                        f.VX = 0;
                        f.TargetX = np == 9 ? 1 : np == 7 ? -1 : 0; // store jump direction
                    }
                    else if (np <= 3)
                    {
                        if (f.State != FState.Crouch) f.SetState(FState.Crouch);
                        f.VX = 0;
                    }
                    else if (np == 6)
                    {
                        if (f.State != FState.Walk) f.SetState(FState.Walk);
                        f.VX = f.Facing * f.Def.WalkSpeed * f.BuffSpeed;
                    }
                    else if (np == 4)
                    {
                        if (f.State != FState.Walk) f.SetState(FState.Walk);
                        f.VX = -f.Facing * f.Def.BackSpeed * f.BuffSpeed;
                    }
                    else
                    {
                        if (f.State != FState.Idle) f.SetState(FState.Idle);
                        f.VX = 0;
                    }
                    break;
                case FState.PreJump:
                    if (f.StateFrame >= 4)
                    {
                        f.VY = f.Def.JumpVel;
                        f.VX = f.TargetX * f.Facing * f.Def.AirSpeed * f.BuffSpeed;
                        f.Y = 0.001f;
                        f.SetState(FState.Air);
                        Emit(Ev.Jump, f.Index, f.X, 0);
                    }
                    break;
                case FState.Air:
                    if (fighting) TryStartMove(f, o, true);
                    break;
                case FState.Attack:
                    TickMove(f, o);
                    break;
                case FState.Landing:
                    Friction(f, 40f);
                    if (f.StateFrame >= 5) f.SetState(FState.Idle);
                    break;
                case FState.Hitstun:
                case FState.Blockstun:
                    Friction(f, 22f);
                    if (--f.Stun <= 0)
                    {
                        f.SetState(f.Crouched && f.State == FState.Blockstun ? FState.Crouch : FState.Idle);
                    }
                    break;
                case FState.Tumble:
                    break;
                case FState.Knockdown:
                    Friction(f, 30f);
                    f.Invuln = Math.Max(f.Invuln, 2);
                    if (f.StateFrame >= 38) { f.SetState(FState.GetUp); }
                    break;
                case FState.GetUp:
                    f.Invuln = Math.Max(f.Invuln, 2);
                    if (f.StateFrame >= 16) { f.SetState(FState.Idle); f.Invuln = 4; }
                    break;
                case FState.Thrown:
                    TickThrown(f, o);
                    break;
            }

            Integrate(f);
        }

        void TickAbilities(Fighter f)
        {
            for (int i = 0; i < 3; i++)
            {
                var m = f.Def.Move(Fighter.SpecialSlots[i]);
                if (m == null || m.Recharge <= 0) continue;
                if (f.Charges[i] < Math.Max(1, m.Charges))
                {
                    if (++f.RechargeTimer[i] >= m.Recharge)
                    {
                        f.RechargeTimer[i] = 0;
                        f.Charges[i]++;
                        Emit(Ev.ChargeReady, f.Index, f.X, f.Y, i);
                    }
                }
            }
            if (f.BuffTimer > 0 && --f.BuffTimer == 0)
            {
                f.BuffDamage = f.BuffSpeed = 1f;
                f.BuffArmor = false;
            }
            bool raging = f.Raging(R);
            if (raging && !f.UsedRageThisRound)
            {
                f.UsedRageThisRound = true;
                Emit(Ev.Rage, f.Index, f.X, f.Y);
            }
        }

        void Integrate(Fighter f)
        {
            bool wasAir = !f.Grounded;
            f.X += f.VX * Dt;
            if (wasAir || f.VY > 0)
            {
                f.Y += f.VY * Dt;
                f.VY -= R.Gravity * Dt;
                if (f.Y <= 0)
                {
                    f.Y = 0;
                    f.VY = 0;
                    OnLand(f);
                }
            }
            float lim = R.StageHalfWidth - f.Def.Width * 0.5f;
            if (f.X < -lim) { f.X = -lim; if (f.VX < 0) f.VX = 0; }
            if (f.X > lim) { f.X = lim; if (f.VX > 0) f.VX = 0; }
        }

        void OnLand(Fighter f)
        {
            switch (f.State)
            {
                case FState.Air:
                    f.VX = 0;
                    f.SetState(FState.Landing);
                    Emit(Ev.Land, f.Index, f.X, 0);
                    break;
                case FState.Attack:
                    if (f.Move != null && f.Move.Slot.StartsWith("air"))
                    {
                        f.VX = 0;
                        f.SetState(FState.Landing);
                        Emit(Ev.Land, f.Index, f.X, 0);
                    }
                    break;
                case FState.Tumble:
                    f.VX *= 0.3f;
                    if (f.Hp <= 0)
                    {
                        f.SetState(FState.KO);
                        Emit(Ev.Knockdown, f.Index, f.X, 0, 1, null, true);
                    }
                    else
                    {
                        f.SetState(FState.Knockdown);
                        f.Invuln = 40;
                        Emit(Ev.Knockdown, f.Index, f.X, 0);
                    }
                    break;
                case FState.Hitstun:
                case FState.Blockstun:
                    f.VX *= 0.5f;
                    break;
            }
        }

        static void Friction(Fighter f, float amount)
        {
            if (!f.Grounded) return;
            float d = amount * Dt;
            if (Math.Abs(f.VX) <= d) f.VX = 0; else f.VX -= Math.Sign(f.VX) * d;
        }

        static void FaceOpponent(Fighter f, Fighter o)
        {
            if (Math.Abs(o.X - f.X) > 0.05f) f.Facing = o.X > f.X ? 1 : -1;
        }

        // ------------------------------------------------------------------ move selection

        static int Rank(MoveDef m)
        {
            if (m == null) return -1;
            switch (m.Slot)
            {
                case "light": case "crouch_light": case "air_light": return 0;
                case "heavy": case "crouch_heavy": case "air_heavy": return 1;
                case "special1": case "special2": case "special3": return 2;
                case "ultimate": return 3;
                default: return 9;
            }
        }

        bool TryStartMove(Fighter f, Fighter o, bool air)
        {
            var inp = f.Input;
            int buf = R.BufferFrames;
            bool crouching = InputHistory.Numpad(inp.Current, f.Facing) <= 3;

            if (!air)
            {
                // Ultimate: button, or 236236 + heavy
                bool ultInput = inp.Buffered(Btn.Ult, buf) || (inp.Buffered(Btn.Heavy, buf) && inp.Motion("236236", f.Facing, 30));
                if (ultInput && f.Ki >= f.UltCost(R) && f.Def.Move("ultimate") != null)
                {
                    inp.Consume(Btn.Ult); inp.Consume(Btn.Heavy);
                    StartMove(f, o, f.Def.Move("ultimate"));
                    return true;
                }
                // Specials: dedicated buttons or motion inputs (236/214/623 + light)
                for (int i = 0; i < 3; i++)
                {
                    Btn b = i == 0 ? Btn.S1 : i == 1 ? Btn.S2 : Btn.S3;
                    string motion = i == 0 ? "236" : i == 1 ? "214" : "623";
                    bool pressed = inp.Buffered(b, buf);
                    bool viaMotion = !pressed && inp.Buffered(Btn.Light, buf) && inp.Motion(motion, f.Facing, 14);
                    if ((pressed || viaMotion) && f.Charges[i] > 0)
                    {
                        var m = f.Def.Move(Fighter.SpecialSlots[i]);
                        if (m == null) continue;
                        inp.Consume(b);
                        if (viaMotion) inp.Consume(Btn.Light);
                        UseCharge(f, i);
                        StartMove(f, o, m);
                        return true;
                    }
                }
                // Throw (dedicated button or light+heavy together)
                bool throwIn = inp.Buffered(Btn.Throw, 4) || (inp.Buffered(Btn.Light, 3) && inp.Buffered(Btn.Heavy, 3));
                if (throwIn && f.Def.Move("throw") != null)
                {
                    inp.Consume(Btn.Throw); inp.Consume(Btn.Light); inp.Consume(Btn.Heavy);
                    StartMove(f, o, f.Def.Move("throw"));
                    return true;
                }
            }

            if (inp.Buffered(Btn.Heavy, buf))
            {
                string id = air ? "air_heavy" : crouching ? "crouch_heavy" : "heavy";
                var m = f.Def.Move(id) ?? f.Def.Move(air ? "air_light" : "heavy");
                if (m != null) { inp.Consume(Btn.Heavy); StartMove(f, o, m); return true; }
            }
            if (inp.Buffered(Btn.Light, buf))
            {
                // give a simultaneous heavy press a couple of frames to arrive (throw input)
                if (!air && inp.PressAge(Btn.Light) < 2 && f.Def.Move("throw") != null && inp.Held(Btn.Heavy)) return false;
                string id = air ? "air_light" : crouching ? "crouch_light" : "light";
                var m = f.Def.Move(id) ?? f.Def.Move("light");
                if (m != null) { inp.Consume(Btn.Light); StartMove(f, o, m); return true; }
            }
            return false;
        }

        /// <summary>Cancel the current move into a stronger one after it connected (Tekken/SF style strings).</summary>
        bool TryCancel(Fighter f, Fighter o)
        {
            if (!(f.MoveHit || f.MoveBlocked)) return false;
            if (f.MoveFrame - f.ConnectFrame > f.Move.CancelWindow + f.Move.Hitstop) return false;
            int cur = Rank(f.Move);
            if (cur >= 3 || cur == 9) return false;
            var inp = f.Input;
            int buf = R.BufferFrames + 4;

            if (inp.Buffered(Btn.Ult, buf) && f.Ki >= f.UltCost(R) && f.MoveHit && !f.Move.Slot.StartsWith("air"))
            {
                inp.Consume(Btn.Ult);
                StartMove(f, o, f.Def.Move("ultimate"));
                return true;
            }
            if (cur <= 1 && !f.Move.Slot.StartsWith("air"))
            {
                for (int i = 0; i < 3; i++)
                {
                    Btn b = i == 0 ? Btn.S1 : i == 1 ? Btn.S2 : Btn.S3;
                    string motion = i == 0 ? "236" : i == 1 ? "214" : "623";
                    bool pressed = inp.Buffered(b, buf) || (inp.Buffered(Btn.Light, buf) && inp.Motion(motion, f.Facing, 16));
                    if (pressed && f.Charges[i] > 0)
                    {
                        var m = f.Def.Move(Fighter.SpecialSlots[i]);
                        if (m == null) continue;
                        inp.Consume(b); inp.Consume(Btn.Light);
                        UseCharge(f, i);
                        StartMove(f, o, m);
                        return true;
                    }
                }
            }
            if (cur == 0 && inp.Buffered(Btn.Heavy, buf))
            {
                string id = f.Move.Slot.StartsWith("air") ? "air_heavy" : f.Move.Slot.StartsWith("crouch") ? "crouch_heavy" : "heavy";
                var m = f.Def.Move(id);
                if (m != null) { inp.Consume(Btn.Heavy); StartMove(f, o, m); return true; }
            }
            if (cur == 0 && f.ChainCount < 2 && inp.Buffered(Btn.Light, buf) && f.MoveHit)
            {
                var m = f.Move;
                inp.Consume(Btn.Light);
                StartMove(f, o, m);
                f.ChainCount++;
                return true;
            }
            return false;
        }

        void UseCharge(Fighter f, int slot)
        {
            f.Charges[slot]--;
            f.Stats.Specials++;
        }

        void StartMove(Fighter f, Fighter o, MoveDef m)
        {
            int chain = f.State == FState.Attack ? f.ChainCount : 0;
            bool air = !f.Grounded;
            f.SetState(FState.Attack);
            f.ChainCount = chain;
            f.Move = m;
            f.MoveFrame = 0;
            f.HitsDone = 0;
            f.MoveHit = f.MoveBlocked = false;
            f.ConnectFrame = -1000;
            f.ProjectilesSpawned = 0;
            f.TargetX = o.X;
            if (!air) f.VX = 0;
            if (m.Slot == "ultimate")
            {
                f.Ki -= f.UltCost(R);
                if (f.Ki < 0) f.Ki = 0;
                f.Stats.Ults++;
                Freeze = 40;
                ActiveUltOwner = f.Index;
                f.Invuln = Math.Max(f.Invuln, 8);
                Emit(Ev.UltStart, f.Index, f.X, f.Y, 0, m.Id);
            }
            Emit(Ev.MoveStart, f.Index, f.X, f.Y, 0, m.Id, Rank(m) >= 1);
        }

        void TickMove(Fighter f, Fighter o)
        {
            var m = f.Move;
            int mf = f.MoveFrame;
            bool air = m.Slot.StartsWith("air");

            if (TryCancel(f, o)) return;

            // movement
            if (!air)
            {
                if (mf < m.Startup) f.VX = f.Facing * m.MoveStartupX * f.BuffSpeed;
                else if (mf < m.Startup + m.Active) f.VX = f.Facing * m.MoveX * f.BuffSpeed;
                else Friction(f, 45f);
                if (m.MoveY != 0 && mf == m.Startup) { f.VY = m.MoveY; f.Y = 0.001f; }
            }

            if (m.TargetOpponent && mf == Math.Max(0, m.Startup - 12)) f.TargetX = o.X;

            if (m.TeleportMode != null && mf == m.TeleportFrame)
            {
                float lim = R.StageHalfWidth - f.Def.Width;
                float nx;
                if (m.TeleportMode == "behind")
                {
                    int side = o.X >= f.X ? 1 : -1;
                    nx = o.X + side * m.TeleportDist;
                    if (nx > lim || nx < -lim) nx = o.X - side * m.TeleportDist; // wall: appear in front instead
                }
                else nx = f.X + f.Facing * m.TeleportDist;
                nx = Math.Max(-lim, Math.Min(lim, nx));
                Emit(Ev.Teleport, f.Index, f.X, f.Y, nx);
                f.X = nx;
                f.Facing = o.X > f.X ? 1 : -1;
                f.Invuln = Math.Max(f.Invuln, 4);
            }

            if (m.Projectile != null)
            {
                var pd = m.Projectile;
                while (f.ProjectilesSpawned < pd.Count && mf >= m.ProjectileFrame + f.ProjectilesSpawned * pd.Interval)
                {
                    SpawnProjectile(f, m, f.ProjectilesSpawned);
                    f.ProjectilesSpawned++;
                }
            }

            if (m.HealAmount > 0 && mf >= m.HealFrom && mf < m.HealTo)
            {
                float per = m.HealAmount / Math.Max(1, m.HealTo - m.HealFrom);
                float before = f.Hp;
                f.Hp = Math.Min(f.Def.MaxHp, f.Hp + per);
                f.Stats.Healed += f.Hp - before;
                if (mf == m.HealFrom) Emit(Ev.Heal, f.Index, f.X, f.Y, m.HealAmount);
            }

            if (m.BuffDuration > 0 && mf == m.Startup)
            {
                f.BuffDamage = m.BuffDamage;
                f.BuffSpeed = m.BuffSpeed;
                f.BuffArmor = m.BuffArmor;
                f.BuffTimer = m.BuffDuration;
                Emit(Ev.Buff, f.Index, f.X, f.Y, m.BuffDuration, m.Id);
            }

            if (m.InvulnFrom >= 0 && mf >= m.InvulnFrom && mf <= m.InvulnTo) f.Invuln = Math.Max(f.Invuln, 1);

            if (mf == m.Startup && !f.MoveWhiffSoundDone)
                Emit(Ev.Whiff, f.Index, f.X, f.Y, 0, m.Id, Rank(m) >= 1);

            f.MoveFrame++;
            if (f.MoveFrame >= m.TotalFrames)
            {
                if (m.Slot == "ultimate") ActiveUltOwner = -1;
                if (f.Grounded) f.SetState(FState.Idle);
                else { f.SetState(FState.Air); }
            }
        }

        void SpawnProjectile(Fighter f, MoveDef m, int k)
        {
            var pd = m.Projectile;
            float spread = pd.Count > 1 ? (k - (pd.Count - 1) * 0.5f) * pd.SpreadY : 0;
            var p = new Projectile
            {
                Owner = f.Index,
                Def = pd,
                Facing = f.Facing,
                X = f.X + f.Facing * pd.X,
                Y = pd.Ground ? pd.H * 0.5f : f.Y + pd.Y + spread,
                VX = f.Facing * pd.Speed,
                VY = pd.VY,
                Life = pd.Life,
                Power = f.Def.Power * f.BuffDamage * (f.Raging(R) ? R.RageDamage : 1f),
                Ultimate = m.Slot == "ultimate",
                MoveId = m.Id,
                Id = ++nextProjId,
            };
            Projectiles.Add(p);
            f.Stats.Projectiles++;
            Emit(Ev.ProjectileSpawn, f.Index, p.X, p.Y, 0, pd.Vfx);
        }

        // ------------------------------------------------------------------ collisions

        struct Box { public float X0, Y0, X1, Y1; }

        static bool Overlap(Box a, Box b) => a.X0 < b.X1 && b.X0 < a.X1 && a.Y0 < b.Y1 && b.Y0 < a.Y1;

        static Box Hurt(Fighter f)
        {
            float hw = f.Def.Width * 0.5f;
            return new Box { X0 = f.X - hw, X1 = f.X + hw, Y0 = f.Y, Y1 = f.Y + f.HurtHeight };
        }

        public static void HitboxWorld(Fighter f, out float cx, out float cy, out float w, out float h)
        {
            var hb = f.Move.Hitbox;
            cx = f.Move.TargetOpponent ? f.TargetX : f.X + f.Facing * hb[0];
            cy = f.Y + hb[1];
            w = hb[2];
            h = hb[3];
        }

        public bool HitboxActive(Fighter f)
        {
            var m = f.Move;
            if (f.State != FState.Attack || m == null || m.Kind == "none") return false;
            if (m.Projectile != null && m.Kind == "projectile") return false;
            int mf = f.MoveFrame - 1; // MoveFrame already advanced this tick
            if (mf < m.Startup || mf >= m.Startup + m.Active) return false;
            if (f.HitsDone >= m.Hits) return false;
            return mf >= m.Startup + f.HitsDone * m.HitInterval;
        }

        public static void HurtWorld(Fighter f, out float x0, out float y0, out float x1, out float y1)
        {
            var b = Hurt(f);
            x0 = b.X0; y0 = b.Y0; x1 = b.X1; y1 = b.Y1;
        }

        void ResolveStrikes(Fighter a, Fighter v)
        {
            if (a.Hitstop > 0 || !HitboxActive(a)) return;
            if (v.State == FState.KO && v.Grounded) return;
            HitboxWorld(a, out float cx, out float cy, out float w, out float h);
            var hb = new Box { X0 = cx - w * 0.5f, X1 = cx + w * 0.5f, Y0 = cy - h * 0.5f, Y1 = cy + h * 0.5f };
            if (!Overlap(hb, Hurt(v))) return;
            var m = a.Move;

            if (m.Height == MoveHeight.Throw)
            {
                TryThrow(a, v);
                a.HitsDone = m.Hits;
                return;
            }

            a.HitsDone++;
            var hp = new HitParams
            {
                Damage = m.Damage, Chip = m.Chip, Hitstun = m.Hitstun, Blockstun = m.Blockstun, Hitstop = m.Hitstop,
                KbX = m.KbX, KbY = m.KbY, Height = m.Height, Launch = m.Launch, Knockdown = m.Knockdown,
                PullX = m.PullX, Ultimate = m.Slot == "ultimate", Special = Rank(m) == 2, MoveId = m.Id,
                Power = a.Def.Power * a.BuffDamage * (a.Raging(R) ? R.RageDamage : 1f),
                Dir = v.X >= a.X ? 1 : -1, SourceX = a.X, Heavy = Rank(m) >= 1,
                LastHit = a.HitsDone >= m.Hits,
            };
            var result = ApplyHit(a, v, hp, false);
            if (result != HitResult.None && result != HitResult.Whiff)
            {
                a.ConnectFrame = a.MoveFrame;
                if (result == HitResult.Hit || result == HitResult.Armored) a.MoveHit = true; else a.MoveBlocked = true;
                if (result == HitResult.Hit && hp.Ultimate && a.HitsDone == 1)
                {
                    a.Stats.UltHits++;
                    Emit(Ev.UltConnect, a.Index, v.X, v.Y + 1f, 0, m.Id);
                }
            }
        }

        enum HitResult { None, Whiff, Hit, Blocked, Shielded, Armored, Countered }

        struct HitParams
        {
            public float Damage, Chip, KbX, KbY, PullX, Power, SourceX;
            public int Hitstun, Blockstun, Hitstop, Dir;
            public MoveHeight Height;
            public bool Launch, Knockdown, Ultimate, Special, Heavy, LastHit;
            public string MoveId;
        }

        bool IsBlocking(Fighter v, HitParams p)
        {
            if (!v.Grounded) return false;
            if (!(v.State == FState.Idle || v.State == FState.Walk || v.State == FState.Crouch || v.State == FState.Blockstun || v.State == FState.Landing && v.StateFrame > 2))
                return false;
            if (p.Height == MoveHeight.Unblockable) return false;
            int np = InputHistory.Numpad(v.Input.Current, p.SourceX > v.X ? 1 : -1);
            bool holdBack = np == 4 || np == 1 || np == 7;
            if (!holdBack) return false;
            bool crouch = np == 1;
            if (p.Height == MoveHeight.Low && !crouch) return false;
            if (p.Height == MoveHeight.Overhead && crouch) return false;
            v.Crouched = crouch;
            return true;
        }

        HitResult ApplyHit(Fighter a, Fighter v, HitParams p, bool projectile)
        {
            if (v.Invuln > 0 || v.JuggleLocked || v.State == FState.Knockdown || v.State == FState.GetUp) return HitResult.Whiff;
            if (v.State == FState.KO) return HitResult.Whiff;

            // shield moves block everything during their window
            if (v.State == FState.Attack && v.Move != null && v.Move.ShieldFrom >= 0
                && v.MoveFrame >= v.Move.ShieldFrom && v.MoveFrame <= v.Move.ShieldTo && !p.Ultimate)
            {
                a.Hitstop = v.Hitstop = 4;
                if (!projectile) a.VX = -p.Dir * 3f;
                Emit(Ev.ShieldBlock, v.Index, v.X + p.Dir * -0.6f, v.Y + 1.2f, 0, v.Move.Id);
                return HitResult.Shielded;
            }

            // counter stances retaliate against strikes
            if (!projectile && v.State == FState.Attack && v.Move != null && v.Move.CounterFrom >= 0
                && v.MoveFrame >= v.Move.CounterFrom && v.MoveFrame <= v.Move.CounterTo && !p.Ultimate)
            {
                var into = v.Def.Move(v.Move.CounterInto);
                v.Stats.Counters++;
                Emit(Ev.Counter, v.Index, v.X, v.Y + 1.2f, 0, v.Move.Id);
                a.Hitstop = 16;
                a.SetState(FState.Hitstun);
                a.Stun = 28;
                a.VX = 0;
                if (into != null)
                {
                    v.Facing = a.X > v.X ? 1 : -1;
                    StartMove(v, a, into);
                    v.Invuln = 10;
                }
                return HitResult.Countered;
            }

            float power = p.Power;
            if (IsBlocking(v, p))
            {
                float chip = p.Chip > 0 ? p.Chip : p.Ultimate ? p.Damage * 0.15f : p.Special ? p.Damage * 0.1f : 0f;
                chip *= power * v.Def.Defense;
                if (v.Hp - chip < 1 && !p.Ultimate) chip = Math.Max(0, v.Hp - 1); // chip can't kill except ultimates
                v.Hp -= chip;
                bool wasBlock = v.State == FState.Blockstun;
                v.SetState(FState.Blockstun);
                v.Blocking = true;
                v.Stun = p.Blockstun;
                v.VX = p.Dir * (2f + p.KbX * 0.6f);
                v.LastBlockFrame = Frame;
                a.Hitstop = v.Hitstop = Math.Max(3, p.Hitstop - 2);
                if (!projectile && Math.Abs(v.X) > R.StageHalfWidth - 1f) a.VX = -p.Dir * 2.5f;
                GainKi(v, R.KiOnBlockTake);
                GainKi(a, R.KiOnBlockDeal);
                v.Stats.Blocks++;
                Emit(Ev.Block, a.Index, v.X - p.Dir * 0.4f, v.Y + 1.2f, chip, p.MoveId, p.Heavy);
                return HitResult.Blocked;
            }

            // armour (move armour frames or buff armour): take damage without flinching
            bool armored = (v.State == FState.Attack && v.Move != null && v.Move.ArmorFrom >= 0 && v.MoveFrame >= v.Move.ArmorFrom && v.MoveFrame <= v.Move.ArmorTo)
                           || (v.BuffArmor && !p.Heavy && !p.Ultimate);

            int combo = v.ComboCount + 1;
            float scale = combo <= 2 ? 1f : Math.Max(0.35f, 1f - 0.12f * (combo - 2));
            if (p.Ultimate) scale = Math.Max(scale, 0.6f);
            float dmg = p.Damage * power * v.Def.Defense * scale;
            if (armored) dmg *= 0.8f;
            v.Hp -= dmg;
            a.Stats.DamageDealt += dmg;
            a.Stats.Hits++;
            v.ComboCount = combo;
            v.ComboDamage += dmg;
            if (combo > a.Stats.MaxCombo) a.Stats.MaxCombo = combo;
            if (!p.Ultimate) GainKi(a, dmg * R.KiOnDeal * a.Def.KiGain);
            GainKi(v, dmg * R.KiOnTake * v.Def.KiGain);
            a.Hitstop = v.Hitstop = p.Hitstop;

            if (armored && v.Hp > 0)
            {
                Emit(Ev.Armor, a.Index, v.X - p.Dir * 0.3f, v.Y + 1.2f, dmg, p.MoveId, p.Heavy);
                return HitResult.Armored;
            }

            // reaction
            int hitstun = (int)Math.Round(p.Hitstun * Math.Max(0.5f, 1f - 0.04f * (combo - 1)));
            float kbx = p.KbX / Math.Max(0.5f, v.Def.Weight);
            bool airborne = !v.Grounded || v.State == FState.Tumble;
            if (p.PullX != 0)
            {
                v.VX = -p.Dir * p.PullX;
            }
            else v.VX = p.Dir * kbx * 2f;

            if (airborne || p.Launch || p.KbY > 0 || p.Knockdown || v.Hp <= 0)
            {
                v.JuggleCount++;
                v.SetState(FState.Tumble);
                float vy = p.Launch ? 15f : p.KbY > 0 ? p.KbY : airborne ? 7f : 5f;
                if (v.Hp <= 0) { vy = Math.Max(vy, 9f); v.VX = p.Dir * Math.Max(4f, kbx * 2.4f); }
                v.VY = vy / Math.Max(0.6f, (float)Math.Sqrt(v.Def.Weight));
                if (v.Y <= 0) v.Y = 0.01f;
                if (v.JuggleCount > 4) v.JuggleLocked = true; // anti-infinite: no more juggling this combo
            }
            else
            {
                v.SetState(FState.Hitstun);
                v.Stun = hitstun;
            }
            // pushback for the attacker when the victim is cornered
            if (!projectile && Math.Abs(v.X) > R.StageHalfWidth - 1f) a.VX = -p.Dir * 3f;

            Emit(Ev.Hit, a.Index, v.X - p.Dir * 0.3f, v.Y + Math.Min(1.4f, v.HurtHeight * 0.65f), dmg, p.MoveId, p.Heavy || p.Ultimate, combo);
            return HitResult.Hit;
        }

        void GainKi(Fighter f, float amount)
        {
            f.Ki = Math.Min(R.KiMax, f.Ki + amount);
        }

        // ------------------------------------------------------------------ throws (Tekken-style, breakable)

        void TryThrow(Fighter a, Fighter v)
        {
            bool throwable = v.Grounded && v.Invuln <= 0 && !v.IsCrouching
                             && v.State != FState.Hitstun && v.State != FState.Blockstun && v.State != FState.Knockdown
                             && v.State != FState.GetUp && v.State != FState.KO && v.State != FState.Thrown;
            if (!throwable) return;
            v.SetState(FState.Thrown);
            v.ThrownBy = a.Index;
            v.ThrowTimer = 0;
            v.VX = 0;
            a.VX = 0;
            a.MoveHit = true;
            a.ConnectFrame = a.MoveFrame;
            Emit(Ev.Throw, a.Index, v.X, v.Y + 1.2f, 0, a.Move?.Id);
        }

        void TickThrown(Fighter v, Fighter a)
        {
            v.ThrowTimer++;
            // break: press throw (or light+heavy) inside the window
            var inp = v.Input;
            bool breakIn = inp.Buffered(Btn.Throw, 3) || (inp.Buffered(Btn.Light, 3) && inp.Buffered(Btn.Heavy, 3));
            if (v.ThrowTimer <= R.ThrowBreakWindow && breakIn)
            {
                inp.Consume(Btn.Throw); inp.Consume(Btn.Light); inp.Consume(Btn.Heavy);
                int dir = v.X >= a.X ? 1 : -1;
                v.SetState(FState.Blockstun); v.Stun = 14; v.VX = dir * 6f;
                a.SetState(FState.Blockstun); a.Stun = 14; a.VX = -dir * 6f;
                v.Stats.ThrowBreaks++;
                Emit(Ev.ThrowBreak, v.Index, (v.X + a.X) * 0.5f, 1.3f);
                return;
            }
            if (v.ThrowTimer > R.ThrowBreakWindow)
            {
                var m = a.Def.Move("throw");
                int dir = v.X >= a.X ? 1 : -1;
                // the thrower tosses the victim over their shoulder: swap sides
                v.X = Math.Max(-R.StageHalfWidth + 0.5f, Math.Min(R.StageHalfWidth - 0.5f, a.X - dir * 1.2f));
                var p = new HitParams
                {
                    Damage = m?.Damage ?? 90, Hitstun = 20, Hitstop = 10, KbX = 3f, KbY = 9f, Knockdown = true,
                    Height = MoveHeight.Unblockable, Power = a.Def.Power * a.BuffDamage, Dir = -dir, SourceX = a.X, Heavy = true, MoveId = "throw",
                };
                v.State = FState.Idle; // so ApplyHit treats it as a normal grounded hit
                ApplyHit(a, v, p, false);
                a.Stats.Throws++;
            }
        }

        // ------------------------------------------------------------------ projectiles

        void TickProjectiles()
        {
            for (int i = 0; i < Projectiles.Count; i++)
            {
                var p = Projectiles[i];
                if (p.Dead) continue;
                p.X += p.VX * Dt;
                p.Y += p.VY * Dt;
                p.VY -= p.Def.Gravity * Dt;
                if (p.Def.Gravity > 0 && p.Y <= p.Def.H * 0.5f) { p.Y = p.Def.H * 0.5f; p.VY = 0; p.Life = Math.Min(p.Life, 2); }
                if (--p.Life <= 0 || Math.Abs(p.X) > R.StageHalfWidth + 2f) { p.Dead = true; continue; }
                if (p.HitTimer > 0) { p.HitTimer--; continue; }

                var pb = new Box { X0 = p.X - p.Def.W * 0.5f, X1 = p.X + p.Def.W * 0.5f, Y0 = p.Y - p.Def.H * 0.5f, Y1 = p.Y + p.Def.H * 0.5f };

                // clash with enemy projectiles
                for (int j = 0; j < Projectiles.Count; j++)
                {
                    var q = Projectiles[j];
                    if (q.Dead || q.Owner == p.Owner) continue;
                    var qb = new Box { X0 = q.X - q.Def.W * 0.5f, X1 = q.X + q.Def.W * 0.5f, Y0 = q.Y - q.Def.H * 0.5f, Y1 = q.Y + q.Def.H * 0.5f };
                    if (!Overlap(pb, qb)) continue;
                    float sp = p.Def.Strength + (p.Ultimate ? 10 : 0), sq = q.Def.Strength + (q.Ultimate ? 10 : 0);
                    if (sp <= sq) p.Dead = true;
                    if (sq <= sp) q.Dead = true;
                    Emit(Ev.Clash, p.Owner, (p.X + q.X) * 0.5f, (p.Y + q.Y) * 0.5f);
                    break;
                }
                if (p.Dead) continue;

                var v = F[1 - p.Owner];
                var a = F[p.Owner];
                if (!Overlap(pb, Hurt(v))) continue;
                var hp = new HitParams
                {
                    Damage = p.Def.Damage, Hitstun = p.Def.Hitstun, Blockstun = p.Def.Blockstun, Hitstop = p.Def.Hitstop,
                    KbX = p.Def.KbX, KbY = p.Def.KbY, Height = p.Def.Height, Launch = p.Def.Launch, Knockdown = p.Def.Knockdown,
                    Ultimate = p.Ultimate, Special = !p.Ultimate, Power = p.Power, Dir = p.VX >= 0 ? 1 : -1, SourceX = p.X - p.VX,
                    Heavy = p.Def.Damage >= 50 || p.Ultimate, MoveId = p.MoveId,
                };
                // shield reflect
                if (v.State == FState.Attack && v.Move != null && v.Move.ShieldReflect && v.MoveFrame >= v.Move.ShieldFrom && v.MoveFrame <= v.Move.ShieldTo && !p.Ultimate)
                {
                    p.Owner = v.Index;
                    p.VX = -p.VX;
                    p.Facing = -p.Facing;
                    p.Power = v.Def.Power;
                    Emit(Ev.Reflect, v.Index, p.X, p.Y);
                    continue;
                }
                var res = ApplyHit(a, v, hp, true);
                if (res == HitResult.Whiff) continue;
                if (res == HitResult.Hit && p.Ultimate && p.HitsDone == 0)
                {
                    a.Stats.UltHits++;
                    Emit(Ev.UltConnect, a.Index, v.X, v.Y + 1f, 0, p.MoveId);
                }
                p.HitsDone++;
                p.HitTimer = p.Def.HitInterval;
                if (p.HitsDone >= p.Def.Hits && !p.Def.Pierce) p.Dead = true;
                if (res == HitResult.Shielded || res == HitResult.Blocked) { if (!p.Def.Pierce) p.Dead = true; }
            }
            Projectiles.RemoveAll(p => p.Dead);
        }

        void TickOrbs()
        {
            for (int i = 0; i < Orbs.Count; i++)
            {
                var o = Orbs[i];
                if (o.Taken) continue;
                o.Age++;
                if (o.Y > 1.1f) o.Y -= 0.02f; // drifts down to reachable height
                foreach (var f in F)
                {
                    if (f.State == FState.KO || f.State == FState.Knockdown) continue;
                    if (Math.Abs(f.X - o.X) < 0.6f + f.Def.Width * 0.5f && o.Y > f.Y - 0.3f && o.Y < f.Y + f.HurtHeight + 0.3f)
                    {
                        o.Taken = true;
                        GainKi(f, R.OrbKi);
                        f.Stats.OrbsTaken++;
                        f.Stats.KiFromOrbs += R.OrbKi;
                        Emit(Ev.OrbTaken, f.Index, o.X, o.Y, R.OrbKi);
                        break;
                    }
                }
            }
            Orbs.RemoveAll(o => o.Taken);
        }

        void ResolvePush()
        {
            var a = F[0];
            var b = F[1];
            if (a.State == FState.KO || b.State == FState.KO || a.State == FState.Thrown || b.State == FState.Thrown) return;
            float minDist = (a.Def.Width + b.Def.Width) * 0.5f;
            float dx = b.X - a.X;
            bool vertical = a.Y < b.Y + b.HurtHeight * 0.8f && b.Y < a.Y + a.HurtHeight * 0.8f;
            if (vertical && Math.Abs(dx) < minDist)
            {
                float push = (minDist - Math.Abs(dx)) * 0.5f;
                int s = dx > 0 ? 1 : dx < 0 ? -1 : (a.Facing > 0 ? 1 : -1);
                a.X -= s * push;
                b.X += s * push;
                float lim = R.StageHalfWidth - 0.4f;
                if (a.X < -lim) { b.X += -lim - a.X; a.X = -lim; }
                if (a.X > lim) { b.X -= a.X - lim; a.X = lim; }
                if (b.X < -lim) { a.X += -lim - b.X; b.X = -lim; }
                if (b.X > lim) { a.X -= b.X - lim; b.X = lim; }
            }
            // camera leash: fighters can't get further apart than the screen
            dx = b.X - a.X;
            if (Math.Abs(dx) > R.MaxSeparation)
            {
                float over = (Math.Abs(dx) - R.MaxSeparation) * 0.5f;
                int s = dx > 0 ? 1 : -1;
                a.X += s * over;
                b.X -= s * over;
            }
        }

        // ------------------------------------------------------------------ rounds

        void CheckKO()
        {
            bool k0 = F[0].Hp <= 0, k1 = F[1].Hp <= 0;
            if (!k0 && !k1) return;
            for (int i = 0; i < 2; i++) if (F[i].Hp <= 0) F[i].Hp = 0;
            Phase = Phase.RoundEnd;
            PhaseFrame = 0;
            ActiveUltOwner = -1;
            if (k0 && k1) RoundWinner = 2;
            else
            {
                RoundWinner = k0 ? 1 : 0;
                F[RoundWinner].RoundWins++;
                Perfect = F[RoundWinner].Hp >= F[RoundWinner].Def.MaxHp - 0.5f;
            }
            for (int i = 0; i < 2; i++)
                if (F[i].Hp <= 0 && F[i].State != FState.Tumble && F[i].State != FState.KO)
                {
                    F[i].SetState(FState.Tumble);
                    F[i].VY = 8f;
                    F[i].Y = Math.Max(F[i].Y, 0.01f);
                }
            Emit(Ev.KO, RoundWinner, 0, 0, Perfect ? 1 : 0);
        }

        void TimeUp()
        {
            Phase = Phase.RoundEnd;
            PhaseFrame = SlowmoFrames; // no slow motion on time-up
            float a = F[0].HpFrac, b = F[1].HpFrac;
            if (Math.Abs(a - b) < 0.0001f) RoundWinner = 2;
            else
            {
                RoundWinner = a > b ? 0 : 1;
                F[RoundWinner].RoundWins++;
            }
            Emit(Ev.TimeUp, RoundWinner, 0, 0);
        }

        void EndRound()
        {
            int w = RoundWinner;
            Emit(Ev.RoundOver, w, 0, 0);
            bool over = F[0].RoundWins >= R.RoundsToWin || F[1].RoundWins >= R.RoundsToWin || Round >= R.MaxRounds;
            if (over)
            {
                Phase = Phase.MatchOver;
                PhaseFrame = 0;
                Winner = F[0].RoundWins > F[1].RoundWins ? 0 : F[1].RoundWins > F[0].RoundWins ? 1 : 2;
                if (Winner < 2) F[Winner].SetState(FState.Victory);
                Emit(Ev.MatchOver, Winner, 0, 0);
                return;
            }
            StartRound();
        }

        void TrainingRefill()
        {
            foreach (var f in F)
            {
                f.Ki = R.KiMax;
                if (f.Actionable && f.StateFrame > 40 && f.Hp < f.Def.MaxHp) f.Hp = f.Def.MaxHp;
                for (int i = 0; i < 3; i++) if (f.Charges[i] == 0 && f.Actionable) f.Charges[i] = f.MaxCharges(i);
            }
        }

        /// <summary>Hash of the deterministic state, used for LAN desync detection.</summary>
        public uint Checksum()
        {
            uint h = 2166136261;
            void Mix(int v) { unchecked { h = (h ^ (uint)v) * 16777619; } }
            void MixF(float v) => Mix(BitConverter.SingleToInt32Bits(v));
            Mix(Frame); Mix(Round); Mix(TimerFrames); Mix((int)Phase);
            foreach (var f in F)
            {
                MixF(f.X); MixF(f.Y); MixF(f.VX); MixF(f.VY); MixF(f.Hp); MixF(f.Ki);
                Mix((int)f.State); Mix(f.StateFrame); Mix(f.MoveFrame); Mix(f.Facing);
                Mix(f.Charges[0]); Mix(f.Charges[1]); Mix(f.Charges[2]); Mix(f.RoundWins);
            }
            Mix(Projectiles.Count);
            foreach (var p in Projectiles) { MixF(p.X); MixF(p.Y); }
            return h;
        }
    }
}
