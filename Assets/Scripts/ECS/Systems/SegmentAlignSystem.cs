// Assets/Scripts/ECS/Systems/SegmentAlignSystem.cs
using System.Collections.Generic;
using UnityEngine;

public class SegmentAlignSystem : MonoBehaviour
{
    // Reuse list to avoid GC allocs
    static readonly List<int> _keys = new List<int>(1024);

    void LateUpdate()
    {
        var world = EcsWorldBootstrap.World;
        if (world == null) return;

        // Snapshot keys so iteration stays stable even if the stores are updated elsewhere.
        _keys.Clear();
        _keys.AddRange(world.SegmentRender.Keys);

        for (int i = 0; i < _keys.Count; i++)
        {
            int vesselE = _keys[i];

            if (!world.SegmentRender.TryGetValue(vesselE, out var seg))
                continue;

            if (!world.VesselTopology.TryGetValue(vesselE, out var topo)) continue;
            if (!world.NodeIdToEntity.TryGetValue(topo.snNodeId, out int snE)) continue;
            if (!world.NodeIdToEntity.TryGetValue(topo.tnNodeId, out int tnE)) continue;

            if (!world.Nodes.TryGetValue(snE, out var sn)) continue;
            if (!world.Nodes.TryGetValue(tnE, out var tn)) continue;
            if (sn.anchor == null || tn.anchor == null) continue;
            if (seg.vesselRootTransform == null) continue;

            Vector3 start = sn.anchor.position;
            Vector3 end = tn.anchor.position;
            Vector3 dir = end - start;

            float lengthWorld = Mathf.Max(dir.magnitude, seg.minLength);
            Vector3 dirNorm = (dir.sqrMagnitude > 1e-9f) ? dir.normalized : Vector3.up;

            // Treat the vessel root as a center pivot between its two mapped node anchors.
            seg.vesselRootTransform.position = start + dirNorm * (lengthWorld * 0.5f);

            // Rotate so chosen axis aligns with direction
            int axis = Mathf.Clamp(seg.lengthAxis, 0, 2);
            Vector3 axisVector =
                (axis == 0) ? Vector3.right :
                (axis == 2) ? Vector3.forward :
                              Vector3.up;

            seg.vesselRootTransform.rotation = Quaternion.FromToRotation(axisVector, dirNorm);

            // Scale mesh child along length axis ONLY (preserve thickness axes)
            int partCount = Mathf.Max(1, seg.subSegmentCount);
            float partLengthWorld = lengthWorld / partCount;

            if (seg.meshChildren != null && seg.initialAxisWorldLength != null)
            {
                Vector3 axisLocal =
                    (axis == 0) ? Vector3.right :
                    (axis == 2) ? Vector3.forward :
                                  Vector3.up;

                for (int s = 0; s < seg.meshChildren.Length; s++)
                {
                    var child = seg.meshChildren[s];
                    if (child == null) continue;
                    if (s >= seg.initialAxisWorldLength.Length || s >= seg.meshChildInitialLocalScale.Length || s >= seg.meshChildInitialLocalPosition.Length)
                        continue;

                    float initAxisWorld = seg.initialAxisWorldLength[s];
                    if (initAxisWorld <= 1e-6f) continue;

                    float scaleFactor = partLengthWorld / initAxisWorld;

                    Vector3 cur = child.localScale;
                    if (cur.sqrMagnitude < 1e-12f)
                        cur = seg.meshChildInitialLocalScale[s];

                    // only adjust length axis
                    cur[axis] = seg.meshChildInitialLocalScale[s][axis] * scaleFactor;
                    child.localScale = cur;

                    // distribute sub-segment roots evenly from start to end along local length axis
                    float centerOffset = (-lengthWorld * 0.5f) + ((s + 0.5f) * partLengthWorld);
                    if (seg.subSegmentRoots != null && s < seg.subSegmentRoots.Length && seg.subSegmentRoots[s] != null)
                    {
                        seg.subSegmentRoots[s].localPosition = axisLocal * centerOffset;
                    }
                }
            }

            // IMPORTANT: do NOT write back to world.SegmentRender here (would modify dictionary)
        }
    }
}
