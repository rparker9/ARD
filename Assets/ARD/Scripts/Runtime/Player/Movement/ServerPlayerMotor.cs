using Unity.Netcode;
using UnityEngine;


/// <summary>
/// Server-authoritative CharacterController movement.
/// Receives input snapshots from owning client via PlayerInputRelay.
/// Uses absolute aim yaw/pitch for authoritative movement + weapon raycasts.
/// Replicates body yaw to non-owners and sends reconciliation to owner.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class ServerPlayerMotor : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private MovementSettings movementSettings;

    // This is the transform that represents the player's view (for yaw rotation).
    [SerializeField] private Transform viewTransform;

    [Header("Aim Clamp (Server)")]
    [SerializeField] private float pitchMin = -80f;
    [SerializeField] private float pitchMax = 80f;

    [Header("Presentation")]
    [Tooltip("Yaw pivot used for non-owners to see body rotation (owner drives locally).")]
    [SerializeField] private Transform yawPivot;

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

    // Replicate body yaw to everyone (for non-owner presentation)
    private readonly NetworkVariable<float> _netBodyYaw = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        _cc = GetComponent<CharacterController>();

        if (IsServer)
        {
            float initialYaw = yawPivot != null ? yawPivot.eulerAngles.y : transform.eulerAngles.y;
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

        _netBodyYaw.Value = _aimYaw;
    }

    private void Update()
    {
        if (!IsServer) return;
        if (_cc == null) return;
        if (movementSettings == null) return;

        float dt = Time.deltaTime;

        Simulate(dt);

        // Update view transform rotation
        if (viewTransform != null)
        {
            // Apply absolute aim rotation
            viewTransform.rotation = Quaternion.Euler(_aimPitch, _aimYaw, 0f);
        }

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
        // Non-owner presentation: apply replicated yaw so other clients see body turning.
        if (!IsClient) return;
        if (IsOwner) return;

        if (yawPivot != null)
            yawPivot.rotation = Quaternion.Euler(0f, _netBodyYaw.Value, 0f);
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
