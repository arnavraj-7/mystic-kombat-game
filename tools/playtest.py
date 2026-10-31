"""Real-keyboard play test: launches the built game, focuses its window and drives it with OS-level
key presses (SendInput scan codes, exactly like a physical keyboard), grabbing screenshots as it goes.

    python tools/playtest.py [--exe Builds/Windows/MysticKombat.exe] [--out autotest/playtest]
"""
import argparse
import ctypes
import os
import random
import subprocess
import sys
import time
from ctypes import wintypes

from PIL import ImageGrab

user32 = ctypes.WinDLL("user32", use_last_error=True)
ctypes.windll.shcore.SetProcessDpiAwareness(2)  # real pixel coordinates on a 150% display

SC = {
    "w": 0x11, "a": 0x1E, "s": 0x1F, "d": 0x20, "f": 0x21, "g": 0x22, "h": 0x23, "r": 0x13, "t": 0x14,
    "y": 0x15, "v": 0x2F, "j": 0x24, "k": 0x25, "l": 0x26, "enter": 0x1C, "esc": 0x01, "space": 0x39,
    "f1": 0x3B, "f2": 0x3C, "f3": 0x3D,
}

INPUT_KEYBOARD = 1
KEYEVENTF_KEYUP = 0x2
KEYEVENTF_SCANCODE = 0x8


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wintypes.WORD), ("wScan", wintypes.WORD), ("dwFlags", wintypes.DWORD),
                ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.POINTER(ctypes.c_ulong))]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [("dx", wintypes.LONG), ("dy", wintypes.LONG), ("mouseData", wintypes.DWORD),
                ("dwFlags", wintypes.DWORD), ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.POINTER(ctypes.c_ulong))]


class _U(ctypes.Union):
    _fields_ = [("ki", KEYBDINPUT), ("mi", MOUSEINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wintypes.DWORD), ("u", _U)]


GAME_HWND = None


def key(name, up=False):
    # safety: only ever type into the game window (never into a dialog that stole focus)
    if GAME_HWND and user32.GetForegroundWindow() != GAME_HWND:
        user32.SetForegroundWindow(GAME_HWND)
        time.sleep(0.2)
        if user32.GetForegroundWindow() != GAME_HWND:
            print("focus lost, key skipped:", name, flush=True)
            return
    flags = KEYEVENTF_SCANCODE | (KEYEVENTF_KEYUP if up else 0)
    inp = INPUT(type=INPUT_KEYBOARD, u=_U(ki=KEYBDINPUT(0, SC[name], flags, 0, None)))
    user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))


def tap(name, hold=0.05, after=0.06):
    key(name)
    time.sleep(hold)
    key(name, True)
    time.sleep(after)


def hold(names, seconds):
    for n in names:
        key(n)
    time.sleep(seconds)
    for n in names:
        key(n, True)


def motion(seq, button, facing_right=True):
    """Numpad motion, e.g. '236' then a button press (fighter facing right)."""
    m = {"2": ["s"], "3": ["s", "d" if facing_right else "a"], "6": ["d" if facing_right else "a"],
         "4": ["a" if facing_right else "d"], "1": ["s", "a" if facing_right else "d"]}
    held = []
    for c in seq:
        for n in held:
            key(n, True)
        held = m[c]
        for n in held:
            key(n)
        time.sleep(0.035)
    tap(button, 0.04, 0.0)
    for n in held:
        key(n, True)
    time.sleep(0.1)


def find_window(title="Mystic Kombat", timeout=40):
    end = time.time() + timeout
    while time.time() < end:
        hwnd = user32.FindWindowW(None, title)
        if hwnd:
            return hwnd
        time.sleep(0.5)
    sys.exit("game window not found")


def focus(hwnd):
    user32.ShowWindow(hwnd, 9)
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.3)


def shot(hwnd, out, name):
    r = wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    img = ImageGrab.grab(bbox=(r.left, r.top, r.right, r.bottom), all_screens=True)
    path = os.path.join(out, name + ".png")
    img.save(path)
    print("shot", path, img.size, flush=True)


