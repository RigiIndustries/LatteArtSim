using UnityEngine;

public class MousePourInput : MonoBehaviour
{
    [Header("Refs")]
    public LatteSimCompute sim;       // assign the compute sim
    public Camera cam;
    public Collider surfaceCollider;

    [Header("Brush")]
    [Range(0.01f, 0.25f)] public float radius = 0.08f;
    [Range(0f, 1f)] public float hardness = 0.85f;
    [Range(0f, 1f)] public float amount = 0.25f;
    [Tooltip("Spacing as a fraction of radius (lower = denser splats).")]
    [Range(0.05f, 1f)] public float spacing = 0.25f;

    [Header("Forces")]
    [Tooltip("Scales stroke velocity (UV/sec) before injecting as force.")]
    public float flowMultiplier = 6f;

    [Header("Hotkeys")]
    public KeyCode clearKey = KeyCode.C;

    Vector2? lastUV;
    float lastTime;

    void Awake()
    {
        if (!cam) cam = Camera.main;
    }

    void OnDisable() { lastUV = null; }

    void Update()
    {
        if (!sim || !cam) return;

        if (Input.GetKeyDown(clearKey)) sim.Clear();

        if (Input.GetMouseButtonDown(0))
        {
            lastUV = null;
            lastTime = Time.time;
        }

        if (!Input.GetMouseButton(0)) return;
        if (!RaycastUV(Input.mousePosition, out var uv)) return;

        if (lastUV == null)
        {
            lastUV = uv;
            lastTime = Time.time;
            // first tap: deposit a bit, no push
            sim.InjectUV(uv, radius, hardness, amount, Vector2.zero);
            return;
        }

        // Frame-wise stroke info
        Vector2 prevUV = lastUV.Value;
        float dt = Mathf.Max(Time.time - lastTime, 1e-4f);
        Vector2 strokeVelUV = (uv - prevUV) / dt;  // UV/sec, used for ALL sub-splats this frame

        // Place sub-splats along the segment
        float dist = Vector2.Distance(prevUV, uv);
        float step = Mathf.Max(0.001f, radius * spacing);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / step));

        float amtPerStep = amount / steps;                 // don’t over-deposit color
        Vector2 forcePerStep = strokeVelUV * flowMultiplier; // constant force per frame

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 p = Vector2.Lerp(prevUV, uv, t);
            sim.InjectUV(p, radius, hardness, amtPerStep, forcePerStep);
        }

        lastUV = uv;
        lastTime = Time.time;
    }

    bool RaycastUV(Vector3 screen, out Vector2 uv)
    {
        uv = default;
        Ray ray = cam.ScreenPointToRay(screen);
        if (!Physics.Raycast(ray, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (surfaceCollider && hit.collider != surfaceCollider) return false;
        uv = hit.textureCoord;
        return true;
    }
}