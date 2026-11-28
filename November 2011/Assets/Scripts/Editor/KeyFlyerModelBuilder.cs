using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the flying key's model from the hand-made PS1 key (Assets/Models/FaceKeyPS1, left untouched):
/// - scales it up to the flying key's size (blade tip 2.5 m under the face centre);
/// - makes his face ONE skinned mesh on two bones, the upper and lower jaw (KeyFlyer swings them and
///   pulls them apart). Only his painted mouth opens: the face splits along the seam between his painted
///   lips, from one lip corner to the other, into a lens (wide open in the middle, shut at the corners).
///   Round it his own painted skin stretches, so the cheeks, chin and back of the head stay joined and
///   the face, lips and mouth are one surface;
/// - the inside of the mouth is the roof and floor of that same cut, skinned the same way, so it meets
///   his lips exactly: open at the front, closing into a crease at the back of the throat. Painted gums
///   turn into his own lip colours at the front; strings of wet flesh stretch between roof and floor;
/// - rows of crooked, yellowed fangs along his lips on the same bones (their own meshes: KeyFlyer shows
///   them only while the mouth is open, so the shut face is exactly as painted);
/// - ember pupils on his painted pupils;
/// - paints the gums and teeth a small atlas in the same style as his key texture;
/// - a low-poly sphere for the throat glow and tongue tip.
/// Writes meshes to Assets/Models/KeyFlyer/ and materials to Assets/Materials/Keys/; the KeyFlyer
/// prefab references them, so re-running this after the source model changes updates the keys.
/// Menu: Tools / Keys / Rebuild Flying Key Model.
/// </summary>
public static class KeyFlyerModelBuilder
{
    const string SourceFbx = "Assets/Models/FaceKeyPS1/FaceKeyPS1.fbx";
    const string SourceMaterial = "Assets/Models/FaceKeyPS1/FaceKeyPS1.mat";
    const string OutDir = "Assets/Models/KeyFlyer";
    const string MaterialDir = "Assets/Materials/Keys";
    const string DiscName = "Circle";

    /// <summary>His key is ~0.45 m tall; ours hang ~3 m.</summary>
    public const float Scale = 7.55f;
    /// <summary>Where both jaws rest (and swing from) in the head, from the last build: the jaw bones sit here.</summary>
    public static Vector3 HingeModel { get; private set; }
    // Where things are on his painted face (64 px tile in the top-left of the 128 px atlas).
    const int AtlasPixels = 128;
    // The line between his painted lips: the dark line at rows 40-41 (upper lip above it, his bright lower
    // lip below; rows 47-50 are the shadow under the lower lip, not the mouth).
    const float LipRow = 40.5f;
    const float MouthLeftPx = 13.5f, MouthRightPx = 49.5f;      // where that line ends: his lip corners
    /// <summary>How far above and below his lips the skin stretches as the mouth opens (disc units; ~0.23 m on the key).</summary>
    const float StretchBand = .03f;
    // Pupils: the darkest centre of each painted eye (found by reading the texture), and their size.
    static readonly Vector2[] PupilPixels = { new Vector2(24.6f, 19.9f), new Vector2(38.9f, 19.5f) };
    const float PupilRadius = .003f;

    // Mouth atlas, painted the same way as his key (ArtSource/FaceKey/build_pixel_key.py): warm limited
    // ramps, a few soft steps between tones, soft light/dark blotches, light from the top-left, no dither.
    const int MouthPixels = 64;                                 // left half gums, right half teeth
    const int ToneSteps = 3;
    const float NoiseAmount = .4f;
    static readonly Color32[] GumRamp =
        { new Color32(30, 4, 8, 255), new Color32(66, 10, 16, 255), new Color32(108, 20, 26, 255),
          new Color32(150, 38, 40, 255), new Color32(184, 66, 60, 255), new Color32(212, 100, 84, 255) };
    static readonly Color32[] ToothRamp =
        { new Color32(74, 48, 24, 255), new Color32(120, 88, 48, 255), new Color32(166, 136, 88, 255),
          new Color32(204, 182, 134, 255), new Color32(230, 214, 170, 255), new Color32(244, 234, 200, 255) };

    /// <summary>A vertex; <see cref="w"/> is how much it follows the upper jaw (the rest follows the lower).</summary>
    struct Vert { public Vector3 p, n; public Vector2 uv; public Color c; public float w; }

    sealed class Piece
    {
        public readonly List<Vert> verts = new List<Vert>();
        public readonly List<int>[] tris = { new List<int>(), new List<int>() }; // 0 = his face, 1 = mouth flesh & teeth

