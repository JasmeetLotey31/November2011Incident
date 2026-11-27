"""Pixel-art astronaut, same art style as the key (ArtSource/FaceKey/build_pixel_key.py).
Never modifies Astro.blend.

  blender -b Astro.blend --python build_pixel_astro.py -- --variant astro|speed [--preview <folder>]

astro  the astronaut as modelled, with spacesuit details painted in: chest control panel with
       status lights, ribbed hoses, belt + buckle, knee/elbow rings, wrist cuffs, gloves, boots,
       fold seams, shoulder mission patch, backpack vents, gold visor with a reflection streak
freed  speed: same body, helmet + backpack removed (holes patched), new low-poly head with ears
       and twisted hair; the front of the head shows SpeedFace_Tile.png (prep_speed_face.py)

Painting rules (both): pixel-art light from the top-left as seen from the front (back lit like
the front), Bayer-dithered tone steps, 1-px dark outlines on big creases and where colours meet,
2-px bright bevels on lit creases, a few glints. Warm palette taken from the key texture.
"""
import math
import random
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
MODELS = HERE.parent.parent / "Assets" / "Models"

W, H = 256, 256           # low resolution on purpose (chunky PS1 pixels)
PAD = 1
SHARP_DEG = 70             # only really sharp edges get a (soft) outline
OUTLINE_STRENGTH = 0.4     # how much an outline darkens/lightens the colour under it (0..1)
NOISE_SCALE = 9.0          # size of the hand-painted light/dark blotches (bigger = smaller blotches)
NOISE_AMOUNT = 0.45        # how strong those blotches are, in tone steps
TONE_STEPS = 3             # soft steps between palette tones (cel-painted look, no dither)
GLINTS = False
FOLD_DEG = 40
ISLAND_DEG = 55
LIGHT = Vector((0.45, -0.55, 0.7)).normalized()     # model faces +X; screen-left is -Y
EDGE_LIGHT = Vector((0, -1, 1)).normalized()
GRADIENT_PER_M = 1.2
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0

# ---- palette (6 tones each, dark -> bright): parody "NASTY" space-agency suit
# white suit, grey/silver hardware, blue + red accents, gold visor only
SUIT = [(52, 56, 66), (104, 110, 122), (160, 166, 176), (208, 212, 218), (232, 235, 239), (250, 251, 253)]
METAL = [(36, 40, 46), (74, 80, 90), (118, 124, 134), (162, 168, 178), (200, 204, 212), (234, 236, 240)]
BLUE = [(10, 20, 60), (18, 40, 110), (30, 66, 160), (52, 96, 200), (90, 134, 226), (150, 180, 240)]
RED = [(70, 10, 10), (130, 20, 20), (190, 34, 34), (222, 56, 48), (240, 96, 84), (252, 150, 140)]
GOLD = [(70, 20, 10), (150, 60, 22), (214, 120, 36), (246, 170, 44), (250, 208, 84), (255, 240, 170)]  # visor
DARK = [(18, 20, 24), (36, 40, 46), (58, 62, 70), (86, 90, 100), (118, 122, 132), (152, 156, 166)]
PACK = [(58, 62, 70), (110, 116, 126), (164, 170, 180), (206, 210, 216), (228, 231, 236), (246, 247, 250)]
AGENCY = "NASTY"            # parody agency name (wordmark on the backpack)
SKIN = [(36, 18, 12), (70, 38, 24), (106, 60, 38), (138, 84, 56), (170, 112, 78), (204, 150, 110)]
HAIR = [(8, 6, 6), (18, 13, 11), (30, 22, 18), (46, 34, 28), (64, 48, 38), (88, 68, 52)]
SPACE = (18, 24, 52)
LEDS = [(255, 48, 36), (70, 230, 90), (255, 214, 48), (70, 150, 255)]
GLINT = (255, 255, 255)

SLOT_BACKPACK, SLOT_SUIT, SLOT_VISOR, SLOT_SKIN, SLOT_HAIR = 0, 1, 2, 3, 4

# ---- body landmarks (metres, measured on Astro.blend)
NECK_Z = 2.01
HEAD = dict(c=Vector((-0.17, 0.0, 2.235)), r=Vector((0.165, 0.15, 0.195)))
FACE_BOX = (-0.155, 0.155, 2.045, 2.43)              # y0, y1, z0, z1 of the face image on the head
FACE_RECT = (0, 192, 64, 64)                         # atlas x, y (from bottom), w, h
SEGMENTS_SPEED = [(64, 192, 192, 64), (0, 0, 256, 192)]
SEGMENTS_ASTRO = [(0, 0, 256, 256)]
DECALS = 0                                           # random small patches/plates scattered on the suit (off)
WAIST_CONNECTORS = False                             # red/blue hose connectors at the waist
DECAL_SEED = 11


# ============================================================ painting helpers
def _hash(a, b, c):
    n = (a * 374761393 + b * 668265263 + c * 1274126177) & 0xFFFFFFFF
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    return (n & 0xFFFF) / 32767.5 - 1.0


def vnoise(p, scale):
    """Smooth value noise in 3D (-1..1): soft, irregular blotches like loose brushwork."""
    x, y, z = p.x * scale, p.y * scale, p.z * scale
    xi, yi, zi = math.floor(x), math.floor(y), math.floor(z)
    fx, fy, fz = x - xi, y - yi, z - zi
    sx, sy, sz = (t * t * (3 - 2 * t) for t in (fx, fy, fz))
    def lerp(a, b, t): return a + (b - a) * t
    c = [[[_hash(xi + i, yi + j, zi + k) for k in (0, 1)] for j in (0, 1)] for i in (0, 1)]
    return lerp(lerp(lerp(c[0][0][0], c[0][0][1], sz), lerp(c[0][1][0], c[0][1][1], sz), sy),
                lerp(lerp(c[1][0][0], c[1][0][1], sz), lerp(c[1][1][0], c[1][1][1], sz), sy), sx)


def shade(ramp, f):
    """Colour at tone f (0..5) along a ramp: smooth blend in a few soft steps, no dither."""
    f = min(5.0, max(0.0, f))
    i = min(4, int(f))
    t = round((f - i) * TONE_STEPS) / TONE_STEPS
    return tuple(a + (b - a) * t for a, b in zip(ramp[i], ramp[i + 1]))


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


# ============================================================ mesh
def leave_edit_mode():
    for o in bpy.data.objects:
        if o.mode != "OBJECT":
            with bpy.context.temp_override(active_object=o, object=o):
                bpy.ops.object.mode_set(mode="OBJECT")


