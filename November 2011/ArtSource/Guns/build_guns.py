"""PS1 guns for the key hunt, modelled and painted from their own geometry with the same rules
as ArtSource/FaceKey/build_pixel_key.py: warm limited colour ramps, light from the top-left
(both sides lit alike), smooth shading in a few soft steps (no dither), soft irregular blotches
like brushwork, only a soft 1-px edge on sharp edges, details painted by 3D position.

  blender -b --python build_guns.py -- [--preview <folder>]

Writes Guns_Textured.blend next to this script and, into Assets/Models/Guns/:
  IonCarbine.fbx  + Textures/IonCarbine_Atlas.png    ION CARBINE (station security, full auto)
  ArcRepeater.fbx + Textures/ArcRepeater_Atlas.png   ARC REPEATER (rotary heavy gun; child "Barrels" spins)
Glowing parts (energy cells, coils, emitter rings, sight dots) are a second material slot,
"Glow": the game makes them emissive and drives them from the gun (charge, shots, reload).
Built in "gun space": f = forward (muzzle), u = up, l = the gun's left (the side you see in
first person). Metres. Origin under the receiver where the hand holds it, like the other
viewmodels. In Unity the muzzle points +Z and the left side faces -X.
"""
import math
import random
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector

HERE = Path(__file__).resolve().parent
ASSET_DIR = HERE.parent.parent / "Assets" / "Models" / "Guns"
W = H = 256
PAD = 1
SHARP_DEG = 40
ISLAND_DEG = 35
TONE_STEPS = 3
NOISE_SCALE = 90.0          # blotch size: bigger = smaller blotches (guns are small)
OUTLINE_STRENGTH = 0.55
GRADIENT_PER_M = 3.0


def G(f, u, l):
    """Gun space -> Blender space (exports to Unity with the muzzle on +Z)."""
    return Vector((l, -f, u))


# light from the top-left of the screen: up, toward the gun's left side, and from the camera
LIGHT = G(-0.35, 0.8, 0.5).normalized()
EDGE_LIGHT = G(-0.2, 1.0, 0.6).normalized()

# warm, limited ramps (dark -> light). Gunmetal is dark blued steel that only shows light on
# its worn edges; polymer is olive drab; wood is a dark oiled brown; nothing toy-bright.
GUNMETAL = [(12, 13, 16), (22, 24, 28), (34, 36, 42), (50, 52, 58), (74, 76, 80), (110, 108, 104)]
OLIVE_DRAB = [(15, 16, 11), (24, 26, 18), (35, 38, 26), (48, 52, 36), (64, 68, 48), (86, 88, 64)]
RAMPS = {
    "gun": GUNMETAL,
    "dark": [(9, 9, 11), (16, 17, 20), (26, 27, 31), (38, 39, 43), (56, 56, 58), (84, 82, 78)],
    "poly": OLIVE_DRAB,
    "mag": [(10, 10, 12), (18, 19, 22), (28, 29, 33), (41, 42, 46), (60, 60, 62), (88, 86, 82)],   # dark steel
    "guard": OLIVE_DRAB,
    "wood": [(30, 14, 7), (50, 24, 11), (72, 37, 17), (96, 52, 25), (122, 70, 36), (150, 94, 54)],
    "olive": [(20, 23, 13), (31, 36, 20), (44, 51, 28), (58, 66, 38), (75, 84, 50), (96, 104, 66)],
    "brass": [(78, 48, 14), (122, 82, 26), (164, 118, 40), (198, 154, 58), (224, 190, 92), (242, 218, 138)],
    "copper": [(56, 22, 11), (92, 40, 18), (128, 62, 29), (160, 88, 44), (188, 118, 64), (210, 150, 96)],
    "hazard": [(64, 46, 8), (110, 80, 12), (156, 118, 22), (190, 150, 36), (214, 178, 62), (232, 204, 104)],
    "rubber": [(12, 12, 12), (20, 20, 20), (30, 30, 29), (42, 42, 40), (56, 56, 52), (72, 72, 66)],
    "shroud": GUNMETAL,
    # station-issue armour panels: warm cream, like the NASA-white of the station itself
    "white": [(62, 58, 50), (100, 94, 82), (138, 132, 116), (172, 166, 148), (198, 192, 172), (220, 214, 192)],
    "orange": [(80, 28, 8), (130, 48, 12), (176, 74, 20), (210, 100, 30), (230, 130, 52), (244, 164, 90)],
    "glow": [(255, 255, 255)] * 6,   # never painted: its faces get the emissive Glow material
}
KINDS = list(RAMPS)
METALS = {"gun", "dark", "shroud"}
NOISE = {"gun": .22, "dark": .18, "shroud": .22, "poly": .2, "mag": .2, "guard": .2, "wood": .32, "white": .2, "orange": .2}


