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
/// Handles player input and synchronizes it across the network, enabling gameplay actions such as movement, jumping,
/// and firing.
/// </summary>
/// <remarks>This class is responsible for managing player controls and sending input data to the server. It
/// ensures that input is only processed for the owner of the player instance and handles UI interactions, including
/// pausing the game. The class also integrates with various components like the camera controller and player motors to
/// apply input effectively.</remarks>
public sealed class PlayerInputRelay : NetworkBehaviour
{
    private PlayerControls _controls;
    private ServerPlayerMotor _serverMotor;
    private ClientPredictedMotor _predictedMotor;
    private PlayerCameraController _cameraController;

    private UIRoot _ui;
    private int _tick;

    public override void OnNetworkSpawn()
    {
        // Get required components for player input handling
        _serverMotor = GetComponent<ServerPlayerMotor>();
        _predictedMotor = GetComponent<ClientPredictedMotor>();
        _cameraController = GetComponent<PlayerCameraController>();

        // Only enable input for the owner (local player)
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        // Get UIRoot from AppRoot singleton (if available)
        _ui = AppRoot.Instance != null ? AppRoot.Instance.GetComponent<UIRoot>() : null;

        // Initialize input controls and enable gameplay and UI action maps
        _controls = new PlayerControls();
        _controls.Gameplay.Enable();
        _controls.UI.Enable();

        // Subscribe to pause action
        _controls.UI.Pause.performed += OnPausePerformed;
    }

    public override void OnNetworkDespawn()
    {
        CleanupControls();
    }

    private void OnDisable()
    {
        CleanupControls();
    }

    /// <summary>
    /// Releases all resources associated with the input controls and removes event handlers to prevent memory leaks.
    /// </summary>
    /// <remarks>Call this method when the input controls are no longer needed, such as during scene
    /// transitions or when disposing of the player input system. This ensures that all event subscriptions are removed
    /// and resources are properly released.</remarks>
    private void CleanupControls()
    {
        if (_controls != null)
        {
            // Unsubscribe from pause action
            _controls.UI.Pause.performed -= OnPausePerformed;

            // Disable action maps and dispose of controls
            _controls.Gameplay.Disable();
            _controls.UI.Disable();
            _controls.Dispose();
            _controls = null;
        }
    }

    /// <summary>
    /// Sets the paused state of the user interface and updates cursor visibility and lock state accordingly.
    /// </summary>
    /// <remarks>This method has an effect only if the current instance is the owner and the user interface is
    /// initialized. When paused, the cursor becomes visible and is unlocked; when resumed, the cursor is hidden and
    /// locked.</remarks>
    /// <param name="paused">true to pause the user interface; false to resume it.</param>
    public void SetPaused(bool paused)
    {
        // Owner-only, early out if not owner or UI not initialized
        if (!IsOwner) return;
        if (_ui == null) return;

        // Ensure UI matches requested state
        bool currentlyPaused = _ui.IsPaused;
        if (paused != currentlyPaused)
            _ui.TogglePause();

        // Update cursor state
        Cursor.visible = paused;
        Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;

        // If pausing, send a stop snapshot to the server
        if (paused)
        {
            SendStopSnapshot();
        }
    }

    /// <summary>
    /// Handles the pause input action by toggling the application's paused state.
    /// </summary>
    /// <remarks>This method only performs the pause toggle if the current instance is the owner and the user
    /// interface is initialized. It inverts the current paused state as indicated by the UI.</remarks>
    /// <param name="_">The context for the input action that triggered the pause event. This parameter is not used within the method.</param>
    private void OnPausePerformed(InputAction.CallbackContext _)
    {
        if (!IsOwner) return;
        if (_ui == null) return;

        // Toggle paused state
        SetPaused(!_ui.IsPaused);
    }


