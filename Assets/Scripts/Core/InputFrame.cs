using System;

namespace MysticKombat.Core
{
    /// <summary>One frame of a player's controller state. Directions are absolute (screen space).</summary>
    [Flags]
    public enum Btn : ushort
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Up = 1 << 2,
        Down = 1 << 3,
        Light = 1 << 4,
        Heavy = 1 << 5,
        S1 = 1 << 6,
        S2 = 1 << 7,
        S3 = 1 << 8,
        Ult = 1 << 9,
        Throw = 1 << 10,
        Start = 1 << 11,
        Attacks = Light | Heavy | S1 | S2 | S3 | Ult | Throw,
    }

    public static class BtnExt
    {
        public static bool Has(this Btn b, Btn f) => (b & f) != 0;
    }

    /// <summary>
    /// Per-player input history: press edges, an input buffer, and numpad motion detection
    /// (236 = quarter-circle forward, 214 = back, 623 = dragon punch, 236236 = double QCF).
    /// </summary>
    public sealed class InputHistory
    {
        public const int Size = 64;
        readonly Btn[] held = new Btn[Size];
        readonly int[] pressFrame = new int[16];   // last press frame per bit
        readonly bool[] consumed = new bool[16];
        int head = -1;
        public int Frame { get; private set; } = -1;

        public Btn Current => head < 0 ? Btn.None : held[head];
        public Btn Previous => head < 1 ? Btn.None : held[(head - 1 + Size) % Size];

        public void Reset()
        {
            Array.Clear(held, 0, Size);
            for (int i = 0; i < 16; i++) { pressFrame[i] = -1000; consumed[i] = true; }
            head = -1;
            Frame = -1;
        }

        public InputHistory() { Reset(); }

        public void Push(Btn b)
        {
            Btn prev = Current;
            head = (head + 1) % Size;
            held[head] = b;
            Frame++;
            Btn pressed = b & ~prev;
            for (int i = 0; i < 16; i++)
                if (((ushort)pressed & (1 << i)) != 0) { pressFrame[i] = Frame; consumed[i] = false; }
        }

        public bool Held(Btn b) => (Current & b) != 0;

        /// <summary>Pressed within the buffer window and not yet used by a move.</summary>
        public bool Buffered(Btn b, int window)
        {
            int bit = Bit(b);
            return !consumed[bit] && Frame - pressFrame[bit] <= window;
        }

        public bool PressedNow(Btn b) => (Current & b) != 0 && (Previous & b) == 0;

        public void Consume(Btn b) { consumed[Bit(b)] = true; }

        public int PressAge(Btn b) => Frame - pressFrame[Bit(b)];

        static int Bit(Btn b)
        {
            ushort v = (ushort)b;
            for (int i = 0; i < 16; i++) if ((v & (1 << i)) != 0) return i;
            return 0;
        }

        /// <summary>Numpad direction (1-9, 5 = neutral) relative to facing.</summary>
        public static int Numpad(Btn b, int facing)
        {
            bool fwd = facing > 0 ? b.Has(Btn.Right) : b.Has(Btn.Left);
            bool back = facing > 0 ? b.Has(Btn.Left) : b.Has(Btn.Right);
            bool up = b.Has(Btn.Up), down = b.Has(Btn.Down);
            int x = fwd && !back ? 1 : back && !fwd ? -1 : 0;
            int y = up && !down ? 1 : down && !up ? -1 : 0;
            return 5 + x + 3 * y;
        }

        public int NumpadAt(int ago, int facing) => Numpad(held[((head - ago) % Size + Size) % Size], facing);

        /// <summary>True if the numpad sequence appears (in order, gaps allowed) within the last <paramref name="window"/> frames.</summary>
        public bool Motion(string seq, int facing, int window)
        {
            int idx = seq.Length - 1;
            int maxAgo = Math.Min(window, Math.Min(Frame, Size - 1));
            for (int ago = 0; ago <= maxAgo && idx >= 0; ago++)
            {
                int d = NumpadAt(ago, facing);
                if (d == seq[idx] - '0') idx--;
            }
            return idx < 0;
        }

        /// <summary>Copy of recent held states, newest first (for the training-mode input display).</summary>
        public Btn HeldAgo(int ago) => ago > Frame ? Btn.None : held[((head - ago) % Size + Size) % Size];
    }
}
