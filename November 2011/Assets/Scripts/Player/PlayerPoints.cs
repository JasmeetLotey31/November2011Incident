using System;
using UnityEngine;

/// <summary>
/// Points for fighting well, spent on guns and ammo between waves (Zombies style):
/// every hit on a key, more for the throat, a lot for kills and the best for finishing one
/// with your boot or winning a pin, plus a bonus for each wave cleared.
/// Shotgun pellets count as one hit per key per shot. The horde director adds this to the
/// player if it's missing. The HUD listens to <see cref="Earned"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerPoints : MonoBehaviour
{
    [SerializeField] int startPoints = 500;
    [Header("Earning")]
    [SerializeField] int hit = 10;
    [SerializeField] int throatHit = 20;
    [SerializeField] int kill = 60;
    [SerializeField] int throatKill = 100;
    [SerializeField] int kickKill = 130;
    [SerializeField] int pinWon = 100;
    [Tooltip("Wave cleared: this times the wave number.")]
    [SerializeField] int clearBonusPerWave = 50;

    public static PlayerPoints Instance { get; private set; }
    public int Points { get; private set; }
    public int TotalEarned { get; private set; }
    public int Kills { get; private set; }

    /// <summary>(amount, label) for the HUD pop-ups; label is empty for plain hits.</summary>
    public event Action<int, string> Earned;
    public event Action Changed;

    PlayerPinned pinned;
    int lastHitFrame = -1;
    Health lastHitTarget;
    bool hooked;

    void Awake()
    {
        Instance = this;
        Points = startPoints;
    }

    void OnEnable()
    {
        CombatFeedback.Damaged += OnHit;
        hooked = false;
    }

    void OnDisable()
    {
        CombatFeedback.Damaged -= OnHit;
        if (pinned != null) pinned.Ended -= OnPinEnded;
        KeyHordeDirector d = KeyHordeDirector.Active;
        if (d != null) d.WaveCleared -= OnWaveCleared;
        hooked = false;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        // The pin component and the director can come up after us: hook them once they exist.
        if (hooked) return;
        var eaten = GetComponent<PlayerEaten>();
        pinned = eaten != null ? eaten.Pinned : GetComponent<PlayerPinned>();
        KeyHordeDirector d = KeyHordeDirector.Active;
        if (pinned == null || d == null) return;
        pinned.Ended += OnPinEnded;
        d.WaveCleared += OnWaveCleared;
        hooked = true;
    }

    public bool CanAfford(int cost) => Points >= cost;

    /// <summary>Pay for something. False (and nothing spent) if you can't afford it.</summary>
    public bool Spend(int cost)
    {
        if (cost < 0 || Points < cost) return false;
        Points -= cost;
        Changed?.Invoke();
        return true;
    }

    public void Add(int amount, string label = "")
    {
        if (amount <= 0) return;
        Points += amount;
        TotalEarned += amount;
        Earned?.Invoke(amount, label);
        Changed?.Invoke();
    }

    void OnHit(DamageInfo info, DamageOutcome outcome, Health target)
    {
        if (info.source == null || info.source.transform.root != transform.root) return; // not ours
        if (target == null || (target.GetComponentInParent<KeyFlyer>() == null && target.GetComponentInParent<KeyAlien>() == null)) return;
        bool throat = info.part == BodyPart.Throat || info.weakSpot;
        if (outcome == DamageOutcome.Killed)
        {
            Kills++;
            if (info.melee) Add(kickKill, throat ? "STAB KILL" : "KICK KILL");
            else if (throat) Add(throatKill, "THROAT");
            else Add(kill, "KILL");
            return;
        }
        // One hit per key per frame (a shotgun blast is one hit, not eight).
        if (lastHitFrame == Time.frameCount && lastHitTarget == target) return;
        lastHitFrame = Time.frameCount;
        lastHitTarget = target;
        Add(throat ? throatHit : hit);
    }

    void OnPinEnded(bool won) { if (won) Add(pinWon, "STABBED IT"); }

    void OnWaveCleared(int wave) => Add(clearBonusPerWave * wave, $"WAVE {wave} CLEARED");
}
