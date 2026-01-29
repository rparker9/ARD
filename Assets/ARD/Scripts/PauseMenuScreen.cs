using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PauseMenuScreen : MonoBehaviour
{
    private UIRoot _ui;
    private AppFlow _flow;

    private void Start()
    {
        _ui = AppRoot.Instance.GetComponent<UIRoot>();
        _flow = AppRoot.Instance.GetComponent<AppFlow>();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            _ui.TogglePause();
    }

    public void OnResumePressed()
    {
        _ui.TogglePause();
    }

    public void OnDisconnectPressed()
    {
        // Ensure unpaused before leaving
        if (Time.timeScale == 0f)
            _ui.TogglePause();

        _flow.DisconnectToMenu();
    }
}