def prepare(ob):
    ob.data = ob.data.copy()
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    me = ob.data
    while len(me.materials) < 5:
        me.materials.append(None)
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    return me


def add_sphere(bm, centre, radii, segs, rings, slot):
    geom = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    verts = geom["verts"]
    bmesh.ops.transform(bm, matrix=Matrix.Diagonal((*radii, 1.0)), verts=verts)
    bmesh.ops.translate(bm, vec=centre, verts=verts)
    faces = list({f for v in verts for f in v.link_faces})
    for f in faces:
        f.material_index = slot
    return verts, faces


HEAD_STYLE = "lowpoly"   # "lowpoly": sculpted pieces. "photo" (silhouette loft from 2 photos) was tried and
                         # doesn't produce a usable realistic head - use a FaceBuilder-type tool for that.
# Photo head: landmarks measured on speed_side.png (2048x2048, faces right) and speed_front.png.
PHOTO = dict(
    side="speed_side.png", front="speed_front.png",
    side_eye_y=1033, side_chin_y=1778, side_top_y=150, side_ear_x=800,
    front_eye_y=345, front_chin_y=487, front_center_x=163, front_rows=(165, 487), front_cols=(18, 318),
    m_per_px=2.35e-4,       # side photo pixel -> metres on the model (eye-to-chin 0.175 m)
    chin_z=2.08, ear_x=-0.20,
    nose_rows=(1060, 1260), lip_rows=(1380, 1600), chin_rows=(1600, 1790),
)
PHOTO_RINGS, PHOTO_SEGS = 72, 48         # head mesh density (~6,900 triangles, PS3/PS4-ish)
HEAD_TEXEL_BOOST = 10                    # head gets this many times the suit's texture detail
PHOTO_ATLAS = 1024                       # atlas size for the photo-head model (smooth-filtered)
PHOTO_DATA = None                        # filled by load_photos()
HEAD_SEGMENTS, HEAD_RINGS = 20, 14      # head density (low-poly)
HAIR_TWISTS = 44                         # stubby twists on the hair cap
# Face landmarks in face-photo space (u: 0 left -> 1 right, v: 0 top -> 1 bottom), measured on
# speed_reference.png inside prep_speed_face.FACE_BOX.
# Soft shaping pushed into the head itself: (u, v, width, height, depth in m)
FACE_RELIEF = [
    (0.30, 0.40, 0.09, 0.055, -0.022),   # left eye socket
    (0.66, 0.40, 0.09, 0.055, -0.022),   # right eye socket
    (0.24, 0.57, 0.10, 0.09, 0.014),     # left cheekbone
    (0.72, 0.57, 0.10, 0.09, 0.014),     # right cheekbone
]
# Features built as separate low-poly pieces on the face (see add_face_features)
NOSE = dict(bridge=(0.48, 0.40), tip=(0.48, 0.565), wing_l=(0.39, 0.60), wing_r=(0.58, 0.60),
            base=(0.48, 0.625), tip_out=0.058, wing_out=0.02, bridge_out=0.012)
LIPS = [((0.49, 0.675), (0.20, 0.035), 0.030),     # upper lip: centre, (width, height) in u/v, depth m
        ((0.49, 0.728), (0.22, 0.045), 0.036)]     # lower lip (fuller)
BROW = ((0.48, 0.335), (0.46, 0.035), 0.022)
CHIN = ((0.49, 0.905), (0.18, 0.08), 0.022)


# ------------------------------------------------------------ photo-built head
def _load_rgb(path):
    img = bpy.data.images.load(str(path))
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)[::-1, :, :3] * 255.0           # rows top -> bottom, like the photo


def _close(mask, it=3):
    """Fill small holes/gaps in a mask (dilate then erode)."""
    m = mask.copy()
    for _ in range(it):
        m = m | np.roll(m, 1, 0) | np.roll(m, -1, 0) | np.roll(m, 1, 1) | np.roll(m, -1, 1)
    for _ in range(it):
        m = m & np.roll(m, 1, 0) & np.roll(m, -1, 0) & np.roll(m, 1, 1) & np.roll(m, -1, 1)
    return m


def load_photos():
    global PHOTO_DATA
    P = PHOTO
    side = _load_rgb(HERE / P["side"])
    front = _load_rgb(HERE / P["front"])
    side_mask = _close(side.max(axis=2) > 14)                  # black background
    r, g, b = front[:, :, 0], front[:, :, 1], front[:, :, 2]
    front_mask = ~((r > 140) & (g < 95) & (b < 95))            # red background
    front_mask[:, :P["front_cols"][0]] = False
    front_mask[:, P["front_cols"][1]:] = False
    front_mask = _close(front_mask, 2)
    k = (P["side_chin_y"] - P["side_eye_y"]) / (P["front_chin_y"] - P["front_eye_y"])
    neck = side[1560:1720, 950:1180].reshape(-1, 3).mean(axis=0)
    PHOTO_DATA = dict(side=side, front=front, side_mask=side_mask, front_mask=front_mask, k=k, neck=neck)
    return PHOTO_DATA


def _side_y(z):
    P = PHOTO
    return P["side_chin_y"] - (z - P["chin_z"]) / P["m_per_px"]


