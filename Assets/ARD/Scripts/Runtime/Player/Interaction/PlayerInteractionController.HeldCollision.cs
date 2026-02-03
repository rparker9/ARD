using Unity.Netcode;
using UnityEngine;

public sealed partial class PlayerInteractionController
{
    private void OnHeldObjectChanged(NetworkObjectReference prev, NetworkObjectReference curr)
    {
        if (ignoreHeldCollisionWithPlayer && _ignoredHeldObj != null)
        {
            SetCollisionBetweenPlayerAndHeld(_ignoredHeldObj, false);
            _ignoredHeldObj = null;
        }

        _clientHeld = null;
        if (IsClient && curr.TryGet(out var netObj))
            _clientHeld = netObj;

        if (ignoreHeldCollisionWithPlayer && curr.TryGet(out var newHeld) && newHeld != null)
        {
            _ignoredHeldObj = newHeld;
            SetCollisionBetweenPlayerAndHeld(_ignoredHeldObj, true);
        }

        RebuildOptions();
    }

    private void SetCollisionBetweenPlayerAndHeld(NetworkObject held, bool ignore)
    {
        if (held == null) return;

        var playerCols = GetComponentsInChildren<Collider>(true);
        var heldCols = held.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < playerCols.Length; i++)
        {
            var pc = playerCols[i];
            if (pc == null || pc.isTrigger) continue;

            for (int j = 0; j < heldCols.Length; j++)
            {
                var hc = heldCols[j];
                if (hc == null || hc.isTrigger) continue;

                Physics.IgnoreCollision(pc, hc, ignore);
            }
        }
    }
}
