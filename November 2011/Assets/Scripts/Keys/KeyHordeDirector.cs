using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// The hunt. No waves: the keys never stop coming, they come in tides.
/// A pacing director (build up, peak, back off) watches how hard you're being hit:
/// - BUILD: keys glitch in one by one, out of your sight, until the pack is the size the
///   run has earned (it grows the longer you survive, up to <see cref="maxPack"/>).
/// - PEAK: when you're taking a beating, or the pack is full and you're holding out, it lets
///   the pressure spike for a while: a blackout, the pack at full strength, no let-up.
/// - RELAX: after a peak it stops sending keys for a few seconds so you can breathe, reload,
///   and grab the ammo it drops in the armory. Then it starts building again.
/// Dead keys are replaced. You hold out until the escape pod is ready (and they keep coming).
/// The HUD listens to <see cref="Radio"/>.
/// </summary>
public sealed class KeyHordeDirector : MonoBehaviour
{
    public enum Phase { Starting, Build, Peak, Relax }

    [Tooltip("Tense but fair: scary, readable, beatable. Brutal: for when you've learned the loop.")]
    [SerializeField] DifficultyLevel difficulty = DifficultyLevel.Brutal;
    [SerializeField] KeyFlyer flyerPrefab;
    [SerializeField] float startDelay = 4f;

    [Header("Pack size")]
    [Tooltip("Keys hunting you at the start.")]
    [FormerlySerializedAs("flockSizeFirstWave")]
    [SerializeField] int startPack = 2;
    [Tooltip("Most keys hunting you at once.")]
    [FormerlySerializedAs("flockSizeCap")]
    [SerializeField] int maxPack = 5;
    [Tooltip("One more key in the pack every this many seconds survived.")]
    [SerializeField] float secondsPerExtraKey = 40f;
    [Tooltip("Seconds between keys glitching in while building (random between).")]
    [SerializeField] Vector2 spawnGap = new Vector2(2.5f, 5f);

    [Header("Pacing")]
    [Tooltip("Intensity added per point of damage you take.")]
    [SerializeField] float intensityPerDamage = 1f / 45f;
    [Tooltip("Intensity lost per second.")]
    [SerializeField] float intensityDecay = .05f;
    [Tooltip("Intensity at which it goes to a peak.")]
    [SerializeField] float peakAt = .85f;
    [Tooltip("Holding out with a full pack forces a peak after this long.")]
    [SerializeField] float buildMaxSeconds = 45f;
    [SerializeField] Vector2 peakSeconds = new Vector2(8f, 12f);
    [SerializeField] Vector2 relaxSeconds = new Vector2(7f, 11f);

    [Header("Holding out")]
    [Tooltip("Seconds survived until the escape pod is ready (the hunt keeps going if you stay).")]
    [SerializeField] float escapeAfterSeconds = 360f;

    [Header("Lights out (the keys cut the power)")]
    [SerializeField] float blackoutSeconds = 6f;
    [Range(0, 1)] [SerializeField] float blackoutChancePerPeak = .6f;
    [SerializeField] string blackoutRadio = "THEY CUT THE POWER! Flashlight, NOW!";
    [SerializeField] AudioSource blackoutAudio;
    [SerializeField] AudioClip blackoutClip;

    [Header("Ammo (dropped in the armory when it backs off)")]
    [SerializeField] AmmoPickup ammoPrefab;
    [SerializeField] Transform[] ammoSpawnPoints;
    [SerializeField] int ammoBoxesPerBreak = 2;
    [Tooltip("Brutal: one magazine short of comfortable.")]
    [SerializeField] int roundsPerBox = 6;

    [Header("Radio")]
    [SerializeField] string startRadio = "ARMORY, COME IN... something is in there with you. Something BIG.";
    [SerializeField] string[] arriveRadio =
    {
        "...did you hear that? Something's in here with you.",
        "It's BEHIND YOU!",
        "They don't show up on the cameras. Watch your back.",
        "Don't let it get its tongue on you!",
        "It's EATING them. It's eating the crew!",
        "Watch the ceiling! They drop blade-first!",
        "If you go down, GET UP. They swallow you whole.",
    };
    [SerializeField] string[] peakRadio =
    {
        "There's too many of them! Keep moving!",
        "Don't stand still! DON'T STAND STILL!",
        "They're all coming at once!",
    };
    [SerializeField] string[] relaxRadio =
    {
        "...they pulled back. Grab ammo from the racks. Now.",
        "It's quiet. That's worse. Reload.",
        "They're feeding. Use it. Get ammo.",
    };
    [SerializeField] string escapeRadio = "POD 3 IS READY. You made it. ...they're still coming.";

