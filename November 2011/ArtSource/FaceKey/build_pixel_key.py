"""Detailed pixel-art texture for the hand-made key, painted from its geometry. Never modifies
FaceByMyself.blend. Run prep first: python ps1_key_atlas.py  (makes the face tile)

  blender -b FaceByMyself.blend --python build_pixel_key.py -- [--preview <folder>]

Texture atlas 256x256, point-filtered:
  top-left 128x128   the detailed face (FaceKeyPS1_FaceTile.png), mapped flat onto the disc front
  the other 3/4      the rest of the key, unwrapped into patches of neighbouring faces that point
                     roughly the same way, painted per pixel from the 3D model:
                 - pixel-art light from the top-left, ordered (Bayer) dither between gold tones
                 - 1-px dark outline along sharp edges, bright 2-px bevel on edges facing the light
                 - small sparkle glints on the bigger faces
Outputs: FaceByMyself_Textured.blend, Assets/Models/FaceKeyPS1/FaceKeyPS1.fbx + Textures/FaceKeyPS1_Atlas.png
"""
import math
import random
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
ASSET_DIR = HERE.parent.parent / "Assets" / "Models" / "FaceKeyPS1"
ATLAS_OUT = ASSET_DIR / "Textures" / "FaceKeyPS1_Atlas.png"
FACE_TILE = HERE / "FaceKeyPS1_FaceTile.png"
DISC_NAME = "Circle"
FRONT = Vector((0, -1, 0))

W, H = 128, 128            # atlas size - low resolution on purpose (chunky PS1 pixels)
FACE_RECT = (0, 64, 64, 64)               # x, y (from bottom), w, h: face tile, top-left
BODY_SEGMENTS = [(64, 0, 64, 128),        # body patches fill the right column...
                 (0, 0, 64, 64)]          # ...then the bottom-left square
PAD = 1                    # texels of gutter around each patch
SHARP_DEG = 45             # edges sharper than this get a soft outline
OUTLINE_STRENGTH = 0.4     # how much an outline darkens/lightens the colour under it (0..1)
NOISE_SCALE = 30.0         # hand-painted light/dark blotches (bigger = smaller blotches)
NOISE_AMOUNT = 0.4         # their strength, in tone steps
TONE_STEPS = 3             # soft steps between palette tones (cel-painted look, no dither)
GLINTS = False
ISLAND_DEG = 35            # max angle from a patch's first face when growing a patch

RAMP = [(108, 32, 16), (198, 66, 32), (230, 118, 36), (250, 162, 40), (250, 194, 68), (250, 226, 108)]
DEEP, SHADOW, MID, MAIN, LIGHT_C, HILITE = range(6)
GLINT = (255, 250, 214)
LIGHT = Vector((-0.55, -0.45, 0.7)).normalized()      # top-left-front
EDGE_LIGHT = Vector((-1, 0, 1)).normalized()          # screen up-left, for bevel highlights
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0
GRADIENT_PER_M = 9.0       # tone steps per metre of height (dithered bands across big faces)


# ------------------------------------------------------------------ painting helpers
def _hash(a, b, c):
    n = (a * 374761393 + b * 668265263 + c * 1274126177) & 0xFFFFFFFF
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    return (n & 0xFFFF) / 32767.5 - 1.0


def vnoise(p, scale):
    """Smooth value noise in 3D (-1..1): soft, irregular blotches like loose brushwork."""
    x, y, z = p.x * scale, p.y * scale, p.z * scale
    xi, yi, zi = math.floor(x), math.floor(y), math.floor(z)
    sx, sy, sz = (t * t * (3 - 2 * t) for t in (x - xi, y - yi, z - zi))
    def lerp(a, b, t): return a + (b - a) * t
    c = [[[_hash(xi + i, yi + j, zi + k) for k in (0, 1)] for j in (0, 1)] for i in (0, 1)]
    return lerp(lerp(lerp(c[0][0][0], c[0][0][1], sz), lerp(c[0][1][0], c[0][1][1], sz), sy),
                lerp(lerp(c[1][0][0], c[1][0][1], sz), lerp(c[1][1][0], c[1][1][1], sz), sy), sx)


