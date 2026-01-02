using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI fill bar that visualizes the remaining milk budget.
/// - Reads usage from HandPourInput.MilkUsed01.
/// - Hides the entire UI when GameSettings.infinitePourEnabled is enabled.
/// </summary>
public class MilkBudgetBar : MonoBehaviour
{
    // ===================== Inspector fields =====================

    [Tooltip("HandPourInput that tracks milk usage.")]
    public HandPourInput handPour;

    [Tooltip("UI Image with type=Filled for the bar.")]
    public Image fillImage;

    [Tooltip("Root GameObject (Canvas or panel) of the milk budget UI.")]
    public GameObject root;   // assign MilkBudgetCanvas or MilkBudgetPanel

    // ===================== Unity hooks =====================

    void Update()
    {
        if (!handPour || !fillImage)
            return;

        bool infinitePour = GameSettings.Instance != null &&
                            GameSettings.Instance.infinitePourEnabled;

        if (infinitePour)
        {
            // Hide the whole milk bar UI when infinite pour is on.
            if (root && root.activeSelf)
                root.SetActive(false);

            return; // no need to update fillAmount
        }
        else
        {
            // Ensure it's visible in normal mode.
            if (root && !root.activeSelf)
                root.SetActive(true);
        }

        // Fill represents "used" fraction (0..1).
        fillImage.fillAmount = handPour.MilkUsed01;
    }
}
