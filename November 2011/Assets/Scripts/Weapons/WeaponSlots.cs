using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The guns you carry: 1 / 2 / mouse wheel / D-pad left-right to switch. Only the active gun's
/// object is enabled (so only its HUD, sounds and hit feedback run). Can't switch mid-reload.
/// Put it on the WeaponHolder with each gun as a child.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponSlots : MonoBehaviour
{
    [SerializeField] Sidearm[] guns;
    [SerializeField] int startIndex;
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip switchClip;

    public Sidearm Active { get; private set; }
    public int ActiveIndex { get; private set; } = -1;
    public event Action<Sidearm> Switched;

    InputAction slot1, slot2, next, prev;

    void Awake()
    {
        if (guns == null || guns.Length == 0) guns = GetComponentsInChildren<Sidearm>(true);
        slot1 = new InputAction("Slot1", InputActionType.Button, "<Keyboard>/1");
        slot2 = new InputAction("Slot2", InputActionType.Button, "<Keyboard>/2");
        next = new InputAction("NextGun", InputActionType.Button, "<Gamepad>/dpad/right");
        prev = new InputAction("PrevGun", InputActionType.Button, "<Gamepad>/dpad/left");
        for (int i = 0; i < guns.Length; i++) guns[i].gameObject.SetActive(false);
        Select(Mathf.Clamp(startIndex, 0, guns.Length - 1), true);
    }

    void OnEnable() { slot1.Enable(); slot2.Enable(); next.Enable(); prev.Enable(); }
    void OnDisable() { slot1.Disable(); slot2.Disable(); next.Disable(); prev.Disable(); }
    void OnDestroy() { slot1.Dispose(); slot2.Dispose(); next.Dispose(); prev.Dispose(); }

    void Update()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (slot1.WasPressedThisFrame()) Select(0);
        else if (slot2.WasPressedThisFrame()) Select(1);
        else if (next.WasPressedThisFrame()) Select((ActiveIndex + 1) % guns.Length);
        else if (prev.WasPressedThisFrame()) Select((ActiveIndex - 1 + guns.Length) % guns.Length);
        else if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (scroll > .1f) Select((ActiveIndex + 1) % guns.Length);
            else if (scroll < -.1f) Select((ActiveIndex - 1 + guns.Length) % guns.Length);
        }
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
