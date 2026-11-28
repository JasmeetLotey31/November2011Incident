using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Buying from the armory's weapon lockers: look at one and HOLD Interact ([E]) until the bar
/// fills. Takes the gun (full ammo, in your hands; a third gun swaps out the one you're holding)
/// or, if you already carry it, refills its ammo for less. Not enough points: refused.
/// Can't buy while something has hold of you. The horde director adds this to the player.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerBuy : MonoBehaviour
{
    [SerializeField] float range = 2.6f;
    [SerializeField] float holdSeconds = .5f;
    [SerializeField] string firstLookRadio = "Security's still logging every kill... rack up enough and the lockers will open for you.";

    public WeaponLocker LookedAt { get; private set; }

    Camera view;
    WeaponSlots slots;
    PlayerEaten eaten;
    PlayerPoints points;
    InputAction interact;
    float hold;
    bool used, saidFirst;

    GameObject hudRoot;
    Text promptText, subText;
    Image barBack, barFill;

    void Awake()
    {
        view = GetComponentInChildren<Camera>();
        slots = GetComponentInChildren<WeaponSlots>(true);
        eaten = GetComponent<PlayerEaten>();
        interact = InputSystem.actions.FindAction("Player/Interact", throwIfNotFound: true);
        BuildHud();
    }

    void OnDestroy() { if (hudRoot != null) Destroy(hudRoot); }

    void Update()
    {
        if (points == null) points = PlayerPoints.Instance;
        LookedAt = Find();
        bool held = LookedAt != null && Cursor.lockState == CursorLockMode.Locked && interact.IsPressed();
        bool busy = eaten != null && (eaten.IsEaten || eaten.IsPulled || eaten.IsPinned);
        if (LookedAt != null && !saidFirst)
        {
            saidFirst = true;
            if (KeyHordeDirector.Active != null) KeyHordeDirector.Active.Say(firstLookRadio);
        }

        if (!held || busy) { hold = 0f; used = false; }
        else if (!used)
        {
            hold += Time.deltaTime / Mathf.Max(.05f, holdSeconds);
            if (hold >= 1f) { used = true; hold = 0f; Buy(LookedAt); }
        }
        UpdateHud(busy);
    }

    WeaponLocker Find()
    {
        if (view == null) return null;
        Ray ray = view.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Collide)) return null;
        return hit.collider.GetComponentInParent<WeaponLocker>();
    }

    /// <summary>What this locker would sell you right now: the gun, its ammo, or nothing.</summary>
    (Sidearm gun, bool ammo, int cost, bool full) Offer(WeaponLocker locker)
    {
        Sidearm gun = slots != null ? slots.Find(locker.WeaponName) : null;
        if (gun == null) return (null, false, 0, false);
        bool own = slots.Carries(gun);
        return (gun, own, own ? locker.AmmoCost : locker.Cost, own && gun.AmmoFull);
    }

    void Buy(WeaponLocker locker)
    {
        var offer = Offer(locker);
        if (offer.gun == null || offer.full || points == null) return;
        if (!points.Spend(offer.cost, offer.ammo ? locker.WeaponName + " AMMO" : locker.WeaponName)) { locker.Refused(); return; }
        if (offer.ammo) offer.gun.FillAmmo();
        else slots.Give(offer.gun);
        locker.Purchased(offer.ammo);
    }

    // ---------------- HUD ----------------

    void BuildHud()
    {
        hudRoot = HudKit.Canvas("Buy Prompt", 210);
        Transform t = hudRoot.transform;
        promptText = HudKit.Txt("Buy", t, 32, TextAnchor.MiddleCenter);
        HudKit.Place(promptText.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -92f), new Vector2(1000f, 40f));
        subText = HudKit.Txt("Buy Sub", t, 22, TextAnchor.MiddleCenter, FontStyle.Normal);
        HudKit.Place(subText.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -124f), new Vector2(1000f, 30f));
        barBack = HudKit.Img("Hold Back", t, HudKit.Panel);
        HudKit.Place(barBack.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -150f), new Vector2(260f, 8f));
        barFill = HudKit.Img("Hold Fill", barBack.transform, new Color(1f, .62f, .2f, 1f));
        HudKit.Stretch(barFill.rectTransform, 1f);
        barFill.sprite = HudKit.White();
        barFill.type = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        hudRoot.SetActive(false);
    }

    void UpdateHud(bool busy)
    {
        bool show = LookedAt != null && !busy && points != null;
        hudRoot.SetActive(show);
        if (!show) return;
        var offer = Offer(LookedAt);
        if (offer.gun == null) { hudRoot.SetActive(false); return; }
        bool afford = points.CanAfford(offer.cost);
        string what = offer.ammo ? $"{LookedAt.WeaponName} AMMO" : LookedAt.WeaponName;
        if (offer.full)
        {
            promptText.text = $"{LookedAt.WeaponName}   AMMO FULL";
            promptText.color = HudKit.Ivory;
            subText.text = "";
        }
        else
        {
            promptText.text = $"HOLD [E]   {what}   {offer.cost}";
            promptText.color = afford ? new Color(1f, .78f, .35f, 1f) : HudKit.Danger;
            Sidearm drop = !offer.ammo && slots != null ? slots.WouldDrop : null;
            subText.text = !afford ? $"need {offer.cost - points.Points} more points"
                         : drop != null ? $"swaps out your {drop.DisplayName.ToLower()}" : "";
            subText.color = afford ? new Color(.8f, .8f, .8f, 1f) : HudKit.Danger;
        }
        barBack.gameObject.SetActive(!offer.full && hold > 0f);
        barFill.fillAmount = hold;
    }
}
