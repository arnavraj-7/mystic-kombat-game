using System;

namespace MysticKombat.Core
{
    /// <summary>Small deterministic RNG (xorshift32) so CPU matches are reproducible from a seed.</summary>
    public sealed class Rng
    {
        uint s;
        public Rng(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }
        public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        public float Value() => (Next() & 0xFFFFFF) / (float)0x1000000;
        public bool Chance(float p) => Value() < p;
        public int Range(int min, int maxExclusive) => min + (int)(Next() % (uint)Math.Max(1, maxExclusive - min));
    }

    /// <summary>
    /// Watches the opponent and counts habits. The CPU leans on these counts (more at higher
    /// difficulty) to counter predictable players: anti-airing jumpers, low-blocking low spammers,
    /// throwing turtles and closing in on zoners.
    /// </summary>
    public sealed class HabitTracker
    {
        public float Jumps, Lows, Mids, Projectiles, Throws, StandBlocks, CrouchBlocks, Approaches, Samples;
        FState lastState;
        MoveDef lastMove;

        public void Observe(Fighter op, Fighter me)
        {
            if (op.State == FState.PreJump && lastState != FState.PreJump) Jumps++;
            if (op.State == FState.Attack && op.Move != lastMove && op.Move != null)
            {
                if (op.Move.Height == MoveHeight.Low) Lows++; else Mids++;
                if (op.Move.Projectile != null) Projectiles++;
                if (op.Move.Height == MoveHeight.Throw) Throws++;
            }
            if (op.State == FState.Blockstun && lastState != FState.Blockstun)
            {
                if (op.Crouched) CrouchBlocks++; else StandBlocks++;
            }
            if (op.State == FState.Walk && Math.Sign(op.VX) == Math.Sign(me.X - op.X)) Approaches += 0.02f;
            lastState = op.State;
            lastMove = op.Move;
            Samples += 1f / 60f;
        }

        public float JumpRate => Jumps / Math.Max(10f, Samples) * 10f;          // jumps per 10 s
        public float LowRatio => Lows / Math.Max(4f, Lows + Mids);
        public float ZoneRate => Projectiles / Math.Max(10f, Samples) * 10f;
        public float BlockRate => (StandBlocks + CrouchBlocks) / Math.Max(10f, Samples) * 10f;
        public float StandBlockRatio => StandBlocks / Math.Max(3f, StandBlocks + CrouchBlocks);
    }

    /// <summary>
    /// Finite-state-machine CPU opponent. States: Neutral, Approach, Retreat, Pressure, Defend, Punish,
    /// Zone, Recover. Four difficulty levels differ in reaction time, block/anti-air/combo
    /// reliability and how much they adapt to the opponent's habits.
    /// </summary>
    public sealed class CpuBrain
    {
        public static readonly string[] LevelNames = { "Easy", "Normal", "Hard", "Mystic" };
        static readonly int[] Reaction = { 24, 15, 9, 5 };
        static readonly float[] BlockP = { 0.18f, 0.45f, 0.74f, 0.9f };
        static readonly float[] AntiAirP = { 0.08f, 0.3f, 0.62f, 0.85f };
        static readonly float[] ComboP = { 0.2f, 0.55f, 0.85f, 0.97f };
        static readonly float[] Aggro = { 0.35f, 0.5f, 0.62f, 0.72f };
        static readonly float[] SpecialP = { 0.2f, 0.4f, 0.6f, 0.75f };
        static readonly float[] BreakP = { 0.08f, 0.3f, 0.6f, 0.8f };
        static readonly float[] Mistake = { 0.22f, 0.1f, 0.04f, 0.01f };
        static readonly float[] Adapt = { 0f, 0.3f, 0.65f, 1f };

        public enum Mode { Neutral, Approach, Retreat, Pressure, Defend, Punish, Zone, Recover }

        public readonly int Index;
        public readonly int Level;
        public Mode State { get; private set; } = Mode.Neutral;
        public readonly HabitTracker Habits = new HabitTracker();
        readonly Rng rng;