    /// <summary>
    /// Submits a final input snapshot to indicate that the player has stopped performing movement or action inputs.
    /// </summary>
    /// <remarks>This method captures the current aiming direction based on the camera controller, if
    /// available, or the object's rotation otherwise. It then sends an input update to the server with all action flags
    /// (jump, sprint, fire) set to false, signaling the end of active input. This is typically used to ensure the
    /// server is aware that the player has ceased input actions.</remarks>
    private void SendStopSnapshot()
    {
        // Get aim from camera controller (or fallback to transform)
        float aimYaw = _cameraController != null ? _cameraController.YawDegrees : transform.eulerAngles.y;
        float aimPitch = _cameraController != null ? _cameraController.PitchDegrees : 0f;

        // Increment tick and send stop input to server
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

    private void Update()
    {
        // Owner-only, early out if not owner or controls not initialized
        if (!IsOwner) return;
        if (_controls == null) return;

        // Do not process gameplay input while paused
        if (_ui != null && _ui.IsPaused)
            return;

        // Apply look to camera controller
        if (_cameraController != null)
        {
            Vector2 look = _controls.Gameplay.Look.ReadValue<Vector2>();
            _cameraController.ApplyLook(look, Time.deltaTime);
        }

        // Increment tick
        _tick++;

        // Read inputs
        Vector2 move = _controls.Gameplay.Move.ReadValue<Vector2>();
        bool jump = _controls.Gameplay.Jump.IsPressed();
        bool sprint = _controls.Gameplay.Sprint.IsPressed();
        bool fire = _controls.Gameplay.Fire.IsPressed();
        bool crouch = _controls.Gameplay.Crouch.IsPressed();

        // Get aim from camera controller (or fallback to transform)
        float aimYaw = _cameraController != null ? _cameraController.YawDegrees : transform.eulerAngles.y;
        float aimPitch = _cameraController != null ? _cameraController.PitchDegrees : 0f;

        // Apply to predicted motor
        if (_predictedMotor != null)
            _predictedMotor.SetLocalInput(move, aimYaw, jump, sprint);

        // Send to server
        SubmitInputRpc(new PlayerInputSnapshot
        {
            Tick = _tick,
            Move = move,
            AimYaw = aimYaw,
            AimPitch = aimPitch,
            Jump = jump,
            Sprint = sprint,
            Fire = fire,
            Crouch = crouch
        });
    }

    /// <summary>
    /// Submits the player's input state to the server for processing during the specified simulation tick.
    /// </summary>
    /// <remarks>This method is intended to be called only on the server. It updates the server-side player
    /// motor with the provided input state for authoritative simulation. Calling this method on a non-server instance
    /// has no effect.</remarks>
    /// <param name="tick">The simulation tick representing the frame or update cycle for which the input is being submitted.</param>
    /// <param name="move">A vector specifying the player's intended movement direction and magnitude.</param>
    /// <param name="aimYaw">The yaw angle, in degrees, indicating the horizontal aim direction of the player.</param>
    /// <param name="aimPitch">The pitch angle, in degrees, indicating the vertical aim direction of the player.</param>
    /// <param name="jump">A value indicating whether the player is attempting to jump. Set to <see langword="true"/> to request a jump;
    /// otherwise, <see langword="false"/>.</param>
    /// <param name="sprint">A value indicating whether the player is attempting to sprint. Set to <see langword="true"/> to request
    /// sprinting; otherwise, <see langword="false"/>.</param>
    /// <param name="fire">A value indicating whether the player is attempting to fire their weapon. Set to <see langword="true"/> to
    /// request firing; otherwise, <see langword="false"/>.</param>
    [Rpc(SendTo.Server)]
    private void SubmitInputRpc(PlayerInputSnapshot snapshot)
    {
        // Server-only, early out if not server
        if (!IsServer) return;

        // Ensure server motor is assigned if not already
        if (_serverMotor == null)
            _serverMotor = GetComponent<ServerPlayerMotor>();

        // Apply input to server motor if available
        if (_serverMotor != null)
            _serverMotor.SetInput(snapshot);
    }
}