def shade(f):
    """Gold at tone f (0..5): smooth blend in a few soft steps, no dither."""
    f = min(5.0, max(0.0, f))
    i = min(4, int(f))
    t = round((f - i) * TONE_STEPS) / TONE_STEPS
    return tuple(a + (b - a) * t for a, b in zip(RAMP[i], RAMP[i + 1]))


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


# ------------------------------------------------------------------ mesh prep
def leave_edit_mode():
    for o in bpy.data.objects:
        if o.mode != "OBJECT":
            with bpy.context.temp_override(active_object=o, object=o):
                bpy.ops.object.mode_set(mode="OBJECT")


def prepare_mesh():
    """Copy the parts, fix inside-out faces, tag the disc front, join into one object for
    unwrapping/painting (split_parts() separates them again afterwards). Returns (key, names)."""
    old = bpy.data.objects.get("FaceKey")
    if old is not None:
        bpy.data.objects.remove(old)
    parts = sorted((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: o.name)
    names = [o.name for o in parts]
    bpy.ops.object.select_all(action="DESELECT")
    for ob in parts:
        ob.data = ob.data.copy()
        ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for ob in parts:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(ob.data)
        bm.free()
        part = ob.data.attributes.new("part_id", "INT", "FACE")
        for p in ob.data.polygons:
            part.data[p.index].value = names.index(ob.name)
        attr = ob.data.attributes.new("is_face", "INT", "FACE")
        for p in ob.data.polygons:
            # face texture covers the whole visible disc front, incl. the tilted faces of its
            # inner step (outlined separately, see paint_face_outline)
            attr.data[p.index].value = int(ob.name == DISC_NAME and p.normal.dot(FRONT) > 0.1)
        for a in list(ob.data.color_attributes):
            ob.data.color_attributes.remove(a)
        while ob.data.uv_layers:
            ob.data.uv_layers.remove(ob.data.uv_layers[0])
    bpy.ops.object.join()
    key = bpy.context.active_object
    key.name = key.data.name = "FaceKeyPS1"
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    key.location = (0, 0, 0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for p in key.data.polygons:
        p.use_smooth = False
    return key, names


def split_parts(key, names, mat):
    """Separate the painted mesh back into the original parts (same names, one shared material
    and texture), each with its pivot at its own centre, under an empty parent 'FaceKeyPS1'."""
    me = key.data
    pid = [d.value for d in me.attributes["part_id"].data]
    me.materials.clear()
    for _ in names:
        me.materials.append(mat)                 # one slot per part, just to split by
    for p in me.polygons:
        p.material_index = pid[p.index]
    bpy.ops.object.select_all(action="DESELECT")
    key.select_set(True)
    bpy.context.view_layer.objects.active = key
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="MATERIAL")
    bpy.ops.object.mode_set(mode="OBJECT")
    pieces = [o for o in bpy.context.selected_objects if o.type == "MESH"]
    root = bpy.data.objects.new("FaceKeyPS1", None)
    bpy.context.collection.objects.link(root)
    out = []
    for ob in pieces:
        idx = ob.data.attributes["part_id"].data[0].value
        ob.name = ob.data.name = names[idx]
        ob.data.materials.clear()
        ob.data.materials.append(mat)
        for p in ob.data.polygons:
            p.material_index = 0
        for a in ("part_id", "is_face"):
            if a in ob.data.attributes:
                ob.data.attributes.remove(ob.data.attributes[a])
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
        ob.parent = root
        out.append(ob)
    print("PARTS", sorted(o.name for o in out))
    return root, out


# ------------------------------------------------------------------ UVs
def unwrap(me):
    """Face polys: flat front projection into the left half. Every other poly: its own patch,
    same texel density everywhere, shelf-packed into the right half."""
    uv = me.uv_layers.new(name="UVMap")
    is_face = [d.value for d in me.attributes["is_face"].data]
    verts = [v.co for v in me.vertices]

    fpts = [verts[i] for p in me.polygons if is_face[p.index] for i in p.vertices]
    fx0, fx1 = min(p.x for p in fpts), max(p.x for p in fpts)
    fz0, fz1 = min(p.z for p in fpts), max(p.z for p in fpts)
    rx, ry, rw, rh = FACE_RECT
    for p in me.polygons:
        if is_face[p.index]:
            for li in p.loop_indices:
                c = verts[me.loops[li].vertex_index]
                s, t = (c.x - fx0) / (fx1 - fx0), (c.z - fz0) / (fz1 - fz0)
                uv.data[li].uv = ((rx + 0.5 + s * (rw - 1)) / W, (ry + 0.5 + t * (rh - 1)) / H)

    # group neighbouring body faces that point roughly the same way into patches
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    patch_of = {}
    groups = []
    for seed in sorted((f for f in bm.faces if not is_face[f.index]), key=lambda f: -f.calc_area()):
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
                    if g.index in patch_of or is_face[g.index]:
                        continue
                    if math.degrees(f.normal.angle(g.normal, 0)) > SHARP_DEG:
                        continue
                    if math.degrees(seed.normal.angle(g.normal, 0)) > ISLAND_DEG:
                        continue
                    patch_of[g.index] = gid
                    stack.append(g)
        groups.append(group)
    bm.free()

    # project each patch onto the plane of its average normal (in metres)
    patches = []
    for group in groups:
        n = sum((me.polygons[i].normal * me.polygons[i].area for i in group), Vector()).normalized()
        t1 = n.cross(Vector((0, 0, 1)))
        if t1.length < 1e-4:
            t1 = n.cross(Vector((1, 0, 0)))
        t1.normalize()
        t2 = n.cross(t1).normalized()
        if t2.z < 0 or (abs(t2.z) < 1e-6 and t2.x > 0):    # keep "up" up in the texture
            t2, t1 = -t2, -t1
        vids = {v for i in group for v in me.polygons[i].vertices}
        co2 = {v: Vector((verts[v].dot(t1), verts[v].dot(t2))) for v in vids}
        lo = Vector((min(c.x for c in co2.values()), min(c.y for c in co2.values())))
        hi = Vector((max(c.x for c in co2.values()), max(c.y for c in co2.values())))
        patches.append((group, co2, lo, hi - lo))

    # biggest texel density that still fits: shelf-pack tallest first across the body segments
    order = sorted(range(len(patches)), key=lambda k: -patches[k][3].y)
    scale = 3000.0
    while True:
        seg, x, y, shelf_h = 0, 0, 0, 0
        placed, ok = {}, True
        for k in order:
            size = patches[k][3]
            w = max(2, math.ceil(size.x * scale)) + 2 * PAD
            h = max(2, math.ceil(size.y * scale)) + 2 * PAD
            while True:
                sx, sy, sw, sh = BODY_SEGMENTS[seg]
                if x + w > sw:
                    x, y, shelf_h = 0, y + shelf_h, 0
                if y + h <= sh and w <= sw:
                    break
                seg, x, y, shelf_h = seg + 1, 0, 0, 0
                if seg >= len(BODY_SEGMENTS):
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
        for i in group:
            p = me.polygons[i]
            for li in p.loop_indices:
                c = co2[me.loops[li].vertex_index]
                uv.data[li].uv = ((px + PAD + (c.x - lo.x) * scale) / W,
                                  (py + PAD + (c.y - lo.y) * scale) / H)
    print(f"UV texel density {scale:.0f} px/m, {len(patches)} body patches from "
          f"{sum(len(g) for g in groups)} faces")
    return uv, is_face, scale


# ------------------------------------------------------------------ painting
def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (a + ab * t - p).length


def paint_body(me, uv, is_face, atlas, rnd):
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.edges.ensure_lookup_table()
    sharp = set()
    for e in bm.edges:
        fs = e.link_faces
        if len(fs) != 2 or math.degrees(fs[0].normal.angle(fs[1].normal, 0)) > SHARP_DEG:
            sharp.add(frozenset(v.index for v in e.verts))
    bm.free()

    filled = np.zeros((H, W), bool)
    face_mask = np.zeros((H, W), bool)
    rx, ry, rw, rh = FACE_RECT
    face_mask[ry:ry + rh, rx:rx + rw] = True
    me.calc_loop_triangles()
    tris_by_poly = {}
    for t in me.loop_triangles:
        tris_by_poly.setdefault(t.polygon_index, []).append(t)

    for p in me.polygons:
        if is_face[p.index]:
            continue
        n = p.normal
        centre = p.center
        # Light both sides like the front (a pickup is seen from all around): mirror back faces.
        back = n.y > 0
        mirror = (lambda v: Vector((-v.x, -v.y, v.z))) if back else (lambda v: v)
        base = 3 + (mirror(n).dot(LIGHT) - 0.45) * 2.6
        # this polygon's sharp edges, in texel space, with "lit" flag
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
            a2 = Vector(uv.data[la].uv) * Vector((W, H))
            b2 = Vector(uv.data[lb].uv) * Vector((W, H))
            edges.append((a2, b2, lit))

        texels = []
        for t in tris_by_poly.get(p.index, []):
            P = [me.vertices[me.loops[li].vertex_index].co for li in t.loops]
            U = [Vector(uv.data[li].uv) * Vector((W, H)) for li in t.loops]
            x0, x1 = int(min(u.x for u in U)) - 1, int(max(u.x for u in U)) + 1
            y0, y1 = int(min(u.y for u in U)) - 1, int(max(u.y for u in U)) + 1
            den = (U[1].y - U[2].y) * (U[0].x - U[2].x) + (U[2].x - U[1].x) * (U[0].y - U[2].y)
            if abs(den) < 1e-9:
                continue
            for ty in range(max(0, y0), min(H, y1 + 1)):
                for tx in range(max(0, x0), min(W, x1 + 1)):
                    if face_mask[ty, tx]:
                        continue
                    q = Vector((tx + 0.5, ty + 0.5))
                    w0 = ((U[1].y - U[2].y) * (q.x - U[2].x) + (U[2].x - U[1].x) * (q.y - U[2].y)) / den
                    w1 = ((U[2].y - U[0].y) * (q.x - U[2].x) + (U[0].x - U[2].x) * (q.y - U[2].y)) / den
                    w2 = 1 - w0 - w1
                    if min(w0, w1, w2) < -0.02:
                        continue
                    pos = P[0] * w0 + P[1] * w1 + P[2] * w2
                    f = (base + (pos.z - centre.z) * GRADIENT_PER_M
                         + NOISE_AMOUNT * vnoise(pos, NOISE_SCALE))
                    col = shade(f)
                    if edges:
                        d, lit = min((seg_dist(q, a, b), l) for a, b, l in edges)
                        if d < 1.0:                                  # soft 1-px edge, no black lines
                            col = mix(col, RAMP[HILITE] if lit else RAMP[DEEP],
                                      OUTLINE_STRENGTH * (0.6 if lit else 1.0))
                    atlas[ty, tx] = col
                    filled[ty, tx] = True
                    texels.append((tx, ty))
        if GLINTS and len(texels) > 250 and rnd.random() < 0.5:   # optional glint on bigger faces
            gx, gy = texels[rnd.randrange(len(texels))]
            for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
                if (gx + dx, gy + dy) in set(texels):
                    atlas[gy + dy, gx + dx] = GLINT if (dx, dy) == (0, 0) else RAMP[HILITE]

    # grow patches into their gutters so point sampling never picks up empty pixels
    for _ in range(PAD + 1):
        grown = filled.copy()
        src = atlas.copy()
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            sh_f = np.roll(filled, (dy, dx), (0, 1))
            sh_c = np.roll(src, (dy, dx), (0, 1))
            take = ~grown & sh_f & ~face_mask
            atlas[take] = sh_c[take]
            grown |= take
        filled = grown


FACE_OUTLINE = True        # pixel-art outline around the face, along the disc front's polygon edge


OUTLINE_FOLD_DEG = 20      # disc-front edges folding more than this get outlined


def paint_face_outline(me, uv, is_face, atlas):
    """Outline the face area along the model's real low-poly edges: the outer rim of the disc
    front AND its inner step ring - every edge of the face polygons that is a border or folds
    more than OUTLINE_FOLD_DEG. 1-px dark line, then a 2-px bevel - bright on edges facing the
    light, darker on the others - like the edges of the key body."""
    users_all = {}
    for p in me.polygons:
        loops = list(p.loop_indices)
        for k in range(len(loops)):
            la, lb = loops[k], loops[(k + 1) % len(loops)]
            key = frozenset((me.loops[la].vertex_index, me.loops[lb].vertex_index))
            users_all.setdefault(key, []).append((la, lb, p.index))
    centre = sum((me.polygons[i].center for i in range(len(me.polygons)) if is_face[i]), Vector())
    centre /= max(1, sum(is_face))
    edges = []
    for key, users in users_all.items():
        mine = [u for u in users if is_face[u[2]]]
        if not mine:
            continue
        if len(users) == 2:
            n0, n1 = (me.polygons[u[2]].normal for u in users)
            if math.degrees(n0.angle(n1, 0)) <= OUTLINE_FOLD_DEG:
                continue                                 # flat inside the face: no line
        la, lb, _ = mine[0]
        a3 = me.vertices[me.loops[la].vertex_index].co
        b3 = me.vertices[me.loops[lb].vertex_index].co
        d = (b3 - a3).normalized()
        out = (a3 + b3) / 2 - centre
        out = (out - d * out.dot(d)).normalized()
        lit = out.dot(EDGE_LIGHT) > 0.2
        a2 = Vector(uv.data[la].uv) * Vector((W, H))
        b2 = Vector(uv.data[lb].uv) * Vector((W, H))
        edges.append((a2, b2, lit))
    rx, ry, rw, rh = FACE_RECT
    for ty in range(ry, ry + rh):
        for tx in range(rx, rx + rw):
            q = Vector((tx + 0.5, ty + 0.5))
            d, lit = min((seg_dist(q, a, b), l) for a, b, l in edges)
            if d < 1.5:                                      # soft rim line, darker of the gold
                atlas[ty, tx] = mix(atlas[ty, tx], RAMP[SHADOW] if not lit else RAMP[HILITE],
                                    0.55 if not lit else 0.4)
    print(f"face outline: {len(edges)} edges")


def build_atlas(me, uv, is_face, rnd):
    atlas = np.zeros((H, W, 3), np.float64)
    atlas[:, :] = RAMP[MAIN]
    tile = bpy.data.images.load(str(FACE_TILE))
    tw, th = tile.size
    px = np.empty(tw * th * 4, np.float32)
    tile.pixels.foreach_get(px)
    rx, ry, rw, rh = FACE_RECT
    assert (tw, th) == (rw, rh), f"face tile is {tw}x{th}, expected {rw}x{rh}"
    atlas[ry:ry + rh, rx:rx + rw] = px.reshape(th, tw, 4)[:, :, :3] * 255.0   # rows bottom-up
    bpy.data.images.remove(tile)
    if FACE_OUTLINE:
        paint_face_outline(me, uv, is_face, atlas)
    paint_body(me, uv, is_face, atlas, rnd)
    img = bpy.data.images.new("FaceKeyPS1_Atlas", W, H, alpha=False)
    rgba = np.dstack([atlas / 255.0, np.ones((H, W, 1))]).astype(np.float32)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = str(ATLAS_OUT)
    img.file_format = "PNG"
    img.save()
    return img


def make_material(img):
    mat = bpy.data.materials.new("FaceKeyPS1")
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


def render_previews(shown, folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    for o in sc.objects:
        o.hide_render = o not in shown
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.color_type = "FLAT", "TEXTURE"
    sh.show_backface_culling = True
    sc.render.resolution_x, sc.render.resolution_y = 600, 800
    tgt = bpy.data.objects.new("T", None)
    sc.collection.objects.link(tgt)
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = "ORTHO"
    cam.constraints.new("TRACK_TO").target = tgt
    for name, d, scale in (("front", (0, -1, 0), 0.5), ("threeq", (0.7, -0.7, 0.3), 0.5),
                           ("back", (0.4, 1, 0.2), 0.5)):
        cam.data.ortho_scale = scale
        cam.location = Vector(d).normalized() * 2
        sc.render.filepath = str(folder / f"pix_{name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    rnd = random.Random(5)
    leave_edit_mode()
    key, names = prepare_mesh()
    me = key.data
    uv, is_face, density = unwrap(me)
    img = build_atlas(me, uv, is_face, rnd)
    mat = make_material(img)
    print("KEY verts", len(me.vertices), "tris", sum(len(p.vertices) - 2 for p in me.polygons))
    root, parts = split_parts(key, names, mat)        # back to your separate parts
    img.pack()
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "FaceByMyself_Textured.blend"), copy=True)
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for ob in parts:
        ob.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=str(ASSET_DIR / "FaceKeyPS1.fbx"), use_selection=True,
        object_types={"EMPTY", "MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", path_mode="STRIP",
    )
    if "--preview" in argv:
        render_previews(set(parts), argv[argv.index("--preview") + 1])


main()