def build_photo_head(bm):
    """Loft a head through horizontal slices: front/back edge from the side photo's silhouette
    (so forehead, nose, lips and chin follow the real profile), width from the front photo."""
    P, D = PHOTO, PHOTO_DATA
    s, k = P["m_per_px"], D["k"]
    rows = np.linspace(P["side_top_y"] + 6, P["side_chin_y"], PHOTO_RINGS)
    fronts, backs, widths = [], [], []
    last_w = None
    for py in rows:
        cols = np.nonzero(D["side_mask"][int(py)])[0]
        fx = cols.max() if len(cols) else P["side_ear_x"]
        bx = cols.min() if len(cols) else P["side_ear_x"]
        fronts.append(P["ear_x"] + (fx - P["side_ear_x"]) * s)
        backs.append(P["ear_x"] + (bx - P["side_ear_x"]) * s)
        fy = P["front_eye_y"] + (py - P["side_eye_y"]) / k
        w = None
        if P["front_rows"][0] <= fy <= P["front_rows"][1]:
            fc = np.nonzero(D["front_mask"][int(fy)])[0]
            if len(fc):
                w = (fc.max() - fc.min()) / 2 * k * s
        widths.append(w)
    # rows above the front photo's coverage: taper the width toward the top of the hair
    first = next(i for i, w in enumerate(widths) if w is not None)
    for i in range(first):
        widths[i] = widths[first] * (0.55 + 0.45 * i / max(1, first))
    widths = [w if w is not None else widths[first] for w in widths]
    fronts = np.array(fronts)
    base = np.convolve(np.pad(fronts, 4, mode="edge"), np.ones(9) / 9, mode="valid")  # broad face shape
    feat = np.clip(fronts - base, -0.01, None)                                         # nose, lips, chin...

    def sigma(py):
        if P["nose_rows"][0] <= py <= P["nose_rows"][1]:
            return 0.016
        if P["lip_rows"][0] <= py <= P["lip_rows"][1]:
            return 0.03
        if P["chin_rows"][0] <= py <= P["chin_rows"][1]:
            return 0.04
        return 0.07

    rings = []
    for j, py in enumerate(rows):
        z = P["chin_z"] + (P["side_chin_y"] - py) * s
        xb, xf = backs[j], base[j]
        mid = (xf + xb) / 2
        af, ab, w = xf - mid, mid - xb, widths[j]
        ring = []
        for i in range(PHOTO_SEGS):
            t = 2 * math.pi * i / PHOTO_SEGS
            sn, cs = math.sin(t), math.cos(t)
            sp = math.copysign(abs(sn) ** 0.85, sn)
            cp = math.copysign(abs(cs) ** 0.85, cs)
            y = w * sp
            x = mid + (af if cs >= 0 else ab) * cp
            if cs > 0:
                x += feat[j] * math.exp(-(y / sigma(py)) ** 2) * cs
            ring.append(bm.verts.new((x, y, z)))
        rings.append(ring)
    faces = []
    for a, b in zip(rings, rings[1:]):
        for i in range(PHOTO_SEGS):
            n = (i + 1) % PHOTO_SEGS
            faces.append(bm.faces.new((a[i], a[n], b[n], b[i])))
    top = bm.verts.new(sum((v.co for v in rings[0]), Vector()) / PHOTO_SEGS + Vector((0, 0, 0.004)))
    bot = bm.verts.new(sum((v.co for v in rings[-1]), Vector()) / PHOTO_SEGS)
    for i in range(PHOTO_SEGS):
        n = (i + 1) % PHOTO_SEGS
        faces.append(bm.faces.new((top, rings[0][n], rings[0][i])))
        faces.append(bm.faces.new((bot, rings[-1][i], rings[-1][n])))
    for f in faces:
        f.material_index = SLOT_SKIN
    print(f"photo head: {len(faces)} faces, height {rows[-1] - rows[0]:.0f}px, "
          f"depth {max(fronts) - min(backs):.3f} m, width {2 * max(widths):.3f} m")


def _sample(img, mask, x, y):
    h, w = mask.shape
    xi, yi = int(x), int(y)
    if 0 <= xi < w and 0 <= yi < h and mask[yi, xi]:
        return img[yi, xi]
    return None


def photo_color(pos, n):
    """Colour of the head at `pos` by projecting the front and side photos, blended by angle."""
    P, D = PHOTO, PHOTO_DATA
    s, k = P["m_per_px"], D["k"]
    sy = _side_y(pos.z)
    sx = P["side_ear_x"] + (pos.x - P["ear_x"]) / s
    fy = P["front_eye_y"] + (sy - P["side_eye_y"]) / k
    fx = P["front_center_x"] + pos.y / (s * k)
    cs = _sample(D["side"], D["side_mask"], sx, sy)
    cf = _sample(D["front"], D["front_mask"], fx, fy) if P["front_rows"][0] <= fy <= P["front_rows"][1] else None
    wf = max(0.0, n.x) ** 2
    if cf is None:
        wf = 0.0
    if cs is None:
        if cf is None:
            return tuple(D["neck"])
        return tuple(cf)
    if cf is None:
        return tuple(cs)
    return tuple(cf * wf + cs * (1 - wf))


def face_relief(u, w):
    """How far (m) the head surface itself pushes in/out at photo position (u, w)."""
    return sum(depth * math.exp(-((u - cu) / su) ** 2 - ((w - cv) / sv) ** 2)
               for cu, cv, su, sv, depth in FACE_RELIEF)


def face_point(u, w, out):
    """3D point on (or `out` metres in front of) the front of the head at photo position (u, w)."""
    c, r = HEAD["c"], HEAD["r"]
    y0, y1, z0, z1 = FACE_BOX
    y = y0 + u * (y1 - y0)
    z = z1 - w * (z1 - z0)
    dy, dz = (y - c.y) / r.y, (z - c.z) / r.z
    t = max(0.0, -(z - c.z) / r.z)
    dy /= (1 - 0.28 * t)                                   # same jaw narrowing as the head
    x = c.x + r.x * math.sqrt(max(0.05, 1 - dy * dy - dz * dz)) * (1 - 0.15 * t) * 0.9
    return Vector((x - 0.006 + out, y, z))                 # sink 6 mm in so pieces meet the head


def add_face_features(bm):
    """Nose, lips, brow and chin as their own low-poly pieces, so they're properly 3D."""
    def piece(points, tris):
        vs = [bm.verts.new(p) for p in points]
        for t in tris:
            f = bm.faces.new([vs[i] for i in t])
            f.material_index = SLOT_SKIN
    # nose: bridge -> tip ridge, two sloping sides, nostril wings, underside
    n = NOSE
    pts = [face_point(*n["bridge"], n["bridge_out"]),                    # 0 bridge
           face_point(*n["tip"], n["tip_out"]),                          # 1 tip
           face_point(*n["wing_l"], n["wing_out"]),                      # 2 left wing
           face_point(*n["wing_r"], n["wing_out"]),                      # 3 right wing
           face_point(n["base"][0], n["base"][1], n["tip_out"] * 0.45),  # 4 under the tip
           face_point(n["bridge"][0] - 0.05, n["bridge"][1] + 0.03, 0),  # 5 bridge side L
           face_point(n["bridge"][0] + 0.05, n["bridge"][1] + 0.03, 0),  # 6 bridge side R
           face_point(n["wing_l"][0] - 0.03, n["wing_l"][1] - 0.03, 0),  # 7 cheek L
           face_point(n["wing_r"][0] + 0.03, n["wing_r"][1] - 0.03, 0)]  # 8 cheek R
    piece(pts, [(0, 5, 1), (5, 7, 1), (7, 2, 1), (0, 1, 6), (6, 1, 8), (8, 1, 3), (2, 4, 1), (4, 3, 1),
                (7, 4, 2), (4, 8, 3)])
    # lips, brow, chin: tapered low-poly blocks hugging the face
    def block(centre, size, depth, taper=0.75):
        (cu, cv), (su, sv) = centre, size
        back = [face_point(cu - su / 2, cv - sv / 2, 0), face_point(cu + su / 2, cv - sv / 2, 0),
                face_point(cu + su / 2, cv + sv / 2, 0), face_point(cu - su / 2, cv + sv / 2, 0)]
        mid = [face_point(cu - su * taper / 2, cv - sv * 0.35, depth), face_point(cu + su * taper / 2, cv - sv * 0.35, depth),
               face_point(cu + su * taper / 2, cv + sv * 0.35, depth), face_point(cu - su * taper / 2, cv + sv * 0.35, depth)]
        piece(back + mid, [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 6, 7)])
    for centre, size, depth in LIPS:
        block(centre, size, depth, taper=0.8)
    block(*BROW, taper=0.9)
    block(*CHIN, taper=0.6)