    public Phase Current { get; private set; }
    public float Intensity { get; private set; }
    public float SurvivedSeconds { get; private set; }
    public int PackSize => Mathf.Min(maxPack, startPack + Mathf.FloorToInt(SurvivedSeconds / Mathf.Max(1f, secondsPerExtraKey)));
    public int Alive => flyersAlive.Count + pending;
    public bool EscapeReady { get; private set; }

    public event Action<string> Radio;
    public event Action<Phase> PhaseChanged;
    public event Action EscapeUnlocked;
    public event Action BlackoutStarted;

    readonly List<KeyFlyer> flyersAlive = new List<KeyFlyer>();
    int pending;
    float phaseUntil, phaseStarted, nextSpawn;
    bool firstArrival = true;

    void Awake()
    {
        Difficulty.Level = difficulty;
        Difficulty.Preset p = Difficulty.Current;
        KeyHorde.MaxRushers = p.maxRushers;
        KeyHorde.VulnerableExtraRushers = p.vulnerableExtraRushers;
        KeyHorde.BlackoutExtraRushers = p.blackoutExtraRushers;
        roundsPerBox = p.roundsPerBox;
    }

    void Start()
    {
        // Player-side numbers (their components have finished Awake by now).
        Difficulty.Preset p = Difficulty.Current;
        KeyTarget target = KeyTarget.Current;
        if (target != null)
        {
            var vitals = target.GetComponent<PlayerVitals>();
            if (vitals != null) vitals.ApplyDifficulty(p.regenCap, p.regenDelay);
            var close = target.GetComponent<PlayerCloseCombat>();
            if (close != null) close.ApplyDifficulty(p.grabFailDamage, p.grabPresses);
            if (target.Gun != null) target.Gun.SetReserve(p.startingReserve);
            if (target.Health != null) target.Health.Damaged += OnPlayerDamaged;
        }
        foreach (AmmoPickup box in FindObjectsByType<AmmoPickup>()) box.SetRounds(p.roundsPerBox);
        Current = Phase.Starting;
        phaseUntil = Time.time + startDelay;
    }

    void OnDestroy()
    {
        KeyTarget target = KeyTarget.Current;
        if (target != null && target.Health != null) target.Health.Damaged -= OnPlayerDamaged;
    }

    void OnPlayerDamaged(DamageInfo info) => Intensity = Mathf.Clamp01(Intensity + info.amount * intensityPerDamage);

    void Update()
    {
        KeyTarget target = KeyTarget.Current;
        if (target == null || target.IsDead || flyerPrefab == null) return;
        flyersAlive.RemoveAll(f => f == null || f.IsDead);

        if (Current != Phase.Starting) SurvivedSeconds += Time.deltaTime;
        Intensity = Mathf.Max(0f, Intensity - intensityDecay * Time.deltaTime);
        // Keys right on top of you count as pressure too.
        foreach (KeyFlyer k in flyersAlive)
            if (k != null && Vector3.Distance(k.transform.position, target.Position) < 3f) Intensity = Mathf.Min(1f, Intensity + .04f * Time.deltaTime);

        if (!EscapeReady && SurvivedSeconds >= escapeAfterSeconds)
        {
            EscapeReady = true;
            Radio?.Invoke(escapeRadio);
            EscapeUnlocked?.Invoke();
        }

        switch (Current)
        {
            case Phase.Starting:
                if (Time.time >= phaseUntil) { Radio?.Invoke(startRadio); Enter(Phase.Build); }
                break;

            case Phase.Build:
                if (Alive < PackSize && Time.time >= nextSpawn) SpawnOne(target, spawnGap);
                if (Intensity >= peakAt || (Alive >= PackSize && Time.time - phaseStarted > buildMaxSeconds)) Enter(Phase.Peak);
                break;

            case Phase.Peak:
                // Full pack, replacements come fast.
                if (Alive < PackSize && Time.time >= nextSpawn) SpawnOne(target, spawnGap * .5f);
                if (Time.time >= phaseUntil) Enter(Phase.Relax);
                break;

            case Phase.Relax:
                // Nothing new arrives; whoever's left keeps hunting. Come back early if you've
                // cleared them all.
                if (Time.time >= phaseUntil || (Alive == 0 && Time.time - phaseStarted > 4f)) Enter(Phase.Build);
                break;
        }
    }

