"""Icon for BigWalk.SkipIntro.

Thumbnail-first: at ~110px in the Thunderstore grid, fine detail and long words
disappear. So this is one big high-contrast glyph (fast-forward) on a flat
ground, with a short wordmark that stays legible when shrunk.

Run from the plugin folder:  python make_icon.py
"""
from PIL import Image, ImageDraw, ImageFont

SIZE = 256
BG = (24, 32, 28)
PANEL = (31, 43, 37)
ACCENT = (126, 217, 87)
TEXT = (233, 244, 226)


def font(size: int, bold: bool = True):
    names = ("segoeuib.ttf", "arialbd.ttf") if bold else ("segoeui.ttf", "arial.ttf")
    for n in names:
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def centered(draw, text, f, y, fill):
    box = draw.textbbox((0, 0), text, font=f)
    draw.text(((SIZE - (box[2] - box[0])) / 2 - box[0], y), text, font=f, fill=fill)


img = Image.new("RGB", (SIZE, SIZE), BG)
d = ImageDraw.Draw(img)

# Soft rounded panel so the tile reads as deliberate rather than a bare square.
d.rounded_rectangle([10, 10, SIZE - 10, SIZE - 10], radius=28, fill=PANEL)

# Fast-forward glyph: two chevrons + end bar, optically centred slightly high
# to leave room for the wordmark.
cy = 112
h = 46
for ox in (58, 116):
    d.polygon([(ox, cy - h), (ox + 60, cy), (ox, cy + h)], fill=ACCENT)
d.rounded_rectangle([186, cy - h, 202, cy + h], radius=7, fill=ACCENT)

centered(d, "SKIP", font(44), 176, TEXT)
centered(d, "INTRO", font(26), 218, ACCENT)

img.save("icon.png", "PNG")
print("wrote icon.png", img.size)
