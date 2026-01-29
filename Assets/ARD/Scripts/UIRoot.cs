using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class UIRoot : MonoBehaviour
{
    private GameObject _mainMenuCanvas;
    private GameObject _hudCanvas;
    private GameObject _pauseCanvas;
    private GameObject _connectingOverlay;

    private void Start()
    {
        CacheSceneUI();
    }

    private void CacheSceneUI()
    {
        _mainMenuCanvas = FindWithTagOrNull("MainMenuCanvas");
        _hudCanvas = FindWithTagOrNull("HudCanvas");
        _pauseCanvas = FindWithTagOrNull("PauseCanvas");
        _connectingOverlay = FindWithTagOrNull("ConnectingOverlay");
    }

    public void ShowMainMenu()
    {
        Set(_mainMenuCanvas, true);
        Set(_hudCanvas, false);
        Set(_pauseCanvas, false);
        // connecting overlay controlled separately
        Time.timeScale = 1f;
    }

    public void ShowInGame()
    {
        Set(_mainMenuCanvas, false);
        Set(_hudCanvas, true);
        Set(_pauseCanvas, false);
        Time.timeScale = 1f;
    }

    public void ShowConnecting(bool show)
    {
        Set(_connectingOverlay, show);
    }

    public void TogglePause()
    {
        if (_pauseCanvas == null) return;

        bool next = !_pauseCanvas.activeSelf;
        Set(_pauseCanvas, next);
        Time.timeScale = next ? 0f : 1f;
    }

    private static void Set(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }

    private static GameObject FindWithTagOrNull(string tag)
    {
        try { return GameObject.FindGameObjectWithTag(tag); }
        catch { return null; } // tag may not exist yet
    }
}
