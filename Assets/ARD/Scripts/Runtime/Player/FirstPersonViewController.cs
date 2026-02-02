using Unity.Netcode;
using UnityEngine;

/// <summary>
/// OWNER-ONLY: Manages first-person view model (FPS arms + weapon).
/// Completely separate from the third-person character model.
/// Handles weapon positioning, sway, recoil, etc.
/// </summary>
[RequireComponent(typeof(PlayerState))]
public sealed class FirstPersonViewController : NetworkBehaviour
{
    [Header("View Model")]
    [Tooltip("Root of FPS arms rig (separate from third-person model)")]
    [SerializeField] private Transform viewModelRoot;

    [Tooltip("Camera position for view model positioning")]
    [SerializeField] private Transform cameraTransform;

    [Header("Weapon Position")]
    [SerializeField] private Vector3 weaponPositionOffset = new Vector3(0.2f, -0.15f, 0.4f);
    [SerializeField] private Vector3 weaponRotationOffset = Vector3.zero;

    [Header("Weapon Bob")]
    [SerializeField] private bool enableWeaponBob = true;
    [SerializeField] private float bobAmount = 0.02f;
    [SerializeField] private float bobSpeed = 14f;

    [Header("Weapon Sway")]
    [SerializeField] private bool enableWeaponSway = true;
    [SerializeField] private float swayAmount = 0.02f;
    [SerializeField] private float swaySmooth = 6f;

    [Header("Recoil")]
    [SerializeField] private float recoilAmount = 0.05f;
    [SerializeField] private float recoilRecoverySpeed = 10f;

    [Header("Visibility")]
    [Tooltip("Renderers to show only for owner")]
    [SerializeField] private Renderer[] viewModelRenderers;

    private PlayerState _state;
    private PlayerCameraController _camera;

    // Weapon effects
    private Vector3 _basePosition;
    private Vector3 _targetPosition;
    private Vector3 _currentRecoil;
    private float _bobTimer;

    // Sway
    private Vector3 _swayPosition;
    private Vector2 _lastLookDelta;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _state = GetComponent<PlayerState>();
        _camera = GetComponent<PlayerCameraController>();
    }

    public override void OnNetworkSpawn()
    {
        // Only run for owner
        if (!IsOwner)
        {
            // Hide view model from other players
            SetViewModelVisible(false);
            enabled = false;
            return;
        }

        // Show view model for owner
        SetViewModelVisible(true);

        // Initialize base position
        if (viewModelRoot != null)
        {
            _basePosition = viewModelRoot.localPosition;
            _targetPosition = _basePosition;
        }
    }

    // ============================================================
    // UPDATE
    // ============================================================

    private void LateUpdate()
    {
        if (!IsOwner) return;
        if (viewModelRoot == null) return;
        if (cameraTransform == null) return;

        // Calculate weapon position
        Vector3 finalPosition = _basePosition + weaponPositionOffset;

        // Add bob
        if (enableWeaponBob)
            finalPosition += CalculateWeaponBob();

        // Add sway
        if (enableWeaponSway)
            finalPosition += CalculateWeaponSway();

        // Add recoil
        finalPosition += _currentRecoil;

        // Recover from recoil
        _currentRecoil = Vector3.Lerp(_currentRecoil, Vector3.zero, Time.deltaTime * recoilRecoverySpeed);

        // Apply position
        viewModelRoot.localPosition = finalPosition;

        // Apply rotation offset
        viewModelRoot.localRotation = Quaternion.Euler(weaponRotationOffset);
    }

    private Vector3 CalculateWeaponBob()
    {
        if (!_state.IsMoving)
        {
            _bobTimer = 0f;
            return Vector3.zero;
        }

        float speed = _state.Speed.Value;
        float bobMultiplier = speed / 5f; // Normalize to walk speed

        _bobTimer += Time.deltaTime * bobSpeed * bobMultiplier;

        float bobX = Mathf.Cos(_bobTimer) * bobAmount;
        float bobY = Mathf.Sin(_bobTimer * 2f) * bobAmount;

        return new Vector3(bobX, bobY, 0f);
    }

    private Vector3 CalculateWeaponSway()
    {
        // Get look input (if available)
        Vector2 lookDelta = Vector2.zero;

        // Store for next frame
        _lastLookDelta = lookDelta;

        // Calculate sway
        Vector3 targetSway = new Vector3(
            -lookDelta.x * swayAmount,
            -lookDelta.y * swayAmount,
            0f);

        // Smooth sway
        _swayPosition = Vector3.Lerp(_swayPosition, targetSway, Time.deltaTime * swaySmooth);

        return _swayPosition;
    }

    // ============================================================
    // PUBLIC API
    // ============================================================

    /// <summary>
    /// Apply recoil when weapon fires.
    /// </summary>
    public void ApplyRecoil()
    {
        if (!IsOwner) return;

        _currentRecoil += Vector3.back * recoilAmount;
    }

    /// <summary>
    /// Update weapon model when switching weapons.
    /// </summary>
    public void SetWeaponModel(GameObject weaponPrefab)
    {
        if (!IsOwner) return;
        if (viewModelRoot == null) return;

        // Clear existing weapon models
        foreach (Transform child in viewModelRoot)
        {
            if (child.CompareTag("Weapon"))
                Destroy(child.gameObject);
        }

        // Instantiate new weapon
        if (weaponPrefab != null)
        {
            GameObject weapon = Instantiate(weaponPrefab, viewModelRoot);
            weapon.tag = "Weapon";
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;
        }
    }

    /// <summary>
    /// Set visibility of view model (for cutscenes, etc.)
    /// </summary>
    public void SetViewModelVisible(bool visible)
    {
        if (viewModelRenderers == null) return;

        foreach (var renderer in viewModelRenderers)
        {
            if (renderer != null)
                renderer.enabled = visible;
        }
    }

    /// <summary>
    /// Manually set weapon position offset (for ADS, etc.)
    /// </summary>
    public void SetWeaponOffset(Vector3 position, Vector3 rotation)
    {
        weaponPositionOffset = position;
        weaponRotationOffset = rotation;
    }
}