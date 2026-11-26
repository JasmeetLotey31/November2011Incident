using UnityEngine;

/// <summary>
/// A box of rounds. Look at it and press Interact. Ammo is scarce on purpose: every shot counts.
/// Needs a collider for the player to aim at.
/// </summary>
public sealed class AmmoPickup : MonoBehaviour
{
    [SerializeField] int rounds = 8;

    public int Rounds => rounds;

    /// <summary>Gives as many rounds as the gun can carry. Returns false if it took none.</summary>
    public bool TryCollect(Sidearm gun)
    {
        if (gun == null || rounds <= 0) return false;
        int taken = gun.AddReserve(rounds);
        if (taken <= 0) return false;
        rounds -= taken;
        if (rounds <= 0) Destroy(gameObject);
        return true;
    }

    /// <summary>Set by a spawner (e.g. the horde director restocking the armory).</summary>
    public void SetRounds(int value) => rounds = Mathf.Max(0, value);
}