# ------------------------------------------------------------------ painting helpers (as the key)
def _hash(a, b, c):
    n = (a * 374761393 + b * 668265263 + c * 1274126177) & 0xFFFFFFFF
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    return (n & 0xFFFF) / 32767.5 - 1.0


def vnoise(p, scale):
    x, y, z = p.x * scale, p.y * scale, p.z * scale
    xi, yi, zi = math.floor(x), math.floor(y), math.floor(z)
    sx, sy, sz = (t * t * (3 - 2 * t) for t in (x - xi, y - yi, z - zi))
    def lerp(a, b, t): return a + (b - a) * t
    c = [[[_hash(xi + i, yi + j, zi + k) for k in (0, 1)] for j in (0, 1)] for i in (0, 1)]
    return lerp(lerp(lerp(c[0][0][0], c[0][0][1], sz), lerp(c[0][1][0], c[0][1][1], sz), sy),
                lerp(lerp(c[1][0][0], c[1][0][1], sz), lerp(c[1][1][0], c[1][1][1], sz), sy), sx)


def shade(ramp, f):
    f = min(5.0, max(0.0, f))
    i = min(4, int(f))
    t = round((f - i) * TONE_STEPS) / TONE_STEPS
    return tuple(a + (b - a) * t for a, b in zip(ramp[i], ramp[i + 1]))


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (a + ab * t - p).length


