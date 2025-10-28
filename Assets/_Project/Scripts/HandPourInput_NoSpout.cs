using UnityEngine;

[DefaultExecutionOrder(20)]
public class HandPourInput_NoSpout : MonoBehaviour
{
    [Header("Required")]
    public OVRHand pitcherHand;          // your pitcher hand (OVRHand)
    public LatteSimCompute sim;          // LatteSimCompute on the surface
    public Collider surfaceCollider;     // MeshCollider on SurfaceDisk

    [Header("Brush (like MousePourInput)")]
    public bool useSimDefaults = true;
    [Range(0.01f, 0.25f)] public float radius   = 0.08f;
    [Range(0f,   1f   )] public float hardness = 0.85f;
    [Range(0f,   1f   )] public float amount   = 0.25f;
    [Tooltip("Spacing as a fraction of brush radius; lower = denser splats.")]
    [Range(0.05f, 1f)] public float spacing = 0.25f;

    [Header("Pour control")]
    [Tooltip("Ignore tilt gating and always pour when hitting the surface (good for testing).")]
    public bool forceAlwaysPour = true;
    [Tooltip("Require palm to be tilted at least this much from upright before pouring (deg).")]
    [Range(0f, 90f)] public float tiltDegreesToStart = 15f;
    [Tooltip("Max ray length (meters).")]
    public float rayLength = 0.35f;
    public LayerMask raycastMask = ~0;

    [Header("In-headset beam")]
    public bool showBeam = true;
    [Range(0.0005f, 0.02f)] public float beamWidth = 0.004f;
    [Range(0f,1f)] public float beamAlpha = 0.9f;
    public bool dimWhenNoHit = true;

    [Header("Debug (Scene view)")]
    public bool drawSceneRay = false;

    // ---- internal state ----
    OVRSkeleton _skel;
    Vector2? _lastUV;
    float _lastTime;

    LineRenderer _beam;
    Material _beamMat;
    Color _hitColor, _missColor;

    void Awake()
    {
        if (pitcherHand) _skel = pitcherHand.GetComponent<OVRSkeleton>();
        SetupBeam();
    }

    void OnEnable()
    {
        SetBeamEnabled(showBeam);
        _lastUV = null;
    }

    void OnDisable()
    {
        SetBeamEnabled(false);
        _lastUV = null;
    }

