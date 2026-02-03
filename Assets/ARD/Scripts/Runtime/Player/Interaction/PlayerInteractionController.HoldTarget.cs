using Unity.Netcode;
using UnityEngine;

public sealed partial class PlayerInteractionController
{
    private void SendHoldTargetIfDue()
    {
        if (Time.time < _nextTargetSendTime)
            return;

        _nextTargetSendTime = Time.time + targetSendInterval;

        var cam = cameraController.Camera;
        Vector3 origin = cam.transform.position;
        Vector3 dir = cam.transform.forward;

        float desiredDist = GetDesiredHoldDistanceLocal();
        Vector3 target = ComputeHoldTarget(origin, dir, desiredDist);

        UpdateHoldTargetServerRpc(target);
    }

    private float GetDesiredHoldDistanceLocal()
    {
        if (_clientHeld != null)
        {
            var body = _clientHeld.GetComponent<NetworkInteractableBody>();
            if (body != null && body.CurrentHoldMode.Value == NetworkInteractableBody.HoldMode.Drag)
                return dragHoldDistance;
        }

        return carryHoldDistance;
    }

    private Vector3 ComputeHoldTarget(Vector3 cameraPos, Vector3 cameraForward, float desiredDistance)
    {
        if (!clampToObstacles)
            return cameraPos + cameraForward * desiredDistance;

        NetworkObject held = _clientHeld;
        Collider[] heldCols = null;
        if (held != null)
            heldCols = held.GetComponents<Collider>();

        float dist = desiredDistance;

        var hits = Physics.RaycastAll(
            cameraPos,
            cameraForward,
            desiredDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];

                // Skip self hits (held object), otherwise clamp always becomes tiny.
                if (heldCols != null)
                {
                    for (int c = 0; c < heldCols.Length; c++)
                    {
                        if (heldCols[c] == h.collider)
                            goto NextHit;
                    }
                }

                dist = Mathf.Max(0.25f, h.distance - obstaclePadding);
                break;

            NextHit:;
            }
        }

        return cameraPos + cameraForward * dist;
    }
}
