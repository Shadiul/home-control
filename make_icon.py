"""Draws the app icon (a light bulb on a warm tile) as assets/icon.ico and assets/icon.png."""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parent / "assets"
S = 1024  # draw large, then scale down for crisp small sizes


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def draw():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # Tile: vertical amber -> orange gradient with Windows 11-style rounded corners.
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    top, bottom = (255, 196, 84, 255), (240, 120, 40, 255)
    for y in range(S):
        gd.line([(0, y), (S, y)], fill=lerp(top, bottom, y / S))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=220, fill=255)
    img.paste(grad, (0, 0), mask)

    # Soft glow behind the bulb.
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([250, 150, 774, 674], fill=(255, 250, 220, 150))
    glow = glow.filter(ImageFilter.GaussianBlur(60))
    img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (S, S)), mask))

    d = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)
    # Bulb glass + neck.
    d.ellipse([312, 190, 712, 590], fill=white)
    d.polygon([(380, 520), (644, 520), (600, 680), (424, 680)], fill=white)
    # Screw base bands.
    base = (90, 50, 20, 230)
    for i, y in enumerate((700, 765)):
        inset = i * 14
        d.rounded_rectangle([420 + inset, y, 604 - inset, y + 48], radius=24, fill=base)
    d.rounded_rectangle([462, 830, 562, 868], radius=19, fill=base)
    # Filament.
    fil = (240, 140, 50, 255)
    d.line([(440, 470), (480, 400), (512, 470), (544, 400), (584, 470)], fill=fil, width=26, joint="curve")
    return img


def main():
    OUT.mkdir(exist_ok=True)
    img = draw()
    img.resize((256, 256), Image.LANCZOS).save(OUT / "icon.png")
    img.save(OUT / "icon.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48),
                                      (64, 64), (128, 128), (256, 256)])
    print("Wrote", OUT / "icon.ico")


if __name__ == "__main__":
    main()
