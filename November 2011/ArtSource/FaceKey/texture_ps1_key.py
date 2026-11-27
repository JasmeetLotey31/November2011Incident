"""Texture the hand-made low-poly key (FaceByMyself.blend) in PS1 style, without touching that file.

  blender -b FaceByMyself.blend --python texture_ps1_key.py -- [--preview <folder>]

- UVs every face of the key onto the 128x128 atlas from ps1_key_atlas.py (pixel-art style):
    flat front of the disc -> FACE; every other face gets one flat gold tone by the way it faces,
    lit from the top-left: tops/left -> HILITE, front/back -> MAIN, right -> SHADOW, under -> DEEP
- one material, nearest-neighbour (pixelated) texture sampling, no shine
- saves FaceByMyself_Textured.blend (your parts kept separate, editable)
- exports one joined mesh to Assets/Models/FaceKeyPS1/FaceKeyPS1.fbx for Unity
"""
import sys
from pathlib import Path

import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
ASSET_DIR = HERE.parent.parent / "Assets" / "Models" / "FaceKeyPS1"
ATLAS = ASSET_DIR / "Textures" / "FaceKeyPS1_Atlas.png"
DISC_NAME = "Circle"         # the part that gets the face
FRONT = Vector((0, -1, 0))   # the key's front faces -Y (Blender front view)

M = 0.5 / 128                # half-texel margin so pixels never bleed across regions
FACE = (0.0 + M, 0.5 + M, 0.5 - M, 1.0 - M)    # u0, v0, u1, v1 (atlas x 0-63, y 0-63)
# Flat tone blocks: every corner of a face goes to the middle of its block (no bleeding).
TONES = {
    "main": (0.75, 0.75),     # atlas x 64-127, y 0-63
    "hilite": (0.75, 0.25),   # atlas x 64-127, y 64-127
    "shadow": (0.125, 0.25),  # atlas x 0-31,   y 64-127
    "deep": (0.375, 0.25),    # atlas x 32-63,  y 64-127
}


def tone_for(n):
    """Pixel-art lighting from the top-left (as seen from the front): pick a flat tone."""
    if n.z < -0.6:
        return "deep"
    if abs(n.y) > 0.85:
        return "main"
    s = 0.7 * (-n.x) + 0.7 * n.z
    return "hilite" if s > 0.25 else "shadow" if s < -0.25 else "main"


