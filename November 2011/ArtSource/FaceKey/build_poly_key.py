"""Low-poly-art version of the hand-made key: all detail is geometry + one flat colour per triangle.
No textures. Never modifies FaceByMyself.blend.

  blender -b FaceByMyself.blend --python build_poly_key.py -- [--preview <folder>]

- the face is a triangulated 3D relief on the front of the disc: dense triangles where the face
  has detail (eyes, nose, lips), sparse on smooth areas; each triangle takes the average colour of
  the face in reference.png under it (same warm face colours as before)
- every triangle of the key body gets a flat gold tone by the way it faces (lit from top-left),
  with a slight per-triangle variation so facets read
- colours live in the mesh ("Col" attribute -> FBX vertex colours); Unity material:
  Retro/PS1 Lit with "Use mesh colours" on
- saves FaceByMyself_Poly.blend, exports Assets/Models/FaceKeyPS1/FaceKeyPS1.fbx
"""
import math
import random
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.geometry import delaunay_2d_cdt

HERE = Path(__file__).resolve().parent
ASSET_DIR = HERE.parent.parent / "Assets" / "Models" / "FaceKeyPS1"
DISC_NAME = "Circle"
FRONT = Vector((0, -1, 0))

# --- face settings (reference.png pixels / metres)
FACE_C, FACE_HALF = (630, 231), 116        # square of reference.png that becomes the face
FACE_OVAL = (122, 112)                     # the actual face inside it (outside gets filled in)
HOLE_C, HOLE_R = (630, 92), 44             # key-ring hole in the photo, filled in
FACE_POINTS = 260                          # interior points -> ~2x triangles
EDGE_POINTS = 44                           # points around the face edge
FACE_COVER = 0.93                          # face radius as a fraction of the disc radius
DOME_DEPTH = 0.010                         # how far the face bulges out (m)
DETAIL_DEPTH = 0.006                       # extra relief from light/dark detail (m)
OLD_GOLD = np.array([214, 158, 48]) / 255.0

# --- body palette (sRGB 0-1), lit from the top-left as seen from the front
TONES = {
    "hilite": (250, 226, 108), "main": (250, 162, 40),
    "shadow": (198, 66, 32), "deep": (108, 32, 16),
}
JITTER = 0.05                              # +-5% brightness per triangle


# ------------------------------------------------------------------ image helpers (numpy)
def blur(a, sigma):
    if sigma <= 0:
        return a
    r = max(1, int(3 * sigma))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k /= k.sum()
    pad = [(r, r), (r, r)] + [(0, 0)] * (a.ndim - 2)
    p = np.pad(a, pad, mode="edge")
    p = np.apply_along_axis(lambda m: np.convolve(m, k, mode="valid"), 0, p)
    return np.apply_along_axis(lambda m: np.convolve(m, k, mode="valid"), 1, p)


def load_face_image():
    """Face crop from reference.png as float RGB (rows top->bottom), with the background and
    key-ring hole filled with nearby face colours, and the old face colour grading applied."""
    img = bpy.data.images.load(str(HERE / "reference.png"))
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    rgb = px.reshape(h, w, 4)[::-1, :, :3]                    # top row first, like the PNG
    x0, y0 = FACE_C[0] - FACE_HALF, FACE_C[1] - FACE_HALF
    crop = rgb[y0:y0 + 2 * FACE_HALF, x0:x0 + 2 * FACE_HALF].astype(np.float64)
    n = crop.shape[0]
    yy, xx = np.mgrid[0:n, 0:n] + 0.5
    ex = (xx + x0 - FACE_C[0]) / (FACE_OVAL[0] - 4)
    ey = (yy + y0 - FACE_C[1]) / (FACE_OVAL[1] - 4)
    keep = (ex ** 2 + ey ** 2 <= 1) & (np.hypot(xx + x0 - HOLE_C[0], yy + y0 - HOLE_C[1]) > HOLE_R)
    k = keep.astype(np.float64)
    out = crop.copy()
    for sigma in (6, 14, 30):                                  # fill outside from the inside
        wsum = blur(k, sigma)
        for c in range(3):
            fill = blur(out[:, :, c] * k, sigma) / np.maximum(wsum, 1e-6)
            out[:, :, c] = np.where(k > 0, out[:, :, c], np.where(wsum > 0.03, fill, out[:, :, c]))
        k = np.maximum(k, (wsum > 0.03).astype(np.float64))
    # old face look: per-channel stretch, a little contrast, warm gold tint
    lo, hi = np.percentile(out, 1, axis=(0, 1)), np.percentile(out, 99, axis=(0, 1))
    out = np.clip((out - lo) / np.maximum(hi - lo, 1e-6), 0, 1)
    out = np.clip((out - out.mean()) * 1.15 + out.mean(), 0, 1)
    out = out * 0.9 + OLD_GOLD * 0.1
    bpy.data.images.remove(img)
    return out


