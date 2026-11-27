using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Basic first-person movement: WASD walk, Shift sprint, Space jump, mouse look.
/// Reads the project-wide input actions (Assets/InputSystem_Actions, "Player" map).
/// Put this on the player root (with a CharacterController) and give it the camera as a child.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera that pitches up/down. Usually a child of this object at eye height.")]
    [SerializeField] Transform cameraPivot;

    [Header("Movement")]
    [SerializeField] float walkSpeed = 4f;
    [SerializeField] float sprintSpeed = 7f;
    [SerializeField] float jumpHeight = 1.2f;
    [SerializeField] float gravity = -20f;

    [Header("Look")]
    [Tooltip("Degrees per pixel of mouse movement.")]
    [SerializeField] float mouseSensitivity = 0.1f;
    [Tooltip("Degrees per second at full gamepad stick.")]
    [SerializeField] float gamepadLookSpeed = 180f;
    [SerializeField] float maxPitch = 85f;

    /// <summary>
    /// Multiplies walk and sprint speed; other systems set it (e.g. 0.6 while carrying a key,
    /// 0 for a moment when grabbed). 1 = normal.
    /// </summary>
    public float SpeedMultiplier { get; set; } = 1f;

    CharacterController controller;
    InputAction moveAction, lookAction, jumpAction, sprintAction;
    float pitch;
    float verticalVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraPivot == null && Camera.main != null)
            cameraPivot = Camera.main.transform;

        moveAction = InputSystem.actions.FindAction("Player/Move", throwIfNotFound: true);
        lookAction = InputSystem.actions.FindAction("Player/Look", throwIfNotFound: true);
        jumpAction = InputSystem.actions.FindAction("Player/Jump", throwIfNotFound: true);
        sprintAction = InputSystem.actions.FindAction("Player/Sprint", throwIfNotFound: true);
    }

    void OnEnable()
    {
        InputSystem.actions.FindActionMap("Player").Enable();
        LockCursor(true);
    }

    void OnDisable() => LockCursor(false);

    void Update()
    {
        HandleCursor();
        Look();
        Move();
    }

    void Look()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        Vector2 look = lookAction.ReadValue<Vector2>();
        // Mouse delta is already per-frame; a stick is a rate, so scale it by time.
        bool fromMouse = lookAction.activeControl?.device is Pointer;
        Vector2 delta = fromMouse ? look * mouseSensitivity : look * gamepadLookSpeed * Time.deltaTime;

        transform.Rotate(0f, delta.x, 0f);
        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        if (cameraPivot != null)
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void Move()
    {
        Vector2 input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        float speed = (sprintAction.IsPressed() ? sprintSpeed : walkSpeed) * Mathf.Max(0f, SpeedMultiplier);
        Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * speed;

        if (controller.isGrounded)
        {
            // Small downward push keeps the controller snapped to slopes and steps.
            if (verticalVelocity < 0f) verticalVelocity = -2f;
            if (jumpAction.WasPressedThisFrame() && SpeedMultiplier > 0f) // no jumping while held / down
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontal + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    void HandleCursor()
    {
        // Escape frees the mouse; clicking back into the game recaptures it.
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            LockCursor(false);
        else if (Cursor.lockState != CursorLockMode.Locked &&
                 Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            LockCursor(true);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