def world_bounds(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    lo = Vector(min(p[i] for p in pts) for i in range(3))
    hi = Vector(max(p[i] for p in pts) for i in range(3))
    return lo, hi


def to_region(region, s, t):
    u0, v0, u1, v1 = region
    return u0 + (u1 - u0) * min(max(s, 0), 1), v0 + (v1 - v0) * min(max(t, 0), 1)


def unwrap(parts):
    disc = bpy.data.objects[DISC_NAME]
    dlo, dhi = world_bounds([disc])
    counts = {"face": 0, "main": 0, "hilite": 0, "shadow": 0, "deep": 0}
    for ob in parts:
        me = ob.data
        while me.uv_layers:                      # every UV is rewritten; start from a clean layer
            me.uv_layers.remove(me.uv_layers[0])
        uv = me.uv_layers.new(name="UVMap")
        rot = ob.matrix_world.to_3x3()
        for poly in me.polygons:
            n = (rot @ poly.normal).normalized()
            kind = "face" if (ob == disc and n.dot(FRONT) > 0.85) else tone_for(n)
            counts[kind] += 1
            for li in poly.loop_indices:
                if kind == "face":   # planar from the front, fitted to the disc
                    p = ob.matrix_world @ me.vertices[me.loops[li].vertex_index].co
                    s = (p.x - dlo.x) / (dhi.x - dlo.x)
                    t = (p.z - dlo.z) / (dhi.z - dlo.z)
                    uv.data[li].uv = to_region(FACE, s, t)
                else:
                    uv.data[li].uv = TONES[kind]
    return counts


def fix_normals(parts):
    """Game engines draw only the outside of each face, so every face must point outward.
    Bake in scale/rotation (a negative scale turns a mesh inside-out), then recalc normals."""
    import bmesh
    bpy.ops.object.select_all(action="DESELECT")
    for ob in parts:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for ob in parts:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        before = [f.normal.copy() for f in bm.faces]
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        flipped = sum(1 for f, n in zip(bm.faces, before) if f.normal.dot(n) < 0)
        vol = sum(f.calc_area() * f.normal.dot(f.calc_center_median()) for f in bm.faces) / 3
        open_edges = sum(1 for e in bm.edges if len(e.link_faces) < 2)
        bm.to_mesh(ob.data)
        bm.free()
        print(f"NORMALS {ob.name}: flipped {flipped} faces outward, volume {vol:+.6f}, open edges {open_edges}")


def make_material():
    mat = bpy.data.materials.new("FaceKeyPS1")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(str(ATLAS))
    tex.interpolation = "Closest"               # hard PS1 pixels
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 1.0
    bsdf.inputs["Metallic"].default_value = 0.0
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
    for c in copies:
        c.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    bpy.ops.object.join()
    key = bpy.context.active_object
    key.name = key.data.name = "FaceKeyPS1"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    key.location = (0, 0, 0)
    bpy.ops.export_scene.fbx(
        filepath=str(ASSET_DIR / "FaceKeyPS1.fbx"), use_selection=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", path_mode="STRIP",
    )
    return key


def render_previews(key, folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    for o in sc.objects:
        o.hide_render = o is not key
    # Workbench with back-face culling = what a game engine draws (inside-out faces vanish).
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.color_type = "STUDIO", "TEXTURE"
    sh.show_backface_culling = True
    sc.render.resolution_x, sc.render.resolution_y = 480, 640
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.2, 0.2, 0.22, 1)
    sc.world = world
    tgt = bpy.data.objects.new("T", None)
    sc.collection.objects.link(tgt)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sc.collection.objects.link(sun)
    sun.data.energy = 3
    sun.rotation_euler = (0.9, 0.0, -0.5)
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type, cam.data.ortho_scale = "ORTHO", 0.55
    cam.constraints.new("TRACK_TO").target = tgt
    for name, d in (("front", (0, -1, 0)), ("threeq", (0.7, -0.7, 0.3)), ("back", (0, 1, 0))):
        cam.location = Vector(d).normalized() * 2
        sc.render.filepath = str(folder / f"ps1_{name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    # The file may have been saved mid-edit; mesh data is only readable in Object Mode.
    edited = [o.name for o in bpy.data.objects if o.mode != "OBJECT"]
    if edited:
        print("LEAVING EDIT MODE ON", edited)
        with bpy.context.temp_override(active_object=bpy.data.objects[edited[0]],
                                       object=bpy.data.objects[edited[0]]):
            bpy.ops.object.mode_set(mode="OBJECT")
    old = bpy.data.objects.get("FaceKey")       # the old generated key, hidden in this file
    if old is not None:
        bpy.data.objects.remove(old)
    parts = [o for o in bpy.data.objects if o.type == "MESH"]
    for ob in parts:                             # own mesh data, so the source stays untouched
        ob.data = ob.data.copy()
    fix_normals(parts)
    counts = unwrap(parts)
    mat = make_material()
    for ob in parts:
        ob.data.materials.clear()
        ob.data.materials.append(mat)
    print("PARTS", [o.name for o in parts], "FACES", counts)

    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "FaceByMyself_Textured.blend"), copy=True)
    key = export_joined(parts)
    print("EXPORT verts", len(key.data.vertices), "tris", sum(len(p.vertices) - 2 for p in key.data.polygons))
    if "--preview" in argv:
        render_previews(key, argv[argv.index("--preview") + 1])


main()