def make_speed(me, rnd):
    """Remove helmet + backpack, patch the holes, add a low-poly head, ears and hair."""
    bm = bmesh.new()
    bm.from_mesh(me)
    # everything of the helmet (any face reaching above the collar), the backpack and its connector
    kill = [f for f in bm.faces if f.material_index == SLOT_BACKPACK
            or f.calc_center_median().z > NECK_Z - 0.025
            or max(v.co.z for v in f.verts) > NECK_Z + 0.01
            or (f.material_index == SLOT_VISOR and f.calc_center_median().x < -0.45)]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    filled = bmesh.ops.holes_fill(bm, edges=edges, sides=0)["faces"]
    for f in filled:
        f.material_index = SLOT_SUIT
    bmesh.ops.triangulate(bm, faces=filled)

    c, r = HEAD["c"], HEAD["r"]
    # neck
    neck = bmesh.ops.create_cone(bm, cap_ends=False, segments=8, radius1=0.09, radius2=0.085, depth=0.18)
    bmesh.ops.translate(bm, vec=Vector((c.x + 0.01, 0, NECK_Z + 0.02)), verts=neck["verts"])
    for f in {f for v in neck["verts"] for f in v.link_faces}:
        f.material_index = SLOT_SKIN
    if HEAD_STYLE == "photo":                 # head + hair built from the photos (no ears/cap/twists)
        build_photo_head(bm)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
        return
    # head: low-poly sculpt - egg shape, narrower jaw, flatter back, and real 3D face features
    verts, _ = add_sphere(bm, c, r, HEAD_SEGMENTS, HEAD_RINGS, SLOT_SKIN)
    y0, y1, z0, z1 = FACE_BOX
    for v in verts:
        d = v.co - c
        t = max(0.0, -d.z / r.z)                              # 0 at middle -> 1 at chin
        v.co.y = c.y + d.y * (1 - 0.28 * t)
        if d.x < 0:
            v.co.x = c.x + d.x * 0.9
        if d.x > 0:
            v.co.x = c.x + d.x * (1 - 0.15 * t) * 0.9         # flatter face plane
            # face features, placed in the same (u, v) space as the face photo on the head
            u = (v.co.y - y0) / (y1 - y0)
            w = (z1 - v.co.z) / (z1 - z0)
            front = min(1.0, max(0.0, d.x / r.x)) ** 1.5      # only on the front of the head
            v.co.x += front * face_relief(u, w)
    add_face_features(bm)
    # ears
    for side in (-1, 1):
        ear = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.transform(bm, matrix=Matrix.Diagonal((0.04, 0.025, 0.07, 1)), verts=ear["verts"])
        bmesh.ops.translate(bm, vec=Vector((c.x - 0.01, side * (r.y * 0.98), c.z - 0.01)), verts=ear["verts"])
        for f in {f for v in ear["verts"] for f in v.link_faces}:
            f.material_index = SLOT_SKIN
    # hair: a cap over the top/back with twisted, uneven tufts
    # hairline just above the forehead, down to the ears at the sides, lower at the back
    hverts, hfaces = add_sphere(bm, c + Vector((-0.015, 0, 0.03)),
                                Vector((r.x * 1.05, r.y * 1.08, r.z * 0.98)), 12, 7, SLOT_HAIR)
    cut = [f for f in hfaces if (f.calc_center_median().x > c.x + 0.02 and f.calc_center_median().z < c.z + 0.075)
           or f.calc_center_median().z < c.z - 0.06]
    bmesh.ops.delete(bm, geom=cut, context="FACES")
    for v in [v for v in hverts if v.is_valid and v.link_faces]:
        out = (v.co - c).normalized()
        v.co += out * rnd.uniform(0.004, 0.02)                 # uneven surface
    # pull the lowest ring of the cap in against the head so it hugs it instead of a brim
    rim = [v for v in hverts if v.is_valid and v.link_faces and len(v.link_faces) < 4]
    for v in rim:
        d = v.co - c
        v.co = c + Vector((d.x * 0.9, d.y * 0.92, d.z))
    # stubby low-poly twists on the cap, leaning down and out like short hair twists
    cap = [f for f in hfaces if f.is_valid]
    for f in rnd.sample(cap, min(HAIR_TWISTS, len(cap))):
        base = f.calc_center_median()
        nrm = (f.normal + Vector((0, 0, -0.35))).normalized()
        tw = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=rnd.uniform(0.022, 0.03),
                                   radius2=rnd.uniform(0.014, 0.02), depth=rnd.uniform(0.04, 0.065))
        rot = nrm.to_track_quat("Z", "Y").to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=rot, verts=tw["verts"])
        bmesh.ops.translate(bm, vec=base + nrm * 0.012, verts=tw["verts"])
        for g in {g for v in tw["verts"] for g in v.link_faces}:
            g.material_index = SLOT_HAIR
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()


