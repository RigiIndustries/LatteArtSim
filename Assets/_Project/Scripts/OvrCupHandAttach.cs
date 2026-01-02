using UnityEngine;

/// <summary>
/// Oculus/OVR-based calibration + tracking for cup and pitcher.
/// Flow:
/// 1) Calibration: user holds both hands inside two world-space zones for a duration while staying below a speed threshold.
///    - Captures an offset from cup palm to cupRoot.
///    - Captures an offset from pitcher "grip" to spoutTarget (so the spout follows where the pitcher actually sits).
/// 2) After calibration: cupRoot follows the cup hand with smoothing, and spoutTarget follows the pitcher grip.
/// </summary>
public class OvrCupHandAttach : MonoBehaviour
{
    // ===================== Inspector fields =====================

    [Header("Hands (from Building Blocks)")]
    [Tooltip("Hand holding the real cup.")]
    public OVRHand cupHand;

    [Tooltip("Other hand (holding the pitcher).")]
    public OVRHand pitcherHand;

    [Header("Targets")]
    [Tooltip("AR cup root transform (SurfaceDisk should be a child).")]
    public Transform cupRoot;

    [Tooltip("Sphere/marker that represents the virtual spout position and rotation.")]
    public Transform spoutTarget;

    [Header("Calibration Zones (world-space boxes)")]
    [Tooltip("Transform that defines the center/rotation of the cup calibration box.")]
    public Transform cupZoneCenter;

    [Tooltip("World-space size of the cup calibration box (meters).")]
    public Vector3 cupZoneSize = new Vector3(0.10f, 0.10f, 0.10f);

    [Tooltip("Transform that defines the center/rotation of the pitcher calibration box.")]
    public Transform pitcherZoneCenter;

    [Tooltip("World-space size of the pitcher calibration box (meters).")]
    public Vector3 pitcherZoneSize = new Vector3(0.10f, 0.10f, 0.10f);

    [Header("Zone visuals (optional)")]
    [Tooltip("Optional visual object for the cup zone (scaled to cupZoneSize).")]
    public GameObject cupZoneVisual;

    [Tooltip("Optional visual object for the pitcher zone (scaled to pitcherZoneSize).")]
    public GameObject pitcherZoneVisual;

    [Tooltip("Alpha applied to zone materials (best effort, depends on shader).")]
    [Range(0f, 1f)] public float zoneAlpha = 0.25f;

    [Header("Calibration Settings")]
    [Tooltip("How long hands must stay inside the zones (seconds).")]
    public float holdSeconds = 3f;

    [Tooltip("Max allowed hand speed during calibration (m/s).")]
    public float maxHandSpeed = 0.25f;

    [Header("Follow Tuning (after calibration)")]
    [Tooltip("Position smoothing amount. Higher values = more smoothing (slower response).")]
    [Range(0, 1)] public float posSmooth = 0.2f;

    [Tooltip("Rotation smoothing amount. Higher values = more smoothing (slower response).")]
    [Range(0, 1)] public float rotSmooth = 0.2f;

    [Tooltip("If true, cupRoot follows the cup hand after calibration.")]
    public bool followAfterCalibration = true;

    [Header("Surface visibility control")]
    [Tooltip("Renderers to enable once calibration completes (hidden before).")]
    public Renderer[] surfaceRenderers;

    // ===================== Runtime state =====================

    OVRSkeleton _cupSkel, _pitchSkel;

    Pose _palmToCup;      // cup palm -> cupRoot (captured at calibration)
    Pose _gripToSpout;    // pitcher grip -> spoutTarget (captured at calibration)

    Pose _lastPose;
    bool _hasLast;

    float _countdown;
    bool _calibrated;

    Vector3 _lastCupPalmPos, _lastPitchPalmPos;
    bool _haveLastVel;

    // ===================== Public state =====================

    /// <summary>True once calibration has completed successfully.</summary>
    public bool Calibrated => _calibrated;

    /// <summary>UI-friendly countdown (0..holdSeconds).</summary>
    public float Countdown => Mathf.Clamp(holdSeconds - _countdown, 0f, holdSeconds);

    // ===================== Unity hooks =====================

    void Reset()
    {
        cupRoot = transform;
    }

    void Awake()
    {
        CacheSkeletons();

        ApplyZoneVisual(cupZoneVisual, cupZoneSize, zoneAlpha);
        ApplyZoneVisual(pitcherZoneVisual, pitcherZoneSize, zoneAlpha);

        SetSurfaceVisible(false);
    }

    void Update()
    {
        CacheSkeletons();

        if (!_calibrated)
            RunCalibration();
        else if (followAfterCalibration)
            FollowCupHand();
    }

    // ===================== Calibration flow =====================

