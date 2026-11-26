using UnityEngine;
using UnityEngine.UI;

/// <summary>Small helpers for HUDs built in code (uGUI): canvases, images, text, generated sprites.</summary>
public static class HudKit
{
    public static readonly Color Ivory = new Color(.92f, .88f, .8f, 1f);
    public static readonly Color Danger = new Color(1f, .22f, .15f, 1f);
    public static readonly Color Panel = new Color(0f, 0f, 0f, .55f);

    static Font font;
    public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    public static GameObject Canvas(string name, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.hideFlags = HideFlags.DontSave;
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;
        return go;
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static Image Img(string name, Transform parent, Color color)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    public static Text Txt(string name, Transform parent, int size, TextAnchor anchor, FontStyle style = FontStyle.Bold)
    {
        var t = Rect(name, parent).gameObject.AddComponent<Text>();
        t.font = Font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = anchor;
        t.color = Ivory;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var shadow = t.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, .85f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    /// <summary>Anchor + pivot at <paramref name="anchor"/>, placed at <paramref name="position"/>.</summary>
    public static void Place(RectTransform r, Vector2 anchor, Vector2 position, Vector2 size)
    {
        r.anchorMin = r.anchorMax = r.pivot = anchor;
        r.anchoredPosition = position;
        r.sizeDelta = size;
    }

    public static void Stretch(RectTransform r, float inset = 0f)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(inset, inset);
        r.offsetMax = new Vector2(-inset, -inset);
    }

    public static Sprite Circle(int size = 32)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        float r = size * .5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(r, r));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
    }

    /// <summary>A soft-edged triangle pointing up (for direction arrows).</summary>
    public static Sprite Triangle(int size = 64)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float v = y / (size - 1f);                     // 0 bottom .. 1 tip
                float halfWidth = (1f - v) * .5f;              // narrows to the tip
                float dx = Mathf.Abs(x / (size - 1f) - .5f);
                float a = Mathf.Clamp01((halfWidth - dx) * size * .5f) * Mathf.Clamp01(v * size * .5f + 1f);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
    }

    /// <summary>A ring of jagged fangs around the screen edge pointing inward (seen from inside a mouth).</summary>
    public static Texture2D TeethRing(int size = 256, int teeth = 22)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
        var jitter = new float[teeth];
        for (int i = 0; i < teeth; i++) jitter[i] = Random.Range(.75f, 1.2f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x / (size - 1f) - .5f, y / (size - 1f) - .5f) * 2f;
                float r = p.magnitude;
                float ang = (Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) + 1f) % 1f;
                int i = Mathf.FloorToInt(ang * teeth) % teeth;
                float local = ang * teeth - Mathf.Floor(ang * teeth); // 0..1 across one tooth
                float tip = .55f + .25f * (1f - jitter[i]);           // how far in this tooth reaches
                float toothEdge = Mathf.Lerp(1f, tip, 1f - Mathf.Abs(local - .5f) * 2f); // triangle
                float gum = .97f;
                float a = r > gum ? 1f : r > toothEdge ? 1f : 0f;
                float shade = r > gum ? .12f : Mathf.Lerp(.95f, .6f, (r - toothEdge) * 4f);
                Color c = r > gum ? new Color(.25f, 0f, 0f, 1f) : new Color(shade, shade * .92f, shade * .75f, 1f);
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
            }
        tex.Apply();
        return tex;
    }

    /// <summary>A blood splatter for the screen: dense centre, droplets, a drip trail.</summary>
    public static Texture2D BloodSplat(int size = 128)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
        var blobs = new System.Collections.Generic.List<Vector3> { new Vector3(.5f, .55f, .2f) };
        for (int i = 0; i < 12; i++)
        {
            float ang = Random.value * Mathf.PI * 2f, d = Random.Range(.1f, .42f);
            blobs.Add(new Vector3(.5f + Mathf.Cos(ang) * d, .55f + Mathf.Sin(ang) * d, Random.Range(.015f, .07f)));
        }
        for (int i = 0; i < 6; i++) blobs.Add(new Vector3(.5f + Random.Range(-.05f, .05f), .45f - i * .06f, .035f - i * .004f)); // drip
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + .5f) / size, (y + .5f) / size);
                float v = 0f;
                foreach (Vector3 b in blobs) v = Mathf.Max(v, Mathf.Clamp01((b.z - Vector2.Distance(p, new Vector2(b.x, b.y))) * size * .3f));
                tex.SetPixel(x, y, new Color(.45f, .01f, .01f, v * .9f));
            }
        tex.Apply();
        return tex;
    }

    public static Sprite White() => Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));

    /// <summary>Soft dark edges, clear centre (for damage vignettes).</summary>
    public static Texture2D Vignette(int size = 128)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x / (size - 1f) - .5f, y / (size - 1f) - .5f) * 2f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p.magnitude - .45f) / .75f));
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        tex.Apply();
        return tex;
    }

    /// <summary>Random grey noise for TV static.</summary>
    public static Texture2D Noise(int size = 256)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float v = Random.value;
                tex.SetPixel(x, y, new Color(v, v, v, 1f));
            }
        tex.Apply();
        return tex;
    }

    /// <summary>Horizontal scanlines (tile vertically).</summary>
    public static Texture2D Scanlines()
    {
        var tex = new Texture2D(1, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, new Color(0, 0, 0, .35f));
        tex.SetPixel(0, 1, new Color(0, 0, 0, 0f));
        tex.SetPixel(0, 2, new Color(0, 0, 0, 0f));
        tex.SetPixel(0, 3, new Color(0, 0, 0, .1f));
        tex.Apply();
        return tex;
    }
}
