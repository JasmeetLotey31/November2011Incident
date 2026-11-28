"""Dead crew of Station Jupiter, for the Armory floor. Low-poly PS1 bodies, posed (no rig) and
painted from their own geometry with the same painter and rules as the guns and his key
(ArtSource/Guns/build_guns.py): warm limited ramps, light from the top-left, a few soft steps, soft
blotches, a soft 1-px edge on sharp edges, details by 3D position.

  blender -b --python build_crew.py -- [--preview <folder>]

Writes Crew_Textured.blend next to this script and, into Assets/Models/Crew/:
  CrewBody_Prone.fbx + Textures/CrewBody_Prone_Atlas.png    face down, dragged, reaching; back torn open
  CrewBody_Torn.fbx  + Textures/CrewBody_Torn_Atlas.png     on its back, arms flung, chest eaten to the ribs
The crew wear the station's orange jumpsuit: cream reflective cuffs and knee bands, dark webbing,
grey gloves, rubber boots, a name tag, a cream helmet with a dark visor (cracked). Blood soaks into
the suit where it lies on the deck and round the wounds the keys left.
Body space: right, toward the head, up (metres, origin on the floor under the hips). In Unity the
head points +Z, up is +Y, the body's right is +X.
"""
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "Guns"))
import build_guns as gp  # noqa: E402  (the painter; its main() only runs when it's the script)

gp.ASSET_DIR = HERE.parent.parent / "Assets" / "Models" / "Crew"
gp.NOISE_SCALE = 40.0           # bodies are bigger than guns: bigger blotches

CREW_RAMPS = {
    "suit": [(62, 24, 10), (102, 40, 14), (140, 60, 20), (174, 84, 30), (200, 110, 46), (222, 142, 74)],
    "visor": [(6, 8, 12), (14, 18, 24), (24, 30, 38), (40, 48, 58), (64, 74, 86), (100, 112, 124)],
    "flesh": [(34, 6, 7), (62, 12, 12), (92, 20, 17), (120, 32, 22), (146, 48, 28), (172, 72, 42)],
    "bone": [(84, 74, 58), (120, 108, 86), (156, 144, 116), (186, 176, 146), (210, 202, 174), (228, 222, 198)],
    "blood": [(40, 4, 4), (70, 8, 6), (100, 14, 10), (128, 22, 14), (150, 34, 20), (170, 50, 30)],
    "glove": [(30, 30, 32), (46, 46, 48), (64, 64, 66), (86, 86, 86), (110, 108, 104), (136, 132, 124)],
}
for k, ramp in CREW_RAMPS.items():
    gp.RAMPS[k] = ramp
    if k not in gp.KINDS:
        gp.KINDS.append(k)
gp.NOISE.update({"suit": .35, "flesh": .45, "blood": .35, "visor": .15, "bone": .3, "glove": .25})


def P(right, head, up):
    """Body space -> Blender space (exports to Unity as right +X, head +Z, up +Y)."""
    return Vector((-right, -head, up))


