using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using System.Collections.Generic;

public class CupHandAttach : MonoBehaviour
{
    public enum Handed { Left, Right }
    public Handed hand = Handed.Left;
    public Transform cupRoot;
    [Range(0,1)] public float posSmooth = 0.2f, rotSmooth = 0.2f;
    [Range(0f,0.05f)] public float pinchThreshold = 0.018f;

    XRHandSubsystem hands; 
    bool holding; 
    Pose palmToCup, last; 
    bool hasLast;

    void OnEnable() { TryGetHands(); }

    void Update()
    {
        if (cupRoot == null) return;

        if (!HasHands())
        {
            TryGetHands();
            return;
        }

        var h = (hand == Handed.Left) ? hands.leftHand : hands.rightHand;
        if (!h.isTracked) { hasLast = false; return; }
        if (!TryPalm(h, out var palm)) { hasLast = false; return; }

        bool pinching = IsPinching(h);

        if (pinching && !holding)
        {
            palmToCup = Multiply(Inverse(palm), new Pose(cupRoot.position, cupRoot.rotation));
            holding = true; hasLast = false;
        }
        if (!pinching && holding) holding = false;

        if (holding)
        {
            var raw = Multiply(palm, palmToCup);
            if (!hasLast) { last = raw; hasLast = true; }
            var p = Vector3.Lerp(last.position, raw.position, 1f - Mathf.Clamp01(posSmooth));
            var r = Quaternion.Slerp(last.rotation, raw.rotation, 1f - Mathf.Clamp01(rotSmooth));
            last = new Pose(p, r);
            cupRoot.SetPositionAndRotation(p, r);
        }
    }

    bool HasHands() => hands != null && hands.running;

    void TryGetHands()
    {
        // via XR Management
        var loader = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager.activeLoader : null;
        if (loader != null) hands = loader.GetLoadedSubsystem<XRHandSubsystem>();

        // fallback: query subsystems directly
        if (hands == null)
        {
            var list = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(list);
            if (list.Count > 0) hands = list[0];
        }
    }

    static bool TryPalm(XRHand h, out Pose palm)
    {
        palm = default;
        var wrist = h.GetJoint(XRHandJointID.Wrist);
        var iMet  = h.GetJoint(XRHandJointID.IndexMetacarpal);
        var lMet  = h.GetJoint(XRHandJointID.LittleMetacarpal);
        if (!wrist.TryGetPose(out var wp) || !iMet.TryGetPose(out var ip) || !lMet.TryGetPose(out var lp)) return false;

        Vector3 x = (ip.position - lp.position).normalized;
        Vector3 n = Vector3.Cross(ip.position - wp.position, lp.position - wp.position).normalized;
        Vector3 z = Vector3.Cross(x, n).normalized;
        palm = new Pose(((ip.position + lp.position) * 0.5f), Quaternion.LookRotation(z, n));
        return true;
    }

    bool IsPinching(XRHand h)
    {
        var th = h.GetJoint(XRHandJointID.ThumbTip);
        var ix = h.GetJoint(XRHandJointID.IndexTip);
        if (!th.TryGetPose(out var tp) || !ix.TryGetPose(out var ip)) return false;
        return Vector3.Distance(tp.position, ip.position) < pinchThreshold;
    }

    static Pose Multiply(in Pose a, in Pose b) => new Pose(a.position + a.rotation * b.position, a.rotation * b.rotation);
    static Pose Inverse(in Pose p){ var ri = Quaternion.Inverse(p.rotation); return new Pose(ri * (-p.position), ri); }
}
