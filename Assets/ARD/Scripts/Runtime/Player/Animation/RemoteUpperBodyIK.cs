using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Single coordinator for REMOTE (non-owner) upper-body presentation:
/// - Drives spine/head aim target transforms (pitch-only; yaw handled by model rotation)
/// - Drives hand IK targets from currently held GrippableObject grip points
/// - Sets aim rig weight
///
/// Owner policy:
/// - If IsOwner: rig weight = 0 and this component disables itself.
/// </summary>
[DisallowMultipleComponent]
public sealed class RemoteUpperBodyIK : NetworkBehaviour
{
    [Header("Rig")]
    [SerializeField] private Rig aimRig;

    [Header("Aim Targets")]
    [SerializeField] private Transform viewPosition;
    [SerializeField] private Transform aimTarget;
    [SerializeField] private Transform headAimTarget;

    [Header("Aim Settings")]
    [SerializeField] private float aimDistance = 10f;
    [SerializeField] private float aimHeightOffset = 0f;
    [SerializeField] private float aimSmoothSpeed = 15f;
    [SerializeField] private float maxPitchAngle = 60f;
    [SerializeField] private float minPitchAngle = -60f;

    [Header("Hand IK Targets")]
    [SerializeField] private Transform rightHandTarget;
    [SerializeField] private Transform leftHandTarget;

    [Header("Hand Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.1f;
    [SerializeField] private float rotationSmoothSpeed = 10f;

    [Header("Hand Offsets")]
    [SerializeField] private bool useRightHandIK = true;
    [SerializeField] private bool useLeftHandIK = true;
    [SerializeField] private Vector3 rightHandOffset = Vector3.zero;
    [SerializeField] private Vector3 leftHandOffset = Vector3.zero;
    [SerializeField] private Vector3 rightHandRotationOffset = Vector3.zero;
    [SerializeField] private Vector3 leftHandRotationOffset = Vector3.zero;

    private NetworkAnimationController _animNet;
    private NetworkHeldItemState _heldState;

    private float _currentPitch;
    private Vector3 _rightHandVelocity;
    private Vector3 _leftHandVelocity;

    private void Awake()
    {
        if (aimRig == null) aimRig = GetComponentInChildren<Rig>();
        _animNet = GetComponent<NetworkAnimationController>();
        _heldState = GetComponent<NetworkHeldItemState>();
    }

    public override void OnNetworkSpawn()
    {
        // Owner uses first-person arms / hidden character model
        if (IsOwner)
        {
            if (aimRig != null) aimRig.weight = 0f;
            enabled = false;
            return;
        }

        if (aimRig != null) aimRig.weight = 1f;
    }

    private void LateUpdate()
    {
        if (IsOwner) return; // belt + suspenders

        UpdateAimTarget();
        UpdateHandTargets();
    }

    private void UpdateAimTarget()
    {
        if (viewPosition == null || aimTarget == null || _animNet == null)
            return;

        // Pitch comes from replicated net state.
        float targetPitch = _animNet.ReplicatedAimPitchDegrees;
        targetPitch = Mathf.Clamp(targetPitch, minPitchAngle, maxPitchAngle);

        // Smooth pitch
        if (aimSmoothSpeed > 0f)
        {
            float t = 1f - Mathf.Exp(-aimSmoothSpeed * Time.deltaTime);
            _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, t);
        }
        else
        {
            _currentPitch = targetPitch;
        }

        // Yaw is handled by model rotation, so compute direction from view forward + pitch only.
        // Rotate around local right axis.
        Vector3 baseForward = viewPosition.forward;
        Vector3 baseRight = viewPosition.right;
        Quaternion pitchRot = Quaternion.AngleAxis(_currentPitch, baseRight);
        Vector3 dir = pitchRot * baseForward;

        Vector3 targetPos = viewPosition.position + dir * aimDistance;
        targetPos.y += aimHeightOffset;

        aimTarget.position = targetPos;

        if (headAimTarget != null)
            headAimTarget.position = targetPos;
    }

    private void UpdateHandTargets()
    {
        if (_heldState == null) return;

        GrippableObject held = _heldState.HeldGrippable;
        if (held == null) return;

        var grips = held.GetTwoHandedGrips();

        if (useRightHandIK && rightHandTarget != null && grips.right?.transform != null)
        {
            UpdateHandTarget(
                rightHandTarget,
                grips.right.transform,
                rightHandOffset,
                rightHandRotationOffset,
                ref _rightHandVelocity);
        }

        if (useLeftHandIK && leftHandTarget != null && grips.left?.transform != null)
        {
            UpdateHandTarget(
                leftHandTarget,
                grips.left.transform,
                leftHandOffset,
                leftHandRotationOffset,
                ref _leftHandVelocity);
        }
    }

    private void UpdateHandTarget(
        Transform handTarget,
        Transform gripPoint,
        Vector3 positionOffset,
        Vector3 rotationOffset,
        ref Vector3 velocity)
    {
        Vector3 targetPosition = gripPoint.position + gripPoint.TransformDirection(positionOffset);
        Quaternion targetRotation = gripPoint.rotation * Quaternion.Euler(rotationOffset);

        if (positionSmoothTime > 0f)
        {
            handTarget.position = Vector3.SmoothDamp(
                handTarget.position,
                targetPosition,
                ref velocity,
                positionSmoothTime);
        }
        else
        {
            handTarget.position = targetPosition;
        }

        if (rotationSmoothSpeed > 0f)
        {
            handTarget.rotation = Quaternion.Slerp(
                handTarget.rotation,
                targetRotation,
                Time.deltaTime * rotationSmoothSpeed);
        }
        else
        {
            handTarget.rotation = targetRotation;
        }
    }

    public void SetHandIKEnabled(bool rightHand, bool leftHand)
    {
        useRightHandIK = rightHand;
        useLeftHandIK = leftHand;
    }
}
