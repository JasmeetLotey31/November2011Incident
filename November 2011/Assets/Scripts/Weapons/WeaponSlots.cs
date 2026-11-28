using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The guns you carry (two at most, Zombies style): 1 / 2 / mouse wheel / D-pad left-right to
/// switch between them. Every gun the player could ever hold is a child of the WeaponHolder; you
/// start with <see cref="startCarried"/> and get the rest from the weapon lockers
/// (<see cref="Give"/>). Taking a third gun swaps out the one in your hands.
/// Only the active gun's object is enabled (so only its HUD, sounds and hit feedback run).
/// Can't switch mid-reload. Put it on the WeaponHolder with each gun as a child.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponSlots : MonoBehaviour
{
    [SerializeField] Sidearm[] guns;
    [Tooltip("Guns carried at the start (indexes into Guns). Empty = carry all of them.")]
    [SerializeField] int[] startCarried = { 0 };
    [SerializeField] int maxCarried = 2;
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip switchClip;

    public Sidearm Active { get; private set; }
    public int ActiveIndex { get; private set; } = -1;
    public IReadOnlyList<Sidearm> Guns => guns;
    public event Action<Sidearm> Switched;

    readonly List<int> carried = new List<int>(); // in slot order (slot 1, slot 2)
    InputAction slot1, slot2, next, prev;

    void Awake()
    {
        if (guns == null || guns.Length == 0) guns = GetComponentsInChildren<Sidearm>(true);
        slot1 = new InputAction("Slot1", InputActionType.Button, "<Keyboard>/1");
        slot2 = new InputAction("Slot2", InputActionType.Button, "<Keyboard>/2");
        next = new InputAction("NextGun", InputActionType.Button, "<Gamepad>/dpad/right");
        prev = new InputAction("PrevGun", InputActionType.Button, "<Gamepad>/dpad/left");
        for (int i = 0; i < guns.Length; i++) guns[i].gameObject.SetActive(false);
        if (startCarried != null)
            foreach (int i in startCarried)
                if (i >= 0 && i < guns.Length && !carried.Contains(i) && carried.Count < maxCarried) carried.Add(i);
        if (carried.Count == 0) for (int i = 0; i < guns.Length; i++) carried.Add(i);
        Select(carried[0], true);
    }

    void OnEnable() { slot1.Enable(); slot2.Enable(); next.Enable(); prev.Enable(); }
    void OnDisable() { slot1.Disable(); slot2.Disable(); next.Disable(); prev.Disable(); }
    void OnDestroy() { slot1.Dispose(); slot2.Dispose(); next.Dispose(); prev.Dispose(); }

    void Update()
    {
        if (Cursor.lockState != CursorLockMode.Locked || carried.Count == 0) return;
        int at = Mathf.Max(0, carried.IndexOf(ActiveIndex));
        if (slot1.WasPressedThisFrame()) Select(carried[0]);
        else if (slot2.WasPressedThisFrame() && carried.Count > 1) Select(carried[1]);
        else if (next.WasPressedThisFrame()) Select(carried[(at + 1) % carried.Count]);
        else if (prev.WasPressedThisFrame()) Select(carried[(at - 1 + carried.Count) % carried.Count]);
        else if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (scroll > .1f) Select(carried[(at + 1) % carried.Count]);
            else if (scroll < -.1f) Select(carried[(at - 1 + carried.Count) % carried.Count]);
        }
    }

    public bool Carries(Sidearm gun) => gun != null && carried.Contains(Array.IndexOf(guns, gun));

    /// <summary>The gun with this display name (e.g. "SHOTGUN"), carried or not.</summary>
    public Sidearm Find(string displayName)
    {
        foreach (Sidearm g in guns) if (g != null && string.Equals(g.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)) return g;
        return null;
    }

    /// <summary>The gun you'd lose by taking a new one (null if there's a free hand).</summary>
    public Sidearm WouldDrop => carried.Count >= maxCarried ? Active : null;

    /// <summary>Take a gun from a locker: full ammo, in your hands. Swaps out the active gun if both hands are full.</summary>
    public void Give(Sidearm gun)
    {
        int index = Array.IndexOf(guns, gun);
        if (index < 0) return;
        if (!carried.Contains(index))
        {
            if (carried.Count >= maxCarried) carried[Mathf.Max(0, carried.IndexOf(ActiveIndex))] = index;
            else carried.Add(index);
        }
        if (Active != null && Active.IsReloading) Active.gameObject.SetActive(false); // drop the reload
        Select(index, true);
        gun.FillAmmo(); // after it's switched on: a gun's first Awake sets its starting ammo
        if (audioSource != null && switchClip != null) audioSource.PlayOneShot(switchClip);
    }

    public void Select(int index, bool force = false)
    {
        if (guns == null || guns.Length == 0 || index < 0 || index >= guns.Length) return;
        if (!force && (index == ActiveIndex || (Active != null && Active.IsReloading))) return;
        if (Active != null) Active.gameObject.SetActive(false);
        ActiveIndex = index;
        Active = guns[index];
        Active.gameObject.SetActive(true);
        if (!force && audioSource != null && switchClip != null) audioSource.PlayOneShot(switchClip);
        Switched?.Invoke(Active);
    }
}
