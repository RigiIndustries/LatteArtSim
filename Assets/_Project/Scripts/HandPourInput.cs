using UnityEngine;

/// <summary>
/// VR/AR pour input that projects a "milk stream" onto the simulated latte surface.
/// - Raycasts straight down from the spout onto the cup surface collider.
/// - Converts hit UVs into splats for LatteSimCompute (dye + optional velocity impulse).
/// - Optional gating: tilt threshold, external calibration, and a milk volume budget.
/// - Optional LineRenderer stream visual whose width scales with tilt-derived flow.
/// </summary>
[DefaultExecutionOrder(20)]
public class HandPourInput : MonoBehaviour
{
    // ===================== Inspector fields =====================

    [Header("Required")]
    [Tooltip("SpoutTarget: the small sphere that marks the pitcher spout.")]
    public Transform spout;

    [Tooltip("LatteSimCompute that drives the AR surface.")]
    public LatteSimCompute sim;

    [Tooltip("MeshCollider on the SurfaceDisk under CupRoot.")]
    public Collider surfaceCollider;

    [Header("Brush (like MousePourInput)")]
    [Tooltip("If true, uses defaultRadius/defaultHardness/defaultAmount from the simulation.")]
    public bool useSimDefaults = true;

    [Range(0.01f, 0.25f)] public float radius = 0.08f;
    [Range(0f, 1f)] public float hardness = 0.85f;
    [Range(0f, 1f)] public float amount = 0.25f;

    [Tooltip("Spacing as a fraction of brush radius; lower = denser splats.")]
    [Range(0.05f, 1f)] public float spacing = 0.25f;

    // --- Height-based radius ---
    [Header("Height → radius")]
    [Tooltip("If true, pour radius depends on distance from spout to surface.")]
    public bool scaleRadiusWithHeight = true;

    [Tooltip("Brush radius when spout is close to the surface (UV units).")]
    [Range(0.01f, 0.3f)] public float radiusNear = 0.10f;

    [Tooltip("Brush radius when spout is at max rayLength distance.")]
    [Range(0.005f, 0.3f)] public float radiusFar = 0.04f;

    /// <summary>
    /// Which spout axis is used to measure tilt relative to a captured neutral pose.
    /// </summary>
    public enum AxisMode
    {
        SpoutDown,
        SpoutForward,
        SpoutRight
    }

    [Header("Pour direction & tilt")]
    [Tooltip("Axis that defines the POUR ROTATION – we compare its current direction to the neutral pose.")]
    public AxisMode tiltAxis = AxisMode.SpoutRight;

    [Tooltip("How far (deg) you must rotate away from the neutral pose before it 'should' pour.")]
    [Range(0f, 180f)] public float pourAngleDeg = 45f;

    [Tooltip("Ignore tilt gating and always pour when hitting the surface (good for testing).")]
    public bool forceAlwaysPour = true;

    [Tooltip("Max ray length (meters).")]
    public float rayLength = 0.35f;

    [Tooltip("Layers that can be hit by the pour raycast.")]
    public LayerMask raycastMask = ~0;

    // --- Tilt-based flow amount ---
    [Header("Tilt → flow")]
    [Tooltip("If true, amount (brightness) scales with pitcher tilt.")]
    public bool scaleAmountWithTilt = true;

    [Tooltip("Angle where flow starts ramping up (deg). Usually = pourAngleDeg.")]
    [Range(0f, 180f)] public float flowStartAngleDeg = 45f;

    [Tooltip("Angle where flow reaches max (deg).")]
    [Range(0f, 180f)] public float flowMaxAngleDeg = 80f;

    [Tooltip("Scale on amount at flowStartAngleDeg.")]
    [Range(0f, 1f)] public float minFlowAmountScale = 0.2f;

    [Tooltip("Scale on amount at flowMaxAngleDeg.")]
    [Range(0f, 2f)] public float maxFlowAmountScale = 1.0f;

    [Header("Optional calibration gate")]
    [Tooltip("If true, pouring only works after this calibration script reports Calibrated = true.")]
    public bool requireCalibration = false;
    public OvrCupHandAttach calibration;

    [Header("Stream visual (LineRenderer)")]
    [Tooltip("If true, renders a stream from spout to hit point.")]
    public bool showBeam = true;

    [Tooltip("Base stream width (meters). Actual width is scaled by tilt.")]
    [Range(0.0005f, 0.02f)] public float beamWidth = 0.004f;

    [Range(0f, 1f)] public float beamAlpha = 0.9f;

    [Header("Milk volume budget")]
    [Tooltip("Total milk you can pour per cup (arbitrary units).")]
    public float milkCapacity = 1.0f;

    [Tooltip("Milk poured per second at flowScale = 1.0.")]
    public float baseMilkPerSecond = 0.25f;

