using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Network-synchronized animation controller with debug support for viewing owner animations.
/// Includes smooth blend tree transitions for natural-looking movement.
/// </summary>
[RequireComponent(typeof(Animator))]
public sealed class NetworkAnimationController : NetworkBehaviour
{
    [Header("Blend Tree Parameters")]
    [Tooltip("Horizontal movement speed (0-1 normalized or absolute m/s depending on your blend tree)")]
    [SerializeField] private string moveSpeedParam = "MoveSpeed";

    [Tooltip("Horizontal input X (-1 to 1, for strafe animations)")]
    [SerializeField] private string moveXParam = "MoveX";

    [Tooltip("Horizontal input Y (-1 to 1, for forward/back animations)")]
    [SerializeField] private string moveYParam = "MoveY";

    [Tooltip("Optional: Movement direction in degrees (0-360) for directional blend trees")]
    [SerializeField] private string moveDirectionParam = "MoveDirection";

    [Header("State Parameters")]
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string isJumpingParam = "IsJumping";
    [SerializeField] private string isCrouchingParam = "IsCrouching";
    [SerializeField] private string isSprintingParam = "IsSprinting";

    [Header("Advanced Blend Tree")]
    [Tooltip("Use velocity magnitude instead of input magnitude for MoveSpeed")]
    [SerializeField] private bool useVelocityForSpeed = true;

    [Tooltip("Speed multiplier/normalization factor")]
    [SerializeField] private float speedMultiplier = 1f;

    [Tooltip("Smooth speed changes over time")]
    [SerializeField] private float speedSmoothTime = 0.1f;

    [Tooltip("Smooth directional input changes (MoveX/MoveY dampening time)")]
    [SerializeField] private float inputDampTime = 0.1f;

    [Header("Layer Parameters")]
    [Tooltip("Aim pitch for upper body layer (-90 to 90)")]
    [SerializeField] private string aimPitchParam = "AimPitch";

    [Tooltip("Aim yaw for upper body layer (-180 to 180)")]
    [SerializeField] private string aimYawParam = "AimYaw";

    [Tooltip("Enable/disable upper body layer weight based on grounded state")]
    [SerializeField] private bool disableUpperBodyInAir = false;

    [Tooltip("Upper body layer index (usually 1)")]
    [SerializeField] private int upperBodyLayerIndex = 1;

    [Header("Visual Settings")]
    [Tooltip("Renderers to hide for the local player (first-person view)")]
    [SerializeField] private Renderer[] characterRenderers;

    [Header("References")]
    [Tooltip("Optional: reference to CharacterController for grounded check")]
    [SerializeField] private CharacterController characterController;

    private Animator _animator;
    private ServerPlayerMotor _serverMotor;

    // Cached animator parameter IDs for performance
    private int _moveSpeedHash;
    private int _moveXHash;
    private int _moveYHash;
    private int _moveDirectionHash;
    private int _isGroundedHash;
    private int _isJumpingHash;
    private int _isCrouchingHash;
    private int _isSprintingHash;
    private int _aimPitchHash;
    private int _aimYawHash;

    // For smooth speed transitions
    private float _currentSpeed;
    private float _speedVelocity;