# ------------------------------------------------------------------ geometry helpers
def leave_edit_mode():
    for o in bpy.data.objects:
        if o.mode != "OBJECT":
            with bpy.context.temp_override(active_object=o, object=o):
                bpy.ops.object.mode_set(mode="OBJECT")


def fix_normals(parts):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in parts:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for ob in parts:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(ob.data)
        bm.free()


def tone_for(n):
    if n.z < -0.6:
        return "deep"
    if abs(n.y) > 0.85:
        return "main"
    s = 0.7 * (-n.x) + 0.7 * n.z
    return "hilite" if s > 0.25 else "shadow" if s < -0.25 else "main"


def color_body(ob, rnd):
    me = ob.data
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    col = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    rot = ob.matrix_world.to_3x3()
    for poly in me.polygons:
        n = (rot @ poly.normal).normalized()
        base = np.array(TONES[tone_for(n)]) / 255.0
        c = np.clip(base * (1 + rnd.uniform(-JITTER, JITTER)), 0, 1)
        for li in poly.loop_indices:
            col.data[li].color_srgb = (*c, 1.0)


def sample_points(detail, rnd):
    """Edge ring + interior points placed where the face has detail (weighted, spaced apart)."""
    n = detail.shape[0]
    pts = [(math.cos(2 * math.pi * i / EDGE_POINTS), math.sin(2 * math.pi * i / EDGE_POINTS))
           for i in range(EDGE_POINTS)]
    weight = detail / detail.max() * 0.85 + 0.15            # always some points on smooth areas
    min_d = 0.055
    tries = 0
    while len(pts) < EDGE_POINTS + FACE_POINTS and tries < 200000:
        tries += 1
        x, y = rnd.uniform(-1, 1), rnd.uniform(-1, 1)
        if x * x + y * y > 0.93:
            continue
        i = min(n - 1, int((y * 0.5 + 0.5) * n))
        j = min(n - 1, int((x * 0.5 + 0.5) * n))
        if rnd.random() > weight[i, j]:
            continue
        d = min_d * (1.25 - 0.75 * weight[i, j])            # tighter spacing in detailed areas
        if all((x - px) ** 2 + (y - py) ** 2 > d * d for px, py in pts):
            pts.append((x, y))
    return pts


