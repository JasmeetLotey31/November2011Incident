using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The hunt, in waves.
/// - WAVE n: a set number of keys glitch in, a few at a time (never more than the wave's cap
///   alive at once), just out of your sight. Kill them all to clear it.
/// - BREAK: a short breather. Ammo restocks in the armory, points for the clear, then the next
///   wave, bigger: more keys, more at once, faster arrivals.
/// - The keys learn: the first wave only bites, whips and rushes; spears, dives, spit, screams
///   and pins come in over the next waves. From wave 5 they get tougher every wave.
/// - Mid-wave the power can go out. At the escape wave the pod is ready... and they keep coming.
/// The HUD listens to <see cref="Radio"/>, <see cref="WaveStarted"/> and <see cref="WaveCleared"/>.
/// </summary>
public sealed class KeyHordeDirector : MonoBehaviour
{
    public enum Phase { Starting, Wave, Break }

    [Tooltip("Tense but fair: scary, readable, beatable. Brutal: for when you've learned the loop.")]
    [SerializeField] DifficultyLevel difficulty = DifficultyLevel.Brutal;
    [SerializeField] KeyFlyer flyerPrefab;
    [SerializeField] float startDelay = 4f;

    [Header("Waves")]
    [SerializeField] int firstWaveKeys = 6;
    [SerializeField] int extraKeysPerWave = 3;
    [Tooltip("Keys alive at once in wave 1...")]
    [SerializeField] int firstWaveMaxAlive = 3;
    [Tooltip("...one more every this many waves...")]
    [SerializeField] int wavesPerExtraAlive = 2;
    [Tooltip("...up to this many.")]
    [SerializeField] int maxAliveCap = 8;
    [Tooltip("Seconds between keys arriving in wave 1 (random between); shrinks every wave.")]
    [SerializeField] Vector2 spawnGap = new Vector2(1.5f, 3.5f);
    [Range(0, .2f)] [SerializeField] float spawnGapShrinkPerWave = .07f;
    [SerializeField] float breakSeconds = 15f;
    [Tooltip("From this wave on, keys get this much more health every wave.")]
    [SerializeField] int tougherFromWave = 5;
    [SerializeField] float extraHealthPerWave = .08f;
    [Tooltip("The escape pod is ready after this wave (the waves keep coming if you stay).")]
    [SerializeField] int escapeWave = 10;

    [Header("Lights out (the keys cut the power)")]
    [SerializeField] int blackoutFromWave = 3;
    [Range(0, 1)] [SerializeField] float blackoutChancePerWave = .5f;
    [SerializeField] float blackoutSeconds = 6f;
    [SerializeField] string blackoutRadio = "THEY CUT THE POWER! Flashlight, NOW!";
    [SerializeField] AudioSource blackoutAudio;
    [SerializeField] AudioClip blackoutClip;

    [Header("Ammo (dropped in the armory between waves)")]
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
    [SerializeField] string[] waveRadio =
    {
        "Here they come again!",
        "More of them. MORE of them!",
        "They're coming through the vents!",
        "Hold the armory. HOLD IT.",
    };
    [SerializeField] string[] clearRadio =
    {
        "...they pulled back. Grab ammo from the racks. Now.",
        "It's quiet. That's worse. Reload.",
        "You're still alive? Get ammo. They'll be back.",
    };
    [SerializeField] string escapeRadio = "POD 3 IS READY. You made it. ...they're still coming.";

    /// <summary>The director running the current level (null if none).</summary>
    public static KeyHordeDirector Active { get; private set; }
    public Phase Current { get; private set; }
    public int Wave { get; private set; }
    /// <summary>Keys of this wave still to kill (alive + not arrived yet).</summary>
    public int KeysLeft => toSpawn + Alive;
    public int Alive => flyersAlive.Count;
    public int MaxAlive { get; private set; }
    public float BreakSecondsLeft => Current == Phase.Break ? Mathf.Max(0f, breakUntil - Time.time) : 0f;
    public float SurvivedSeconds { get; private set; }
    public bool EscapeReady { get; private set; }

    public event Action<string> Radio;
    public event Action<Phase> PhaseChanged;
    public event Action<int> WaveStarted, WaveCleared;
    public event Action EscapeUnlocked;
    public event Action BlackoutStarted;

    readonly List<KeyFlyer> flyersAlive = new List<KeyFlyer>();
    int toSpawn, spawnedThisWave, blackoutAtKeysLeft = -1;
    float phaseUntil, breakUntil, nextSpawn;
    bool firstArrival = true;