    void Enter(Phase next)
    {
        Current = next;
        phaseStarted = Time.time;
        switch (next)
        {
            case Phase.Build:
                nextSpawn = Time.time + .5f;
                break;
            case Phase.Peak:
                phaseUntil = Time.time + UnityEngine.Random.Range(peakSeconds.x, peakSeconds.y);
                Radio?.Invoke(peakRadio[UnityEngine.Random.Range(0, peakRadio.Length)]);
                if (UnityEngine.Random.value < blackoutChancePerPeak) StartCoroutine(BlackoutSoon());
                break;
            case Phase.Relax:
                phaseUntil = Time.time + UnityEngine.Random.Range(relaxSeconds.x, relaxSeconds.y);
                Intensity *= .3f;
                Radio?.Invoke(relaxRadio[UnityEngine.Random.Range(0, relaxRadio.Length)]);
                Restock();
                break;
        }
        PhaseChanged?.Invoke(next);
    }

    void SpawnOne(KeyTarget target, Vector2 gap)
    {
        nextSpawn = Time.time + UnityEngine.Random.Range(gap.x, gap.y);
        if (firstArrival || UnityEngine.Random.value < .3f) Radio?.Invoke(arriveRadio[UnityEngine.Random.Range(0, arriveRadio.Length)]);
        firstArrival = false;
        Vector3 at = AppearPoint(target);
        KeyFlyer key = Instantiate(flyerPrefab, at, Quaternion.identity);
        key.ApplyDifficulty(Difficulty.Current);
        key.Died += f => flyersAlive.Remove(f);
        flyersAlive.Add(key);
        key.Spawn(at, target.Position - at);
    }

    IEnumerator BlackoutSoon()
    {
        yield return new WaitForSeconds(UnityEngine.Random.Range(1f, 3f));
        if (Current != Phase.Peak || flyersAlive.Count == 0) yield break;
        if (KeyTarget.Current != null && KeyTarget.Current.IsDead) yield break;
        TriggerBlackout();
    }

    /// <summary>Somewhere in the hall, at hovering height, preferably just outside the player's view.</summary>
    Vector3 AppearPoint(KeyTarget target)
    {
        Vector3 centre = target != null ? target.Position : Vector3.zero;
        Vector3 best = centre + Vector3.up * 2.5f + Vector3.forward * 6f;
        for (int tries = 0; tries < 16; tries++)
        {
            Vector2 ring = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(6f, 10f);
            Vector3 p = centre + new Vector3(ring.x, UnityEngine.Random.Range(2.2f, 3.2f), ring.y);
            // Inside the hall: open space all around and a floor and ceiling.
            if (Physics.CheckSphere(p, 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (!Physics.Raycast(p, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (!Physics.Raycast(p, Vector3.up, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
            best = p;
            if (target == null || target.View == null) break;
            Vector3 vp = target.View.WorldToViewportPoint(p);
            bool inView = vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
            if (!inView) break; // out of sight: perfect
        }
        return best;
    }

    /// <summary>Cut the lights now (also callable from tests / scripted moments).</summary>
    public void TriggerBlackout()
    {
        EmergencyLight.Blackout(blackoutSeconds);
        if (blackoutAudio != null && blackoutClip != null) blackoutAudio.PlayOneShot(blackoutClip);
        Radio?.Invoke(blackoutRadio);
        BlackoutStarted?.Invoke();
    }

    /// <summary>Force a phase (tests / scripted moments).</summary>
    public void ForcePhase(Phase phase) => Enter(phase);

    void Restock()
    {
        if (ammoPrefab == null || ammoSpawnPoints == null || ammoSpawnPoints.Length == 0) return;
        var free = new List<Transform>();
        foreach (Transform p in ammoSpawnPoints)
            if (p != null && p.GetComponentInChildren<AmmoPickup>() == null) free.Add(p);
        for (int i = 0; i < ammoBoxesPerBreak && free.Count > 0; i++)
        {
            int pick = UnityEngine.Random.Range(0, free.Count);
            AmmoPickup box = Instantiate(ammoPrefab, free[pick].position, free[pick].rotation, free[pick]);
            box.SetRounds(roundsPerBox);
            free.RemoveAt(pick);
        }
    }
}