        Btn lastOut;
        Btn holdDir;
        int holdTimer, modeTimer, idleTimer, decisionCooldown;
        int threatId = -1, threatDecideAt = -1, threatSpecial = -1, lastProjThreat;
        bool threatBlock, threatLow;
        int comboMoveSeen = -1;
        int throwDecided = -1;
        bool throwBreak;
        int pendingPressAt = -1;
        Btn pendingPress;

        public CpuBrain(int index, int level, uint seed)
        {
            Index = index;
            Level = Math.Max(0, Math.Min(3, level));
            rng = new Rng(seed * 2654435761u + (uint)index * 97u + 1u);
        }

        public Btn Think(BattleSim sim)
        {
            var me = sim.F[Index];
            var op = sim.F[1 - Index];
            Habits.Observe(op, me);
            Btn outB = Decide(sim, me, op);
            // buttons are edge-triggered: never hold an attack button two frames in a row
            Btn attacks = outB & Btn.Attacks;
            Btn repeated = attacks & lastOut;
            outB &= ~repeated;
            lastOut = outB;
            return outB;
        }

        Btn Fwd(Fighter me) => me.Facing > 0 ? Btn.Right : Btn.Left;
        Btn Back(Fighter me) => me.Facing > 0 ? Btn.Left : Btn.Right;

