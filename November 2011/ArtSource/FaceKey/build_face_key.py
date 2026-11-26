"""Build the face key in Blender from reference.png measurements, save FaceKey.blend,
export Assets/Models/FaceKey/FaceKey.fbx, and optionally render preview images.

Run headless (after prep_face_textures.py):
  blender -b --factory-startup --python build_face_key.py -- [--preview <folder>]

All shape numbers are pixels in reference.png (1280x720, middle key); the model is scaled
so 1700 px = 1 m (key is ~0.38 m tall). Edit the numbers below and re-run to tweak it.
"""
import math
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
ASSET_DIR = HERE.parent.parent / "Assets" / "Models" / "FaceKey"
TEX_DIR = ASSET_DIR / "Textures"

PX_TO_M = 1 / 1700
ORIGIN = (630, 380)          # reference.png pixel that becomes the model origin

# Bow: ellipse disc + loop circle at the top, blended together, with a key-ring hole.
DISC_C, DISC_R = (630, 228), (145, 137)
LOOP_C, LOOP_R = (630, 92), 52
HOLE_R = 32
BLEND_K = 20                 # how smoothly the loop flows into the disc
PLATE_EXTRUDE, PLATE_BEVEL = 7, 5      # bow half-thickness = 12 px
RIM_INSET, RIM_RADIUS = 10, 7

# Face relief, on the front of the bow.
FACE_C, FACE_R = (630, 231), (125, 114)
FACE_TEX_C, FACE_TEX_HALF = (630, 235), 128  # must match prep_face_textures.py
# LOW POLY: there will be hundreds of these keys in a level, so geometry only carries the big
# shapes and the face features live in the normal map. Raise these for a hero/close-up version.
FACE_RINGS = (0.22, 0.42, 0.6, 0.75, 0.87, 0.96, 1.0, 1.05)  # face mesh rings (1 = oval edge)
FACE_SEGS = 28               # points per ring (only the bulge; features -> normal map)
FACE_AMP = 34                # how far the face bulges out, px
DOME_W, DETAIL_W = 0.5, 0.5
GEO_BLUR = 14.0              # detail-map blur (texels) used for the mesh; the rest goes to the normal map

OUTLINE_RAYS = 40            # points around the bow outline
CIRCLE_SEGS = 14             # points around the key-ring hole
BEVEL_RES = 0                # 0 = single chamfer on edges, 4-sided tubes
BACK_DETAIL = False          # rim + ridge on the back side too

# Blade + shoulders outline (crop-space px from the key crop at (470,30)), top-left going round.
BLADE_CROP = [
    (105, 330), (95, 345), (92, 365), (100, 385), (115, 388),
    (118, 395), (118, 450), (128, 460), (118, 470), (118, 490), (130, 500), (118, 510),
    (120, 530), (132, 540), (122, 555), (125, 600), (150, 665), (160, 672),
    (172, 668), (185, 650), (200, 620), (205, 595), (195, 585), (210, 570), (205, 550),
    (215, 530), (200, 515), (213, 500), (200, 480), (210, 465), (198, 450), (210, 435),
    (200, 420), (205, 395),
    (220, 388), (228, 368), (225, 345), (215, 332), (160, 300),
]
BLADE_EXTRUDE, BLADE_BEVEL = 4, 3      # blade half-thickness = 7 px
RIB_TOP, RIB_BOTTOM, RIB_RADIUS = 392, 650, 9


def to_local(x, y):
    """reference.png pixel -> model-space px (Y up, origin at ORIGIN)."""
    return x - ORIGIN[0], -(y - ORIGIN[1])


# ---------------------------------------------------------------- outline maths
def sdf_bow(x, y):
    dx, dy = (x - DISC_C[0]) / DISC_R[0], (y - DISC_C[1]) / DISC_R[1]
    a = (math.hypot(dx, dy) - 1) * min(DISC_R)
    b = math.hypot(x - LOOP_C[0], y - LOOP_C[1]) - LOOP_R
    h = max(0.0, min(1.0, 0.5 + 0.5 * (b - a) / BLEND_K))      # smooth union
    return b * (1 - h) + a * h - BLEND_K * h * (1 - h)


def bow_outline(level=0.0, rays=OUTLINE_RAYS):
    cx, cy = 630, 200
    pts = []
    for i in range(rays):
        t = 2 * math.pi * i / rays
        r = 0.0
        while sdf_bow(cx + r * math.cos(t), cy + r * math.sin(t)) < level:
            r += 0.25
        pts.append((cx + r * math.cos(t), cy + r * math.sin(t)))
    return pts