class Body(gp.Gun):
    """A posed body from chunky prisms and blocks, each face tagged with a paint kind."""

    def seg(self, kind, a, b, r0, r1, sides=8, across=None, flat=1.0):
        """A prism from a to b (body space), radius r0 -> r1. `across` (body space) is the wide axis
        of the cross-section; `flat` squashes the other one (torsos are wider than thick)."""
        A, B = P(*a), P(*b)
        d = (B - A).normalized()
        ref = P(*across) if across else Vector((0, 0, 1))
        u = (ref - d * ref.dot(d))
        if u.length < 1e-4:
            u = d.orthogonal()
        u.normalize()
        w = d.cross(u).normalized()
        before = set(self.bm.faces)
        rings = []
        for c, r in ((A, r0), (B, r1)):
            ring = []
            for i in range(sides):
                t = 2 * math.pi * (i + .5) / sides
                ring.append(self.bm.verts.new(c + u * math.cos(t) * r + w * math.sin(t) * r * flat))
            rings.append(ring)
        self.bm.faces.new(rings[0])
        self.bm.faces.new(list(reversed(rings[1])))
        for i in range(sides):
            j = (i + 1) % sides
            self.bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
        self._tag(before, kind)

    def block(self, kind, a, b, width, height, up=(0, 0, 1)):
        """A box from a to b (body space) of the given width (across) and height (along `up`)."""
        A, B = P(*a), P(*b)
        d = (B - A)
        length = d.length
        d.normalize()
        upv = P(*up)
        upv = (upv - d * upv.dot(d)).normalized()
        side = d.cross(upv).normalized()
        before = set(self.bm.faces)
        corners = []
        for s in (-1, 1):
            for h in (-1, 1):
                corners.append((s, h))
        vs = {}
        for end, c in ((0, A), (1, B)):
            for s, h in corners:
                vs[(end, s, h)] = self.bm.verts.new(c + side * s * width / 2 + upv * h * height / 2)
        quads = [
            [(0, -1, -1), (0, 1, -1), (0, 1, 1), (0, -1, 1)], [(1, -1, -1), (1, -1, 1), (1, 1, 1), (1, 1, -1)],
            [(0, -1, -1), (0, -1, 1), (1, -1, 1), (1, -1, -1)], [(0, 1, -1), (1, 1, -1), (1, 1, 1), (0, 1, 1)],
            [(0, -1, 1), (0, 1, 1), (1, 1, 1), (1, -1, 1)], [(0, -1, -1), (1, -1, -1), (1, 1, -1), (0, 1, -1)],
        ]
        for q in quads:
            self.bm.faces.new([vs[k] for k in q])
        self._tag(before, kind)

    def crater(self, centre, rx, ry, seed, sides=11):
        """A ragged wound on top of the torso: a sunken bowl of wet flesh with an uneven rim, and
        torn flaps of suit curling up round it. Closed, so normals sort themselves out."""
        cx, cy, cz = centre
        rnd = __import__("random").Random(seed)
        rim, low = [], []
        for i in range(sides):
            t = 2 * math.pi * i / sides
            k = rnd.uniform(.72, 1.18)
            rim.append((cx + math.cos(t) * rx * k, cy + math.sin(t) * ry * k, cz + .006))
            low.append((cx + math.cos(t) * rx * k * .9, cy + math.sin(t) * ry * k * .9, cz - .035))
        before = set(self.bm.faces)
        top = self.bm.verts.new(P(cx, cy, cz - .03))            # the bowl dips down in the middle
        bottom = self.bm.verts.new(P(cx, cy, cz - .05))
        rv = [self.bm.verts.new(P(*p)) for p in rim]
        lv = [self.bm.verts.new(P(*p)) for p in low]
        for i in range(sides):
            j = (i + 1) % sides
            self.bm.faces.new((top, rv[i], rv[j]))
            self.bm.faces.new((rv[i], lv[i], lv[j], rv[j]))
            self.bm.faces.new((bottom, lv[j], lv[i]))
        self._tag(before, "flesh")
        # torn suit flaps curling up and out from the rim
        for i in range(0, sides, 2):
            x, y, z = rim[i]
            ox, oy = x - cx, y - cy
            ln = math.hypot(ox, oy) or 1
            ox, oy = ox / ln, oy / ln
            self.seg("suit", (x - ox * .01, y - oy * .01, z - .004), (x + ox * .035, y + oy * .035, z + .022 + rnd.uniform(0, .012)), .02, .003, 3)

    def helmet(self, centre, radius, facing, segs=8, rings=6):
        """A low-poly helmet: cream shell, the faces looking along `facing` (body space) are the visor."""
        C, F = P(*centre), P(*facing).normalized()
        before = set(self.bm.faces)
        grid = []
        for r in range(rings + 1):
            th = math.pi * r / rings
            row = []
            for s in range(segs):
                ph = 2 * math.pi * s / segs
                row.append(self.bm.verts.new(C + Vector((math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th))) * radius))
            grid.append(row)
        for r in range(rings):
            for s in range(segs):
                t = (s + 1) % segs
                if r == 0:
                    self.bm.faces.new((grid[0][s], grid[1][t], grid[1][s]))
                elif r == rings - 1:
                    self.bm.faces.new((grid[r][s], grid[r][t], grid[r + 1][s]))
                else:
                    self.bm.faces.new((grid[r][s], grid[r][t], grid[r + 1][t], grid[r + 1][s]))
        bmesh.ops.remove_doubles(self.bm, verts=[v for row in grid for v in row], dist=1e-5)
        new = [f for f in self.bm.faces if f not in before]
        for f in new:
            n = (f.calc_center_median() - C).normalized()
            f[self.kind] = gp.KINDS.index("visor" if n.dot(F) > .5 else "white")


def limb(b, kind, joints, radii, across=None):
    for (a, c), (r0, r1) in zip(zip(joints, joints[1:]), radii):
        b.seg(kind, a, c, r0, r1, 8, across)


def band(b, kind, a, c, t, r, width=.035):
    """A thin ring round a limb at fraction t from a to c (cuffs, knee bands, belt)."""
    A, C = Vector(a), Vector(c)
    mid = A.lerp(C, t)
    d = (C - A).normalized() * width / 2
    b.seg(kind, tuple(mid - d), tuple(mid + d), r, r, 8)


