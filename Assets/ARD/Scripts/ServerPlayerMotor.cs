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
    public void SetInput(int tick, Vector2 move, float aimYaw, float aimPitch, bool jump, bool sprint, bool fire)
    {
        if (!IsServer) return;

        _tick = tick;
        _move = Vector2.ClampMagnitude(move, 1f);
        _aimYaw = aimYaw;
        _aimPitch = Mathf.Clamp(aimPitch, pitchMin, pitchMax);
        _jump = jump;
        _sprint = sprint;
        _fire = fire;

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
        float speed = _sprint ? movementSettings.sprintSpeed : movementSettings.walkSpeed;

        Quaternion yawRot = Quaternion.Euler(0f, _aimYaw, 0f);
        Vector3 desired = yawRot * new Vector3(_move.x, 0f, _move.y);
        desired *= speed;

        bool grounded = _cc.isGrounded;

        float accel = grounded ? movementSettings.groundAccel : movementSettings.airAccel;
        float decel = grounded ? movementSettings.groundDecel : movementSettings.airDecel;

        bool hasInput = _move.sqrMagnitude > 0.0001f;
        float maxDelta = (hasInput ? accel : decel) * dt;
        _horizVel = Vector3.MoveTowards(_horizVel, hasInput ? desired : Vector3.zero, maxDelta);

        if (grounded)
        {
            if (_verticalVel < 0f) _verticalVel = -1f;
            if (_jump) _verticalVel = movementSettings.jumpSpeed;
        }

        _verticalVel += movementSettings.gravity * dt;

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
