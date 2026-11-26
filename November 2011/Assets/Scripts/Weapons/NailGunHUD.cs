using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Nail gun HUD, built at runtime (no prefab or art needed):
/// - bottom-right nail strip: one tick per loaded nail, "loaded / spare" count, reload bar;
/// - a small centre crosshair;
/// - an "E" prompt when you look at nails you can pick up.
/// Styled after the Western project's RevolverAmmoHUD (brass rim on dark leather), with the
/// six-chamber cylinder replaced by a nail strip. Put it on the same object as the NailGun.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NailGun))]
public sealed class NailGunHUD : MonoBehaviour
{
    [SerializeField] Vector2 screenOffset = new Vector2(-34f, 30f);
    [SerializeField] bool showCrosshair = true;

    static readonly Color Brass = new Color(0.72f, 0.48f, 0.20f, 1f);
    static readonly Color Leather = new Color(0.055f, 0.038f, 0.030f, 0.92f);
    static readonly Color LoadedNail = new Color(0.86f, 0.88f, 0.90f, 1f);
    static readonly Color EmptySlot = new Color(0.16f, 0.12f, 0.10f, 1f);
    static readonly Color Ivory = new Color(0.92f, 0.84f, 0.68f, 1f);
    static readonly Color Warning = new Color(0.95f, 0.35f, 0.2f, 1f);

    NailGun gun;
    GameObject uiRoot;
    Image[] nailTicks, nailHeads;
    Text countText;
    Image reloadFill;
    RectTransform reloadBar;
    Text promptText;
    Font font;
    Texture2D circleTexture;
    Sprite circleSprite;
    int lastLoaded = -1, lastReserve = -1;

    void Awake()
    {
        gun = GetComponent<NailGun>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
    }

    void OnEnable()
    {
        if (uiRoot != null) uiRoot.SetActive(true);
    }

    void OnDisable()
    {
        if (uiRoot != null) uiRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (uiRoot != null) Destroy(uiRoot);
        if (circleSprite != null) Destroy(circleSprite);
        if (circleTexture != null) Destroy(circleTexture);
    }

    void LateUpdate()
    {
        if (gun.Loaded != lastLoaded || gun.Reserve != lastReserve) RefreshAmmo();

        reloadBar.gameObject.SetActive(gun.IsReloading);
        if (gun.IsReloading) reloadFill.fillAmount = gun.ReloadProgress;

        NailPickup pickup = gun.LookedAtPickup();
        promptText.gameObject.SetActive(pickup != null);
        if (pickup != null)
            promptText.text = gun.ReserveFull ? "Can't carry more nails"
                : pickup.Nails > 1 ? $"[E] Take nail strip (+{pickup.Nails})"
                : "[E] Pull out nail";
    }

    void RefreshAmmo()
    {
        lastLoaded = gun.Loaded;
        lastReserve = gun.Reserve;
        for (int i = 0; i < nailTicks.Length; i++)
            nailTicks[i].color = nailHeads[i].color = i < gun.Loaded ? LoadedNail : EmptySlot;
        countText.text = $"{gun.Loaded} / {gun.Reserve}";
        countText.color = gun.Loaded == 0 ? Warning : Ivory;
    }

