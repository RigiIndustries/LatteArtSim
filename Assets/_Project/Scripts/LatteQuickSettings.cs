using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Quick settings UI handler for the latte simulator.
/// - Shows/hides the panel based on GameSettings.quickSettingsEnabled.
/// - Exposes button callbacks to reset the sim, restart calibration (reload scene),
///   and return to the main menu.
/// </summary>
public class LatteQuickSettings : MonoBehaviour
{
    // ===================== Inspector fields =====================

    [Header("Refs")]
    [Tooltip("Latte simulation controller to reset.")]
    [SerializeField] private LatteSimCompute sim;

    [Tooltip("Pour input to reset milk budget.")]
    [SerializeField] private HandPourInput handPour;

    [Header("UI")]
    [Tooltip("Root of the QuickSettings canvas/panel.")]
    [SerializeField] private GameObject panelRoot;

    [Header("Scene Names")]
    [Tooltip("Scene name of the latte simulator (used to restart calibration by reloading).")]
    [SerializeField] private string latteSceneName = "LatteArtSim";

    [Tooltip("Scene name of the main menu.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    // ===================== Unity hooks =====================

    void Start()
    {
        // QuickSettings global show/hide.
        bool enabled = GameSettings.Instance == null || GameSettings.Instance.quickSettingsEnabled;

        if (panelRoot)
            panelRoot.SetActive(enabled);
    }

    // ===================== UI callbacks =====================

    /// <summary>
    /// Clears the simulation buffers and resets the milk budget.
    /// </summary>
    public void OnResetSimClicked()
    {
        if (sim != null)
        {
            sim.Clear();
        }
        else
        {
            Debug.LogWarning("LatteQuickSettings: sim reference not assigned.");
        }

        if (handPour != null)
        {
            handPour.ResetMilk();
        }
        else
        {
            Debug.LogWarning("LatteQuickSettings: handPour reference not assigned.");
        }
    }

    /// <summary>
    /// Restarts the calibration step by reloading the latte scene.
    /// </summary>
    public void OnResetCalibrationClicked()
    {
        if (string.IsNullOrEmpty(latteSceneName))
        {
            Debug.LogWarning("LatteQuickSettings: latteSceneName is empty.");
            return;
        }

        SceneManager.LoadScene(latteSceneName);
    }

    /// <summary>
    /// Returns to the main menu scene.
    /// </summary>
    public void OnReturnToMainMenuClicked()
    {
        if (string.IsNullOrEmpty(mainMenuSceneName))
        {
            Debug.LogWarning("LatteQuickSettings: mainMenuSceneName is empty.");
            return;
        }

        SceneManager.LoadScene(mainMenuSceneName);
    }
}
