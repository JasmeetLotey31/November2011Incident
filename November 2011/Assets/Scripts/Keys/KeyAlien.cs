using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A key: a tall, hunched alien shaped like a living key, chanting "keys, keys, keys".
/// Smart on its own and as a pack (see <see cref="KeyHorde"/>):
/// - SENSES: sees in a cone (much further when your flashlight is on), always senses you up close,
///   hears gunshots and reloads, and remembers where you were for a while.
/// - CALLS THE PACK: when it spots you, every key nearby learns where you are.
/// - STALKS: circles at a distance in its own slot around you (flanks, gets behind you), facing you,
///   and sidesteps when you line up a shot on it.
/// - RUSHES: only when the pack gives it a turn, or when you're reloading/empty. Zig-zags in,
///   crouches and barks (the tell: shoot now), lunges, then backs off into the dark.
/// - HURT: staggers (longer on a face shot); badly hurt, it may flee into a vent and come back later.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public sealed class KeyAlien : MonoBehaviour, IKickable
{
    public enum State { Emerging, Prowl, Investigate, Stalk, Rush, WindUp, Lunge, Recover, Stagger, Retreat, Hidden, Dead, Grabbing, Flank, Finished }

    [Header("Senses")]
    [Tooltip("Head/eyes: where sight is checked from.")]
    [SerializeField] Transform eyes;
    [SerializeField] float sightRangeDark = 12f;
    [Tooltip("How far it sees you when your flashlight is on.")]
    [SerializeField] float sightRangeFlashlight = 28f;
    [Tooltip("Within this distance it senses you even behind it.")]
    [SerializeField] float closeSenseRange = 4f;
    [SerializeField] float fieldOfView = 140f;
    [SerializeField] float hearingMultiplier = 1f;
    [Tooltip("Seconds it keeps hunting your last known position after losing sight.")]
    [SerializeField] float memorySeconds = 7f;
    [SerializeField] float alertCallRadius = 14f;
    [SerializeField] LayerMask sightMask = ~0;

    [Header("Movement")]
    [SerializeField] float prowlSpeed = 2f;
    [SerializeField] float stalkSpeed = 3.2f;
    [Tooltip("Just under the player's sprint (7): you can outrun one, not forever.")]
    [SerializeField] float rushSpeed = 6.3f;
    [SerializeField] float retreatSpeed = 5.5f;
    [SerializeField] float stalkDistance = 7f;
    [SerializeField] float stalkDistanceJitter = 1.5f;
    [SerializeField] float zigzagWidth = 1.4f;
    [SerializeField] float turnSpeed = 600f;
    [SerializeField] float emergeSeconds = .9f;

    [Header("Attack")]
    [SerializeField] float lungeRange = 3.3f;
    [Tooltip("Crouch + scream before the lunge: the window to shoot it or parry (shove) it.")]
    [SerializeField] float windUpSeconds = .55f;
    [SerializeField] float lungeSpeed = 12f;
    [SerializeField] float lungeSeconds = .3f;
    [SerializeField] float lungeReach = 1.3f;
    [SerializeField] float lungeDamage = 28f;
    [SerializeField] float recoverSeconds = .7f;
    [Tooltip("Gives up a rush that takes this long and goes back to stalking.")]
    [SerializeField] float rushTimeout = 7f;

    [Header("Smarts")]
    [Tooltip("Sidesteps when the player aims within this many degrees of it.")]
    [SerializeField] float dodgeAimDegrees = 5f;
    [SerializeField] float dodgeCooldown = 1.6f;
    [Range(0, 1)] [SerializeField] float dodgeChance = .7f;
    [SerializeField] float dodgeDistance = 2.4f;
    [Range(0, 1)] [SerializeField] float retreatBelowHealth = .3f;
    [Range(0, 1)] [SerializeField] float retreatChance = .55f;
    [SerializeField] float staggerSeconds = .28f;
    [Tooltip("Stalkers creep in this much faster while the player isn't looking at them (1 = off; off feels fairer).")]
    [SerializeField] float unwatchedSpeedMultiplier = 1f;
    [Tooltip("...and close in to this distance instead of the normal stalk distance.")]
    [SerializeField] float unwatchedStalkDistance = 7f;

    [Header("Hit reactions (kickable while staggered)")]
    [Tooltip("Shot in the face: reels back dazed.")]
    [SerializeField] float dazedSeconds = 1.4f;
    [Tooltip("Shot in the leg: drops to a knee.")]
    [SerializeField] float kneelSeconds = 1.6f;
    [Tooltip("Kicked / blasted: knocked flat.")]
    [SerializeField] float knockdownSeconds = 1.1f;
    [Tooltip("Hides behind cover when shot at (off for relentless walkers).")]
    [SerializeField] bool useCover = false;
    [Tooltip("Under this health it limps (slower, lurching).")]
    [Range(0, 1)] [SerializeField] float limpBelowHealth = .5f;
    [Tooltip("Chance that a lunge that connects becomes a grab you must struggle out of.")]
    [Range(0, 1)] [SerializeField] float grabChance = .35f;
    [Tooltip("Chance a hurt key fakes a retreat and comes back at you from behind.")]
    [Range(0, 1)] [SerializeField] float flankChance = .5f;

    [Header("Audio")]
    [Tooltip("Looping chant source (3D).")]
    [SerializeField] AudioSource voice;
    [Tooltip("One-shots: barks, hurt, death, steps (3D).")]
    [SerializeField] AudioSource sfx;
    [SerializeField] AudioClip[] chantClips;
    [SerializeField] AudioClip[] barkClips;
    [SerializeField] AudioClip[] hurtClips;
    [SerializeField] AudioClip deathClip;
    [SerializeField] AudioClip[] stepClips;
    [Tooltip("Pitch range for this key's voice; low = monstrous.")]
    [SerializeField] Vector2 voicePitch = new Vector2(.55f, .72f);

    public State Current => state;
    public bool IsDead => state == State.Dead;
    public bool IsHidden => state == State.Hidden;
    public bool WantsRush => state == State.Rush || state == State.WindUp || state == State.Lunge || state == State.Grabbing;
    public float HealthFraction => health != null ? health.Fraction : 1f;
    /// <summary>Movement left after injuries: 1 healthy, lower when limping.</summary>
    public float Mobility => health.Fraction < limpBelowHealth ? Mathf.Lerp(.6f, 1f, health.Fraction / limpBelowHealth) : 1f;
    public bool IsLimping => health.Fraction < limpBelowHealth && !IsDead;
    public enum StaggerKind { Flinch, Dazed, Kneel, Knockdown }
    /// <summary>Staggered and kickable (the body uses this for the dazed / kneel / knocked-down pose).</summary>
    public bool IsOpen => IsKickable;
    public StaggerKind CurrentStagger => staggerKind;
    /// <summary>Killed by a kick or bash: pays out more loot (Doom's glory-kill rule).</summary>
    public bool KilledByKick { get; private set; }
    /// <summary>Seconds into the current state (for animation).</summary>
    public float StateTime => stateTime;
    public float WindUpSeconds => windUpSeconds;
    public bool CanSeePlayer { get; private set; }
    public bool IsHunting => Time.time - lastSeenTime < memorySeconds;
    /// <summary>0 calm … 1 attacking (eyes glow with it).</summary>
    public float Aggression { get; private set; }
    public float Speed => agent != null && agent.enabled ? agent.velocity.magnitude : 0f;
    public Vector3 LungeDirection => lungeDir;
    public KeyVent HomeVent { get; set; }

    public event Action<KeyAlien> Died;
    /// <summary>Fled into a vent and is now hidden (the director brings it back later).</summary>
    public event Action<KeyAlien> Retreated;

    NavMeshAgent agent;
    Health health;
    KeyAlienBody body;
    State state, stateBeforeStagger;
    float stateTime, stateDuration;
    Vector3 lastKnown, noisePos, lungeDir, dodgeTarget;
    bool hasNoise, lungeHit, retreatedOnce, flankedOnce;
    float lastSeenTime = -999f, nextPerceive, nextDestination, nextTokenTry, nextDodge, dodgeUntil, nextStep;
    float lastThreatTime = -999f, coverUntil, kickableUntil = -1f;
    bool disarmed;
    StaggerKind staggerKind;
    Vector3 coverPoint, knockbackVelocity, flankHidePoint;
    PlayerCloseCombat grabbedPlayer;
    float stalkJitter, seed, basePitch;
    static readonly RaycastHit[] sightHits = new RaycastHit[8];

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        body = GetComponentInChildren<KeyAlienBody>();
        agent.updateRotation = false;
        seed = UnityEngine.Random.value * 100f;
        stalkJitter = UnityEngine.Random.Range(-1f, 1f) * stalkDistanceJitter;
        basePitch = UnityEngine.Random.Range(voicePitch.x, voicePitch.y);
        if (eyes == null) eyes = transform;
    }

    void OnEnable()
    {
        KeyHorde.Register(this);
        Kickables.Register(this);
        Noise.Emitted += OnNoise;
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        StartChant();
    }

    void OnDisable()
    {
        KeyHorde.Unregister(this);
        Kickables.Unregister(this);
        Noise.Emitted -= OnNoise;
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
    }

    /// <summary>Crawl out of a vent at <paramref name="exit"/>.</summary>
    public void Spawn(KeyVent vent, Vector3 exit)
    {
        HomeVent = vent;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        agent.enabled = true;
        agent.Warp(exit);
        Vector3 outward = vent != null ? Flat(exit - vent.transform.position) : Vector3.zero;
        if (outward.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
        Enter(State.Emerging);
        if (body != null) body.PlayEmerge();
    }

    /// <summary>Come back out of a vent after fleeing, partly healed.</summary>
    public void ReturnFromVent(KeyVent vent, Vector3 exit, float healthFraction)
    {
        Spawn(vent, exit);
        health.SetCurrent(health.Max * healthFraction);
        // It remembers roughly where you were.
        KeyTarget target = KeyTarget.Current;
        if (target != null) { lastKnown = target.Position; lastSeenTime = Time.time - memorySeconds * .5f; }
    }

    void Update()
    {
        if (state == State.Dead || state == State.Hidden) return;
        float dt = Time.deltaTime;
        stateTime += dt;
        KeyTarget target = KeyTarget.Current;

        if (Time.time >= nextPerceive)
        {
            nextPerceive = Time.time + .12f;
            Perceive(target);
        }
        bool targetAlive = target != null && !target.IsDead;
        float distance = targetAlive ? Vector3.Distance(Flat(transform.position), Flat(target.Position)) : float.PositiveInfinity;

        switch (state)
        {
            case State.Emerging:
                agent.isStopped = true;
                if (stateTime >= emergeSeconds) Enter(IsHunting ? State.Stalk : State.Prowl);
                break;

            case State.Prowl:
                agent.isStopped = false;
                agent.speed = prowlSpeed;
                if (targetAlive && IsHunting) { Enter(State.Stalk); break; }
                if (hasNoise) { Enter(State.Investigate); break; }
                if (Time.time >= nextDestination || Arrived()) PickWanderPoint(target);
                FaceMovement(dt);
                break;

            case State.Investigate:
                agent.isStopped = false;
                agent.speed = prowlSpeed * 1.3f;
                if (targetAlive && IsHunting) { Enter(State.Stalk); break; }
                if (Time.time >= nextDestination) { SetDestination(noisePos); nextDestination = Time.time + 1f; }
                if (Arrived() || stateTime > 10f)
                {
                    // Look around where the noise was, then give up.
                    agent.isStopped = true;
                    transform.Rotate(0f, Mathf.Sin((Time.time + seed) * 2f) * 120f * dt, 0f);
                    if (stateTime > 12f) { hasNoise = false; Enter(State.Prowl); }
                }
                else FaceMovement(dt);
                break;

            case State.Stalk:
                if (!targetAlive || !IsHunting)
                {
                    // Lost you: search your last known spot.
                    noisePos = lastKnown;
                    hasNoise = true;
                    Enter(State.Investigate);
                    break;
                }
                agent.isStopped = false;
                {
                    // Creeps in fast while you're not looking at it; slows, twitching, when you are.
                    bool watched = target.IsAimingAt(transform.position + Vector3.up, 45f);
                    float creep = watched ? .55f : unwatchedSpeedMultiplier;
                    agent.speed = (Time.time < dodgeUntil ? 9f : stalkSpeed * creep) * Mobility;
                    if (Time.time < dodgeUntil) SetDestination(dodgeTarget);
                    else if (Time.time >= nextDestination)
                    {
                        // Shot at or aimed at recently: hide behind cover between you and it.
                        bool threatened = Time.time - lastThreatTime < 4f;
                        threatened &= useCover;
                        if (threatened && Time.time >= coverUntil) coverPoint = FindCover(target);
                        if (threatened && coverPoint != Vector3.zero) SetDestination(coverPoint);
                        else
                        {
                            float angle = KeyHorde.StalkAngle(this) * Mathf.Deg2Rad;
                            float radius = (watched ? stalkDistance : unwatchedStalkDistance) + stalkJitter;
                            Vector3 ring = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                            Vector3 anchor = CanSeePlayer ? target.Position : lastKnown;
                            SetDestination(anchor + ring);
                        }
                        nextDestination = Time.time + .4f;
                    }
                    if (target.IsAimingAt(eyes.position, 12f)) lastThreatTime = Time.time;
                }
                FaceTowards(CanSeePlayer ? target.Position : agent.steeringTarget, dt);
                TryDodge(target, distance);
                // Ask the pack for a turn to attack; up close it always tries.
                if (Time.time >= nextTokenTry && (CanSeePlayer || distance < closeSenseRange))
                {
                    nextTokenTry = Time.time + (target.IsVulnerable ? .1f : .5f);
                    if (KeyHorde.TryTakeRushToken(this)) Enter(State.Rush);
                }
                break;

            case State.Rush:
                if (!targetAlive) { Enter(State.Prowl); break; }
                agent.isStopped = false;
                agent.speed = rushSpeed * Mobility;
                if (Time.time >= nextDestination)
                {
                    Vector3 to = Flat(target.Position - transform.position);
                    Vector3 side = Vector3.Cross(Vector3.up, to.normalized);
                    float zig = distance > 5f ? Mathf.Sin((Time.time + seed) * 3.2f) * zigzagWidth : 0f;
                    SetDestination(target.Position + side * zig);
                    nextDestination = Time.time + .1f;
                }
                FaceTowards(target.Position, dt);
                if (distance <= lungeRange && CanSeePlayer) Enter(State.WindUp);
                else if (stateTime > rushTimeout) { KeyHorde.ReleaseRushToken(this); Enter(State.Stalk); }
                break;

            case State.WindUp:
                agent.isStopped = true;
                if (targetAlive) FaceTowards(target.Position, dt);
                if (stateTime >= windUpSeconds)
                {
                    // Lead the target a little so strafing alone doesn't save you.
                    Vector3 aimAt = targetAlive ? target.Position + target.Velocity * .2f : transform.position + transform.forward;
                    lungeDir = Flat(aimAt - transform.position).normalized;
                    if (lungeDir.sqrMagnitude < .01f) lungeDir = transform.forward;
                    lungeHit = false;
                    Enter(State.Lunge);
                }
                break;

            case State.Lunge:
                agent.isStopped = true;
                // Stop in your face rather than driving through you.
                if (agent.isOnNavMesh && distance > .9f) agent.Move(lungeDir * (lungeSpeed * dt));
                if (!lungeHit && targetAlive && distance <= lungeReach)
                {
                    lungeHit = true;
                    // Sometimes it doesn't just slash: it grabs on and you have to fight it off.
                    PlayerCloseCombat close = target.GetComponent<PlayerCloseCombat>();
                    if (close != null && !disarmed && UnityEngine.Random.value < grabChance && close.BeginGrab(this))
                    {
                        grabbedPlayer = close;
                        Enter(State.Grabbing);
                        break;
                    }
                    target.Health.TakeDamage(new DamageInfo
                    {
                        amount = lungeDamage,
                        point = target.EyePosition - Vector3.up * .9f,
                        direction = lungeDir,
                        source = gameObject,
                    });
                }
                if (stateTime >= lungeSeconds) Enter(State.Recover);
                break;

            case State.Grabbing:
                agent.isStopped = true;
                // Clamped onto you, face in your face. PlayerCloseCombat decides how it ends.
                if (targetAlive && agent.isOnNavMesh)
                {
                    Vector3 hold = target.Position + Flat(target.View.transform.forward).normalized * .85f;
                    agent.Move((Flat(hold - transform.position)) * Mathf.Min(1f, 12f * dt));
                    FaceTowards(target.Position, dt);
                }
                if (!targetAlive || grabbedPlayer == null || !grabbedPlayer.IsGrabbed) Enter(State.Recover);
                break;

            case State.Finished:
                agent.isStopped = true;
                break;

            case State.Flank:
                // Fake retreat: slip out of sight, wait, then come at you from behind.
                agent.isStopped = false;
                if (!targetAlive) { Enter(State.Prowl); break; }
                if (stateTime < 3f)
                {
                    agent.speed = retreatSpeed * Mobility;
                    if (Time.time >= nextDestination) { SetDestination(flankHidePoint); nextDestination = Time.time + .5f; }
                    FaceMovement(dt);
                }
                else
                {
                    agent.speed = stalkSpeed * 1.4f * Mobility;
                    Vector3 behind = target.Position - Flat(target.View.transform.forward).normalized * 3.5f;
                    if (Time.time >= nextDestination) { SetDestination(behind); nextDestination = Time.time + .25f; }
                    FaceMovement(dt);
                    if (distance < 5f && !target.IsAimingAt(transform.position + Vector3.up, 60f) && KeyHorde.TryTakeRushToken(this))
                        Enter(State.Rush);
                    else if (stateTime > 12f) Enter(State.Stalk);
                }
                break;

            case State.Recover:
                agent.isStopped = true;
                // Back off into the dark, then come again.
                if (stateTime < .45f && agent.isOnNavMesh) agent.Move(-lungeDir * (3f * dt));
                if (targetAlive) FaceTowards(target.Position, dt);
                if (stateTime >= recoverSeconds)
                {
                    KeyHorde.ReleaseRushToken(this);
                    Enter(State.Stalk);
                }
                break;

            case State.Stagger:
                agent.isStopped = true;
                // Knocked back (by a shove or a heavy hit), sliding to a stop.
                if (knockbackVelocity.sqrMagnitude > .01f && agent.isOnNavMesh)
                {
                    agent.Move(knockbackVelocity * dt);
                    knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, 18f * dt);
                }
                if (stateTime >= stateDuration)
                {
                    knockbackVelocity = Vector3.zero;
                    if (!retreatedOnce && health.Fraction < retreatBelowHealth && UnityEngine.Random.value < retreatChance)
                    {
                        retreatedOnce = true;
                        KeyHorde.ReleaseRushToken(this);
                        Enter(State.Retreat);
                    }
                    else if (!flankedOnce && health.Fraction < .65f && targetAlive && UnityEngine.Random.value < flankChance)
                    {
                        flankedOnce = true;
                        KeyHorde.ReleaseRushToken(this);
                        flankHidePoint = FindCover(target);
                        if (flankHidePoint == Vector3.zero) flankHidePoint = FleePoint(target);
                        Enter(State.Flank);
                    }
                    else Enter(stateBeforeStagger == State.Rush ? State.Rush : State.Stalk);
                }
                break;

            case State.Retreat:
                agent.isStopped = false;
                agent.speed = retreatSpeed * Mobility;
                if (Time.time >= nextDestination)
                {
                    SetDestination(HomeVent != null ? HomeVent.ExitPosition : FleePoint(target));
                    nextDestination = Time.time + .5f;
                }
                FaceMovement(dt);
                if ((Arrived() && stateTime > .5f) || stateTime > 8f) Hide();
                break;
        }

        UpdateAggression(dt);
        UpdateAudio();
    }

    // ---------------- senses ----------------

    void Perceive(KeyTarget target)
    {
        CanSeePlayer = false;
        if (target == null || target.IsDead) return;

        Vector3 eye = eyes.position;
        Vector3 to = target.EyePosition - eye;
        float distance = to.magnitude;
        float range = target.FlashlightOn ? sightRangeFlashlight : sightRangeDark;
        bool close = distance <= closeSenseRange;
        if (!close && (distance > range || Vector3.Angle(transform.forward, to) > fieldOfView * .5f)) return;
        if (!HasLineOfSight(eye, to, distance, target.transform)) return;

        bool wasHunting = IsHunting;
        CanSeePlayer = true;
        lastKnown = target.Position;
        lastSeenTime = Time.time;
        if (!wasHunting)
        {
            // Found you: call the others and screech.
            KeyHorde.Alert(target.Position, transform.position, alertCallRadius);
            PlayOneShot(barkClips, .8f);
        }
    }

    bool HasLineOfSight(Vector3 eye, Vector3 to, float distance, Transform target)
    {
        int count = Physics.RaycastNonAlloc(eye, to / distance, sightHits, distance, sightMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Transform blocker = null;
        for (int i = 0; i < count; i++)
        {
            Transform t = sightHits[i].collider.transform;
            if (t.IsChildOf(transform)) continue;                 // own body
            if (t.GetComponentInParent<KeyAlien>() != null) continue; // other keys don't block
            if (sightHits[i].distance < nearest) { nearest = sightHits[i].distance; blocker = t; }
        }
        return blocker == null || blocker.IsChildOf(target);
    }

    /// <summary>Another key saw the player and called the pack.</summary>
    public void HearAlert(Vector3 playerPosition)
    {
        if (state == State.Dead || state == State.Hidden || state == State.Retreat) return;
        lastKnown = playerPosition;
        lastSeenTime = Mathf.Max(lastSeenTime, Time.time - memorySeconds * .4f);
        if (state == State.Prowl || state == State.Investigate) Enter(State.Stalk);
    }

    void OnNoise(NoiseEvent e)
    {
        if (state == State.Dead || state == State.Hidden || state == State.Retreat) return;
        if (e.source != null && e.source.GetComponentInParent<KeyAlien>() != null) return; // own kind
        float distance = Vector3.Distance(transform.position, e.position);
        if (distance > e.radius * hearingMultiplier) return;

        bool fromPlayer = e.source != null && e.source.GetComponentInParent<KeyTarget>() != null;
        if (fromPlayer && distance < e.radius * hearingMultiplier * .6f)
        {
            // Close enough to pinpoint it: it knows where you are.
            lastKnown = e.position;
            lastSeenTime = Mathf.Max(lastSeenTime, Time.time - memorySeconds * .3f);
            if (state == State.Prowl || state == State.Investigate) Enter(State.Stalk);
        }
        else if (!IsHunting)
        {
            noisePos = e.position;
            hasNoise = true;
            if (state == State.Prowl) Enter(State.Investigate);
        }
    }

    // ---------------- damage ----------------

    void OnDamaged(DamageInfo info)
    {
        if (state == State.Dead) return;
        // It knows where the shot came from.
        KeyTarget target = KeyTarget.Current;
        if (target != null) { lastKnown = target.Position; lastSeenTime = Time.time; }
        lastThreatTime = Time.time;
        PlayOneShot(hurtClips, 1f);
        if (body != null) body.Flinch(info.direction, info.part == BodyPart.Head);
        if (state == State.Emerging) return;
        if (info.part == BodyPart.Arm) disarmed = true; // claws shot off: it can't grab any more

        // Resident Evil-style: where you hit it decides what it does. Head and leg hits (and big
        // knockback from kicks and shotgun blasts) interrupt anything, even a lunge or a grab.
        float push = info.knockback > 0f ? info.knockback : (info.part == BodyPart.Head ? 3f : 1.8f);
        if (info.knockback > 0f) { Stun(knockdownSeconds, info.direction, info.knockback, StaggerKind.Knockdown); return; }
        if (info.part == BodyPart.Head) { Stun(dazedSeconds, info.direction, push, StaggerKind.Dazed); return; }
        if (info.part == BodyPart.Leg) { Stun(kneelSeconds, info.direction, push * .4f, StaggerKind.Kneel); return; }

        if (IsKickable) return; // already staggered: stays down
        if (state == State.Lunge || state == State.Grabbing) return; // committed
        if (state != State.Stagger) stateBeforeStagger = state;
        stateDuration = staggerSeconds;
        staggerKind = StaggerKind.Flinch;
        knockbackVelocity = Flat(info.direction).normalized * push;
        Enter(State.Stagger);
    }

    public void ApplyDifficulty(Difficulty.Preset p)
    {
        lungeDamage = p.lungeDamage;
        windUpSeconds = p.windUpSeconds;
        grabChance = p.grabChance;
    }

    /// <summary>Staggered for <paramref name="seconds"/> and kickable: dazed, kneeling or knocked down.</summary>
    void Stun(float seconds, Vector3 direction, float push, StaggerKind kind)
    {
        if (state == State.Dead || state == State.Hidden) return;
        if (grabbedPlayer != null) { grabbedPlayer.ForceEndGrab(); grabbedPlayer = null; }
        KeyHorde.ReleaseRushToken(this);
        stateBeforeStagger = State.Stalk;
        stateDuration = seconds;
        kickableUntil = Time.time + seconds;
        staggerKind = kind;
        knockbackVelocity = Flat(direction).normalized * push;
        if (state != State.Stagger) Enter(State.Stagger);
        else stateTime = 0f;
    }

    // ---------------- IKickable ----------------

    public bool IsKickable => state == State.Stagger && Time.time < kickableUntil;
    bool IKickable.IsAlive => !IsDead && !IsHidden;
    Transform IKickable.Root => transform;

    /// <summary>Kicked or bashed by the player.</summary>
    public void TakeMelee(DamageInfo info)
    {
        if (IsDead || IsHidden) return;
        info.part = BodyPart.Body;
        info.melee = true;
        DamageOutcome outcome = health.TakeDamage(info);
        if (outcome != DamageOutcome.None) CombatFeedback.Report(info, outcome, health);
    }

    /// <summary>Shoved / blasted back: interrupts any attack and knocks it down.</summary>
    public void Knockback(Vector3 direction, float speed, float staggerTime) =>
        Stun(staggerTime, direction, speed, StaggerKind.Knockdown);

    /// <summary>The player's struggle ended: thrown off (knocked down, kickable) or it got its bite in.</summary>
    public void ReleaseGrab(bool brokeFree, Vector3 throwDirection)
    {
        grabbedPlayer = null;
        if (state != State.Grabbing) return;
        if (brokeFree) Stun(knockdownSeconds + .5f, throwDirection, 6f, StaggerKind.Knockdown);
        else Enter(State.Recover);
    }

    /// <summary>
    /// A nearby spot the player can't see from where they stand (behind a pillar or crate),
    /// roughly at stalking range. Zero if there's none.
    /// </summary>
    Vector3 FindCover(KeyTarget target)
    {
        coverUntil = Time.time + 2f;
        if (target == null) return Vector3.zero;
        Vector3 eye = target.EyePosition;
        Vector3 best = Vector3.zero;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < 10; i++)
        {
            float angle = (KeyHorde.StalkAngle(this) + UnityEngine.Random.Range(-80f, 80f)) * Mathf.Deg2Rad;
            Vector3 candidate = target.Position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * UnityEngine.Random.Range(4f, 9f);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
            Vector3 head = hit.position + Vector3.up * 1.6f;
            // Hidden if something solid (not a key, not the player) blocks the line to the player's eyes.
            if (!Physics.Linecast(head, eye, out RaycastHit block, sightMask, QueryTriggerInteraction.Ignore)) continue;
            if (block.transform.IsChildOf(target.transform) || block.transform.GetComponentInParent<KeyAlien>() != null) continue;
            float score = Vector3.Distance(hit.position, transform.position);
            if (score < bestScore) { bestScore = score; best = hit.position; }
        }
        return best;
    }

    void OnDied(DamageInfo info)
    {
        if (grabbedPlayer != null) { grabbedPlayer.ForceEndGrab(); grabbedPlayer = null; }
        KeyHorde.ReleaseRushToken(this);
        Enter(State.Dead);
        if (agent.enabled) { agent.isStopped = true; agent.enabled = false; }
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (voice != null) voice.Stop();
        if (sfx != null && deathClip != null) { sfx.pitch = basePitch * 1.2f; sfx.PlayOneShot(deathClip); }
        if (body != null) body.Die(info.direction);
        KilledByKick = info.melee;
        KeyHorde.Unregister(this);
        Kickables.Unregister(this);
        Died?.Invoke(this);
        Destroy(gameObject, 8f);
    }

    // ---------------- helpers ----------------

    void Enter(State next)
    {
        state = next;
        stateTime = 0f;
        nextDestination = 0f;
        if (next == State.WindUp) PlayOneShot(barkClips, 1f);
    }

    void Hide()
    {
        Enter(State.Hidden);
        if (agent.enabled) agent.isStopped = true;
        if (voice != null) voice.Stop();
        Retreated?.Invoke(this);
        gameObject.SetActive(false);
    }

    void TryDodge(KeyTarget target, float distance)
    {
        if (!CanSeePlayer || Time.time < nextDodge || distance > 25f) return;
        if (!target.IsAimingAt(eyes.position - Vector3.up * .4f, dodgeAimDegrees)) return;
        nextDodge = Time.time + dodgeCooldown;
        if (UnityEngine.Random.value > dodgeChance) return;

        Vector3 to = Flat(target.Position - transform.position).normalized;
        float sideSign = UnityEngine.Random.value < .5f ? -1f : 1f;
        Vector3 side = Vector3.Cross(Vector3.up, to) * sideSign;
        if (NavMesh.SamplePosition(transform.position + side * dodgeDistance, out NavMeshHit hit, 1f, NavMesh.AllAreas))
        {
            dodgeTarget = hit.position;
            dodgeUntil = Time.time + .35f;
            if (body != null) body.Dodge(sideSign);
        }
    }

    void PickWanderPoint(KeyTarget target)
    {
        nextDestination = Time.time + UnityEngine.Random.Range(3f, 6f);
        // Drift toward where the player is, loosely, so the station feels hunted.
        Vector3 centre = target != null && UnityEngine.Random.value < .4f ? target.Position : transform.position;
        Vector3 random = centre + new Vector3(UnityEngine.Random.Range(-9f, 9f), 0f, UnityEngine.Random.Range(-9f, 9f));
        SetDestination(random);
    }

    Vector3 FleePoint(KeyTarget target)
    {
        Vector3 away = target != null ? Flat(transform.position - target.Position).normalized : transform.forward;
        return transform.position + away * 10f;
    }

    void SetDestination(Vector3 point)
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, 3f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
    }

    bool Arrived() => agent.enabled && !agent.pathPending && agent.remainingDistance <= Mathf.Max(.5f, agent.stoppingDistance);

    void FaceTowards(Vector3 point, float dt)
    {
        Vector3 dir = Flat(point - transform.position);
        if (dir.sqrMagnitude < .01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * dt);
    }

    void FaceMovement(float dt)
    {
        Vector3 v = Flat(agent.velocity);
        if (v.sqrMagnitude > .05f) FaceTowards(transform.position + v, dt);
    }

    void UpdateAggression(float dt)
    {
        float wanted;
        switch (state)
        {
            case State.Rush: wanted = .9f; break;
            case State.WindUp:
            case State.Lunge:
            case State.Grabbing: wanted = 1f; break;
            case State.Flank: wanted = .4f; break;
            case State.Recover: wanted = .7f; break;
            case State.Stalk: wanted = .55f; break;
            case State.Investigate: wanted = .3f; break;
            case State.Stagger: wanted = Aggression; break;
            default: wanted = .12f; break;
        }
        Aggression = Mathf.MoveTowards(Aggression, wanted, dt * 3f);
    }

    void StartChant()
    {
        if (voice == null || chantClips == null || chantClips.Length == 0) return;
        voice.clip = chantClips[UnityEngine.Random.Range(0, chantClips.Length)];
        voice.loop = true;
        voice.pitch = basePitch;
        voice.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, voice.clip.length - .1f));
        voice.Play();
    }

    void UpdateAudio()
    {
        if (voice != null && voice.isPlaying)
        {
            // Whisper when calm, chant louder and faster as it closes in. A pack = a chorus.
            voice.volume = Mathf.Lerp(.35f, 1f, Aggression);
            voice.pitch = basePitch * Mathf.Lerp(.92f, 1.12f, Aggression);
        }
        float speed = Speed;
        if (speed > .6f && Time.time >= nextStep && stepClips != null && stepClips.Length > 0 && sfx != null)
        {
            nextStep = Time.time + Mathf.Lerp(.32f, .13f, speed / rushSpeed); // skittering
            sfx.pitch = UnityEngine.Random.Range(.8f, 1.1f);
            sfx.PlayOneShot(stepClips[UnityEngine.Random.Range(0, stepClips.Length)], .35f);
        }
    }

    void PlayOneShot(AudioClip[] clips, float volume)
    {
        if (sfx == null || clips == null || clips.Length == 0) return;
        sfx.pitch = basePitch * UnityEngine.Random.Range(.95f, 1.08f);
        sfx.PlayOneShot(clips[UnityEngine.Random.Range(0, clips.Length)], volume);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