    void Build()
    {
        CreateCircleSprite();

        uiRoot = new GameObject("Runtime Nail Gun HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        uiRoot.hideFlags = HideFlags.DontSave;
        var canvas = uiRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        var scaler = uiRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // --- nail strip panel (bottom right) ---
        int slots = Mathf.Max(1, gun.MagazineSize);
        float stripWidth = slots * 14f + 16f;
        RectTransform panel = Rect("Nail Strip Panel", uiRoot.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
        panel.sizeDelta = new Vector2(stripWidth + 96f, 62f);
        panel.anchoredPosition = screenOffset;

        Image rim = Img("Brass Rim", panel, Brass);
        Stretch(rim.rectTransform, 0f);
        Image face = Img("Leather Face", panel, Leather);
        Stretch(face.rectTransform, 3f);

        RectTransform strip = Rect("Strip", panel);
        strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(0f, 0.5f);
        strip.sizeDelta = new Vector2(stripWidth, 36f);
        strip.anchoredPosition = new Vector2(10f, 4f);
        Image stripBacking = Img("Strip Backing", strip, new Color(0.30f, 0.20f, 0.10f, 1f));
        Stretch(stripBacking.rectTransform, 0f);

        nailTicks = new Image[slots];
        nailHeads = new Image[slots];
        for (int i = 0; i < slots; i++)
        {
            // A nail: thin shaft with a round head on top.
            Image shaft = Img("Nail " + (i + 1), strip, LoadedNail);
            RectTransform s = shaft.rectTransform;
            s.anchorMin = s.anchorMax = s.pivot = new Vector2(0f, 0.5f);
            s.sizeDelta = new Vector2(4f, 26f);
            s.anchoredPosition = new Vector2(10f + i * 14f, -2f);
            Image head = Img("Head", s, Color.white);
            head.sprite = circleSprite;
            RectTransform h = head.rectTransform;
            h.anchorMin = h.anchorMax = h.pivot = new Vector2(0.5f, 1f);
            h.sizeDelta = new Vector2(10f, 5f);
            h.anchoredPosition = new Vector2(0f, 3f);
            nailTicks[i] = shaft;
            nailHeads[i] = head;
        }

        // Reload bar under the strip.
        reloadBar = Rect("Reload Bar", panel);
        reloadBar.anchorMin = reloadBar.anchorMax = reloadBar.pivot = new Vector2(0f, 0f);
        reloadBar.sizeDelta = new Vector2(stripWidth, 5f);
        reloadBar.anchoredPosition = new Vector2(10f, 6f);
        Image barBack = Img("Back", reloadBar, EmptySlot);
        Stretch(barBack.rectTransform, 0f);
        reloadFill = Img("Fill", reloadBar, Brass);
        Stretch(reloadFill.rectTransform, 0f);
        reloadFill.type = Image.Type.Filled;
        reloadFill.fillMethod = Image.FillMethod.Horizontal;
        reloadFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));
        reloadBar.gameObject.SetActive(false);

        countText = Txt("Count", panel, 26, TextAnchor.MiddleRight);
        RectTransform c = countText.rectTransform;
        c.anchorMin = c.anchorMax = c.pivot = new Vector2(1f, 0.5f);
        c.sizeDelta = new Vector2(86f, 40f);
        c.anchoredPosition = new Vector2(-12f, 2f);

        // --- crosshair ---
        if (showCrosshair)
        {
            RectTransform cross = Rect("Crosshair", uiRoot.transform);
            cross.anchorMin = cross.anchorMax = cross.pivot = new Vector2(.5f, .5f);
            cross.sizeDelta = new Vector2(24f, 24f);
            Image dotShadow = Img("Dot Shadow", cross, new Color(0, 0, 0, .55f));
            dotShadow.sprite = circleSprite;
            Center(dotShadow.rectTransform, Vector2.zero, new Vector2(10f, 10f));
            Image dot = Img("Dot", cross, Ivory);
            dot.sprite = circleSprite;
            Center(dot.rectTransform, Vector2.zero, new Vector2(6f, 6f));
            Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            foreach (Vector2 d in dirs)
            {
                Vector2 size = d.x != 0 ? new Vector2(10f, 3f) : new Vector2(3f, 10f);
                Image tickShadow = Img("Tick Shadow", cross, new Color(0, 0, 0, .5f));
                Center(tickShadow.rectTransform, d * 14f + new Vector2(1f, -1f), size);
                Image tick = Img("Tick", cross, new Color(Ivory.r, Ivory.g, Ivory.b, .9f));
                Center(tick.rectTransform, d * 14f, size);
            }
        }

        // --- pickup prompt (just below the crosshair) ---
        promptText = Txt("Pickup Prompt", uiRoot.transform, 22, TextAnchor.MiddleCenter);
        Center(promptText.rectTransform, new Vector2(0f, -48f), new Vector2(480f, 32f));
        promptText.gameObject.SetActive(false);

        RefreshAmmo();
    }

    RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Image Img(string name, Transform parent, Color color)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    Text Txt(string name, Transform parent, int size, TextAnchor anchor)
    {
        var t = Rect(name, parent).gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = anchor;
        t.color = Ivory;
        t.raycastTarget = false;
        var shadow = t.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, .8f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    static void Stretch(RectTransform r, float inset)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(inset, inset);
        r.offsetMax = new Vector2(-inset, -inset);
    }

    static void Center(RectTransform r, Vector2 position, Vector2 size)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
        r.anchoredPosition = position;
        r.sizeDelta = size;
    }

    void CreateCircleSprite()
    {
        const int size = 32;
        circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        float r = size * .5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(r, r));
            circleTexture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d)));
        }
        circleTexture.Apply();
        circleSprite = Sprite.Create(circleTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
    }
}