        Btn Decide(BattleSim sim, Fighter me, Fighter op)
        {
            if (sim.Phase != Phase.Fight) { holdTimer = 0; return Btn.None; }
            int now = sim.Frame;
            float dist = Math.Abs(op.X - me.X);
            float adapt = Adapt[Level];

            // throw escape
            if (me.State == FState.Thrown)
            {
                if (throwDecided != sim.Frame - me.ThrowTimer)
                {
                    throwDecided = sim.Frame - me.ThrowTimer;
                    throwBreak = rng.Chance(BreakP[Level]);
                }
                if (throwBreak && me.ThrowTimer == Math.Min(sim.R.ThrowBreakWindow - 1, Reaction[Level] / 2 + 3)) return Btn.Throw;
                return Btn.None;
            }

            if (me.State == FState.Hitstun || me.State == FState.Tumble || me.State == FState.Knockdown || me.State == FState.KO)
            {
                holdTimer = 0;
                pendingPressAt = -1;
                return Btn.None;
            }

            // a scheduled press (reaction-delayed decisions)
            if (pendingPressAt >= 0 && now >= pendingPressAt)
            {
                pendingPressAt = -1;
                Btn b = pendingPress;
                if (b != Btn.None) return b | (holdTimer > 0 ? holdDir : Btn.None);
            }

            // combo continuation on hit confirm
            if (me.State == FState.Attack && me.Move != null)
            {
                int key = me.Move.GetHashCode() ^ (sim.Frame - me.MoveFrame);
                if (me.MoveHit && comboMoveSeen != key)
                {
                    comboMoveSeen = key;
                    if (rng.Chance(ComboP[Level]))
                    {
                        Btn next = ComboFollowUp(sim, me);
                        if (next != Btn.None)
                        {
                            pendingPress = next;
                            pendingPressAt = now + Math.Max(1, me.Move.Hitstop / 2 + rng.Range(0, 3) + (3 - Level));
                        }
                    }
                }
                return holdTimer-- > 0 ? holdDir : Btn.None;
            }

            // blockstun: keep guarding
            if (me.State == FState.Blockstun)
            {
                return BlockDir(me, threatLow);
            }

            // ---- threat detection (reaction-delayed)
            bool opAttacking = op.State == FState.Attack && op.Move != null && op.Move.Kind != "none";
            int moveId = opAttacking ? (sim.Frame - op.MoveFrame) : -1;
            if (opAttacking && moveId != threatId && op.MoveFrame < op.Move.Startup + op.Move.Active)
            {
                threatId = moveId;
                threatDecideAt = now + Reaction[Level];
                float block = BlockP[Level];
                bool inRange = dist < ReachOf(op.Move) + 1.2f || op.Move.Projectile != null || op.Move.TargetOpponent || op.Move.TeleportMode != null;
                threatBlock = inRange && rng.Chance(block);
                float lowGuess = op.Move.Height == MoveHeight.Low ? 0.85f : 0.1f;
                lowGuess = lowGuess * (1 - adapt * 0.5f) + Habits.LowRatio * adapt * 0.5f;
                threatLow = op.Move.Height == MoveHeight.Low ? rng.Chance(0.55f + Level * 0.13f) : rng.Chance(lowGuess * 0.4f);
                if (op.Move.Height == MoveHeight.Throw) threatBlock = false;
                // read the attack with a counter stance or a barrier instead of blocking
                threatSpecial = -1;
                if (inRange && Level >= 1 && op.Move.Slot != "ultimate" && rng.Chance(0.12f + 0.08f * Level))
                {
                    bool strike = op.Move.Projectile == null;
                    threatSpecial = FindSpecial(me, m => (strike && m.CounterFrom >= 0) || m.ShieldFrom >= 0);
                }
            }
            if (threatSpecial >= 0 && threatDecideAt >= 0 && now >= threatDecideAt)
            {
                int s = threatSpecial;
                threatSpecial = -1;
                threatDecideAt = -1;
                if (opAttacking && op.MoveFrame < op.Move.Startup) return SlotBtn(s);
            }
            // incoming projectile
            foreach (var p in sim.Projectiles)
            {
                if (p.Owner == Index || p.Id <= lastProjThreat) continue;
                float pd = Math.Abs(p.X - me.X);
                bool towards = Math.Sign(me.X - p.X) == Math.Sign(p.VX);
                if (towards && pd < 4.5f)
                {
                    lastProjThreat = p.Id;
                    threatDecideAt = now + Reaction[Level];
                    threatBlock = rng.Chance(BlockP[Level]);
                    int shield = FindSpecial(me, m => m.ShieldFrom >= 0);
                    if (shield >= 0 && Level >= 1 && rng.Chance(0.15f + 0.1f * Level))
                    {
                        pendingPress = SlotBtn(shield);
                        pendingPressAt = now + Reaction[Level] / 2;
                    }
                    threatLow = p.Def.Ground;
                    if (!threatBlock && Level >= 2 && !p.Def.Ground && rng.Chance(0.35f))
                    {
                        // jump over it toward the opponent
                        holdDir = Btn.Up | Fwd(me);
                        holdTimer = 6;
                        return holdDir;
                    }
                }
            }
            if (threatDecideAt >= 0 && now >= threatDecideAt && threatBlock)
            {
                bool stillThreat = (opAttacking && op.MoveFrame < op.Move.Startup + op.Move.Active + 2) || ProjectileNear(sim, me, 3.5f);
                if (stillThreat)
                {
                    State = Mode.Defend;
                    return BlockDir(me, threatLow);
                }
                threatDecideAt = -1;
            }

            // ---- anti-air
            if (!op.Grounded && op.State == FState.Air && dist < 3.2f && op.VY < 4f)
            {
                float aa = Math.Min(0.97f, AntiAirP[Level] + adapt * Math.Min(0.3f, Habits.JumpRate * 0.08f));
                if (decisionCooldown <= 0)
                {
                    decisionCooldown = 20;
                    if (rng.Chance(aa))
                    {
                        // prefer an anti-air special (upward move) when available
                        int s = FindSpecial(me, m => m.MoveY > 0 || m.Launch || m.InvulnFrom == 0);
                        Btn press = s >= 0 ? SlotBtn(s) : Btn.Heavy;
                        pendingPress = press;
                        pendingPressAt = now + Math.Max(1, Reaction[Level] / 2);
                    }
                }
            }

            // ---- punish whiffed / recovering opponent
            if (opAttacking && op.MoveFrame >= op.Move.Startup + op.Move.Active && !op.MoveHit && decisionCooldown <= 0)
            {
                decisionCooldown = 18;
                float punishP = 0.1f + Level * 0.27f;
                if (rng.Chance(punishP))
                {
                    State = Mode.Punish;
                    if (dist < 1.9f) { pendingPress = Btn.Heavy; pendingPressAt = now + Reaction[Level] / 2; }
                    else if (dist < 4f)
                    {
                        int s = FindSpecial(me, m => m.MoveX > 4 || m.TeleportMode != null);
                        if (s >= 0) { pendingPress = SlotBtn(s); pendingPressAt = now + Reaction[Level] / 2; }
                    }
                }
            }
            if (decisionCooldown > 0) decisionCooldown--;

            // ---- held movement continues
            if (holdTimer > 0) { holdTimer--; return holdDir; }

            if (!me.Actionable) return Btn.None;

            // random hesitation (easier levels make more mistakes)
            if (idleTimer > 0) { idleTimer--; return Btn.None; }
            if (rng.Chance(Mistake[Level] * 0.05f)) { idleTimer = rng.Range(10, 40); return Btn.None; }

            // ---- ultimate when it can land
            if (me.Ki >= me.UltCost(sim.R))
            {
                var ult = me.Def.Move("ultimate");
                if (ult != null && (ult.Projectile != null || ult.TargetOpponent || dist < ReachOf(ult) + 0.3f))
                {
                    bool opVulnerable = op.State == FState.Hitstun || op.State == FState.Landing || op.State == FState.Attack && op.MoveFrame > op.Move.Startup + op.Move.Active
                                        || rng.Chance(0.04f + Level * 0.02f);
                    if (opVulnerable && op.State != FState.Knockdown && op.State != FState.GetUp) return Btn.Ult;
                }
            }

            // ---- heal when hurt and safe
            int healSlot = FindSpecial(me, m => m.HealAmount > 0);
            if (healSlot >= 0 && me.HpFrac < 0.55f && dist > 4.2f && op.State != FState.Attack && rng.Chance(0.05f + Level * 0.03f))
                return SlotBtn(healSlot);
            int buffSlot = FindSpecial(me, m => m.BuffDuration > 0);
            if (buffSlot >= 0 && me.BuffTimer == 0 && dist > 3.5f && rng.Chance(0.01f + Level * 0.005f))
                return SlotBtn(buffSlot);

            // ---- go for a Ki orb
            if (sim.Orbs.Count > 0)
            {
                var orb = sim.Orbs[0];
                float od = orb.X - me.X;
                if (Math.Abs(od) > 0.4f && Math.Abs(op.X - orb.X) > 1.2f)
                {
                    Btn toward = od > 0 ? Btn.Right : Btn.Left;
                    if (orb.Y > 1.9f && Math.Abs(od) < 1.4f) { holdDir = Btn.Up | toward; holdTimer = 4; return holdDir; }
                    return toward;
                }
            }

            // ---- neutral game by range
            modeTimer--;
            if (modeTimer <= 0)
            {
                modeTimer = rng.Range(25, 70);
                float aggr = Aggro[Level] + (me.Raging(sim.R) ? 0.1f : 0) + adapt * Math.Min(0.2f, Habits.ZoneRate * 0.1f);
                if (dist > 5.5f) State = rng.Chance(0.55f) && HasProjectile(me) ? Mode.Zone : Mode.Approach;
                else if (dist > 2.2f) State = rng.Chance(aggr) ? Mode.Approach : rng.Chance(0.4f) ? Mode.Zone : Mode.Neutral;
                else State = rng.Chance(aggr) ? Mode.Pressure : rng.Chance(0.5f) ? Mode.Retreat : Mode.Neutral;
                if (me.HpFrac < 0.3f && op.HpFrac > 0.5f && Level >= 2 && rng.Chance(0.3f)) State = Mode.Retreat;
            }

            switch (State)
            {
                case Mode.Zone:
                {
                    int s = FindSpecial(me, m => m.Projectile != null && m.Slot != "ultimate");
                    if (s >= 0 && rng.Chance(SpecialP[Level] * 0.08f)) return SlotBtn(s);
                    if (dist > 6.5f) return Fwd(me);
                    if (dist < 3f) return Back(me);
                    return Btn.None;
                }
                case Mode.Approach:
                    if (dist > 2.2f)
                    {
                        if (rng.Chance(0.012f + (1 - Level * 0.1f) * 0.01f)) { holdDir = Btn.Up | Fwd(me); holdTimer = 5; return holdDir; }
                        int s = FindSpecial(me, m => m.TeleportMode != null || m.MoveX > 6);
                        if (s >= 0 && dist < 5f && rng.Chance(SpecialP[Level] * 0.03f)) return SlotBtn(s);
                        return Fwd(me);
                    }
                    State = Mode.Pressure;
                    return Btn.None;
                case Mode.Retreat:
                    if (dist < 4.5f) return Back(me);
                    State = Mode.Neutral;
                    return Btn.None;
                case Mode.Pressure:
                    return PressureAttack(sim, me, op, dist, adapt);
                default:
                    if (rng.Chance(0.02f)) { holdDir = rng.Chance(0.5f) ? Fwd(me) : Back(me); holdTimer = rng.Range(6, 18); }
                    return Btn.None;
            }
        }

