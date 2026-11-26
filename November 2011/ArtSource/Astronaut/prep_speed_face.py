"""Pixelated face tile for the head-swapped astronaut, from a user-supplied front photo.

Run:  python prep_speed_face.py [preview_x4.png]     (needs Pillow)
Reads speed_reference.png (front-facing photo), writes SpeedFace_Tile.png (128x128) next to this
script; build_pixel_astro.py --variant speed maps it onto the front of the low-poly head.
"""
import sys
from pathlib import Path
from PIL import Image, ImageEnhance, ImageFilter, ImageOps

HERE = Path(__file__).resolve().parent
SRC = HERE / "speed_reference.png"
OUT = HERE / "SpeedFace_Tile.png"
TILE = 64                    # low resolution on purpose (chunky PS1 pixels)
COLORS = 64
# Square around the face in speed_reference.png (centre x, centre y, half size), from hairline to chin.
FACE_BOX = (520, 525, 295)


def main():
    src = Image.open(SRC).convert("RGB")
    cx, cy, h = FACE_BOX
    crop = src.crop((cx - h, cy - h, cx + h, cy + h))
    crop = ImageOps.autocontrast(crop, cutoff=1)
    crop = crop.filter(ImageFilter.UnsharpMask(radius=4, percent=120, threshold=2))
    crop = ImageEnhance.Contrast(crop).enhance(1.12)
    face = crop.resize((TILE, TILE), Image.LANCZOS)
    face = face.filter(ImageFilter.UnsharpMask(radius=1, percent=80, threshold=1))
    face = face.quantize(colors=COLORS, method=Image.Quantize.MEDIANCUT,
                         dither=Image.Dither.NONE).convert("RGB")      # smooth colour areas, no speckle
    face.save(OUT)
    print("wrote", OUT)
    if len(sys.argv) > 1:
        face.resize((TILE * 4, TILE * 4), Image.NEAREST).save(sys.argv[1])


if __name__ == "__main__":
    main()
