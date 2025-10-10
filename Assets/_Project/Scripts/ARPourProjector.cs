using UnityEngine;

public class ARPourProjector : MonoBehaviour
{
    public LatteSimController sim;
    public Transform pitcherTip;
    public Transform surface;
    public float cupRadiusMeters = 0.04f;
    [Range(0,1)] public float amount = 0.6f;
    [Range(0.001f, 0.25f)] public float baseRadius = 0.04f;
    [Range(0,1)] public float hardness = 0.6f;
    public bool flipV;

    void Update()
    {
        if (!sim || !pitcherTip || !surface) return;

        Vector3 n = surface.up;
        Vector3 p0 = pitcherTip.position;
        Vector3 dir = -n;

        float denom = Vector3.Dot(dir, n);
        if (Mathf.Abs(denom) < 1e-4f) return;
        float t = Vector3.Dot(surface.position - p0, n) / denom;
        if (t < 0) return;

        Vector3 hit = p0 + dir * t;
        Vector3 local = surface.InverseTransformPoint(hit);
        Vector2 uv = new Vector2(local.x + 0.5f, local.z + 0.5f);
        if (flipV) uv.y = 1f - uv.y;

        float rLocal = new Vector2(local.x, local.z).magnitude;
        float half = cupRadiusMeters * 0.5f / Mathf.Max(surface.lossyScale.x, 1e-4f);
        if (rLocal > half) return;

        float height = Mathf.Max(0.01f, Vector3.Dot(pitcherTip.position - surface.position, n));
        float radius = Mathf.Clamp(baseRadius * (0.5f + height * 2.0f), 0.003f, 0.15f);

        sim.InjectUV(uv, amount, radius, hardness);
    }
}