# ============================================================ UVs
def unwrap(me, face_polys, segments):
    uv = me.uv_layers.new(name="UVMap")
    verts = [v.co for v in me.vertices]
    rx, ry, rw, rh = FACE_RECT
    y0, y1, z0, z1 = FACE_BOX
    for i in face_polys:
        for li in me.polygons[i].loop_indices:
            c = verts[me.loops[li].vertex_index]
            s = (c.y - y0) / (y1 - y0)
            t = (c.z - z0) / (z1 - z0)
            uv.data[li].uv = ((rx + 0.5 + min(max(s, 0), 1) * (rw - 1)) / W,
                              (ry + 0.5 + min(max(t, 0), 1) * (rh - 1)) / H)

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    patch_of, groups = {}, []
    skip = set(face_polys)
    for seed in sorted((f for f in bm.faces if f.index not in skip), key=lambda f: -f.calc_area()):
        if seed.index in patch_of:
            continue
        gid = len(groups)
        group, stack = [], [seed]
        patch_of[seed.index] = gid
        while stack:
            f = stack.pop()
            group.append(f.index)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index in patch_of or g.index in skip or g.material_index != seed.material_index:
                        continue
                    if math.degrees(f.normal.angle(g.normal, 0)) > FOLD_DEG:
                        continue
                    if math.degrees(seed.normal.angle(g.normal, 0)) > ISLAND_DEG:
                        continue
                    patch_of[g.index] = gid
                    stack.append(g)
        groups.append(group)
    bm.free()

    patches = []
    boost = {}
    for gi, group in enumerate(groups):
        if HEAD_STYLE == "photo" and PHOTO_DATA is not None and me.polygons[group[0]].material_index == SLOT_SKIN:
            boost[gi] = HEAD_TEXEL_BOOST         # the photo head gets far more texture detail
    for group in groups:
        n = sum((me.polygons[i].normal * me.polygons[i].area for i in group), Vector())
        n = n.normalized() if n.length > 1e-9 else me.polygons[group[0]].normal
        t1 = n.cross(Vector((0, 0, 1)))
        if t1.length < 1e-4:
            t1 = n.cross(Vector((1, 0, 0)))
        t1.normalize()
        t2 = n.cross(t1).normalized()
        if t2.z < 0:
            t2, t1 = -t2, -t1
        vids = {v for i in group for v in me.polygons[i].vertices}
        co2 = {v: Vector((verts[v].dot(t1), verts[v].dot(t2))) for v in vids}
        lo = Vector((min(c.x for c in co2.values()), min(c.y for c in co2.values())))
        hi = Vector((max(c.x for c in co2.values()), max(c.y for c in co2.values())))
        patches.append((group, co2, lo, hi - lo))

    order = sorted(range(len(patches)), key=lambda k: -patches[k][3].y * boost.get(k, 1))
    scale = 1500.0
    while True:
        seg, x, y, shelf_h = 0, 0, 0, 0
        placed, ok = {}, True
        for k in order:
            size = patches[k][3] * boost.get(k, 1)
            w = max(2, math.ceil(size.x * scale)) + 2 * PAD
            h = max(2, math.ceil(size.y * scale)) + 2 * PAD
            while True:
                sx, sy, sw, sh = segments[seg]
                if x + w > sw:
                    x, y, shelf_h = 0, y + shelf_h, 0
                if y + h <= sh and w <= sw:
                    break
                seg, x, y, shelf_h = seg + 1, 0, 0, 0
                if seg >= len(segments):
                    ok = False
                    break
            if not ok:
                break
            placed[k] = (sx + x, sy + y)
            x += w
            shelf_h = max(shelf_h, h)
        if ok:
            break
        scale *= 0.95
    for k, (group, co2, lo, size) in enumerate(patches):
        px, py = placed[k]
        sc = scale * boost.get(k, 1)
        for i in group:
            for li in me.polygons[i].loop_indices:
                c = co2[me.loops[li].vertex_index]
                uv.data[li].uv = ((px + PAD + (c.x - lo.x) * sc) / W, (py + PAD + (c.y - lo.y) * sc) / H)
    print(f"UV texel density {scale:.0f} px/m"
          + (f" (head x{HEAD_TEXEL_BOOST} = {scale * HEAD_TEXEL_BOOST:.0f} px/m)" if boost else "")
          + f", {len(patches)} patches")
    return uv


# ============================================================ suit details (by 3D position)
def region_ramp(p, n, slot):
    if slot == SLOT_BACKPACK:
        return PACK
    if slot == SLOT_VISOR:
        return BLUE
    if slot == SLOT_SKIN:
        return SKIN
    if slot == SLOT_HAIR:
        return HAIR
    ay = abs(p.y)
    if p.z < 0.30:
        return METAL[:1] + METAL[:5]                           # boots (darker grey)
    if ay > 0.40 and p.z < 1.20:
        return METAL                                           # gloves
    if 1.20 <= p.z <= 1.28 and ay < 0.31:
        return DARK                                            # belt
    if 1.18 <= p.z < 1.24 and ay > 0.38:
        return BLUE                                            # wrist rings
    limb_leg = p.z < 1.2 and 0.08 < ay < 0.41
    limb_arm = 1.0 < p.z < 1.95 and ay > 0.33
    if (limb_leg and 0.66 <= p.z <= 0.75) or (limb_arm and 1.42 <= p.z <= 1.49):
        return BLUE                                            # knee / elbow bearing rings
    return SUIT


def _make_decals():
    """Random small patches/plates on the suit: (side, centre, half-size, colour ramp, kind).
    side: 'front'/'back' use (y, z); 'left'/'right' (outer arm/leg) use (x, z)."""
    rnd = random.Random(DECAL_SEED)
    spots = {  # where things can go: side, y-or-x range, z range
        "front": [((-0.26, 0.26), (1.32, 1.52)), ((-0.32, -0.12), (0.4, 1.1)), ((0.12, 0.32), (0.4, 1.1))],
        "back": [((-0.24, 0.24), (1.3, 1.9)), ((-0.32, -0.12), (0.4, 1.1)), ((0.12, 0.32), (0.4, 1.1))],
        "left": [((-0.30, -0.12), (1.25, 1.85)), ((-0.30, -0.10), (0.4, 1.0))],
        "right": [((-0.30, -0.12), (1.25, 1.85)), ((-0.30, -0.10), (0.4, 1.0))],
    }
    out = []
    for _ in range(DECALS):
        side = rnd.choice(["front", "front", "back", "left", "right"])
        (a0, a1), (z0, z1) = rnd.choice(spots[side])
        c = (rnd.uniform(a0, a1), rnd.uniform(z0, z1))
        half = (rnd.uniform(0.025, 0.05), rnd.uniform(0.02, 0.045))
        ramp = rnd.choice([METAL, METAL, SUIT, DARK, BLUE])
        kind = rnd.choice(["plate", "plate", "vent", "dot", "stripe"])
        out.append((side, c, half, ramp, kind))
    return out


DECAL_LIST = _make_decals()


def _side_of(p, n):
    if n.x > 0.35 and p.x > -0.15:
        return "front"
    if n.x < -0.35:
        return "back"
    if n.y < -0.4:
        return "left"
    if n.y > 0.4:
        return "right"
    return None


