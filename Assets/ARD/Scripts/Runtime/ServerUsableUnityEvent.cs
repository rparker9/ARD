
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Simple usable that invokes a UnityEvent on the server.
/// Great for prototyping buttons/levers/etc.
/// </summary>
public sealed class ServerUsableUnityEvent : ServerUsable
{
    [SerializeField] private UnityEvent onServerUse;

    protected override void OnServerUse(PlayerInteractionController interactor, ulong clientId)
    {
        onServerUse?.Invoke();
    }
}