    void Update()
    {
        if (!pitcherHand || !sim || !surfaceCollider) { SetBeamEnabled(false); return; }
        if (!_skel) _skel = pitcherHand.GetComponent<OVRSkeleton>();

        // 1) Get index tip pose + a stable palm frame
        if (!TryGetIndexTip(out Pose tip) || !TryGetPalmFrame(out Vector3 palmPos, out Vector3 palmNormal, out Vector3 palmRight))
        {
            _lastUV = null;
            DrawBeam(tip.position, tip.position, false);
            return;
        }

        // 2) Compute pour direction: gravity projected onto palm plane
        //    This yields the direction liquid would run along the spout rim for your current wrist roll.
        Vector3 gravity = Vector3.down;
        Vector3 pourDir = Vector3.ProjectOnPlane(gravity, palmNormal).normalized;

        // Robust fallback if palm is nearly vertical (projection degenerates)
        if (pourDir.sqrMagnitude < 1e-6f)
        {
            // For a right-hand 90° clockwise pour, −tip.right typically points "down the rim".
            pourDir = (-tip.right).normalized;
        }

        // 3) Optional tilt gating: require palm to be tilted away from upright by a threshold
        bool allowPour = forceAlwaysPour || (Vector3.Angle(palmNormal, Vector3.up) >= tiltDegreesToStart);

        // 4) Raycast
        Vector3 origin = tip.position;
        if (drawSceneRay) Debug.DrawRay(origin, pourDir * rayLength, Color.cyan);

        bool hitSurface = Physics.Raycast(origin, pourDir, out RaycastHit hit, rayLength, raycastMask, QueryTriggerInteraction.Ignore)
                          && hit.collider == surfaceCollider;

        Vector3 end = hitSurface ? hit.point : (origin + pourDir * rayLength);
        DrawBeam(origin, end, hitSurface && allowPour);

        if (!hitSurface || !allowPour)
        {
            _lastUV = null;
            _lastTime = Time.time;
            return;
        }

        // 5) UV & brush
        Vector2 uv = hit.textureCoord;
        float r = useSimDefaults ? sim.defaultRadius   : radius;
        float h = useSimDefaults ? sim.defaultHardness : hardness;
        float a = useSimDefaults ? sim.defaultAmount   : amount;

        // 6) First contact
        if (!_lastUV.HasValue)
        {
            sim.InjectUV(uv, r, h, a, Vector2.zero);
            _lastUV  = uv;
            _lastTime = Time.time;
            return;
        }

        // 7) Stroke placement (MousePourInput-style)
        Vector2 prevUV = _lastUV.Value;
        float dt = Mathf.Max(Time.time - _lastTime, 1e-4f);
        Vector2 seg = uv - prevUV;
        float dist = seg.magnitude;
        Vector2 flowUV = dist > 1e-6f ? (seg / dt) : Vector2.zero;

        float step = Mathf.Max(0.001f, r * spacing);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / step));
        float amtPer = a / steps;

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector2 p = Vector2.Lerp(prevUV, uv, t);
            sim.InjectUV(p, r, h, amtPer, flowUV);
        }

        _lastUV  = uv;
        _lastTime = Time.time;
    }

    // ---------------- palm & tip helpers ----------------

    bool TryGetPalmFrame(out Vector3 pos, out Vector3 normal, out Vector3 right)
    {
        pos = default; normal = default; right = default;

        if (_skel && _skel.IsDataValid && _skel.IsDataHighConfidence && _skel.Bones != null)
        {
            Transform wrist = null, middle1 = null, pinky1 = null, index1 = null;
            foreach (var b in _skel.Bones)
            {
                if (b.Id == OVRSkeleton.BoneId.Hand_WristRoot && b.Transform) wrist = b.Transform;
                if (b.Id == OVRSkeleton.BoneId.Hand_Middle1   && b.Transform) middle1 = b.Transform;
                if (b.Id == OVRSkeleton.BoneId.Hand_Pinky1    && b.Transform) pinky1  = b.Transform;
                if (b.Id == OVRSkeleton.BoneId.Hand_Index1    && b.Transform) index1  = b.Transform;
            }
            if (wrist)
            {
                // Position near the palm base
                pos = wrist.position;

                // Right vector across palm: index base -> pinky base
                if (index1 && pinky1) right = (index1.position - pinky1.position).normalized;
                else right = wrist.right; // fallback

                // Up vector roughly toward fingers
                Vector3 up = (middle1 ? (middle1.position - wrist.position).normalized : wrist.up);
                // Palm normal pointing "out" of the palm
                normal = Vector3.Cross(right, up).normalized;

                // Make sure we have something sane
                if (normal.sqrMagnitude < 1e-6f) normal = wrist.forward;
                if (right.sqrMagnitude  < 1e-6f) right  = wrist.right;

                return true;
            }
        }

        // Fallback to PointerPose if skeleton not ready
        if (pitcherHand && pitcherHand.PointerPose != null)
        {
            var pp = pitcherHand.PointerPose;
            pos = pp.position;
            // Derive a palm-like frame from pointer pose
            right  = pp.right;
            normal = pp.forward;
            return true;
        }

        return false;
    }

    bool TryGetIndexTip(out Pose tip)
    {
        tip = default;
        if (_skel && _skel.IsDataValid && _skel.IsDataHighConfidence && _skel.Bones != null)
        {
            foreach (var b in _skel.Bones)
            {
                if (b.Id == OVRSkeleton.BoneId.Hand_IndexTip && b.Transform)
                {
                    tip = new Pose(b.Transform.position, b.Transform.rotation);
                    return true;
                }
            }
        }
        if (pitcherHand && pitcherHand.PointerPose != null)
        {
            var pp = pitcherHand.PointerPose;
            tip = new Pose(pp.position, pp.rotation);
            return true;
        }
        return false;
    }

    // ---------------- beam ----------------

    void SetupBeam()
    {
        var go = new GameObject("PourBeam");
        go.transform.SetParent(transform, false);
        _beam = go.AddComponent<LineRenderer>();
        _beamMat = new Material(Shader.Find("Unlit/Color"));
        _beam.material = _beamMat;
        _beam.positionCount = 2;
        _beam.useWorldSpace = true;
        _beam.textureMode = LineTextureMode.Stretch;
        _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _beam.receiveShadows = false;
        _beam.numCapVertices = 6;
        _beam.numCornerVertices = 6;

        _hitColor  = new Color(1f, 1f, 1f, beamAlpha);
        _missColor = new Color(1f, 1f, 1f, beamAlpha * 0.25f);

        ApplyBeamAppearance();
        SetBeamEnabled(showBeam);
    }

    void ApplyBeamAppearance()
    {
        if (!_beam) return;
        _beam.startWidth = beamWidth;
        _beam.endWidth   = beamWidth;
        _beamMat.color   = _missColor;
    }

    void SetBeamEnabled(bool on)
    {
        if (_beam) _beam.enabled = on;
    }

    void DrawBeam(Vector3 a, Vector3 b, bool hittingAndPouring)
    {
        if (!_beam) return;
        if (!showBeam) { _beam.enabled = false; return; }
        _beam.enabled = true;
        _beam.SetPosition(0, a);
        _beam.SetPosition(1, b);
        _beamMat.color = hittingAndPouring ? _hitColor : (dimWhenNoHit ? _missColor : _hitColor);
    }
}
