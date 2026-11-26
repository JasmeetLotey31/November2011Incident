using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The found-footage layer that makes it feel like the recovered black box:
/// - "● REC  BLACK BOX // CVR-2" with a ticking timecode on 13 NOV 2011;
/// - wave and keys-alive readout, radio transmissions as subtitles;
/// - vitals bar, "LEG SCRATCHED" tag;
/// - scanlines always, TV static and a red edge flash when you're hit (worse when nearly dead);
/// - death: the feed breaks up into static and "SIGNAL LOST", R to restart;
/// - escape pod ready message after the final scripted wave.
/// Put it on the player root (with PlayerVitals). Optional: assign the horde director.
/// </summary>
[DisallowMultipleComponent]
public sealed class BlackBoxHUD : MonoBehaviour
{
    [SerializeField] KeyHordeDirector director;
    [Tooltip("Recording clock starts at this time of day (hours, minutes, seconds).")]
    [SerializeField] Vector3Int startClock = new Vector3Int(21, 4, 17);
    [SerializeField] string dateText = "13 NOV 2011";
    [SerializeField] float radioSeconds = 5f;

    PlayerVitals vitals;
    PlayerEaten eaten;
    RawImage teethRing, mouthDark;
    Text mouthPrompt, tonguePrompt, injuryText;
    Image biteTimer;
    readonly System.Collections.Generic.List<RawImage> splats = new System.Collections.Generic.List<RawImage>();
    readonly System.Collections.Generic.List<float> splatBorn = new System.Collections.Generic.List<float>();
    Texture2D[] splatTextures;
    RawImage[] goreBlind;
    Vector2[] goreHome;
    RawImage downDark;
    Text downText, diveText;
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    PlayerCloseCombat closeCombat;
    Image staminaFill, grabProgressFill, grabTimerFill;
    Text grabText, finishText, parryText;
    Image[] threatArrows;
    Camera view;
    const float ThreatRange = 12f;
    RectTransform grabPanel;
    GameObject root;
    Text recText, timecodeText, waveText, radioText, legText, deathTitle, deathSub;
    Image recDot, healthFill, hurtFlash;
    RawImage staticImage, scanlines;
    RectTransform deathPanel;
    float radioUntil, hurtAt = -10f, deathAt = -1f, startTime;

    void Awake()
    {
        vitals = GetComponent<PlayerVitals>();
        closeCombat = GetComponent<PlayerCloseCombat>();
        eaten = GetComponent<PlayerEaten>();
        view = GetComponentInChildren<Camera>();
        if (director == null) director = FindAnyObjectByType<KeyHordeDirector>();
        startTime = Time.time;
        Build();
    }

    void OnEnable()
    {
        if (vitals != null) { vitals.Hurt += OnHurt; vitals.Died += OnDied; }
        if (director != null) { director.Radio += ShowRadio; }
    }

    void OnDisable()
    {
        if (vitals != null) { vitals.Hurt -= OnHurt; vitals.Died -= OnDied; }
        if (director != null) { director.Radio -= ShowRadio; }
    }

    void OnDestroy() { if (root != null) Destroy(root); }

    void OnHurt(DamageInfo info)
    {
        hurtAt = Time.unscaledTime;
        // Blood hits the lens.
        int count = info.amount >= 20f ? 3 : 1;
        for (int i = 0; i < count; i++) AddSplat();
    }

