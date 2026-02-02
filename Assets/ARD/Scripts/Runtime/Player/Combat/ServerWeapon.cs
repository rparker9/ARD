using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative hitscan weapon.
/// Server validates cadence and applies damage. Clients receive cosmetic FX via RPC.
/// </summary>
public sealed class ServerWeapon : NetworkBehaviour
{
    [SerializeField] private float fireCooldown = 0.2f;
    [SerializeField] private float range = 200f;

    [Header("Server Aim")]
    [SerializeField] private Transform aimOrigin;

    private float _nextFireTime;

    public void TryFireServer()
    {
        if (!IsServer) return;

        if (Time.time < _nextFireTime) 
            return;

        _nextFireTime = Time.time + fireCooldown;

        Vector3 origin = aimOrigin != null
            ? aimOrigin.position
            : transform.position;

        Vector3 dir = transform.forward;

        if (TryGetComponent(out ServerPlayerMotor motor))
            dir = motor.AimDirection;

        if (Physics.Raycast(origin, dir, out var hit, range))
        {
            // Prefer searching up the hierarchy in case collider is on a child.
            var health = hit.collider.GetComponentInParent<NetworkHealth>();
            if (health != null)
                health.ApplyDamage(10);

            // Debug: Draw a line at the hit point with hit plane normal
            Debug.DrawRay(hit.point, hit.normal, Color.red, 1f);
        }

        FireFxRpc(origin, dir);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void FireFxRpc(Vector3 origin, Vector3 dir)
    {
        // Cosmetic only
        if (!IsServer) return;
        Debug.DrawRay(origin, dir * range, Color.green, 0.5f);
    }
}
