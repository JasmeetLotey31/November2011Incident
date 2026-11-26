using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gun HUD, built at runtime:
/// - bottom right: rounds left as bullets, spare count (red when you're nearly out);
/// - centre: a crosshair that opens with your spread (moving, sprinting, firing fast) and
///   tightens when you aim down sights; a hit marker (white hit, red face shot, bigger X on a kill);
/// - "RELOADING!" flashing while you reload (that's when the keys rush you), "EMPTY" when dry;
/// - ammo pickup prompt.
/// Put it on the gun (same object as Sidearm).
/// </summary>
[RequireComponent(typeof(Sidearm))]
[DisallowMultipleComponent]
public sealed class GunHUD : MonoBehaviour
{
    static readonly Color Brass = new Color(.85f, .62f, .25f, 1f);
    static readonly Color Spent = new Color(.18f, .16f, .14f, .9f);

    Sidearm gun;
    GameObject root;
    Image[] bullets;
    Text reserveText, statusText, promptText;
    RectTransform crosshair;
    RectTransform[] crossTicks;
    RectTransform hitMarker;
    Image[] hitBars;
    Camera cam;
    int lastLoaded = -1, lastReserve = -1;
    float hitAt = -10f, hitDuration, hitScale;
    Color hitColor;

    void Awake()
    {
        gun = GetComponent<Sidearm>();
        cam = Camera.main;
        Build();
    }

    void OnEnable() { if (root != null) root.SetActive(true); CombatFeedback.Damaged += OnDamaged; }
    void OnDisable() { if (root != null) root.SetActive(false); CombatFeedback.Damaged -= OnDamaged; }
    void OnDestroy() { if (root != null) Destroy(root); }

    void OnDamaged(DamageInfo info, DamageOutcome outcome, Health target)
    {
        if (info.source != transform.root.gameObject) return;
        hitAt = Time.unscaledTime;
        bool kill = outcome == DamageOutcome.Killed;
        hitColor = info.weakSpot ? HudKit.Danger : Color.white;
        hitScale = kill ? 1.6f : info.weakSpot ? 1.2f : 1f;
        hitDuration = kill ? .35f : .15f;
    }

    void LateUpdate()
    {
        if (gun.Loaded != lastLoaded || gun.Reserve != lastReserve) RefreshAmmo();

        // Crosshair gap follows the real spread cone, converted to pixels.
        float fov = cam != null ? cam.fieldOfView : 70f;
        float pixelsPerDegree = 1080f / fov;
        float gap = Mathf.Clamp(gun.CurrentSpread * pixelsPerDegree, 6f, 120f);
        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        for (int i = 0; i < 4; i++) crossTicks[i].anchoredPosition = dirs[i] * (gap + 6f);
        crosshair.gameObject.SetActive(gun.AimAmount < .95f);

        // Status: reloading flashes; empty warns.
        if (gun.IsReloading)
        {
            statusText.text = "RELOADING!";
            statusText.color = Color.Lerp(HudKit.Danger, HudKit.Ivory, Mathf.PingPong(Time.unscaledTime * 6f, 1f));
            statusText.gameObject.SetActive(true);
        }
        else if (gun.IsEmpty)
        {
            statusText.text = gun.Reserve > 0 ? "EMPTY  [R]" : "NO AMMO";
            statusText.color = HudKit.Danger;
            statusText.gameObject.SetActive(true);
        }
        else statusText.gameObject.SetActive(false);

        AmmoPickup pickup = gun.LookedAtPickup();
        promptText.gameObject.SetActive(pickup != null);
        if (pickup != null) promptText.text = gun.ReserveFull ? "Can't carry more" : $"[E] Take ammo (+{pickup.Rounds})";

        float age = Time.unscaledTime - hitAt;
        bool show = age < hitDuration;
        hitMarker.gameObject.SetActive(show);
        if (show)
        {
            float t = age / hitDuration;
            hitMarker.localScale = Vector3.one * hitScale * (1f + .3f * (1f - t) * (1f - t));
            Color c = hitColor; c.a = 1f - t * t;
            foreach (Image b in hitBars) b.color = c;
        }
    }

