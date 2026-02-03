using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerState))]
public sealed partial class PlayerInteractionController : NetworkBehaviour
{
    [Header("Input (Optional)")]
    [Tooltip("Primary interact (Use / Pickup / Drop). If unassigned, falls back to E key.")]
    [SerializeField] private InputActionReference primaryInteract;

    [Tooltip("Throw while holding. If unassigned, falls back to Q key.")]
    [SerializeField] private InputActionReference throwAction;

    [Header("Raycast")]
    [SerializeField] private float maxInteractDistance = 3.0f;
    [SerializeField] private LayerMask interactMask = ~0;

    [Header("Hold Target")]
    [SerializeField] private float carryHoldDistance = 1.8f;
    [SerializeField] private float dragHoldDistance = 2.2f;

    [Header("Auto Mode by Weight")]
    [SerializeField] private float dragMassThreshold = 12f;

    [Tooltip("How often we send hold target updates to the server (seconds).")]
    [SerializeField] private float targetSendInterval = 1f / 30f;

    [Tooltip("Clamp hold point so held bodies don't go through walls.")]
    [SerializeField] private bool clampToObstacles = true;

    [SerializeField] private float obstaclePadding = 0.15f;

    [Header("Throw")]
    [SerializeField] private float throwImpulse = 6.5f;

    [Header("References")]
    [SerializeField] private PlayerCameraController cameraController;

    [Header("Held Collision")]
    [SerializeField] private bool ignoreHeldCollisionWithPlayer = true;

    // Public UI hooks
    public NetworkObject FocusedObject { get; private set; }
    public NetworkInteractableBody FocusedBody { get; private set; }
    public IReadOnlyList<InteractionOption> CurrentOptions => _options;
    public event Action FocusChanged;
    public event Action OptionsChanged;

    // Internal
    private PlayerState _state;
    private UIRoot _ui;

    private readonly List<InteractionOption> _options = new();
    private NetworkObject _clientHeld;
    private NetworkObject _ignoredHeldObj;

    private float _nextTargetSendTime;
    private bool _primaryWasDown;
    private bool _throwWasDown;

    private void Awake()
    {
        _state = GetComponent<PlayerState>();

        if (cameraController == null)
            cameraController = GetComponent<PlayerCameraController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            primaryInteract?.action?.Enable();
            throwAction?.action?.Enable();

            _ui = AppRoot.Instance != null ? AppRoot.Instance.GetComponent<UIRoot>() : null;
        }

        if (_state != null)
            _state.HeldObject.OnValueChanged += OnHeldObjectChanged;

        OnHeldObjectChanged(default, _state != null ? _state.HeldObject.Value : default);
    }

    public override void OnNetworkDespawn()
    {
        if (_state != null)
            _state.HeldObject.OnValueChanged -= OnHeldObjectChanged;

        if (IsOwner)
        {
            primaryInteract?.action?.Disable();
            throwAction?.action?.Disable();
        }

        if (IsServer)
            ServerDropIfHolding();
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (cameraController == null || cameraController.Camera == null) return;

        if (_ui != null && _ui.IsPaused)
            return;

        UpdateFocus();

        bool primaryPressed = ReadPressedThisFrame(primaryInteract, Key.E, ref _primaryWasDown);
        bool throwPressed = ReadPressedThisFrame(throwAction, Key.Q, ref _throwWasDown);

        if (primaryPressed)
            RequestPrimary();

        if (throwPressed && _clientHeld != null)
            RequestThrow();

        if (_clientHeld != null)
            SendHoldTargetIfDue();
    }
}
