using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shared network state for a player. Server writes, everyone reads.
/// This is the ONLY data that flows over the network - no animation, no IK, just state.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerState : NetworkBehaviour
{
    // ============================================================
    // MOVEMENT STATE
    // ============================================================

    /// <summary>Input vector from player (magnitude 0-1)</summary>
    public readonly NetworkVariable<Vector2> MoveInput = new(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Current movement speed (m/s)</summary>
    public readonly NetworkVariable<float> Speed = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Body rotation (yaw only, for character model)</summary>
    public readonly NetworkVariable<Quaternion> BodyRotation = new(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ============================================================
    // PHYSICS STATE
    // ============================================================

    /// <summary>Is character touching ground?</summary>
    public readonly NetworkVariable<bool> IsGrounded = new(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Is character in jump state? (rising with upward velocity)</summary>
    public readonly NetworkVariable<bool> IsJumping = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ============================================================
    // MOVEMENT MODES
    // ============================================================

    /// <summary>Is player crouching?</summary>
    public readonly NetworkVariable<bool> IsCrouching = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Is player sprinting?</summary>
    public readonly NetworkVariable<bool> IsSprinting = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ============================================================
    // AIM STATE
    // ============================================================

    /// <summary>Horizontal aim angle (degrees, world space)</summary>
    public readonly NetworkVariable<float> AimYaw = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Vertical aim angle (degrees, -80 to +80)</summary>
    public readonly NetworkVariable<float> AimPitch = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ============================================================
    // EQUIPMENT STATE
    // ============================================================

    /// <summary>Currently grabbed object (for IK targeting)</summary>
    public readonly NetworkVariable<NetworkObjectReference> GrabbedObject = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ============================================================
    // COMPUTED PROPERTIES
    // ============================================================

    /// <summary>Currently grabbed object's HandGripPoints (resolved from GrabbedObject)</summary>
    private HandGripPoints _cachedGripPoints;
    public HandGripPoints GrabbedGripPoints
    {
        get
        {
            if (_cachedGripPoints == null && GrabbedObject.Value.TryGet(out NetworkObject netObj))
                _cachedGripPoints = netObj != null ? netObj.GetComponent<HandGripPoints>() : null;
            return _cachedGripPoints;
        }
    }

    /// ============================================================
    /// GETTERS
    /// ===========================================================

    /// <summary>World-space aim direction vector</summary>
    public Vector3 AimDirection =>
        Quaternion.Euler(AimPitch.Value, AimYaw.Value, 0f) * Vector3.forward;

    /// <summary>Is player moving? (based on input, not velocity)</summary>
    public bool IsMoving => MoveInput.Value.sqrMagnitude > 0.01f;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    public override void OnNetworkSpawn()
    {
        GrabbedObject.OnValueChanged += OnGrabbedObjectChanged;

        if (IsServer)
        {
            BodyRotation.Value = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }
    }

    public override void OnNetworkDespawn()
    {
        GrabbedObject.OnValueChanged -= OnGrabbedObjectChanged;
    }

    private void OnGrabbedObjectChanged(NetworkObjectReference prev, NetworkObjectReference curr)
    {
        _cachedGripPoints = null; // Force re-resolve
    }

    // ============================================================
    // SERVER-ONLY API
    // ============================================================

    /// <summary>Server: Update grabbed object reference</summary>
    public void SetGrabbedObjectServer(NetworkObject grabbedObj)
    {
        if (!IsServer)
        {
            Debug.LogWarning("SetGrabbedObjectServer called on non-server");
            return;
        }

        GrabbedObject.Value = grabbedObj != null
            ? new NetworkObjectReference(grabbedObj)
            : default;
    }

    /// <summary>Server: Clear grabbed object</summary>
    public void ClearGrabbedObjectServer()
    {
        if (!IsServer)
        {
            Debug.LogWarning("ClearGrabbedObjectServer called on non-server");
            return;
        }

        GrabbedObject.Value = default;
    }
}