        public void Tri(int sub, Vert a, Vert b, Vert c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris[sub].Add(i); tris[sub].Add(i + 1); tris[sub].Add(i + 2);
        }

        /// <summary>A flat-shaded triangle whose front faces <paramref name="outward"/>.</summary>
        public void Flat(int sub, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 outward,
                         float wa = 0f, float wb = 0f, float wc = 0f)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0f) { (b, c) = (c, b); (ub, uc) = (uc, ub); (wb, wc) = (wc, wb); n = -n; }
            n.Normalize();
            Tri(sub, new Vert { p = a, n = n, uv = ua, c = Color.white, w = wa }, new Vert { p = b, n = n, uv = ub, c = Color.white, w = wb },
                new Vert { p = c, n = n, uv = uc, c = Color.white, w = wc });
        }

        /// <summary>Sets the upper-jaw weight of vertices [from, to) from their position.</summary>
        public void Weigh(System.Func<Vector3, float> weight, int start = 0, int to = -1)
        {
            if (to < 0) to = verts.Count;
            for (int i = start; i < to; i++) { Vert v = verts[i]; v.w = weight(v.p); verts[i] = v; }
        }

        public void Append(Piece other)
        {
            int o = verts.Count;
            verts.AddRange(other.verts);
            for (int s = 0; s < 2; s++) foreach (int i in other.tris[s]) tris[s].Add(i + o);
        }

        /// <summary>Static mesh relative to <paramref name="pivot"/>, or (with <paramref name="bones"/>) a mesh skinned to
        /// the two jaws, both resting at <paramref name="pivot"/>, with its vertices in the head's space.</summary>
        public Mesh ToMesh(string name, Vector3 pivot, float scale, bool bones = false)
        {
            var m = new Mesh { name = name };
            Vector3 origin = bones ? Vector3.zero : pivot;
            var p = new Vector3[verts.Count]; var n = new Vector3[verts.Count]; var uv = new Vector2[verts.Count]; var c = new Color[verts.Count];
            for (int i = 0; i < verts.Count; i++) { p[i] = (verts[i].p - origin) * scale; n[i] = verts[i].n; uv[i] = verts[i].uv; c[i] = verts[i].c; }
            m.vertices = p; m.normals = n; m.uv = uv; m.colors = c;
            int subs = tris[1].Count > 0 && tris[0].Count > 0 ? 2 : 1;
            m.subMeshCount = subs;
            if (subs == 2) for (int s = 0; s < 2; s++) m.SetTriangles(tris[s], s);
            else m.SetTriangles(tris[0].Count > 0 ? tris[0] : tris[1], 0);
            if (bones)
            {
                var bw = new BoneWeight[verts.Count];
                for (int i = 0; i < verts.Count; i++)
                {
                    float w = Mathf.Clamp01(verts[i].w);
                    bw[i] = w >= .5f ? new BoneWeight { boneIndex0 = 0, weight0 = w, boneIndex1 = 1, weight1 = 1f - w }
                                     : new BoneWeight { boneIndex0 = 1, weight0 = 1f - w, boneIndex1 = 0, weight1 = w };
                }
                m.boneWeights = bw;
                Matrix4x4 bind = Matrix4x4.Translate(-pivot * scale);   // head space -> jaw space (both jaws rest at the hinge)
                m.bindposes = new[] { bind, bind };
            }
            m.RecalculateBounds();
            return m;
        }
    }

    [MenuItem("Tools/Keys/Rebuild Flying Key Model")]
    static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(SourceFbx);
        if (root == null) return "Missing " + SourceFbx;
        Transform discT = root.transform.Find(DiscName);
        Mesh disc = discT.GetComponent<MeshFilter>().sharedMesh;
        Matrix4x4 toDisc = discT.worldToLocalMatrix;
        Vector3[] dv = disc.vertices; Vector2[] duv = disc.uv; Vector3[] dn = disc.normals;
        int[] t = disc.triangles;
        Vector2 PixelUV(Vector2 px) => new Vector2((px.x + .5f) / AtlasPixels, 1f - (px.y + .5f) / AtlasPixels);
        Vector3 OnFace(Vector2 px) { UVToSurface(dv, duv, t, PixelUV(px), out Vector3 pos, out _); return pos; }
        float cut = UVToSurface(dv, duv, t, PixelUV(new Vector2(31f, LipRow)), out Vector3 seam, out _) ? seam.y : -.0462f;
        float B = StretchBand;

        // Front of the face at (x, y) in disc space (it's domed and bevelled, not flat).
        float SurfaceZ(Vector2 q)
        {
            float best = float.NegativeInfinity;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = dv[t[i]], b = dv[t[i + 1]], c = dv[t[i + 2]];
                float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(den) < 1e-9f) continue;
                float wa = ((b.y - c.y) * (q.x - c.x) + (c.x - b.x) * (q.y - c.y)) / den;
                float wb = ((c.y - a.y) * (q.x - c.x) + (a.x - c.x) * (q.y - c.y)) / den;
                float wc = 1f - wa - wb;
                if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                best = Mathf.Max(best, wa * a.z + wb * b.z + wc * c.z);
            }
            return best;
        }

        // ---- his disc, finer round the lips so the skin stretches smoothly; cut along the lips ----
        var discTris = new List<Vert>(t.Length);
        for (int i = 0; i < t.Length; i++) discTris.Add(new Vert { p = dv[t[i]], n = dn[t[i]], uv = duv[t[i]], c = Color.white });
        for (int level = 0; level < 2; level++) discTris = Subdivide(discTris, cut - B, cut + B);
        var upper = new Piece();
        var lower = new Piece();
        var section = new List<Vector2>(); // (x, z) points on the cut
        for (int i = 0; i < discTris.Count; i += 3)
        {
            var tri = new[] { discTris[i], discTris[i + 1], discTris[i + 2] };
            AddClipped(upper, tri, cut, true, section);
            AddClipped(lower, tri, cut, false, null);
        }
        List<Vector2> hull = ConvexHull(section);
        float zBack = float.PositiveInfinity, zFront = float.NegativeInfinity;
        foreach (Vector2 h in hull) { zBack = Mathf.Min(zBack, h.y); zFront = Mathf.Max(zFront, h.y); }
        var hinge = new Vector3(0f, cut, zBack);
        HingeModel = hinge * Scale;

        // ---- the other parts, clipped at the lips too (the top of the blade reaches up inside the face):
        // the ring goes with the upper jaw, the blade and its ridge with the lower ----
        foreach (Transform part in root.transform)
        {
            if (part == discT) continue;
            Mesh m = part.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 toD = toDisc * part.localToWorldMatrix;
            Piece target = toD.MultiplyPoint3x4(m.bounds.center).y > cut ? upper : lower;
            Vector3[] v = m.vertices; Vector3[] n = m.normals; Vector2[] uv = m.uv; int[] tt = m.triangles;
            for (int i = 0; i < tt.Length; i += 3)
            {
                Vert V(int j) => new Vert { p = toD.MultiplyPoint3x4(v[j]), n = toD.MultiplyVector(n[j]).normalized, uv = uv[j], c = Color.white };
                AddClipped(target, new[] { V(tt[i]), V(tt[i + 1]), V(tt[i + 2]) }, cut, target == upper, null);
            }
        }

        // ---- his mouth: the seam between his painted lips, corner to corner, on the front of the cut ----
        var lipEdge = new List<Vector2>();
        foreach (Vector2 s in section)
            if (s.y > 0f && !lipEdge.Exists(q => (q - s).sqrMagnitude < 1e-10f)) lipEdge.Add(s);
        lipEdge.Sort((a, b) => a.x.CompareTo(b.x));
        float cornerA = OnFace(new Vector2(MouthLeftPx, LipRow)).x, cornerB = OnFace(new Vector2(MouthRightPx, LipRow)).x;
        int Nearest(float x) { int best = 0; for (int i = 1; i < lipEdge.Count; i++) if (Mathf.Abs(lipEdge[i].x - x) < Mathf.Abs(lipEdge[best].x - x)) best = i; return best; }
        int i0 = Nearest(Mathf.Min(cornerA, cornerB)), i1 = Nearest(Mathf.Max(cornerA, cornerB));
        lipEdge = lipEdge.GetRange(i0, i1 - i0 + 1);
        float mouthL = lipEdge[0].x, mouthR = lipEdge[lipEdge.Count - 1].x, mouthMid = (mouthL + mouthR) * .5f;
        float frontZ = SurfaceZ(new Vector2(mouthMid, cut));
        float throatZ = zBack * .6f;                       // where the mouth closes into the back of the throat

        // How open the mouth is at a point of the cut: 1 in the middle of his lips, 0 at the lip corners,
        // all round the sides and at the back of the head; it closes going in, into the throat.
        float Open(Vector3 p)
        {
            float q = p.x < mouthMid ? (mouthMid - p.x) / (mouthMid - mouthL) : (p.x - mouthMid) / (mouthR - mouthMid);
            float lens = q >= 1f ? 0f : Mathf.Sqrt(1f - q * q);
            return lens * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(throatZ, frontZ * .85f, p.z));
        }
        // How much a point follows the upper jaw. On the cut the lips part by how open it is there; above
        // and below, his skin stretches over StretchBand and then moves with its own jaw.
        float UpperWeight(Vector3 p, bool upperSide)
        {
            float m = Open(p);
            float edge = upperSide ? .5f + .5f * m : .5f - .5f * m;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Abs(p.y - cut) / B);
            return Mathf.Lerp(edge, upperSide ? 1f : 0f, k);
        }

        // ---- inside the mouth: the roof and floor of the cut, from his lips back to the throat ----
        Vector2 GumUV(Vector3 p) => new Vector2(.02f + .46f * Mathf.InverseLerp(mouthL, mouthR, p.x), .03f + .94f * Mathf.InverseLerp(throatZ, frontZ, p.z));
        const int Depth = 6;
        for (int i = 0; i + 1 < lipEdge.Count; i++)
            for (int j = 0; j < Depth; j++)
            {
                Vector3 P(int ii, int jj) { Vector2 f = lipEdge[ii]; return new Vector3(f.x, cut, Mathf.Lerp(f.y, throatZ - .004f, (float)jj / Depth)); }
                Vector3 a = P(i, j), b = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
                upper.Flat(1, a, b, c, GumUV(a), GumUV(b), GumUV(c), Vector3.down);
                upper.Flat(1, a, c, d, GumUV(a), GumUV(c), GumUV(d), Vector3.down);
                lower.Flat(1, a, b, c, GumUV(a), GumUV(b), GumUV(c), Vector3.up);
                lower.Flat(1, a, c, d, GumUV(a), GumUV(c), GumUV(d), Vector3.up);
            }
        upper.Weigh(p => UpperWeight(p, true));
        lower.Weigh(p => UpperWeight(p, false));

        // ---- strings of wet flesh between roof and floor, sagging (fraction across the mouth, depth 0 throat..1 lips, width, sag) ----
        var strings = new Piece();
        foreach (var st in new (float across, float depth, float width, float sag)[] { (-.62f, .55f, .045f, .07f), (.66f, .5f, .035f, .05f), (.3f, .3f, .022f, .09f) })
        {
            float x = st.across < 0f ? Mathf.Lerp(mouthMid, mouthL, -st.across) : Mathf.Lerp(mouthMid, mouthR, st.across);
            float z = Mathf.Lerp(throatZ, frontZ, st.depth);
            float hw = st.width * .5f / Scale, sag = st.sag / Scale;
            var top = new Vector3(x, cut, z);
            float wTop = UpperWeight(top, true), wBot = UpperWeight(top, false), wMid = (wTop + wBot) * .5f;
            Vector3 tl = top - Vector3.right * hw, tr = top + Vector3.right * hw;
            Vector3 ml = top + new Vector3(hw * .1f, -sag, .001f), mr = top + new Vector3(hw * .7f, -sag, .001f);
            Vector2 U(float s, float v) => new Vector2(.1f + .1f * s, .5f + .25f * v);
            foreach (Vector3 facing in new[] { Vector3.forward, Vector3.back })
            {
                strings.Flat(1, tl, tr, mr, U(0f, 1f), U(1f, 1f), U(1f, .5f), facing, wTop, wTop, wMid);
                strings.Flat(1, tl, mr, ml, U(0f, 1f), U(1f, .5f), U(0f, .5f), facing, wTop, wMid, wMid);
                strings.Flat(1, ml, mr, tr, U(0f, .5f), U(1f, .5f), U(1f, 0f), facing, wMid, wMid, wBot);
                strings.Flat(1, ml, tr, tl, U(0f, .5f), U(1f, 0f), U(0f, 0f), facing, wMid, wBot, wBot);
            }
        }

        var face = new Piece();
        face.Append(upper); face.Append(lower); face.Append(strings);

        // ---- fangs along his lips: their own meshes, shown only while the mouth is open (KeyFlyer toggles
        // them), each riding on the lip where it grows, so the ones near the corners part less ----
        var upperFangs = new Piece();
        var lowerFangs = new Piece();
        float LipZ(float x, float hw, float halfD) =>
            Mathf.Min(SurfaceZ(new Vector2(x - hw, cut)), SurfaceZ(new Vector2(x + hw, cut))) - halfD - .002f;
        float AlongLips(float f) => f < 0f ? Mathf.Lerp(mouthMid, mouthL, -f) : Mathf.Lerp(mouthMid, mouthR, f);
        var rng = new System.Random(2011);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        float[] upperF = { -.8f, -.57f, -.35f, -.12f, .1f, .33f, .56f, .79f };
        float[] upperLen = { .011f, .016f, .027f, .014f, .018f, .026f, .015f, .01f };
        float[] lowerF = { -.68f, -.44f, -.2f, .04f, .28f, .51f, .72f };
        float[] lowerLen = { .009f, .015f, .02f, .012f, .019f, .013f, .009f };
        for (int i = 0; i < upperF.Length; i++)
        {
            bool canine = upperLen[i] > .024f;
            float hw = canine ? .007f : R(.0045f, .0058f), x = AlongLips(upperF[i]);
            var rootP = new Vector3(x, cut + .002f, LipZ(x, hw, .005f));
            int start = upperFangs.verts.Count;
            Tooth(upperFangs, 0, rootP, Vector3.down, hw, .005f, upperLen[i] * R(.9f, 1.08f), R(-.003f, .003f), R(-.005f, -.001f), .5f + .05f * rng.Next(3));
            float w = UpperWeight(rootP, true);
            upperFangs.Weigh(_ => w, start);
        }
        for (int i = 0; i < lowerF.Length; i++)
        {
            bool canine = lowerLen[i] > .018f;
            float hw = canine ? .006f : R(.004f, .0052f), x = AlongLips(lowerF[i]);
            var rootP = new Vector3(x, cut - .002f, LipZ(x, hw, .0045f));
            int start = lowerFangs.verts.Count;
            Tooth(lowerFangs, 0, rootP, Vector3.up, hw, .0045f, lowerLen[i] * R(.9f, 1.08f), R(-.003f, .003f), R(-.005f, -.001f), .5f + .05f * rng.Next(3));
            float w = UpperWeight(rootP, false);
            lowerFangs.Weigh(_ => w, start);
        }

        // ---- ember pupils: exactly where the painted pupils are on the surface (found through the
        // texture coordinates of his mesh, not a guessed position); they sit well above the stretch ----
        var pupils = new Piece();
        foreach (Vector2 px in PupilPixels)
        {
            if (!UVToSurface(dv, duv, t, PixelUV(px), out Vector3 at, out Vector3 nrm)) continue;
            Vector3 c = at + nrm * .0016f;
            Vector3 ax = Vector3.Cross(nrm, Vector3.up).normalized, ay = Vector3.Cross(ax, nrm).normalized;
            for (int k = 0; k < 6; k++)
            {
                float a0 = k * Mathf.PI / 3f, a1 = (k + 1) * Mathf.PI / 3f;
                pupils.Flat(0, c, c + (ax * Mathf.Cos(a0) + ay * Mathf.Sin(a0)) * PupilRadius, c + (ax * Mathf.Cos(a1) + ay * Mathf.Sin(a1)) * PupilRadius,
                            Vector2.zero, Vector2.zero, Vector2.zero, nrm);
            }
        }

        // ---- save ----
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Models", "KeyFlyer");
        Mesh head = Save(face.ToMesh("KeyFlyer_Face", hinge, Scale, true), OutDir + "/KeyFlyer_Face.asset");
        Mesh fangsUp = Save(upperFangs.ToMesh("KeyFlyer_UpperFangs", hinge, Scale, true), OutDir + "/KeyFlyer_UpperFangs.asset");
        Mesh fangsDown = Save(lowerFangs.ToMesh("KeyFlyer_LowerFangs", hinge, Scale, true), OutDir + "/KeyFlyer_LowerFangs.asset");
        Mesh eyes = Save(pupils.ToMesh("KeyFlyer_Pupils", hinge, Scale), OutDir + "/KeyFlyer_Pupils.asset");
        Mesh sphere = Save(LowSphere(8, 6), OutDir + "/LowSphere.asset");
        BuildMaterials();
        AssetDatabase.SaveAssets();

        return $"cut y={cut:F4} hinge(model)={hinge * Scale:F3} mouth {mouthL * Scale:F3}..{mouthR * Scale:F3} ({lipEdge.Count} lip points) front z={frontZ * Scale:F3} throat z={throatZ * Scale:F3}\n" +
               $"face {head.triangles.Length / 3} tris ({head.vertexCount} verts), fangs {fangsUp.triangles.Length / 3}+{fangsDown.triangles.Length / 3}, pupils {eyes.triangles.Length / 3}, sphere {sphere.triangles.Length / 3}\n" +
               $"face bounds {head.bounds.min:F2}..{head.bounds.max:F2}";
    }

    /// <summary>Splits every triangle that reaches into [lo, hi] (in y) into four. Triangles left whole lie
    /// entirely outside, where the skin moves rigidly, so the T-junctions on their edges never crack.</summary>
    static List<Vert> Subdivide(List<Vert> tris, float lo, float hi)
    {
        var o = new List<Vert>(tris.Count * 2);
        Vert Mid(Vert a, Vert b) => new Vert { p = (a.p + b.p) * .5f, n = (a.n + b.n).normalized, uv = (a.uv + b.uv) * .5f, c = Color.white };
        for (int i = 0; i < tris.Count; i += 3)
        {
            Vert a = tris[i], b = tris[i + 1], c = tris[i + 2];
            float min = Mathf.Min(a.p.y, Mathf.Min(b.p.y, c.p.y)), max = Mathf.Max(a.p.y, Mathf.Max(b.p.y, c.p.y));
            if (max < lo || min > hi) { o.Add(a); o.Add(b); o.Add(c); continue; }
            Vert ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            o.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
        }
        return o;
    }

    /// <summary>Sutherland-Hodgman against the plane y = cut; keeps one side, fan-triangulated.</summary>
    static void AddClipped(Piece piece, Vert[] tri, float cut, bool above, List<Vector2> section)
    {
        var poly = new List<Vert>(4);
        for (int i = 0; i < 3; i++)
        {
            Vert a = tri[i], b = tri[(i + 1) % 3];
            bool ina = above ? a.p.y >= cut : a.p.y <= cut;
            bool inb = above ? b.p.y >= cut : b.p.y <= cut;
            if (ina) poly.Add(a);
            if (ina != inb)
            {
                float f = (cut - a.p.y) / (b.p.y - a.p.y);
                var x = new Vert { p = Vector3.Lerp(a.p, b.p, f), n = Vector3.Lerp(a.n, b.n, f).normalized, uv = Vector2.Lerp(a.uv, b.uv, f), c = Color.white };
                x.p.y = cut;
                poly.Add(x);
                section?.Add(new Vector2(x.p.x, x.p.z));
            }
        }
        for (int i = 1; i + 1 < poly.Count; i++) piece.Tri(0, poly[0], poly[i], poly[i + 1]);
    }

    static void Tooth(Piece piece, int sub, Vector3 root, Vector3 dir, float halfW, float halfD, float length, float tiltX, float tiltZ, float column)
    {
        // A crooked four-sided fang: root sunk into the gum, tip leaning a little. Its sides take
        // neighbouring strips of the teeth half of the mouth atlas (root at the bottom, tip at the top).
        Vector3 tip = root + dir * length + new Vector3(tiltX, 0f, tiltZ);
        Vector3[] c =
        {
            root + new Vector3(-halfW, 0f, -halfD), root + new Vector3(halfW, 0f, -halfD),
            root + new Vector3(halfW, 0f, halfD), root + new Vector3(-halfW, 0f, halfD),
        };
        Vector3 axis = root + dir * length * .3f;
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = c[i], b = c[(i + 1) % 4];
            float u0 = column + i * .1f, u1 = u0 + .1f;
            piece.Flat(sub, a, b, tip, new Vector2(u0, .04f), new Vector2(u1, .04f), new Vector2((u0 + u1) * .5f, .96f), (a + b) * .5f - axis);
        }
    }

    // ---------------- painting (same rules as build_pixel_key.py) ----------------

    static Color Shade(Color32[] ramp, float f)
    {
        f = Mathf.Clamp(f, 0f, ramp.Length - 1);
        int i = Mathf.Min(ramp.Length - 2, Mathf.FloorToInt(f));
        float t = Mathf.Round((f - i) * ToneSteps) / ToneSteps;
        return Color.Lerp(ramp[i], ramp[i + 1], t);
    }

    static float Hash(int a, int b, int c)
    {
        uint n = (uint)(a * 374761393 + b * 668265263 + c * 1274126177);
        n = (n ^ (n >> 13)) * 1274126177u;
        return (n & 0xFFFF) / 32767.5f - 1f;
    }

    /// <summary>Smooth value noise (-1..1): soft, irregular blotches like loose brushwork.</summary>
    static float Blotch(float x, float y, float seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(seed);
        float sx = x - xi, sy = y - yi;
        sx = sx * sx * (3f - 2f * sx); sy = sy * sy * (3f - 2f * sy);
        float a = Mathf.Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), sx);
        float b = Mathf.Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), sx);
        return Mathf.Lerp(a, b, sy);
    }

    // Wet flesh, dark down the throat; and his own lip colours (sampled from his painting: the seam
    // between the lips is (79,23,11), the lips (131,53,19)..(186,90,31)) so the inside of the mouth
    // continues straight on from the painted lips.
    static readonly Color32[] FleshRamp =
        { new Color32(10, 2, 3, 255), new Color32(30, 6, 7, 255), new Color32(58, 11, 12, 255),
          new Color32(88, 18, 17, 255), new Color32(116, 30, 22, 255), new Color32(140, 46, 26, 255) };
    static readonly Color32[] LipRamp =
        { new Color32(79, 23, 11, 255), new Color32(96, 29, 14, 255), new Color32(131, 53, 19, 255),
          new Color32(170, 79, 26, 255), new Color32(186, 90, 31, 255), new Color32(210, 120, 50, 255) };
    static readonly Color32[] RotRamp =
        { new Color32(30, 18, 10, 255), new Color32(54, 34, 18, 255), new Color32(84, 58, 30, 255),
          new Color32(118, 90, 52, 255), new Color32(150, 124, 80, 255), new Color32(176, 154, 108, 255) };

    static Texture2D PaintMouth(string path)
    {
        int n = MouthPixels, half = n / 2;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = (y + .5f) / n;
                Color c;
                if (x < half)
                {
                    // GUMS (u across the mouth, v from the back of the mouth to the lips).
                    float u = (x + .5f) / half;
                    float f = 4.4f * Mathf.Pow(v, 1.15f) + NoiseAmount * (Blotch(x * .3f, y * .3f, 1f) * 2f - .5f) - .35f * u;
                    if (Blotch(x * .55f, y * .55f, 9f) > .72f) f += .7f;                       // wet sheen, soft
                    if (Mathf.Abs(Blotch(x * .22f, y * .3f, 5f) - .5f) < .022f) f -= 1.1f;     // a thin vein
                    if (v > .64f && v < .88f)
                    {
                        float pocket = Mathf.Cos(u * Mathf.PI * 2f * 6.5f);
                        if (pocket > .55f) f -= .9f;                                          // dark pockets between fangs
                        else if (pocket < -.4f) f += .3f;                                     // gum swelling over the roots
                    }
                    c = Shade(FleshRamp, f);
                    if (v > .88f)                                                             // into his lips
                    {
                        float t = (v - .88f) / .12f;
                        Color lip = Shade(LipRamp, 1f + 2.6f * t + .3f * Blotch(x * .4f, y * .4f, 6f) - .3f * u);
                        c = Color.Lerp(c, lip, Mathf.Round(t * 3f) / 3f);
                    }
                }
                else
                {
                    // TEETH: each tooth side takes a strip; strips differ (some cracked, some rotten).
                    float u = (x - half + .5f) / half;
                    int band = Mathf.Min(4, (int)(u * 5f));
                    bool rotten = band == 4;
                    float f = .5f + 4.1f * Mathf.SmoothStep(0f, 1f, v) - .3f * Mathf.Abs(u - .3f)
                              + NoiseAmount * (Blotch(x * .45f, y * .35f, 3f) * 2f - .5f);
                    if (Blotch(x * .4f, y * .6f, 4f) < .28f) f -= .8f;                         // brown stains
                    if (band == 2 && (x % 9 == 4) && v > .3f) f -= 1.3f;                       // a crack
                    c = Shade(rotten ? RotRamp : ToothRamp, f);
                    if (v < .12f) c = Color.Lerp(Shade(FleshRamp, 3.2f), c, Mathf.Round(v / .12f * 3f) / 3f);   // gum line
                }
                px[y * n + x] = c;
            }
        return SaveTexture(px, n, path);
    }

    static Texture2D SaveTexture(Color[] px, int n, string path)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
        tex.SetPixels(px);
        tex.Apply();
        string dir = System.IO.Path.GetDirectoryName(path);
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(OutDir, "Textures");
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = false;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.maxTextureSize = n;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static float Width(List<Vector2> hull)
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        foreach (Vector2 h in hull) { min = Mathf.Min(min, h.x); max = Mathf.Max(max, h.x); }
        return max - min;
    }

    static List<Vector2> ConvexHull(List<Vector2> pts)
    {
        var p = new List<Vector2>(pts);
        p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        var h = new List<Vector2>();
        float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        for (int pass = 0; pass < 2; pass++)
        {
            int start = h.Count;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 q = pass == 0 ? p[i] : p[p.Count - 1 - i];
                while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 1e-9f) h.RemoveAt(h.Count - 1);
                h.Add(q);
            }
            h.RemoveAt(h.Count - 1);
        }
        return h;
    }

    /// <summary>Diameter-1 sphere with few faces (the built-in one is 768 triangles).</summary>
    static Mesh LowSphere(int segments, int rings)
    {
        var piece = new Piece();
        Vector3 P(int ring, int seg)
        {
            float th = Mathf.PI * ring / rings, ph = 2f * Mathf.PI * seg / segments;
            return new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * .5f;
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                Vector3 a = P(r, s), b = P(r, s + 1), c = P(r + 1, s), d = P(r + 1, s + 1);
                Vector3 mid = (a + b + c + d) * .25f;
                if (r > 0) SmoothTri(piece, a, b, c, mid);
                if (r < rings - 1) SmoothTri(piece, b, d, c, mid);
            }
        return piece.ToMesh("LowSphere", Vector3.zero, 1f);
    }

    static void SmoothTri(Piece piece, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
        Vert V(Vector3 p) => new Vert { p = p, n = p.normalized, uv = new Vector2(p.x + .5f, p.y + .5f), c = Color.white };
        piece.Tri(0, V(a), V(b), V(c));
    }

    /// <summary>The point on the face (and its normal) that carries texture coordinate <paramref name="uv"/>.</summary>
    static bool UVToSurface(Vector3[] v, Vector2[] uv, int[] tris, Vector2 target, out Vector3 pos, out Vector3 normal)
    {
        pos = normal = Vector3.zero;
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector2 a = uv[tris[i]], b = uv[tris[i + 1]], c = uv[tris[i + 2]];
            float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(den) < 1e-12f) continue;
            float wa = ((b.y - c.y) * (target.x - c.x) + (c.x - b.x) * (target.y - c.y)) / den;
            float wb = ((c.y - a.y) * (target.x - c.x) + (a.x - c.x) * (target.y - c.y)) / den;
            float wc = 1f - wa - wb;
            if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
            Vector3 pa = v[tris[i]], pb = v[tris[i + 1]], pc = v[tris[i + 2]];
            Vector3 n = Vector3.Cross(pb - pa, pc - pa).normalized;
            if (n.z < 0f) n = -n;                       // the face looks down +Z
            if (n.z < .3f) continue;                    // only the painted front
            pos = pa * wa + pb * wb + pc * wc;
            normal = n;
            return true;
        }
        return false;
    }

    static Mesh Save(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        existing.Clear();
        existing.vertices = mesh.vertices; existing.normals = mesh.normals; existing.uv = mesh.uv; existing.colors = mesh.colors;
        existing.subMeshCount = mesh.subMeshCount;
        for (int s = 0; s < mesh.subMeshCount; s++) existing.SetTriangles(mesh.GetTriangles(s), s);
        existing.boneWeights = mesh.boneWeights; existing.bindposes = mesh.bindposes;
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static void BuildMaterials()
    {
        // His painted gold, tuned for a key that gets right up in your face (less texture warp).
        string facePath = MaterialDir + "/Key_FaceKey.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(facePath) == null) AssetDatabase.CopyAsset(SourceMaterial, facePath);
        var face = AssetDatabase.LoadAssetAtPath<Material>(facePath);
        face.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        face.SetFloat("_Affine", .15f);
        face.SetFloat("_Lighting", .78f);   // a little self-lit: his face still reads in the shadows between the lights
        face.enableInstancing = true;
        EditorUtility.SetDirty(face);

        // Gums and teeth: their own small painted atlas.
        Texture2D mouth = PaintMouth(OutDir + "/Textures/KeyFlyer_Mouth.png");
        string fleshPath = MaterialDir + "/Key_MouthFlesh.mat";
        var flesh = AssetDatabase.LoadAssetAtPath<Material>(fleshPath);
        if (flesh == null) { flesh = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(flesh, fleshPath); }
        flesh.SetTexture("_BaseMap", mouth);
        flesh.SetColor("_BaseColor", Color.white);
        flesh.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        flesh.SetFloat("_Affine", .5f);
        flesh.SetFloat("_Lighting", .85f);  // nearly as self-lit as his face, so his lips run on into the mouth
        flesh.SetFloat("_VertexColors", 0f);
        flesh.DisableKeyword("_VERTEX_COLORS");
        flesh.enableInstancing = true;
        EditorUtility.SetDirty(flesh);
    }
}
