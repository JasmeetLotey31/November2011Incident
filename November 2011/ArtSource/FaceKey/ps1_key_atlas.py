"""Make the pixelated face tile for the key (the detailed face, original colours, no frame).

Run:  python ps1_key_atlas.py [preview_x4.png]      (needs Pillow)
Writes ArtSource/FaceKey/FaceKeyPS1_FaceTile.png (128x128). build_pixel_key.py (Blender) puts it
into the key's texture atlas next to the pixel-art body texture it paints from the geometry.
"""
import sys
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageOps

HERE = Path(__file__).resolve().parent
OUT = HERE / "FaceKeyPS1_FaceTile.png"

TILE = 64                     # face resolution in pixels - low on purpose (chunky PS1 pixels)
FACE_COLORS = 64              # limited palette, dithered
FACE_C, FACE_R = (630, 231), (122, 112)      # face oval in reference.png
FACE_HALF = 116                              # square around it that fills the whole disc front
HOLE_C, HOLE_R = (630, 92), 44               # key-ring hole in the photo (filled in)
OLD_GOLD = (214, 158, 48)                    # warm tint for the "photo" style
FACE_STYLE = "gold"          # "gold": face recoloured with the key's gold ramp; "photo": original colours
# Same gold ramp as the key body (build_pixel_key.py), dark -> bright: shadows -> highlights.
GOLD_RAMP = [(70, 20, 10), (108, 32, 16), (198, 66, 32), (230, 118, 36),
             (250, 162, 40), (250, 194, 68), (250, 226, 108), (255, 246, 190)]


def gradient_map(img, ramp):
    """Recolour by brightness: each pixel's luminance picks a colour along the ramp."""
    lum = ImageOps.autocontrast(img.convert("L"), cutoff=1)
    lut = []
    for v in range(256):
        f = v / 255 * (len(ramp) - 1)
        i = min(int(f), len(ramp) - 2)
        t = f - i
        lut.append(tuple(int(a + (b - a) * t) for a, b in zip(ramp[i], ramp[i + 1])))
    out = Image.new("RGB", img.size)
    lp, op = lum.load(), out.load()
    for y in range(img.size[1]):
        for x in range(img.size[0]):
            op[x, y] = lut[lp[x, y]]
    return out


def fill_outside(img, keep):
    """Replace pixels outside `keep` with the face's own nearby colours (cheap inpaint)."""
    out = img.copy()
    for radius in (6, 14, 30):
        blur_img = Image.composite(out, Image.new("RGB", out.size), keep).filter(ImageFilter.GaussianBlur(radius))
        blur_w = keep.filter(ImageFilter.GaussianBlur(radius))
        bp, wp, op, kp = blur_img.load(), blur_w.load(), out.load(), keep.load()
        for y in range(out.size[1]):
            for x in range(out.size[0]):
                if not kp[x, y] and wp[x, y] > 8:
                    op[x, y] = tuple(min(255, int(c * 255 / wp[x, y])) for c in bp[x, y])
        keep = ImageChops.lighter(keep, blur_w.point(lambda v: 255 if v > 8 else 0))
    return out


def face_tile(n):
    src = Image.open(HERE / "reference.png").convert("RGB")
    cx, cy = FACE_C
    ox, oy = cx - FACE_HALF, cy - FACE_HALF
    crop = src.crop((ox, oy, cx + FACE_HALF, cy + FACE_HALF))
    keep = Image.new("L", crop.size, 0)
    d = ImageDraw.Draw(keep)
    d.ellipse((FACE_C[0] - FACE_R[0] + 4 - ox, FACE_C[1] - FACE_R[1] + 4 - oy,
               FACE_C[0] + FACE_R[0] - 4 - ox, FACE_C[1] + FACE_R[1] - 4 - oy), fill=255)
    d.ellipse((HOLE_C[0] - HOLE_R - ox, HOLE_C[1] - HOLE_R - oy,
               HOLE_C[0] + HOLE_R - ox, HOLE_C[1] + HOLE_R - oy), fill=0)
    crop = fill_outside(crop, keep)
    crop = ImageOps.autocontrast(crop, cutoff=1)
    crop = crop.filter(ImageFilter.UnsharpMask(radius=4, percent=140, threshold=2))
    crop = ImageEnhance.Contrast(crop).enhance(1.15)
    face = crop.resize((n, n), Image.LANCZOS)
    face = face.filter(ImageFilter.UnsharpMask(radius=1, percent=90, threshold=1))
    if FACE_STYLE == "gold":
        face = gradient_map(face, GOLD_RAMP)
    else:
        face = Image.blend(face, Image.new("RGB", face.size, OLD_GOLD), 0.10)
    return face.quantize(colors=FACE_COLORS, method=Image.Quantize.MEDIANCUT,
                         dither=Image.Dither.NONE).convert("RGB")      # smooth colour, no speckle


FACE_SCALE = 0.80            # face size as a fraction of the disc (rest is plain gold)


def place_on_gold(face, n):
    """Put the (smaller) face in the middle of a gold disc; its edge blends softly into the gold
    so there's no ring. Gold: smooth shading, lighter at the top-left, darker at the bottom-right."""
    def lerp(a, b, t):
        return tuple(int(x + (y - x) * t) for x, y in zip(a, b))
    out = Image.new("RGB", (n, n))
    op = out.load()
    for y in range(n):
        for x in range(n):
            f = 1 - (0.65 * y + 0.35 * x) / (n - 1)     # 1 top-left -> 0 bottom-right
            op[x, y] = lerp(GOLD_RAMP[3], GOLD_RAMP[5], f)
    s = face.size[0]
    o = (n - s) // 2
    fp = face.load()
    for y in range(s):
        for x in range(s):
            r = (((x + 0.5) / s - 0.5) ** 2 + ((y + 0.5) / s - 0.5) ** 2) ** 0.5 * 2   # 0 centre, 1 edge
            fade = min(1.0, max(0.0, (1.0 - r) / 0.18))                             # soft edge
            if fade > 0:
                op[x + o, y + o] = lerp(op[x + o, y + o], fp[x, y], fade)
    return out


def main():
    if FACE_STYLE == "gold" and FACE_SCALE < 1:
        tile = place_on_gold(face_tile(round(TILE * FACE_SCALE)), TILE)
    else:
        tile = face_tile(TILE)
    tmp = OUT.with_name(OUT.stem + ".tmp.png")     # write then swap: works if a viewer has OUT open
    tile.save(tmp)
    try:
        tmp.replace(OUT)
    except OSError:
        OUT.unlink(missing_ok=True)
        tmp.rename(OUT)
    print("wrote", OUT)
    if len(sys.argv) > 1:
        tile.resize((TILE * 4, TILE * 4), Image.NEAREST).save(sys.argv[1])


if __name__ == "__main__":
    main()
