using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Drop this on a NetworkObject to make it "usable".
/// The PlayerInteractionController discovers these and asks the SERVER to execute them.
/// </summary>
[DisallowMultipleComponent]
public abstract class ServerUsable : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private string verb = "Use";
    [SerializeField] private int priority = 0;

    public string Verb => verb;
    public int Priority => priority;

    /// <summary>
    /// Client-side UI filter (NOT authoritative).
    /// </summary>
    public virtual bool CanUseLocal(PlayerInteractionController interactor) => true;

    /// <summary>
    /// Server-side authorization gate (authoritative).
    /// </summary>
    public virtual bool CanUseServer(PlayerInteractionController interactor, ulong clientId) => true;

    /// <summary>
    /// Called by the server when a player uses this.
    /// </summary>
    public void ServerUse(PlayerInteractionController interactor, ulong clientId)
    {
        if (!IsServer) return;
        if (!CanUseServer(interactor, clientId)) return;
        OnServerUse(interactor, clientId);
    }

    protected abstract void OnServerUse(PlayerInteractionController interactor, ulong clientId);
}