def _decal(p, n, tone):
    side = _side_of(p, n)
    if side is None:
        return None
    a = p.y if side in ("front", "back") else p.x
    for s, (ca, cz), (ha, hz), ramp, kind in DECAL_LIST:
        if s != side or abs(a - ca) > ha or abs(p.z - cz) > hz:
            continue
        u, v = (a - ca) / ha, (p.z - cz) / hz                 # -1..1 across the decal
        if kind == "dot":
            if u * u + v * v > 1:
                continue
            return shade(ramp, 1.2) if u * u + v * v > 0.45 else shade(ramp, 3.8 - v)
        if max(abs(u), abs(v)) > 0.78:                        # rim of a plate, lit top-left
            return shade(ramp, 4.4 if (v > 0.7 or u < -0.7) else 1.6)
        if kind == "vent" and int((v + 1) * 3) % 2:
            return shade(ramp, 1.4)
        if kind == "stripe" and abs(v) < 0.3:
            return RED[3] if ramp is not BLUE else SUIT[4]
        return shade(ramp, 3.0 + 0.5 * v)
    return None


# Chunky 5-px-tall pixel lettering (original), for the parody agency wordmark.
FONT = {
    "N": ["X..X", "XX.X", "X.XX", "X..X", "X..X"],
    "A": [".X.", "X.X", "XXX", "X.X", "X.X"],
    "S": [".XX", "X..", ".X.", "..X", "XX."],
    "T": ["XXX", ".X.", ".X.", ".X.", ".X."],
    "Y": ["X.X", "X.X", ".X.", ".X.", ".X."],
}
LETTER_PX = 0.019          # one letter pixel on the model, metres (~1 texel at 256 px)


def _wordmark(y, z, centre_z):
    """Red wordmark on the back of the backpack. Seen from behind, screen-right is -Y."""
    glyphs = [FONT[c] for c in AGENCY]
    width = sum(len(g[0]) for g in glyphs) + len(glyphs) - 1
    col = int(math.floor((width * LETTER_PX / 2 - y) / LETTER_PX))
    row = int(math.floor((centre_z + 2.5 * LETTER_PX - z) / LETTER_PX))
    if not (0 <= col < width and 0 <= row < 5):
        return None
    for g in glyphs:
        w = len(g[0])
        if col < w:
            return RED[3] if g[row][col] == "X" else None
        col -= w + 1
        if col < 0:
            return None
    return None


def detail(p, n, slot, tone, ramp, tx, ty):
    """Painted-on spacesuit details. Returns a colour or None. `tone` is the smooth tone (0..5).
    Sizes are chunky on purpose: at 256 px a pixel is ~2 cm on the suit."""
    ay = abs(p.y)
    front = n.x > 0.35 and p.x > -0.12
    if slot == SLOT_SUIT and front:
        # chest control panel
        if -0.14 <= p.y <= 0.14 and 1.57 <= p.z <= 1.81:
            if min(p.y + 0.14, 0.14 - p.y, p.z - 1.57, 1.81 - p.z) < 0.02:
                return DARK[1]
            for k, ly in enumerate((-0.085, -0.028, 0.028, 0.085)):  # status lights
                if abs(p.y - ly) < 0.02 and abs(p.z - 1.75) < 0.02:
                    return LEDS[k]
            for dy in (-0.07, 0.06):                                # two dials
                d = math.hypot(p.y - dy, p.z - 1.645)
                if d < 0.04:
                    return DARK[0] if d > 0.028 else METAL[5]
            return shade(DARK, 2.4 + (p.z - 1.69) * 4 + 0.4 * vnoise(p, 30))
        # name tag on the right chest
        if 0.16 <= p.y <= 0.27 and 1.66 <= p.z <= 1.74:
            if min(p.y - 0.16, 0.27 - p.y, p.z - 1.66, 1.74 - p.z) < 0.016:
                return DARK[2]
            return DARK[1] if (int(p.y * 55) % 3 == 0 and abs(p.z - 1.70) < 0.012) else SUIT[4]
        # zipper down the middle, below the panel
        if 1.29 <= p.z < 1.57 and ay < 0.014:
            return DARK[2] if int(p.z * 50) % 2 else DARK[3]
        # hose connectors either side of the waist
        for sy, col in ((((-0.23, BLUE), (0.23, RED))) if WAIST_CONNECTORS else ()):
            d = math.hypot(p.y - sy, p.z - 1.355)
            if d < 0.04:
                return METAL[1] if d > 0.026 else (col[3] if d > 0.012 else DARK[0])
        # thigh pockets with a flap
        if 0.85 <= p.z <= 1.07 and 0.14 <= ay <= 0.32:
            if min(ay - 0.14, 0.32 - ay, p.z - 0.85, 1.07 - p.z) < 0.018 or abs(p.z - 1.02) < 0.01:
                return shade(SUIT, 1.6)
            return shade(SUIT, tone - 0.4)
        # belt buckle
        if 1.20 <= p.z <= 1.28 and ay < 0.05:
            return METAL[2] if min(0.05 - ay, p.z - 1.20, 1.28 - p.z) < 0.016 else shade(METAL, 4.0 + (p.z - 1.24) * 20)
    if slot == SLOT_SUIT:
        dec = _decal(p, n, tone)
        if dec is not None:
            return dec
        # right-arm flag patch: blue corner, red/white stripes
        if n.y > 0.4 and p.y > 0.38 and abs(p.z - 1.72) < 0.05 and abs(p.x + 0.20) < 0.07:
            if abs(p.z - 1.72) > 0.04 or abs(p.x + 0.20) > 0.06:
                return METAL[2]
            if p.z > 1.72 and p.x > -0.20:                         # canton (towards the front)
                return BLUE[2] if (int(p.x * 70) + int(p.z * 70)) % 3 else SUIT[5]
            return RED[3] if int((p.z - 1.68) / 0.016) % 2 == 0 else SUIT[5]
        # scuffs and grime toward the feet and on the knees
        if p.z < 1.0 and vnoise(p, 14) > 0.35 - 0.25 * (1.0 - p.z):
            return shade(ramp, tone - 0.9)
        # left-shoulder "NASTY" parody agency badge: blue disc, white stars, red swoosh, orbit ring
        if n.y < -0.4 and p.y < -0.38:
            u, v = (p.x + 0.20) / 0.075, (p.z - 1.79) / 0.075
            d = math.hypot(u, v)
            if d < 1.0:
                if d > 0.84:
                    return METAL[4]                               # white rim
                if abs(v - (0.55 * u - 0.05) - 0.25 * u * u) < 0.16 and -0.85 < u < 0.85:
                    return RED[3] if v > 0.55 * u - 0.05 else RED[2]   # swoosh
                if abs(math.hypot(u * 0.6, v * 1.4) - 0.62) < 0.08:
                    return SUIT[5]                                # orbit ring
                if (int((u + 1) * 7) * 7 + int((v + 1) * 7) * 13) % 11 == 0:
                    return SUIT[5]                                # stars
                return shade(BLUE, 2.4 + 0.6 * v)
        # fold seams on arms and legs
        limb = (p.z < 1.2 and 0.08 < ay < 0.41 and p.z > 0.3) or (1.0 < p.z < 1.95 and ay > 0.33)
        if limb and (p.z % 0.16) < 0.02 and int((p.x + p.y) * 60) % 3:
            return shade(ramp, tone - 0.7)                             # stitched fold line
    if slot == SLOT_BACKPACK and n.x < -0.5:
        word = _wordmark(p.y, p.z, centre_z=2.12)                    # "NASTY" across the top
        if word is not None:
            return word
        if 1.45 < p.z < 1.98 and ay < 0.22 and (p.z % 0.08) < 0.025:  # vents
            return shade(PACK, tone - 1.5)
        if abs(p.z - 2.32) < 0.025 and abs(ay - 0.12) < 0.025:          # two lights
            return LEDS[0] if p.y < 0 else LEDS[1]
    if slot == SLOT_VISOR:
        if abs((p.y + 0.6 * (p.z - 2.22))) < 0.03 and n.x > 0.2:       # reflection streak
            return BLUE[5]
    if slot == SLOT_HAIR:
        return shade(HAIR, tone + 1.3 * vnoise(p, 55))                 # curly clumps, soft
    return None


