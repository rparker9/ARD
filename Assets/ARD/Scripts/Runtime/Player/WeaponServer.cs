using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SERVER-ONLY: Authoritative weapon logic.
/// Validates firing, performs raycasts, applies damage.
/// Broadcasts effects to all clients.
/// </summary>
[RequireComponent(typeof(PlayerState))]
public sealed class WeaponServer : NetworkBehaviour
{
    [Header("Weapon Stats")]
    [SerializeField] private float fireRate = 0.2f;
    [SerializeField] private float range = 200f;
    [SerializeField] private int damage = 10;

    [Header("View Position")]
    [Tooltip("Where bullets originate (should be near camera for owner)")]
    [SerializeField] private Transform viewPosition;

    [Header("Effects")]
    [Tooltip("Prefab to spawn at hit point (bullet hole, impact effect, etc.)")]
    [SerializeField] private GameObject hitEffectPrefab;

    [Tooltip("Layer mask for raycasts")]
    [SerializeField] private LayerMask hitMask = ~0;

    private PlayerState _state;
    private PlayerMotorServer _motor;
    private float _nextFireTime;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _state = GetComponent<PlayerState>();
        _motor = GetComponent<PlayerMotorServer>();
    }

    public override void OnNetworkSpawn()
    {
        // Only run on server
        if (!IsServer)
        {
            enabled = false;
            return;
        }
    }

    // ============================================================
    // FIRING (server-only)
    // ============================================================

    private void Update()
    {
        if (!IsServer) return;

        // Check if player is trying to fire
        if (_motor != null && _motor.IsFiring)
        {
            TryFire();
        }
    }

    private void TryFire()
    {
        // Rate limiting
        if (Time.time < _nextFireTime)
            return;

        _nextFireTime = Time.time + fireRate;

        // Get aim origin and direction
        Vector3 origin = viewPosition != null
            ? viewPosition.position
            : transform.position + Vector3.up * 1.6f; // Approx head height

        Vector3 direction = _state.AimDirection;

        // Perform raycast
        bool hit = Physics.Raycast(
            origin,
            direction,
            out RaycastHit hitInfo,
            range,
            hitMask,
            QueryTriggerInteraction.Ignore);

        if (hit)
        {
            // Apply damage
            ApplyDamage(hitInfo);

            // Broadcast hit effect
            BroadcastHitRpc(hitInfo.point, hitInfo.normal);
        }

        // Broadcast fire effect (muzzle flash, sound, etc.)
        BroadcastFireRpc(origin, direction);
    }

    private void ApplyDamage(RaycastHit hit)
    {
        // Search for NetworkHealth component (prefer parent search)
        var health = hit.collider.GetComponentInParent<NetworkHealth>();

        if (health != null)
        {
            health.ApplyDamage(damage);
            Debug.Log($"[WeaponServer] Hit {hit.collider.name} for {damage} damage");
        }
    }

    // ============================================================
    // EFFECT BROADCASTING (to all clients)
    // ============================================================

    [Rpc(SendTo.ClientsAndHost)]
    private void BroadcastFireRpc(Vector3 origin, Vector3 direction)
    {
        // Owner client: apply recoil to FPS view
        if (IsOwner)
        {
            var fpsView = GetComponent<FirstPersonViewController>();
            if (fpsView != null)
                fpsView.ApplyRecoil();
        }

        // All clients: play muzzle flash, sound, etc.
        PlayFireEffects(origin, direction);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void BroadcastHitRpc(Vector3 position, Vector3 normal)
    {
        // All clients: spawn hit effect
        PlayHitEffect(position, normal);
    }

    // ============================================================
    // CLIENT EFFECTS (runs on all clients)
    // ============================================================

    private void PlayFireEffects(Vector3 origin, Vector3 direction)
    {
        // TODO: Implement
        // - Play muzzle flash
        // - Play fire sound
        // - Spawn shell casing
        // - Draw tracer line (Debug.DrawRay for now)

        Debug.DrawRay(origin, direction * range, Color.yellow, 0.1f);
    }

    private void PlayHitEffect(Vector3 position, Vector3 normal)
    {
        // Spawn hit effect prefab
        if (hitEffectPrefab != null)
        {
            GameObject effect = Instantiate(
                hitEffectPrefab,
                position,
                Quaternion.LookRotation(normal));

            // Auto-destroy after a few seconds
            Destroy(effect, 2f);
        }

        // Debug visualization
        Debug.DrawRay(position, normal * 0.5f, Color.red, 1f);
    }
}