using UnityEngine;

public sealed class AppRoot : MonoBehaviour
{
    public static AppRoot Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Destroy is end-of-frame; deactivate immediately so child EventSystem/AudioListener
            // don't briefly enable and trigger warnings.
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