    void AddSplat()
    {
        if (root == null || splatTextures == null) return;
        var img = HudKit.Rect("Blood", root.transform).gameObject.AddComponent<RawImage>();
        img.raycastTarget = false;
        img.texture = splatTextures[Random.Range(0, splatTextures.Length)];
        float size = Random.Range(220f, 520f);
        HudKit.Place(img.rectTransform, new Vector2(.5f, .5f), new Vector2(Random.Range(-820f, 820f), Random.Range(-420f, 420f)), new Vector2(size, size));
        img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-25f, 25f));
        img.transform.SetSiblingIndex(3); // under the text, over the vignette
        splats.Add(img);
        splatBorn.Add(Time.unscaledTime);
        while (splats.Count > 14) { Destroy(splats[0].gameObject); splats.RemoveAt(0); splatBorn.RemoveAt(0); }
    }

    void UpdateEatenOverlay(float now)
    {
        // Blood on the lens: drips down and fades.
        for (int i = splats.Count - 1; i >= 0; i--)
        {
            float age = now - splatBorn[i];
            if (age > 4f) { Destroy(splats[i].gameObject); splats.RemoveAt(i); splatBorn.RemoveAt(i); continue; }
            splats[i].rectTransform.anchoredPosition += Vector2.down * (12f * Time.unscaledDeltaTime);
            splats[i].color = new Color(1f, 1f, 1f, Mathf.Clamp01(1.4f - age / 3f));
        }

        bool inMouth = eaten != null && eaten.IsEaten;
        bool tongued = eaten != null && eaten.IsPulled;

        // Gore over the eyes: thick, slides down, fades as you blink it away.
        float gore = eaten != null ? eaten.Splatter01 : 0f;
        for (int i = 0; i < goreBlind.Length; i++)
        {
            goreBlind[i].gameObject.SetActive(gore > 0f);
            if (gore <= 0f) continue;
            float slide = (1f - gore) * (90f + i * 40f);
            goreBlind[i].rectTransform.anchoredPosition = goreHome[i] + Vector2.down * slide;
            goreBlind[i].color = new Color(.55f, .42f, .12f, Mathf.Clamp01(gore * 1.6f) * .97f);
        }

        // Knocked down: the world goes dark at the edges; get up.
        bool down = eaten != null && eaten.IsDown && !inMouth;
        downDark.gameObject.SetActive(down);
        downText.gameObject.SetActive(down);
        if (down)
        {
            downDark.color = new Color(.2f, 0f, 0f, .55f);
            downText.color = Color.Lerp(Color.white, HudKit.Danger, Mathf.PingPong(now * 6f, 1f));
        }

        // A key is about to drop on you.
        bool above = false;
        if (view != null)
            foreach (KeyFlyer k in KeyHorde.Flyers)
                if (k != null && k.IsAttackingDive && Vector3.Distance(Flat(k.DiveTarget), Flat(view.transform.position)) < 3.5f) { above = true; break; }
        diveText.gameObject.SetActive(above && !inMouth);
        if (above)
        {
            diveText.color = Mathf.Repeat(now * 8f, 1f) < .5f ? HudKit.Danger : Color.white;
            diveText.rectTransform.localScale = Vector3.one * (1f + .1f * Mathf.Sin(now * 30f));
        }

        mouthDark.gameObject.SetActive(inMouth);
        teethRing.gameObject.SetActive(inMouth);
        mouthPrompt.gameObject.SetActive(inMouth);
        biteTimer.transform.parent.gameObject.SetActive(inMouth);
        tonguePrompt.gameObject.SetActive(tongued && !inMouth);
        if (inMouth)
        {
            // Teeth close in with every chew and the closer it gets to biting.
            float chew = Mathf.Clamp01(1f - (now - eaten.LastChewTime) / .35f);
            float scale = Mathf.Lerp(1.55f, 1.05f, eaten.EatProgress01) - chew * .12f;
            teethRing.rectTransform.localScale = Vector3.one * scale;
            teethRing.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(now * 31f), Mathf.Sin(now * 27f)) * (6f + chew * 14f);
            mouthDark.color = new Color(.25f, 0f, 0f, .35f + .25f * eaten.EatProgress01);
            biteTimer.fillAmount = 1f - eaten.EatProgress01;
            mouthPrompt.color = Color.Lerp(Color.white, HudKit.Danger, Mathf.PingPong(now * 7f, 1f));
        }
        if (tongued) tonguePrompt.color = Color.Lerp(HudKit.Danger, Color.white, Mathf.PingPong(now * 8f, 1f));

        if (eaten != null && eaten.Injuries.Count > 0)
        {
            int arm = eaten.Count(PlayerEaten.Injury.Arm), leg = eaten.Count(PlayerEaten.Injury.Leg), side = eaten.Count(PlayerEaten.Injury.Side);
            var s = new System.Text.StringBuilder();
            if (arm > 0) s.Append(arm > 1 ? $"ARM BITTEN x{arm}   " : "ARM BITTEN   ");
            if (leg > 0) s.Append(leg > 1 ? $"LEG BITTEN x{leg}   " : "LEG BITTEN   ");
            if (side > 0) s.Append(side > 1 ? $"SIDE BITTEN x{side}" : "SIDE BITTEN");
            injuryText.text = s.ToString();
            injuryText.gameObject.SetActive(true);
        }
        else injuryText.gameObject.SetActive(false);
    }
    void OnDied() => deathAt = Time.unscaledTime;

    public void ShowRadio(string line)
    {
        radioText.text = line;
        radioUntil = Time.unscaledTime + radioSeconds;
    }

    void LateUpdate()
    {
        float now = Time.unscaledTime;

        // REC dot blinks; timecode ticks.
        recDot.enabled = Mathf.Repeat(now, 1f) < .6f;
        int seconds = startClock.x * 3600 + startClock.y * 60 + startClock.z + Mathf.FloorToInt(Time.time - startTime);
        int frames = Mathf.FloorToInt(Mathf.Repeat(Time.time * 30f, 30f));
        timecodeText.text = $"{dateText}   {seconds / 3600 % 24:00}:{seconds / 60 % 60:00}:{seconds % 60:00}:{frames:00}";

        if (director != null)
            waveText.text = director.Current != KeyHordeDirector.Phase.Starting ? $"KEYS {KeyHorde.AliveCount}   {(int)director.SurvivedSeconds / 60:00}:{(int)director.SurvivedSeconds % 60:00} ALIVE" : "";

        radioText.gameObject.SetActive(now < radioUntil);

        Health health = vitals != null ? vitals.Health : null;
        float fraction = health != null ? health.Fraction : 1f;
        healthFill.fillAmount = fraction;
        healthFill.color = fraction < .35f ? HudKit.Danger : HudKit.Ivory;
        legText.gameObject.SetActive(vitals != null && vitals.LegScratchRemaining > 0f && !vitals.IsDead);

        UpdateThreatArrows(now);
        UpdateEatenOverlay(now);

        if (closeCombat != null)
        {
            bool canFinish = closeCombat.KickTarget != null && !closeCombat.IsGrabbed && deathAt < 0f;
            finishText.gameObject.SetActive(canFinish);
            if (canFinish) finishText.rectTransform.localScale = Vector3.one * (1f + .08f * Mathf.Sin(now * 14f));
            float sinceParry = now - parryShownAt;
            if (closeCombat.LastKickTime > lastParrySeen) { lastParrySeen = closeCombat.LastKickTime; parryShownAt = now; sinceParry = 0f; }
            parryText.gameObject.SetActive(sinceParry < .7f);
            if (sinceParry < .7f)
            {
                parryText.rectTransform.localScale = Vector3.one * (1.4f - .4f * Mathf.Clamp01(sinceParry / .15f));
                parryText.color = new Color(1f, .85f, .3f, 1f - Mathf.Clamp01((sinceParry - .4f) / .3f));
            }
            staminaFill.fillAmount = closeCombat.DashReady01;
            staminaFill.color = closeCombat.DashReady01 < 1f ? new Color(.4f, .45f, .5f, 1f) : new Color(.55f, .8f, .95f, 1f);
            bool grabbed = closeCombat.IsGrabbed;
            grabPanel.gameObject.SetActive(grabbed);
            if (grabbed)
            {
                grabProgressFill.fillAmount = closeCombat.GrabProgress;
                grabTimerFill.fillAmount = closeCombat.GrabTimeLeft01;
                // Shake and pulse: it's on you.
                grabPanel.anchoredPosition = new Vector2(Mathf.Sin(now * 53f), Mathf.Sin(now * 41f)) * 6f;
                grabText.color = Color.Lerp(HudKit.Danger, Color.white, Mathf.PingPong(now * 8f, 1f));
            }
        }

        // Red flash and static: a burst on every hit, a constant hiss when nearly dead.
        float sinceHurt = now - hurtAt;
        float burst = Mathf.Clamp01(1f - sinceHurt / .5f);
        float lowHealth = Mathf.Clamp01((.4f - fraction) / .4f);
        hurtFlash.color = new Color(.6f, 0f, 0f, Mathf.Max(burst * .7f, lowHealth * (.25f + .15f * Mathf.Sin(now * 6f))));
        float staticAmount = Mathf.Max(burst * .35f, lowHealth * .12f);

        if (deathAt >= 0f)
        {
            float d = now - deathAt;
            staticAmount = Mathf.Clamp01(d / 1.2f) * .7f;
            deathPanel.gameObject.SetActive(d > .6f);
            deathTitle.color = new Color(1f, 1f, 1f, Mathf.Clamp01((d - .6f) / .5f));
            deathSub.color = new Color(.8f, .8f, .8f, Mathf.Clamp01((d - 1.4f) / .5f));
        }

        staticImage.color = new Color(1f, 1f, 1f, staticAmount);
        staticImage.enabled = staticAmount > .01f;
        if (staticImage.enabled) staticImage.uvRect = new Rect(Random.value, Random.value, 3f, 2f);
        scanlines.uvRect = new Rect(0f, Mathf.Repeat(now * .05f, 1f), 1f, Screen.height / 4f);
    }

    float parryShownAt = -10f, lastParrySeen = -10f;

    /// <summary>Red arrows at the screen edge for keys attacking from outside your view.</summary>
    void UpdateThreatArrows(float now)
    {
        int used = 0;
        if (view != null && deathAt < 0f)
        {
            foreach (KeyAlien key in KeyHorde.Members)
            {
                if (used >= threatArrows.Length) break;
                if (key == null || key.IsDead || key.IsHidden) continue;
                KeyAlien.State s = key.Current;
                if (s != KeyAlien.State.Rush && s != KeyAlien.State.WindUp && s != KeyAlien.State.Lunge && s != KeyAlien.State.Flank) continue;
                Vector3 world = key.transform.position + Vector3.up;
                float distance = Vector3.Distance(world, view.transform.position);
                if (distance > ThreatRange) continue;
                Vector3 vp = view.WorldToViewportPoint(world);
                if (vp.z > 0f && vp.x > .05f && vp.x < .95f && vp.y > .05f && vp.y < .95f) continue; // on screen
                Vector3 local = view.transform.InverseTransformDirection(world - view.transform.position);
                float angle = Mathf.Atan2(local.x, local.z); // 0 = ahead, +right
                Image arrow = threatArrows[used++];
                arrow.gameObject.SetActive(true);
                Vector2 dir = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                arrow.rectTransform.anchoredPosition = dir * 330f;
                arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
                bool attacking = s == KeyAlien.State.WindUp || s == KeyAlien.State.Lunge;
                float urgency = 1f - distance / ThreatRange;
                float pulse = attacking ? (Mathf.Repeat(now * 10f, 1f) < .5f ? 1f : .5f) : .75f;
                arrow.color = new Color(1f, .15f, .1f, Mathf.Lerp(.45f, 1f, urgency) * pulse);
                arrow.rectTransform.localScale = Vector3.one * Mathf.Lerp(.8f, attacking ? 1.5f : 1.15f, urgency);
            }
            // Flyers lining up a dive from off-screen.
            foreach (KeyFlyer flyer in KeyHorde.Flyers)
            {
                if (used >= threatArrows.Length) break;
                if (flyer == null || flyer.IsDead || !flyer.IsDiving) continue;
                Vector3 world = flyer.transform.position;
                float distance = Vector3.Distance(world, view.transform.position);
                if (distance > ThreatRange * 1.5f) continue;
                Vector3 vp = view.WorldToViewportPoint(world);
                if (vp.z > 0f && vp.x > .05f && vp.x < .95f && vp.y > .05f && vp.y < .95f) continue;
                Vector3 local = view.transform.InverseTransformDirection(world - view.transform.position);
                float angle = Mathf.Atan2(local.x, local.z);
                Image arrow = threatArrows[used++];
                arrow.gameObject.SetActive(true);
                arrow.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 330f;
                arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
                float pulse = Mathf.Repeat(now * 12f, 1f) < .5f ? 1f : .45f;
                arrow.color = new Color(1f, .55f, .1f, pulse); // orange: incoming from the air
                arrow.rectTransform.localScale = Vector3.one * 1.3f;
            }
        }
        for (int i = used; i < threatArrows.Length; i++) threatArrows[i].gameObject.SetActive(false);
    }

    void Build()
    {
        root = HudKit.Canvas("Runtime Black Box HUD", 200);
        Transform t = root.transform;

        scanlines = Rect<RawImage>("Scanlines", t);
        HudKit.Stretch(scanlines.rectTransform);
        scanlines.texture = HudKit.Scanlines();

        hurtFlash = HudKit.Img("Hurt Vignette", t, new Color(.6f, 0f, 0f, 0f));
        HudKit.Stretch(hurtFlash.rectTransform);
        Texture2D vig = HudKit.Vignette();
        hurtFlash.sprite = Sprite.Create(vig, new Rect(0, 0, vig.width, vig.height), new Vector2(.5f, .5f));

        staticImage = Rect<RawImage>("Static", t);
        HudKit.Stretch(staticImage.rectTransform);
        staticImage.texture = HudKit.Noise();
        staticImage.enabled = false;

        // Top-left: REC + timecode.
        recDot = HudKit.Img("REC Dot", t, HudKit.Danger);
        recDot.sprite = HudKit.Circle();
        HudKit.Place(recDot.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(20f, 20f));
        recText = HudKit.Txt("REC", t, 26, TextAnchor.MiddleLeft);
        recText.text = "REC   BLACK BOX // CVR-2   STATION JUPITER";
        HudKit.Place(recText.rectTransform, new Vector2(0f, 1f), new Vector2(72f, -34f), new Vector2(900f, 34f));
        timecodeText = HudKit.Txt("Timecode", t, 22, TextAnchor.MiddleLeft, FontStyle.Normal);
        HudKit.Place(timecodeText.rectTransform, new Vector2(0f, 1f), new Vector2(72f, -66f), new Vector2(600f, 30f));

        // Top-right: wave.
        waveText = HudKit.Txt("Wave", t, 26, TextAnchor.MiddleRight);
        HudKit.Place(waveText.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -34f), new Vector2(500f, 34f));

        // Bottom-centre: radio subtitles.
        radioText = HudKit.Txt("Radio", t, 28, TextAnchor.MiddleCenter, FontStyle.Italic);
        radioText.color = new Color(.75f, .95f, .8f, 1f);
        HudKit.Place(radioText.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 150f), new Vector2(1400f, 40f));
        radioText.gameObject.SetActive(false);

        // Bottom-left: vitals.
        Text vitalsLabel = HudKit.Txt("Vitals Label", t, 20, TextAnchor.MiddleLeft);
        vitalsLabel.text = "VITALS";
        HudKit.Place(vitalsLabel.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 74f), new Vector2(200f, 26f));
        Image back = HudKit.Img("Vitals Back", t, HudKit.Panel);
        HudKit.Place(back.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 48f), new Vector2(300f, 14f));
        healthFill = HudKit.Img("Vitals Fill", back.transform, HudKit.Ivory);
        HudKit.Stretch(healthFill.rectTransform, 2f);
        healthFill.sprite = HudKit.White();
        healthFill.type = Image.Type.Filled;
        healthFill.fillMethod = Image.FillMethod.Horizontal;
        // Stamina (shove / dodge), thin bar under vitals.
        Image staminaBack = HudKit.Img("Stamina Back", t, HudKit.Panel);
        HudKit.Place(staminaBack.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 32f), new Vector2(220f, 8f));
        staminaFill = HudKit.Img("Stamina Fill", staminaBack.transform, Color.white);
        HudKit.Stretch(staminaFill.rectTransform, 1f);
        staminaFill.sprite = HudKit.White();
        staminaFill.type = Image.Type.Filled;
        staminaFill.fillMethod = Image.FillMethod.Horizontal;

        // Grabbed: struggle prompt in the middle of the screen.
        grabPanel = HudKit.Rect("Grab Struggle", t);
        HudKit.Place(grabPanel, new Vector2(.5f, .5f), Vector2.zero, new Vector2(700f, 200f));
        grabText = HudKit.Txt("Mash", grabPanel, 64, TextAnchor.MiddleCenter);
        grabText.text = "MASH [E]!";
        HudKit.Place(grabText.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 40f), new Vector2(700f, 80f));
        Image progressBack = HudKit.Img("Break Free Back", grabPanel, HudKit.Panel);
        HudKit.Place(progressBack.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -20f), new Vector2(420f, 22f));
        grabProgressFill = HudKit.Img("Break Free", progressBack.transform, Color.white);
        HudKit.Stretch(grabProgressFill.rectTransform, 3f);
        grabProgressFill.sprite = HudKit.White();
        grabProgressFill.type = Image.Type.Filled;
        grabProgressFill.fillMethod = Image.FillMethod.Horizontal;
        Image timerBack = HudKit.Img("Timer Back", grabPanel, HudKit.Panel);
        HudKit.Place(timerBack.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -48f), new Vector2(420f, 8f));
        grabTimerFill = HudKit.Img("Timer", timerBack.transform, HudKit.Danger);
        HudKit.Stretch(grabTimerFill.rectTransform, 1f);
        grabTimerFill.sprite = HudKit.White();
        grabTimerFill.type = Image.Type.Filled;
        grabTimerFill.fillMethod = Image.FillMethod.Horizontal;
        grabPanel.gameObject.SetActive(false);

        // Inside a mouth: darkness, a ring of fangs closing in, a prompt and the bite timer.
        splatTextures = new[] { HudKit.BloodSplat(), HudKit.BloodSplat(), HudKit.BloodSplat() };
        mouthDark = Rect<RawImage>("Mouth Dark", t);
        HudKit.Stretch(mouthDark.rectTransform);
        mouthDark.texture = HudKit.Vignette();
        mouthDark.gameObject.SetActive(false);
        teethRing = Rect<RawImage>("Teeth", t);
        HudKit.Place(teethRing.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(2100f, 2100f));
        teethRing.texture = HudKit.TeethRing();
        teethRing.gameObject.SetActive(false);
        mouthPrompt = HudKit.Txt("Mouth Prompt", t, 46, TextAnchor.MiddleCenter);
        mouthPrompt.text = "SHOOT THE THROAT!     [V] PRY IT OPEN";
        HudKit.Place(mouthPrompt.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -170f), new Vector2(1200f, 60f));
        mouthPrompt.gameObject.SetActive(false);
        Image biteBack = HudKit.Img("Bite Timer Back", t, HudKit.Panel);
        HudKit.Place(biteBack.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -215f), new Vector2(500f, 12f));
        biteTimer = HudKit.Img("Bite Timer", biteBack.transform, HudKit.Danger);
        HudKit.Stretch(biteTimer.rectTransform, 2f);
        biteTimer.sprite = HudKit.White();
        biteTimer.type = Image.Type.Filled;
        biteTimer.fillMethod = Image.FillMethod.Horizontal;
        biteBack.gameObject.SetActive(false);
        tonguePrompt = HudKit.Txt("Tongue Prompt", t, 40, TextAnchor.MiddleCenter);
        tonguePrompt.text = "SHOOT THE TONGUE!     [ALT] DASH";
        HudKit.Place(tonguePrompt.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -170f), new Vector2(1200f, 60f));
        tonguePrompt.gameObject.SetActive(false);
        injuryText = HudKit.Txt("Injuries", t, 20, TextAnchor.MiddleLeft);
        injuryText.color = HudKit.Danger;
        HudKit.Place(injuryText.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 100f), new Vector2(700f, 26f));
        injuryText.gameObject.SetActive(false);

        // Gore spit over the eyes.
        goreBlind = new RawImage[4];
        goreHome = new Vector2[goreBlind.Length];
        for (int i = 0; i < goreBlind.Length; i++)
        {
            goreBlind[i] = Rect<RawImage>("Gore", t);
            goreBlind[i].texture = splatTextures[i % splatTextures.Length];
            goreHome[i] = new Vector2(Random.Range(-260f, 260f), Random.Range(-120f, 160f));
            float size = Random.Range(900f, 1300f);
            HudKit.Place(goreBlind[i].rectTransform, new Vector2(.5f, .5f), goreHome[i], new Vector2(size, size));
            goreBlind[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            goreBlind[i].gameObject.SetActive(false);
        }

        // Knocked down.
        downDark = Rect<RawImage>("Down Dark", t);
        HudKit.Stretch(downDark.rectTransform);
        downDark.texture = HudKit.Vignette();
        downDark.gameObject.SetActive(false);
        downText = HudKit.Txt("Down", t, 40, TextAnchor.MiddleCenter);
        downText.text = "YOU'RE DOWN!     [ALT] ROLL UP";
        HudKit.Place(downText.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -170f), new Vector2(1200f, 60f));
        downText.gameObject.SetActive(false);

        // Blade dive incoming.
        diveText = HudKit.Txt("Dive", t, 56, TextAnchor.MiddleCenter);
        diveText.text = "ABOVE YOU!  MOVE!";
        HudKit.Place(diveText.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 220f), new Vector2(1200f, 80f));
        diveText.gameObject.SetActive(false);

        // Off-screen threat arrows around the crosshair.
        RectTransform arrows = HudKit.Rect("Threat Arrows", t);
        HudKit.Place(arrows, new Vector2(.5f, .5f), Vector2.zero, new Vector2(10f, 10f));
        Sprite tri = HudKit.Triangle();
        threatArrows = new Image[6];
        for (int i = 0; i < threatArrows.Length; i++)
        {
            threatArrows[i] = HudKit.Img("Arrow", arrows, HudKit.Danger);
            threatArrows[i].sprite = tri;
            HudKit.Place(threatArrows[i].rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(46f, 34f));
            threatArrows[i].gameObject.SetActive(false);
        }

        // Finisher prompt and parry flash.
        finishText = HudKit.Txt("Finish", t, 40, TextAnchor.MiddleCenter);
        finishText.text = "[V]  KICK";
        finishText.color = Color.white;
        HudKit.Place(finishText.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -90f), new Vector2(500f, 50f));
        finishText.gameObject.SetActive(false);
        parryText = HudKit.Txt("Parry", t, 72, TextAnchor.MiddleCenter);
        parryText.text = "KICK!";
        HudKit.Place(parryText.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 120f), new Vector2(600f, 90f));
        parryText.gameObject.SetActive(false);

        legText = HudKit.Txt("Leg", t, 20, TextAnchor.MiddleLeft);
        legText.text = "LEG SCRATCHED";
        legText.color = HudKit.Danger;
        HudKit.Place(legText.rectTransform, new Vector2(0f, 0f), new Vector2(150f, 74f), new Vector2(300f, 26f));
        legText.gameObject.SetActive(false);

        // Death.
        deathPanel = HudKit.Rect("Signal Lost", t);
        HudKit.Stretch(deathPanel);
        // Dark band so the text reads through the static.
        Image band = HudKit.Img("Band", deathPanel, new Color(0f, 0f, 0f, .8f));
        HudKit.Place(band.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 0f), new Vector2(3000f, 200f));
        deathTitle = HudKit.Txt("Title", deathPanel, 72, TextAnchor.MiddleCenter);
        deathTitle.text = "SIGNAL LOST";
        HudKit.Place(deathTitle.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 30f), new Vector2(1200f, 100f));
        deathSub = HudKit.Txt("Sub", deathPanel, 26, TextAnchor.MiddleCenter, FontStyle.Normal);
        deathSub.text = "end of recording.        [R] restart";
        HudKit.Place(deathSub.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -40f), new Vector2(1200f, 40f));
        deathPanel.gameObject.SetActive(false);
    }

    static T Rect<T>(string name, Transform parent) where T : Graphic
    {
        var g = HudKit.Rect(name, parent).gameObject.AddComponent<T>();
        g.raycastTarget = false;
        return g;
    }
}
