using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative CharacterController movement.
/// Receives input snapshots from owning client via PlayerInputRelay.
/// Uses absolute aim yaw/pitch for authoritative movement + weapon raycasts.
/// Replicates body yaw to everyone and rotates ViewModelRoot to face aim direction.
/// 
/// UPDATED: Rotates ViewModelRoot (character model) instead of yawPivot (camera).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class ServerPlayerMotor : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private MovementSettings movementSettings;

    [Header("Aim Clamp (Server)")]
    [SerializeField] private float pitchMin = -80f;
    [SerializeField] private float pitchMax = 80f;

    [Header("Character Model")]
    [Tooltip("The character's visual model root (e.g., ViewModelRoot) - rotates to face aim direction")]
    [SerializeField] private Transform viewModelRoot;

    [Tooltip("How fast the model rotates (degrees/sec). 0 = instant snap.")]
    [SerializeField] private float modelRotationSpeed = 720f;

    private CharacterController _cc;

    // Input snapshot (latest from owner)
    private int _tick;
    private Vector2 _move;
    private bool _jump;
    private bool _sprint;
    private bool _fire;
    private bool _crouch;

    // Absolute aim angles (degrees)
    private float _aimYaw;
    private float _aimPitch;

    // Simulated state
    private Vector3 _horizVel;
    private float _verticalVel;

    // Replicate body yaw to everyone (for non-owner character model rotation)
    private readonly NetworkVariable<float> _netBodyYaw = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Public properties for animation system
    public bool IsCrouching => _crouch;
    public bool IsSprinting => _sprint;
    public bool IsJumping => _verticalVel > 1f;
    public Vector2 MoveInput => _move;
    public float HorizontalSpeed => _horizVel.magnitude;
    public float AimYawDegrees => _aimYaw; // Added for CharacterModelRotation

    public override void OnNetworkSpawn()
    {
        _cc = GetComponent<CharacterController>();

        if (IsServer)
        {
            // Initialize aim yaw from ViewModelRoot or root transform
            float initialYaw = viewModelRoot != null ? viewModelRoot.eulerAngles.y : transform.eulerAngles.y;
            _aimYaw = initialYaw;
            _netBodyYaw.Value = initialYaw;
        }
    }

    public float AimPitchDegrees => _aimPitch;
    public Vector3 AimDirection => Quaternion.Euler(_aimPitch, _aimYaw, 0f) * Vector3.forward;

    /// <summary>
    /// Server receives latest input snapshot from owner.
    /// </summary>
    public void SetInput(PlayerInputSnapshot snapshot)
    {
        if (!IsServer) return;

        _tick = snapshot.Tick;
        _move = Vector2.ClampMagnitude(snapshot.Move, 1f);
        _aimYaw = snapshot.AimYaw;
        _aimPitch = Mathf.Clamp(snapshot.AimPitch, pitchMin, pitchMax);
        _jump = snapshot.Jump;
        _sprint = snapshot.Sprint;
        _fire = snapshot.Fire;
        _crouch = snapshot.Crouch;

        // Update networked body yaw for remote players
        _netBodyYaw.Value = _aimYaw;
    }

    private void Update()
    {
        if (!IsServer) return;
        if (_cc == null) return;
        if (movementSettings == null) return;

        float dt = Time.deltaTime;

        Simulate(dt);

        // Server-authoritative firing
        if (_fire && TryGetComponent(out ServerWeapon weapon))
            weapon.TryFireServer();

        // Reconcile owner (position + vertical velocity + tick)
        SendReconcileRpc(_tick, transform.position, _verticalVel);
    }

    private void Simulate(float dt)
    {
        // Determine target speed based on crouch, sprint, or walk state
        float speed = _crouch ? movementSettings.crouchSpeed : (_sprint ? movementSettings.sprintSpeed : movementSettings.walkSpeed);

        // Calculate desired horizontal velocity in world space based on input and aim yaw
        Quaternion yawRot = Quaternion.Euler(0f, _aimYaw, 0f);          // Yaw only
        Vector3 desired = yawRot * new Vector3(_move.x, 0f, _move.y);   // Local to world
        desired *= speed;                                               // Scale by target speed

        // Grounded check
        bool grounded = _cc.isGrounded;

        // Horizontal velocity acceleration/deceleration
        float accel = grounded ? movementSettings.groundAccel : movementSettings.airAccel;
        float decel = grounded ? movementSettings.groundDecel : movementSettings.airDecel;

        // Move horizontal velocity toward desired or zero based on input
        bool hasInput = _move.sqrMagnitude > 0.0001f;
        float maxDelta = (hasInput ? accel : decel) * dt;
        _horizVel = Vector3.MoveTowards(_horizVel, hasInput ? desired : Vector3.zero, maxDelta);

        if (grounded)
        {
            // Clamp small negative vertical velocity when grounded
            if (_verticalVel < 0f)
                _verticalVel = -1f;

            // If jumping, set vertical velocity
            if (_jump)
                _verticalVel = movementSettings.jumpSpeed;
        }

        // Apply gravity
        _verticalVel += movementSettings.gravity * dt;

        // Move CharacterController using combined velocity (this also handles grounding)
        Vector3 vel = _horizVel + Vector3.up * _verticalVel;
        _cc.Move(vel * dt);
    }

    private void LateUpdate()
    {
        // Rotate ViewModelRoot (character model) to face aim direction
        if (viewModelRoot == null) return;

        float targetYaw;

        // Owner: Use local aim yaw (updated this frame, no network lag)
        if (IsOwner && IsClient)
        {
            // Get yaw from camera controller
            var cameraController = GetComponent<PlayerCameraController>();
            targetYaw = cameraController != null ? cameraController.YawDegrees : _aimYaw;
        }
        // Remote players: Use networked yaw
        else if (!IsOwner && IsClient)
        {
            targetYaw = _netBodyYaw.Value;
        }
        // Server-only (no client): Use server's aim yaw
        else
        {
            targetYaw = _aimYaw;
        }

        // Create target rotation (only Y axis)
        Quaternion targetRotation = Quaternion.Euler(0f, targetYaw, 0f);

        // Apply rotation (smooth or instant)
        if (modelRotationSpeed > 0f)
        {
            viewModelRoot.rotation = Quaternion.RotateTowards(
                viewModelRoot.rotation,
                targetRotation,
                modelRotationSpeed * Time.deltaTime
            );
        }
        else
        {
            viewModelRoot.rotation = targetRotation;
        }
    }

    [Rpc(SendTo.Owner)]
    private void SendReconcileRpc(int tick, Vector3 serverPos, float serverVerticalVel)
    {
        // This runs only on the owning client.
        var predicted = GetComponent<ClientPredictedMotor>();
        if (predicted != null)
            predicted.ApplyServerState(tick, serverPos, serverVerticalVel);
    }
}