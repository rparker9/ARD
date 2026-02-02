using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using System;

[Serializable]
public struct PlayerInputSnapshot : INetworkSerializable, IEquatable<PlayerInputSnapshot>
{
    public int Tick;
    public Vector2 Move;
    public float AimYaw;
    public float AimPitch;
    public bool Jump;
    public bool Sprint;
    public bool Fire;
    public bool Crouch;

    public bool Equals(PlayerInputSnapshot other)
    {
        return Tick == other.Tick &&
               Move == other.Move &&
               AimYaw == other.AimYaw &&
               AimPitch == other.AimPitch &&
               Jump == other.Jump &&
               Sprint == other.Sprint &&
               Fire == other.Fire &&
               Crouch == other.Crouch;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Move);
        serializer.SerializeValue(ref AimYaw);
        serializer.SerializeValue(ref AimPitch);
        serializer.SerializeValue(ref Jump);
        serializer.SerializeValue(ref Sprint);
        serializer.SerializeValue(ref Fire);
        serializer.SerializeValue(ref Crouch);
    }
}

/// <summary>
/// OWNER-ONLY: Collects input, sends to server, feeds to local prediction.
/// This is the single source of input for both client prediction and server authority.
/// </summary>
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerInputController : NetworkBehaviour
{
    private PlayerControls _controls;
    private PlayerMotorServer _serverMotor;
    private PlayerMotorClient _clientMotor;
    private PlayerCameraController _camera;
    private PlayerState _state;

    private UIRoot _ui;
    private int _tick;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    public override void OnNetworkSpawn()
    {
        // Only run for owner
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        // Get components
        _serverMotor = GetComponent<PlayerMotorServer>();
        _clientMotor = GetComponent<PlayerMotorClient>();
        _camera = GetComponent<PlayerCameraController>();
        _state = GetComponent<PlayerState>();

        // Get UI
        _ui = AppRoot.Instance?.GetComponent<UIRoot>();

        // Setup input
        _controls = new PlayerControls();

        if (_controls == null)
        {
            Debug.LogError("[PlayerInput] Failed to create PlayerControls!");
            return;
        }

        _controls.Gameplay.Enable();
        _controls.UI.Enable();
        _controls.UI.Pause.performed += OnPausePressed;

        // Lock cursor for gameplay
        SetCursorLocked(true);
    }

    public override void OnNetworkDespawn()
    {
        CleanupInput();
    }

    private void OnDisable()
    {
        CleanupInput();
    }

    private void CleanupInput()
    {
        if (_controls != null)
        {
            _controls.UI.Pause.performed -= OnPausePressed;
            _controls.Gameplay.Disable();
            _controls.UI.Disable();
            _controls.Dispose();
            _controls = null;
        }
    }

    // ============================================================
    // INPUT READING (every frame)
    // ============================================================

    private void Update()
    {
        if (!IsOwner) return;
        if (_controls == null)
        {
            return;
        }

        // Skip input if paused
        if (_ui != null && _ui.IsPaused)
            return;

        // Update camera
        if (_camera != null)
        {
            Vector2 look = _controls.Gameplay.Look.ReadValue<Vector2>();
            _camera.ApplyLook(look, Time.deltaTime);
        }

        // Read input
        Vector2 move = _controls.Gameplay.Move.ReadValue<Vector2>();
        bool jump = _controls.Gameplay.Jump.IsPressed();
        bool sprint = _controls.Gameplay.Sprint.IsPressed();
        bool fire = _controls.Gameplay.Fire.IsPressed();
        bool crouch = _controls.Gameplay.Crouch.IsPressed();

        // Get aim from camera
        float aimYaw = _camera != null ? _camera.YawDegrees : transform.eulerAngles.y;
        float aimPitch = _camera != null ? _camera.PitchDegrees : 0f;

        // Increment tick
        _tick++;

        // Create snapshot
        var snapshot = new PlayerInputSnapshot
        {
            Tick = _tick,
            Move = move,
            AimYaw = aimYaw,
            AimPitch = aimPitch,
            Jump = jump,
            Sprint = sprint,
            Fire = fire,
            Crouch = crouch
        };

        // Apply to local prediction (if client)
        if (_clientMotor != null && !IsServer)
        {
            _clientMotor.SetLocalInput(move, aimYaw, jump, sprint, crouch);
        }

        // Send to server
        SubmitInputRpc(snapshot);
    }

    [Rpc(SendTo.Server)]
    private void SubmitInputRpc(PlayerInputSnapshot snapshot)
    {
        if (!IsServer) return;

        // FIX: Get the motor reference if we don't have it yet
        // This happens on the server's copy of the client's player object
        if (_serverMotor == null)
            _serverMotor = GetComponent<PlayerMotorServer>();

        // Apply to server motor
        if (_serverMotor != null)
        {
            _serverMotor.SetInput(snapshot);
        }
        else
        {
            Debug.LogError("[PlayerInput] ServerMotor component is missing from player prefab!");
        }
    }

    // ============================================================
    // PAUSE HANDLING
    // ============================================================

    private void OnPausePressed(InputAction.CallbackContext _)
    {
        if (!IsOwner) return;
        if (_ui == null) return;

        bool wasPaused = _ui.IsPaused;
        _ui.TogglePause();

        SetCursorLocked(!_ui.IsPaused);

        // Send stop input when pausing
        if (!wasPaused && _ui.IsPaused)
        {
            SendStopInput();
        }
    }

    public void SetPaused(bool paused)
    {
        if (!IsOwner) return;
        if (_ui == null) return;

        bool currentlyPaused = _ui.IsPaused;
        if (paused != currentlyPaused)
            _ui.TogglePause();

        SetCursorLocked(!paused);

        if (paused)
            SendStopInput();
    }

    private void SendStopInput()
    {
        float aimYaw = _camera != null ? _camera.YawDegrees : transform.eulerAngles.y;
        float aimPitch = _camera != null ? _camera.PitchDegrees : 0f;

        _tick++;

        SubmitInputRpc(new PlayerInputSnapshot
        {
            Tick = _tick,
            Move = Vector2.zero,
            AimYaw = aimYaw,
            AimPitch = aimPitch,
            Jump = false,
            Sprint = false,
            Fire = false,
            Crouch = false
        });
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}