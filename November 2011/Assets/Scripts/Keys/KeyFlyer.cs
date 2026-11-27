using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// THE KEY: a giant flying golden key that hunts and eats people. Hangs upright in the air like a
/// key on a chain, chanting, teeth clacking; glitches in and out of sight; feeds on the dead and grows.
///
/// Moveset (picked by range, chained by the pack):
/// - CHOMP (close): jaw unhinges, scream, it snaps at your face. Heavy damage, can tear off a chunk.
/// - TONGUE WHIP (mid): the tongue swings across the room in a wide arc and knocks you off your feet.
/// - TONGUE SPEAR (far): gurgle, then a fast stab that yanks you through the air into a chomp.
/// - BLADE DIVE (any): rises to the ceiling, a red mark glows on the floor under you, then it drops
///   blade-first and impales the floor. Stuck afterwards with its face at your level: punish it.
/// - GORE SPIT (far): vomits globs of half-eaten crew that blind you and burn where they land.
/// - SCREAM (pack): lights blow out, your aim shakes, the others pounce.
/// - PIN: a chomp from behind, out of a rush, or while you're down can slam you to the floor and
///   get on top of you (see <see cref="PlayerPinned"/>): shove it back, then stab it.
/// - SWALLOW (finisher): only if you're knocked down or nearly dead: it eats you (shoot the throat
///   or pry it open, or it kills you).
/// The throat (inside the mouth) is the weak spot; the blade shaft is armoured.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public sealed class KeyFlyer : MonoBehaviour, IKickable
{
    public enum State
    {
        Hidden, Appear, Hunt, Feed,
        BiteWindUp, Bite, WhipWindUp, Whip, SpearWindUp, SpearOut, Yank,
        DiveRise, DiveAim, DiveFall, Stuck, SpitWindUp, Spit, Scream,
        EatWindUp, Eating, Pin, Recover, Stagger, Dead,
    }

    [Header("Model")]
    [SerializeField] Transform visual;
    [SerializeField] Transform jawUpper;
    [SerializeField] Transform jawLower;
    [Tooltip("How far each jaw swings when the mouth is fully open (degrees about its hinge): upper, lower.")]
    [SerializeField] Vector2 jawOpenDegrees = new Vector2(-60f, 80f);
    [SerializeField] GameObject mouthInterior;
    [Tooltip("Shown only while the mouth is open (so the shut face stays as painted).")]
    [SerializeField] GameObject[] fangs;
    [SerializeField] Collider throatHitbox;
    [SerializeField] Light throatLight;
    [SerializeField] Transform mouthPoint;
    [SerializeField] Renderer[] glowRenderers;
    [SerializeField] Color glowColor = new Color(1f, .2f, .05f);
    [SerializeField] LineRenderer tongueLine;
    [SerializeField] Transform tongueTip;
    [Tooltip("Glowing material for the blade-dive floor marker.")]
    [SerializeField] Material diveMarkerMaterial;
    [Tooltip("Material for vomited gore globs.")]
    [SerializeField] Material goreMaterial;

    [Header("Chomp")]
    [SerializeField] float chompRange = 3.6f;
    [SerializeField] float chompWindUp = .42f;
    [SerializeField] float chompLungeSpeed = 15f;
    [SerializeField] float chompDamage = 32f;
    [Range(0, 1)] [SerializeField] float chompInjuryChance = .3f;

    [Header("Tongue whip")]
    [SerializeField] Vector2 whipRange = new Vector2(2.5f, 8f);
    [SerializeField] float whipWindUp = .5f;
    [SerializeField] float whipSeconds = .32f;
    [SerializeField] float whipArc = 140f;
    [SerializeField] float whipDamage = 22f;
    [SerializeField] float whipKnockdown = 1f;

    [Header("Tongue spear")]
    [SerializeField] Vector2 spearRange = new Vector2(7f, 18f);
    [SerializeField] float spearWindUp = .6f;
    [SerializeField] float spearSpeed = 45f;
    [SerializeField] float spearDamage = 16f;
    [SerializeField] float yankSpeed = 16f;
    [Tooltip("Damage to the tongue (during the gurgle or the stab) that cuts it.")]
    [SerializeField] float tongueToughness = 25f;

    [Header("Blade dive")]
    [SerializeField] float diveCooldown = 9f;
    [SerializeField] float diveAimSeconds = .95f;
    [SerializeField] float diveFallSpeed = 28f;
    [SerializeField] float diveDamage = 45f;
    [SerializeField] float diveRadius = 2.6f;
    [SerializeField] float stuckSeconds = 2.2f;

    [Header("Gore spit")]
    [SerializeField] Vector2 spitRange = new Vector2(5f, 16f);
    [SerializeField] float spitWindUp = .55f;
    [SerializeField] int spitGlobs = 3;
    [SerializeField] float spitDamage = 10f;
    [SerializeField] float puddleDps = 14f;

    [Header("Scream")]
    [SerializeField] float screamCooldown = 16f;
    [SerializeField] float screamSeconds = 1.1f;

    [Header("Swallow (finisher)")]
    [SerializeField] float eatWindUp = .45f;
    [SerializeField] float holdDistance = .95f;
    [SerializeField] float chewInterval = .55f;
    [SerializeField] float chewDamage = 6f;
    [SerializeField] float biteAfter = 2.2f;
    [SerializeField] float swallowDamage = 70f;
    [SerializeField] float escapeDamage = 60f;

    [Header("Flight")]
    [SerializeField] Vector2 orbitRadius = new Vector2(5f, 10f);
    [SerializeField] Vector2 orbitHeight = new Vector2(2.6f, 3.3f);
    [Tooltip("Chance a key is a ranged one that circles further out.")]
    [Range(0, 1)] [SerializeField] float rangedChance = .35f;
    [SerializeField] Vector2 rangedRadius = new Vector2(8f, 11f);
    [SerializeField] float cruiseSpeed = 7f;
    [SerializeField] float acceleration = 16f;
    [SerializeField] Vector2 attackCooldown = new Vector2(1.2f, 2.6f);
    [SerializeField] LayerMask worldMask = ~0;

    [Header("Glitching")]
    [SerializeField] Vector2 glitchCooldown = new Vector2(8f, 14f);
    [SerializeField] Vector2 hiddenSeconds = new Vector2(1.2f, 2.4f);
    [SerializeField] Vector2 appearDistance = new Vector2(3.5f, 6f);

    [Header("Feeding & growth")]
    [SerializeField] float feedRate = 18f;
    [SerializeField] float meatPerGrowth = 60f;
    [SerializeField] float growthPerStep = .15f;
    [SerializeField] float maxGrowth = 1.6f;

    [Header("Stagger")]
    [Tooltip("Staggers each time it loses this fraction of its health.")]
    [Range(.1f, .6f)] [SerializeField] float staggerEvery = .5f;
    [SerializeField] float staggerSeconds = 1.6f;
    [Tooltip("Stun when you cancel an attack (cut the tongue, blast the mouth mid wind-up).")]
    [SerializeField] float cancelStunSeconds = .9f;
    [Tooltip("Damage into the mouth during a chomp/spit wind-up needed to stop it (less only flinches it).")]
    [SerializeField] float interruptDamage = 120f;

    [Header("Aggression")]
    [Tooltip("After it's shot, it attacks again within this many seconds (if it can).")]
    [SerializeField] float retaliateSeconds = .35f;
    [Tooltip("Chance it rushes in for a chomp when it's ready to attack but too far for one.")]
    [Range(0, 1)] [SerializeField] float rushChance = .4f;
    [SerializeField] float rushSpeed = 13f;
    [Tooltip("Other keys this close follow up on an attack within (min, max) seconds.")]
    [SerializeField] float packRadius = 14f;
    [SerializeField] Vector2 packFollowUp = new Vector2(.4f, 1f);

    [Header("Pin")]
    [Tooltip("Chance a chomp pins you instead of only biting, when it comes from behind, out of a rush, or while you're down.")]
    [Range(0, 1)] [SerializeField] float pinChance = .75f;
    [Tooltip("A chomp from more than this many degrees off where you're looking counts as from behind.")]
    [SerializeField] float pinBehindAngle = 100f;
    [SerializeField] float pinSlamDamage = 14f;

    [Header("Audio")]
    [SerializeField] AudioSource voice;
    [SerializeField] AudioSource sfx;
    [SerializeField] AudioSource whisper;
    [SerializeField] AudioClip[] chantClips;
    [SerializeField] AudioClip[] screamClips;
    [SerializeField] AudioClip[] gurgleClips;
    [SerializeField] AudioClip[] chewClips;
    [SerializeField] AudioClip[] clackClips;
    [SerializeField] AudioClip tongueClip;
    [SerializeField] AudioClip deathClip;
    [SerializeField] Vector2 voicePitch = new Vector2(.5f, .62f);

    public State Current => state;
    public bool IsDead => state == State.Dead;
    public bool IsDiving => rushing || state != State.Hidden && state != State.Appear && state != State.Hunt && state != State.Feed
                            && state != State.Recover && state != State.Stagger && state != State.Stuck && state != State.Dead;
    public bool IsPinning => state == State.Pin;
    public bool IsAttackingDive => state == State.DiveAim || state == State.DiveFall;
    public Vector3 DiveTarget => diveTarget;
    public float Growth => growth;
    public Vector3 MouthPosition => mouthPoint != null ? mouthPoint.position : transform.position;
    public float ReelSpeed => yankSpeed;
    public float EatTime01 => state == State.Eating ? Mathf.Clamp01(stateTime / biteAfter) : 0f;
    public bool KilledByKick { get; private set; }

    public event Action<KeyFlyer> Died;

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    Health health;
    PlayerEaten victim;
    State state;
    float stateTime, orbitAngle, orbitDir, radius, height, nextAttack, nextGlitch, nextChant, nextDive, nextScream, basePitch, seed;
    float mouthOpen, mouthTarget, growth = 1f, meatEaten, tongueDamage, throatDamage, nextChew, lastStaggerAt = 1f, hiddenFor;
    float whipStartAngle, whipY, spitFired, deadFall;
    float staggerFor, windUpDamage, lastHurtTime = -10f, rushUntil;
    bool rushing, rushBite;
    PlayerPinned pinner;
    float nextPinClack;
    bool hitLanded;
    Vector3 velocity, tongueDir, tongueTipPos, baseScale, diveTarget, lungeDir;
    Corpse meal;
    Renderer[] allRenderers;
    Collider[] allColliders;
    bool visible = true;
    MaterialPropertyBlock block;
    GameObject diveMarker;

    // ================= lifecycle =================

    void Awake()
    {
        health = GetComponent<Health>();
        block = new MaterialPropertyBlock();
        seed = UnityEngine.Random.value * 100f;
        orbitDir = UnityEngine.Random.value < .5f ? -1f : 1f;
        // Roles: most swarm in close (chomp, whip, rush); some hang back (spear, spit, dive).
        radius = UnityEngine.Random.value < rangedChance
            ? UnityEngine.Random.Range(rangedRadius.x, rangedRadius.y)
            : UnityEngine.Random.Range(orbitRadius.x, orbitRadius.y);
        height = UnityEngine.Random.Range(orbitHeight.x, orbitHeight.y);
        basePitch = UnityEngine.Random.Range(voicePitch.x, voicePitch.y);
        if (visual != null) baseScale = visual.localScale;
        allRenderers = GetComponentsInChildren<Renderer>(true);
        allColliders = GetComponentsInChildren<Collider>(true);
        SetTongue(false);
        nextDive = Time.time + UnityEngine.Random.Range(4f, diveCooldown);
        nextScream = Time.time + UnityEngine.Random.Range(6f, screamCooldown);
    }

    void OnEnable()
    {
        KeyHorde.RegisterFlyer(this);
        Kickables.Register(this);
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        if (voice != null && chantClips != null && chantClips.Length > 0)
        {
            voice.clip = chantClips[UnityEngine.Random.Range(0, chantClips.Length)];
            voice.loop = true; voice.pitch = basePitch;
            voice.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, voice.clip.length - .1f));
            voice.Play();
        }
        if (whisper != null && whisper.clip != null)
        {
            whisper.loop = true; whisper.volume = 0f;
            whisper.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, whisper.clip.length - .1f));
            whisper.Play();
        }
    }

    void OnDisable()
    {
        KeyHorde.UnregisterFlyer(this);
        Kickables.Unregister(this);
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
        ClearDiveMarker();
    }

    public void Spawn(Vector3 position, Vector3 facing)
    {
        transform.position = position;
        if (Flat(facing).sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(Flat(facing));
        nextAttack = Time.time + UnityEngine.Random.Range(1f, 2.5f);
        nextGlitch = Time.time + UnityEngine.Random.Range(glitchCooldown.x, glitchCooldown.y);
        StartCoroutine(GlitchIn(true));
    }

    public void ApplyDifficulty(Difficulty.Preset p)
    {
        float scale = p.lungeDamage / 16f; // Brutal preset = 1
        chompDamage *= scale; whipDamage *= scale; spearDamage *= scale; diveDamage *= scale; spitDamage *= scale;
        float tell = Mathf.Clamp(p.windUpSeconds / .42f, .8f, 1.4f);
        chompWindUp *= tell; whipWindUp *= tell; spearWindUp *= tell; diveAimSeconds *= tell; spitWindUp *= tell;
    }

    // ================= brain =================

    void Update()
    {
        if (state == State.Dead) { AnimateDead(); return; }
        float dt = Time.deltaTime;
        stateTime += dt;
        KeyTarget target = KeyTarget.Current;
        bool alive = target != null && !target.IsDead;
        PlayerEaten prey = alive ? target.GetComponent<PlayerEaten>() : null;
        bool frenzy = prey != null && prey.IsBleeding;
        float speedMul = frenzy ? 1.25f : 1f;
        mouthTarget = 0f;
        float distance = alive ? Vector3.Distance(transform.position, target.EyePosition) : 99f;

        switch (state)
        {
            case State.Hidden:
                if (alive)
                {
                    Vector3 behind = target.Position - Flat(target.View.transform.forward).normalized * UnityEngine.Random.Range(appearDistance.x, appearDistance.y);
                    Steer(behind + Vector3.up * height, cruiseSpeed * 1.8f, dt);
                }
                if (stateTime >= hiddenFor) StartCoroutine(GlitchIn(false));
                break;

            case State.Appear:
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, 20f * dt);
                transform.position += velocity * dt;
                if (alive) Face(target.EyePosition - transform.position, dt, 720f);
                mouthTarget = .5f;
                break;

            case State.Hunt:
                if (!alive) { rushing = false; Steer(transform.position + Vector3.up, 2f, dt); break; }
                ChantClack();
                if (rushing)
                {
                    // Charging straight at your face for a chomp (it already holds an attack slot).
                    Vector3 side = Flat(transform.position - target.Position);
                    side = side.sqrMagnitude > .01f ? side.normalized : -Flat(target.View.transform.forward).normalized;
                    Steer(target.EyePosition + side * 2f + Vector3.up * .5f, rushSpeed * speedMul, dt);
                    Face(target.EyePosition - transform.position, dt, 720f);
                    mouthTarget = .35f;
                    if (distance <= chompRange) { rushing = false; Enter(State.BiteWindUp); rushBite = true; }
                    else if (Time.time > rushUntil) { rushing = false; KeyHorde.ReleaseDiveToken(this); Enter(State.Hunt); }
                    break;
                }
                orbitAngle += 50f * orbitDir * dt;
                float a = orbitAngle * Mathf.Deg2Rad;
                Vector3 desired = target.Position + new Vector3(Mathf.Sin(a) * radius, height + Mathf.Sin((Time.time + seed) * 1.7f) * .35f, Mathf.Cos(a) * radius);
                Steer(KeepInside(target.Position + Vector3.up * height, desired), cruiseSpeed * speedMul, dt);
                Face(target.EyePosition - transform.position, dt, 360f);
                if (Time.time >= nextAttack && KeyHorde.TryTakeDiveToken(this))
                {
                    if (!ChooseAttack(target, prey, distance)) { KeyHorde.ReleaseDiveToken(this); nextAttack = Time.time + .4f; }
                }
                // Glitching away is for stalking, not for running from a fight.
                else if (!frenzy && Time.time >= nextGlitch && Time.time - lastHurtTime > 3f && !target.IsAimingAt(transform.position, 25f)) GlitchOut();
                // Feeding only while the hunt has backed off.
                else if (!frenzy && meal == null && IsLull() && Mathf.Repeat(Time.time + seed, 3f) < dt) TryStartFeeding(target);
                break;

            case State.Feed: UpdateFeed(target, alive, frenzy, dt); break;

            // ---------- CHOMP ----------
            case State.BiteWindUp:
                mouthTarget = 1f;
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, 25f * dt);
                transform.position += velocity * dt;
                if (alive) Face(target.EyePosition - transform.position, dt, 900f);
                if (stateTime >= chompWindUp)
                {
                    lungeDir = alive ? (target.EyePosition + target.Velocity * .15f - MouthPosition).normalized : transform.forward;
                    hitLanded = false;
                    Enter(State.Bite);
                }
                break;

            case State.Bite:
                mouthTarget = stateTime < .18f ? 1f : 0f; // snaps shut
                transform.position += lungeDir * chompLungeSpeed * speedMul * dt;
                if (!hitLanded && alive && Vector3.Distance(MouthPosition, target.EyePosition) < 1.6f)
                {
                    hitLanded = true;
                    PlayOneShot(chewClips, 1f);
                    BloodVFX.Spray(target.EyePosition, lungeDir, BloodVFX.HumanBlood, 2f);
                    // From behind, out of a rush, or while you're down: it slams you to the floor and gets on top of you.
                    if (prey != null && WantsPin(target, prey) && TryPin(prey)) break;
                    prey?.Hit(chompDamage, gameObject, lungeDir, 5f, chompInjuryChance, 0f);
                }
                if (hitLanded || stateTime > .35f) Enter(State.Recover);
                break;

            // ---------- PIN ----------
            case State.Pin:
                if (!alive || pinner == null || pinner.Key != this) { pinner = null; Enter(State.Recover); break; }
                UpdatePin(target, dt);
                break;

            // ---------- TONGUE WHIP ----------
            case State.WhipWindUp:
                mouthTarget = .9f;
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, 20f * dt);
                transform.position += velocity * dt;
                if (alive) Face(target.EyePosition - transform.position, dt, 600f);
                // Tongue curls out to one side, dripping.
                SetTongue(true);
                Vector3 curl = MouthPosition + (transform.right * 1.6f * (whipStartAngle > 0 ? 1f : -1f)) + Vector3.up * .6f + transform.forward * .5f;
                UpdateTongue(Vector3.Lerp(MouthPosition, curl, Mathf.Clamp01(stateTime / whipWindUp)), .1f);
                if (stateTime >= whipWindUp)
                {
                    hitLanded = false;
                    // The sweep height is set now: jump over it, or dash through it.
                    whipY = alive ? FloorPoint(target.Position).y + 1.05f : MouthPosition.y;
                    PlayOneShot(screamClips, .8f);
                    if (sfx != null && tongueClip != null) sfx.PlayOneShot(tongueClip);
                    Enter(State.Whip);
                }
                break;

            case State.Whip:
            {
                // The tongue sweeps a wide arc at chest height around you.
                float t = Mathf.Clamp01(stateTime / whipSeconds);
                float swing = Mathf.Lerp(whipStartAngle, -whipStartAngle, t * t * (3f - 2f * t));
                float reach = alive ? Vector3.Distance(Flat(MouthPosition), Flat(target.Position)) + 1.8f : 6f;
                Vector3 baseDir = alive ? Flat(target.Position - MouthPosition).normalized : transform.forward;
                Vector3 dir = Quaternion.AngleAxis(swing, Vector3.up) * baseDir;
                Vector3 sweepStart = new Vector3(MouthPosition.x, whipY, MouthPosition.z) + dir * .8f;
                Vector3 tip = new Vector3(MouthPosition.x, whipY, MouthPosition.z) + dir * reach;
                UpdateTongue(tip, .2f);
                if (!hitLanded && alive)
                {
                    Vector3 chest = target.Position + Vector3.up * 1.05f;
                    Vector3 closest = ClosestPointOnSegment(sweepStart, tip, chest);
                    if (Vector3.Distance(closest, chest) < .7f)
                    {
                        hitLanded = true;
                        Vector3 side = Vector3.Cross(Vector3.up, dir) * -Mathf.Sign(whipStartAngle);
                        prey?.Hit(whipDamage, gameObject, side, 7f, 0f, whipKnockdown);
                    }
                }
                if (t >= 1f) { SetTongue(false); Enter(State.Recover); }
                break;
            }

            // ---------- TONGUE SPEAR ----------
            case State.SpearWindUp:
                mouthTarget = 1f;
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, 15f * dt);
                transform.position += velocity * dt;
                if (alive) Face(target.EyePosition - transform.position, dt, 600f);
                if (stateTime >= spearWindUp)
                {
                    Vector3 aim = alive ? target.Position + Vector3.up * 1.1f + target.Velocity * .12f : MouthPosition + transform.forward;
                    tongueDir = (aim - MouthPosition).normalized;
                    tongueTipPos = MouthPosition;
                    tongueDamage = 0f;
                    SetTongue(true);
                    if (sfx != null && tongueClip != null) { sfx.pitch = UnityEngine.Random.Range(.8f, 1f); sfx.PlayOneShot(tongueClip); }
                    Enter(State.SpearOut);
                }
                break;

            case State.SpearOut:
                mouthTarget = 1f;
                Vector3 tipBefore = tongueTipPos;
                tongueTipPos += tongueDir * spearSpeed * dt;
                UpdateTongue(tongueTipPos, .05f);
                if (alive && Vector3.Distance(ClosestPointOnSegment(tipBefore, tongueTipPos, target.Position + Vector3.up * 1.1f), target.Position + Vector3.up * 1.1f) <= .85f)
                {
                    prey?.Hit(spearDamage, gameObject, tongueDir, 0f, 0f, 0f);
                    if (prey != null && prey.BeginPull(this)) { victim = prey; PlayOneShot(screamClips, 1f); Enter(State.Yank); break; }
                }
                if (Vector3.Distance(tongueTipPos, MouthPosition) > spearRange.y + 1f ||
                    Physics.Raycast(tongueTipPos - tongueDir * .5f, tongueDir, .6f, worldMask, QueryTriggerInteraction.Ignore))
                    RetractTongue();
                break;

            case State.Yank:
                // Ripping you through the air toward its mouth; it chomps as you arrive.
                mouthTarget = 1f;
                velocity = Vector3.zero;
                if (!alive || victim == null || !victim.IsPulledBy(this)) { RetractTongue(); break; }
                Face(target.EyePosition - transform.position, dt, 600f);
                UpdateTongue(target.Position + Vector3.up * 1.1f, .05f);
                break;

            // ---------- BLADE DIVE ----------
            case State.DiveRise:
            {
                // Up to the ceiling above you.
                Vector3 above = alive ? target.Position : transform.position;
                above.y = CeilingHeight(above) - 1f;
                Steer(above, cruiseSpeed * 1.6f, dt);
                Face(alive ? target.Position - transform.position : transform.forward, dt, 360f);
                if (Vector3.Distance(transform.position, above) < .8f || stateTime > 2f)
                {
                    diveTarget = alive ? target.Position : FloorPoint(transform.position);
                    CreateDiveMarker(diveTarget);
                    PlayOneShot(screamClips, 1f);
                    Enter(State.DiveAim);
                }
                break;
            }

            case State.DiveAim:
            {
                // Tracking you from above, the mark on the floor following, then committing.
                mouthTarget = .6f;
                if (alive && stateTime < diveAimSeconds * .7f)
                    diveTarget = Vector3.MoveTowards(diveTarget, FloorPoint(target.Position), 4.5f * dt);
                Vector3 over = diveTarget; over.y = transform.position.y;
                transform.position = Vector3.MoveTowards(transform.position, over, 9f * dt);
                transform.position += UnityEngine.Random.insideUnitSphere * .02f; // shuddering
                UpdateDiveMarker(Mathf.Clamp01(stateTime / diveAimSeconds));
                if (stateTime >= diveAimSeconds) Enter(State.DiveFall);
                break;
            }

            case State.DiveFall:
            {
                // Blade-first, straight down.
                float bladeLength = 2.1f * growth;
                float stopY = diveTarget.y + bladeLength - .45f;
                transform.position = new Vector3(diveTarget.x, Mathf.MoveTowards(transform.position.y, stopY, diveFallSpeed * dt), diveTarget.z);
                if (transform.position.y <= stopY + .01f) DiveImpact(target, prey);
                break;
            }

            case State.Stuck:
                // Blade buried in the floor, head at face level, mouth slack: punish it.
                mouthTarget = .55f;
                transform.position += UnityEngine.Random.insideUnitSphere * .008f;
                if (stateTime >= stuckSeconds) { velocity = Vector3.up * 5f; Enter(State.Recover); }
                break;

            // ---------- GORE SPIT ----------
            case State.SpitWindUp:
                mouthTarget = 1f;
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, 15f * dt);
                transform.position += velocity * dt;
                if (alive) Face(target.EyePosition - transform.position, dt, 600f);
                if (stateTime >= spitWindUp) { spitFired = 0; Enter(State.Spit); }
                break;

            case State.Spit:
                mouthTarget = 1f;
                if (alive) Face(target.EyePosition - transform.position, dt, 600f);
                if (stateTime >= spitFired * .14f && spitFired < spitGlobs)
                {
                    spitFired++;
                    PlayOneShot(gurgleClips, .7f);
                    if (alive) GoreGlob.Launch(MouthPosition, BallisticVelocity(MouthPosition, target.Position + Vector3.up * .9f + target.Velocity * .35f + UnityEngine.Random.insideUnitSphere * 1.1f),
                        spitDamage, puddleDps, 5f, goreMaterial, gameObject);
                }
                if (spitFired >= spitGlobs && stateTime > spitGlobs * .14f + .2f) Enter(State.Recover);
                break;

            // ---------- SCREAM ----------
            case State.Scream:
                mouthTarget = 1f;
                transform.position += UnityEngine.Random.insideUnitSphere * .03f;
                if (alive) Face(target.EyePosition - transform.position, dt, 600f);
                if (stateTime >= screamSeconds) Enter(State.Recover);
                break;

            // ---------- SWALLOW (finisher) ----------
            case State.EatWindUp:
                mouthTarget = 1f;
                if (!alive) { Enter(State.Recover); break; }
                Vector3 toFace = target.EyePosition - MouthPosition;
                velocity = toFace.normalized * chompLungeSpeed;
                transform.position += velocity * dt;
                Face(toFace, dt, 900f);
                if (toFace.magnitude <= 1.4f && prey != null && prey.BeginEaten(this)) { victim = prey; StartEating(); }
                else if (stateTime > eatWindUp + .7f) Enter(State.Recover);
                break;

            case State.Eating:
                mouthTarget = .75f + .25f * Mathf.Abs(Mathf.Sin(Time.time * 7f));
                if (!alive || victim == null || !victim.IsEatenBy(this)) { Enter(State.Recover); break; }
                HoldInFrontOf(target, dt);
                if (Time.time >= nextChew) { nextChew = Time.time + chewInterval; PlayOneShot(chewClips, 1f); victim.Chew(chewDamage, gameObject); }
                if (stateTime >= biteAfter)
                {
                    PlayOneShot(screamClips, 1f);
                    victim.Bitten(swallowDamage, gameObject);
                    victim = null;
                    Enter(State.Recover);
                }
                break;

            case State.Recover:
            {
                // Swings off to the side and straight back into the circle: no running away.
                Vector3 toward = alive ? Flat(target.Position - transform.position).normalized : transform.forward;
                Vector3 strafe = Vector3.Cross(Vector3.up, toward) * orbitDir;
                Steer(transform.position + strafe * 2.5f - toward * .8f + Vector3.up * .6f, cruiseSpeed, dt);
                if (alive) Face(target.EyePosition - transform.position, dt, 360f);
                if (stateTime > .35f) { KeyHorde.ReleaseDiveToken(this); Enter(State.Hunt); }
                break;
            }

            case State.Stagger:
                mouthTarget = .7f;
                Vector3 low = new Vector3(transform.position.x, FloorHeight() + 1.9f * growth, transform.position.z);
                transform.position = Vector3.MoveTowards(transform.position, low, 6f * dt);
                velocity = Vector3.zero;
                if (stateTime >= staggerFor) Enter(State.Hunt);
                break;
        }

        AnimateBody(dt);
        UpdateGlow();
        if (voice != null && voice.isPlaying)
        {
            voice.volume = visible ? (IsDiving ? 1f : .7f) : 0f;
            voice.pitch = basePitch / Mathf.Sqrt(growth) * (frenzy ? 1.12f : 1f);
        }
        if (whisper != null) whisper.volume = visible ? 0f : .9f;
    }

    /// <summary>Pick an attack for this range. Returns false if nothing fits right now.</summary>
    bool ChooseAttack(KeyTarget target, PlayerEaten prey, float distance)
    {
        var options = new List<(State s, float w)>();
        if (prey != null && prey.CanBeExecuted && distance < 7f) options.Add((State.EatWindUp, 6f));
        if (distance <= chompRange) options.Add((State.BiteWindUp, 5f));
        if (distance >= whipRange.x && distance <= whipRange.y) options.Add((State.WhipWindUp, 4f));
        if (distance >= spearRange.x && distance <= spearRange.y && HasLineTo(target.EyePosition)) options.Add((State.SpearWindUp, 3.5f));
        if (distance >= spitRange.x && distance <= spitRange.y && HasLineTo(target.EyePosition)) options.Add((State.SpitWindUp, 2.5f));
        if (Time.time >= nextDive && CeilingHeight(target.Position) - FloorHeight() > 3.5f) options.Add((State.DiveRise, 3f));
        if (Time.time >= nextScream && CountKeysNear(14f) >= 2) options.Add((State.Scream, 1.5f));
        if (distance > chompRange && distance < 12f && HasLineTo(target.EyePosition)) options.Add((State.Hunt, 10f * rushChance)); // rush in
        if (options.Count == 0) return false;

        float total = 0f; foreach (var o in options) total += o.w;
        float roll = UnityEngine.Random.value * total;
        State pick = options[0].s;
        foreach (var o in options) { if ((roll -= o.w) <= 0f) { pick = o.s; break; } }

        if (pick == State.Hunt)
        {
            // Rush: charge in and chomp. Keeps the attack slot while closing.
            rushing = true;
            rushUntil = Time.time + 2.2f;
            PlayOneShot(screamClips, 1f);
            CallPack();
            return true;
        }
        switch (pick)
        {
            case State.WhipWindUp: whipStartAngle = whipArc * .5f * (UnityEngine.Random.value < .5f ? -1f : 1f); break;
            case State.DiveRise: nextDive = Time.time + diveCooldown; break;
            case State.Scream: nextScream = Time.time + screamCooldown; DoScream(target, prey); break;
        }
        Enter(pick);
        return true;
    }

    /// <summary>One attacks, the ones nearby pile in right after.</summary>
    void CallPack()
    {
        foreach (KeyFlyer k in KeyHorde.Flyers)
        {
            if (k == null || k == this || k.IsDead || k.state != State.Hunt || k.rushing) continue;
            if (Vector3.Distance(k.transform.position, transform.position) > packRadius) continue;
            k.nextAttack = Mathf.Min(k.nextAttack, Time.time + UnityEngine.Random.Range(packFollowUp.x, packFollowUp.y));
        }
    }

    /// <summary>The director has backed off (or there is none): time to eat.</summary>
    static bool IsLull()
    {
        KeyHordeDirector d = KeyHordeDirector.Active;
        return d == null || d.Current == KeyHordeDirector.Phase.Relax;
    }

    void DoScream(KeyTarget target, PlayerEaten prey)
    {
        PlayOneShot(screamClips, 1f);
        EmergencyLight.Blackout(2.5f);
        prey?.Screamed(2.2f);
        // The pack takes the opening.
        foreach (KeyFlyer k in KeyHorde.Flyers)
            if (k != null && k != this && !k.IsDead) k.nextAttack = Mathf.Min(k.nextAttack, Time.time + .3f);
    }

    int CountKeysNear(float r)
    {
        int n = 0;
        foreach (KeyFlyer k in KeyHorde.Flyers) if (k != null && !k.IsDead && Vector3.Distance(k.transform.position, transform.position) <= r) n++;
        return n;
    }

    /// <summary>The yanked player has reached the mouth: swallow them if they're weak, else chomp.</summary>
    public void ReeledIn(PlayerEaten prey)
    {
        if (state != State.Yank) return;
        SetTongue(false);
        victim = null;
        if (prey != null && prey.CanBeExecuted && prey.BeginEaten(this)) { victim = prey; StartEating(); return; }
        lungeDir = (KeyTarget.Current != null ? KeyTarget.Current.EyePosition - MouthPosition : transform.forward).normalized;
        hitLanded = false;
        Enter(State.Bite);
    }

    // ================= pin =================

    bool WantsPin(KeyTarget target, PlayerEaten prey)
    {
        Vector3 toMe = Flat(transform.position - target.Position);
        bool behind = toMe.sqrMagnitude > .01f && target.View != null
                      && Vector3.Angle(Flat(target.View.transform.forward), toMe) > pinBehindAngle;
        return (rushBite || behind || prey.IsDown) && UnityEngine.Random.value < pinChance;
    }

    /// <summary>Slam the player to the floor and get on top of them. False if they can't be pinned now.</summary>
    public bool TryPin(PlayerEaten prey)
    {
        if (state == State.Dead || prey == null || prey.Pinned == null) return false;
        KeyHorde.TryTakeDiveToken(this); // keep (or take) the attack slot while it's on you
        if (!prey.Pinned.TryBegin(this)) return false;
        pinner = prey.Pinned;
        velocity = Vector3.zero;
        nextPinClack = Time.time + .2f;
        Enter(State.Pin);
        PlayOneShot(screamClips, 1f);
        prey.Wound(pinSlamDamage, gameObject, 0f);
        return true;
    }

    void UpdatePin(KeyTarget target, float dt)
    {
        // On top of you, jaws snapping at your face: the harder it's winning, the closer they get.
        // When you've shoved it back it rears up, throat open: that's the stab.
        Transform cam = target.View.transform;
        bool rearing = pinner.StabReady;
        float bit = Mathf.Clamp01(1f - (Time.time - pinner.LastBiteTime) / .25f);
        float shoved = Mathf.Clamp01(1f - (Time.time - pinner.LastShoveTime) / .15f);
        float reach = rearing ? 1.85f : Mathf.Lerp(1.6f, .6f, pinner.Pressure) + shoved * .15f - bit * .3f;
        float shake = rearing ? .02f : .04f + .06f * pinner.Pressure;
        Vector3 want = cam.position + cam.forward * reach + cam.up * (rearing ? .4f : 0f)
                       + (cam.right * Mathf.Sin(Time.time * 23f + seed) + cam.up * Mathf.Sin(Time.time * 29f)) * shake;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(-cam.forward, Vector3.up), 1f - Mathf.Exp(-12f * dt));
        transform.position += (want - MouthPosition) * Mathf.Min(1f, 16f * dt);
        velocity = Vector3.zero;
        mouthTarget = rearing ? 1f : bit > 0f ? 0f : .7f + .3f * Mathf.Abs(Mathf.Sin(Time.time * 13f + seed)); // fangs always showing
        if (!rearing && Time.time >= nextPinClack)
        {
            nextPinClack = Time.time + Mathf.Lerp(.4f, .18f, pinner.Pressure);
            PlayOneShot(clackClips, .9f);
        }
    }

    /// <summary>You shoved it back far enough: it rears up, throat open.</summary>
    public void PinStabOpening() { if (state == State.Pin) PlayOneShot(screamClips, 1f); }

    /// <summary>You didn't stab in time: it comes back down.</summary>
    public void PinStabMissed() { if (state == State.Pin) PlayOneShot(gurgleClips, 1f); }

    /// <summary>The jaws reached you.</summary>
    public void PinBite()
    {
        if (state != State.Pin) return;
        PlayOneShot(chewClips, 1f);
        mouthOpen = 0f; // snaps shut
        BloodVFX.Spray(MouthPosition, -transform.forward + Vector3.up * .3f, BloodVFX.HumanBlood, 2.2f);
    }

    /// <summary>The pin is over: you stabbed it off (staggered, kickable), or it let go.</summary>
    public void PinReleased(bool playerWon)
    {
        if (state != State.Pin) return;
        pinner = null;
        if (playerWon) { PlayOneShot(screamClips, 1f); Stagger(staggerSeconds); return; }
        velocity = Vector3.up * 4f + transform.forward * -3f;
        Enter(State.Recover);
    }

    void DiveImpact(KeyTarget target, PlayerEaten prey)
    {
        ClearDiveMarker();
        PlayOneShot(chewClips, 1f);
        PlayOneShot(screamClips, .8f);
        ImpactVFX.PlaySparks(diveTarget + Vector3.up * .05f, Vector3.up, 4f);
        BloodVFX.Spray(diveTarget, Vector3.up, BloodVFX.KeyIchor, 1.5f);
        Noise.Emit(diveTarget, 15f, gameObject);
        if (target != null && !target.IsDead)
        {
            Vector3 d = Flat(target.Position - diveTarget);
            float dist = d.magnitude;
            if (dist <= diveRadius && prey != null)
            {
                bool impaled = dist < 1.1f;
                prey.Hit(impaled ? diveDamage : diveDamage * .45f, gameObject, d.sqrMagnitude > .01f ? d.normalized : Vector3.forward,
                    impaled ? 4f : 8f, impaled ? .5f : 0f, impaled ? 1.2f : .7f);
                if (impaled) BloodVFX.Spray(target.Position + Vector3.up, Vector3.up, BloodVFX.HumanBlood, 3f);
            }
            var cam = target.View != null ? target.View.GetComponent<CameraKick>() : null;
            if (cam != null) cam.Play(Mathf.Lerp(2f, .5f, Mathf.Clamp01(dist / 10f)), 2f, 7);
        }
        Enter(State.Stuck);
    }

    void CreateDiveMarker(Vector3 at)
    {
        ClearDiveMarker();
        diveMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        diveMarker.name = "Dive Marker";
        Destroy(diveMarker.GetComponent<Collider>());
        if (diveMarkerMaterial != null) diveMarker.GetComponent<Renderer>().sharedMaterial = diveMarkerMaterial;
        var l = new GameObject("Glow").AddComponent<Light>();
        l.transform.SetParent(diveMarker.transform, false);
        l.transform.localPosition = Vector3.up * 2f;
        l.type = LightType.Point; l.color = new Color(1f, .1f, .05f); l.range = 4f; l.intensity = 3f; l.shadows = LightShadows.None;
        UpdateDiveMarker(0f);
    }

    void UpdateDiveMarker(float t)
    {
        if (diveMarker == null) return;
        diveMarker.transform.position = diveTarget + Vector3.up * .02f;
        float pulse = 1f + Mathf.Sin(Time.time * 25f) * .08f * t;
        diveMarker.transform.localScale = new Vector3(diveRadius * 2f * pulse, .005f, diveRadius * 2f * pulse);
    }

    void ClearDiveMarker()
    {
        if (diveMarker != null) Destroy(diveMarker);
        diveMarker = null;
    }

    // ================= feeding =================

    void UpdateFeed(KeyTarget target, bool alive, bool frenzy, float dt)
    {
        if (meal == null || meal.IsGone || frenzy || (alive && Vector3.Distance(transform.position, target.Position) < 4f) || stateTime > 7f)
        { StopFeeding(); Enter(State.Hunt); return; }
        Steer(meal.FeedPoint + Vector3.up * 1.7f * growth - Flat(transform.forward) * .6f, cruiseSpeed, dt);
        Face(meal.FeedPoint - transform.position, dt, 300f);
        if (Vector3.Distance(transform.position, meal.FeedPoint) < 2.4f * growth)
        {
            mouthTarget = .55f + .45f * Mathf.Abs(Mathf.Sin(Time.time * 9f));
            float got = meal.Bite(feedRate * dt);
            Grow(got);
            health.Heal(got * .6f);
            if (Time.time >= nextChew) { nextChew = Time.time + .4f; PlayOneShot(chewClips, .9f); }
        }
    }

    void TryStartFeeding(KeyTarget target)
    {
        Corpse best = null;
        float bestDistance = 25f;
        foreach (Corpse c in Corpse.All)
        {
            if (c == null || c.IsGone || (c.Feeder != null && c.Feeder != this)) continue;
            if (target != null && Vector3.Distance(c.transform.position, target.Position) < 5f) continue;
            float d = Vector3.Distance(c.transform.position, transform.position);
            if (d < bestDistance) { bestDistance = d; best = c; }
        }
        if (best == null || !best.TryClaim(this)) return;
        meal = best;
        Enter(State.Feed);
    }

    void StopFeeding() { if (meal != null) meal.Release(this); meal = null; }

    void Grow(float meat)
    {
        meatEaten += meat;
        float wanted = Mathf.Min(maxGrowth, 1f + Mathf.Floor(meatEaten / meatPerGrowth) * growthPerStep);
        if (wanted <= growth + .001f) return;
        growth = wanted;
        health.SetMax(health.Max * 1.2f, true);
        chompDamage *= 1.15f; whipDamage *= 1.15f; diveDamage *= 1.15f; swallowDamage *= 1.1f;
        PlayOneShot(screamClips, .8f);
    }

    // ================= swallow =================

    void StartEating()
    {
        SetTongue(false);
        nextChew = Time.time + .2f;
        throatDamage = 0f;
        PlayOneShot(screamClips, 1f);
        Enter(State.Eating);
    }

    void HoldInFrontOf(KeyTarget target, float dt)
    {
        Transform cam = target.View.transform;
        Vector3 want = cam.position + cam.forward * holdDistance
                       + cam.right * (Mathf.Sin(Time.time * 11f + seed) * .08f) + cam.up * (Mathf.Sin(Time.time * 13f) * .06f);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(-cam.forward, Vector3.up), 1f - Mathf.Exp(-12f * dt));
        transform.position += (want - MouthPosition) * Mathf.Min(1f, 14f * dt);
    }

    // ================= tongue =================

    void SetTongue(bool on)
    {
        if (tongueLine != null) tongueLine.enabled = on && visible;
        if (tongueTip != null) tongueTip.gameObject.SetActive(on && visible);
    }

    void UpdateTongue(Vector3 tip, float wobble)
    {
        if (tongueLine != null)
        {
            int n = tongueLine.positionCount;
            Vector3 start = MouthPosition;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                Vector3 p = Vector3.Lerp(start, tip, t);
                p += Vector3.up * Mathf.Sin(t * Mathf.PI) * (.15f + wobble) * Mathf.Sin(Time.time * 18f + t * 6f);
                tongueLine.SetPosition(i, p);
            }
        }
        if (tongueTip != null) tongueTip.position = tip;
        tongueTipPos = tip;
    }

    public void TakeTongueTearDamage()
    {
        if (state != State.Yank && state != State.SpearOut) return;
        BloodVFX.Spray(tongueTipPos, Vector3.down, BloodVFX.KeyIchor, 1.5f);
        PlayOneShot(screamClips, .9f);
        RetractTongue();
    }

    void RetractTongue()
    {
        if (victim != null && victim.IsPulledBy(this)) victim.EndPull(false);
        victim = null;
        SetTongue(false);
        Enter(State.Recover);
    }

    // ================= glitch =================

    void GlitchOut()
    {
        StopFeeding();
        StartCoroutine(GlitchOutRoutine());
    }

    IEnumerator GlitchOutRoutine()
    {
        Enter(State.Appear);
        for (int i = 0; i < 6; i++) { SetVisible(i % 2 == 1); yield return new WaitForSeconds(.05f); }
        SetVisible(false);
        hiddenFor = UnityEngine.Random.Range(hiddenSeconds.x, hiddenSeconds.y);
        Enter(State.Hidden);
    }

    IEnumerator GlitchIn(bool firstTime)
    {
        Enter(State.Appear);
        SetVisible(false);
        yield return new WaitForSeconds(firstTime ? UnityEngine.Random.Range(0f, .4f) : 0f);
        EmergencyLight.Blackout(.18f);
        PlayOneShot(screamClips, 1f);
        for (int i = 0; i < 7; i++) { SetVisible(i % 2 == 0); yield return new WaitForSeconds(.04f + i * .01f); }
        SetVisible(true);
        nextGlitch = Time.time + UnityEngine.Random.Range(glitchCooldown.x, glitchCooldown.y);
        yield return new WaitForSeconds(.2f);
        if (state == State.Appear)
        {
            // Appeared right on top of you: straight into a chomp.
            KeyTarget t = KeyTarget.Current;
            bool close = t != null && !t.IsDead && Vector3.Distance(transform.position, t.EyePosition) <= chompRange + 1f;
            if (close && KeyHorde.TryTakeDiveToken(this)) Enter(State.BiteWindUp);
            else Enter(State.Hunt);
        }
    }

    void SetVisible(bool on)
    {
        visible = on;
        foreach (Renderer r in allRenderers)
            if (r != null && r != tongueLine && (tongueTip == null || !r.transform.IsChildOf(tongueTip))) r.enabled = on;
        foreach (Collider c in allColliders)
            if (c != null && c != throatHitbox && (tongueTip == null || !c.transform.IsChildOf(tongueTip))) c.enabled = on;
    }

    // ================= damage =================

    void OnDamaged(DamageInfo info)
    {
        if (state == State.Dead) return;
        BloodVFX.Spray(info.point, info.direction + info.normal, BloodVFX.KeyIchor, info.part == BodyPart.Throat ? 2.2f : 1f);

        // Shooting the tongue while it's out (or the throat during the gurgle) cuts it short.
        bool tongueOut = state == State.SpearWindUp || state == State.SpearOut || state == State.Yank || state == State.Whip || state == State.WhipWindUp;
        if (tongueOut && (info.part == BodyPart.Tongue || info.part == BodyPart.Throat))
        {
            tongueDamage += info.amount;
            if (tongueDamage >= tongueToughness)
            {
                BloodVFX.Spray(tongueTipPos, Vector3.down, BloodVFX.KeyIchor, 2f);
                PlayOneShot(screamClips, 1f);
                RetractTongue();
                Stagger(cancelStunSeconds);
                return;
            }
        }
        if (state == State.Eating && (info.part == BodyPart.Throat || info.melee))
        {
            throatDamage += info.amount;
            if (throatDamage >= escapeDamage) { SpitOut(); return; }
        }
        // Shots into the open mouth during a chomp or spit wind-up: one only makes it flinch (a
        // fraction later, still coming); a shotgun blast or a couple of clean hits stop it.
        if ((state == State.BiteWindUp || state == State.SpitWindUp) && info.part == BodyPart.Throat)
        {
            windUpDamage += info.amount;
            if (windUpDamage >= interruptDamage) { Stagger(cancelStunSeconds); return; }
            stateTime = Mathf.Max(0f, stateTime - .12f);
            mouthOpen *= .6f;
        }
        if (state == State.Feed) { StopFeeding(); Enter(State.Hunt); }
        lastHurtTime = Time.time;

        if (state != State.Eating && state != State.DiveFall && state != State.Stuck && health.Fraction <= lastStaggerAt - staggerEvery)
        {
            lastStaggerAt = health.Fraction;
            Stagger(staggerSeconds);
            return;
        }
        if (state == State.Hunt && visible)
        {
            // Getting shot makes it angry, not careful: it comes for you sooner.
            nextAttack = Mathf.Min(nextAttack, Time.time + retaliateSeconds);
            // Only a point-blank heavy hit (shotgun) still shoves it back.
            KeyTarget t = KeyTarget.Current;
            if (info.knockback >= 5f && t != null && Vector3.Distance(transform.position, t.EyePosition) < 4f)
                velocity += info.direction * info.knockback * .5f;
        }
    }

    public void SpitOut()
    {
        if (victim != null) victim.Escaped(this);
        victim = null;
        PlayOneShot(screamClips, 1f);
        Stagger(staggerSeconds);
    }

    void Stagger(float seconds)
    {
        staggerFor = seconds;
        ReleaseVictim();
        SetTongue(false);
        StopFeeding();
        ClearDiveMarker();
        KeyHorde.ReleaseDiveToken(this);
        Enter(State.Stagger);
    }

    void ReleaseVictim()
    {
        if (victim == null) return;
        if (victim.IsPulledBy(this)) victim.EndPull(false);
        if (victim.IsEatenBy(this)) victim.Escaped(this);
        victim = null;
    }

    void OnDied(DamageInfo info)
    {
        ReleaseVictim();
        SetTongue(false);
        StopFeeding();
        ClearDiveMarker();
        KeyHorde.ReleaseDiveToken(this);
        KilledByKick = info.melee;
        state = State.Dead;
        stateTime = 0f;
        SetVisible(true);
        if (voice != null) voice.Stop();
        if (whisper != null) whisper.Stop();
        if (deathClip != null) AudioSource.PlayClipAtPoint(deathClip, transform.position, 1f);
        BloodVFX.Spray(transform.position + Vector3.up * .5f, info.direction + Vector3.up, BloodVFX.KeyIchor, 3.5f);
        foreach (Collider c in allColliders) if (c != null) c.enabled = false;
        KeyHorde.UnregisterFlyer(this);
        Kickables.Unregister(this);
        var carcass = gameObject.AddComponent<Corpse>();
        carcass.Init(60f * growth, visual, true);
        Died?.Invoke(this);
        Destroy(gameObject, 45f);
    }

    void AnimateDead()
    {
        float floor = FloorHeight() + .35f;
        if (transform.position.y > floor)
        {
            deadFall += 12f * Time.deltaTime;
            transform.position += Vector3.down * deadFall * Time.deltaTime;
            if (transform.position.y <= floor)
            {
                transform.position = new Vector3(transform.position.x, floor, transform.position.z);
                BloodVFX.Pool(transform.position, BloodVFX.KeyIchor, 2f * growth);
                PlayOneShot(chewClips, 1f);
            }
        }
        if (visual != null) visual.localRotation = Quaternion.Slerp(visual.localRotation, Quaternion.Euler(80f, 0f, 25f), 6f * Time.deltaTime);
        mouthOpen = Mathf.MoveTowards(mouthOpen, .9f, Time.deltaTime);
        ApplyJaw();
    }

    // ================= IKickable =================

    public bool IsKickable => state == State.Stagger || state == State.Stuck;
    bool IKickable.IsAlive => state != State.Dead;
    Transform IKickable.Root => transform;

    public void TakeMelee(DamageInfo info)
    {
        if (state == State.Dead) return;
        info.melee = true;
        DamageOutcome outcome = health.TakeDamage(info);
        if (outcome != DamageOutcome.None) CombatFeedback.Report(info, outcome, health);
    }

    // ================= presentation =================

    void AnimateBody(float dt)
    {
        mouthOpen = Mathf.MoveTowards(mouthOpen, mouthTarget, dt * (mouthTarget > mouthOpen ? 6f : 4f));
        ApplyJaw();
        if (visual == null) return;
        Vector3 localVel = transform.InverseTransformDirection(velocity);
        float swingX = Mathf.Clamp(localVel.z * 4f, -30f, 30f) + Mathf.Sin((Time.time + seed) * 1.3f) * 4f;
        float swingZ = Mathf.Clamp(-localVel.x * 4f, -30f, 30f) + Mathf.Sin((Time.time + seed) * 1.7f) * 5f;
        switch (state)
        {
            case State.Stagger: swingX = 25f + Mathf.Sin(Time.time * 3f) * 5f; swingZ = Mathf.Sin(Time.time * 2f) * 15f; break;
            case State.Bite: case State.BiteWindUp: case State.EatWindUp: case State.Eating: swingX = -18f; break;
            case State.Pin: swingX = pinner != null && pinner.StabReady ? -40f : -18f + Mathf.Sin(Time.time * 21f) * 6f; break;
            case State.Feed: swingX = 55f + Mathf.Sin(Time.time * 9f) * 8f; break;
            case State.DiveAim: swingX = Mathf.Sin(Time.time * 30f) * 3f; swingZ = Mathf.Sin(Time.time * 27f) * 3f; break; // dead straight, quivering
            case State.DiveFall: case State.Stuck: swingX = 0f; swingZ = state == State.Stuck ? Mathf.Sin(Time.time * 18f) * 4f : 0f; break;
            case State.Scream: swingX = -25f + Mathf.Sin(Time.time * 40f) * 4f; break;
            case State.WhipWindUp: swingZ = -whipStartAngle * .15f; break;
            case State.Whip: swingZ = whipStartAngle * .25f; break;
        }
        visual.localRotation = Quaternion.Slerp(visual.localRotation, Quaternion.Euler(swingX, 0f, swingZ), 1f - Mathf.Exp(-8f * dt));
        visual.localScale = baseScale * growth;
    }

    void ApplyJaw()
    {
        if (jawUpper != null) jawUpper.localRotation = Quaternion.Euler(jawOpenDegrees.x * mouthOpen, 0f, 0f);
        if (jawLower != null) jawLower.localRotation = Quaternion.Euler(jawOpenDegrees.y * mouthOpen, 0f, 0f);
        bool open = mouthOpen > .08f && visible;
        if (mouthInterior != null && mouthInterior.activeSelf != open) mouthInterior.SetActive(open);
        if (fangs != null) foreach (GameObject f in fangs) if (f != null && f.activeSelf != open) f.SetActive(open);
        if (throatHitbox != null) throatHitbox.enabled = mouthOpen > .35f && visible && state != State.Dead;
        if (throatLight != null) throatLight.intensity = mouthOpen * 3.5f * (visible ? 1f : 0f);
    }

    void UpdateGlow()
    {
        if (glowRenderers == null) return;
        bool telling = state == State.BiteWindUp || state == State.WhipWindUp || state == State.SpearWindUp || state == State.SpitWindUp
                       || state == State.DiveAim || state == State.EatWindUp || state == State.Scream;
        // On top of you it just smoulders (white-hot would blind you at this range); it flashes when the stab opens.
        bool exposed = state == State.Stagger || state == State.Stuck || (state == State.Pin && pinner != null && pinner.StabReady);
        float intensity = telling ? 7f + Mathf.Sin(Time.time * 50f) * 2f
                        : exposed ? (Mathf.Repeat(Time.time * 6f, 1f) < .5f ? 5f : .8f)
                        : 3f;
        Color c = telling ? Color.Lerp(glowColor, Color.white, .6f) : glowColor;
        foreach (Renderer r in glowRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, c * intensity);
            r.SetPropertyBlock(block);
        }
    }

    void ChantClack()
    {
        if (Time.time < nextChant || !visible) return;
        nextChant = Time.time + UnityEngine.Random.Range(.35f, .6f);
        PlayOneShot(clackClips, .5f);
    }

    // ================= movement helpers =================

    void Steer(Vector3 point, float speed, float dt)
    {
        Vector3 wanted = point - transform.position;
        Vector3 desiredVelocity = wanted.sqrMagnitude > .04f ? wanted.normalized * Mathf.Min(speed, wanted.magnitude * 3f) : Vector3.zero;
        velocity = Vector3.MoveTowards(velocity, desiredVelocity, acceleration * dt);
        if (velocity.sqrMagnitude > .01f &&
            Physics.SphereCast(transform.position, .6f * growth, velocity.normalized, out RaycastHit hit, velocity.magnitude * dt + .8f, worldMask, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<KeyTarget>() == null)
            velocity = Vector3.ProjectOnPlane(velocity, hit.normal) + hit.normal * 2f;
        transform.position += velocity * dt;
    }

    Vector3 KeepInside(Vector3 centre, Vector3 point)
    {
        Vector3 to = point - centre;
        if (Physics.Raycast(centre, to.normalized, out RaycastHit hit, to.magnitude + 1.2f, worldMask, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<KeyFlyer>() == null && hit.collider.GetComponentInParent<KeyTarget>() == null)
            return centre + to.normalized * Mathf.Max(1.5f, hit.distance - 1.4f);
        return point;
    }

    bool HasLineTo(Vector3 point)
    {
        Vector3 from = MouthPosition;
        Vector3 to = point - from;
        if (!Physics.Raycast(from, to.normalized, out RaycastHit hit, to.magnitude, worldMask, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.GetComponentInParent<KeyTarget>() != null || hit.collider.GetComponentInParent<KeyFlyer>() != null;
    }

    float FloorHeight() =>
        Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 20f, worldMask, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;

    Vector3 FloorPoint(Vector3 near) =>
        Physics.Raycast(near + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 10f, worldMask, QueryTriggerInteraction.Ignore)
        && hit.collider.GetComponentInParent<KeyTarget>() == null ? hit.point : new Vector3(near.x, 0f, near.z);

    float CeilingHeight(Vector3 at) =>
        Physics.Raycast(at + Vector3.up * 1f, Vector3.up, out RaycastHit hit, 30f, worldMask, QueryTriggerInteraction.Ignore) ? hit.point.y : at.y + 6f;

    /// <summary>Launch velocity for an arcing glob to land on <paramref name="to"/> (fixed flight time).</summary>
    static Vector3 BallisticVelocity(Vector3 from, Vector3 to)
    {
        float t = Mathf.Clamp(Vector3.Distance(from, to) / 14f, .45f, 1.1f);
        return (to - from) / t - .5f * Physics.gravity * t;
    }

    void Face(Vector3 direction, float dt, float degreesPerSecond)
    {
        Vector3 flat = Flat(direction);
        if (flat.sqrMagnitude < .001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), degreesPerSecond * dt);
    }

    void Enter(State next)
    {
        if (next != State.Hunt) rushing = false;
        if (next != State.BiteWindUp && next != State.Bite) rushBite = false;
        state = next;
        stateTime = 0f;
        switch (next)
        {
            case State.SpearWindUp: case State.SpitWindUp: PlayOneShot(gurgleClips, 1f); windUpDamage = 0f; CallPack(); break;
            case State.BiteWindUp: case State.EatWindUp: case State.WhipWindUp: PlayOneShot(screamClips, 1f); windUpDamage = 0f; CallPack(); break;
            case State.DiveRise: CallPack(); break;
            case State.Hunt: nextAttack = Time.time + UnityEngine.Random.Range(attackCooldown.x, attackCooldown.y); break;
        }
    }

    void PlayOneShot(AudioClip[] clips, float volume)
    {
        if (sfx == null || clips == null || clips.Length == 0) return;
        sfx.pitch = basePitch * UnityEngine.Random.Range(.9f, 1.1f) / Mathf.Sqrt(growth) * 1.4f;
        sfx.PlayOneShot(clips[UnityEngine.Random.Range(0, clips.Length)], volume);
    }

    static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(.0001f, ab.sqrMagnitude));
        return a + ab * t;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