def build_body(name, J, face, wound, torn_chest):
    """J: joints in body space. face: where the visor looks. wound: centre of the feeding wound."""
    b = Body(name)
    across = (1, 0, 0)
    # torso (wider than thick), collar, belt, harness straps
    b.seg("suit", J["pelvis"], J["chest"], .165, .19, 8, across, flat=.62)
    b.seg("suit", J["chest"], J["neck"], .19, .12, 8, across, flat=.62)
    b.seg("dark", J["neck"], J["head"], .075, .07, 8)
    p0, p1 = Vector(J["pelvis"]).lerp(Vector(J["chest"]), .08), Vector(J["pelvis"]).lerp(Vector(J["chest"]), .2)
    b.seg("dark", tuple(p0), tuple(p1), .174, .176, 8, across, flat=.66)          # belt, flat like the torso
    for side in ("R", "L"):
        b.seg("dark", J["sh" + side], J["harness" + side], .02, .02, 4)
    b.helmet(J["head"], .145, face)
    # arms: upper, fore, cuffs, gloves; legs: thigh, shin, knee bands, boots
    for side in ("R", "L"):
        limb(b, "suit", [J["sh" + side], J["el" + side], J["wr" + side]], [(.058, .05), (.048, .04)])
        band(b, "white", J["el" + side], J["wr" + side], .82, .046)
        b.block("glove", J["wr" + side], J["ha" + side], .075, .035, J.get("palm" + side, (0, 0, 1)))
        b.seg("suit", J["hip" + side], J["kn" + side], .085, .068, 8)
        b.seg("suit", J["kn" + side], J["an" + side], .064, .052, 8)
        band(b, "white", J["hip" + side], J["kn" + side], .92, .074)
        b.block("rubber", J["an" + side], J["toe" + side], .11, .11, J.get("sole" + side, (0, 0, 1)))
    # the feeding wound: a ragged hole torn through the suit into wet flesh, bone showing
    wx, wy, wz = wound
    b.crater(wound, .12 if torn_chest else .1, .17, seed=7 if torn_chest else 3)
    if torn_chest:
        # ribs: uneven pairs arching out from a broken sternum, some snapped short
        for k, (y, reach_l, reach_r) in enumerate(((-.1, 1, 1), (-.04, .55, 1), (.02, 1, .5), (.08, .8, 1))):
            for sgn, reach in ((-1, reach_l), (1, reach_r)):
                a = (wx + sgn * .015, wy + y, wz - .005)
                m = (wx + sgn * .065, wy + y - .008, wz + .018)
                e = (wx + sgn * .11, wy + y - .025, wz - .004)
                b.seg("bone", a, m, .009, .008, 5)
                if reach > .9:
                    b.seg("bone", m, e, .008, .006, 5)
        b.seg("bone", (wx, wy - .14, wz + .002), (wx, wy - .02, wz + .006), .012, .011, 5)   # sternum, snapped
        b.seg("bone", (wx + .006, wy + .02, wz + .008), (wx, wy + .12, wz + .002), .011, .01, 5)
    else:
        # the spine laid bare: vertebrae with gaps, a few rib stubs
        for k in range(5):
            y = wy - .14 + k * .062
            b.seg("bone", (wx, y, wz + .004), (wx, y + .044, wz + .006), .016, .015, 6)
            if k in (1, 2, 4):
                for sgn in (-1, 1):
                    b.seg("bone", (wx + sgn * .016, y + .02, wz + .002), (wx + sgn * .06, y + .012, wz + .012), .007, .005, 4)
    return b.finish()


