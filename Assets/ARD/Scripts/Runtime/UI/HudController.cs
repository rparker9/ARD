using TMPro;
using UnityEngine;

/// <summary>
/// 
/// </summary>
public sealed class HUDController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text promptText;

    [Header("Behavior")]
    [Tooltip("How often to refresh (unscaled time). Lower = more responsive, higher = cheaper.")]
    [SerializeField] private float refreshRate = 0.10f;

    private PlayerInteractionController _pic;
    private float _nextRefresh;

    private void Awake()
    {
        if (root == null) root = gameObject;
        SetVisible(false);
    }

    private void OnEnable()
    {
        TryBindToLocalPlayer();
        _nextRefresh = 0f;
    }

    private void Update()
    {
        if (_pic == null)
        {
            TryBindToLocalPlayer();
            return;
        }

        if (Time.unscaledTime < _nextRefresh)
            return;

        _nextRefresh = Time.unscaledTime + refreshRate;
        Refresh();
    }

    private void OnDisable()
    {
        Unhook();
    }

    private void TryBindToLocalPlayer()
    {
        // Simple binding: find the local owner's PlayerInteractionController.
        // Replace with your own player registry later if desired.
        var all = FindObjectsByType<PlayerInteractionController>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {   
            var pic = all[i];
            if (pic != null && pic.IsOwner)
            {
                Bind(pic);
                return;
            }
        }
    }

    private void Bind(PlayerInteractionController pic)
    {
        Unhook();

        _pic = pic;
        _pic.OptionsChanged += OnOptionsChanged;
        _pic.FocusChanged += OnFocusChanged;

        Refresh();
    }

    private void Unhook()
    {
        if (_pic != null)
        {
            _pic.OptionsChanged -= OnOptionsChanged;
            _pic.FocusChanged -= OnFocusChanged;
            _pic = null;
        }
    }

    private void OnOptionsChanged() => Refresh();
    private void OnFocusChanged() => Refresh();

    private void Refresh()
    {
        if (_pic == null || promptText == null)
        {
            SetVisible(false);
            return;
        }

        var options = _pic.CurrentOptions;
        if (options == null || options.Count == 0)
        {
            SetVisible(false);
            return;
        }

        // Convention:
        // - First option is the primary (E)
        // - Second option (if present) is Throw (Q) in your current design
        string line1 = options.Count >= 1 ? $"E: {options[0].Label}" : null;
        string line2 = options.Count >= 2 ? $"Q: {options[1].Label}" : null;

        if (string.IsNullOrEmpty(line1) && string.IsNullOrEmpty(line2))
        {
            SetVisible(false);
            return;
        }

        promptText.text = (string.IsNullOrEmpty(line2)) ? line1 : (line1 + "\n" + line2);
        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        if (root != null)
            root.SetActive(visible);
        else
            gameObject.SetActive(visible);
    }
}
