using UnityEngine;
using UnityEngine.XR.Hands;
using System.Collections.Generic;

public class HandPourInput : MonoBehaviour
{
    [Header("Refs")]
    public LatteSimCompute sim;
    public Transform spout;                  // tip of the pitcher
    public Collider surfaceCollider;         // assign SurfaceDisk collider (optional)

    [Header("Pour gating")]
    public bool pourOnTilt = true;
    [Range(0f,90f)] public float tiltDegrees = 35f; // start pouring when pitched >= this
    public bool pourOnPinch = false;         // optionally require right-hand pinch
    public float pinchThreshold = 0.018f;

    [Header("Brush")]
    public bool useSimDefaults = true;
    [Range(0.01f, 0.25f)] public float radius = 0.08f;
    [Range(0f, 1f)] public float hardness = 0.85f;
    [Range(0f, 1f)] public float amount = 0.25f;
    [Range(0.05f, 2f)] public float splatsPerUV = 0.3f; // sub-steps per UV distance

    [Header("Input")]
    public float maxRayDistance = 0.3f;
    public LayerMask mask = ~0;

    XRHandSubsystem hands;
    Vector2? lastUV;
    float lastTime;

    void OnEnable() { AcquireHands(); }

    void Update()
    {
        if (!sim || !spout) return;

        if (pourOnPinch && (hands == null || !hands.running)) AcquireHands();

        bool shouldPour = ShouldPourByTilt();
        if (pourOnPinch) shouldPour &= IsRightPinching();

        if (!shouldPour) { lastUV = null; return; }

        if (!RaycastUV(out var uv)) { lastUV = null; return; }

        float now = Time.time;
        if (lastUV == null) { lastUV = uv; lastTime = now; return; }

        float r = useSimDefaults ? sim.defaultRadius   : radius;
        float h = useSimDefaults ? sim.defaultHardness : hardness;
        float a = useSimDefaults ? sim.defaultAmount   : amount;

        float dt = Mathf.Max(1e-4f, now - lastTime);
        Vector2 strokeVelUV = (uv - lastUV.Value) / dt;

        float uvDist = (uv - lastUV.Value).magnitude;
        int steps = Mathf.Clamp(Mathf.CeilToInt(uvDist / Mathf.Max(1e-4f, 1f / (splatsPerUV * 100f))), 1, 32);

        float amtPer = a / steps;
        for (int i = 1; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(lastUV.Value, uv, i / (float)steps);
            sim.InjectUV(p, r, h, amtPer, strokeVelUV);
        }

        lastUV = uv;
        lastTime = now;
    }

    bool ShouldPourByTilt()
    {
        if (!pourOnTilt) return true;
        // pour when the pitcher is tilted away from world up by tiltDegrees
        float dot = Vector3.Dot(spout.up.normalized, Vector3.up);
        float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
        return angle >= tiltDegrees;
    }

    bool RaycastUV(out Vector2 uv)
    {
        uv = default;
        Ray ray = new Ray(spout.position, spout.forward);
        if (!Physics.Raycast(ray, out var hit, maxRayDistance, mask, QueryTriggerInteraction.Ignore)) return false;
        if (surfaceCollider && hit.collider != surfaceCollider) return false;
        uv = hit.textureCoord;
        return true;
    }

    void AcquireHands()
    {
        var list = new List<XRHandSubsystem>();
        UnityEngine.SubsystemManager.GetSubsystems(list);
        if (list.Count > 0) hands = list[0];
    }

    bool IsRightPinching()
    {
        if (hands == null || !hands.running) return false;
        var h = hands.rightHand;
        if (!h.isTracked) return false;
        var th = h.GetJoint(XRHandJointID.ThumbTip);
        var ix = h.GetJoint(XRHandJointID.IndexTip);
        if (!th.TryGetPose(out var tp) || !ix.TryGetPose(out var ip)) return false;
        return Vector3.Distance(tp.position, ip.position) < pinchThreshold;
    }
}
