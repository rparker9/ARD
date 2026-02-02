using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-auth replicated reference to the object currently held by the player.
/// This is network-stable (unlike GetInstanceID) and resolves instantly on clients.
/// </summary>
[DisallowMultipleComponent]
public sealed class NetworkHeldItemState : NetworkBehaviour
{
    // Networked identity of the held object.
    private readonly NetworkVariable<NetworkObjectReference> _heldObjectRef = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private GrippableObject _cachedGrippable;

    public GrippableObject HeldGrippable => _cachedGrippable;

    public override void OnNetworkSpawn()
    {
        _heldObjectRef.OnValueChanged += OnHeldChanged;
        ResolveHeld(_heldObjectRef.Value);
    }

    public override void OnNetworkDespawn()
    {
        _heldObjectRef.OnValueChanged -= OnHeldChanged;
        _cachedGrippable = null;
    }

    private void OnHeldChanged(NetworkObjectReference prev, NetworkObjectReference curr)
    {
        ResolveHeld(curr);
    }

    private void ResolveHeld(NetworkObjectReference r)
    {
        _cachedGrippable = null;

        if (!r.TryGet(out NetworkObject netObj) || netObj == null)
            return;

        _cachedGrippable = netObj.GetComponent<GrippableObject>();
    }

    /// <summary>
    /// Server-only: set held object. Call this from your pickup / equip system.
    /// </summary>
    public void SetHeldObjectServer(NetworkObject held)
    {
        if (!IsServer) return;

        _heldObjectRef.Value = held != null
            ? new NetworkObjectReference(held)
            : default;

        ResolveHeld(_heldObjectRef.Value); // keep server cache consistent too
    }

    /// <summary>
    /// Server-only convenience: clear held object.
    /// </summary>
    public void ClearHeldObjectServer()
    {
        if (!IsServer) return;
        _heldObjectRef.Value = default;
        _cachedGrippable = null;
    }
}
