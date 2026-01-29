using UnityEngine;
using TMPro;

public sealed class MainMenuScreen : MonoBehaviour
{
    [SerializeField] private TMP_InputField addressInput;

    private AppFlow _flow;

    private void Start()
    {
        _flow = AppRoot.Instance.GetComponent<AppFlow>();
    }

    public void OnHostPressed()
    {
        _flow.HostGame();
    }

    public void OnJoinPressed()
    {
        string addr = addressInput != null ? addressInput.text : "127.0.0.1";
        if (string.IsNullOrWhiteSpace(addr)) addr = "127.0.0.1";
        _flow.JoinGame(addr);
    }

    public void OnQuitPressed()
    {
        Application.Quit();
    }
}
