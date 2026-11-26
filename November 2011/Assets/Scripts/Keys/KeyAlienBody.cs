using UnityEngine;

/// <summary>
/// Procedural animation that makes a key feel alive and wrong (no animation clips needed):
/// - skittering bob and sway when it moves, hunched lean that deepens as it rushes;
/// - sudden head twitches, more often when it's stalking you;
/// - the attack tell: crouches low and raises its blade arms, then stretches into the lunge;
/// - flinches when shot (harder on a face shot), crawls out of vents, collapses and sinks on death;
/// - eyes glow brighter the more aggressive it is.
/// Put it on the "Visual" pivot (at the key's feet) under the KeyAlien root.
/// </summary>
[DisallowMultipleComponent]
public sealed class KeyAlienBody : MonoBehaviour
{
    [SerializeField] KeyAlien brain;
    [SerializeField] Transform head;
    [SerializeField] Transform armLeft;
    [SerializeField] Transform armRight;
    [Tooltip("Glowing eye/mouth renderers (material must have Emission enabled).")]
    [SerializeField] Renderer[] glowRenderers;
    [SerializeField] Color glowColor = new Color(1f, .12f, .05f);
    [SerializeField] float glowCalm = 1.5f;
    [SerializeField] float glowRage = 7f;

    [Header("Motion")]
    [SerializeField] float skitterRate = 3.6f;
    [SerializeField] float bobHeight = .06f;
    [SerializeField] float twitchDegrees = 28f;

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    Vector3 restPosition, restScale;
    Quaternion restRotation, headRest, armLRest, armRRest;
    MaterialPropertyBlock block;
    float phase, lean, crouch, stretch, flinch, flinchVelocity, dodgeLean;
    Quaternion twitch = Quaternion.identity, twitchTarget = Quaternion.identity;
    float nextTwitch, emergeStart = -10f, deathStart = -1f, deathSide = 1f, glow, finisherStart = -1f;

    void Awake()
    {
        if (brain == null) brain = GetComponentInParent<KeyAlien>();
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        restScale = transform.localScale;
        if (head != null) headRest = head.localRotation;
        if (armLeft != null) armLRest = armLeft.localRotation;
        if (armRight != null) armRRest = armRight.localRotation;
        block = new MaterialPropertyBlock();
    }

    public void PlayEmerge() => emergeStart = Time.time;

    public void Flinch(Vector3 direction, bool weakSpot)
    {
        // Knocked back from the shot; a face shot snaps the head too.
        flinchVelocity -= weakSpot ? 420f : 260f;
        if (head != null && weakSpot) twitch = Quaternion.Euler(-35f, Random.Range(-30f, 30f), Random.Range(-25f, 25f));
    }

    public void Dodge(float side) => dodgeLean = 22f * side;

    public void Die(Vector3 direction)
    {
        deathStart = Time.time;
        deathSide = Vector3.Dot(direction, transform.right) >= 0f ? 1f : -1f;
    }