    [Tooltip("Reset milk budget when this component is enabled.")]
    public bool autoRefillOnEnable = true;

    // internal tracking
    float _milkUsed;

    /// <summary>0–1 fraction for UI.</summary>
    public float MilkUsed01 => (milkCapacity <= 0f) ? 0f : Mathf.Clamp01(_milkUsed / milkCapacity);

    /// <summary>True if milkCapacity is set and the budget has been exhausted.</summary>
    public bool OutOfMilk => (milkCapacity > 0f) && (_milkUsed >= milkCapacity);

    /// <summary>Public reset API (e.g., when starting a new cup).</summary>
    public void ResetMilk()
    {
        _milkUsed = 0f;
    }

    [Header("Debug (Scene view)")]
    [Tooltip("Draws the downward ray in Scene view.")]
    public bool drawSceneRay = false;

    // ===================== Internal state =====================

    Vector2? _lastUV;
    float _lastTime;

    LineRenderer _beam;
    Material _beamMat;
    Color _hitColor;

    // Tilt reference
    bool _hasNeutralTilt;
    Vector3 _neutralTiltDir;  // world-space axis direction at "neutral" pose

    // Debug state
    float _currentTiltAngleDeg;
    bool _tiltSatisfied;
    bool _isPouring;
    float _currentFlowScale = 1f;  // used for stream width

    // Public read-only access for debug overlay
    public float CurrentTiltAngleDeg => _currentTiltAngleDeg;
    public bool TiltSatisfied => _tiltSatisfied;
    public bool IsPouring => _isPouring;

    // ===================== Unity hooks =====================

    void Awake()
    {
        SetupBeam();
    }

    void OnEnable()
    {
        SetBeamEnabled(showBeam);
        _lastUV = null;
        _hasNeutralTilt = false;
        _isPouring = false;

        if (autoRefillOnEnable)
            _milkUsed = 0f;
    }

    void OnDisable()
    {
        SetBeamEnabled(false);
        _lastUV = null;
        _hasNeutralTilt = false;
        _isPouring = false;
    }

    void Update()
    {
        // Hard requirements: without these, disable stream and do nothing.
        if (!sim || !surfaceCollider || !spout)
        {
            SetBeamEnabled(false);
            _isPouring = false;
            return;
        }

        // Optional gating: only pour after external calibration has completed.
        if (requireCalibration && calibration && !calibration.Calibrated)
        {
            _lastUV = null;
            DrawBeam(spout.position, spout.position, false);
            _hasNeutralTilt = false;
            _isPouring = false;
            return;
        }

        // Optional global override (project-specific).
        bool infinitePour = GameSettings.Instance != null &&
                            GameSettings.Instance.infinitePourEnabled;

        // 1) Get the current tilt axis in world space.
        Vector3 tiltDir = GetTiltAxisWorld();
        if (tiltDir.sqrMagnitude < 1e-6f)
            tiltDir = Vector3.forward;
        tiltDir.Normalize();

        // 2) Capture neutral tilt direction once (after calibration / first valid frame).
        if (!_hasNeutralTilt)
        {
            _neutralTiltDir = tiltDir;
            _hasNeutralTilt = true;
        }

        // Angle between current tilt axis and neutral axis.
        float tiltAngle = Vector3.Angle(_neutralTiltDir, tiltDir);
        _currentTiltAngleDeg = tiltAngle;

        bool tiltSatisfied = tiltAngle >= pourAngleDeg;
        _tiltSatisfied = tiltSatisfied;

        // Simulation gating: tilt (unless forced) + milk budget (unless infinite).
        bool allowPour = infinitePour
            ? (forceAlwaysPour || tiltSatisfied)
            : (forceAlwaysPour || tiltSatisfied) && !OutOfMilk;

        // --- Compute flow scale from tilt angle ---
        float flowScale = 1f;
        if (scaleAmountWithTilt)
        {
            float startA = Mathf.Min(flowStartAngleDeg, flowMaxAngleDeg);
            float maxA = Mathf.Max(flowStartAngleDeg, flowMaxAngleDeg);

            float t;
            if (tiltAngle <= startA) t = 0f;
            else if (tiltAngle >= maxA) t = 1f;
            else t = Mathf.InverseLerp(startA, maxA, tiltAngle);

            flowScale = Mathf.Lerp(minFlowAmountScale, maxFlowAmountScale, t);
        }
        _currentFlowScale = flowScale;

        // 3) Raycast: ALWAYS straight down in world space.
        Vector3 origin = spout.position;
        Vector3 rayDir = Vector3.down;

        if (drawSceneRay)
            Debug.DrawRay(origin, rayDir * rayLength, Color.cyan);

        bool hitSurface = Physics.Raycast(
            origin,
            rayDir,
            out RaycastHit hit,
            rayLength,
            raycastMask,
            QueryTriggerInteraction.Ignore
        ) && hit.collider == surfaceCollider;

        Vector3 end = hitSurface ? hit.point : (origin + rayDir * rayLength);

        // Stream is purely visual: show when actually hitting and tilt is satisfied.
        bool visualShouldShowStream = hitSurface && tiltSatisfied &&
                                     (!OutOfMilk || infinitePour);
        DrawBeam(origin, end, visualShouldShowStream);

        // If we can't pour now, reset stroke state and stop.
        if (!hitSurface || !allowPour)
        {
            _lastUV = null;
            _lastTime = Time.time;
            _isPouring = false;
            return;
        }

        // --- Milk budget: accumulate by tilt & time ---
        if (!infinitePour && milkCapacity > 0f)   // guard
        {
            _milkUsed += baseMilkPerSecond * _currentFlowScale * Time.deltaTime;

            if (_milkUsed >= milkCapacity)
            {
                _milkUsed = milkCapacity;
                _lastUV = null;
                _isPouring = false;
                return;
            }
        }

        // 4) UV & brush settings
        Vector2 uv = hit.textureCoord;

        float baseR = useSimDefaults ? sim.defaultRadius : radius;
        float baseH = useSimDefaults ? sim.defaultHardness : hardness;
        float baseA = useSimDefaults ? sim.defaultAmount : amount;

        // Height-based radius
        float r = baseR;
        if (scaleRadiusWithHeight)
        {
            float d = Mathf.Clamp(hit.distance, 0f, rayLength);
            float tH = Mathf.InverseLerp(rayLength, 0f, d); // high → low
            r = Mathf.Lerp(radiusFar, radiusNear, tH);
        }

        float h = baseH;
        float a = baseA * (scaleAmountWithTilt ? flowScale : 1f);

        // 5) First contact: single splat to seed the stroke.
        if (!_lastUV.HasValue)
        {
            sim.InjectUV(uv, r, h, a, Vector2.zero);
            _lastUV = uv;
            _lastTime = Time.time;
            _isPouring = true;
            return;
        }

        // 6) Stroke placement (MousePourInput-style): interpolate along segment and distribute amount.
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

        _lastUV = uv;
        _lastTime = Time.time;
        _isPouring = true;
    }

