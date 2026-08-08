"""Generate the 256x256 icon Thunderstore requires for each package.

Thunderstore rejects packages whose icon is not exactly 256x256 PNG, so this
produces a plain, legible placeholder rather than leaving the slot empty.
Replace the output with real art whenever you have some.

usage: make-icon.py <label> <out.png> [motif] [bg] [fg]
  motif: "skip" (fast-forward chevrons) or "walk" (trail of steps, default)
"""
import sys
from PIL import Image, ImageDraw, ImageFont

SIZE = 256


def _font(size: int):
    for name in ("segoeuib.ttf", "arialbd.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def _draw_skip(draw: ImageDraw.ImageDraw, fg: str) -> None:
    """Two fast-forward chevrons plus a bar, centred low on the tile."""
    cy = 168
    for ox in (54, 122):
        draw.polygon([(ox, cy - 42), (ox + 62, cy), (ox, cy + 42)], fill=fg)
    draw.rectangle([200, cy - 42, 214, cy + 42], fill=fg)


def _draw_walk(draw: ImageDraw.ImageDraw, fg: str) -> None:
    """A diagonal trail of steps."""
    for i in range(7):
        x = 26 + i * 30
        y = 200 - i * 22
        draw.ellipse([x, y, x + 14, y + 14], fill=fg)


def build(label: str, out_path: str, motif: str, bg: str, fg: str) -> None:
    img = Image.new("RGB", (SIZE, SIZE), bg)
    draw = ImageDraw.Draw(img)

    if motif == "skip":
        _draw_skip(draw, fg)
    else:
        _draw_walk(draw, fg)

    font = _font(34)
    y = 22
    for line in label.split():
        draw.text((20, y), line.upper(), fill=fg, font=font)
        y += 38

    img.save(out_path, "PNG")


if __name__ == "__main__":
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    label = sys.argv[1]
    out = sys.argv[2]
    motif = sys.argv[3] if len(sys.argv) > 3 else "walk"
    bg = sys.argv[4] if len(sys.argv) > 4 else "#1d2b1f"
    fg = sys.argv[5] if len(sys.argv) > 5 else "#d8e8c0"
    build(label, out, motif, bg, fg)
    print(f"wrote {out}")