    void RefreshAmmo()
    {
        lastLoaded = gun.Loaded;
        lastReserve = gun.Reserve;
        for (int i = 0; i < bullets.Length; i++) bullets[i].color = i < gun.Loaded ? Brass : Spent;
        reserveText.text = gun.Reserve.ToString();
        reserveText.color = gun.Reserve <= gun.MagazineSize ? HudKit.Danger : HudKit.Ivory;
    }

    void Build()
    {
        root = HudKit.Canvas("Runtime Gun HUD", 300);
        Transform t = root.transform;

        // --- ammo (bottom right) ---
        RectTransform panel = HudKit.Rect("Ammo", t);
        HudKit.Place(panel, new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(300f, 70f));
        RectTransform row = HudKit.Rect("Bullets", panel);
        HudKit.Place(row, new Vector2(0f, .5f), new Vector2(0f, 0f), new Vector2(200f, 50f));
        bullets = new Image[gun.MagazineSize];
        float spacing = Mathf.Min(18f, 190f / Mathf.Max(1, bullets.Length));
        for (int i = 0; i < bullets.Length; i++)
        {
            Image b = HudKit.Img("Round " + (i + 1), row, Brass);
            HudKit.Place(b.rectTransform, new Vector2(0f, .5f), new Vector2(i * spacing, 0f), new Vector2(Mathf.Min(9f, spacing * .6f), 34f));
            var outline = b.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, .7f);
            bullets[i] = b;
        }
        reserveText = HudKit.Txt("Reserve", panel, 40, TextAnchor.MiddleRight);
        HudKit.Place(reserveText.rectTransform, new Vector2(1f, .5f), new Vector2(0f, 2f), new Vector2(100f, 60f));
        Text nameText = HudKit.Txt("Weapon", panel, 18, TextAnchor.LowerRight, FontStyle.Normal);
        nameText.text = gun.DisplayName + "   [1] [2]";
        HudKit.Place(nameText.rectTransform, new Vector2(1f, 1f), new Vector2(0f, 26f), new Vector2(300f, 24f));

        statusText = HudKit.Txt("Status", t, 34, TextAnchor.MiddleCenter);
        HudKit.Place(statusText.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -120f), new Vector2(500f, 50f));
        statusText.gameObject.SetActive(false);

        // --- crosshair ---
        crosshair = HudKit.Rect("Crosshair", t);
        HudKit.Place(crosshair, new Vector2(.5f, .5f), Vector2.zero, new Vector2(10f, 10f));
        Image dot = HudKit.Img("Dot", crosshair, new Color(1f, 1f, 1f, .8f));
        dot.sprite = HudKit.Circle();
        HudKit.Place(dot.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(4f, 4f));
        crossTicks = new RectTransform[4];
        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        for (int i = 0; i < 4; i++)
        {
            Image tick = HudKit.Img("Tick", crosshair, new Color(1f, 1f, 1f, .85f));
            HudKit.Place(tick.rectTransform, new Vector2(.5f, .5f), dirs[i] * 12f, dirs[i].x != 0 ? new Vector2(12f, 2.5f) : new Vector2(2.5f, 12f));
            tick.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .6f);
            crossTicks[i] = tick.rectTransform;
        }

        // --- hit marker ---
        hitMarker = HudKit.Rect("Hit Marker", t);
        HudKit.Place(hitMarker, new Vector2(.5f, .5f), Vector2.zero, new Vector2(48f, 48f));
        hitMarker.localRotation = Quaternion.Euler(0f, 0f, 45f);
        hitBars = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            hitBars[i] = HudKit.Img("Bar", hitMarker, Color.white);
            HudKit.Place(hitBars[i].rectTransform, new Vector2(.5f, .5f), dirs[i] * 14f, dirs[i].x != 0 ? new Vector2(12f, 3f) : new Vector2(3f, 12f));
            hitBars[i].gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .6f);
        }
        hitMarker.gameObject.SetActive(false);

        promptText = HudKit.Txt("Prompt", t, 24, TextAnchor.MiddleCenter);
        HudKit.Place(promptText.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -60f), new Vector2(500f, 34f));
        promptText.gameObject.SetActive(false);

        RefreshAmmo();
    }
}
