using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class AppFlow : MonoBehaviour
{
    private NetSession _net;
    private UIRoot _ui;

    private void Start()
    {
        _net = GetComponent<NetSession>();
        _ui = GetComponent<UIRoot>();

        _net.Connected += OnConnected;
        _net.Disconnected += OnDisconnected;

        // Single-scene: just show the main menu UI.
        _ui.ShowMainMenu(); 
    }

    public void HostGame()
    {
        _ui.ShowConnecting(true);
        _net.StartHost();
    }

    public void JoinGame(string address)
    {
        _ui.ShowConnecting(true);
        _net.StartClient(address);
    }

    public void DisconnectToMenu()
    {
        _ui.ShowConnecting(true);
        _net.Shutdown();
        // OnDisconnected will finish the transition.
    }

    private void OnConnected()
    {
        _ui.ShowInGame();
        _ui.ShowConnecting(false);
    }

    private void OnDisconnected()
    {
        _ui.ShowMainMenu();
        _ui.ShowConnecting(false);
    }
}