    /// <summary>
    /// Runs the calibration process until completion:
    /// - Both palms must be tracked, inside their respective zones,
    /// - and moving slower than maxHandSpeed for holdSeconds.
    /// </summary>
    void RunCalibration()
    {
        if (!cupRoot || !cupHand || !pitcherHand) return;

        if (!cupHand.IsTracked || !pitcherHand.IsTracked)
        {
            ResetCountdown();
            return;
        }

        if (!TryGetCupPalm(out var cupPalm) || !TryGetPitchPalm(out var pitchPalm))
        {
            ResetCountdown();
            return;
        }

        bool cupInside = PointInBox(cupPalm.position, cupZoneCenter, cupZoneSize);
        bool pitInside = PointInBox(pitchPalm.position, pitcherZoneCenter, pitcherZoneSize);
        if (!cupInside || !pitInside)
        {
            ResetCountdown();
            return;
        }

        // Speed gating to ensure "stable hold" before capturing offsets.
        float cupSpeed = 0f, pitSpeed = 0f;
        if (_haveLastVel)
        {
            cupSpeed = Vector3.Distance(_lastCupPalmPos, cupPalm.position) / Mathf.Max(Time.deltaTime, 1e-5f);
            pitSpeed = Vector3.Distance(_lastPitchPalmPos, pitchPalm.position) / Mathf.Max(Time.deltaTime, 1e-5f);
        }

        _lastCupPalmPos = cupPalm.position;
        _lastPitchPalmPos = pitchPalm.position;
        _haveLastVel = true;

        if (cupSpeed > maxHandSpeed || pitSpeed > maxHandSpeed)
        {
            ResetCountdown();
            return;
        }

        _countdown += Time.deltaTime;

        if (_countdown >= holdSeconds)
        {
            // --- Cup: bake cup palm -> cupRoot offset ---
            _palmToCup = Multiply(Inverse(cupPalm), new Pose(cupRoot.position, cupRoot.rotation));

            // --- Spout: bake pitcher GRIP -> spout offset ---
            if (spoutTarget && TryGetPitchGrip(out var pitchGrip))
            {
                var spoutWorld = new Pose(spoutTarget.position, spoutTarget.rotation);
                _gripToSpout = Multiply(Inverse(pitchGrip), spoutWorld);
            }

            _calibrated = true;
            _hasLast = false;

            SetSurfaceVisible(true);

            if (cupZoneVisual) cupZoneVisual.SetActive(false);
            if (pitcherZoneVisual) pitcherZoneVisual.SetActive(false);
        }
    }

    /// <summary>
    /// Resets the calibration timer and velocity tracking.
    /// </summary>
    void ResetCountdown()
    {
        _countdown = 0f;
        _haveLastVel = false;
    }

    // ===================== Follow after calibration =====================

    /// <summary>
    /// Drives cupRoot from the cup palm pose and drives spoutTarget from the pitcher grip pose.
    /// </summary>
    void FollowCupHand()
    {
        // --- Cup following cup palm ---
        if (!cupHand || !cupHand.IsTracked) { _hasLast = false; return; }
        if (!TryGetCupPalm(out var cupPalm)) { _hasLast = false; return; }

        var target = Multiply(cupPalm, _palmToCup);

        // Keep upright (flatten rotation).
        Vector3 fwd = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        Quaternion uprightRot = Quaternion.LookRotation(fwd, Vector3.up);

        if (!_hasLast)
        {
            _lastPose = new Pose(target.position, uprightRot);
            _hasLast = true;
        }

        float kp = 1f - Mathf.Clamp01(posSmooth);
        float kr = 1f - Mathf.Clamp01(rotSmooth);

        Vector3 p = Vector3.Lerp(_lastPose.position, target.position, kp);
        Quaternion r = Quaternion.Slerp(_lastPose.rotation, uprightRot, kr);

        _lastPose = new Pose(p, r);
        cupRoot.SetPositionAndRotation(p, r);

        // --- Spout following the GRIP pose, not the wrist ---
        if (spoutTarget && pitcherHand && pitcherHand.IsTracked && TryGetPitchGrip(out var pitchGrip))
        {
            var spoutPose = Multiply(pitchGrip, _gripToSpout);
            spoutTarget.SetPositionAndRotation(spoutPose.position, spoutPose.rotation);
        }
    }

    // ===================== Pose helpers =====================

    /// <summary>
    /// Caches skeleton components for both hands (if present).
    /// </summary>
    void CacheSkeletons()
    {
        if (cupHand && !_cupSkel) _cupSkel = cupHand.GetComponent<OVRSkeleton>();
        if (pitcherHand && !_pitchSkel) _pitchSkel = pitcherHand.GetComponent<OVRSkeleton>();
    }

