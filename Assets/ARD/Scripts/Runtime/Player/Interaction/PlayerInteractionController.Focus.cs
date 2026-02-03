using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 
/// </summary>
public sealed partial class PlayerInteractionController
{
    public readonly struct InteractionOption
    {
        public readonly string Label;
        public InteractionOption(string label) { Label = label; }
    }

    private void UpdateFocus()
    {
        var cam = cameraController.Camera;
        Vector3 origin = cam.transform.position;
        Vector3 dir = cam.transform.forward;

        NetworkObject newFocusedObj = null;
        NetworkInteractableBody newFocusedBody = null;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxInteractDistance, interactMask, QueryTriggerInteraction.Ignore))
        {
            newFocusedBody = hit.collider.GetComponent<NetworkInteractableBody>();
            newFocusedObj = hit.collider.GetComponent<NetworkObject>();
        }

        if (newFocusedObj == FocusedObject && newFocusedBody == FocusedBody)
            return;

        FocusedObject = newFocusedObj;
        FocusedBody = newFocusedBody;

        RebuildOptions();
        FocusChanged?.Invoke();
    }

    private void RebuildOptions()
    {
        _options.Clear();

        string primaryLabel = null;

        if (FocusedObject != null)
        {
            var usable = FindBestUsableLocal(FocusedObject);
            if (usable != null)
                primaryLabel = usable.Verb;
        }
        else if (_clientHeld != null)
        {
            primaryLabel = "Drop";
        }
        else if (FocusedBody != null)
        {
            primaryLabel = "Pick Up";
        }

        if (!string.IsNullOrEmpty(primaryLabel))
            _options.Add(new InteractionOption(primaryLabel));

        // Secondary option (Throw) only while holding
        if (_clientHeld != null)
            _options.Add(new InteractionOption("Throw"));

        OptionsChanged?.Invoke();
    }

    private ServerUsable FindBestUsableLocal(NetworkObject target)
    {
        if (target == null) return null;

        var usables = target.GetComponents<ServerUsable>();

        ServerUsable best = null;
        int bestPriority = int.MinValue;

        for (int i = 0; i < usables.Length; i++)
        {
            var u = usables[i];
            if (u == null) continue;

            // UI-only filter
            if (!u.CanUseLocal(this))
                continue;

            if (u.Priority > bestPriority)
            {
                best = u;
                bestPriority = u.Priority;
            }
        }

        return best;
    }
}