        Btn PressureAttack(BattleSim sim, Fighter me, Fighter op, float dist, float adapt)
        {
            if (dist > 2.1f) return Fwd(me);
            if (!rng.Chance(0.18f + Level * 0.05f)) return rng.Chance(0.3f) ? BlockDir(me, false) : Btn.None;
            // mix-up selection, biased by what the opponent tends to do
            float throwW = 0.08f + adapt * Math.Min(0.25f, Habits.BlockRate * 0.05f);
            float lowW = 0.18f + adapt * 0.25f * Habits.StandBlockRatio;
            float specialW = SpecialP[Level] * 0.25f;
            float r = rng.Value();
            if (r < throwW && dist < 1.3f) return Btn.Throw;
            r -= throwW;
            if (r < lowW) { holdDir = Btn.Down; holdTimer = 3; return Btn.Down | (rng.Chance(0.6f) ? Btn.Light : Btn.Heavy); }
            r -= lowW;
            if (r < specialW)
            {
                int s = FindSpecial(me, m => m.Damage > 0 && m.HealAmount <= 0 && m.Projectile == null && m.CounterFrom < 0 && m.ShieldFrom < 0);
                if (s >= 0) return SlotBtn(s);
            }
            return rng.Chance(0.65f) ? Btn.Light : Btn.Heavy;
        }

