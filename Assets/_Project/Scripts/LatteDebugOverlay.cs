using UnityEngine;
using TMPro;
using UnityEngine.UI;
#if UNITY_2019_3_OR_NEWER
using UnityEngine.XR;
#endif

/// <summary>
/// Simple runtime debug overlay for performance + simulation state.
/// - Visibility is driven by GameSettings.debugOverlayEnabled
/// </summary>
public class LatteDebugOverlay : MonoBehaviour
{
    // ===================== Inspector fields =====================

    [Header("Refs")]
    [Tooltip("Latte simulation controller to read values from.")]
    public LatteSimCompute sim;

    [Tooltip("Optional: pour input to display tilt/pour state.")]
    public HandPourInput handPour;

    [Tooltip("Root panel GameObject that should be shown/hidden.")]
    public GameObject panelRoot;

    [Header("Text Outputs (assign ONE of these)")]
    [Tooltip("TextMeshPro output target.")]
    public TMP_Text tmpText;   // TextMeshProUGUI

    [Tooltip("Legacy UI Text output target.")]
    public Text uiText;        // legacy UI Text

    [Header("FPS Settings")]
    [Tooltip("Smoothing factor for FPS readout (higher = more responsive, lower = steadier).")]
    [Range(0.01f, 1f)] public float fpsSmoothing = 0.1f;

    // ===================== Internal state =====================

    float smoothedDeltaTime = 0.016f; // ~60 fps baseline

    TMP_Text ActiveTMP => tmpText;
    Text ActiveUI => uiText;

    // ===================== Unity hooks =====================

    void Awake()
    {
        smoothedDeltaTime = Time.unscaledDeltaTime;
    }

    void Update()
    {
        // Read global toggle (optional singleton).
        bool debugOn = GameSettings.Instance != null &&
                       GameSettings.Instance.debugOverlayEnabled;

        if (panelRoot)
            panelRoot.SetActive(debugOn);

        if (!debugOn)
            return;

        // Choose the active text target once per frame.
        bool hasTMP = ActiveTMP != null;
        bool hasUI  = ActiveUI != null;

        if (!hasTMP && !hasUI)
        {
            Debug.LogWarning("LatteDebugOverlay: no text component assigned (tmpText/uiText).");
            return;
        }

        if (!panelRoot)
        {
            Debug.LogWarning("LatteDebugOverlay: panelRoot not assigned, will not toggle visibility.");
        }

        if (!sim)
        {
            SetText("LatteDebugOverlay:\n\nsim reference is NULL.");
            return;
        }

        // ===================== FPS & frame times =====================

        smoothedDeltaTime = Mathf.Lerp(
            smoothedDeltaTime,
            Time.unscaledDeltaTime,
            fpsSmoothing
        );

        float fps   = (smoothedDeltaTime > 0f) ? 1f / smoothedDeltaTime : 0f;
        float cpuMs = Time.unscaledDeltaTime * 1000f;
        float gpuMs = -1f;

#if UNITY_2019_3_OR_NEWER
        float gpuTimeSec;
        if (XRStats.TryGetGPUTimeLastFrame(out gpuTimeSec))
        {
            gpuMs = gpuTimeSec * 1000f;
            cpuMs = Mathf.Max(0f, cpuMs - gpuMs); // crude split
        }
#endif

        string hwInfo =
            $"FPS: {fps:0.0}\n" +
            $"CPU frame: {cpuMs:0.0} ms\n" +
            (gpuMs > 0f ? $"GPU frame: {gpuMs:0.0} ms\n" : "GPU frame: N/A\n") +
            $"Device: {SystemInfo.deviceModel}\n" +
            $"GPU: {SystemInfo.graphicsDeviceName}\n" +
            $"VRAM: {SystemInfo.graphicsMemorySize} MB\n" +
            $"CPU: {SystemInfo.processorType}\n" +
            $"RAM: {SystemInfo.systemMemorySize} MB\n";

        // ===================== Simulation state =====================

        string simInfo =
            $"Resolution: {sim.resolution}\n" +
            $"Timestep: {sim.timestep:F4}\n" +
            $"Viscosity: {sim.viscosity:F5}\n" +
            $"Dye Dissipation: {sim.dyeDissipation:F3}\n" +
            $"Pressure Iters: {sim.pressureIters}\n" +
            $"Vel Diffuse Iters: {sim.velDiffuseIters}\n";

        // ===================== Pour debug =====================

        string pourInfo = "";
        if (handPour)
        {
            pourInfo =
                $"\n\nPour Tilt: {handPour.CurrentTiltAngleDeg:0.0}°\n" +
                $"Tilt Satisfied: {handPour.TiltSatisfied}\n" +
                $"Pouring: {handPour.IsPouring}";
        }

        SetText(hwInfo + "\n" + simInfo + pourInfo);
    }

    // ===================== Output =====================

    /// <summary>
    /// Writes to any assigned text targets (TMP and/or legacy UI).
    /// </summary>
    void SetText(string s)
    {
        if (tmpText) tmpText.text = s;
        if (uiText)  uiText.text  = s;
    }
}
