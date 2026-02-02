using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SERVER-ONLY: Authoritative character controller simulation.
/// Receives input from owner, runs physics, updates PlayerState.
/// NO animation, NO IK, NO presentation logic.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerMotorServer : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private MovementSettings settings;

    [Header("Aim Limits")]
    [SerializeField] private float pitchMin = -80f;
    [SerializeField] private float pitchMax = 80f;

    [Header("Reconciliation")]
    [Tooltip("Send corrections to owner every N frames (0 = every frame)")]
    [SerializeField] private int reconcileInterval = 2;

    private CharacterController _cc;
    private PlayerState _state;

    // Latest input from owner
    private PlayerInputSnapshot _input;

    // Simulation state
    private Vector3 _horizVel;
    private float _verticalVel;

    // Reconciliation tracking
    private int _frameCounter;

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
        // Only run on server
        if (!IsServer)
        {
            enabled = false;
            return;
        }

        // Initialize state
        _state.BodyRotation.Value = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        _state.AimYaw.Value = transform.eulerAngles.y;
    }

    // ============================================================
    // INPUT API (called via RPC from owner)
    // ============================================================

    /// <summary>
    /// Server receives input snapshot from owning client.
    /// </summary>
    public void SetInput(PlayerInputSnapshot input)
    {
        if (!IsServer) return;
        _input = input;
    }

    // ============================================================
    // SIMULATION (runs every frame on server)
    // ============================================================

    private void Update()
    {
        if (!IsServer) return;
        if (settings == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Run physics simulation
        Simulate(dt);

        // Update network state
        UpdateState();

        // Send reconciliation to owner
        SendReconciliation();
    }

    private void Simulate(float dt)
    {
        // Clamp and store aim angles
        float aimYaw = _input.AimYaw;
        float aimPitch = Mathf.Clamp(_input.AimPitch, pitchMin, pitchMax);

        // Determine speed based on movement mode
        float targetSpeed = settings.walkSpeed;
        if (_input.Crouch)
            targetSpeed = settings.crouchSpeed;
        else if (_input.Sprint)
            targetSpeed = settings.sprintSpeed;

        // Calculate desired velocity in world space
        Vector2 moveInput = Vector2.ClampMagnitude(_input.Move, 1f);
        Quaternion yawRot = Quaternion.Euler(0f, aimYaw, 0f);
        Vector3 desiredVel = yawRot * new Vector3(moveInput.x, 0f, moveInput.y) * targetSpeed;

        // Ground check
        bool grounded = _cc.isGrounded;

        // Horizontal acceleration
        bool hasInput = moveInput.sqrMagnitude > 0.01f;
        float accel = grounded
            ? (hasInput ? settings.groundAccel : settings.groundDecel)
            : (hasInput ? settings.airAccel : settings.airDecel);

        float maxDelta = accel * dt;
        _horizVel = Vector3.MoveTowards(
            _horizVel,
            hasInput ? desiredVel : Vector3.zero,
            maxDelta);

        // Vertical physics
        if (grounded)
        {
            if (_verticalVel < 0f)
                _verticalVel = -1f;

            if (_input.Jump)
                _verticalVel = settings.jumpSpeed;
        }

        _verticalVel += settings.gravity * dt;

        // Apply movement
        Vector3 velocity = _horizVel + Vector3.up * _verticalVel;
        _cc.Move(velocity * dt);
    }

    private void UpdateState()
    {
        // Movement
        _state.MoveInput.Value = _input.Move;
        _state.Speed.Value = _horizVel.magnitude;

        // Physics
        _state.IsGrounded.Value = _cc.isGrounded;
        _state.IsJumping.Value = _verticalVel > 1f;

        // Modes
        _state.IsCrouching.Value = _input.Crouch;
        _state.IsSprinting.Value = _input.Sprint;

        // Aim
        _state.AimYaw.Value = _input.AimYaw;
        _state.AimPitch.Value = Mathf.Clamp(_input.AimPitch, pitchMin, pitchMax);

        // Body rotation (for animation)
        _state.BodyRotation.Value = Quaternion.Euler(0f, _input.AimYaw, 0f);
    }

    private void SendReconciliation()
    {
        // Skip if not owner's server instance
        if (!IsOwner) return;

        // Throttle reconciliation messages
        _frameCounter++;
        if (reconcileInterval > 0 && _frameCounter % reconcileInterval != 0)
            return;

        // Send correction to owner
        SendReconciliationRpc(
            _input.Tick,
            transform.position,
            _verticalVel);
    }

    [Rpc(SendTo.Owner)]
    private void SendReconciliationRpc(int tick, Vector3 position, float verticalVel)
    {
        // Received by PlayerMotorClient on owning client
        var client = GetComponent<PlayerMotorClient>();
        if (client != null)
            client.ApplyServerCorrection(tick, position, verticalVel);
    }

    // ============================================================
    // PUBLIC API (for other server systems)
    // ============================================================

    /// <summary>Current aim direction for weapon raycasts</summary>
    public Vector3 AimDirection => _state.AimDirection;

    /// <summary>Is player currently firing?</summary>
    public bool IsFiring => _input.Fire;
}