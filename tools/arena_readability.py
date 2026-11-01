"""Readability pass for arena art: push the stage back behind the fighters.

Fighting-game backgrounds must never compete with the characters. We apply atmospheric perspective:
lower contrast + saturation, slightly darker and slightly blurred (depth of field), and the side regions -
where the big props (towers, cars, gates, pillars) sit right next to cornered fighters - pushed back the most.

Writes Arenas/<id>/sky_fx.png and mid_fx.png next to the originals; BattleView prefers the _fx versions,
menus keep the crisp originals.

    python tools/arena_readability.py [--preview autotest/arenas_fx.jpg]
"""
import argparse
import glob
import os

from PIL import Image, ImageEnhance, ImageFilter

ROOT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Arenas")


def grade(im, blur, sat, con, bri):
    rgb = im.convert("RGB")
    if blur > 0:
        rgb = rgb.filter(ImageFilter.GaussianBlur(blur))
    rgb = ImageEnhance.Color(rgb).enhance(sat)
    rgb = ImageEnhance.Contrast(rgb).enhance(con)
    rgb = ImageEnhance.Brightness(rgb).enhance(bri)
    return rgb


def edge_mask(w, h, inner, outer):
    """0 in the middle of the stage, rising to 1 at the left/right edges (smoothstep)."""
    m = Image.new("L", (w, 1))
    px = m.load()
    for x in range(w):
        d = abs(x / (w - 1) * 2 - 1)          # 0 centre .. 1 edge
        t = min(1.0, max(0.0, (d - inner) / (outer - inner)))
        px[x, 0] = int(255 * t * t * (3 - 2 * t))
    return m.resize((w, h))


def process(arena):
    d = os.path.join(ROOT, arena)
    sky = Image.open(os.path.join(d, "sky.png"))
    mid = Image.open(os.path.join(d, "mid.png")).convert("RGBA")

    # far plane: clearly out of focus and hazy
    sky_fx = grade(sky, 2.2, 0.72, 0.78, 0.86)
    sky_fx.save(os.path.join(d, "sky_fx.png"), optimize=True)

    # stage plane: gently softened overall, edges pushed back hard
    w, h = mid.size
    alpha = mid.getchannel("A")
    soft = grade(mid, 1.0, 0.84, 0.86, 0.9)
    deep = grade(mid, 3.2, 0.55, 0.68, 0.7)
    out = Image.composite(deep, soft, edge_mask(w, h, 0.42, 0.95))
    # blur the alpha with the colour so blurred props do not keep razor-sharp cut-out edges
    a2 = Image.composite(alpha.filter(ImageFilter.GaussianBlur(3.2)), alpha.filter(ImageFilter.GaussianBlur(1.0)),
                         edge_mask(w, h, 0.42, 0.95))
    out = out.convert("RGBA")
    out.putalpha(a2)
    out.save(os.path.join(d, "mid_fx.png"), optimize=True)
    return sky_fx, out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview")
    a = ap.parse_args()
    tiles = []
    for d in sorted(glob.glob(os.path.join(ROOT, "*", "mid.png"))):
        arena = os.path.basename(os.path.dirname(d))
        sky_fx, mid_fx = process(arena)
        print("processed", arena)
        if a.preview:
            c = sky_fx.convert("RGBA").resize(mid_fx.size)
            c.alpha_composite(mid_fx)
            tiles.append(c.resize((800, 360)))
    if a.preview and tiles:
        sheet = Image.new("RGB", (1600, 360 * ((len(tiles) + 1) // 2)))
        for i, t in enumerate(tiles):
            sheet.paste(t, ((i % 2) * 800, (i // 2) * 360))
        sheet.save(a.preview, quality=90)


if __name__ == "__main__":
    main()
