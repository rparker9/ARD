using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Owner-only client-side prediction for CharacterController movement.
/// Runs in Update for smooth feel.
/// Receives authoritative corrections from server via ApplyServerState.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class ClientPredictedMotor : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private MovementSettings movementSettings;

    [Header("Reconciliation")]
    [Tooltip("If error exceeds this, hard snap to server.")]
    [SerializeField] private float snapDistance = 0.75f;

    [Tooltip("Smaller error gets smoothed out with this rate (1/sec). Try 10-25.")]
    [SerializeField] private float correctionRate = 18f;

    private CharacterController _cc;

    // Predicted state
    private Vector3 _horizVel;
    private float _verticalVel;

    // Latest input snapshot from PlayerInputRelay
    private Vector2 _move;
    private bool _jump;
    private bool _sprint;
    private float _aimYaw;

    // Reconciliation
    private int _lastServerTick = -1;
    private Vector3 _pendingCorrection;

    public override void OnNetworkSpawn()
    {
        _cc = GetComponent<CharacterController>();
    }

    /// <summary>
    /// Called by PlayerInputRelay each frame for the owner (client prediction).
    /// </summary>
    public void SetLocalInput(Vector2 move, float aimYaw, bool jump, bool sprint)
    {
        _move = Vector2.ClampMagnitude(move, 1f);
        _aimYaw = aimYaw;
        _jump = jump;
        _sprint = sprint;
    }

    private void Update()
    {
        // Only predict on remote clients. Host/server uses authoritative simulation.
        if (!IsOwner || IsServer) return;
        if (_cc == null) return;
        if (movementSettings == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Simulate(dt);
        ApplyPendingCorrection(dt);
    }

    private void Simulate(float dt)
    {
        float speed = _sprint ? movementSettings.sprintSpeed : movementSettings.walkSpeed;

        // Desired horizontal velocity in world space based on aim yaw.
        Quaternion yawRot = Quaternion.Euler(0f, _aimYaw, 0f);
        Vector3 desired = yawRot * new Vector3(_move.x, 0f, _move.y);
        desired *= speed;

        bool grounded = _cc.isGrounded;

        float accel = grounded ? movementSettings.groundAccel : movementSettings.airAccel;
        float decel = grounded ? movementSettings.groundDecel : movementSettings.airDecel;

        // If no input, decelerate toward zero; otherwise accelerate toward desired.
        bool hasInput = _move.sqrMagnitude > 0.0001f;
        float maxDelta = (hasInput ? accel : decel) * dt;
        _horizVel = Vector3.MoveTowards(_horizVel, hasInput ? desired : Vector3.zero, maxDelta);

        // Vertical
        if (grounded)
        {
            if (_verticalVel < 0f) _verticalVel = -1f;
            if (_jump) _verticalVel = movementSettings.jumpSpeed;
        }

        _verticalVel += movementSettings.gravity * dt;

        Vector3 vel = _horizVel + Vector3.up * _verticalVel;
        _cc.Move(vel * dt);
    }

    private void ApplyPendingCorrection(float dt)
    {
        if (_pendingCorrection == Vector3.zero) return;

        // Smooth correction (exponential)
        float t = 1f - Mathf.Exp(-correctionRate * dt);
        Vector3 step = Vector3.Lerp(Vector3.zero, _pendingCorrection, t);

        Teleport(transform.position + step);

        _pendingCorrection -= step;

        // Clamp tiny leftovers
        if (_pendingCorrection.sqrMagnitude < 0.000001f)
            _pendingCorrection = Vector3.zero;
    }

    private void Teleport(Vector3 pos)
    {
        // CharacterController doesn't love direct position edits while enabled.
        bool wasEnabled = _cc.enabled;
        _cc.enabled = false;
        transform.position = pos;
        _cc.enabled = wasEnabled;
    }

    /// <summary>
    /// Server reconciliation hook. Called by ServerPlayerMotor (owner-only RPC).
    /// </summary>
    public void ApplyServerState(int serverTick, Vector3 serverPos, float serverVerticalVel)
    {
        // Ignore out-of-order updates
        if (serverTick <= _lastServerTick) return;
        _lastServerTick = serverTick;

        Vector3 predictedPos = transform.position;
        Vector3 error = serverPos - predictedPos;

        if (error.magnitude >= snapDistance)
        {
            Teleport(serverPos);
            _pendingCorrection = Vector3.zero;
        }
        else
        {
            // Smooth out small drift over a few frames
            _pendingCorrection += error;
        }

        // Keep vertical velocity aligned to avoid bounce/drift after correction
        _verticalVel = serverVerticalVel;
    }

    /// <summary>For debugging/telemetry if needed.</summary>
    public float PredictedVerticalVel => _verticalVel;
}
