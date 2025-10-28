using UnityEngine;

public class OvrCupHandAttach : MonoBehaviour
{
    [Header("Hands (from Building Blocks)")]
    public OVRHand cupHand;          // hand holding the real cup
    public OVRHand pitcherHand;      // other hand (the pitcher)

    [Header("Targets")]
    public Transform cupRoot;        // AR cup root (SurfaceDisk should be child)
    public Transform spoutTarget;

    [Header("Calibration Zones (world-space boxes)")]
    public Transform cupZoneCenter;
    public Vector3 cupZoneSize = new Vector3(0.10f, 0.10f, 0.10f);
    public Transform pitcherZoneCenter;
    public Vector3 pitcherZoneSize = new Vector3(0.10f, 0.12f, 0.10f);

    [Header("Zone Visuals (optional)")]
    public GameObject cupZoneVisual;
    public GameObject pitcherZoneVisual;
    [Range(0f,1f)] public float zoneAlpha = 0.25f;

    [Header("Calibration Settings")]
    public float holdSeconds = 3f;
    public float maxHandSpeed = 0.25f;
    public Vector3 spoutLocalOffset = new Vector3(0f, 0f, 0.02f);

    [Header("Follow Tuning (after calibration)")]
    [Range(0,1)] public float posSmooth = 0.2f;
    [Range(0,1)] public float rotSmooth = 0.2f;
    public bool followAfterCalibration = true;

    [Header("Surface visibility control")]
    public Renderer[] surfaceRenderers;

    // runtime
    OVRSkeleton _cupSkel, _pitchSkel;
    Pose _palmToCup, _lastPose;
    bool _hasLast;
    float _countdown;
    bool _calibrated;
    Vector3 _lastCupPalmPos, _lastPitchPalmPos;
    bool _haveLastVel;

    public bool Calibrated => _calibrated;
    public float Countdown => Mathf.Clamp(holdSeconds - _countdown, 0f, holdSeconds);

    void Reset() { cupRoot = transform; }

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

    // -------------------- Calibration Flow --------------------
    void RunCalibration()
    {
        if (!cupRoot || !cupHand || !pitcherHand) return;
        if (!cupHand.IsTracked || !pitcherHand.IsTracked) { ResetCountdown(); return; }

        if (!TryGetCupPalm(out var cupPalm) || !TryGetPitchPalm(out var pitchPalm))
        {
            ResetCountdown(); return;
        }

        bool cupInside = PointInBox(cupPalm.position, cupZoneCenter, cupZoneSize);
        bool pitInside = PointInBox(pitchPalm.position, pitcherZoneCenter, pitcherZoneSize);
        if (!cupInside || !pitInside) { ResetCountdown(); return; }

        float cupSpeed = 0f, pitSpeed = 0f;
        if (_haveLastVel)
        {
            cupSpeed = Vector3.Distance(_lastCupPalmPos, cupPalm.position) / Mathf.Max(Time.deltaTime, 1e-5f);
            pitSpeed = Vector3.Distance(_lastPitchPalmPos, pitchPalm.position) / Mathf.Max(Time.deltaTime, 1e-5f);
        }
        _lastCupPalmPos = cupPalm.position;
        _lastPitchPalmPos = pitchPalm.position;
        _haveLastVel = true;

        if (cupSpeed > maxHandSpeed || pitSpeed > maxHandSpeed) { ResetCountdown(); return; }

        _countdown += Time.deltaTime;

        if (_countdown >= holdSeconds)
        {
            _palmToCup = Multiply(Inverse(cupPalm), new Pose(cupRoot.position, cupRoot.rotation));

            if (spoutTarget && TryGetIndexTip(pitcherHand, _pitchSkel, out var indexTip))
            {
                var spoutWorld = new Pose(indexTip.position + indexTip.rotation * spoutLocalOffset, indexTip.rotation);
                spoutTarget.SetPositionAndRotation(spoutWorld.position, spoutWorld.rotation);
            }

            _calibrated = true;
            _hasLast = false;

            SetSurfaceVisible(true);
            if (cupZoneVisual) cupZoneVisual.SetActive(false);
            if (pitcherZoneVisual) pitcherZoneVisual.SetActive(false);
        }
    }

    void ResetCountdown()
    {
        _countdown = 0f;
        _haveLastVel = false;
    }

    // -------------------- Follow After Calibration --------------------
    void FollowCupHand()
    {
        if (!cupHand || !cupHand.IsTracked) { _hasLast = false; return; }
        if (!TryGetCupPalm(out var cupPalm)) { _hasLast = false; return; }

        var target = Multiply(cupPalm, _palmToCup);

        // keep upright (flatten rotation)
        Vector3 fwd = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        Quaternion uprightRot = Quaternion.LookRotation(fwd, Vector3.up);

        if (!_hasLast) { _lastPose = new Pose(target.position, uprightRot); _hasLast = true; }

        float kp = 1f - Mathf.Clamp01(posSmooth);
        float kr = 1f - Mathf.Clamp01(rotSmooth);

        Vector3 p = Vector3.Lerp(_lastPose.position, target.position, kp);
        Quaternion r = Quaternion.Slerp(_lastPose.rotation, uprightRot, kr);

        _lastPose = new Pose(p, r);
        cupRoot.SetPositionAndRotation(p, r);
    }