    // Network variables for animation state (server writes, everyone reads)
    private readonly NetworkVariable<float> _netMoveSpeed = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _netMoveX = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _netMoveY = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _netMoveDirection = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _netIsGrounded = new(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _netIsJumping = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _netIsCrouching = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _netIsSprinting = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _netAimPitch = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _netAimYaw = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public float ReplicatedAimPitchDegrees => _netAimPitch.Value;

    private void Awake()
    {
        _animator = GetComponent<Animator>();

        // Cache animator parameter hashes
        _moveSpeedHash = Animator.StringToHash(moveSpeedParam);
        _moveXHash = Animator.StringToHash(moveXParam);
        _moveYHash = Animator.StringToHash(moveYParam);
        _moveDirectionHash = Animator.StringToHash(moveDirectionParam);
        _isGroundedHash = Animator.StringToHash(isGroundedParam);
        _isJumpingHash = Animator.StringToHash(isJumpingParam);
        _isCrouchingHash = Animator.StringToHash(isCrouchingParam);
        _isSprintingHash = Animator.StringToHash(isSprintingParam);
        _aimPitchHash = Animator.StringToHash(aimPitchParam);
        _aimYawHash = Animator.StringToHash(aimYawParam);

        // Get CharacterController if not assigned
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        _serverMotor = GetComponent<ServerPlayerMotor>();

        // Hide character mesh for local player (first-person view)
        if (IsOwner)
        {
            SetRenderersVisible(false);
            enabled = false;
        }

        // Subscribe to network variable changes (non-owners OR owner in debug mode)
        if (!IsOwner)
        {
            _netMoveSpeed.OnValueChanged += OnMoveSpeedChanged;
            _netMoveX.OnValueChanged += OnMoveXChanged;
            _netMoveY.OnValueChanged += OnMoveYChanged;
            _netMoveDirection.OnValueChanged += OnMoveDirectionChanged;
            _netIsGrounded.OnValueChanged += OnGroundedChanged;
            _netIsJumping.OnValueChanged += OnJumpingChanged;
            _netIsCrouching.OnValueChanged += OnCrouchingChanged;
            _netIsSprinting.OnValueChanged += OnSprintingChanged;
            _netAimPitch.OnValueChanged += OnAimPitchChanged;
            _netAimYaw.OnValueChanged += OnAimYawChanged;

            // Initialize with current values
            UpdateAnimatorParameters();
        }
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from network variable changes
        if (!IsOwner)
        {
            _netMoveSpeed.OnValueChanged -= OnMoveSpeedChanged;
            _netMoveX.OnValueChanged -= OnMoveXChanged;
            _netMoveY.OnValueChanged -= OnMoveYChanged;
            _netMoveDirection.OnValueChanged -= OnMoveDirectionChanged;
            _netIsGrounded.OnValueChanged -= OnGroundedChanged;
            _netIsJumping.OnValueChanged -= OnJumpingChanged;
            _netIsCrouching.OnValueChanged -= OnCrouchingChanged;
            _netIsSprinting.OnValueChanged -= OnSprintingChanged;
            _netAimPitch.OnValueChanged -= OnAimPitchChanged;
            _netAimYaw.OnValueChanged -= OnAimYawChanged;
        }
    }

    private void Update()
    {
        // Server updates animation state based on player input and physics
        if (IsServer)
        {
            UpdateAnimationStateServer();
        }

        // Non-owners update upper body layer weight if needed
        if (!IsOwner && disableUpperBodyInAir)
        {
            UpdateUpperBodyLayerWeight();
        }
    }

    /// <summary>
    /// Server-only: Update animation state based on current player input and physics state.
    /// </summary>
    private void UpdateAnimationStateServer()
    {
        if (_serverMotor == null) return;

        // Get movement input from ServerPlayerMotor
        Vector2 moveInput = _serverMotor.MoveInput;
        _netMoveX.Value = moveInput.x;
        _netMoveY.Value = moveInput.y;

        // Calculate movement speed
        float speed;
        if (useVelocityForSpeed && characterController != null)
        {
            // Use actual velocity magnitude
            Vector3 velocity = characterController.velocity;
            speed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
        }
        else
        {
            // Use input magnitude
            speed = moveInput.magnitude;
        }

        _netMoveSpeed.Value = speed * speedMultiplier;

        // Calculate movement direction (angle from forward)
        if (moveInput.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(moveInput.x, moveInput.y) * Mathf.Rad2Deg;
            _netMoveDirection.Value = angle;
        }

        // Grounded state
        bool isGrounded = characterController != null && characterController.isGrounded;
        _netIsGrounded.Value = isGrounded;

        // Read input state from ServerPlayerMotor
        _netIsCrouching.Value = _serverMotor.IsCrouching;
        _netIsSprinting.Value = _serverMotor.IsSprinting;
        _netIsJumping.Value = _serverMotor.IsJumping;

        // Aim angles for upper body layer
        _netAimPitch.Value = _serverMotor.AimPitchDegrees;
        _netAimYaw.Value = 0f; // Relative to body, body rotation is handled by model rotation
    }

    // Network variable change callbacks (client-side only)
    private void OnMoveSpeedChanged(float prev, float current)
    {
        // Don't update directly, let LateUpdate() smooth it
    }

    private void OnMoveXChanged(float prev, float current)
    {
        if (_animator != null)
        {
            // Use built-in animator dampening for smooth transitions
            _animator.SetFloat(_moveXHash, current, inputDampTime, Time.deltaTime);
        }
    }

    private void OnMoveYChanged(float prev, float current)
    {
        if (_animator != null)
        {
            // Use built-in animator dampening for smooth transitions
            _animator.SetFloat(_moveYHash, current, inputDampTime, Time.deltaTime);
        }
    }

    private void OnMoveDirectionChanged(float prev, float current)
    {
        if (_animator != null)
            _animator.SetFloat(_moveDirectionHash, current);
    }

    private void OnGroundedChanged(bool prev, bool current)
    {
        if (_animator != null)
            _animator.SetBool(_isGroundedHash, current);
    }

    private void OnJumpingChanged(bool prev, bool current)
    {
        if (_animator != null)
            _animator.SetBool(_isJumpingHash, current);
    }

    private void OnCrouchingChanged(bool prev, bool current)
    {
        if (_animator != null)
            _animator.SetBool(_isCrouchingHash, current);
    }

    private void OnSprintingChanged(bool prev, bool current)
    {
        if (_animator != null)
            _animator.SetBool(_isSprintingHash, current);
    }

    private void OnAimPitchChanged(float prev, float current)
    {
        if (_animator != null)
            _animator.SetFloat(_aimPitchHash, current);
    }

    private void OnAimYawChanged(float prev, float current)
    {
        if (_animator != null)
            _animator.SetFloat(_aimYawHash, current);
    }

    private void LateUpdate()
    {
        // Non-owner: Smooth speed transitions for blend tree
        if (!IsOwner && _animator != null)
        {
            _currentSpeed = Mathf.SmoothDamp(
                _currentSpeed,
                _netMoveSpeed.Value,
                ref _speedVelocity,
                speedSmoothTime);

            _animator.SetFloat(_moveSpeedHash, _currentSpeed);
        }
    }

    /// <summary>
    /// Update all animator parameters at once (used for initial sync)
    /// </summary>
    private void UpdateAnimatorParameters()
    {
        if (_animator == null) return;

        _animator.SetFloat(_moveSpeedHash, _netMoveSpeed.Value);

        // Use dampening for initial values too
        _animator.SetFloat(_moveXHash, _netMoveX.Value, inputDampTime, Time.deltaTime);
        _animator.SetFloat(_moveYHash, _netMoveY.Value, inputDampTime, Time.deltaTime);

        _animator.SetFloat(_moveDirectionHash, _netMoveDirection.Value);
        _animator.SetBool(_isGroundedHash, _netIsGrounded.Value);
        _animator.SetBool(_isJumpingHash, _netIsJumping.Value);
        _animator.SetBool(_isCrouchingHash, _netIsCrouching.Value);
        _animator.SetBool(_isSprintingHash, _netIsSprinting.Value);
        _animator.SetFloat(_aimPitchHash, _netAimPitch.Value);
        _animator.SetFloat(_aimYawHash, _netAimYaw.Value);

        _currentSpeed = _netMoveSpeed.Value;
    }

    /// <summary>
    /// Optionally disable upper body layer when in air (for better jump animations)
    /// </summary>
    private void UpdateUpperBodyLayerWeight()
    {
        if (_animator == null) return;

        float targetWeight = _netIsGrounded.Value ? 1f : 0f;
        float currentWeight = _animator.GetLayerWeight(upperBodyLayerIndex);
        float newWeight = Mathf.Lerp(currentWeight, targetWeight, Time.deltaTime * 5f);

        _animator.SetLayerWeight(upperBodyLayerIndex, newWeight);
    }

    /// <summary>
    /// Show or hide character renderers (used to hide local player's body in first-person)
    /// </summary>
    private void SetRenderersVisible(bool visible)
    {
        if (characterRenderers == null) return;

        foreach (var renderer in characterRenderers)
        {
            if (renderer != null)
                renderer.enabled = visible;
        }
    }

    /// <summary>
    /// Public method to manually show/hide renderers if needed
    /// </summary>
    public void SetCharacterVisible(bool visible)
    {
        SetRenderersVisible(visible);
    }

    /// <summary>
    /// Manually set upper body layer weight (useful for cutscenes, etc.)
    /// </summary>
    public void SetUpperBodyLayerWeight(float weight)
    {
        if (_animator != null && !IsOwner)
        {
            _animator.SetLayerWeight(upperBodyLayerIndex, weight);
        }
    }
}