# ------------------------------------------------------------------ poses (body space: right, head, up)
PRONE = {          # face down, dragged toward a vent, right arm reaching ahead
    "pelvis": (0, 0, .13), "chest": (0, .42, .15), "neck": (0, .58, .14), "head": (0, .73, .15),
    "shR": (.2, .5, .15), "elR": (.27, .78, .09), "wrR": (.3, 1.0, .06), "haR": (.31, 1.1, .045),
    "shL": (-.2, .5, .15), "elL": (-.34, .3, .08), "wrL": (-.3, .07, .06), "haL": (-.27, -.03, .045),
    "harnessR": (.06, .1, .24), "harnessL": (-.06, .1, .24),
    "hipR": (.1, -.05, .13), "knR": (.17, -.5, .09), "anR": (.22, -.92, .08), "toeR": (.23, -.94, -.02), "soleR": (0, -1, 0),
    "hipL": (-.1, -.05, .13), "knL": (-.27, -.44, .09), "anL": (-.2, -.88, .08), "toeL": (-.21, -.9, -.02), "soleL": (0, -1, 0),
}
TORN = {           # on its back, arms flung out, chest eaten open, one knee up
    "pelvis": (0, 0, .12), "chest": (0, .42, .14), "neck": (0, .58, .13), "head": (.04, .73, .14),
    "shR": (.2, .5, .14), "elR": (.48, .55, .07), "wrR": (.74, .64, .05), "haR": (.84, .67, .04),
    "shL": (-.2, .5, .14), "elL": (-.42, .67, .07), "wrL": (-.33, .9, .05), "haL": (-.3, .99, .04),
    "harnessR": (.06, .1, .23), "harnessL": (-.06, .1, .23),
    "hipR": (.1, -.05, .12), "knR": (.13, -.5, .1), "anR": (.16, -.94, .09), "toeR": (.17, -.97, .22), "soleR": (0, -1, 0),
    "hipL": (-.1, -.05, .12), "knL": (-.17, -.38, .34), "anL": (-.21, -.8, .1), "toeL": (-.22, -.86, .21), "soleL": (0, -1, 0),
}


def detail(name, kind, pos, n, centre, tone):
    """Painted by 3D position: blood where the body lies on the deck and round the wound, a name
    tag on the chest, cracks across the visor."""
    # body space from Blender
    right, head, up = -pos.x, -pos.y, pos.z
    wound = WOUNDS[name]
    near = math.dist((right, head, up), wound)
    if kind in ("suit", "white", "dark", "glove"):
        # soaked where it lies on the deck, and spreading from the wound (both ragged)
        soak = max(0.0, (.07 - up) * 9) + (.36 - near) * 4.2 + .8 * gp.vnoise(pos, 9.0)
        if soak > .55:
            return tone - .4 - min(1.2, soak - .55), "blood"
        if soak > .3:
            return tone - .7, None                           # darker, damp edge round the soak
    if kind == "bone" and gp.vnoise(pos, 60.0) > .15:
        return tone - .6, "blood"                           # bloodied bone
    if kind == "suit" and name == "CrewBody_Torn" and n.z > .7 and -.17 < right < -.07 and .5 < head < .56:
        return tone + 1.4, "white"                          # name tag
    if kind == "visor" and abs(gp.vnoise(pos, 70.0)) < .05:
        return 3.6, "visor"                                 # cracks
    if kind == "flesh" and gp.vnoise(pos, 45.0) > .35:
        return tone + 1.0, None                             # wet sheen
    return tone, None


WOUNDS = {"CrewBody_Prone": (0, .28, .27), "CrewBody_Torn": (0, .34, .25)}
gp.detail = detail


def render_previews(obs, folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.color_type = "FLAT", "TEXTURE"
    sh.show_backface_culling = True
    sc.render.resolution_x, sc.render.resolution_y = 900, 600
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = "ORTHO"
    tgt = bpy.data.objects.new("T", None)
    sc.collection.objects.link(tgt)
    cam.constraints.new("TRACK_TO").target = tgt
    for ob in obs:
        for o in obs:
            o.hide_render = o is not ob
        tgt.location = P(0, .05, .1)
        for view, d in (("above", P(.3, -.4, 1.6)), ("side", P(1.6, .2, .6))):
            cam.data.ortho_scale = 2.4
            cam.location = tgt.location + d.normalized() * 3
            sc.render.filepath = str(folder / f"{ob.name}_{view}.png")
            bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    gp.ASSET_DIR.mkdir(parents=True, exist_ok=True)
    obs = [build_body("CrewBody_Prone", PRONE, (0, .4, -1), WOUNDS["CrewBody_Prone"], False),
           build_body("CrewBody_Torn", TORN, (.15, .25, 1), WOUNDS["CrewBody_Torn"], True)]
    gp.W = gp.H = 256
    for ob in obs:
        gp.unwrap([ob.data])
        img = gp.paint([ob], ob.name)
        gp.assign_materials([ob], gp.make_material(ob.name, img))
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        print(f"CREW {ob.name}: {len(ob.data.vertices)} verts, {tris} tris")
        img.pack()
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "Crew_Textured.blend"), copy=True)
    for ob in obs:
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.fbx(
            filepath=str(gp.ASSET_DIR / f"{ob.name}.fbx"), use_selection=True, object_types={"MESH"},
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z", axis_up="Y", bake_space_transform=True,
            mesh_smooth_type="FACE", path_mode="STRIP",
        )
    if "--preview" in argv:
        render_previews(obs, argv[argv.index("--preview") + 1])


main()
