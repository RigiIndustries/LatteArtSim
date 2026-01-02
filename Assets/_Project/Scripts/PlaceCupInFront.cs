using UnityEngine;

/// <summary>
/// Places the AR cup (and calibration zones / spout target) in front of the user's camera at startup.
/// - Positions and orients two calibration zones (cup + pitcher) relative to the camera.
/// - Places spoutTarget relative to the pitcher zone (top face + configurable local offset).
/// </summary>
public class PlaceCupInFront : MonoBehaviour
{
    // ===================== Inspector fields =====================

    [Header("What to place")]
    [Tooltip("AR cup root (SurfaceDisk should be a child).")]
    public Transform cupRoot;

    [Header("Offsets (meters)")]
    [Tooltip("Distance in front of the camera.")]
    public float forward = 0.45f;

    [Tooltip("Vertical offset relative to camera (negative = below camera).")]
    public float verticalOffset = -0.10f;

    [Header("Orientation")]
    [Tooltip("If true, faces the camera direction but keeps the cup upright.")]
    public bool keepUpright = true;

    [Header("Calibration Zones (optional)")]
    [Tooltip("Transform of the cup calibration zone box.")]
    public Transform cupZoneCenter;

    [Tooltip("Transform of the pitcher calibration zone box.")]
    public Transform pitcherZoneCenter;

    [Tooltip("Horizontal separation between the two zones (meters).")]
    public float zoneSeparation = 0.25f;

    [Tooltip("Additional forward/back offset applied to both zones (meters).")]
    public float zoneDepthOffset = 0.0f;

    [Tooltip("Additional vertical offset applied to both zones (meters).")]
    public float zoneVerticalOffset = 0.0f;

    [Tooltip("If true, rotates zones to face camera direction while staying upright.")]
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
    [Tooltip("Optional object representing the spout tip used for pouring.")]
    public Transform spoutTarget;

    [Tooltip("Local offset inside the pitcher zone, relative to its center (meters).")]
    public Vector3 spoutLocalOffset = Vector3.zero;

    // ===================== Unity hooks =====================

    void Start()
    {
        var cam = Camera.main;
        if (!cam) return;

        // ===================== Place zones =====================

        if (cupZoneCenter || pitcherZoneCenter)
        {
            Vector3 basePos =
                cam.transform.position +
                cam.transform.forward * (forward + zoneDepthOffset) +
                cam.transform.up * (verticalOffset + zoneVerticalOffset);

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

        // ===================== CupRoot placement =====================

        if (cupRoot)
        {
            if (cupZoneCenter && calibration != null)
            {
                // Place cupRoot at the top face of the cup zone plus a configurable local offset.
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
                // Fallback: place cupRoot directly in front of the camera.
                Vector3 pos =
                    cam.transform.position +
                    cam.transform.forward * forward +
                    cam.transform.up * verticalOffset;

                Quaternion rot;
                if (keepUpright)
                {
                    Vector3 f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                    if (f.sqrMagnitude < 1e-6f) f = cam.transform.forward;
                    rot = Quaternion.LookRotation(f, Vector3.up);
                }
                else
                {
                    rot = cam.transform.rotation;
                }

                cupRoot.SetPositionAndRotation(pos, rot);
            }
        }

        // ===================== SpoutTarget placement =====================

        if (spoutTarget && pitcherZoneCenter && calibration != null)
        {
            // Place spoutTarget near the top of the pitcher zone plus a configurable local offset.
            float topY = calibration.pitcherZoneSize.y * 0.5f;

            Vector3 localPos = new Vector3(0f, topY, 0f) + spoutLocalOffset;
            Vector3 worldPos = pitcherZoneCenter.TransformPoint(localPos);

            Quaternion worldRot = pitcherZoneCenter.rotation;
            spoutTarget.SetPositionAndRotation(worldPos, worldRot);
        }
    }
}
