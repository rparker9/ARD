using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Defines grip points on an object that hands can attach to.
/// Supports multiple grip configurations (two-handed, one-handed, etc.)
/// </summary>
public class GrippableObject : MonoBehaviour
{
    [System.Serializable]
    public class GripPoint
    {
        [Tooltip("Name of this grip point (e.g., 'Handle', 'Top', 'Bottom')")]
        public string name = "Grip";

        [Tooltip("Transform defining position and rotation for this grip")]
        public Transform transform;

        [Tooltip("Which hand uses this grip (or Both for two-handed)")]
        public HandType handType = HandType.Right;

        [Tooltip("Priority when multiple grips available (higher = preferred)")]
        public int priority = 0;
    }

    public enum HandType
    {
        Right,
        Left,
        Both  // For two-handed objects
    }

    [Header("Grip Configuration")]
    [Tooltip("Grip points on this object")]
    public GripPoint[] gripPoints = new GripPoint[2];

    [Header("Object Type")]
    [Tooltip("Is this a two-handed object (like a box or rifle)?")]
    public bool requiresTwoHands = true;

    [Tooltip("Can be picked up with one hand if two hands not available")]
    public bool allowOneHanded = false;

    [Header("Visual Helpers")]
    [Tooltip("Show grip point gizmos in editor")]
    public bool showGizmos = true;

    [Tooltip("Size of grip point gizmos")]
    public float gizmoSize = 0.05f;

    private void OnValidate()
    {
        // Auto-create grip transforms if they don't exist
        for (int i = 0; i < gripPoints.Length; i++)
        {
            if (gripPoints[i] != null && gripPoints[i].transform == null)
            {
                // Create grip point transform
                GameObject gripGO = new GameObject($"GripPoint_{i}");
                gripGO.transform.SetParent(transform);
                gripGO.transform.localPosition = Vector3.zero;
                gripGO.transform.localRotation = Quaternion.identity;
                gripPoints[i].transform = gripGO.transform;
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos || gripPoints == null) return;

        foreach (var grip in gripPoints)
        {
            if (grip?.transform == null) continue;

            // Draw coordinate axes at grip point
            Gizmos.color = grip.handType == HandType.Right ? Color.red : Color.blue;
            if (grip.handType == HandType.Both) Gizmos.color = Color.green;

            // Draw sphere at grip position
            Gizmos.DrawWireSphere(grip.transform.position, gizmoSize);

            // Draw forward direction (where palm faces)
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(grip.transform.position,
                           grip.transform.position + grip.transform.forward * gizmoSize * 3);

            // Draw up direction (where fingers point)
            Gizmos.color = Color.green;
            Gizmos.DrawLine(grip.transform.position,
                           grip.transform.position + grip.transform.up * gizmoSize * 2);
        }
    }

    /// <summary>
    /// Get the grip point for a specific hand
    /// </summary>
    public GripPoint GetGripForHand(HandType hand)
    {
        GripPoint bestGrip = null;
        int highestPriority = int.MinValue;

        foreach (var grip in gripPoints)
        {
            if (grip?.transform == null) continue;

            // Check if this grip matches the requested hand
            bool matches = grip.handType == hand || grip.handType == HandType.Both;

            if (matches && grip.priority > highestPriority)
            {
                bestGrip = grip;
                highestPriority = grip.priority;
            }
        }

        return bestGrip;
    }

    /// <summary>
    /// Get all grip points for two-handed holding
    /// </summary>
    public (GripPoint right, GripPoint left) GetTwoHandedGrips()
    {
        GripPoint rightGrip = GetGripForHand(HandType.Right);
        GripPoint leftGrip = GetGripForHand(HandType.Left);

        return (rightGrip, leftGrip);
    }
}