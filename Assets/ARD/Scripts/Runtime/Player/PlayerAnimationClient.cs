using Unity.Netcode;
using UnityEngine;

/// <summary>
/// CLIENT-ONLY (Non-Owner): Drives animator based on PlayerState.
/// This component only runs for REMOTE players (what others see).
/// Owner uses separate FPS arm rig.
/// 
/// IMPORTANT: This version properly handles blend tree parameters.
/// - MoveSpeed: Magnitude of movement (0-1 for walk, 1-1.5 for sprint, etc.)
/// - MoveX/MoveY: Direction of movement relative to body (-1 to 1)
/// </summary>
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerAnimationClient : NetworkBehaviour
{
    [Header("Character Model")]
    [Tooltip("Character visual root - rotates to match body rotation")]
    [SerializeField] private Transform characterModelRoot;

    [Header("Animator Parameters")]
    [SerializeField] private string moveSpeedParam = "MoveSpeed";
    [SerializeField] private string moveXParam = "MoveX";
    [SerializeField] private string moveYParam = "MoveY";
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string isJumpingParam = "IsJumping";
    [SerializeField] private string isCrouchingParam = "IsCrouching";
    [SerializeField] private string isSprintingParam = "IsSprinting";

    [Header("Animation Speed Mapping")]
    [Tooltip("How to map actual speed to animation speed")]
    [SerializeField] private float walkSpeedReference = 4.5f; // Should match MovementSettings
    [SerializeField] private float sprintSpeedReference = 6.0f; // Should match MovementSettings
    [SerializeField] private float crouchSpeedReference = 2.5f; // Should match MovementSettings

    [Header("Smoothing")]
    [Tooltip("Smooth speed changes (0 = instant)")]
    [SerializeField] private float speedSmoothTime = 0.1f;

    [Tooltip("Smooth directional input (0 = instant)")]
    [SerializeField] private float inputDampTime = 0.1f;

    [Tooltip("Body rotation speed (0 = instant)")]
    [SerializeField] private float bodyRotationSpeed = 720f;

    [Header("Visibility")]
    [Tooltip("Renderers to hide for owner (FPS view)")]
    [SerializeField] private Renderer[] characterRenderers;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    private Animator _animator;
    private PlayerState _state;

    // Cached parameter hashes
    private int _moveSpeedHash;
    private int _moveXHash;
    private int _moveYHash;
    private int _isGroundedHash;
    private int _isJumpingHash;
    private int _isCrouchingHash;
    private int _isSprintingHash;

    // Smoothed values
    private float _currentSpeed;
    private float _speedVelocity;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        _state = GetComponent<PlayerState>();

        // Cache parameter hashes
        _moveSpeedHash = Animator.StringToHash(moveSpeedParam);
        _moveXHash = Animator.StringToHash(moveXParam);
        _moveYHash = Animator.StringToHash(moveYParam);
        _isGroundedHash = Animator.StringToHash(isGroundedParam);
        _isJumpingHash = Animator.StringToHash(isJumpingParam);
        _isCrouchingHash = Animator.StringToHash(isCrouchingParam);
        _isSprintingHash = Animator.StringToHash(isSprintingParam);
    }

    public override void OnNetworkSpawn()
    {
        // Owner: hide character model (using FPS arms instead)
        if (IsOwner)
        {
            SetRenderersVisible(false);
            enabled = false;
            return;
        }

        // Non-owner: subscribe to state changes
        _state.MoveInput.OnValueChanged += OnMoveInputChanged;
        _state.Speed.OnValueChanged += OnSpeedChanged;
        _state.IsGrounded.OnValueChanged += OnGroundedChanged;
        _state.IsJumping.OnValueChanged += OnJumpingChanged;
        _state.IsCrouching.OnValueChanged += OnCrouchingChanged;
        _state.IsSprinting.OnValueChanged += OnSprintingChanged;

        // Initial sync
        UpdateAllParameters();
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner)
        {
            _state.MoveInput.OnValueChanged -= OnMoveInputChanged;
            _state.Speed.OnValueChanged -= OnSpeedChanged;
            _state.IsGrounded.OnValueChanged -= OnGroundedChanged;
            _state.IsJumping.OnValueChanged -= OnJumpingChanged;
            _state.IsCrouching.OnValueChanged -= OnCrouchingChanged;
            _state.IsSprinting.OnValueChanged -= OnSprintingChanged;
        }
    }

    // ============================================================
    // STATE CHANGE CALLBACKS
    // ============================================================

    private void OnMoveInputChanged(Vector2 prev, Vector2 curr)
    {
        if (_animator == null) return;

        // Convert input to body-relative direction
        // The input is already in the correct space from the server
        Vector2 bodyRelativeInput = CalculateBodyRelativeInput(curr);

        if (showDebugInfo)
        {
            Debug.Log($"[AnimClient] Input: {curr} → BodyRelative: {bodyRelativeInput}");
        }

        // Use dampening for smooth transitions
        _animator.SetFloat(_moveXHash, bodyRelativeInput.x, inputDampTime, Time.deltaTime);
        _animator.SetFloat(_moveYHash, bodyRelativeInput.y, inputDampTime, Time.deltaTime);
    }

    private void OnSpeedChanged(float prev, float curr)
    {
        // Speed smoothing handled in LateUpdate
        if (showDebugInfo)
        {
            Debug.Log($"[AnimClient] Speed changed: {prev} → {curr}");
        }
    }

    private void OnGroundedChanged(bool prev, bool curr)
    {
        if (_animator != null)
            _animator.SetBool(_isGroundedHash, curr);
    }

    private void OnJumpingChanged(bool prev, bool curr)
    {
        if (_animator != null)
            _animator.SetBool(_isJumpingHash, curr);
    }

    private void OnCrouchingChanged(bool prev, bool curr)
    {
        if (_animator != null)
            _animator.SetBool(_isCrouchingHash, curr);
    }

    private void OnSprintingChanged(bool prev, bool curr)
    {
        if (_animator != null)
            _animator.SetBool(_isSprintingHash, curr);
    }

    // ============================================================
    // UPDATE
    // ============================================================

    private void LateUpdate()
    {
        if (IsOwner) return;

        // Smooth speed for blend tree
        UpdateSpeed();

        // Rotate character model to match body rotation
        UpdateBodyRotation();
    }

    private void UpdateSpeed()
    {
        if (_animator == null) return;

        // Get actual speed from state
        float actualSpeed = _state.Speed.Value;

        // Normalize speed based on movement mode
        float normalizedSpeed = 0f;

        if (_state.IsCrouching.Value)
        {
            // Crouch: 0-1 range
            normalizedSpeed = actualSpeed / crouchSpeedReference;
        }
        else if (_state.IsSprinting.Value)
        {
            // Sprint: 1-1.5+ range (or whatever your blend tree uses)
            normalizedSpeed = actualSpeed / walkSpeedReference; // Normalize to walk speed
        }
        else
        {
            // Walk: 0-1 range
            normalizedSpeed = actualSpeed / walkSpeedReference;
        }

        // Clamp to reasonable range
        normalizedSpeed = Mathf.Clamp(normalizedSpeed, 0f, 2f);

        // Smooth transition
        if (speedSmoothTime > 0f)
        {
            _currentSpeed = Mathf.SmoothDamp(
                _currentSpeed,
                normalizedSpeed,
                ref _speedVelocity,
                speedSmoothTime);
        }
        else
        {
            _currentSpeed = normalizedSpeed;
        }

        _animator.SetFloat(_moveSpeedHash, _currentSpeed);

        if (showDebugInfo)
        {
            Debug.Log($"[AnimClient] ActualSpeed: {actualSpeed:F2}, Normalized: {normalizedSpeed:F2}, Current: {_currentSpeed:F2}");
        }
    }

    private void UpdateBodyRotation()
    {
        if (characterModelRoot == null) return;

        Quaternion targetRotation = _state.BodyRotation.Value;

        if (bodyRotationSpeed > 0f)
        {
            characterModelRoot.rotation = Quaternion.RotateTowards(
                characterModelRoot.rotation,
                targetRotation,
                bodyRotationSpeed * Time.deltaTime);
        }
        else
        {
            characterModelRoot.rotation = targetRotation;
        }
    }

    /// <summary>
    /// Convert world-space input to body-relative input for animations.
    /// The server sends input in aim-space (where forward is aim direction).
    /// We need to convert it to body-space for the animator.
    /// </summary>
    private Vector2 CalculateBodyRelativeInput(Vector2 worldInput)
    {
        // The input from server is already in aim-space
        // Since body rotation matches aim yaw, input is already correct
        return worldInput;
    }

    private void UpdateAllParameters()
    {
        if (_animator == null) return;

        // Update directional input
        Vector2 moveInput = _state.MoveInput.Value;
        Vector2 bodyRelativeInput = CalculateBodyRelativeInput(moveInput);

        _animator.SetFloat(_moveXHash, bodyRelativeInput.x, inputDampTime, Time.deltaTime);
        _animator.SetFloat(_moveYHash, bodyRelativeInput.y, inputDampTime, Time.deltaTime);

        // Update bool states
        _animator.SetBool(_isGroundedHash, _state.IsGrounded.Value);
        _animator.SetBool(_isJumpingHash, _state.IsJumping.Value);
        _animator.SetBool(_isCrouchingHash, _state.IsCrouching.Value);
        _animator.SetBool(_isSprintingHash, _state.IsSprinting.Value);

        // Initialize speed
        float actualSpeed = _state.Speed.Value;
        float normalizedSpeed = actualSpeed / walkSpeedReference;
        _currentSpeed = normalizedSpeed;
        _animator.SetFloat(_moveSpeedHash, _currentSpeed);

        // Initialize body rotation
        if (characterModelRoot != null)
            characterModelRoot.rotation = _state.BodyRotation.Value;
    }

    // ============================================================
    // VISIBILITY
    // ============================================================

    private void SetRenderersVisible(bool visible)
    {
        if (characterRenderers == null) return;

        foreach (var renderer in characterRenderers)
        {
            if (renderer != null)
                renderer.enabled = visible;
        }
    }

    public void SetCharacterVisible(bool visible)
    {
        SetRenderersVisible(visible);
    }

    // ============================================================
    // DEBUG
    // ============================================================

    private void OnGUI()
    {
        if (!showDebugInfo || IsOwner) return;

        GUILayout.BeginArea(new Rect(10, 200, 300, 200));
        GUILayout.Label($"=== Animation Debug ===");
        GUILayout.Label($"Speed: {_state.Speed.Value:F2} → {_currentSpeed:F2}");
        GUILayout.Label($"MoveInput: {_state.MoveInput.Value}");
        GUILayout.Label($"Grounded: {_state.IsGrounded.Value}");
        GUILayout.Label($"Sprinting: {_state.IsSprinting.Value}");
        GUILayout.Label($"Crouching: {_state.IsCrouching.Value}");

        if (_animator != null)
        {
            GUILayout.Label($"--- Animator Params ---");
            GUILayout.Label($"MoveSpeed: {_animator.GetFloat(_moveSpeedHash):F2}");
            GUILayout.Label($"MoveX: {_animator.GetFloat(_moveXHash):F2}");
            GUILayout.Label($"MoveY: {_animator.GetFloat(_moveYHash):F2}");
        }
        GUILayout.EndArea();
    }
}