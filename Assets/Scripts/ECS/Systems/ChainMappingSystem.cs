// Assets/Scripts/ECS/Systems/ChainMappingSystem.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ChainMappingSystem : MonoBehaviour
{
    [Header("References")]
    public Transform skeletonRoot;
    public Transform anchorsParent;

    [Header("Run")]
    public bool mapOnStart = true;

    [Header("Bone Follow")]
    public bool attachToNearestBoneAlways = true;
    public bool applyBoneRotation = false;

    [Header("Tree Layout")]
    [Tooltip("Base branch angle from longest path direction.")]
    public float branchAngleDeg = 25f;
    [Tooltip("Extra angle for additional branches.")]
    public float branchAngleStepDeg = 12f;
    [Tooltip("Branch angle used for side branches in leg groups.")]
    public float legBranchAngleDeg = 9f;
    [Tooltip("Extra angle step for additional leg side branches.")]
    public float legBranchAngleStepDeg = 3f;

    [Header("Logging")]
    public bool verbose = true;

    [Serializable]
    public class BodyPart
    {
        // Defines the anatomical span a major vascular group should occupy on the skeleton.
        public string regionName;     // body, neck_head, arm, leg
        public string sideName;       // left, right, midline
        public Transform partStart;
        public Transform partEnd;
        public float lateralOffset;
    }

    public List<BodyPart> bodyParts = new();

    enum MajorRegion
    {
        Body,
        NeckHead,
        Arm,
        Leg
    }

    class DirectedEdge
    {
        public string label;
        public int sn;
        public int tn;
        public float len;
    }

    class DirectedGraph
    {
        // Derived adjacency form used only during mapping/layout.
        public readonly List<DirectedEdge> edges = new();
        public readonly Dictionary<int, List<DirectedEdge>> outgoingBySn = new();
        public readonly Dictionary<int, int> inDegree = new();
    }

    void Start()
    {
        if (!mapOnStart) return;

        var world = EcsWorldBootstrap.World;
        if (world == null) { Debug.LogError("ChainMappingSystem: missing world."); return; }

        if (anchorsParent == null) anchorsParent = transform;

        Transform[] bones = skeletonRoot != null
            ? skeletonRoot.GetComponentsInChildren<Transform>(true)
            : Array.Empty<Transform>();

        if (bodyParts == null || bodyParts.Count == 0)
            AutoBuildBodyPartsFromKnownSkeleton();

        // Group vessels into a few high-level anatomical trees before laying them out.
        var groups = BuildMajorGroups(world);
        var partLookup = BuildPartLookup();

        // Global "placed once" lock to preserve continuity between major subtrees.
        var globallyPlacedNodeIds = new HashSet<int>();

        // Priority: body -> neck/head -> arms -> legs
        var orderedGroups = groups
            .OrderBy(g => GroupPriority(g.Key.region))
            .ThenBy(g => SidePriority(g.Key.side))
            .ToList();

        foreach (var kv in orderedGroups)
        {
            var key = kv.Key;
            var vessels = kv.Value;
            if (vessels.Count == 0) continue;

            if (!partLookup.TryGetValue(key, out var part))
            {
                // fallback to midline
                if (!partLookup.TryGetValue((key.region, "midline"), out part))
                {
                    if (verbose) Debug.LogWarning($"ChainMappingSystem: missing BodyPart for {key.region}:{key.side}");
                    continue;
                }
            }

            // Each group is laid out independently as a directed tree-like graph.
            var graph = BuildDirectedGraph(vessels);
            if (graph.edges.Count == 0) continue;

            int rootNode = SelectRootNodeForGroup(graph, key.region, key.side);
            if (rootNode == int.MinValue) continue;

            Vector3 partStart = part.partStart.position;
            Vector3 partEnd = part.partEnd.position;

            bool vertical = key.region == "neck_head";
            if (vertical && partEnd.y < partStart.y)
            {
                var t = partStart;
                partStart = partEnd;
                partEnd = t;
            }

            bool isBodyGroup = key.region == "body";
            bool allowOverwritePlacedNodes = !isBodyGroup;

            // Only body reuses previously anchored root directly.
            // Limb/neck groups should snap to their own body-part start and are allowed to overwrite.
            if (isBodyGroup && TryGetNodeAnchorPosition(world, rootNode, out var rootedPos))
                partStart = rootedPos;

            // Use longest-path length to scale graph space into the available body-part span.
            float mainLen = LongestPathFromNode(rootNode, graph.outgoingBySn, new Dictionary<int, float>(), new HashSet<int>());
            float span = Vector3.Distance(partStart, partEnd);
            float scale = span / Mathf.Max(1e-6f, mainLen);

            Vector3 lateral = (skeletonRoot != null) ? skeletonRoot.right : Vector3.right;
            float sideMul = key.side == "left" ? -1f : (key.side == "right" ? 1f : 0f);
            Vector3 rootPos = partStart + lateral * (part.lateralOffset + 0.015f * sideMul);

            Vector3 trunkDir = vertical ? Vector3.up : (partEnd - partStart).normalized;
            if (key.region == "arm" && Mathf.Abs(sideMul) > 0.5f)
                trunkDir = (lateral * Mathf.Sign(sideMul)).normalized;

            bool torsoMidlineSideways = (key.region == "body" && Mathf.Abs(sideMul) < 0.5f);
            float activeBranchAngle = (key.region == "leg") ? legBranchAngleDeg : branchAngleDeg;
            float activeBranchStep = (key.region == "leg") ? legBranchAngleStepDeg : branchAngleStepDeg;

            var longestMemo = new Dictionary<int, float>();
            var active = new HashSet<int>();
            var localPlaced = new HashSet<int>();

            LayoutFromNode(
                world,
                graph,
                rootNode,
                rootPos,
                trunkDir,
                scale,
                lateral,
                sideMul,
                torsoMidlineSideways,
                allowOverwritePlacedNodes,
                activeBranchAngle,
                activeBranchStep,
                bones,
                longestMemo,
                active,
                localPlaced,
                globallyPlacedNodeIds);

            if (verbose)
            {
                int bif = graph.outgoingBySn.Values.Count(v => v.Count > 1);
                Debug.Log($"ChainMappingSystem: mapped {key.region}:{key.side} edges={graph.edges.Count} root={rootNode} bif={bif}");
            }
        }
    }

    Dictionary<(string region, string side), List<VesselTopologyComponent>> BuildMajorGroups(EcsWorld world)
    {
        var groups = new Dictionary<(string region, string side), List<VesselTopologyComponent>>(new KeyComparer());

        foreach (var kv in world.VesselTopology)
        {
            var topo = kv.Value;
            var ont = VesselOntologyParser.Parse(topo.label);
            var major = ClassifyMajorRegion(topo.label, ont);

            string region = MajorToString(major);
            string side = (ont.side ?? "midline").ToLowerInvariant();

            if (major == MajorRegion.Body) side = "midline";
            if (major == MajorRegion.NeckHead && side != "left" && side != "right") side = "midline";
            if (major == MajorRegion.Arm && side != "left" && side != "right") side = "midline";
            if (major == MajorRegion.Leg && side != "left" && side != "right") side = "midline";

            var key = (region, side);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<VesselTopologyComponent>();
                groups[key] = list;
            }
            list.Add(topo);
        }

        return groups;
    }

    static MajorRegion ClassifyMajorRegion(string label, VesselOntology ont)
    {
        string n = (label ?? string.Empty).ToLowerInvariant();
        string r = (ont.region ?? string.Empty).ToLowerInvariant();

        if (n.Contains("carotid") || n.Contains("vertebral"))
            return MajorRegion.NeckHead;

        if (r == "shoulder" || r == "upper_arm" || r == "lower_arm")
            return MajorRegion.Arm;

        if (r == "upper_leg" || r == "lower_leg")
            return MajorRegion.Leg;

        return MajorRegion.Body;
    }

    static string MajorToString(MajorRegion r)
    {
        switch (r)
        {
            case MajorRegion.Body: return "body";
            case MajorRegion.NeckHead: return "neck_head";
            case MajorRegion.Arm: return "arm";
            case MajorRegion.Leg: return "leg";
            default: return "body";
        }
    }

    Dictionary<(string region, string side), BodyPart> BuildPartLookup()
    {
        var lookup = new Dictionary<(string region, string side), BodyPart>(new KeyComparer());
        foreach (var bp in bodyParts)
        {
            if (bp == null || bp.partStart == null || bp.partEnd == null) continue;
            lookup[(bp.regionName.ToLowerInvariant(), (bp.sideName ?? "midline").ToLowerInvariant())] = bp;
        }
        return lookup;
    }

    DirectedGraph BuildDirectedGraph(List<VesselTopologyComponent> vessels)
    {
        var g = new DirectedGraph();
        foreach (var v in vessels)
        {
            var e = new DirectedEdge
            {
                label = v.label,
                sn = v.snNodeId,
                tn = v.tnNodeId,
                len = Mathf.Max(1e-6f, v.lengthL)
            };

            g.edges.Add(e);

            if (!g.outgoingBySn.TryGetValue(e.sn, out var list))
            {
                list = new List<DirectedEdge>();
                g.outgoingBySn[e.sn] = list;
            }
            list.Add(e);

            if (!g.inDegree.ContainsKey(e.sn)) g.inDegree[e.sn] = 0;
            if (!g.inDegree.ContainsKey(e.tn)) g.inDegree[e.tn] = 0;
            g.inDegree[e.tn]++;
        }
        return g;
    }

    int SelectRootNode(DirectedGraph graph)
    {
        var memo = new Dictionary<int, float>();
        var zeroIn = graph.inDegree
            .Where(kv => kv.Value == 0 && graph.outgoingBySn.ContainsKey(kv.Key))
            .Select(kv => kv.Key)
            .ToList();

        if (zeroIn.Count > 0)
        {
            return zeroIn
                .OrderByDescending(n => LongestPathFromNode(n, graph.outgoingBySn, memo, new HashSet<int>()))
                .First();
        }

        var starts = graph.outgoingBySn.Keys.ToList();
        if (starts.Count == 0) return int.MinValue;
        return starts
            .OrderByDescending(n => LongestPathFromNode(n, graph.outgoingBySn, memo, new HashSet<int>()))
            .First();
    }

    int SelectRootNodeForGroup(DirectedGraph graph, string region, string side)
    {
        if (string.Equals(region, "leg", StringComparison.OrdinalIgnoreCase))
        {
            int preferred = SelectLegRootNode(graph, side);
            if (preferred != int.MinValue)
                return preferred;
        }

        return SelectRootNode(graph);
    }

    int SelectLegRootNode(DirectedGraph graph, string side)
    {
        string token = string.Equals(side, "left", StringComparison.OrdinalIgnoreCase) ? "_l" : "_r";

        // Prefer external iliac source node for side-specific hip root.
        for (int i = 0; i < graph.edges.Count; i++)
        {
            var e = graph.edges[i];
            string n = (e.label ?? string.Empty).ToLowerInvariant();
            if (n.Contains("external_iliac") && n.Contains(token))
                return e.sn;
        }

        // Fallback to femoral source if external iliac is not present.
        for (int i = 0; i < graph.edges.Count; i++)
        {
            var e = graph.edges[i];
            string n = (e.label ?? string.Empty).ToLowerInvariant();
            if (n.Contains("femoral") && n.Contains(token))
                return e.sn;
        }

        return int.MinValue;
    }

    float LongestPathFromNode(
        int nodeId,
        Dictionary<int, List<DirectedEdge>> outgoingBySn,
        Dictionary<int, float> memo,
        HashSet<int> stack)
    {
        if (memo.TryGetValue(nodeId, out float c)) return c;
        if (!stack.Add(nodeId)) return 0f;

        float best = 0f;
        if (outgoingBySn.TryGetValue(nodeId, out var edges))
        {
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                float d = e.len + LongestPathFromNode(e.tn, outgoingBySn, memo, stack);
                if (d > best) best = d;
            }
        }

        stack.Remove(nodeId);
        memo[nodeId] = best;
        return best;
    }

    void LayoutFromNode(
        EcsWorld world,
        DirectedGraph graph,
        int nodeId,
        Vector3 nodePos,
        Vector3 trunkDir,
        float scale,
        Vector3 lateralWorld,
        float sideMul,
        bool forceSidewaysTorsoBranches,
        bool allowOverwritePlacedNodes,
        float localBranchAngleDeg,
        float localBranchAngleStepDeg,
        Transform[] bones,
        Dictionary<int, float> longestMemo,
        HashSet<int> active,
        HashSet<int> localPlaced,
        HashSet<int> globalPlaced)
    {
        if (!active.Add(nodeId)) return;

        if (localPlaced.Add(nodeId))
        {
            if (allowOverwritePlacedNodes || !globalPlaced.Contains(nodeId))
            {
                // Anchors are created lazily so the same mapping pass can both place and attach nodes.
                PlaceNode(world, nodeId, nodePos, bones);
                globalPlaced.Add(nodeId);
            }
        }

        if (!graph.outgoingBySn.TryGetValue(nodeId, out var outgoing) || outgoing.Count == 0)
        {
            active.Remove(nodeId);
            return;
        }

        // Always expand the visually longest branch first so the main trunk stays straight.
        var ordered = outgoing
            .OrderByDescending(e => e.len + LongestPathFromNode(e.tn, graph.outgoingBySn, longestMemo, new HashSet<int>()))
            .ToList();

        bool isIliacSplit =
            forceSidewaysTorsoBranches &&
            ordered.Count >= 2 &&
            ordered.All(e => ((e.label ?? string.Empty).ToLowerInvariant().Contains("common_iliac")));

        for (int i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            float edgeLen = Mathf.Max(1e-6f, e.len) * scale;

            Vector3 dir;
            if (isIliacSplit)
            {
                // Keep L/R common iliac symmetric to the vertical trunk axis.
                Vector3 sideBasis = Vector3.ProjectOnPlane(lateralWorld, trunkDir).normalized;
                if (sideBasis.sqrMagnitude < 1e-8f) sideBasis = Vector3.right;
                float sign = InferSideSignFromLabel(e.label, i);
                const float iliacAngleDeg = 60f;
                float rad = iliacAngleDeg * Mathf.Deg2Rad;
                dir = (trunkDir.normalized * Mathf.Cos(rad) + sideBasis * (sign * Mathf.Sin(rad))).normalized;
            }
            else if (i == 0)
            {
                dir = trunkDir.normalized;
            }
            else if (forceSidewaysTorsoBranches && Mathf.Abs(sideMul) < 0.5f)
            {
                // For body midline branches, use vessel suffix side when available (_L/_R).
                Vector3 sideBasis = Vector3.ProjectOnPlane(lateralWorld, trunkDir).normalized;
                if (sideBasis.sqrMagnitude < 1e-8f) sideBasis = Vector3.right;
                float sign = InferSideSignFromLabel(e.label, i);

                string labelLower = (e.label ?? string.Empty).ToLowerInvariant();
                if (labelLower.Contains("common_iliac"))
                {
                    // Iliac split: symmetric ±60° from body axis (trunkDir).
                    const float iliacAngleDeg = 60f;
                    float rad = iliacAngleDeg * Mathf.Deg2Rad;
                    dir = (trunkDir.normalized * Mathf.Cos(rad) + sideBasis * (sign * Mathf.Sin(rad))).normalized;
                }
                else
                {
                    dir = sideBasis * sign;
                }
            }
            else
            {
                dir = ComputeBranchDirection(
                    trunkDir.normalized,
                    lateralWorld,
                    sideMul,
                    forceSidewaysTorsoBranches,
                    i,
                    localBranchAngleDeg,
                    localBranchAngleStepDeg);
            }

            Vector3 childPos = nodePos + dir * edgeLen;

            LayoutFromNode(
                world,
                graph,
                e.tn,
                childPos,
                dir,
                scale,
                lateralWorld,
                sideMul,
                forceSidewaysTorsoBranches,
                allowOverwritePlacedNodes,
                localBranchAngleDeg,
                localBranchAngleStepDeg,
                bones,
                longestMemo,
                active,
                localPlaced,
                globalPlaced);
        }

        active.Remove(nodeId);
    }

    static float InferSideSignFromLabel(string label, int fallbackIndex)
    {
        string n = (label ?? string.Empty).ToLowerInvariant();

        bool isLeft = n.EndsWith("_l") || n.EndsWith(".l") || n.EndsWith("_left") || n.EndsWith("-l")
                   || n.Contains("_l_") || n.Contains("_left");
        bool isRight = n.EndsWith("_r") || n.EndsWith(".r") || n.EndsWith("_right") || n.EndsWith("-r")
                    || n.Contains("_r_") || n.Contains("_right");

        if (isLeft && !isRight) return -1f;
        if (isRight && !isLeft) return +1f;

        return (fallbackIndex % 2 == 1) ? +1f : -1f;
    }

    Vector3 ComputeBranchDirection(
        Vector3 trunkDir,
        Vector3 lateralWorld,
        float sideMul,
        bool forceSidewaysTorsoBranches,
        int branchIndex,
        float localBranchAngleDeg,
        float localBranchAngleStepDeg)
    {
        float step = Mathf.Max(0f, localBranchAngleStepDeg);
        float baseAngle = Mathf.Max(0f, localBranchAngleDeg);
        float angleDeg = baseAngle + Mathf.Max(0, branchIndex - 1) * step;
        float angleRad = angleDeg * Mathf.Deg2Rad;

        if (Mathf.Abs(sideMul) > 0.5f)
        {
            Vector3 outward = lateralWorld * Mathf.Sign(sideMul);
            outward = Vector3.ProjectOnPlane(outward, trunkDir).normalized;
            if (outward.sqrMagnitude < 1e-8f) outward = Vector3.right;
            return (trunkDir * Mathf.Cos(angleRad) + outward * Mathf.Sin(angleRad)).normalized;
        }

        if (forceSidewaysTorsoBranches)
        {
            float sign = (branchIndex % 2 == 1) ? 1f : -1f;
            Vector3 sideBasis = Vector3.ProjectOnPlane(lateralWorld, trunkDir).normalized;
            if (sideBasis.sqrMagnitude < 1e-8f) sideBasis = Vector3.right;
            return sideBasis * sign;
        }

        int tier = (branchIndex + 1) / 2;
        float midSign = (branchIndex % 2 == 1) ? 1f : -1f;
        float midlineAngle = baseAngle + (tier - 1) * step;
        Vector3 midBasis = Vector3.ProjectOnPlane(lateralWorld, trunkDir).normalized;
        if (midBasis.sqrMagnitude < 1e-8f) midBasis = Vector3.right;
        float rad = midlineAngle * Mathf.Deg2Rad;
        return (trunkDir * Mathf.Cos(rad) + midBasis * (midSign * Mathf.Sin(rad))).normalized;
    }

    void PlaceNode(EcsWorld world, int nodeId, Vector3 worldPos, Transform[] bones)
    {
        if (!world.NodeIdToEntity.TryGetValue(nodeId, out int entity))
            return;

        Transform anchor = FindOrCreateAnchor(nodeId);
        anchor.position = worldPos;
        anchor.rotation = Quaternion.identity;

        var node = world.Nodes[entity];
        node.anchor = anchor;
        world.Nodes[entity] = node;

        if (attachToNearestBoneAlways && bones.Length > 0 && skeletonRoot != null)
        {
            // BoneFollow keeps the graph attached to the animated skeleton after initial layout.
            Transform nearest = NearestBone(anchor.position, bones) ?? skeletonRoot;
            world.BoneFollows[entity] = new BoneFollowComponent
            {
                bone = nearest,
                localPosOffset = nearest.InverseTransformPoint(anchor.position),
                localEulerOffset = Vector3.zero,
                applyRotation = applyBoneRotation
            };
        }
    }

    Transform FindOrCreateAnchor(int nodeId)
    {
        string name = $"Node_{nodeId}_anchor";
        var existing = anchorsParent.Find(name);
        if (existing != null) return existing;

        var go = new GameObject(name);
        go.transform.SetParent(anchorsParent, false);
        return go.transform;
    }

    static bool TryGetNodeAnchorPosition(EcsWorld world, int nodeId, out Vector3 pos)
    {
        pos = default;
        if (!world.NodeIdToEntity.TryGetValue(nodeId, out int entity)) return false;
        if (!world.Nodes.TryGetValue(entity, out var node)) return false;
        if (node.anchor == null) return false;
        pos = node.anchor.position;
        return true;
    }

    static Transform NearestBone(Vector3 pos, Transform[] bones)
    {
        Transform best = null;
        float bestD2 = float.MaxValue;
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i];
            if (b == null) continue;
            float d2 = (b.position - pos).sqrMagnitude;
            if (d2 < bestD2) { bestD2 = d2; best = b; }
        }
        return best;
    }

    void AutoBuildBodyPartsFromKnownSkeleton()
    {
        if (skeletonRoot == null) return;

        Transform FindBone(string name) =>
            skeletonRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        var hips = FindBone("Hips");
        var upperChest = FindBone("Upper.Chest");
        var head = FindBone("Head");

        var shoulderL = FindBone("Shoulder.L");
        var handL = FindBone("Hand.L");
        var shoulderR = FindBone("Shoulder.R");
        var handR = FindBone("Hand.R");

        var upperLegL = FindBone("Upper.Leg.L");
        var footL = FindBone("Foot.L");
        var upperLegR = FindBone("Upper.Leg.R");
        var footR = FindBone("Foot.R");

        // Build a default body-part map from the standard humanoid bone names used in the project.
        bodyParts = new List<BodyPart>();

        void Add(string region, string side, Transform a, Transform b, float lateral = 0f)
        {
            if (a == null || b == null || a == b) return;
            bodyParts.Add(new BodyPart
            {
                regionName = region,
                sideName = side,
                partStart = a,
                partEnd = b,
                lateralOffset = lateral
            });
        }

        Add("body", "midline", upperChest, hips);
        Add("neck_head", "midline", upperChest, head);
        Add("arm", "left", shoulderL, handL, -0.01f);
        Add("arm", "right", shoulderR, handR, +0.01f);
        Add("leg", "left", upperLegL, footL, -0.01f);
        Add("leg", "right", upperLegR, footR, +0.01f);
    }

    static int GroupPriority(string region)
    {
        switch (region)
        {
            case "body": return 0;
            case "neck_head": return 1;
            case "arm": return 2;
            case "leg": return 3;
            default: return 10;
        }
    }

    static int SidePriority(string side)
    {
        if (side == "midline") return 0;
        if (side == "left") return 1;
        if (side == "right") return 2;
        return 9;
    }

    class KeyComparer : IEqualityComparer<(string region, string side)>
    {
        public bool Equals((string region, string side) x, (string region, string side) y) =>
            string.Equals(x.region, y.region, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.side, y.side, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string region, string side) obj)
        {
            int h1 = obj.region?.ToLowerInvariant().GetHashCode() ?? 0;
            int h2 = obj.side?.ToLowerInvariant().GetHashCode() ?? 0;
            return h1 ^ (h2 << 1);
        }
    }
}
