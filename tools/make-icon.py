# Draws BTO Sync's icon (accent rounded square + dark sync arrows, like the Primary button) into a multi-size .ico.
# Usage: python tools/make-icon.py sync/app.ico preview.png
import math, sys
from PIL import Image, ImageDraw
out, preview = sys.argv[1], sys.argv[2]
ACCENT, ACCENT_FG, BIG = (0x3C, 0xC8, 0xB4), (0x06, 0x23, 0x1F), 1024

def arrows(d, w, r, head):
    c = BIG / 2
    for a0, a1 in ((205, 335), (25, 155)):         # two arcs, each with a round tail and an arrowhead at its end
        d.arc([c - r, c - r, c + r, c + r], a0, a1, fill=ACCENT_FG, width=w)
        rm = r - w / 2                              # PIL grows the arc's width inward: rm is the stroke's centre line
        x, y = c + rm * math.cos(math.radians(a0)), c + rm * math.sin(math.radians(a0))
        d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=ACCENT_FG)
        t = math.radians(a1)
        P = (c + rm * math.cos(t), c + rm * math.sin(t)); tg = (-math.sin(t), math.cos(t)); n = (math.cos(t), math.sin(t))
        b = (P[0] - tg[0] * head * .15, P[1] - tg[1] * head * .15)
        d.polygon([(P[0] + tg[0] * head, P[1] + tg[1] * head),
                   (b[0] + n[0] * head, b[1] + n[1] * head), (b[0] - n[0] * head, b[1] - n[1] * head)], fill=ACCENT_FG)

def frame(size):
    im = Image.new("RGBA", (BIG, BIG), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    pad = 0 if size <= 32 else BIG // 32          # small sizes: use every pixel
    d.rounded_rectangle([pad, pad, BIG - 1 - pad, BIG - 1 - pad], radius=BIG * 10 // 36, fill=ACCENT)
    small = size <= 24                              # thicker, bigger arrows where pixels are few
    arrows(d, BIG // (7 if small else 9), BIG * (.33 if small else .29), BIG * (.15 if small else .12))
    return im.resize((size, size), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
frames = [frame(s) for s in sizes]
frames[-1].save(out, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
sheet = Image.new("RGBA", (sum(sizes) + 10 * len(sizes), 266), (24, 26, 32, 255))
x = 5
for s, im in zip(sizes, frames):
    sheet.alpha_composite(im, (x, 5)); x += s + 10
sheet.save(preview)
