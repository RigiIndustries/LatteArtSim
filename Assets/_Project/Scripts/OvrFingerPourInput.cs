using System.Linq;                 // <-- important
using UnityEngine;

public class OvrFingerPourInput : MonoBehaviour
{
    [Header("Refs")]
    public LatteSimCompute sim;          // drag LatteSimCompute here
    public OVRSkeleton rightSkeleton;    // RightHandAnchor/OVRSkeleton
    public OVRHand rightHand;            // RightHandAnchor/OVRHand
    public Collider surfaceCollider;     // SurfaceDisk MeshCollider

    [Header("Pour control")]
    public bool requirePinch = false;
    [Range(0f,1f)] public float pinchStrengthMin = 0.5f;   // used if requirePinch=true

    [Header("Brush (or use sim defaults)")]
    public bool useSimDefaults = true;
    [Range(0.01f, 0.25f)] public float radius = 0.08f;
    [Range(0f,   1f   )] public float hardness = 0.85f;
    [Range(0f,   1f   )] public float amount = 0.25f;
    [Tooltip("Higher = denser stroke along UV motion")]
    [Range(0.05f, 2f)]  public float splatsPerUV = 0.3f;

    [Header("Raycast")]
    public float maxRayDistance = 0.25f;
    public LayerMask mask = ~0;

    Vector2? lastUV; float lastTime;

    void Update()
    {
        if (!sim || !rightSkeleton || !rightHand || surfaceCollider == null) return;
        if (!rightHand.IsTracked) { lastUV = null; return; }

        if (!TryPalm(rightSkeleton, out var palm) ||
            !TryBone(rightSkeleton, OVRSkeleton.BoneId.Hand_IndexTip, out var tip))
        { lastUV = null; return; }

        if (requirePinch &&
            !(rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index) ||
              rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) >= pinchStrengthMin))
        { lastUV = null; return; }

        // Ray from fingertip toward palm normal (into the cup)
        Vector3 origin = tip.position + palm.up * 0.005f;
        Vector3 dir    = -palm.up;

        if (!Physics.Raycast(origin, dir, out var hit, maxRayDistance, mask, QueryTriggerInteraction.Ignore))
        { lastUV = null; return; }

        if (hit.collider != surfaceCollider) { lastUV = null; return; }

        var uv = hit.textureCoord;
        var now = Time.time;

        if (lastUV == null) { lastUV = uv; lastTime = now; return; }

        float r = useSimDefaults ? sim.defaultRadius   : radius;
        float h = useSimDefaults ? sim.defaultHardness : hardness;
        float a = useSimDefaults ? sim.defaultAmount   : amount;

        float dt = Mathf.Max(1e-4f, now - lastTime);
        Vector2 velUV = (uv - lastUV.Value) / dt;
        float uvDist = (uv - lastUV.Value).magnitude;

        int steps = Mathf.Clamp(Mathf.CeilToInt(uvDist / Mathf.Max(1e-4f, 1f / (splatsPerUV * 100f))), 1, 32);
        float amtPer = a / steps;

        for (int i = 1; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(lastUV.Value, uv, i / (float)steps);
            sim.InjectUV(p, r, h, amtPer, velUV);
        }

        lastUV = uv; lastTime = now;
    }

    static bool TryBone(OVRSkeleton skel, OVRSkeleton.BoneId id, out Pose pose)
    {
        pose = default;
        var b = skel.Bones?.FirstOrDefault(x => x.Id == id);
        if (b == null) return false;
        pose = new Pose(b.Transform.position, b.Transform.rotation);
        return true;
    }

    static bool TryPalm(OVRSkeleton skel, out Pose palm)
    {
        palm = default;
        var bones = skel.Bones;
        if (bones == null || bones.Count == 0) return false;

        var wrist = bones.FirstOrDefault(b => b.Id == OVRSkeleton.BoneId.Hand_WristRoot);
        var iMet  = bones.FirstOrDefault(b => b.Id == OVRSkeleton.BoneId.Hand_Index1);
        var lMet  = bones.FirstOrDefault(b => b.Id == OVRSkeleton.BoneId.Hand_Pinky1);
        if (wrist == null || iMet == null || lMet == null) return false;

        Vector3 wp = wrist.Transform.position, ip = iMet.Transform.position, lp = lMet.Transform.position;
        Vector3 x = (ip - lp).normalized;
        Vector3 n = Vector3.Cross(ip - wp, lp - wp).normalized;
        Vector3 z = Vector3.Cross(x, n).normalized;
        palm = new Pose(((ip + lp) * 0.5f), Quaternion.LookRotation(z, n));
        return true;
    }
}
