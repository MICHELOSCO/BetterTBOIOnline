# Draws BTO Sync's icon (the sidebar logo: accent rounded square + Segoe sync glyph) into a multi-size .ico.
# Usage: python tools/make-icon.py sync/app.ico preview.png
import sys
from PIL import Image, ImageDraw, ImageFont
out, preview = sys.argv[1], sys.argv[2]
ACCENT, BIG = (0xE5, 0x48, 0x4D, 255), 1024

def frame(size):
    im = Image.new("RGBA", (BIG, BIG), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    pad = 0 if size <= 32 else BIG // 32          # small sizes: use every pixel
    d.rounded_rectangle([pad, pad, BIG - 1 - pad, BIG - 1 - pad], radius=BIG * 10 // 36, fill=ACCENT)
    f = ImageFont.truetype("C:/Windows/Fonts/segmdl2.ttf", int(BIG * (0.60 if size <= 32 else 0.52)))
    stroke = BIG // 40 if size <= 24 else BIG // 70 if size <= 48 else BIG // 110  # thicker arrows where pixels are few
    d.text((BIG / 2, BIG / 2), "\uE895", font=f, fill="white", anchor="mm", stroke_width=stroke, stroke_fill="white")
    return im.resize((size, size), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
frames = [frame(s) for s in sizes]
frames[-1].save(out, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
sheet = Image.new("RGBA", (sum(sizes) + 10 * len(sizes), 266), (24, 26, 32, 255))
x = 5
for s, im in zip(sizes, frames):
    sheet.alpha_composite(im, (x, 5)); x += s + 10
sheet.save(preview)
