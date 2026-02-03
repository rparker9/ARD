using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SERVER-AUTHORITATIVE physics body that supports player interaction modes (carry/drag/throw).
///
/// - Server simulates Rigidbody and replicates via NetworkTransform.
/// - Clients keep Rigidbody kinematic (no local simulation).
/// - A player can "hold" the body in a mode (Carry or Drag) and send a desired target point.
/// - Server applies a spring+damper toward that target point.
/// - Contention is handled by HolderClientId (only one holder at a time).
///
/// NOTE:
/// - This does NOT handle third-person visuals or hand gripping. (That can hook into PlayerState.HeldObject later.)
/// - Requires: NetworkObject + Rigidbody + Collider + NetworkTransform on the same GameObject (or parent for Collider).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public sealed class NetworkInteractableBody : NetworkBehaviour
{
    public enum HoldMode : byte
    {
        None = 0,
        Carry = 1,
        Drag = 2
    }

    [Serializable]
    private struct HoldTuning
    {
        [Header("Spring Motion")]
        public float positionSpring;
        public float positionDamping;
        public float maxAccel;

        [Header("Break / Safety")]
        public float breakDistance;

        [Header("Rigidbody While Held")]
        public bool disableGravity;
        public float heldDrag;
        public float heldAngularDrag;
    }

    [Header("Hold Tuning")]
    [SerializeField]
    private HoldTuning carryTuning = new HoldTuning
    {
        positionSpring = 180f,
        positionDamping = 22f,
        maxAccel = 60f,
        breakDistance = 3.0f,
        disableGravity = true,
        heldDrag = 8f,
        heldAngularDrag = 8f
    };

    [SerializeField]
    private HoldTuning dragTuning = new HoldTuning
    {
        positionSpring = 90f,
        positionDamping = 18f,
        maxAccel = 40f,
        breakDistance = 3.5f,
        disableGravity = false,
        heldDrag = 3f,
        heldAngularDrag = 3f
    };

    private Rigidbody _rb;

    // Who currently holds this body (ulong.MaxValue = nobody)
    public readonly NetworkVariable<ulong> HolderClientId = new(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Current hold mode (None/Carry/Drag)
    public readonly NetworkVariable<HoldMode> CurrentHoldMode = new(
        HoldMode.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Server-only target
    private Vector3 _targetPos;
    private bool _hasTarget;

    // Cached original RB state (server only)
    private bool _origUseGravity;
    private float _origDrag;
    private float _origAngularDrag;

    public bool IsHeld => HolderClientId.Value != ulong.MaxValue;
    public float Mass => _rb != null ? _rb.mass : 0f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        // We rely on server authority for physics.
        if (!IsServer)
        {
            if (_rb != null)
                _rb.isKinematic = true;
        }
        else
        {
            if (_rb != null)
            {
                _rb.isKinematic = false;
                CacheOriginalRbState();
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            RestoreOriginalRbState();
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (_rb == null) return;
        if (!IsHeld) return;
        if (!_hasTarget) return;

        HoldTuning tuning = GetActiveTuning();

        // Break/drop if too far away (prevents tethering across the map).
        float dist = Vector3.Distance(_rb.position, _targetPos);
        if (dist > tuning.breakDistance)
        {
            ServerDrop();
            return;
        }

        // PD controller (spring + damping) as an acceleration.
        Vector3 posError = _targetPos - _rb.position;
        Vector3 accel = posError * tuning.positionSpring + (-_rb.linearVelocity) * tuning.positionDamping;

        // Clamp acceleration.
        float mag = accel.magnitude;
        if (mag > tuning.maxAccel)
            accel = accel * (tuning.maxAccel / mag);

        _rb.AddForce(accel, ForceMode.Acceleration);
    }

    // ============================================================
    // SERVER API
    // ============================================================

    public bool ServerTryStartHold(ulong holderClientId, Vector3 initialTargetPos, HoldMode mode)
    {
        if (!IsServer) return false;
        if (_rb == null) return false;
        if (mode == HoldMode.None) return false;

        if (IsHeld)
            return false;

        HolderClientId.Value = holderClientId;
        CurrentHoldMode.Value = mode;

        _targetPos = initialTargetPos;
        _hasTarget = true;

        ApplyHeldRbState(GetActiveTuning());
        return true;
    }

    public void ServerUpdateTarget(ulong holderClientId, Vector3 targetPos)
    {
        if (!IsServer) return;
        if (!IsHeld) return;
        if (HolderClientId.Value != holderClientId) return;

        _targetPos = targetPos;
        _hasTarget = true;
    }

    public void ServerSetHoldMode(ulong holderClientId, HoldMode mode)
    {
        if (!IsServer) return;
        if (!IsHeld) return;
        if (HolderClientId.Value != holderClientId) return;

        if (mode == HoldMode.None)
        {
            ServerDrop();
            return;
        }

        CurrentHoldMode.Value = mode;
        ApplyHeldRbState(GetActiveTuning());
    }

    public void ServerDrop()
    {
        if (!IsServer) return;

        HolderClientId.Value = ulong.MaxValue;
        CurrentHoldMode.Value = HoldMode.None;
        _hasTarget = false;

        RestoreOriginalRbState();
    }

    public void ServerThrow(ulong holderClientId, Vector3 impulse)
    {
        if (!IsServer) return;
        if (!IsHeld) return;
        if (HolderClientId.Value != holderClientId) return;
        if (_rb == null) return;

        // Drop first (restores gravity/drag), then apply impulse.
        ServerDrop();
        _rb.AddForce(impulse, ForceMode.Impulse);
        _rb.WakeUp();
    }

    // ============================================================
    // INTERNAL
    // ============================================================

    private HoldTuning GetActiveTuning()
    {
        return CurrentHoldMode.Value switch
        {
            HoldMode.Drag => dragTuning,
            HoldMode.Carry => carryTuning,
            _ => carryTuning
        };
    }

    private void CacheOriginalRbState()
    {
        _origUseGravity = _rb.useGravity;
        _origDrag = _rb.linearDamping;
        _origAngularDrag = _rb.angularDamping;
    }

    private void ApplyHeldRbState(HoldTuning tuning)
    {
        CacheOriginalRbState();

        _rb.useGravity = !tuning.disableGravity ? _origUseGravity : false;
        _rb.linearDamping = tuning.heldDrag;
        _rb.angularDamping = tuning.heldAngularDrag;
        _rb.WakeUp();
    }

    private void RestoreOriginalRbState()
    {
        if (_rb == null) return;

        _rb.useGravity = _origUseGravity;
        _rb.linearDamping = _origDrag;
        _rb.angularDamping = _origAngularDrag;
    }
}