# ============================================================ painting
def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (a + ab * t - p).length


def paint(me, uv, face_polys, face_tile, rnd, out_path):
    bm = bmesh.new()
    bm.from_mesh(me)
    sharp = set()
    for e in bm.edges:
        fs = e.link_faces
        if len(fs) != 2 or math.degrees(fs[0].normal.angle(fs[1].normal, 0)) > SHARP_DEG \
                or fs[0].material_index != fs[1].material_index:
            sharp.add(frozenset(v.index for v in e.verts))
    bm.free()

    atlas = np.zeros((H, W, 3), np.float64)
    atlas[:, :] = SUIT[3]
    filled = np.zeros((H, W), bool)
    reserved = np.zeros((H, W), bool)
    face_set = set(face_polys)
    if face_tile is not None:
        rx, ry, rw, rh = FACE_RECT
        reserved[ry:ry + rh, rx:rx + rw] = True
        # face oval from the photo, skin around it (photo corners show hair/background)
        tile = face_tile.copy()
        yy, xx = np.mgrid[0:rh, 0:rw] + 0.5
        inside = ((xx / rw - 0.5) / 0.44) ** 2 + ((yy / rh - 0.45) / 0.52) ** 2 <= 1
        for k in range(3):
            tile[:, :, k] = np.where(inside, tile[:, :, k], SKIN[3][k])
        atlas[ry:ry + rh, rx:rx + rw] = tile

    me.calc_loop_triangles()
    tris_by_poly = {}
    for t in me.loop_triangles:
        tris_by_poly.setdefault(t.polygon_index, []).append(t)

    for p in me.polygons:
        if p.index in face_set:
            continue
        slot = p.material_index
        n, centre = p.normal, p.center
        photo_head = PHOTO_DATA is not None and slot == SLOT_SKIN and centre.z > NECK_Z + 0.05
        back = n.x < 0
        mirror = (lambda v: Vector((-v.x, -v.y, v.z))) if back else (lambda v: v)
        base = 3 + (mirror(n).dot(LIGHT) - 0.45) * 2.6
        loops = list(p.loop_indices)
        edges = []
        for k in range(len(loops)):
            la, lb = loops[k], loops[(k + 1) % len(loops)]
            va, vb = me.loops[la].vertex_index, me.loops[lb].vertex_index
            if frozenset((va, vb)) not in sharp:
                continue
            a3, b3 = me.vertices[va].co, me.vertices[vb].co
            d = (b3 - a3).normalized()
            out = (a3 + b3) / 2 - centre
            out = (out - d * out.dot(d)).normalized()
            lit = mirror(out).dot(EDGE_LIGHT) > 0.2
            edges.append((Vector(uv.data[la].uv) * Vector((W, H)), Vector(uv.data[lb].uv) * Vector((W, H)), lit))
        texels = []
        for t in tris_by_poly.get(p.index, []):
            P = [me.vertices[me.loops[li].vertex_index].co for li in t.loops]
            NV = [me.vertices[me.loops[li].vertex_index].normal for li in t.loops]
            U = [Vector(uv.data[li].uv) * Vector((W, H)) for li in t.loops]
            den = (U[1].y - U[2].y) * (U[0].x - U[2].x) + (U[2].x - U[1].x) * (U[0].y - U[2].y)
            if abs(den) < 1e-9:
                continue
            x0, x1 = int(min(u.x for u in U)) - 1, int(max(u.x for u in U)) + 1
            y0, y1 = int(min(u.y for u in U)) - 1, int(max(u.y for u in U)) + 1
            for ty in range(max(0, y0), min(H, y1 + 1)):
                for tx in range(max(0, x0), min(W, x1 + 1)):
                    if reserved[ty, tx]:
                        continue
                    q = Vector((tx + 0.5, ty + 0.5))
                    w0 = ((U[1].y - U[2].y) * (q.x - U[2].x) + (U[2].x - U[1].x) * (q.y - U[2].y)) / den
                    w1 = ((U[2].y - U[0].y) * (q.x - U[2].x) + (U[0].x - U[2].x) * (q.y - U[2].y)) / den
                    w2 = 1 - w0 - w1
                    if min(w0, w1, w2) < -0.02:
                        continue
                    pos = P[0] * w0 + P[1] * w1 + P[2] * w2
                    if photo_head:                           # project the photos, smooth normal
                        nv = (NV[0] * w0 + NV[1] * w1 + NV[2] * w2).normalized()
                        atlas[ty, tx] = photo_color(pos, nv)
                        filled[ty, tx] = True
                        continue
                    f = (base + (pos.z - centre.z) * GRADIENT_PER_M
                         + NOISE_AMOUNT * vnoise(pos, NOISE_SCALE))
                    ramp = region_ramp(pos, n, slot)
                    col = detail(pos, n, slot, f, ramp, tx, ty) or shade(ramp, f)
                    if edges:
                        dist, lit = min((seg_dist(q, a, b), l) for a, b, l in edges)
                        if dist < 1.0:                               # soft 1-px edge, no black lines
                            col = mix(col, ramp[5] if lit else ramp[0],
                                      OUTLINE_STRENGTH * (0.6 if lit else 1.0))
                    atlas[ty, tx] = col
                    filled[ty, tx] = True
                    texels.append((tx, ty))
        if GLINTS and len(texels) > 400 and slot in (SLOT_SUIT, SLOT_BACKPACK, SLOT_VISOR) and rnd.random() < 0.3:
            tset = set(texels)
            gx, gy = texels[rnd.randrange(len(texels))]
            for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
                if (gx + dx, gy + dy) in tset:
                    atlas[gy + dy, gx + dx] = GLINT if (dx, dy) == (0, 0) else SUIT[5]

    for _ in range(PAD + 1):
        grown, src = filled.copy(), atlas.copy()
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            take = ~grown & ~reserved & np.roll(filled, (dy, dx), (0, 1))
            atlas[take] = np.roll(src, (dy, dx), (0, 1))[take]
            grown |= take
        filled = grown

    img = bpy.data.images.new(out_path.stem, W, H, alpha=False)
    img.pixels.foreach_set(np.dstack([atlas / 255.0, np.ones((H, W, 1))]).astype(np.float32).ravel())
    out_path.parent.mkdir(parents=True, exist_ok=True)
    img.filepath_raw = str(out_path)
    img.file_format = "PNG"
    img.save()
    return img


