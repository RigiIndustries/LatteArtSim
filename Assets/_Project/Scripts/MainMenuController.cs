using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Main menu UI controller.
/// - Switches between main and options panels.
/// - Loads the latte scene on start.
/// - Writes option toggles into GameSettings(debug overlay, infinite pour, quick settings).
/// </summary>
public class MainMenuController : MonoBehaviour
{
    // ===================== Inspector fields =====================

    [Header("Scene")]
    [Tooltip("Scene name to load when the user starts the latte sim.")]
    [SerializeField] private string latteSceneName = "LatteScene";

    [Header("Panels")]
    [Tooltip("Main menu panel root.")]
    [SerializeField] private GameObject mainPanel;

    [Tooltip("Options panel root.")]
    [SerializeField] private GameObject optionsPanel;

    [Header("Controls")]
    [Tooltip("Toggles debug overlay visibility.")]
    [SerializeField] private Toggle debugOverlayToggle;

    [Tooltip("Toggles infinite pour mode.")]
    [SerializeField] private Toggle infinitePourToggle;

    [Tooltip("Toggles quick settings UI in the sim.")]
    [SerializeField] private Toggle quickSettingsToggle;

    // ===================== Unity hooks =====================

    void Start()
    {
        // Default panel state.
        if (mainPanel) mainPanel.SetActive(true);
        if (optionsPanel) optionsPanel.SetActive(false);

        // Initialize toggle UI from current settings.
        if (GameSettings.Instance != null)
        {
            if (debugOverlayToggle)
                debugOverlayToggle.isOn = GameSettings.Instance.debugOverlayEnabled;

            if (infinitePourToggle)
                infinitePourToggle.isOn = GameSettings.Instance.infinitePourEnabled;

            if (quickSettingsToggle)
                quickSettingsToggle.isOn = GameSettings.Instance.quickSettingsEnabled;
        }
    }

    // ===================== Button callbacks =====================

    /// <summary>
    /// Loads the configured latte simulation scene.
    /// </summary>
    public void OnStartClicked()
    {
        SceneManager.LoadScene(latteSceneName);
    }

    /// <summary>
    /// Toggles visibility between the main panel and options panel.
    /// </summary>
    public void OnOptionsClicked()
    {
        if (!mainPanel || !optionsPanel) return;

        bool showOptions = !optionsPanel.activeSelf;
        optionsPanel.SetActive(showOptions);
        mainPanel.SetActive(!showOptions);
    }

    /// <summary>
    /// Quits the application (no-op in editor).
    /// </summary>
    public void OnQuitClicked()
    {
        Application.Quit();
    }

    // ===================== Toggle callbacks =====================

    /// <summary>
    /// Updates the debug overlay setting.
    /// </summary>
    public void OnDebugOverlayToggleChanged(bool value)
    {
        if (GameSettings.Instance != null)
            GameSettings.Instance.SetDebugOverlay(value);
    }

    /// <summary>
    /// Updates the infinite pour setting.
    /// </summary>
    public void OnInfinitePourToggleChanged(bool value)
    {
        if (GameSettings.Instance != null)
            GameSettings.Instance.SetInfinitePour(value);
    }

    /// <summary>
    /// Updates the quick settings UI setting.
    /// </summary>
    public void OnQuickSettingsToggleChanged(bool value)
    {
        if (GameSettings.Instance != null)
            GameSettings.Instance.SetQuickSettings(value);
    }
}
