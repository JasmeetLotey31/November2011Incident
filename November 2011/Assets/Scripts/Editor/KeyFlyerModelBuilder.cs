using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the flying key's model from the hand-made PS1 key (Assets/Models/FaceKeyPS1, left untouched):
/// - scales it up to the flying key's size (blade tip 2.5 m under the face centre);
/// - cuts the face disc along the seam between the painted lips: the top (forehead, eyes, ring) is
///   the upper jaw, the chin and the blade are the lower jaw, each pivoting on the back edge of the cut;
/// - caps both cuts with wet gums, and builds rows of crooked, yellowed fangs as separate meshes
///   (KeyFlyer shows them only while the mouth is open, so the shut face is exactly as painted);
/// - paints the gums and teeth a small atlas in the same style as his key texture;
/// - adds a low-poly sphere for the throat, gullet and tongue tip.
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
    // Where things are on his painted face (64 px tile in the top-left of the 128 px atlas).
    const int AtlasPixels = 128;
    const float LipRow = 47f;                                   // seam between the lips

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

    struct Vert { public Vector3 p, n; public Vector2 uv; public Color c; }

    sealed class Piece
    {
        public readonly List<Vert> verts = new List<Vert>();
        public readonly List<int>[] tris = { new List<int>(), new List<int>() }; // 0 = gold face, 1 = flesh & teeth

        public void Tri(int sub, Vert a, Vert b, Vert c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris[sub].Add(i); tris[sub].Add(i + 1); tris[sub].Add(i + 2);
        }

        /// <summary>A flat-shaded triangle whose front faces <paramref name="outward"/>.</summary>
        public void Flat(int sub, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0f) { (b, c) = (c, b); (ub, uc) = (uc, ub); n = -n; }
            n.Normalize();
            Tri(sub, new Vert { p = a, n = n, uv = ua, c = Color.white }, new Vert { p = b, n = n, uv = ub, c = Color.white },
                new Vert { p = c, n = n, uv = uc, c = Color.white });
        }

        public Mesh ToMesh(string name, Vector3 pivot, float scale)
        {
            var m = new Mesh { name = name };
            var p = new Vector3[verts.Count]; var n = new Vector3[verts.Count]; var uv = new Vector2[verts.Count]; var c = new Color[verts.Count];
            for (int i = 0; i < verts.Count; i++) { p[i] = (verts[i].p - pivot) * scale; n[i] = verts[i].n; uv[i] = verts[i].uv; c[i] = verts[i].c; }
            m.vertices = p; m.normals = n; m.uv = uv; m.colors = c;
            int subs = tris[1].Count > 0 ? 2 : 1;
            m.subMeshCount = subs;
            for (int s = 0; s < subs; s++) m.SetTriangles(tris[s], s);
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

        // Painted-face mapping: the face tile is laid flat on the disc front, so UV -> position is linear.
        Vector3[] dv = disc.vertices; Vector2[] duv = disc.uv; Vector3[] dn = disc.normals;
        Vector2 minP = Vector2.positiveInfinity, maxP = Vector2.negativeInfinity, minUV = Vector2.positiveInfinity, maxUV = Vector2.negativeInfinity;
        for (int i = 0; i < dv.Length; i++)
        {
            if (duv[i].x > .5f || duv[i].y < .5f || dn[i].z < .3f) continue;
            minP = Vector2.Min(minP, dv[i]); maxP = Vector2.Max(maxP, dv[i]);
            minUV = Vector2.Min(minUV, duv[i]); maxUV = Vector2.Max(maxUV, duv[i]);
        }
        Vector2 PixelToDisc(Vector2 px)
        {
            var uv = new Vector2((px.x + .5f) / AtlasPixels, 1f - (px.y + .5f) / AtlasPixels);
            return new Vector2(Mathf.Lerp(minP.x, maxP.x, Mathf.InverseLerp(minUV.x, maxUV.x, uv.x)),
                               Mathf.Lerp(minP.y, maxP.y, Mathf.InverseLerp(minUV.y, maxUV.y, uv.y)));
        }
        float cut = PixelToDisc(new Vector2(0f, LipRow)).y;

        // ---- cut the disc at the lips ----
        var upper = new Piece();
        var lower = new Piece();
        var section = new List<Vector2>(); // (x, z) points on the cut
        int[] t = disc.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            var tri = new Vert[3];
            for (int k = 0; k < 3; k++) tri[k] = new Vert { p = dv[t[i + k]], n = dn[t[i + k]], uv = duv[t[i + k]], c = Color.white };
            AddClipped(upper, tri, cut, true, section);
            AddClipped(lower, tri, cut, false, null);
        }
        List<Vector2> hull = ConvexHull(section);
        float zBack = float.PositiveInfinity, zFront = float.NegativeInfinity;
        foreach (Vector2 h in hull) { zBack = Mathf.Min(zBack, h.y); zFront = Mathf.Max(zFront, h.y); }
        var hinge = new Vector3(0f, cut, zBack);

        // ---- the other parts: ring on the upper jaw, blade and its ridge on the lower ----
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
                target.Tri(0, V(tt[i]), V(tt[i + 1]), V(tt[i + 2]));
            }
        }

        // ---- gums on both cuts ----
        Vector2 centre = Vector2.zero;
        foreach (Vector2 h in hull) centre += h;
        centre /= hull.Count;
        float xMin = float.PositiveInfinity, xMax = float.NegativeInfinity;
        foreach (Vector2 h in hull) { xMin = Mathf.Min(xMin, h.x); xMax = Mathf.Max(xMax, h.x); }
        // Gums use the left half of the mouth atlas: across = u, back of the mouth -> front = v.
        Vector2 GumUV(Vector2 xz) => new Vector2(.02f + .46f * Mathf.InverseLerp(xMin, xMax, xz.x), .03f + .94f * Mathf.InverseLerp(zBack, zFront, xz.y));
        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 a = hull[i], b = hull[(i + 1) % hull.Count];
            Vector3 A = new Vector3(a.x, cut, a.y), B = new Vector3(b.x, cut, b.y), C = new Vector3(centre.x, cut, centre.y);
            upper.Flat(1, A, B, C, GumUV(a), GumUV(b), GumUV(centre), Vector3.down);
            lower.Flat(1, A, B, C, GumUV(a), GumUV(b), GumUV(centre), Vector3.up);
        }

        // ---- fangs along the front of both cuts (inside the other jaw while the mouth is shut) ----
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

        // ---- fangs along the front of both cuts: their own meshes, shown only while the mouth is
        // open (KeyFlyer toggles them), so with the mouth shut the face is exactly as painted ----
        var upperFangs = new Piece();
        var lowerFangs = new Piece();
        float LipZ(float x, float hw, float halfD) =>
            Mathf.Min(SurfaceZ(new Vector2(x - hw, cut)), SurfaceZ(new Vector2(x + hw, cut))) - halfD - .002f;
        var rng = new System.Random(2011);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        float[] upperX = { -.060f, -.047f, -.034f, -.019f, -.006f, .007f, .020f, .034f, .047f, .060f };
        float[] upperLen = { .018f, .024f, .042f, .022f, .027f, .020f, .025f, .040f, .023f, .017f };
        float[] lowerX = { -.052f, -.040f, -.026f, -.012f, .002f, .014f, .027f, .041f, .053f };
        float[] lowerLen = { .015f, .022f, .032f, .018f, .020f, .016f, .030f, .021f, .014f };
        for (int i = 0; i < upperX.Length; i++)
        {
            bool canine = upperLen[i] > .035f;
            float hw = canine ? .0085f : R(.0055f, .007f);
            Tooth(upperFangs, 0, new Vector3(upperX[i], cut + .002f, LipZ(upperX[i], hw, .005f)), Vector3.down,
                  hw, .005f, upperLen[i] * R(.9f, 1.08f), R(-.003f, .003f), R(-.005f, -.001f), R(.52f, .56f));
        }
        for (int i = 0; i < lowerX.Length; i++)
        {
            bool canine = lowerLen[i] > .028f;
            float hw = canine ? .0075f : R(.005f, .0065f);
            Tooth(lowerFangs, 0, new Vector3(lowerX[i], cut - .002f, LipZ(lowerX[i], hw, .0045f)), Vector3.up,
                  hw, .0045f, lowerLen[i] * R(.9f, 1.08f), R(-.003f, .003f), R(-.005f, -.001f), R(.52f, .56f));
        }

        // ---- save ----
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Models", "KeyFlyer");
        Mesh head = Save(upper.ToMesh("KeyFlyer_UpperJaw", hinge, Scale), OutDir + "/KeyFlyer_UpperJaw.asset");
        Mesh jaw = Save(lower.ToMesh("KeyFlyer_LowerJaw", hinge, Scale), OutDir + "/KeyFlyer_LowerJaw.asset");
        Mesh fangsUp = Save(upperFangs.ToMesh("KeyFlyer_UpperFangs", hinge, Scale), OutDir + "/KeyFlyer_UpperFangs.asset");
        Mesh fangsDown = Save(lowerFangs.ToMesh("KeyFlyer_LowerFangs", hinge, Scale), OutDir + "/KeyFlyer_LowerFangs.asset");
        Mesh sphere = Save(LowSphere(8, 6), OutDir + "/LowSphere.asset");
        BuildMaterials();
        AssetDatabase.SaveAssets();

        Vector3 hingeScaled = hinge * Scale;
        return $"cut y={cut:F4} hinge(disc)={hinge:F4} hinge(model)={hingeScaled:F3} front z={zFront * Scale:F3} width={(hull.Count > 0 ? Width(hull) * Scale : 0):F3}\n" +
               $"upper {head.triangles.Length / 3} tris, lower {jaw.triangles.Length / 3} tris, fangs {fangsUp.triangles.Length / 3}+{fangsDown.triangles.Length / 3}, sphere {sphere.triangles.Length / 3}\n" +
               $"upper bounds {head.bounds.min:F2}..{head.bounds.max:F2}  lower bounds {jaw.bounds.min:F2}..{jaw.bounds.max:F2}";
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

    static Texture2D PaintMouth(string path)
    {
        int n = MouthPixels, half = n / 2;
        var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = (y + .5f) / n;
                Color c;
                if (x < half)
                {
                    // Gums: dark down the throat, wet red toward the teeth, lit from the left.
                    float u = (x + .5f) / half;
                    float f = .6f + 3.4f * Mathf.Pow(v, .8f) - .5f * u
                              + NoiseAmount * Blotch(x * .32f, y * .32f, 1f) + .25f * Blotch(x * .9f, y * .9f, 2f);
                    if (v > .9f) f += .5f;              // the rim of the gum where the teeth come out
                    c = Shade(GumRamp, f);
                }
                else
                {
                    // Teeth: stained at the root, old ivory at the tip.
                    float u = (x - half + .5f) / half;
                    float f = .4f + 4.2f * Mathf.SmoothStep(0f, 1f, v) - .35f * Mathf.Abs(u - .3f)
                              + NoiseAmount * Blotch(x * .45f, y * .35f, 3f);
                    c = Shade(ToothRamp, f);
                }
                tex.SetPixel(x, y, c);
            }
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

    static Mesh Save(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        existing.Clear();
        existing.vertices = mesh.vertices; existing.normals = mesh.normals; existing.uv = mesh.uv; existing.colors = mesh.colors;
        existing.subMeshCount = mesh.subMeshCount;
        for (int s = 0; s < mesh.subMeshCount; s++) existing.SetTriangles(mesh.GetTriangles(s), s);
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
        face.SetFloat("_Lighting", 1f);
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
        flesh.SetFloat("_Lighting", 1f);
        flesh.SetFloat("_VertexColors", 0f);
        flesh.DisableKeyword("_VERTEX_COLORS");
        flesh.enableInstancing = true;
        EditorUtility.SetDirty(flesh);
    }
}
