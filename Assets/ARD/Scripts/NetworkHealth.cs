using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shared server-authoritative health component for any damageable NetworkObject.
/// - Server writes HP
/// - Everyone can read HP
/// - Damage requests should go through server logic (e.g., weapons / hazards)
/// </summary>
public sealed class NetworkHealth : NetworkBehaviour
{
    [SerializeField] private int maxHp = 100;

    public int MaxHp => maxHp;

    public NetworkVariable<int> Hp = new(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsDead => Hp.Value <= 0;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            Hp.Value = maxHp;
    }

    /// <summary>
    /// Server-only: apply damage. Call this from server-authoritative systems.
    /// </summary>
    public void ApplyDamage(int amount)
    {
        if (!IsServer) 
            return;

        if (amount <= 0) return;
        if (IsDead) return;

        Hp.Value = Mathf.Max(0, Hp.Value - amount);

        if (Hp.Value == 0)
            OnDiedServer();
    }

    /// <summary>
    /// Server-only: heal.
    /// </summary>
    public void Heal(int amount)
    {
        if (!IsServer) 
            return;

        if (amount <= 0) return;
        if (IsDead) return;

        Hp.Value = Mathf.Min(maxHp, Hp.Value + amount);
    }

    /// <summary>
    /// Server-only: override later if you want different death behavior.
    /// Prototype default: despawn non-player, keep player for now.
    /// </summary>
    private void OnDiedServer()
    {
        if (!IsServer) return;

        // Prototype policy:
        // - If this is a player object: don't despawn (you may want ragdoll/respawn).
        // - If not: despawn.
        if (TryGetComponent(out NetworkObject netObj))
        {
            if (!netObj.IsPlayerObject)
                netObj.Despawn(true);
        }
    }
}