def training_ult(hwnd, a):
    """Training: P1 vs standing dummy with infinite Ki; walk in and fire every special and the ultimate."""
    for _ in range(5):
        tap("s")           # -> TRAINING
    tap("enter")
    time.sleep(1.0)
    for _ in range(a.fighter_steps):
        tap("d")
    tap("f")               # P1
    time.sleep(0.4)
    tap("d")
    tap("f")               # dummy
    time.sleep(2.5)
    shot(hwnd, a.out, "t01_training")
    hold(["d"], 0.9)
    for i, k in enumerate(["h", "r", "t"]):
        tap(k, after=0.15)
        shot(hwnd, a.out, f"t1{i}_special_{k}")
        time.sleep(1.3)
    tap("y", after=0.05)       # ultimate (Ki is refilled in training)
    for i in range(8):
        time.sleep(0.35)
        shot(hwnd, a.out, f"t2{i}_ultimate")
    time.sleep(2)
    shot(hwnd, a.out, "t30_after_ult")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default="Builds/Windows/MysticKombat.exe")
    ap.add_argument("--out", default="autotest/playtest")
    ap.add_argument("--fighter-steps", type=int, default=0, help="move right N tiles on char select")
    ap.add_argument("--flow", default="vscpu", choices=["vscpu", "training-ult"])
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    proc = subprocess.Popen([os.path.abspath(a.exe), "-screen-fullscreen", "0", "-screen-width", "1600", "-screen-height", "900",
                             "-logFile", os.path.join(a.out, "player.log")])
    global GAME_HWND
    hwnd = find_window()
    time.sleep(4)
    focus(hwnd)
    GAME_HWND = hwnd
    shot(hwnd, a.out, "01_intro_or_title")
    tap("enter")           # skip intro if it plays
    time.sleep(1.5)
    tap("enter")           # title -> main menu
    time.sleep(1.0)
    shot(hwnd, a.out, "02_main_menu")
    if a.flow == "training-ult":
        training_ult(hwnd, a)
        proc.terminate()
        return
    tap("s"); tap("s")     # ARCADE -> VERSUS -> VERSUS CPU
    shot(hwnd, a.out, "03_menu_vs_cpu")
    tap("enter")
    time.sleep(1.0)
    for _ in range(a.fighter_steps):
        tap("d")
    shot(hwnd, a.out, "04_char_select")
    tap("f")               # lock P1
    time.sleep(0.4)
    tap("d"); tap("d")     # move to the CPU's fighter
    tap("f")               # lock CPU
    time.sleep(0.4)
    tap("d")               # difficulty Normal -> Hard
    shot(hwnd, a.out, "05_cpu_difficulty")
    tap("f")
    time.sleep(0.8)
    shot(hwnd, a.out, "06_stage_select")
    tap("enter")
    time.sleep(1.2)
    shot(hwnd, a.out, "07_vs")
    time.sleep(3.5)
    shot(hwnd, a.out, "08_round_intro")
    time.sleep(1.8)

    # --- fight with the real keyboard
    hold(["d"], 0.6)
    tap("f"); tap("f"); tap("g", after=0.2)
    shot(hwnd, a.out, "09_jab_jab_heavy")
    tap("d", 0.04, 0.04); tap("d", 0.04, 0.02)      # dash
    time.sleep(0.15)
    shot(hwnd, a.out, "10_dash")
    tap("h", after=0.25)                              # special 1
    shot(hwnd, a.out, "11_special1")
    time.sleep(0.6)
    motion("236", "f")                                # special 1 via motion input
    time.sleep(0.6)
    hold(["s"], 0.1); tap("g", after=0.3)             # sweep
    shot(hwnd, a.out, "12_sweep")
    hold(["w", "d"], 0.15); time.sleep(0.25); tap("g", after=0.3)  # jump-in heavy
    shot(hwnd, a.out, "13_jump_in")
    hold(["a"], 1.0)                                  # block
    shot(hwnd, a.out, "14_block")
    tap("v", after=0.3)                               # throw
    shot(hwnd, a.out, "15_throw")
    tap("t", after=0.6)                               # special 3
    shot(hwnd, a.out, "16_special3")
    tap("esc", after=0.5)
    shot(hwnd, a.out, "17_pause")
    tap("esc", after=0.4)

    # mash a varied but sensible pattern for a while, screenshot periodically
    t0 = time.time()
    n = 0
    rng = random.Random(3)
    while time.time() - t0 < 150:
        r = rng.random()
        if r < 0.25: hold(["d"], 0.3)
        elif r < 0.45: tap("f"); tap("f"); tap("g")
        elif r < 0.55: tap(rng.choice(["h", "r", "t"]))
        elif r < 0.62: tap("y")
        elif r < 0.7: hold(["a"], 0.5)
        elif r < 0.78: motion("236", "f")
        elif r < 0.84: hold(["w", "d"], 0.12); time.sleep(0.2); tap("f")
        elif r < 0.9: tap("d", 0.04, 0.04); tap("d", 0.04, 0.05); tap("f")
        else: hold(["s"], 0.08); tap("f"); tap("g")
        if time.time() - t0 > (n + 1) * 15:
            n += 1
            shot(hwnd, a.out, f"20_fight_{n:02d}")
        if proc.poll() is not None:
            print("GAME EXITED", proc.returncode)
            break
    time.sleep(1)
    shot(hwnd, a.out, "30_after_fight")
    time.sleep(8)
    shot(hwnd, a.out, "31_result")
    proc.terminate()


if __name__ == "__main__":
    main()