def circle(c, r, n=CIRCLE_SEGS):
    return [(c[0] + r * math.cos(2 * math.pi * i / n), c[1] + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


# ---------------------------------------------------------------- face maps
def gaussian_blur(a, sigma):
    r = int(3 * sigma)
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k /= k.sum()
    p = np.pad(a, r, mode="edge")
    p = np.apply_along_axis(lambda m: np.convolve(m, k, mode="valid"), 1, p)
    return np.apply_along_axis(lambda m: np.convolve(m, k, mode="valid"), 0, p)


def face_mask(h, w):
    """Per-texel relief weight: 1 inside the face, fading to 0 at its edge (texel row 0 = bottom)."""
    half = FACE_TEX_HALF
    xs = FACE_TEX_C[0] - half + (np.arange(w) + 0.5) / w * 2 * half
    ys = FACE_TEX_C[1] + half - (np.arange(h) + 0.5) / h * 2 * half
    X, Y = np.meshgrid(xs, ys)
    e = np.hypot((X - FACE_C[0]) / FACE_R[0], (Y - FACE_C[1]) / FACE_R[1])
    return np.clip((1 - e) / 0.15, 0, 1)


def write_normal_map(fine_detail):
    """Fine detail the coarse mesh can't hold -> tangent-space (OpenGL, Y+) normal map."""
    h, w = fine_detail.shape
    height_px = FACE_AMP * DETAIL_W * fine_detail * face_mask(h, w)   # in reference px
    texel = 2 * FACE_TEX_HALF / w                                    # reference px per texel
    gy, gx = np.gradient(height_px, texel)
    n = np.dstack([-gx, -gy, np.ones_like(gx)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    rgba = np.dstack([n * 0.5 + 0.5, np.ones((h, w, 1))]).astype(np.float32)
    img = bpy.data.images.new("FaceKey_Face_Normal", w, h, alpha=False)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = str(TEX_DIR / "FaceKey_Face_Normal.png")
    img.file_format = "PNG"
    img.save()
    return img


# ---------------------------------------------------------------- object helpers
def curve_object(name, loops, dims="2D", extrude=0.0, bevel=0.0, z=0.0, fill="BOTH", caps=False, cyclic=True):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = dims
    if dims == "2D":
        cu.fill_mode = fill
    cu.extrude = extrude
    cu.bevel_depth = bevel
    cu.bevel_resolution = BEVEL_RES
    cu.use_fill_caps = caps
    for loop in loops:
        sp = cu.splines.new("POLY")
        sp.points.add(len(loop) - 1)
        for p, (x, y) in zip(sp.points, loop):
            lx, ly = to_local(x, y)
            p.co = (lx, ly, z, 1.0)
        sp.use_cyclic_u = cyclic
    ob = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(ob)
    return ob


def face_object(detail):
    h, w = detail.shape
    def sample(u, v):
        fx, fy = u * (w - 1), v * (h - 1)
        x0, y0 = int(fx), int(fy)
        x1, y1 = min(x0 + 1, w - 1), min(y0 + 1, h - 1)
        tx, ty = fx - x0, fy - y0
        top = detail[y0, x0] * (1 - tx) + detail[y0, x1] * tx
        bot = detail[y1, x0] * (1 - tx) + detail[y1, x1] * tx
        return top * (1 - ty) + bot * ty

    front = PLATE_EXTRUDE + PLATE_BEVEL
    clear = HOLE_R + 12          # keep the face out of the key-ring hole

    def vertex(e, t):
        x = FACE_C[0] + e * FACE_R[0] * math.cos(t)
        y = FACE_C[1] + e * FACE_R[1] * math.sin(t)
        dx, dy = x - LOOP_C[0], y - LOOP_C[1]
        d = math.hypot(dx, dy)
        if d < clear:            # slide out of the hole along the line from its centre
            x, y = LOOP_C[0] + dx / d * clear, LOOP_C[1] + dy / d * clear
            e = math.hypot((x - FACE_C[0]) / FACE_R[0], (y - FACE_C[1]) / FACE_R[1])
        u = (x - (FACE_TEX_C[0] - FACE_TEX_HALF)) / (2 * FACE_TEX_HALF)
        v = 1 - (y - (FACE_TEX_C[1] - FACE_TEX_HALF)) / (2 * FACE_TEX_HALF)
        if e < 1:
            dome = math.sqrt(1 - e * e)
            fall = max(0.0, min(1.0, (1 - e) / 0.15))
            z = front + FACE_AMP * (DOME_W * dome + DETAIL_W * fall * sample(u, v))
        else:
            z = front - 1        # outer ring tucks under the rim
        lx, ly = to_local(x, y)
        verts.append((lx, ly, z))
        uvs.append((u, v))
        return len(verts) - 1

    # Rings that follow the face oval: a centre point, then FACE_SEGS points per ring.
    verts, uvs, faces = [], [], []
    centre = vertex(0.0, 0.0)
    rings = [[vertex(e, 2 * math.pi * s / FACE_SEGS) for s in range(FACE_SEGS)] for e in FACE_RINGS]
    for s in range(FACE_SEGS):
        n = (s + 1) % FACE_SEGS
        # Wound so normals face out of the key (+Z); Unity culls back faces.
        faces.append([centre, rings[0][n], rings[0][s]])
        for a, b in zip(rings, rings[1:]):
            faces.append([a[s], a[n], b[n], b[s]])
    me = bpy.data.meshes.new("Face")
    me.from_pydata(verts, [], faces)
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        for li in poly.loop_indices:
            uv.data[li].uv = uvs[me.loops[li].vertex_index]
    ob = bpy.data.objects.new("Face", me)
    bpy.context.collection.objects.link(ob)
    return ob


def make_materials(normal_img):
    gold = bpy.data.materials.new("FaceKey_Gold")
    gold.use_nodes = True
    b = gold.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.95, 0.56, 0.09, 1)
    b.inputs["Metallic"].default_value = 1.0
    b.inputs["Roughness"].default_value = 0.25

    face = bpy.data.materials.new("FaceKey_Face")
    face.use_nodes = True
    nt = face.node_tree
    fb = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(str(TEX_DIR / "FaceKey_Face_Albedo.png"))
    nt.links.new(tex.outputs["Color"], fb.inputs["Base Color"])
    ntex = nt.nodes.new("ShaderNodeTexImage")
    ntex.image = normal_img
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], fb.inputs["Normal"])
    fb.inputs["Metallic"].default_value = 0.85
    fb.inputs["Roughness"].default_value = 0.35
    for m in (gold, face):
        m.use_backface_culling = True   # previews match Unity: wrong-way faces show up as holes
    return gold, face


# ---------------------------------------------------------------- build
def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)

    det_img = bpy.data.images.load(str(TEX_DIR / "FaceKey_Face_Detail.png"))
    w, h = det_img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    det_img.pixels.foreach_get(px)
    detail = px.reshape(h, w, 4)[:, :, 0].astype(np.float64)   # row 0 = bottom, matches v up
    coarse = gaussian_blur(detail, GEO_BLUR)        # what the low-poly mesh can carry
    normal_img = write_normal_map(detail - coarse)  # everything finer
    gold, face_mat = make_materials(normal_img)

    outline = bow_outline()
    rim_line = bow_outline(level=-RIM_INSET)
    hole = circle(LOOP_C, HOLE_R)
    front = PLATE_EXTRUDE + PLATE_BEVEL
    blade_front = BLADE_EXTRUDE + BLADE_BEVEL
    blade = [(x + 470, y + 30) for x, y in BLADE_CROP]

    parts = [
        curve_object("Plate", [outline, hole], extrude=PLATE_EXTRUDE, bevel=PLATE_BEVEL),
        curve_object("Blade", [blade], extrude=BLADE_EXTRUDE, bevel=BLADE_BEVEL),
    ]
    for side in ((1, -1) if BACK_DETAIL else (1,)):
        parts.append(curve_object("Rim", [rim_line], dims="3D", bevel=RIM_RADIUS, z=side * front))
        parts.append(curve_object("HoleRim", [circle(LOOP_C, HOLE_R + 6)], dims="3D", bevel=RIM_RADIUS - 1, z=side * front))
        parts.append(curve_object("Rib", [[(630, RIB_TOP), (630, RIB_BOTTOM)]], dims="3D", bevel=RIB_RADIUS,
                                  z=side * blade_front * 0.45, caps=True, cyclic=False))
    for ob in parts:
        ob.data.materials.append(gold)
    face = face_object(coarse)
    face.data.materials.append(face_mat)
    parts.append(face)

    bpy.ops.object.select_all(action="DESELECT")
    for ob in parts:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.convert(target="MESH")
    bpy.ops.object.join()
    key = bpy.context.active_object
    key.name = key.data.name = "FaceKey"

    # Stand it up: model Y (key length) -> Blender Z, face looking toward -Y (Blender front view).
    key.rotation_euler = (math.radians(90), 0, 0)
    key.scale = (PX_TO_M,) * 3
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=1e-6)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    return key


