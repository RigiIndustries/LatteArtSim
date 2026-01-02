using UnityEngine;

/// <summary>
/// Global runtime settings singleton.
/// Persists across scene loads and exposes simple toggles used by gameplay/UI.
/// </summary>
public class GameSettings : MonoBehaviour
{
    /// <summary>
    /// Active singleton instance (created from the first loaded GameSettings object).
    /// </summary>
    public static GameSettings Instance { get; private set; }

    // ===================== Inspector fields =====================

    [Header("Debug")]
    [Tooltip("Enables the in-game debug overlay UI.")]
    public bool debugOverlayEnabled = false;

    [Header("Gameplay")]
    [Tooltip("If true, disables milk budget limits and allows infinite pouring.")]
    public bool infinitePourEnabled = false;

    [Header("UI")]
    [Tooltip("If true, shows quick settings UI.")]
    public bool quickSettingsEnabled = true;

    // ===================== Unity hooks =====================

    void Awake()
    {
        // Enforce a single persistent instance.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ===================== Public API =====================

    /// <summary>
    /// Enables/disables the debug overlay.
    /// </summary>
    public void SetDebugOverlay(bool enabled)
    {
        debugOverlayEnabled = enabled;
    }

    /// <summary>
    /// Enables/disables infinite pour mode.
    /// </summary>
    public void SetInfinitePour(bool enabled)
    {
        infinitePourEnabled = enabled;
    }

    /// <summary>
    /// Enables/disables the quick settings UI.
    /// </summary>
    public void SetQuickSettings(bool enabled)
    {
        quickSettingsEnabled = enabled;
    }
}
