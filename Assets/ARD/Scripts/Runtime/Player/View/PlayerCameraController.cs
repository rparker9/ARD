using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Owner-only presentation layer: camera enable + smooth local yaw/pitch.
/// Input is provided externally by PlayerInputRelay (single input owner).
/// </summary>
public sealed class PlayerCameraController : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;

    [Header("View Rig")]
    [Tooltip("World-space yaw pivot (typically parent of pitchPivot).")]
    [SerializeField] private Transform yawPivot;

    [Tooltip("Local-space pitch pivot (typically child of yawPivot).")]
    [SerializeField] private Transform pitchPivot;

    [Header("Sensitivity")]
    [SerializeField] private float lookSensitivityX = 0.10f;
    [SerializeField] private float lookSensitivityY = 0.10f;

    [Header("Pitch Clamp")]
    [SerializeField] private float pitchMin = -80f;
    [SerializeField] private float pitchMax = 80f;

    [Header("Smoothing")]
    [Tooltip("Higher = smoother. Try 15–25.")]
    [SerializeField] private float lookSmoothing = 20f;

    private const string FallbackCameraTag = "FallbackCamera";

    private float _yaw;
    private float _pitch;
    private Vector2 _smoothedLookDelta;

    private GameObject _fallbackCamera;

    public float PitchDegrees => _pitch;
    public float YawDegrees => _yaw;

    public override void OnNetworkSpawn()
    {
        // Enable only for the owner.
        bool enable = IsOwner;

        // Enable/disable player camera
        if (playerCamera != null)
            playerCamera.gameObject.SetActive(enable);

        // Early out if not owner
        enabled = enable;
        if (!enable)
            return;

        // Find fallback camera in scene
        if (_fallbackCamera == null)
            _fallbackCamera = GameObject.FindGameObjectWithTag(FallbackCameraTag);

        // Disable fallback camera
        if (_fallbackCamera != null)
            _fallbackCamera.SetActive(false);

        // Initialize yaw/pitch from current transforms
        if (yawPivot != null)
            _yaw = yawPivot.eulerAngles.y;

        // Default to locked cursor for FPS; pause system will unlock when paused.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            if (_fallbackCamera == null)
                _fallbackCamera = GameObject.FindGameObjectWithTag(FallbackCameraTag);

            if (_fallbackCamera != null)
                _fallbackCamera.SetActive(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    /// <summary>
    /// Called by PlayerInputRelay (single input owner). Supply raw look delta from Input System.
    /// </summary>
    public void ApplyLook(Vector2 lookDelta, float dt)
    {
        // Sensitivity
        lookDelta = new Vector2(
            lookDelta.x * lookSensitivityX,
            lookDelta.y * lookSensitivityY);

        // Smooth delta (exponential smoothing)
        float t = 1f - Mathf.Exp(-lookSmoothing * dt);
        _smoothedLookDelta = Vector2.Lerp(_smoothedLookDelta, lookDelta, t);

        // Integrate yaw/pitch
        _yaw += _smoothedLookDelta.x;

        if (_yaw > 360f || _yaw < -360f)
            _yaw = Mathf.Repeat(_yaw, 360f);

        _pitch = Mathf.Clamp(_pitch - _smoothedLookDelta.y, pitchMin, pitchMax);

        if (yawPivot != null)
            yawPivot.rotation = Quaternion.Euler(0f, _yaw, 0f);

        if (pitchPivot != null)
            pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }
}