        Btn ComboFollowUp(BattleSim sim, Fighter me)
        {
            var m = me.Move;
            if (me.Ki >= me.UltCost(sim.R) && (m.Slot == "heavy" || m.Slot.StartsWith("special")) && Level >= 1 && rng.Chance(0.7f))
                return Btn.Ult;
            switch (m.Slot)
            {
                case "light":
                case "crouch_light":
                    if (me.ChainCount < 1 && rng.Chance(0.5f)) return Btn.Light;
                    return Btn.Heavy;
                case "heavy":
                case "crouch_heavy":
                {
                    int s = FindSpecial(me, x => x.Damage > 0 && x.HealAmount <= 0 && x.CounterFrom < 0 && x.ShieldFrom < 0 && x.BuffDuration <= 0);
                    return s >= 0 ? SlotBtn(s) : Btn.None;
                }
                case "air_light":
                    return Btn.Heavy;
                default:
                    return Btn.None;
            }
        }

        static float ReachOf(MoveDef m)
        {
            if (m.Hitbox == null) return 1.5f;
            return Math.Abs(m.Hitbox[0]) + m.Hitbox[2] * 0.5f + Math.Abs(m.MoveX) * m.Active / 60f + Math.Abs(m.MoveStartupX) * m.Startup / 60f;
        }

        static bool ProjectileNear(BattleSim sim, Fighter me, float d)
        {
            foreach (var p in sim.Projectiles)
                if (p.Owner != me.Index && Math.Abs(p.X - me.X) < d && Math.Sign(me.X - p.X) == Math.Sign(p.VX)) return true;
            return false;
        }

        static bool HasProjectile(Fighter me) => FindSpecial(me, m => m.Projectile != null) >= 0;

        static int FindSpecial(Fighter me, Func<MoveDef, bool> pred)
        {
            for (int i = 0; i < 3; i++)
            {
                var m = me.Def.Move(Fighter.SpecialSlots[i]);
                if (m != null && me.Charges[i] > 0 && pred(m)) return i;
            }
            return -1;
        }

        static Btn SlotBtn(int i) => i == 0 ? Btn.S1 : i == 1 ? Btn.S2 : Btn.S3;

        Btn BlockDir(Fighter me, bool low) => Back(me) | (low ? Btn.Down : Btn.None);
    }
}