    // -------------------- Pose helpers --------------------
    void CacheSkeletons()
    {
        if (cupHand && !_cupSkel) _cupSkel = cupHand.GetComponent<OVRSkeleton>();
        if (pitcherHand && !_pitchSkel) _pitchSkel = pitcherHand.GetComponent<OVRSkeleton>();
    }

    // --- main cup palm logic: wrist position + upright rotation ---
    bool TryGetCupPalm(out Pose palm)
    {
        palm = default;

        if (_cupSkel && _cupSkel.IsDataValid && _cupSkel.IsDataHighConfidence && _cupSkel.Bones != null)
        {
            Transform wrist = null;
            foreach (var b in _cupSkel.Bones)
            {
                if (b.Id == OVRSkeleton.BoneId.Hand_WristRoot && b.Transform)
                {
                    wrist = b.Transform;
                    break;
                }
            }

            if (wrist)
            {
                // position at wrist
                Vector3 pos = wrist.position;

                // orientation: derive palm facing direction using middle finger base if possible
                Vector3 palmForward = wrist.forward;
                Vector3 palmUp = Vector3.up;

                foreach (var b in _cupSkel.Bones)
                {
                    if (b.Id == OVRSkeleton.BoneId.Hand_Middle1 && b.Transform)
                    {
                        Vector3 midDir = (b.Transform.position - wrist.position).normalized;
                        palmForward = Vector3.Cross(midDir, wrist.right).normalized;
                        break;
                    }
                }

                // flatten rotation to world up
                Vector3 flatFwd = Vector3.ProjectOnPlane(palmForward, Vector3.up).normalized;
                if (flatFwd.sqrMagnitude < 1e-6f) flatFwd = Vector3.forward;
                Quaternion rot = Quaternion.LookRotation(flatFwd, Vector3.up);

                palm = new Pose(pos, rot);
                return true;
            }
        }

        if (cupHand && cupHand.PointerPose != null)
        {
            Vector3 pos = cupHand.PointerPose.position;
            Vector3 fwd = Vector3.ProjectOnPlane(cupHand.PointerPose.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            palm = new Pose(pos, Quaternion.LookRotation(fwd, Vector3.up));
            return true;
        }

        return false;
    }

    bool TryGetPitchPalm(out Pose palm)
    {
        palm = default;
        if (_pitchSkel && _pitchSkel.IsDataValid && _pitchSkel.IsDataHighConfidence && _pitchSkel.Bones != null)
        {
            foreach (var b in _pitchSkel.Bones)
            {
                if (b.Id == OVRSkeleton.BoneId.Hand_WristRoot && b.Transform)
                {
                    palm = new Pose(b.Transform.position, b.Transform.rotation);
                    return true;
                }
            }
        }
        if (pitcherHand && pitcherHand.PointerPose != null)
        {
            palm = new Pose(pitcherHand.PointerPose.position, pitcherHand.PointerPose.rotation);
            return true;
        }
        return false;
    }

    static bool TryGetIndexTip(OVRHand hand, OVRSkeleton skel, out Pose tip)
    {
        tip = default;
        if (skel && skel.IsDataValid && skel.IsDataHighConfidence && skel.Bones != null)
        {
            foreach (var b in skel.Bones)
            {
                if (b.Id == OVRSkeleton.BoneId.Hand_IndexTip && b.Transform)
                {
                    tip = new Pose(b.Transform.position, b.Transform.rotation);
                    return true;
                }
            }
        }
        if (hand && hand.PointerPose != null)
        {
            tip = new Pose(hand.PointerPose.position, hand.PointerPose.rotation);
            return true;
        }
        return false;
    }

    // -------------------- Visual helpers --------------------
    void ApplyZoneVisual(GameObject go, Vector3 size, float alpha)
    {
        if (!go) return;
        go.transform.localScale = size;
        var rends = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
            foreach (var m in r.materials)
                SetMaterialTransparent(m, alpha);
    }

    void SetSurfaceVisible(bool visible)
    {
        if (surfaceRenderers == null) return;
        foreach (var r in surfaceRenderers)
            if (r) r.enabled = visible;
    }

    static void SetMaterialTransparent(Material m, float alpha)
    {
        if (!m) return;
        Color c = m.color; c.a = Mathf.Clamp01(alpha); m.color = c;
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

    // -------------------- Math & gizmos --------------------
    static bool PointInBox(Vector3 p, Transform center, Vector3 size)
    {
        if (!center) return false;
        var local = Quaternion.Inverse(center.rotation) * (p - center.position);
        var half = size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    static Pose Multiply(in Pose a, in Pose b) =>
        new Pose(a.position + a.rotation * b.position, a.rotation * b.rotation);

    static Pose Inverse(in Pose p)
    {
        var ri = Quaternion.Inverse(p.rotation);
        return new Pose(ri * (-p.position), ri);
    }

    void OnDrawGizmosSelected()
    {
        if (cupZoneCenter)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
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
