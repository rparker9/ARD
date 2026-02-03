using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class PlayerInteractionController
{
    private void RequestPrimary()
    {
        var target = FocusedObject != null ? new NetworkObjectReference(FocusedObject) : default;
        RequestPrimaryServerRpc(target);
    }

    private void RequestThrow()
    {
        RequestThrowServerRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestPrimaryServerRpc(NetworkObjectReference targetObj)
    {
        if (!IsServer) return;
        if (_state == null) return;

        NetworkObject target = null;
        if (targetObj.TryGet(out var t))
            target = t;

        // Use wins if a usable target is focused and valid.
        if (target != null && ServerValidateInteractRangeAndLos(target))
        {
            if (ServerExecuteUseIfAny(target))
                return;
        }

        // Otherwise: drop if holding, else pickup if possible.
        if (_state.HeldObject.Value.TryGet(out _))
        {
            ServerDropIfHolding();
            return;
        }

        if (target != null)
            ServerTryPickupBody(target);
    }

    [Rpc(SendTo.Server)]
    private void RequestThrowServerRpc()
    {
        if (!IsServer) return;
        ServerThrowIfHolding();
    }

    [Rpc(SendTo.Server)]
    private void UpdateHoldTargetServerRpc(Vector3 target)
    {
        if (!IsServer) return;
        if (_state == null) return;

        if (!_state.HeldObject.Value.TryGet(out var heldObj) || heldObj == null)
            return;

        var body = heldObj.GetComponent<NetworkInteractableBody>();
        if (body == null)
            return;

        body.ServerUpdateTarget(OwnerClientId, target);
    }

    private bool ServerExecuteUseIfAny(NetworkObject target)
    {
        var usables = target.GetComponents<ServerUsable>();

        ServerUsable best = null;
        int bestPriority = int.MinValue;

        for (int i = 0; i < usables.Length; i++)
        {
            var u = usables[i];
            if (u == null) continue;

            if (u.Priority > bestPriority)
            {
                best = u;
                bestPriority = u.Priority;
            }
        }

        if (best == null)
            return false;

        best.ServerUse(this, OwnerClientId);
        return true;
    }

    private void ServerTryPickupBody(NetworkObject target)
    {
        var body = target.GetComponent<NetworkInteractableBody>();
        if (body == null) return;
        if (body.IsHeld) return;
        if (!ServerValidateInteractRangeAndLos(target)) return;

        var mode = (body.Mass >= dragMassThreshold)
            ? NetworkInteractableBody.HoldMode.Drag
            : NetworkInteractableBody.HoldMode.Carry;

        Vector3 eye = ServerEyePosition();
        Vector3 dir = _state.AimDirection.normalized;

        float desiredDist = (mode == NetworkInteractableBody.HoldMode.Drag) ? dragHoldDistance : carryHoldDistance;
        Vector3 initialTarget = eye + dir * desiredDist;

        if (!body.ServerTryStartHold(OwnerClientId, initialTarget, mode))
            return;

        _state.SetHeldObjectServer(target);
    }

    private void ServerDropIfHolding()
    {
        if (_state == null) return;

        if (!_state.HeldObject.Value.TryGet(out var heldObj) || heldObj == null)
            return;

        var body = heldObj.GetComponent<NetworkInteractableBody>();
        if (body != null)
            body.ServerDrop();

        _state.ClearHeldObjectServer();
    }

    private void ServerThrowIfHolding()
    {
        if (_state == null) return;

        if (!_state.HeldObject.Value.TryGet(out var heldObj) || heldObj == null)
            return;

        var body = heldObj.GetComponent<NetworkInteractableBody>();
        if (body == null)
        {
            _state.ClearHeldObjectServer();
            return;
        }

        Vector3 impulse = _state.AimDirection.normalized * throwImpulse;
        body.ServerThrow(OwnerClientId, impulse);
        _state.ClearHeldObjectServer();
    }

    private bool ServerValidateInteractRangeAndLos(NetworkObject expected)
    {
        if (expected == null) return false;

        Vector3 eye = ServerEyePosition();

        // Aim at collider closest point to avoid pivot issues.
        Vector3 targetPoint = expected.transform.position;
        var col = expected.GetComponent<Collider>();
        if (col != null)
            targetPoint = col.ClosestPoint(eye);

        Vector3 toTarget = targetPoint - eye;
        float dist = toTarget.magnitude;
        if (dist > maxInteractDistance)
            return false;

        if (dist < 0.001f)
            return true;

        Vector3 dir = toTarget / dist;

        if (Physics.Raycast(eye, dir, out RaycastHit hit, dist + 0.05f, ~0, QueryTriggerInteraction.Ignore))
        {
            var hitNetObj = hit.collider.GetComponentInParent<NetworkObject>();
            return hitNetObj == expected;
        }

        return false;
    }

    private Vector3 ServerEyePosition()
    {
        return transform.position + Vector3.up * 1.6f;
    }

    private static bool ReadPressedThisFrame(InputActionReference actionRef, Key fallbackKey, ref bool wasDown)
    {
        bool isDown;

        if (actionRef != null && actionRef.action != null)
            isDown = actionRef.action.ReadValue<float>() > 0.5f;
        else
            isDown = Keyboard.current != null && Keyboard.current[fallbackKey].isPressed;

        bool pressed = isDown && !wasDown;
        wasDown = isDown;
        return pressed;
    }
}