    void Awake()
    {
        Active = this;
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
            if (target.GetComponent<PlayerPoints>() == null) target.gameObject.AddComponent<PlayerPoints>();
        }
        foreach (AmmoPickup box in FindObjectsByType<AmmoPickup>()) box.SetRounds(p.roundsPerBox);
        Current = Phase.Starting;
        phaseUntil = Time.time + startDelay;
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
    }

    void Update()
    {
        KeyTarget target = KeyTarget.Current;
        if (target == null || target.IsDead || flyerPrefab == null) return;
        flyersAlive.RemoveAll(f => f == null || f.IsDead);
        if (Current != Phase.Starting) SurvivedSeconds += Time.deltaTime;

        switch (Current)
        {
            case Phase.Starting:
                if (Time.time >= phaseUntil) { Radio?.Invoke(startRadio); StartWave(1); }
                break;

            case Phase.Wave:
                if (toSpawn > 0 && Alive < MaxAlive && Time.time >= nextSpawn) SpawnOne(target);
                if (blackoutAtKeysLeft >= 0 && KeysLeft <= blackoutAtKeysLeft && Alive > 0)
                {
                    blackoutAtKeysLeft = -1;
                    StartCoroutine(BlackoutSoon());
                }
                if (toSpawn == 0 && Alive == 0) ClearWave();
                break;

            case Phase.Break:
                if (Time.time >= breakUntil) StartWave(Wave + 1);
                break;
        }
    }

    // ---------------- waves ----------------

    /// <summary>How many keys a wave sends.</summary>
    public int KeysInWave(int wave) => firstWaveKeys + extraKeysPerWave * Mathf.Max(0, wave - 1);

    void StartWave(int wave)
    {
        Wave = wave;
        toSpawn = KeysInWave(wave);
        spawnedThisWave = 0;
        MaxAlive = Mathf.Min(maxAliveCap, firstWaveMaxAlive + (wave - 1) / Mathf.Max(1, wavesPerExtraAlive));
        nextSpawn = Time.time + .5f;
        // Sometimes the power goes out partway through (from the blackout wave on).
        blackoutAtKeysLeft = wave >= blackoutFromWave && UnityEngine.Random.value < blackoutChancePerWave
            ? Mathf.RoundToInt(toSpawn * UnityEngine.Random.Range(.35f, .7f)) : -1;
        if (wave > 1) Radio?.Invoke(waveRadio[UnityEngine.Random.Range(0, waveRadio.Length)]);
        Enter(Phase.Wave);
        WaveStarted?.Invoke(wave);
    }

    void ClearWave()
    {
        breakUntil = Time.time + breakSeconds;
        Enter(Phase.Break);
        Radio?.Invoke(Wave == escapeWave ? escapeRadio : clearRadio[UnityEngine.Random.Range(0, clearRadio.Length)]);
        Restock();
        WaveCleared?.Invoke(Wave);
        if (Wave >= escapeWave && !EscapeReady)
        {
            EscapeReady = true;
            EscapeUnlocked?.Invoke();
        }
    }

    /// <summary>Skip the rest of the break (a "ready" button later; tests now).</summary>
    public void StartNextWaveNow() { if (Current == Phase.Break) breakUntil = Time.time; }

    void Enter(Phase next)
    {
        Current = next;
        PhaseChanged?.Invoke(next);
    }

    void SpawnOne(KeyTarget target)
    {
        float shrink = Mathf.Pow(1f - spawnGapShrinkPerWave, Wave - 1);
        nextSpawn = Time.time + Mathf.Max(.5f, UnityEngine.Random.Range(spawnGap.x, spawnGap.y) * shrink);
        // A wave opens with its whole pack arriving fast; an emptied room refills fast too.
        spawnedThisWave++;
        if (spawnedThisWave < MaxAlive || Alive == 0) nextSpawn = Time.time + UnityEngine.Random.Range(.5f, .9f);
        toSpawn--;
        if (firstArrival || UnityEngine.Random.value < .2f) Radio?.Invoke(arriveRadio[UnityEngine.Random.Range(0, arriveRadio.Length)]);
        firstArrival = false;
        Vector3 at = AppearPoint(target);
        KeyFlyer key = Instantiate(flyerPrefab, at, Quaternion.identity);
        key.ApplyDifficulty(Difficulty.Current);
        float health = Wave >= tougherFromWave ? 1f + extraHealthPerWave * (Wave - tougherFromWave + 1) : 1f;
        key.ApplyWave(Wave, health);
        key.Died += f => flyersAlive.Remove(f);
        flyersAlive.Add(key);
        key.Spawn(at, target.Position - at);
    }

    IEnumerator BlackoutSoon()
    {
        yield return new WaitForSeconds(UnityEngine.Random.Range(.5f, 2f));
        if (Current != Phase.Wave || flyersAlive.Count == 0) yield break;
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