def export(key):
    bpy.ops.object.select_all(action="DESELECT")
    key.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=str(ASSET_DIR / "FaceKey.fbx"), use_selection=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", path_mode="STRIP", use_mesh_modifiers=True,
    )
    bpy.ops.file.pack_all()   # .blend carries its textures, so it works when copied anywhere
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "FaceKey.blend"))


def render_previews(folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            sc.render.engine = eng
            break
        except TypeError:
            continue
    sc.render.resolution_x, sc.render.resolution_y = 720, 960
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.12, 0.12, 0.14, 1)
    sc.world = world

    def light(name, loc, energy, size):
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy, ld.size = energy, size
        ob = bpy.data.objects.new(name, ld)
        sc.collection.objects.link(ob)
        ob.location = loc
        con = ob.constraints.new("TRACK_TO")
        con.target = target
        return ob

    target = bpy.data.objects.new("Target", None)
    sc.collection.objects.link(target)
    light("Key", (-0.5, -0.8, 0.6), 60, 0.6)
    light("Fill", (0.6, -0.6, 0.0), 25, 0.8)
    light("Rim", (0.0, 0.8, 0.5), 40, 0.5)

    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    con = cam.constraints.new("TRACK_TO")
    con.target = target
    for name, loc in (("front", (0, -1.3, 0.0)), ("three_quarter", (0.75, -1.0, 0.25))):
        cam.location = loc
        cam_data.lens = 85
        sc.render.filepath = str(folder / f"preview_{name}.png")
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    key = build()
    export(key)
    print("FaceKey verts:", len(key.data.vertices), "faces:", len(key.data.polygons))
    if "--preview" in argv:
        render_previews(argv[argv.index("--preview") + 1])