    void LateUpdate()
    {
        if (brain == null) return;
        float dt = Time.deltaTime;
        KeyAlien.State state = brain.Current;

        if (deathStart >= 0f) { AnimateDeath(); UpdateGlow(0f, dt); return; }

        float speed = brain.Speed;
        phase += speed * skitterRate * dt;
        float moving = Mathf.Clamp01(speed / 2f);

        // Hunch / crouch / stretch targets per state.
        float targetLean = 6f, targetCrouch = 0f, targetStretch = 0f, armRaise = 0f;
        switch (state)
        {
            case KeyAlien.State.Stalk: targetLean = 14f; armRaise = .2f; break;
            case KeyAlien.State.Rush: targetLean = 28f; armRaise = .35f; break;
            case KeyAlien.State.WindUp: targetLean = 32f; targetCrouch = .24f; armRaise = 1f; break;
            case KeyAlien.State.Lunge: targetLean = 48f; targetStretch = .18f; armRaise = -.6f; break;
            case KeyAlien.State.Recover: targetLean = -6f; break;
            case KeyAlien.State.Retreat: targetLean = 35f; targetCrouch = .15f; break;
            case KeyAlien.State.Flank: targetLean = 30f; targetCrouch = .2f; break;
            case KeyAlien.State.Grabbing:
                // Clamped on, thrashing.
                targetLean = 22f + Mathf.Sin(Time.time * 23f) * 6f;
                armRaise = .75f + Mathf.Sin(Time.time * 17f) * .15f;
                break;
        }
        if (brain.IsLimping) targetLean += 10f * (1f - brain.HealthFraction); // sags as it weakens
        if (brain.IsOpen)
        {
            switch (brain.CurrentStagger)
            {
                case KeyAlien.StaggerKind.Dazed:
                    // Reeling back from a face shot, arms flailing.
                    targetLean = -22f + Mathf.Sin(Time.time * 9f) * 6f;
                    armRaise = .5f + Mathf.Sin(Time.time * 13f) * .3f;
                    break;
                case KeyAlien.StaggerKind.Kneel:
                    // Leg shot out: drops to a knee, hunched.
                    targetLean = 24f; targetCrouch = .38f; armRaise = -.1f;
                    break;
                case KeyAlien.StaggerKind.Knockdown:
                    // Kicked flat on its back.
                    targetLean = -70f; targetCrouch = .3f; armRaise = .3f;
                    break;
            }
        }
        float rate = state == KeyAlien.State.Lunge || state == KeyAlien.State.WindUp ? 18f : 7f;
        lean = Mathf.Lerp(lean, targetLean, 1f - Mathf.Exp(-rate * dt));
        crouch = Mathf.Lerp(crouch, targetCrouch, 1f - Mathf.Exp(-rate * dt));
        stretch = Mathf.Lerp(stretch, targetStretch, 1f - Mathf.Exp(-rate * dt));
        dodgeLean = Mathf.Lerp(dodgeLean, 0f, 1f - Mathf.Exp(-6f * dt));

        // Flinch spring.
        float step = Mathf.Min(dt, 1f / 30f);
        flinchVelocity += (-180f * flinch - 14f * flinchVelocity) * step;
        flinch += flinchVelocity * step;

        // Emerging from a vent: low crawl, unfolding.
        float emerge = Mathf.Clamp01((Time.time - emergeStart) / .9f);
        float crawl = 1f - emerge * emerge * (3f - 2f * emerge);

        float bob = Mathf.Abs(Mathf.Sin(phase * Mathf.PI)) * bobHeight * moving;
        float roll = Mathf.Sin(phase * Mathf.PI) * 6f * moving + dodgeLean;
        if (brain.IsLimping)
        {
            // Badly hurt: lurches to one side every other step and sags.
            float limpStep = Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * .5f));
            roll += limpStep * 14f * moving + 8f;
            bob *= .5f;
        }

        transform.localPosition = restPosition + Vector3.up * bob;
        transform.localRotation = restRotation * Quaternion.Euler(lean + flinch + crawl * 55f, 0f, roll);
        transform.localScale = Vector3.Scale(restScale, new Vector3(1f, 1f - crouch - crawl * .5f, 1f + stretch));

        AnimateHead(state, dt);
        AnimateArms(armRaise, moving);
        UpdateGlow(brain.Aggression, dt);
    }

    public void PlayFinisher() => finisherStart = Time.time;

    void AnimateHead(KeyAlien.State state, float dt)
    {
        if (head == null || !head.IsChildOf(transform)) return; // popped off: physics owns it now
        if (state == KeyAlien.State.Finished && finisherStart >= 0f)
        {
            // Twisted like a key in a lock: jerky quarter turns, then a final crank.
            float t = Mathf.Clamp01((Time.time - finisherStart) / .45f);
            float turns = Mathf.Floor(t * 3f) / 3f + Mathf.SmoothStep(0f, 1f, (t * 3f) % 1f) / 3f;
            head.localRotation = headRest * Quaternion.Euler(0f, 0f, -200f * turns);
            return;
        }
        // Sudden jerks to a new angle, then holding it: unnatural, bird-like.
        float interval = state == KeyAlien.State.Stalk ? .45f : state == KeyAlien.State.Prowl ? 1.2f : 2f;
        if (Time.time >= nextTwitch)
        {
            nextTwitch = Time.time + Random.Range(interval * .5f, interval * 1.5f);
            twitchTarget = state == KeyAlien.State.Rush || state == KeyAlien.State.WindUp || state == KeyAlien.State.Lunge
                ? Quaternion.Euler(-10f, 0f, Random.Range(-10f, 10f)) // locked on
                : Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(-twitchDegrees, twitchDegrees), Random.Range(-twitchDegrees, twitchDegrees));
        }
        twitch = Quaternion.Slerp(twitch, twitchTarget, 1f - Mathf.Exp(-30f * dt)); // snap fast
        head.localRotation = headRest * twitch;
    }

    void AnimateArms(float raise, float moving)
    {
        float swing = Mathf.Sin(phase * Mathf.PI) * 25f * moving;
        // raise: 1 = claws up over the head (attack tell), negative = slashing forward.
        float pitch = -raise * 120f;
        if (armLeft != null) armLeft.localRotation = armLRest * Quaternion.Euler(pitch + swing, 0f, 0f);
        if (armRight != null) armRight.localRotation = armRRest * Quaternion.Euler(pitch - swing, 0f, 0f);
    }

    void AnimateDeath()
    {
        float t = Time.time - deathStart;
        float fall = Mathf.Clamp01(t / .55f);
        fall = 1f - (1f - fall) * (1f - fall);
        float sink = Mathf.Clamp01((t - 3f) / 3f);
        transform.localPosition = restPosition + Vector3.down * (sink * .6f);
        transform.localRotation = restRotation * Quaternion.Euler(fall * 70f, 0f, fall * 50f * deathSide);
        transform.localScale = restScale;
        if (head != null && head.IsChildOf(transform)) head.localRotation = headRest * Quaternion.Euler(fall * 30f, 0f, fall * -40f * deathSide);
    }

    void UpdateGlow(float aggression, float dt)
    {
        if (glowRenderers == null) return;
        float wanted = deathStart >= 0f ? 0f : Mathf.Lerp(glowCalm, glowRage, aggression);
        // Dead eyes fade out over a second; living ones flicker slightly.
        glow = Mathf.MoveTowards(glow, wanted, dt * (deathStart >= 0f ? 6f : 30f));
        float flicker = deathStart >= 0f ? 1f : 1f + (Mathf.PerlinNoise(Time.time * 9f, phase) - .5f) * .4f;
        Color color = glowColor;
        if (deathStart < 0f && brain != null)
        {
            if (brain.Current == KeyAlien.State.WindUp)
            {
                // The attack tell: eyes flare white-hot as it winds up.
                float flare = Mathf.Clamp01(brain.StateTime / Mathf.Max(.05f, brain.WindUpSeconds));
                color = Color.Lerp(glowColor, Color.white, .3f + .5f * flare);
                flicker *= 1.6f + flare;
            }
            else if (brain.IsOpen)
            {
                // Open to a finisher: eyes strobe white, unmistakable.
                bool on = Mathf.Repeat(Time.time * 8f, 1f) < .5f;
                color = on ? Color.white : glowColor;
                flicker = on ? 1.8f : .5f;
            }
        }
        foreach (Renderer r in glowRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, color * (glow * flicker));
            r.SetPropertyBlock(block);
        }
    }
}
