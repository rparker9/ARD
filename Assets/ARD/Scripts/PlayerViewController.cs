using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Owner-only presentation layer: camera enable + smooth local yaw/pitch.
/// This does NOT decide gameplay outcomes (server does).
/// </summary>
public sealed class PlayerViewController : NetworkBehaviour
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

    private PlayerControls _controls;

    private float _yaw;
    private float _pitch;

    private Vector2 _smoothedLookDelta;
    private GameObject _fallbackCamera;

    public float PitchDegrees => _pitch;
    public float YawDegrees => _yaw;

    private void OnDisable()
    {
        // If we get disabled unexpectedly (scene changes/editor stop), clean up input.
        if (_controls != null)
        {
            _controls.Gameplay.Disable();
            _controls.Dispose();
            _controls = null;
        }
    }

    public override void OnNetworkSpawn()
    {
        bool enable = IsOwner;

        // Enable/disable player camera
        if (playerCamera != null)
            playerCamera.gameObject.SetActive(enable);

        // Early out if not owner
        enabled = enable;
        if (!enable)
            return;

        // Disable fallback camera in the Game scene for local player
        if (_fallbackCamera == null)
            _fallbackCamera = GameObject.FindGameObjectWithTag(FallbackCameraTag);

        if (_fallbackCamera != null)
            _fallbackCamera.SetActive(false);

        // Initialize yaw from current pivot so we don't snap.
        if (yawPivot != null)
            _yaw = yawPivot.eulerAngles.y;

        // Lock cursor for FPS
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Setup input
        _controls = new PlayerControls();
        _controls.Gameplay.Enable();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            // Restore fallback camera (if we're staying in this scene)
            if (_fallbackCamera == null)
                _fallbackCamera = GameObject.FindGameObjectWithTag(FallbackCameraTag);

            if (_fallbackCamera != null)
                _fallbackCamera.SetActive(true);

            // Unlock cursor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Clean up input
        if (_controls != null)
        {
            _controls.Gameplay.Disable();
            _controls.Dispose();
            _controls = null;
        }
    }

    private void Update()
    {
        if (_controls == null) return;

        // Mouse delta this frame (Input System "Delta" binding)
        Vector2 lookDelta = _controls.Gameplay.Look.ReadValue<Vector2>();

        // Sensitivity (applied consistently to both axes)
        lookDelta = new Vector2(
            lookDelta.x * lookSensitivityX,
            lookDelta.y * lookSensitivityY);

        float dt = Time.deltaTime;

        // Smooth delta (exponential smoothing)
        float t = 1f - Mathf.Exp(-lookSmoothing * dt);
        _smoothedLookDelta = Vector2.Lerp(_smoothedLookDelta, lookDelta, t);

        // Integrate yaw/pitch
        _yaw += _smoothedLookDelta.x;

        // Keep yaw bounded (avoids float drift over long sessions)
        if (_yaw > 360f || _yaw < -360f)
            _yaw = Mathf.Repeat(_yaw, 360f);

        _pitch = Mathf.Clamp(_pitch - _smoothedLookDelta.y, pitchMin, pitchMax);

        if (yawPivot != null)
            yawPivot.rotation = Quaternion.Euler(0f, _yaw, 0f);

        if (pitchPivot != null)
            pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }
}
