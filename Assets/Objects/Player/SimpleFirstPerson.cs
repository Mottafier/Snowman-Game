using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class CursorLock : MonoBehaviour
{
    

    void Update()
    {
        
    }
}

    [RequireComponent(typeof(CharacterController))]
public class SimpleFirstPerson : MonoBehaviour
{
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Transform head;

    private PlayerSnow playerSnow;

    public float moveSpeed = 3.5f;
    public float jumpHeight = 1.2f;
    public float snowJumpHeight = 3f; // meters gained by an air jump that spends one snow

    private float slowMultiplier = 1f;
    private float slowedUntil;
    public float gravity = -9.81f;
    public float lookSensitivity = 0.4f;

    private CharacterController controller;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private float yVelocity;
    private float pitch;
    private float yaw;
    private float headYaw;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerSnow = GetComponent<PlayerSnow>();
    }

    void Start()
    {
        // lock & hide cursor when game starts
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Input System events
    public void OnMove(InputValue value) => moveInput = value.Get<Vector2>();
    public void OnLook(InputValue value) => lookInput = value.Get<Vector2>();
    public void OnJump(InputValue value)
    {
        if (!value.isPressed) return;

        if (controller.isGrounded) Jump();
        else if (playerSnow.SnowAmount >= 1) SnowJump();
    }
    public void Jump()
    {
        yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    // Mid-air jump that spends one snow and launches the player snowJumpHeight meters up
    public void SnowJump()
    {
        playerSnow.SpendSnow(1);

        // Vertical movement is scaled by the current move speed too, so divide it back out to get a consistent height
        yVelocity = Mathf.Sqrt(snowJumpHeight * -2f * gravity / CurrentMoveSpeed());
    }

    private float CurrentMoveSpeed()
    {
        var speed = moveSpeed;
        if (playerSnow.isCollecting)
        {
            speed *= 0.3f;
        }

        speed *= Mathf.Lerp(0.3f, 1f, playerSnow.SnowAmount / 6f);

        if (Time.time < slowedUntil)
        {
            speed *= slowMultiplier;
        }
        return speed;
    }

    // Temporarily slows the player, e.g. when hit by an enemy with the "slows" ability
    public void ApplySlow(float multiplier, float seconds)
    {
        slowMultiplier = multiplier;
        slowedUntil = Mathf.Max(slowedUntil, Time.time + seconds);
    }
    void Update()
    {
        // Look
        float dx = lookInput.x * lookSensitivity;
        float dy = lookInput.y * lookSensitivity;

        headYaw += dx;

        transform.Rotate(Vector3.up * dx);

        pitch = Mathf.Clamp(pitch - dy, -80f, 80f);
        if (head) head.localRotation = Quaternion.Euler(pitch, 0, 0);
        if (playerCamera) playerCamera.localRotation = Quaternion.Euler(0, yaw, 0);
        // Movement
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;

        var moveSpeedCurrent = CurrentMoveSpeed();

        yVelocity += gravity * Time.deltaTime;
        if (controller.isGrounded && yVelocity < 0) yVelocity = -2f;
        move.y = yVelocity;

        controller.Move(move * moveSpeedCurrent * Time.deltaTime);

        // unlock & show cursor on Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // re-lock with left click (optional)
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}