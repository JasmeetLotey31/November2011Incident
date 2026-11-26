using UnityEngine;

/// <summary>
/// What a key drops when it dies. Fighting up close pays (Doom's rule): a normal kill sometimes
/// drops a brass scrap of ammo; a kick kill always pays out more ammo plus a health scrap.
/// Works on anything with Health (walking keys and flying keys).
/// </summary>
[RequireComponent(typeof(Health))]
public sealed class KeyLoot : MonoBehaviour
{
    [SerializeField] LootScrap ammoScrapPrefab;
    [SerializeField] LootScrap healthScrapPrefab;
    [SerializeField] int roundsPerScrap = 2;
    [Tooltip("Kick-kill payout.")]
    [SerializeField] int kickAmmoScraps = 2;
    [SerializeField] float kickHeal = 15f;
    [Tooltip("Multiplies the difficulty's drop chance (flyers drop less than walkers).")]
    [Range(0, 2)] [SerializeField] float dropChanceScale = 1f;

    Health health;

    void Awake() => health = GetComponent<Health>();
    void OnEnable() => health.Died += OnDied;
    void OnDisable() => health.Died -= OnDied;

    void OnDied(DamageInfo info)
    {
        Vector3 origin = transform.position + Vector3.up * .6f;
        if (info.melee)
        {
            for (int i = 0; i < kickAmmoScraps; i++) Drop(ammoScrapPrefab, origin, roundsPerScrap, 0f);
            Drop(healthScrapPrefab, origin, 0, kickHeal);
        }
        else if (Random.value < Difficulty.Current.lootChance * dropChanceScale)
            Drop(ammoScrapPrefab, origin, roundsPerScrap, 0f);
    }

    void Drop(LootScrap prefab, Vector3 origin, int rounds, float heal)
    {
        if (prefab == null) return;
        LootScrap scrap = Instantiate(prefab, origin, Random.rotation);
        Vector3 launch = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(3f, 4.5f), Random.Range(-1.5f, 1.5f));
        scrap.Setup(rounds, heal, launch);
    }
}
