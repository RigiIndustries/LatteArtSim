using UnityEngine;
using System.Collections.Generic;

public class DebugPourTest : MonoBehaviour
{
    [Header("References")]
    public OVRHand pitcherHand;        // drag from your rig
    public LatteSimCompute sim;        // latte sim
    public Collider surfaceCollider;   // collider on SurfaceDisk

    [Header("Pour control")]
    public float rayLength = 0.3f;
    public bool alwaysPour = true;     // for debugging, ignore tilt
    public bool showRay = true;

    [Header("Brush settings")]
    public bool useSimDefaults = true;
    [Range(0.01f, 0.25f)] public float radius = 0.08f;
    [Range(0f, 1f)] public float hardness = 0.85f;
    [Range(0f, 1f)] public float amount = 0.25f;

    OVRSkeleton _skel;
    Vector2? _lastUV;
    float _lastTime;

    void Start()
    {
        if (pitcherHand) _skel = pitcherHand.GetComponent<OVRSkeleton>();
    }

    void Update()
    {
        if (!pitcherHand || !sim || !surfaceCollider) return;
        if (!_skel) _skel = pitcherHand.GetComponent<OVRSkeleton>();

        // Try get index tip
        Pose tipPose;
        if (!TryGetIndexTip(out tipPose))
        {
            _lastUV = null;
            return;
        }

        Vector3 origin = tipPose.position;
        Vector3 dir = Vector3.down; // for now, just shoot straight down in world space

        // visualize the ray
        if (showRay)
        {
            Debug.DrawRay(origin, dir * rayLength, Color.cyan);
        }

        // Raycast toward the surface
        if (Physics.Raycast(origin, dir, out var hit, rayLength))
        {
            if (hit.collider == surfaceCollider)
            {
                // brush parameters
                float r = useSimDefaults ? sim.defaultRadius   : radius;
                float h = useSimDefaults ? sim.defaultHardness : hardness;
                float a = useSimDefaults ? sim.defaultAmount   : amount;

                Vector2 uv = hit.textureCoord;
                if (!_lastUV.HasValue)
                {
                    sim.InjectUV(uv, r, h, a, Vector2.zero);
                    _lastUV = uv;
                    _lastTime = Time.time;
                    return;
                }

                // basic stroke follow
                Vector2 prev = _lastUV.Value;
                float now = Time.time, dt = Mathf.Max(1e-5f, now - _lastTime);
                Vector2 dUV = uv - prev;
                float dist = dUV.magnitude;
                Vector2 flow = (dUV / dt);
                int steps = Mathf.Max(1, Mathf.CeilToInt(dist * 160f));
                float amtStep = a / steps;
                for (int i = 1; i <= steps; i++)
                {
                    Vector2 p = Vector2.Lerp(prev, uv, i / (float)steps);
                    sim.InjectUV(p, r, h, amtStep, flow);
                }

                _lastUV = uv;
                _lastTime = now;
            }
        }
    }

    bool TryGetIndexTip(out Pose pose)
    {
        pose = default;

        if (_skel && _skel.IsDataValid && _skel.IsDataHighConfidence && _skel.Bones != null)
        {
            foreach (var b in _skel.Bones)
            {
                if (b.Id == OVRSkeleton.BoneId.Hand_IndexTip && b.Transform)
                {
                    pose = new Pose(b.Transform.position, b.Transform.rotation);
                    return true;
                }
            }
        }

        // fallback
        if (pitcherHand.PointerPose != null)
        {
            pose = new Pose(pitcherHand.PointerPose.position, pitcherHand.PointerPose.rotation);
            return true;
        }

        return false;
    }
}
