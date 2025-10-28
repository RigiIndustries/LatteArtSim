using UnityEngine;

public class PlaceCupInFront : MonoBehaviour
{
    [Header("What to place")]
    public Transform cupRoot;              // AR cup root (SurfaceDisk is a child)

    [Header("Offsets (meters)")]
    public float forward = 0.45f;          // distance in front of camera
    public float verticalOffset = -0.10f;  // negative = below camera

    [Header("Orientation")]
    public bool keepUpright = true;        // face camera direction but stay upright

    [Header("Calibration Zones (optional)")]
    public Transform cupZoneCenter;        // cup zone cube
    public Transform pitcherZoneCenter;    // pitcher zone cube
    public float zoneSeparation = 0.25f;
    public float zoneDepthOffset = 0.0f;
    public float zoneVerticalOffset = 0.0f;
    public bool orientZonesToCamera = true;

    [Header("Sync with calibration sizes (optional)")]
    [Tooltip("Drag your OvrCupHandAttach here so we can read zone sizes.")]
    public OvrCupHandAttach calibration;   // to read cube sizes

    [Header("Surface alignment tweaks")]
    [Tooltip("Fine-tune placement relative to cup zone top (in zone local space).")]
    public Vector3 surfaceOffset = Vector3.zero;
    [Tooltip("If true, align cupRoot rotation to the cup zone; else keep upright to camera.")]
    public bool alignToZone = true;

    [Header("Optional Spout Target")]
    [Tooltip("Optional object representing the spout tip; will be placed on the top-left edge of the pitcher cube.")]
    public Transform spoutTarget;
    [Tooltip("Offset of the spout point along cube edges (0=center, 0.5=edge).")]
    [Range(0f, 0.5f)] public float spoutEdgeOffset = 0.45f; // how far toward the side

    void Start()
    {
        var cam = Camera.main;
        if (!cam) return;

        // --- Place zones ---
        if (cupZoneCenter || pitcherZoneCenter)
        {
            Vector3 basePos = cam.transform.position
                            + cam.transform.forward * (forward + zoneDepthOffset)
                            + cam.transform.up * (verticalOffset + zoneVerticalOffset);

            Quaternion facingRot = orientZonesToCamera
                ? Quaternion.LookRotation(
                    Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized,
                    Vector3.up)
                : Quaternion.identity;

            Vector3 right = facingRot * Vector3.right;

            if (cupZoneCenter)
                cupZoneCenter.SetPositionAndRotation(basePos - right * (zoneSeparation * 0.5f), facingRot);

            if (pitcherZoneCenter)
                pitcherZoneCenter.SetPositionAndRotation(basePos + right * (zoneSeparation * 0.5f), facingRot);
        }

        // --- CupRoot placement ---
        if (cupRoot)
        {
            if (cupZoneCenter && calibration != null)
            {
                float topOffsetY = calibration.cupZoneSize.y * 0.5f;
                Vector3 pos = cupZoneCenter.position + cupZoneCenter.up * topOffsetY;
                pos += cupZoneCenter.TransformVector(surfaceOffset);

                Quaternion rot = alignToZone
                    ? cupZoneCenter.rotation
                    : Quaternion.LookRotation(
                        Vector3.ProjectOnPlane(cupZoneCenter.forward, Vector3.up).normalized,
                        Vector3.up);

                cupRoot.SetPositionAndRotation(pos, rot);
            }
            else
            {
                Vector3 pos = cam.transform.position
                             + cam.transform.forward * forward
                             + cam.transform.up * verticalOffset;

                Quaternion rot;
                if (keepUpright)
                {
                    Vector3 f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                    if (f.sqrMagnitude < 1e-6f) f = cam.transform.forward;
                    rot = Quaternion.LookRotation(f, Vector3.up);
                }
                else rot = cam.transform.rotation;

                cupRoot.SetPositionAndRotation(pos, rot);
            }
        }

        // --- SpoutTarget placement ---
        if (spoutTarget && pitcherZoneCenter && calibration != null)
        {
            // place it at the top-left edge of the pitcher cube
            float topY = calibration.pitcherZoneSize.y * 0.5f;
            float sideX = calibration.pitcherZoneSize.x * 0.5f * spoutEdgeOffset * 2f; // map 0–0.5 to 0–edge
            // "left edge" = negative X in cube local space
            Vector3 localPos = new Vector3(-sideX, topY, 0f);
            Vector3 worldPos = pitcherZoneCenter.TransformPoint(localPos);

            Quaternion worldRot = pitcherZoneCenter.rotation;
            spoutTarget.SetPositionAndRotation(worldPos, worldRot);
        }
    }
}
