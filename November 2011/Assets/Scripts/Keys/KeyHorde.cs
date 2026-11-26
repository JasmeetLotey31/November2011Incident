using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The pack brain shared by every key. It keeps the horde smart instead of a conga line:
/// - only a couple of keys may rush at once ("rush tokens"); the rest stalk and spread out
///   around you to cut off escape routes;
/// - when you reload or run dry, more tokens open up and the pack charges;
/// - a key that spots you calls the others nearby (they converge on your position).
/// </summary>
public static class KeyHorde
{
    /// <summary>Rushers allowed while the player can shoot back. (Brutal: 3.)</summary>
    public static int MaxRushers = 3;
    /// <summary>Extra rushers allowed while the player is reloading / empty / grabbed.</summary>
    public static int VulnerableExtraRushers = 5;
    /// <summary>Extra rushers while the lights are out.</summary>
    public static int BlackoutExtraRushers = 2;

    /// <summary>Flyers allowed to dive at once (more when you're vulnerable or the lights are out).</summary>
    public static int MaxDivers = 3;

    static readonly List<KeyAlien> members = new List<KeyAlien>();
    static readonly List<KeyAlien> rushers = new List<KeyAlien>();
    static readonly List<KeyFlyer> flyers = new List<KeyFlyer>();
    static readonly List<KeyFlyer> divers = new List<KeyFlyer>();

    public static IReadOnlyList<KeyAlien> Members => members;
    public static IReadOnlyList<KeyFlyer> Flyers => flyers;
    public static int AliveCount
    {
        get
        {
            int n = 0;
            foreach (KeyAlien k in members) if (k != null && !k.IsDead && !k.IsHidden) n++;
            foreach (KeyFlyer f in flyers) if (f != null && !f.IsDead) n++;
            return n;
        }
    }

    public static void RegisterFlyer(KeyFlyer f) { if (!flyers.Contains(f)) flyers.Add(f); }
    public static void UnregisterFlyer(KeyFlyer f) { flyers.Remove(f); divers.Remove(f); }

    public static bool TryTakeDiveToken(KeyFlyer f)
    {
        if (divers.Contains(f)) return true;
        divers.RemoveAll(d => d == null || d.IsDead || !d.IsDiving);
        KeyTarget target = KeyTarget.Current;
        PlayerEaten prey = target != null ? target.GetComponent<PlayerEaten>() : null;
        int allowed = MaxDivers + (target != null && target.IsVulnerable ? 1 : 0) + (EmergencyLight.IsBlackout ? 1 : 0) + (prey != null && prey.IsBleeding ? 1 : 0);
        if (divers.Count >= allowed) return false;
        divers.Add(f);
        return true;
    }

    public static void ReleaseDiveToken(KeyFlyer f) => divers.Remove(f);

    public static void Register(KeyAlien key) { if (!members.Contains(key)) members.Add(key); }

    public static void Unregister(KeyAlien key)
    {
        members.Remove(key);
        rushers.Remove(key);
    }

    /// <summary>Ask to attack now. Returns true if this key may rush.</summary>
    public static bool TryTakeRushToken(KeyAlien key)
    {
        if (rushers.Contains(key)) return true;
        rushers.RemoveAll(k => k == null || k.IsDead || !k.WantsRush);
        KeyTarget target = KeyTarget.Current;
        int allowed = MaxRushers
                      + (target != null && target.IsVulnerable ? VulnerableExtraRushers : 0)
                      + (EmergencyLight.IsBlackout ? BlackoutExtraRushers : 0);
        if (rushers.Count >= allowed) return false;
        rushers.Add(key);
        return true;
    }

    public static void ReleaseRushToken(KeyAlien key) => rushers.Remove(key);

    /// <summary>
    /// Where this stalker should circle: keys spread evenly around the player, starting from the
    /// sides and back (where you aren't looking), so the pack surrounds you.
    /// </summary>
    public static float StalkAngle(KeyAlien key)
    {
        int index = 0, count = 0;
        foreach (KeyAlien k in members)
        {
            if (k == null || k.IsDead || k.IsHidden) continue;
            if (k == key) index = count;
            count++;
        }
        count = Mathf.Max(1, count);
        KeyTarget target = KeyTarget.Current;
        float facing = target != null && target.View != null ? target.View.transform.eulerAngles.y : 0f;
        // Start behind-left, fan around; never park everyone dead ahead.
        float spread = 300f / count;
        return facing + 150f + index * spread;
    }

    /// <summary>A key saw the player: every key within <paramref name="radius"/> learns where.</summary>
    public static void Alert(Vector3 playerPosition, Vector3 from, float radius)
    {
        foreach (KeyAlien k in members)
            if (k != null && !k.IsDead && !k.IsHidden && (k.transform.position - from).sqrMagnitude <= radius * radius)
                k.HearAlert(playerPosition);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        members.Clear();
        rushers.Clear();
        flyers.Clear();
        divers.Clear();
        MaxDivers = 3;
        MaxRushers = 3;
        VulnerableExtraRushers = 5;
        BlackoutExtraRushers = 2;
    }
}
