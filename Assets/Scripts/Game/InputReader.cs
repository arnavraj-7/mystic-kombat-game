using MysticKombat.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace MysticKombat
{
    /// <summary>
    /// Reads the shared keyboard and any gamepads into per-player <see cref="Btn"/> frames, and gives
    /// menus simple navigation events. Default layout (both players on one keyboard):
    ///   P1: WASD move, F light, G heavy, H special1, R special2, T special3, Y ultimate, V throw
    ///   P2: arrows move, Numpad 1/2/3/4/5/6 = light/heavy/sp1/sp2/sp3/ult, Numpad 0 throw
    ///       (no numpad: , . / = light/heavy/sp1, L ; ' = sp2/sp3/ult, Right Shift throw)
    ///   Solo modes: P1 may also use J K L / U I O and the arrow keys.
    /// Gamepads: stick/d-pad move, X/Square light, Y/Triangle heavy, A/Cross sp1, B/Circle sp2,
    ///   RB sp3, RT ultimate, LB throw. Pad 1 -> P1, pad 2 -> P2.
    /// </summary>
    public static class InputReader
    {
        public static bool SoloLayout = true;   // P1 also gets the right-hand keys when P2 isn't human

        static Keyboard Kb => Keyboard.current;

        static bool K(Key k)
        {
            var kb = Kb;
            return kb != null && kb[k].isPressed;
        }

        static bool KDown(Key k)
        {
            var kb = Kb;
            return kb != null && kb[k].wasPressedThisFrame;
        }

        static Gamepad Pad(int player)
        {
            var all = Gamepad.all;
            return player < all.Count ? all[player] : null;
        }

        public static Btn Read(int player)
        {
            Btn b = Btn.None;
            if (player == 0)
            {
                if (K(Key.A)) b |= Btn.Left;
                if (K(Key.D)) b |= Btn.Right;
                if (K(Key.W)) b |= Btn.Up;
                if (K(Key.S)) b |= Btn.Down;
                if (K(Key.F)) b |= Btn.Light;
                if (K(Key.G)) b |= Btn.Heavy;
                if (K(Key.H)) b |= Btn.S1;
                if (K(Key.R)) b |= Btn.S2;
                if (K(Key.T)) b |= Btn.S3;
                if (K(Key.Y)) b |= Btn.Ult;
                if (K(Key.V)) b |= Btn.Throw;
                if (SoloLayout)
                {
                    if (K(Key.LeftArrow)) b |= Btn.Left;
                    if (K(Key.RightArrow)) b |= Btn.Right;
                    if (K(Key.UpArrow) || K(Key.Space)) b |= Btn.Up;
                    if (K(Key.DownArrow)) b |= Btn.Down;
                    if (K(Key.J)) b |= Btn.Light;
                    if (K(Key.K)) b |= Btn.Heavy;
                    if (K(Key.L)) b |= Btn.S1;
                    if (K(Key.U)) b |= Btn.S2;
                    if (K(Key.I)) b |= Btn.S3;
                    if (K(Key.O)) b |= Btn.Ult;
                    if (K(Key.Semicolon)) b |= Btn.Throw;
                }
            }
            else
            {
                if (K(Key.LeftArrow)) b |= Btn.Left;
                if (K(Key.RightArrow)) b |= Btn.Right;
                if (K(Key.UpArrow)) b |= Btn.Up;
                if (K(Key.DownArrow)) b |= Btn.Down;
                if (K(Key.Numpad1) || K(Key.Comma)) b |= Btn.Light;
                if (K(Key.Numpad2) || K(Key.Period)) b |= Btn.Heavy;
                if (K(Key.Numpad3) || K(Key.Slash)) b |= Btn.S1;
                if (K(Key.Numpad4) || K(Key.L)) b |= Btn.S2;
                if (K(Key.Numpad5) || K(Key.Semicolon)) b |= Btn.S3;
                if (K(Key.Numpad6) || K(Key.Quote)) b |= Btn.Ult;
                if (K(Key.Numpad0) || K(Key.RightShift)) b |= Btn.Throw;
            }

            var pad = Pad(player);
            if (pad != null) b |= ReadPad(pad);
            return b;
        }

        static Btn ReadPad(Gamepad p)
        {
            Btn b = Btn.None;
            Vector2 s = p.leftStick.ReadValue();
            if (p.dpad.left.isPressed || s.x < -0.45f) b |= Btn.Left;
            if (p.dpad.right.isPressed || s.x > 0.45f) b |= Btn.Right;
            if (p.dpad.up.isPressed || s.y > 0.55f) b |= Btn.Up;
            if (p.dpad.down.isPressed || s.y < -0.55f) b |= Btn.Down;
            if (p.buttonWest.isPressed) b |= Btn.Light;
            if (p.buttonNorth.isPressed) b |= Btn.Heavy;
            if (p.buttonSouth.isPressed) b |= Btn.S1;
            if (p.buttonEast.isPressed) b |= Btn.S2;
            if (p.rightShoulder.isPressed) b |= Btn.S3;
            if (p.rightTrigger.ReadValue() > 0.5f) b |= Btn.Ult;
            if (p.leftShoulder.isPressed || p.leftTrigger.ReadValue() > 0.5f) b |= Btn.Throw;
            if (p.startButton.isPressed) b |= Btn.Start;
            return b;
        }

        // ------------------------------------------------------------------ menu navigation

        public enum Nav { None, Up, Down, Left, Right, Confirm, Back, Start, Alt }

        static readonly float[] repeatAt = new float[2];
        static readonly Nav[] heldNav = new Nav[2];

        /// <summary>Menu event for a player this frame (with key-repeat for directions).</summary>
        public static Nav Menu(int player)
        {
            Nav fresh = MenuPressed(player);
            Nav held = MenuHeld(player);
            if (fresh != Nav.None)
            {
                heldNav[player] = fresh;
                repeatAt[player] = Time.unscaledTime + 0.38f;
                return fresh;
            }
            if (held != Nav.None && held == heldNav[player] && held <= Nav.Right && Time.unscaledTime >= repeatAt[player])
            {
                repeatAt[player] = Time.unscaledTime + 0.09f;
                return held;
            }
            if (held == Nav.None) heldNav[player] = Nav.None;
            return Nav.None;
        }

        /// <summary>Either player's menu event (P1 first).</summary>
        public static Nav AnyMenu(out int who)
        {
            var a = Menu(0);
            if (a != Nav.None) { who = 0; return a; }
            var b = Menu(1);
            who = b != Nav.None ? 1 : -1;
            return b;
        }

        static Nav MenuPressed(int player)
        {
            var pad = Pad(player);
            if (player == 0)
            {
                if (KDown(Key.W) || KDown(Key.UpArrow) && SoloLayout) return Nav.Up;
                if (KDown(Key.S) || KDown(Key.DownArrow) && SoloLayout) return Nav.Down;
                if (KDown(Key.A) || KDown(Key.LeftArrow) && SoloLayout) return Nav.Left;
                if (KDown(Key.D) || KDown(Key.RightArrow) && SoloLayout) return Nav.Right;
                if (KDown(Key.F) || KDown(Key.Enter) || KDown(Key.Space) || KDown(Key.J)) return Nav.Confirm;
                if (KDown(Key.G) || KDown(Key.Escape) || KDown(Key.Backspace) || KDown(Key.K)) return Nav.Back;
                if (KDown(Key.H) || KDown(Key.Tab)) return Nav.Alt;
            }
            else
            {
                if (KDown(Key.UpArrow)) return Nav.Up;
                if (KDown(Key.DownArrow)) return Nav.Down;
                if (KDown(Key.LeftArrow)) return Nav.Left;
                if (KDown(Key.RightArrow)) return Nav.Right;
                if (KDown(Key.Numpad1) || KDown(Key.Comma) || KDown(Key.NumpadEnter)) return Nav.Confirm;
                if (KDown(Key.Numpad2) || KDown(Key.Period)) return Nav.Back;
                if (KDown(Key.Numpad3) || KDown(Key.Slash)) return Nav.Alt;
            }
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) return Nav.Up;
                if (pad.dpad.down.wasPressedThisFrame) return Nav.Down;
                if (pad.dpad.left.wasPressedThisFrame) return Nav.Left;
                if (pad.dpad.right.wasPressedThisFrame) return Nav.Right;
                if (pad.buttonSouth.wasPressedThisFrame) return Nav.Confirm;
                if (pad.buttonEast.wasPressedThisFrame) return Nav.Back;
                if (pad.buttonNorth.wasPressedThisFrame) return Nav.Alt;
                if (pad.startButton.wasPressedThisFrame) return Nav.Start;
                Vector2 s = pad.leftStick.ReadValue();
                // stick edges handled by MenuHeld + repeat
            }
            return Nav.None;
        }

        static Nav MenuHeld(int player)
        {
            if (player == 0)
            {
                if (K(Key.W) || K(Key.UpArrow) && SoloLayout) return Nav.Up;
                if (K(Key.S) || K(Key.DownArrow) && SoloLayout) return Nav.Down;
                if (K(Key.A) || K(Key.LeftArrow) && SoloLayout) return Nav.Left;
                if (K(Key.D) || K(Key.RightArrow) && SoloLayout) return Nav.Right;
            }
            else
            {
                if (K(Key.UpArrow)) return Nav.Up;
                if (K(Key.DownArrow)) return Nav.Down;
                if (K(Key.LeftArrow)) return Nav.Left;
                if (K(Key.RightArrow)) return Nav.Right;
            }
            var pad = Pad(player);
            if (pad != null)
            {
                Vector2 s = pad.leftStick.ReadValue();
                if (pad.dpad.up.isPressed || s.y > 0.6f) return Nav.Up;
                if (pad.dpad.down.isPressed || s.y < -0.6f) return Nav.Down;
                if (pad.dpad.left.isPressed || s.x < -0.6f) return Nav.Left;
                if (pad.dpad.right.isPressed || s.x > 0.6f) return Nav.Right;
            }
            return Nav.None;
        }

        public static bool PausePressed()
        {
            if (KDown(Key.Escape) || KDown(Key.P)) return true;
            foreach (var p in Gamepad.all) if (p.startButton.wasPressedThisFrame) return true;
            return false;
        }

        public static bool AnyKeyDown()
        {
            if (Kb != null && Kb.anyKey.wasPressedThisFrame) return true;
            foreach (var p in Gamepad.all)
                foreach (var c in p.allControls)
                    if (c is ButtonControl bc && bc.wasPressedThisFrame) return true;
            var m = Mouse.current;
            return m != null && m.leftButton.wasPressedThisFrame;
        }

        public static bool KeyDown(Key k) => KDown(k);

        public static string[] P1Keys => new[] { "F", "G", "H", "R", "T", "Y", "V" };
        public static string[] P2Keys => new[] { "Num1", "Num2", "Num3", "Num4", "Num5", "Num6", "Num0" };
        public static string[] SoloKeys => new[] { "J", "K", "L", "U", "I", "O", ";" };
    }
}