    bool TryGetCupPalm(out Pose palm) => TryGetPalm(cupHand, _cupSkel, out palm);
    bool TryGetPitchPalm(out Pose palm) => TryGetPalm(pitcherHand, _pitchSkel, out palm);

    /// <summary>
    /// "Palm" pose = wrist root pose (primary) with PointerPose as fallback.
    /// </summary>
    static bool TryGetPalm(OVRHand hand, OVRSkeleton skel, out Pose palm)
    {
        palm = default;

        if (skel && skel.IsDataValid && skel.IsDataHighConfidence && skel.Bones != null)
        {
            foreach (var b in skel.Bones)
            {
                if (b.Id == OVRSkeleton.BoneId.Hand_WristRoot && b.Transform)
                {
                    palm = new Pose(b.Transform.position, b.Transform.rotation);
                    return true;
                }
            }
        }

        if (hand && hand.PointerPose != null)
        {
            palm = new Pose(hand.PointerPose.position, hand.PointerPose.rotation);
            return true;
        }

        return false;
    }

    /// <summary>
    /// "Grip" pose = base of middle finger (Hand_Middle1) to better match a held pitcher.
    /// Falls back to palm pose if not available.
    /// </summary>
    bool TryGetPitchGrip(out Pose grip)
    {
        grip = default;

        if (_pitchSkel && _pitchSkel.IsDataValid && _pitchSkel.IsDataHighConfidence && _pitchSkel.Bones != null)
        {
            Transform middle1 = null;
            foreach (var b in _pitchSkel.Bones)
            {
                if (b.Id == OVRSkeleton.BoneId.Hand_Middle1 && b.Transform)
                {
                    middle1 = b.Transform;
                    break;
                }
            }

            if (middle1 != null)
            {
                grip = new Pose(middle1.position, middle1.rotation);
                return true;
            }
        }

        // Fallback to palm if grip not found.
        return TryGetPitchPalm(out grip);
    }

    /// <summary>
    /// Checks whether a world-space point lies inside a rotated box defined by a center transform and size.
    /// </summary>
    static bool PointInBox(Vector3 p, Transform center, Vector3 size)
    {
        if (!center) return false;

        var local = Quaternion.Inverse(center.rotation) * (p - center.position);
        var half = size * 0.5f;

        return Mathf.Abs(local.x) <= half.x &&
               Mathf.Abs(local.y) <= half.y &&
               Mathf.Abs(local.z) <= half.z;
    }

    static Pose Multiply(in Pose a, in Pose b)
        => new Pose(a.position + a.rotation * b.position, a.rotation * b.rotation);

    static Pose Inverse(in Pose p)
    {
        var ri = Quaternion.Inverse(p.rotation);
        return new Pose(ri * (-p.position), ri);
    }

    // ===================== Zone visuals & surface visibility =====================

    /// <summary>
    /// Scales the visual to match zone size and applies transparency to its materials (best effort).
    /// </summary>
    void ApplyZoneVisual(GameObject go, Vector3 size, float alpha)
    {
        if (!go) return;

        go.transform.localScale = size;

        var rends = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
            foreach (var m in r.materials)
                SetMaterialTransparent(m, alpha);
    }

    /// <summary>
    /// Enables/disables the listed surface renderers.
    /// </summary>
    void SetSurfaceVisible(bool visible)
    {
        if (surfaceRenderers == null) return;

        foreach (var r in surfaceRenderers)
            if (r) r.enabled = visible;
    }

    /// <summary>
    /// Attempts to force a material into a transparent mode and set its alpha.
    /// Supports Standard shader and URP Lit (best effort).
    /// </summary>
    static void SetMaterialTransparent(Material m, float alpha)
    {
        if (!m) return;

        Color c = m.color;
        c.a = Mathf.Clamp01(alpha);
        m.color = c;

        var name = m.shader ? m.shader.name : "";

        if (name == "Standard")
        {
            m.SetFloat("_Mode", 3);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else if (name.Contains("Universal Render Pipeline") && name.Contains("Lit"))
        {
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }

    // ===================== Gizmos =====================

    void OnDrawGizmosSelected()
    {
        if (cupZoneCenter)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
            DrawBoxGizmo(cupZoneCenter.position, cupZoneCenter.rotation, cupZoneSize);
        }

        if (pitcherZoneCenter)
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.25f);
            DrawBoxGizmo(pitcherZoneCenter.position, pitcherZoneCenter.rotation, pitcherZoneSize);
        }
    }

    static void DrawBoxGizmo(Vector3 pos, Quaternion rot, Vector3 size)
    {
        var old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(pos, rot, size);
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = old;
    }
}
