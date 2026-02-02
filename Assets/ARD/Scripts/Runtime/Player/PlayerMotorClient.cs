using Unity.Netcode;
using UnityEngine;

/// <summary>
/// CLIENT-ONLY (Owner): Local prediction for responsive movement.
/// Mirrors server simulation exactly, applies corrections when received.
/// Only runs for the local player.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerMotorClient : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private MovementSettings settings;

    [Header("Reconciliation")]
    [Tooltip("Error larger than this = hard snap to server")]
    [SerializeField] private float snapThreshold = 0.5f;

    [Tooltip("Smooth out small errors at this rate (units/sec)")]
    [SerializeField] private float correctionSpeed = 15f;

    [Header("Thresholds")]
    [Tooltip("Speed below this is snapped to zero (must match server)")]
    [SerializeField] private float speedEpsilon = 0.01f;

    [Tooltip("Input magnitude below this is snapped to zero (must match server)")]
    [SerializeField] private float inputEpsilon = 0.01f;

    private CharacterController _cc;
    private PlayerState _state;

    // Local prediction state
    private Vector2 _moveInput;
    private float _aimYaw;
    private bool _jump;
    private bool _sprint;
    private bool _crouch;

    private Vector3 _horizVel;
    private float _verticalVel;

    // Reconciliation
    private int _lastServerTick = -1;
    private Vector3 _correctionOffset;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _state = GetComponent<PlayerState>();
    }

    public override void OnNetworkSpawn()
    {
        // Only run for owner on client (not host)
        if (!IsOwner || IsServer)
        {
            enabled = false;
            return;
        }
    }

    // ============================================================
    // INPUT API (called by PlayerInputController)
    // ============================================================

    /// <summary>
    /// Set local input for this frame's prediction.
    /// </summary>
    public void SetLocalInput(Vector2 move, float aimYaw, bool jump, bool sprint, bool crouch)
    {
        _moveInput = Vector2.ClampMagnitude(move, 1f);

        // THRESHOLD CHECK: Snap tiny input to zero (must match server)
        if (_moveInput.sqrMagnitude < inputEpsilon * inputEpsilon)
            _moveInput = Vector2.zero;

        _aimYaw = aimYaw;
        _jump = jump;
        _sprint = sprint;
        _crouch = crouch;
    }

    // ============================================================
    // PREDICTION (runs every frame on client)
    // ============================================================

    private void Update()
    {
        if (!IsOwner || IsServer) return;
        if (settings == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Run prediction (identical to server simulation)
        Simulate(dt);

        // Apply pending corrections smoothly
        ApplyCorrectionSmoothing(dt);
    }

    private void Simulate(float dt)
    {
        // Determine speed based on movement mode
        float targetSpeed = settings.walkSpeed;
        if (_crouch)
            targetSpeed = settings.crouchSpeed;
        else if (_sprint)
            targetSpeed = settings.sprintSpeed;

        // Calculate desired velocity in world space
        Quaternion yawRot = Quaternion.Euler(0f, _aimYaw, 0f);
        Vector3 desiredVel = yawRot * new Vector3(_moveInput.x, 0f, _moveInput.y) * targetSpeed;

        // Ground check
        bool grounded = _cc.isGrounded;

        // Horizontal acceleration (must match server exactly)
        bool hasInput = _moveInput.sqrMagnitude > 0.01f;
        float accel = grounded
            ? (hasInput ? settings.groundAccel : settings.groundDecel)
            : (hasInput ? settings.airAccel : settings.airDecel);

        float maxDelta = accel * dt;
        _horizVel = Vector3.MoveTowards(
            _horizVel,
            hasInput ? desiredVel : Vector3.zero,
            maxDelta);

        // THRESHOLD CHECK: Snap tiny velocity to zero (must match server)
        float horizSpeed = _horizVel.magnitude;
        if (horizSpeed < speedEpsilon)
            _horizVel = Vector3.zero;

        // Vertical physics (must match server exactly)
        if (grounded)
        {
            if (_verticalVel < 0f)
                _verticalVel = -1f;

            if (_jump)
                _verticalVel = settings.jumpSpeed;
        }

        _verticalVel += settings.gravity * dt;

        // Apply movement
        Vector3 velocity = _horizVel + Vector3.up * _verticalVel;
        _cc.Move(velocity * dt);
    }

    private void ApplyCorrectionSmoothing(float dt)
    {
        if (_correctionOffset.sqrMagnitude < 0.0001f) return;

        // Smoothly apply correction over multiple frames
        float t = 1f - Mathf.Exp(-correctionSpeed * dt);
        Vector3 step = Vector3.Lerp(Vector3.zero, _correctionOffset, t);

        // Apply step
        TeleportCharacter(transform.position + step);

        // Reduce remaining correction
        _correctionOffset -= step;

        // Clamp tiny leftovers
        if (_correctionOffset.sqrMagnitude < 0.0001f)
            _correctionOffset = Vector3.zero;
    }

    // ============================================================
    // RECONCILIATION API (called via RPC from server)
    // ============================================================

    /// <summary>
    /// Receive server correction and reconcile prediction.
    /// </summary>
    public void ApplyServerCorrection(int serverTick, Vector3 serverPos, float serverVerticalVel)
    {
        // Ignore out-of-order messages
        if (serverTick <= _lastServerTick) return;
        _lastServerTick = serverTick;

        Vector3 predictedPos = transform.position;
        Vector3 error = serverPos - predictedPos;

        // Large error: hard snap
        if (error.magnitude >= snapThreshold)
        {
            TeleportCharacter(serverPos);
            _correctionOffset = Vector3.zero;

            Debug.LogWarning($"[Prediction] Large error {error.magnitude:F3}m - snapping to server");
        }
        // Small error: smooth correction
        else if (error.magnitude > 0.001f)
        {
            _correctionOffset += error;
        }

        // Always sync vertical velocity to prevent bounce/drift
        _verticalVel = serverVerticalVel;
    }

    private void TeleportCharacter(Vector3 position)
    {
        // CharacterController needs to be disabled during teleport
        bool wasEnabled = _cc.enabled;
        _cc.enabled = false;
        transform.position = position;
        _cc.enabled = wasEnabled;
    }
}