def load_tile(path):
    img = bpy.data.images.load(str(path))
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    assert (w, h) == FACE_RECT[2:], f"face tile must be {FACE_RECT[2]}x{FACE_RECT[3]}"
    return px.reshape(h, w, 4)[:, :, :3] * 255.0


def make_material(name, img):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 1.0
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.0
    mat.use_backface_culling = True
    return mat


def render_previews(ob, folder, tag, head=False):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    for o in sc.objects:
        o.hide_render = o is not ob
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.color_type = "FLAT", "TEXTURE"
    sh.show_backface_culling = True
    sc.render.resolution_x, sc.render.resolution_y = 600, 900
    pts = [ob.matrix_world @ v.co for v in ob.data.vertices]
    centre = sum(pts, Vector()) / len(pts)
    tgt = bpy.data.objects.new("T", None)
    sc.collection.objects.link(tgt)
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = "ORTHO"
    cam.constraints.new("TRACK_TO").target = tgt
    shots = [("front", (1, 0, 0), centre, 3.0), ("threeq", (0.7, -0.7, 0.25), centre, 3.0),
             ("back", (-1, 0.3, 0.1), centre, 3.0), ("chest", (1, -0.25, 0.1), Vector((-0.1, 0, 1.6)), 1.0)]
    if head:
        shots.append(("head", (1, -0.35, 0.1), HEAD["c"], 0.75))
    for name, d, aim, scale in shots:
        tgt.location = aim
        cam.data.ortho_scale = scale
        cam.location = aim + Vector(d).normalized() * 6
        sc.render.filepath = str(folder / f"{tag}_{name}.png")
        bpy.ops.render.render(write_still=True)
    if head:                                     # plain shaded: shows the 3D shape on its own
        sh.light, sh.color_type = "STUDIO", "SINGLE"
        for name, d in (("head_solid", (1, -0.45, 0.1)), ("head_profile", (0.05, -1, 0.05))):
            tgt.location = HEAD["c"]
            cam.data.ortho_scale = 0.6
            cam.location = HEAD["c"] + Vector(d).normalized() * 6
            sc.render.filepath = str(folder / f"{tag}_{name}.png")
            bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    variant = argv[argv.index("--variant") + 1] if "--variant" in argv else "astro"
    rnd = random.Random(3)
    leave_edit_mode()
    ob = next(o for o in bpy.data.objects if o.type == "MESH")
    me = prepare(ob)
    global SKIN, W, H
    if variant == "speed" and HEAD_STYLE == "photo":
        load_photos()
        make_speed(me, rnd)
        name, folder = "AstronautSpeed", MODELS / "AstronautSpeed"
        W = H = PHOTO_ATLAS
        face_polys, face_tile, segments = [], None, [(0, 0, W, H)]
        mid = PHOTO_DATA["neck"]                    # neck skin matches the photo
        SKIN = [tuple(int(min(255, c * k)) for c in mid) for k in (0.32, 0.52, 0.72, 0.88, 1.04, 1.22)]
    elif variant == "speed":
        make_speed(me, rnd)
        name, folder = "AstronautSpeed", MODELS / "AstronautSpeed"
        # the photo covers the front of the head (incl. the sides of the nose, whatever way they face)
        face_polys = [p.index for p in me.polygons if p.material_index == SLOT_SKIN
                      and p.center.x > HEAD["c"].x + 0.06 and p.normal.x > -0.2
                      and p.center.z > NECK_Z + 0.06 and abs(p.center.y) < 0.15]
        face_tile = load_tile(HERE / "SpeedFace_Tile.png")
        segments = SEGMENTS_SPEED
        # skin on the sides/back of the head and neck: same tone as the face in the photo
        th, tw = face_tile.shape[:2]
        mid = face_tile[int(th * 0.44):int(th * 0.62), int(tw * 0.31):int(tw * 0.69)].reshape(-1, 3).mean(axis=0)
        SKIN = [tuple(int(min(255, c * k)) for c in mid) for k in (0.32, 0.52, 0.72, 0.88, 1.04, 1.22)]
    else:
        name, folder = "Astronaut", MODELS / "Astronaut"
        face_polys, face_tile, segments = [], None, SEGMENTS_ASTRO
    for p in me.polygons:
        # the photo head is smooth-shaded (PS3/PS4 look); everything else stays faceted
        p.use_smooth = (PHOTO_DATA is not None and p.material_index == SLOT_SKIN and p.center.z > NECK_Z + 0.05)
    uv = unwrap(me, face_polys, segments)
    img = paint(me, uv, face_polys, face_tile, rnd, folder / "Textures" / f"{name}_Atlas.png")
    mat = make_material(f"{name}_PS1", img)
    me.materials.clear()
    me.materials.append(mat)
    for p in me.polygons:
        p.material_index = 0
    ob.name = me.name = name
    print(f"{name} verts {len(me.vertices)} tris {sum(len(p.vertices) - 2 for p in me.polygons)} face polys {len(face_polys)}")
    img.pack()
    out_blend = HERE / ("Astro_Textured.blend" if variant == "astro" else "Astro_Speed_Textured.blend")
    bpy.ops.wm.save_as_mainfile(filepath=str(out_blend), copy=True)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=str(folder / f"{name}.fbx"), use_selection=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", path_mode="STRIP",
    )
    if "--preview" in argv:
        render_previews(ob, argv[argv.index("--preview") + 1], variant, head=(variant == "speed"))


main()