def build_face(disc, face_rgb, rnd):
    n = face_rgb.shape[0]
    lum = face_rgb @ np.array([0.299, 0.587, 0.114])
    soft = blur(lum, 2.0)
    gy, gx = np.gradient(soft)
    detail = blur(np.hypot(gx, gy), 2.0)
    height = blur(lum, 3.0)
    height = (height - height.mean()) / (height.std() + 1e-6)
    col_img = blur(face_rgb, 1.0)

    pts = sample_points(detail, rnd)
    verts2d, _, faces, *_ = delaunay_2d_cdt([Vector(p) for p in pts], [], [], 0, 1e-6)

    # place on the disc front
    mw = disc.matrix_world
    dv = [mw @ v.co for v in disc.data.vertices]
    cx = (min(p.x for p in dv) + max(p.x for p in dv)) / 2
    cz = (min(p.z for p in dv) + max(p.z for p in dv)) / 2
    radius = (max(p.x for p in dv) - min(p.x for p in dv)) / 2 * FACE_COVER
    front_y = min(p.y for p in dv) - 0.0004

    def img_at(x, y, arr):
        i = min(n - 1, max(0, int((y * 0.5 + 0.5) * n)))   # y = -1 top of image
        j = min(n - 1, max(0, int((x * 0.5 + 0.5) * n)))
        return arr[i, j]

    verts3d = []
    for v in verts2d:
        x, y = v.x, v.y
        r2 = min(1.0, x * x + y * y)
        dome = math.sqrt(1 - r2)
        fall = min(1.0, max(0.0, (1 - math.sqrt(r2)) / 0.2))
        h = DOME_DEPTH * dome + DETAIL_DEPTH * 0.35 * fall * img_at(x, y, height)
        verts3d.append((cx + x * radius, front_y - max(h, 0.0), cz - y * radius))

    me = bpy.data.meshes.new("Face")
    me.from_pydata(verts3d, [], [list(f) for f in faces])
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    for f in bm.faces:                                       # all triangles face the front (-Y)
        if f.normal.y > 0:
            f.normal_flip()
    bm.to_mesh(me)
    bm.free()

    col = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for poly in me.polygons:
        # average the face colour at the centre and toward each corner of the triangle
        cs = [verts2d[i] for i in poly.vertices]
        centre = sum(cs, Vector((0, 0))) / 3
        samples = [centre] + [centre.lerp(c, 0.6) for c in cs]
        c = np.mean([img_at(s.x, s.y, col_img) for s in samples], axis=0)
        for li in poly.loop_indices:
            col.data[li].color_srgb = (*np.clip(c, 0, 1), 1.0)
    ob = bpy.data.objects.new("Face", me)
    bpy.context.collection.objects.link(ob)
    return ob


def make_material():
    mat = bpy.data.materials.new("FaceKeyPS1")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    ca = nt.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    nt.links.new(ca.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 1.0
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.0
    mat.use_backface_culling = True
    return mat


def export_joined(parts):
    bpy.ops.object.select_all(action="DESELECT")
    copies = []
    for ob in parts:
        c = ob.copy()
        c.data = ob.data.copy()
        bpy.context.collection.objects.link(c)
        copies.append(c)
        c.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    bpy.ops.object.join()
    key = bpy.context.active_object
    key.name = key.data.name = "FaceKeyPS1"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    key.location = (0, 0, 0)
    for p in key.data.polygons:
        p.use_smooth = False
    bpy.ops.export_scene.fbx(
        filepath=str(ASSET_DIR / "FaceKeyPS1.fbx"), use_selection=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", colors_type="SRGB", path_mode="STRIP",
    )
    return key


def render_previews(key, folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    for o in sc.objects:
        o.hide_render = o is not key
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.color_type = "STUDIO", "VERTEX"
    sh.show_backface_culling = True
    sc.render.resolution_x, sc.render.resolution_y = 600, 800
    tgt = bpy.data.objects.new("T", None)
    sc.collection.objects.link(tgt)
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type, cam.data.ortho_scale = "ORTHO", 0.5
    cam.constraints.new("TRACK_TO").target = tgt
    for name, d, scale in (("front", (0, -1, 0), 0.5), ("threeq", (0.7, -0.7, 0.3), 0.5),
                           ("face", (0, -1, 0), 0.24)):
        tgt.location = (0, 0, 0.12) if name == "face" else (0, 0, 0)
        cam.data.ortho_scale = scale
        cam.location = tgt.location + Vector(d).normalized() * 2
        sc.render.filepath = str(folder / f"poly_{name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    rnd = random.Random(11)
    leave_edit_mode()
    old = bpy.data.objects.get("FaceKey")
    if old is not None:
        bpy.data.objects.remove(old)
    parts = [o for o in bpy.data.objects if o.type == "MESH"]
    for ob in parts:
        ob.data = ob.data.copy()
    fix_normals(parts)
    for ob in parts:
        color_body(ob, rnd)
    face = build_face(bpy.data.objects[DISC_NAME], load_face_image(), rnd)
    parts.append(face)
    mat = make_material()
    for ob in parts:
        ob.data.materials.clear()
        ob.data.materials.append(mat)
        for p in ob.data.polygons:
            p.use_smooth = False
    print("FACE tris", len(face.data.polygons))
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "FaceByMyself_Poly.blend"), copy=True)
    key = export_joined(parts)
    tris = sum(len(p.vertices) - 2 for p in key.data.polygons)
    print("EXPORT verts", len(key.data.vertices), "tris", tris)
    if "--preview" in argv:
        render_previews(key, argv[argv.index("--preview") + 1])


main()
