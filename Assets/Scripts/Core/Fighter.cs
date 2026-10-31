using System;

namespace MysticKombat.Core
{
    public enum FState { Intro, Idle, Walk, Crouch, PreJump, Air, Attack, Landing, Hitstun, Blockstun, Tumble, Knockdown, GetUp, Thrown, KO, Victory }

    public sealed class MatchStats
    {
        public float DamageDealt, Healed, KiFromOrbs;
        public int Hits, Blocks, Specials, Ults, UltHits, Throws, ThrowBreaks, Counters, MaxCombo, OrbsTaken, Projectiles;
    }

    /// <summary>Runtime state of one fighter inside a <see cref="BattleSim"/>.</summary>
    public sealed class Fighter
    {
        public readonly int Index;
        public readonly FighterDef Def;
        public readonly InputHistory Input = new InputHistory();
        public MatchStats Stats = new MatchStats();

        public float X, Y, VX, VY;
        public int Facing = 1;
        public float Hp, Ki;
        public FState State = FState.Intro;
        public int StateFrame;

        // current move
        public MoveDef Move;
        public int MoveFrame, HitsDone, ConnectFrame = -1000, ChainCount;
        public bool MoveHit, MoveBlocked, MoveWhiffSoundDone;
        public float TargetX;
        public int ProjectilesSpawned;

        public int Stun, Hitstop, Invuln;
        public int ComboCount, JuggleCount;
        public float ComboDamage;
        public bool JuggleLocked;

        // abilities (Valorant-style charges per special slot)
        public readonly int[] Charges = new int[3];
        public readonly int[] RechargeTimer = new int[3];

        // power-up buff
        public float BuffDamage = 1f, BuffSpeed = 1f;
        public int BuffTimer;
        public bool BuffArmor;

        public int RoundWins;
        public bool UsedRageThisRound;
        public int ThrownBy = -1, ThrowTimer;
        public bool Blocking;      // visual: currently blocking
        public int LastBlockFrame = -1000;

        public Fighter(int index, FighterDef def)
        {
            Index = index;
            Def = def;
        }

        public bool Grounded => Y <= 0.0001f;
        public float HpFrac => Hp / Def.MaxHp;
        public bool Raging(Rules r) => Hp > 0 && HpFrac <= r.RageThreshold;
        public bool Actionable => State == FState.Idle || State == FState.Walk || State == FState.Crouch;
        public bool IsCrouching => State == FState.Crouch || (State == FState.Blockstun && Crouched) || (State == FState.Attack && Move != null && Move.Slot.StartsWith("crouch"));
        public bool Crouched;      // remembered crouch for blockstun

        public float UltCost(Rules r) => Raging(r) ? r.RageUltCost : r.KiMax;

        public static readonly string[] SpecialSlots = { "special1", "special2", "special3" };

        public void ResetForRound(float x, int facing)
        {
            X = x; Y = 0; VX = VY = 0;
            Facing = facing;
            Hp = Def.MaxHp;
            State = FState.Intro;
            StateFrame = 0;
            Move = null;
            Stun = Hitstop = Invuln = 0;
            ComboCount = JuggleCount = 0;
            ComboDamage = 0;
            JuggleLocked = false;
            BuffDamage = BuffSpeed = 1f;
            BuffTimer = 0;
            BuffArmor = false;
            ThrownBy = -1;
            UsedRageThisRound = false;
            Blocking = false;
            for (int i = 0; i < 3; i++)
            {
                var m = Def.Move(SpecialSlots[i]);
                Charges[i] = m == null ? 0 : Math.Max(1, m.Charges);
                RechargeTimer[i] = 0;
            }
            Input.Reset();
        }

        public int MaxCharges(int slot)
        {
            var m = Def.Move(SpecialSlots[slot]);
            return m == null ? 0 : Math.Max(1, m.Charges);
        }

        public float HurtHeight
        {
            get
            {
                switch (State)
                {
                    case FState.Crouch: return Def.Height * 0.62f;
                    case FState.Knockdown:
                    case FState.KO: return Def.Height * 0.3f;
                    case FState.Tumble: return Def.Height * 0.6f;
                    case FState.Air: return Def.Height * 0.8f;
                    default: return IsCrouching ? Def.Height * 0.62f : Def.Height;
                }
            }
        }

        public void SetState(FState s)
        {
            State = s;
            StateFrame = 0;
            if (s != FState.Attack) Move = null;
            if (s == FState.Idle || s == FState.Walk || s == FState.Crouch) Blocking = false;
        }
    }

    public sealed class Projectile
    {
        public int Owner;
        public ProjectileDef Def;
        public float X, Y, VX, VY;
        public int Facing, Life, HitsDone, HitTimer, Id;
        public float Power = 1f;
        public bool Dead, Ultimate;
        public string MoveId;
    }

    public sealed class KiOrb
    {
        public float X, Y;
        public int Age;
        public bool Taken;
    }
}