# ------------------------------------------------------------------ modelling
class Gun:
    """A gun built from chunky low-poly pieces, each tagged with a paint kind."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.kind = self.bm.faces.layers.int.new("kind")

    def _tag(self, before, kind, bevel=0.0):
        new = [f for f in self.bm.faces if f not in before]
        if bevel > 0:
            edges = list({e for f in new for e in f.edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, offset_type="OFFSET", segments=1,
                            profile=0.5, affect="EDGES", clamp_overlap=True)
            new = [f for f in self.bm.faces if f not in before]
        for f in new:
            f[self.kind] = KINDS.index(kind)

    def box(self, kind, f, u, l, sf, su, sl, bevel=0.0, tilt=0.0):
        """Box centred at (f, u, l), sizes along forward/up/left, tilted about the left axis
        (positive = an upright piece leans its top forward; a lengthwise piece dips its front)."""
        before = set(self.bm.faces)
        verts = bmesh.ops.create_cube(self.bm, size=1.0)["verts"]
        m = (Matrix.Translation(G(f, u, l)) @ Matrix.Rotation(math.radians(tilt), 4, G(0, 0, 1))
             @ Matrix.Diagonal((sl, sf, su, 1.0)))
        bmesh.ops.transform(self.bm, matrix=m, verts=verts)
        self._tag(before, kind, bevel)

    def tube(self, kind, f0, f1, u, l, r, segments=8, r1=None):
        """Cylinder along the barrel axis from f0 to f1."""
        before = set(self.bm.faces)
        verts = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segments,
                                      radius1=r, radius2=r if r1 is None else r1, depth=f1 - f0)["verts"]
        # cone axis is +Z: turn it to point down the barrel, and give it a flat top
        m = (Matrix.Translation(G((f0 + f1) / 2, u, l)) @ Matrix.Rotation(math.radians(-90), 4, "X")
             @ Matrix.Rotation(math.pi / segments, 4, "Z"))
        bmesh.ops.transform(self.bm, matrix=m, verts=verts)
        self._tag(before, kind)

    def rod(self, kind, f, u0, u1, l, r, segments=6):
        """Upright cylinder (posts)."""
        before = set(self.bm.faces)
        verts = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segments,
                                      radius1=r, radius2=r, depth=u1 - u0)["verts"]
        bmesh.ops.transform(self.bm, matrix=Matrix.Translation(G(f, (u0 + u1) / 2, l)), verts=verts)
        self._tag(before, kind)

    def profile(self, kind, pts, width, l=0.0, bevel=0.0):
        """Side silhouette (forward, up) extruded to `width` across the gun."""
        before = set(self.bm.faces)
        left = [self.bm.verts.new(G(f, u, l + width / 2)) for f, u in pts]
        right = [self.bm.verts.new(G(f, u, l - width / 2)) for f, u in pts]
        self.bm.faces.new(left)
        self.bm.faces.new(list(reversed(right)))
        n = len(pts)
        for i in range(n):
            j = (i + 1) % n
            self.bm.faces.new((left[j], left[i], right[i], right[j]))
        self._tag(before, kind, bevel)

    def finish(self):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        for p in me.polygons:
            p.use_smooth = False
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def build_carbine():
    """ION CARBINE: dark core under cream armour, a glowing energy cell on the left (the side
    you see), holo sight, finned muzzle shroud with a glowing emitter ring, power-cell magazine
    with a charge stripe, skeletal stock."""
    g = Gun("IonCarbine")
    g.box("gun", .07, .005, 0, .34, .06, .042, bevel=.004)                  # core, f -.10..+.24
    panel = [(-.085, .03), (.19, .03), (.225, .008), (.205, -.022), (.02, -.022), (-.01, -.012), (-.095, -.012)]
    g.profile("white", panel, .008, l=.024, bevel=.002)
    g.profile("white", panel, .008, l=-.024, bevel=.002)
    g.box("white", .06, .04, 0, .28, .012, .034, bevel=.003)                # top spine
    g.box("dark", .06, .049, 0, .2, .006, .014)
    # energy cell on the left, held by two straps between dark end caps
    g.tube("glow", -.03, .11, .005, .034, .0095, 8)
    g.tube("dark", -.045, -.03, .005, .034, .0125, 8)
    g.tube("dark", .11, .125, .005, .034, .0125, 8)
    g.box("dark", .02, .005, .034, .008, .026, .026)
    g.box("dark", .07, .005, .034, .008, .026, .026)
    # holo sight: base, two posts, top bar, glowing reticle dot
    g.box("dark", .0, .056, 0, .05, .01, .028)
    g.box("dark", .0, .076, .013, .036, .03, .004)
    g.box("dark", .0, .076, -.013, .036, .03, .004)
    g.box("dark", .0, .092, 0, .036, .004, .03)
    g.box("glow", .0, .075, 0, .004, .006, .006)
    # muzzle shroud, cooling fins, orange hazard plate, glowing emitter ring, crown
    g.box("gun", .30, .005, 0, .12, .052, .046, bevel=.005)                 # f .24..36
    for k in range(4):
        g.box("dark", .262 + k * .026, .034, 0, .009, .008, .05)
    g.box("orange", .27, .005, .0235, .032, .022, .002)
    g.tube("glow", .36, .368, .005, 0, .0165, 8)
    g.tube("dark", .368, .386, .005, 0, .0195, 8)
    # power-cell magazine (bottom forward) with a charge stripe, grip, trigger guard
    g.box("white", .065, -.058, 0, .05, .06, .034, bevel=.003, tilt=-6)
    g.box("glow", .067, -.058, .0175, .03, .006, .002, tilt=-6)
    g.box("dark", .068, -.091, 0, .054, .008, .036, tilt=-6)
    g.box("dark", -.06, -.066, 0, .034, .09, .03, bevel=.004, tilt=16)
    g.box("dark", .0, -.04, 0, .07, .006, .012)
    g.box("dark", .032, -.031, 0, .006, .018, .012)
    # skeletal stock: top bar, slanted lower strut, butt plate, orange stripe
    g.box("white", -.19, .018, 0, .2, .02, .03, bevel=.003)
    g.box("white", -.195, -.037, 0, .215, .014, .026, bevel=.002, tilt=-9.5)
    g.box("dark", -.3, -.015, 0, .02, .1, .04, bevel=.003)
    g.box("orange", -.2, .029, 0, .05, .004, .031)
    return [g.finish()]


def build_repeater():
    """ARC REPEATER: heavy rotary gun. Cream-armoured housing over a dark core, a power pack on
    the left wrapped in glowing coils, an orange-ringed stator the three barrels spin out of
    (the barrels are their own object so the game can spin them), carry handle, front grip."""
    body = Gun("ArcRepeater")
    body.box("gun", .03, .0, 0, .30, .1, .08, bevel=.006)                    # core, f -.12..+.18
    body.profile("white", [(-.12, .048), (.14, .048), (.18, .03), (.18, .062), (.12, .078),
                           (-.10, .078), (-.12, .068)], .07, bevel=.003)     # top shell
    # capacitor on the left: a dark cylinder in a cream cradle, wound with thin glowing coil rings
    body.box("white", .05, -.05, .055, .17, .02, .05, bevel=.003)            # cradle under it
    body.box("white", -.035, -.02, .062, .01, .07, .05, bevel=.002)           # end brackets
    body.box("white", .135, -.02, .062, .01, .07, .05, bevel=.002)
    body.tube("dark", -.03, .13, -.02, .066, .03, 8)
    for k in range(5):
        body.tube("glow", -.012 + k * .03, -.006 + k * .03, -.02, .066, .0335, 8)
    body.box("orange", .05, -.062, .055, .17, .006, .052)                     # hazard trim under it
    # stator the barrels come out of, orange warning ring
    body.tube("dark", .18, .26, .0, 0, .042, 8)
    body.tube("orange", .254, .262, .0, 0, .0445, 8)
    # carry handle
    body.box("dark", -.02, .095, 0, .012, .04, .012)
    body.box("dark", .10, .095, 0, .012, .04, .012)
    body.box("white", .04, .118, 0, .15, .014, .022, bevel=.003)
    # rear grip, trigger guard, front grip under the stator
    body.box("dark", -.075, -.1, 0, .036, .1, .032, bevel=.004, tilt=14)
    body.box("dark", -.03, -.056, 0, .06, .006, .014)
    body.box("dark", .22, -.045, 0, .05, .012, .03)
    body.box("dark", .22, -.095, 0, .03, .09, .03, bevel=.004, tilt=4)
    # back plate (braced at the hip, no stock) and three status lights
    body.box("white", -.145, .0, 0, .05, .1, .074, bevel=.004)
    body.box("dark", -.172, .0, 0, .006, .08, .06)
    for k in range(3):
        body.box("glow", -.135 + k * .014, .03, .0375, .006, .006, .002)

    bar = Gun("Barrels")                                                      # spins about f at u=0, l=0
    for k in range(3):
        a = k * 2 * math.pi / 3
        bar.tube("dark", .2, .62, .021 * math.cos(a), .021 * math.sin(a), .0085, 6)
    bar.tube("gun", .40, .415, 0, 0, .031, 8)
    bar.tube("gun", .585, .60, 0, 0, .033, 8)
    bar.tube("dark", .2, .585, 0, 0, .006, 6)
    bar.tube("glow", .60, .604, 0, 0, .009, 6)
    ob_body, ob_bar = body.finish(), bar.finish()
    ob_bar.parent = ob_body
    return [ob_body, ob_bar]


# ------------------------------------------------------------------ unwrap (patches, like the key)
def unwrap(meshes):
    """One atlas for all the given meshes: neighbouring faces that point the same way become
    patches, projected flat at one texel density and shelf-packed. Glow faces get no texels."""
    patches = []
    for me in meshes:
        uv = me.uv_layers.new(name="UVMap")
        kinds = [KINDS[d.value] for d in me.attributes["kind"].data]
        verts = [v.co.copy() for v in me.vertices]
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        patch_of = {}
        for seed in sorted(bm.faces, key=lambda f: -f.calc_area()):
            if seed.index in patch_of or kinds[seed.index] == "glow":
                continue
            group, stack = [], [seed]
            patch_of[seed.index] = True
            while stack:
                f = stack.pop()
                group.append(f.index)
                for e in f.edges:
                    for o in e.link_faces:
                        if o.index in patch_of or kinds[o.index] == "glow":
                            continue
                        if math.degrees(f.normal.angle(o.normal, 0)) > SHARP_DEG:
                            continue
                        if math.degrees(seed.normal.angle(o.normal, 0)) > ISLAND_DEG:
                            continue
                        patch_of[o.index] = True
                        stack.append(o)
            n = sum((me.polygons[i].normal * me.polygons[i].area for i in group), Vector())
            n = n.normalized() if n.length > 1e-9 else me.polygons[group[0]].normal
            t1 = n.cross(Vector((0, 0, 1)))
            if t1.length < 1e-4:
                t1 = n.cross(Vector((1, 0, 0)))
            t1.normalize()
            t2 = n.cross(t1).normalized()
            if t2.z < 0 or (abs(t2.z) < 1e-6 and t2.x > 0):
                t2, t1 = -t2, -t1
            vids = {v for i in group for v in me.polygons[i].vertices}
            co2 = {v: Vector((verts[v].dot(t1), verts[v].dot(t2))) for v in vids}
            lo = Vector((min(c.x for c in co2.values()), min(c.y for c in co2.values())))
            hi = Vector((max(c.x for c in co2.values()), max(c.y for c in co2.values())))
            patches.append((me, uv, group, co2, lo, hi - lo))
        bm.free()
        for i, p in enumerate(me.polygons):
            if kinds[i] == "glow":
                for li in p.loop_indices:
                    uv.data[li].uv = (0.0, 0.0)

    order = sorted(range(len(patches)), key=lambda k: -patches[k][5].y)
    scale = 4000.0
    while True:
        x = y = shelf_h = 0
        placed, ok = {}, True
        for k in order:
            size = patches[k][5]
            w = max(2, math.ceil(size.x * scale)) + 2 * PAD
            h = max(2, math.ceil(size.y * scale)) + 2 * PAD
            if x + w > W:
                x, y, shelf_h = 0, y + shelf_h, 0
            if y + h > H or w > W:
                ok = False
                break
            placed[k] = (x, y)
            x += w
            shelf_h = max(shelf_h, h)
        if ok:
            break
        scale *= 0.96
    for k, (me, uv, group, co2, lo, size) in enumerate(patches):
        px, py = placed[k]
        for i in group:
            for li in me.polygons[i].loop_indices:
                c = co2[me.loops[li].vertex_index]
                uv.data[li].uv = ((px + PAD + (c.x - lo.x) * scale) / W, (py + PAD + (c.y - lo.y) * scale) / H)
    print(f"{meshes[0].name}: UV texel density {scale:.0f} px/m, {len(patches)} patches")


# ------------------------------------------------------------------ painting
def gun_space(p):
    """Blender position -> (forward, up, left)."""
    return -p.y, p.z, p.x


def detail(name, kind, pos, n, centre, tone):
    """Details painted by 3D position: returns (tone, override ramp or None)."""
    f, u, l = gun_space(pos)
    nf, nu, nl = gun_space(n)
    if name == "ArcRepeater" and kind == "white" and nu > .8 and -.08 < f < .1 and abs(l) < .026:
        if ((f + .08) % .03) < .011:                       # heat vents across the top shell
            return 0.4, "dark"
    if kind == "white" and abs(nl) > .8:                    # panel seams: soft darker grooves
        if name == "IonCarbine" and (abs(f - .06) < .0018 or abs(f - .15) < .0018):
            return tone - 1.2, None
        if name == "ArcRepeater" and abs(f - .05) < .0018 and u < .02:
            return tone - 1.2, None
    if kind == "white" and name == "IonCarbine" and abs(nl) > .8 and l > 0:   # stencilled unit number
        if .1 < f < .16 and -.012 < u < .0 and (int((f - .1) / .006) % 2 == 0) and ((u + .012) % .006) < .003:
            return tone - 0.8, None
    if kind == "gun" and abs(nl) > 0.8:                     # bolts on the core
        for rf, ru in ((-.08, -.018), (.21, -.015)) if name == "IonCarbine" else ((-.1, -.035), (.15, -.035), (.15, .035)):
            if math.hypot(f - rf, u - ru) < .0028:
                return tone - 1.0, None
    return tone, None


def paint(obs, atlas_name):
    atlas = np.zeros((H, W, 3), float)
    filled = np.zeros((H, W), bool)
    for ob in obs:
        me = ob.data
        uv = me.uv_layers[0]
        kinds = [KINDS[d.value] for d in me.attributes["kind"].data]
        bm = bmesh.new()
        bm.from_mesh(me)
        sharp = set()
        for e in bm.edges:
            fs = e.link_faces
            if len(fs) != 2 or math.degrees(fs[0].normal.angle(fs[1].normal, 0)) > SHARP_DEG:
                sharp.add(frozenset(v.index for v in e.verts))
        bm.free()
        me.calc_loop_triangles()
        tris_by_poly = {}
        for t in me.loop_triangles:
            tris_by_poly.setdefault(t.polygon_index, []).append(t)

        for p in me.polygons:
            kind = kinds[p.index]
            if kind == "glow":
                continue
            ramp = RAMPS[kind]
            n = p.normal
            lit_n = Vector((abs(n.x), n.y, n.z))           # both sides lit like the left
            base = 2.4 + (lit_n.dot(LIGHT) - 0.4) * 2.4 - (0.4 if kind == "dark" else 0.0)
            centre = p.center
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
                out = Vector((abs(out.x), out.y, out.z))
                lit = out.dot(EDGE_LIGHT) > 0.2
                edges.append((Vector(uv.data[la].uv) * Vector((W, H)), Vector(uv.data[lb].uv) * Vector((W, H)), lit))

            for t in tris_by_poly.get(p.index, []):
                P = [me.vertices[me.loops[li].vertex_index].co for li in t.loops]
                U = [Vector(uv.data[li].uv) * Vector((W, H)) for li in t.loops]
                x0, x1 = int(min(q.x for q in U)) - 1, int(max(q.x for q in U)) + 1
                y0, y1 = int(min(q.y for q in U)) - 1, int(max(q.y for q in U)) + 1
                den = (U[1].y - U[2].y) * (U[0].x - U[2].x) + (U[2].x - U[1].x) * (U[0].y - U[2].y)
                if abs(den) < 1e-9:
                    continue
                for ty in range(max(0, y0), min(H, y1 + 1)):
                    for tx in range(max(0, x0), min(W, x1 + 1)):
                        q = Vector((tx + 0.5, ty + 0.5))
                        w0 = ((U[1].y - U[2].y) * (q.x - U[2].x) + (U[2].x - U[1].x) * (q.y - U[2].y)) / den
                        w1 = ((U[2].y - U[0].y) * (q.x - U[2].x) + (U[0].x - U[2].x) * (q.y - U[2].y)) / den
                        w2 = 1 - w0 - w1
                        if min(w0, w1, w2) < -0.02:
                            continue
                        pos = P[0] * w0 + P[1] * w1 + P[2] * w2
                        tone = base + (pos.z - centre.z) * GRADIENT_PER_M + NOISE.get(kind, .3) * vnoise(pos, NOISE_SCALE)
                        tone, swap = detail(obs[0].name, kind, pos, n, centre, tone)
                        col = shade(RAMPS[swap] if swap else ramp, tone)
                        if edges:
                            dist, lit = min((seg_dist(q, a, b), l) for a, b, l in edges)
                            if dist < 1.0:
                                hi = (kind in METALS) and lit       # worn edges on lit metal
                                target = ramp[5] if lit else ramp[0]
                                col = mix(col, target, OUTLINE_STRENGTH * (0.75 if hi else 0.5 if lit else 1.0))
                        atlas[ty, tx] = col
                        filled[ty, tx] = True

    for _ in range(PAD + 1):                                 # grow patches into their gutters
        grown = filled.copy()
        src = atlas.copy()
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            sh_f = np.roll(filled, (dy, dx), (0, 1))
            sh_c = np.roll(src, (dy, dx), (0, 1))
            take = ~grown & sh_f
            atlas[take] = sh_c[take]
            grown |= take
        filled = grown

    out = ASSET_DIR / "Textures" / f"{atlas_name}_Atlas.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    img = bpy.data.images.new(f"{atlas_name}_Atlas", W, H, alpha=False)
    rgba = np.dstack([atlas / 255.0, np.ones((H, W, 1))]).astype(np.float32)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = str(out)
    img.file_format = "PNG"
    img.save()
    return img


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
    return mat


def glow_material():
    mat = bpy.data.materials.get("Glow") or bpy.data.materials.new("Glow")
    mat.diffuse_color = (0.35, 0.95, 1.0, 1.0)              # what the preview shows
    return mat


def assign_materials(obs, atlas_mat):
    glow = glow_material()
    for ob in obs:
        me = ob.data
        me.materials.clear()
        me.materials.append(atlas_mat)
        kinds = [KINDS[d.value] for d in me.attributes["kind"].data]
        if "glow" in kinds:
            me.materials.append(glow)
            for i, p in enumerate(me.polygons):
                p.material_index = 1 if kinds[i] == "glow" else 0


def render_previews(sets, folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.color_type = "FLAT", "TEXTURE"
    sh.show_backface_culling = True
    sc.render.resolution_x, sc.render.resolution_y = 900, 500
    tgt = bpy.data.objects.new("T", None)
    sc.collection.objects.link(tgt)
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = "ORTHO"
    cam.constraints.new("TRACK_TO").target = tgt
    everything = [o for s in sets for o in s]
    for obs in sets:
        for o in everything:
            o.hide_render = o not in obs
        tgt.location = G(.15, -.01, 0)
        for view, d in (("left", G(0, .15, 1)), ("right", G(0, .15, -1)), ("threeq", G(-.8, .5, .8)), ("fps", G(-1, .35, .45))):
            cam.data.ortho_scale = 1.0
            cam.location = tgt.location + d.normalized() * 2
            sc.render.filepath = str(folder / f"{obs[0].name}_{view}.png")
            bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    ASSET_DIR.mkdir(parents=True, exist_ok=True)
    sets = [build_carbine(), build_repeater()]
    for obs in sets:
        name = obs[0].name
        unwrap([o.data for o in obs])
        img = paint(obs, name)
        assign_materials(obs, make_material(name, img))
        for o in obs:
            tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
            print(f"GUN {o.name}: {len(o.data.vertices)} verts, {tris} tris")
        img.pack()
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "Guns_Textured.blend"), copy=True)
    for obs in sets:
        bpy.ops.object.select_all(action="DESELECT")
        for o in obs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = obs[0]
        bpy.ops.export_scene.fbx(
            filepath=str(ASSET_DIR / f"{obs[0].name}.fbx"), use_selection=True, object_types={"MESH"},
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z", axis_up="Y", bake_space_transform=True,
            mesh_smooth_type="FACE", path_mode="STRIP",
        )
    if "--preview" in argv:
        render_previews(sets, argv[argv.index("--preview") + 1])


main()
