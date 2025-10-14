using UnityEngine;

public class MousePourInput : MonoBehaviour
{
    [Header("Refs")]
    public LatteSimCompute sim;       // Assign the compute sim
    public Camera cam;                // Camera used for raycasts
    public Collider surfaceCollider;  // Optional: restrict hits to this collider

    [Header("Brush")]
    [Tooltip("When ON, brush radius/hardness/amount come from LatteSimCompute inspector (Splat Defaults).")]
    public bool useSimDefaults = true;

    [Range(0.01f, 0.25f)] public float radius = 0.08f;
    [Range(0f, 1f)] public float hardness = 0.85f;
    [Range(0f, 1f)] public float amount = 0.25f;

    [Tooltip("Spacing as a fraction of radius (lower = denser splats).")]
    [Range(0.05f, 1f)] public float spacing = 0.25f;

    [Header("Input")]
    public int mouseButton = 0;       // 0 = LMB
    public KeyCode clearKey = KeyCode.C;

    // State
    Vector2? lastUV = null;
    float lastTime;

    void Reset()
    {
        if (!cam) cam = Camera.main;
    }

    void Update()
    {
        if (!sim || !cam) return;

        // Optional: quick clear
        if (Input.GetKeyDown(clearKey))
        {
            sim.Clear();
        }

        bool down = Input.GetMouseButtonDown(mouseButton);
        bool held = Input.GetMouseButton(mouseButton);
        bool up   = Input.GetMouseButtonUp(mouseButton);

        if (down)
        {
            if (RaycastUV(Input.mousePosition, out var uv))
            {
                // Choose brush source once per stroke
                var r = useSimDefaults ? sim.defaultRadius   : radius;
                var h = useSimDefaults ? sim.defaultHardness : hardness;
                var a = useSimDefaults ? sim.defaultAmount   : amount;

                // First tap: deposit with no push
                sim.InjectUV(uv, r, h, a, Vector2.zero);

                lastUV = uv;
                lastTime = Time.time;
            }
            return;
        }

        if (held && lastUV.HasValue)
        {
            if (!RaycastUV(Input.mousePosition, out var uv))
                return;

            // Brush parameters (consistent for the whole frame)
            var r = useSimDefaults ? sim.defaultRadius   : radius;
            var h = useSimDefaults ? sim.defaultHardness : hardness;
            var a = useSimDefaults ? sim.defaultAmount   : amount;

            // Frame-wise stroke info
            Vector2 prevUV = lastUV.Value;
            float dt = Mathf.Max(Time.time - lastTime, 1e-4f);
            Vector2 strokeVelUV = (uv - prevUV) / dt;  // UV/sec, used for all sub-splats

            // Place sub-splats along the segment
            float dist = Vector2.Distance(prevUV, uv);
            float step = Mathf.Max(0.001f, r * spacing);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / step));

            float amtPerStep = a / steps; // keep total deposit ≈ a

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector2 p = Vector2.Lerp(prevUV, uv, t);

                // Each sub-splat: small amount + same velocity push
                sim.InjectUV(p, r, h, amtPerStep, strokeVelUV);
            }

            lastUV = uv;
            lastTime = Time.time;
            return;
        }

        if (up)
        {
            lastUV = null;
        }
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
