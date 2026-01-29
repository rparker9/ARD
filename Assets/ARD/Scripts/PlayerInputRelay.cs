using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Owner-side input collection + submission to server.
/// Also feeds the client-side predicted motor for immediate movement feel.
/// </summary>
public sealed class PlayerInputRelay : NetworkBehaviour
{
    private PlayerControls _controls;
    private ServerPlayerMotor _serverMotor;
    private ClientPredictedMotor _predictedMotor;
    private PlayerViewController _view;

    private int _tick;

    public override void OnNetworkSpawn()
    {
        _serverMotor = GetComponent<ServerPlayerMotor>();
        _predictedMotor = GetComponent<ClientPredictedMotor>();
        _view = GetComponent<PlayerViewController>();

        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        _controls = new PlayerControls();
        _controls.Gameplay.Enable();

        Debug.Log($"[Spawn] IsServer={IsServer} IsOwner={IsOwner} pos={transform.position}");

        var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        Debug.Log($"[NT] present={(nt != null)}");
    }

    public override void OnNetworkDespawn()
    {
        CleanupControls();
    }

    private void OnDisable()
    {
        CleanupControls();
    }

    private void CleanupControls()
    {
        if (_controls != null)
        {
            _controls.Gameplay.Disable();
            _controls.Dispose();
            _controls = null;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (_controls == null) return;

        _tick++;

        Vector2 move = _controls.Gameplay.Move.ReadValue<Vector2>();
        bool jump = _controls.Gameplay.Jump.IsPressed();
        bool sprint = _controls.Gameplay.Sprint.IsPressed();
        bool fire = _controls.Gameplay.Fire.IsPressed();

        float aimYaw = _view != null ? _view.YawDegrees : transform.eulerAngles.y;
        float aimPitch = _view != null ? _view.PitchDegrees : 0f;

        // Client prediction (only does anything on remote clients; host is server)
        if (_predictedMotor != null)
            _predictedMotor.SetLocalInput(move, aimYaw, jump, sprint);

        // Send snapshot to server
        SubmitInputRpc(_tick, move, aimYaw, aimPitch, jump, sprint, fire);
    }

    [Rpc(SendTo.Server)]
    private void SubmitInputRpc(int tick, Vector2 move, float aimYaw, float aimPitch, bool jump, bool sprint, bool fire)
    {
        if (!IsServer) return;

        if (_serverMotor == null)
            _serverMotor = GetComponent<ServerPlayerMotor>();

        if (_serverMotor != null)
            _serverMotor.SetInput(tick, move, aimYaw, aimPitch, jump, sprint, fire);
    }
}