    // ===================== Axis helpers =====================

    /// <summary>
    /// Returns the selected spout axis direction in world space.
    /// </summary>
    Vector3 GetTiltAxisWorld()
    {
        if (!spout) return Vector3.forward;

        switch (tiltAxis)
        {
            case AxisMode.SpoutDown: return -spout.up;
            case AxisMode.SpoutForward: return spout.forward;
            case AxisMode.SpoutRight: return spout.right;
            default: return spout.forward;
        }
    }

    // ===================== Beam helpers =====================

    /// <summary>
    /// Creates a child LineRenderer used as the stream visual.
    /// </summary>
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

        _hitColor = new Color(1f, 1f, 1f, beamAlpha);

        ApplyBeamAppearance();
        SetBeamEnabled(showBeam);
    }

    /// <summary>
    /// Applies base width and color to the beam (before tilt-based scaling).
    /// </summary>
    void ApplyBeamAppearance()
    {
        if (!_beam) return;

        _beam.startWidth = beamWidth;
        _beam.endWidth = beamWidth;

        if (_beamMat != null)
            _beamMat.color = _hitColor;
    }

    /// <summary>
    /// Toggles the LineRenderer component.
    /// </summary>
    void SetBeamEnabled(bool on)
    {
        if (_beam) _beam.enabled = on;
    }

    /// <summary>
    /// Draws the beam from spout to ray end and scales width based on current flow.
    /// </summary>
    void DrawBeam(Vector3 a, Vector3 b, bool shouldShowStream)
    {
        if (!_beam) return;

        if (!showBeam || !shouldShowStream)
        {
            _beam.enabled = false;
            return;
        }

        _beam.enabled = true;
        _beam.SetPosition(0, a);
        _beam.SetPosition(1, b);

        if (_beamMat == null) return;

        _beamMat.color = _hitColor;

        // Width scales with flow: min → thin, max → thick.
        float normalizedFlow = 1f;
        if (scaleAmountWithTilt && maxFlowAmountScale > minFlowAmountScale)
        {
            normalizedFlow = Mathf.InverseLerp(
                minFlowAmountScale,
                maxFlowAmountScale,
                _currentFlowScale
            );
        }

        // 0.5x width at min flow, 2.0x at max flow.
        float widthFactor = Mathf.Lerp(0.5f, 2f, normalizedFlow);
        float width = beamWidth * widthFactor;
        _beam.startWidth = width;
        _beam.endWidth = width;
    }
}
