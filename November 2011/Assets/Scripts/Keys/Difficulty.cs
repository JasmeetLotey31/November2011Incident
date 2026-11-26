using UnityEngine;

public enum DifficultyLevel
{
    /// <summary>Scary and dangerous, but readable: a careful player can hold out.</summary>
    TenseButFair,
    /// <summary>Chaos: walkers and flying flocks everywhere, fast tells, plenty of ammo from kills.</summary>
    Brutal,
}

/// <summary>
/// One place for every number that decides how hard the hold-out is. The horde director picks the
/// level and applies it to the keys, the pack brain, the player and the ammo restocks.
/// </summary>
public static class Difficulty
{
    public struct Preset
    {
        public float lungeDamage;
        public float windUpSeconds;
        public float grabChance;
        public float grabFailDamage;
        public int grabPresses;
        public int maxRushers;
        public int vulnerableExtraRushers;
        public int blackoutExtraRushers;
        public float regenCap;
        public float regenDelay;
        public int startingReserve;
        public int roundsPerBox;
        /// <summary>Chance a normal kill drops ammo scrap (finishers always drop).</summary>
        public float lootChance;
    }

    public static DifficultyLevel Level = DifficultyLevel.Brutal;

    static readonly Preset TenseButFair = new Preset
    {
        lungeDamage = 18f, windUpSeconds = .55f, grabChance = .15f, grabFailDamage = 25f, grabPresses = 6,
        maxRushers = 2, vulnerableExtraRushers = 2, blackoutExtraRushers = 1,
        regenCap = .5f, regenDelay = 5f, startingReserve = 24, roundsPerBox = 8, lootChance = .7f,
    };

    // Brutal = CHAOTIC: lots of keys at once and fast tells, but every hit is survivable on its own
    // and kills keep you stocked. Pressure from numbers and speed, not from cheap one-shots.
    static readonly Preset Brutal = new Preset
    {
        lungeDamage = 16f, windUpSeconds = .42f, grabChance = .2f, grabFailDamage = 20f, grabPresses = 6,
        maxRushers = 4, vulnerableExtraRushers = 3, blackoutExtraRushers = 2,
        regenCap = .4f, regenDelay = 4f, startingReserve = 32, roundsPerBox = 10, lootChance = .85f,
    };

    public static Preset Current => Level == DifficultyLevel.Brutal ? Brutal : TenseButFair;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Level = DifficultyLevel.Brutal;
}
