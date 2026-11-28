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
    PlayerPinned pinned;
    RectTransform pinPanel;
    RawImage pinDark, pinTeeth;
    Text pinMash, pinLeft, pinRight, pinStab, pinWinText;
    Image pinShoveFill, pinTimerFill, pinStabFill;
    float pinEndedAt = -10f;
    bool pinWon;
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

    // Waves and points.
    Text waveBanner, waveSub, breakText, pointsText, pointsLabel;
    RectTransform popRoot;
    readonly System.Collections.Generic.List<(Text text, float born)> pops = new System.Collections.Generic.List<(Text, float)>();
    float bannerAt = -10f, pointsBumpAt = -10f;
    PlayerPoints points;

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
        if (director != null) { director.Radio += ShowRadio; director.WaveStarted += OnWaveStarted; director.WaveCleared += OnWaveCleared; }
    }

    void OnDisable()
    {
        if (vitals != null) { vitals.Hurt -= OnHurt; vitals.Died -= OnDied; }
        if (director != null) { director.Radio -= ShowRadio; director.WaveStarted -= OnWaveStarted; director.WaveCleared -= OnWaveCleared; }
        if (points != null) points.Earned -= OnEarned;
        points = null;
    }

    // ---------------- waves & points ----------------

    void OnWaveStarted(int wave)
    {
        waveBanner.text = $"WAVE {wave}";
        waveBanner.color = HudKit.Danger;
        waveSub.text = wave == 1 ? "hold the armory" : $"{director.KeysInWave(wave)} keys";
        bannerAt = Time.unscaledTime;
    }

    void OnWaveCleared(int wave)
    {
        waveBanner.text = $"WAVE {wave} CLEARED";
        waveBanner.color = new Color(1f, .85f, .3f, 1f);
        waveSub.text = "reload. restock. they're coming back.";
        bannerAt = Time.unscaledTime;
    }

    void OnEarned(int amount, string label)
    {
        pointsBumpAt = Time.unscaledTime;
        var t = HudKit.Txt("Pop", popRoot, string.IsNullOrEmpty(label) ? 26 : 30, TextAnchor.MiddleRight);
        t.text = string.IsNullOrEmpty(label) ? $"+{amount}" : $"+{amount}  {label}";
        t.color = string.IsNullOrEmpty(label) ? HudKit.Ivory : new Color(1f, .85f, .3f, 1f);
        HudKit.Place(t.rectTransform, new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(500f, 34f));
        pops.Add((t, Time.unscaledTime));
        while (pops.Count > 6) { Destroy(pops[0].text.gameObject); pops.RemoveAt(0); }
    }

    void UpdateWavesAndPoints(float now)
    {
        if (points == null && PlayerPoints.Instance != null)
        {
            points = PlayerPoints.Instance;
            points.Earned += OnEarned;
        }
        bool alive = deathAt < 0f;

        // Top right: where you are in the hunt.
        if (director != null)
            waveText.text = director.Current == KeyHordeDirector.Phase.Wave ? $"WAVE {director.Wave}   KEYS LEFT {director.KeysLeft}"
                          : director.Current == KeyHordeDirector.Phase.Break ? $"WAVE {director.Wave} CLEARED" : "";

        // Big banner: fades in, holds, fades out.
        float b = now - bannerAt;
        bool banner = alive && b < 3f;
        waveBanner.gameObject.SetActive(banner);
        waveSub.gameObject.SetActive(banner);
        if (banner)
        {
            float a = Mathf.Clamp01(b / .25f) * (1f - Mathf.Clamp01((b - 2.3f) / .7f));
            Color c = waveBanner.color; c.a = a; waveBanner.color = c;
            waveSub.color = new Color(.85f, .85f, .85f, a);
            waveBanner.rectTransform.localScale = Vector3.one * (1.25f - .25f * Mathf.Clamp01(b / .2f));
        }

        // Between waves: the countdown.
        bool onBreak = alive && director != null && director.Current == KeyHordeDirector.Phase.Break && b > 2.5f;
        breakText.gameObject.SetActive(onBreak);
        if (onBreak)
        {
            int s = Mathf.CeilToInt(director.BreakSecondsLeft);
            breakText.text = $"NEXT WAVE IN {s}";
            breakText.color = s <= 3 ? (Mathf.Repeat(now * 4f, 1f) < .5f ? HudKit.Danger : Color.white) : HudKit.Ivory;
        }

        // Points, with the pop-ups sliding up out of them.
        bool showPoints = alive && points != null;
        pointsText.gameObject.SetActive(showPoints);
        pointsLabel.gameObject.SetActive(showPoints);
        if (showPoints)
        {
            pointsText.text = points.Points.ToString();
            float bump = Mathf.Clamp01(1f - (now - pointsBumpAt) / .2f);
            pointsText.rectTransform.localScale = Vector3.one * (1f + .15f * bump);
            pointsText.color = Color.Lerp(new Color(1f, .85f, .3f, 1f), Color.white, bump);
        }
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            float age = now - pops[i].born;
            if (age > 1.1f || !alive) { Destroy(pops[i].text.gameObject); pops.RemoveAt(i); continue; }
            int slot = pops.Count - 1 - i;
            pops[i].text.rectTransform.anchoredPosition = new Vector2(-10f * age, 8f + slot * 30f + 40f * age);
            Color c = pops[i].text.color; c.a = 1f - Mathf.Clamp01((age - .7f) / .4f);
            pops[i].text.color = c;
        }
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

        bool isPinned = UpdatePinned(now);

        // Knocked down: the world goes dark at the edges; get up.
        bool down = eaten != null && eaten.IsDown && !inMouth && !isPinned;
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
        diveText.gameObject.SetActive(above && !inMouth && !isPinned);
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
    /// <summary>Pinned: fangs closing in with the pressure, which key to hit next, the shove bar, then STAB.</summary>
    bool UpdatePinned(float now)
    {
        if (pinned == null && eaten != null && eaten.Pinned != null)
        {
            pinned = eaten.Pinned;
            pinned.Ended += won => { pinEndedAt = Time.time; pinWon = won; };
        }
        bool on = pinned != null && pinned.IsPinned && deathAt < 0f;
        pinPanel.gameObject.SetActive(on);
        pinDark.gameObject.SetActive(on);
        pinTeeth.gameObject.SetActive(on);

        // A beat after it ends: "STABBED IT!" or nothing (the bite says enough).
        float sinceEnd = Time.time - pinEndedAt;
        bool showWin = !on && pinWon && sinceEnd < .8f && deathAt < 0f;
        pinWinText.gameObject.SetActive(showWin);
        if (showWin)
        {
            pinWinText.rectTransform.localScale = Vector3.one * (1.4f - .4f * Mathf.Clamp01(sinceEnd / .15f));
            pinWinText.color = new Color(1f, .85f, .3f, 1f - Mathf.Clamp01((sinceEnd - .5f) / .3f));
        }
        if (!on) return false;

        float pressure = pinned.Pressure;
        float bit = Mathf.Clamp01(1f - (Time.time - pinned.LastBiteTime) / .4f);
        float shoved = Mathf.Clamp01(1f - (Time.time - pinned.LastShoveTime) / .12f);
        bool stab = pinned.StabReady;

        pinTeeth.rectTransform.localScale = Vector3.one * (Mathf.Lerp(1.75f, 1.02f, pressure) + shoved * .05f - bit * .12f);
        pinTeeth.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(now * 31f), Mathf.Sin(now * 27f)) * (4f + pressure * 16f + bit * 20f);
        pinTeeth.color = stab ? new Color(1f, 1f, 1f, .35f) : Color.white;
        pinDark.color = new Color(.3f, 0f, 0f, .25f + .35f * pressure + .3f * bit);

        pinMash.gameObject.SetActive(!stab);
        pinLeft.gameObject.SetActive(!stab);
        pinRight.gameObject.SetActive(!stab);
        pinStab.gameObject.SetActive(stab);
        pinStabFill.transform.parent.gameObject.SetActive(stab);
        if (stab)
        {
            pinStab.color = Mathf.Repeat(now * 10f, 1f) < .5f ? new Color(1f, .85f, .3f, 1f) : Color.white;
            pinStab.rectTransform.localScale = Vector3.one * (1.1f + .12f * Mathf.Sin(now * 30f));
            pinStabFill.fillAmount = pinned.StabWindow01;
        }
        else
        {
            int next = pinned.NextSide;
            LightKey(pinLeft, next != 1, now);
            LightKey(pinRight, next != 0, now);
            pinMash.color = Color.Lerp(Color.white, HudKit.Danger, Mathf.PingPong(now * 7f, 1f));
        }
        pinShoveFill.fillAmount = pinned.Shove;
        pinShoveFill.color = Color.Lerp(Color.white, new Color(1f, .85f, .3f, 1f), pinned.Shove);
        pinTimerFill.fillAmount = 1f - pinned.Elapsed01;
        pinPanel.anchoredPosition = new Vector2(Mathf.Sin(now * 53f), Mathf.Sin(now * 41f)) * (2f + 6f * pressure);
        return true;
    }

    static void LightKey(Text key, bool lit, float now)
    {
        key.color = lit ? Color.white : new Color(.5f, .5f, .5f, .5f);
        key.rectTransform.localScale = Vector3.one * (lit ? 1.15f + .08f * Mathf.Sin(now * 25f) : .85f);
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

        UpdateWavesAndPoints(now);

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
        waveText = HudKit.Txt("Wave", t, 30, TextAnchor.MiddleRight);
        HudKit.Place(waveText.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -36f), new Vector2(600f, 38f));

        // Wave banner (start / cleared) and the between-waves countdown.
        waveBanner = HudKit.Txt("Wave Banner", t, 96, TextAnchor.MiddleCenter);
        HudKit.Place(waveBanner.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 230f), new Vector2(1400f, 120f));
        waveBanner.gameObject.SetActive(false);
        waveSub = HudKit.Txt("Wave Sub", t, 30, TextAnchor.MiddleCenter, FontStyle.Italic);
        HudKit.Place(waveSub.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 160f), new Vector2(1400f, 40f));
        waveSub.gameObject.SetActive(false);
        breakText = HudKit.Txt("Next Wave", t, 40, TextAnchor.MiddleCenter);
        HudKit.Place(breakText.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -110f), new Vector2(800f, 50f));
        breakText.gameObject.SetActive(false);

        // Points, bottom right above the gun panel, with pop-ups rising out of them.
        pointsLabel = HudKit.Txt("Points Label", t, 18, TextAnchor.LowerRight, FontStyle.Normal);
        pointsLabel.text = "POINTS";
        HudKit.Place(pointsLabel.rectTransform, new Vector2(1f, 0f), new Vector2(-40f, 200f), new Vector2(300f, 24f));
        pointsText = HudKit.Txt("Points", t, 48, TextAnchor.LowerRight);
        pointsText.rectTransform.pivot = new Vector2(1f, 0f);
        HudKit.Place(pointsText.rectTransform, new Vector2(1f, 0f), new Vector2(-40f, 146f), new Vector2(300f, 56f));
        pointsText.gameObject.SetActive(false);
        pointsLabel.gameObject.SetActive(false);
        popRoot = HudKit.Rect("Point Pops", t);
        HudKit.Place(popRoot, new Vector2(1f, 0f), new Vector2(-40f, 226f), new Vector2(10f, 10f));

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

        // Pinned: red dark, fangs closing in, A / D to shove, shove bar, time bar, then [V] STAB!
        pinDark = Rect<RawImage>("Pin Dark", t);
        HudKit.Stretch(pinDark.rectTransform);
        pinDark.texture = HudKit.Vignette();
        pinDark.gameObject.SetActive(false);
        pinTeeth = Rect<RawImage>("Pin Teeth", t);
        HudKit.Place(pinTeeth.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(2100f, 2100f));
        pinTeeth.texture = HudKit.TeethRing();
        pinTeeth.gameObject.SetActive(false);
        pinPanel = HudKit.Rect("Pinned", t);
        HudKit.Place(pinPanel, new Vector2(.5f, .5f), Vector2.zero, new Vector2(900f, 700f));
        pinMash = HudKit.Txt("Shove", pinPanel, 46, TextAnchor.MiddleCenter);
        pinMash.text = "IT'S ON YOU!  SHOVE IT OFF!";
        HudKit.Place(pinMash.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -150f), new Vector2(1200f, 60f));
        pinLeft = HudKit.Txt("Left", pinPanel, 84, TextAnchor.MiddleCenter);
        pinLeft.text = "[A]";
        HudKit.Place(pinLeft.rectTransform, new Vector2(.5f, .5f), new Vector2(-150f, -235f), new Vector2(220f, 100f));
        pinRight = HudKit.Txt("Right", pinPanel, 84, TextAnchor.MiddleCenter);
        pinRight.text = "[D]";
        HudKit.Place(pinRight.rectTransform, new Vector2(.5f, .5f), new Vector2(150f, -235f), new Vector2(220f, 100f));
        pinStab = HudKit.Txt("Stab", pinPanel, 110, TextAnchor.MiddleCenter);
        pinStab.text = "[V]  STAB!";
        HudKit.Place(pinStab.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -200f), new Vector2(1000f, 130f));
        Image stabBack = HudKit.Img("Stab Window Back", pinPanel, HudKit.Panel);
        HudKit.Place(stabBack.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -280f), new Vector2(380f, 14f));
        pinStabFill = HudKit.Img("Stab Window", stabBack.transform, new Color(1f, .85f, .3f, 1f));
        HudKit.Stretch(pinStabFill.rectTransform, 2f);
        pinStabFill.sprite = HudKit.White();
        pinStabFill.type = Image.Type.Filled;
        pinStabFill.fillMethod = Image.FillMethod.Horizontal;
        pinStabFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        Image shoveBack = HudKit.Img("Shove Back", pinPanel, HudKit.Panel);
        HudKit.Place(shoveBack.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -310f), new Vector2(440f, 22f));
        pinShoveFill = HudKit.Img("Shove Fill", shoveBack.transform, Color.white);
        HudKit.Stretch(pinShoveFill.rectTransform, 3f);
        pinShoveFill.sprite = HudKit.White();
        pinShoveFill.type = Image.Type.Filled;
        pinShoveFill.fillMethod = Image.FillMethod.Horizontal;
        Image pinTimeBack = HudKit.Img("Pin Time Back", pinPanel, HudKit.Panel);
        HudKit.Place(pinTimeBack.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -334f), new Vector2(440f, 8f));
        pinTimerFill = HudKit.Img("Pin Time", pinTimeBack.transform, HudKit.Danger);
        HudKit.Stretch(pinTimerFill.rectTransform, 1f);
        pinTimerFill.sprite = HudKit.White();
        pinTimerFill.type = Image.Type.Filled;
        pinTimerFill.fillMethod = Image.FillMethod.Horizontal;
        pinPanel.gameObject.SetActive(false);
        pinWinText = HudKit.Txt("Stabbed", t, 80, TextAnchor.MiddleCenter);
        pinWinText.text = "STABBED IT!   [V] KICK";
        HudKit.Place(pinWinText.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 120f), new Vector2(1200f, 100f));
        pinWinText.gameObject.SetActive(false);

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
