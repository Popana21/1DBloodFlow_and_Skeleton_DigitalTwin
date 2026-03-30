using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs reproducible evaluation metrics for topology, anatomy/layout, geometry readability, and runtime.
/// Exported JSON can be directly used to fill thesis evaluation tables.
/// </summary>
public class EvaluationRunner : MonoBehaviour
{
    [Header("Execution")]
    public bool runOnStart = true;
    public float runDelaySeconds = 0.5f;
    public bool includePerformanceMetrics = true;
    public int performanceWarmupFrames = 20;
    public float performanceSampleSeconds = 10f;

    [Header("References")]
    public ChainMappingSystem chainMappingSystem;
    public Camera projectionCamera;
    public TextAsset topologyJsonOverride;
    public string topologyJsonFileName = "vessels.json";

    [Header("Thresholds")]
    public float lengthMismatchTolerance = 1e-4f;
    public float overlapDistanceThreshold = 0.01f;

    [Header("Output")]
    public bool logSummary = true;
    public string outputFileName = "evaluation_report.json";

    const float kEpsilon = 1e-6f;

    void Start()
    {
        if (!runOnStart) return;
        StartCoroutine(RunEvaluationRoutine());
    }

    IEnumerator RunEvaluationRoutine()
    {
        if (runDelaySeconds > 0f)
            yield return new WaitForSeconds(runDelaySeconds);

        var world = EcsWorldBootstrap.World;
        if (world == null)
        {
            Debug.LogError("EvaluationRunner: EcsWorld not available.");
            yield break;
        }

        if (chainMappingSystem == null)
            chainMappingSystem = FindAnyObjectByType<ChainMappingSystem>();

        var report = new EvaluationReport
        {
            generatedAtUtc = DateTime.UtcNow.ToString("o"),
            unityVersion = Application.unityVersion,
            sceneName = SceneManager.GetActiveScene().name,
            topology = MeasureTopology(world),
            ontology = MeasureOntology(world),
            layout = MeasureLayout(world),
            geometry = MeasureGeometry(world)
        };

        if (includePerformanceMetrics)
            yield return StartCoroutine(MeasurePerformance(report));
        else
            report.performance = new PerformanceMetrics();

        string outPath = WriteReport(report);
        if (logSummary)
            LogSummary(report, outPath);
    }

    TopologyMetrics MeasureTopology(EcsWorld world)
    {
        var metrics = new TopologyMetrics();

        VesselArray json = LoadTopologyJson();
        var jsonVessels = (json != null && json.vessels != null) ? json.vessels : Array.Empty<Vessel>();
        metrics.jsonVesselCount = jsonVessels.Length;
        metrics.worldVesselCount = world.VesselTopology.Count;

        var jsonNodes = new HashSet<int>();
        var jsonLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateLabels = new List<string>();
        foreach (var v in jsonVessels)
        {
            if (v == null) continue;
            jsonNodes.Add(v.sn);
            jsonNodes.Add(v.tn);

            if (string.IsNullOrWhiteSpace(v.label)) continue;
            if (!jsonLabels.Add(v.label))
                duplicateLabels.Add(v.label);
        }

        metrics.jsonNodeCount = jsonNodes.Count;
        metrics.worldNodeCount = world.NodeIdToEntity.Count;
        metrics.nodeCoveragePct = Percent(metrics.worldNodeCount, metrics.jsonNodeCount);
        metrics.vesselCoveragePct = Percent(metrics.worldVesselCount, metrics.jsonVesselCount);

        var missingVessels = new List<string>();
        int endpointMismatchCount = 0;
        int lengthMismatchCount = 0;

        foreach (var v in jsonVessels)
        {
            if (v == null || string.IsNullOrWhiteSpace(v.label)) continue;
            if (!world.VesselLabelToEntity.TryGetValue(v.label, out int entity))
            {
                missingVessels.Add(v.label);
                continue;
            }

            if (!world.VesselTopology.TryGetValue(entity, out var topo))
                continue;

            if (topo.snNodeId != v.sn || topo.tnNodeId != v.tn)
                endpointMismatchCount++;

            if (Mathf.Abs(topo.lengthL - v.L) > lengthMismatchTolerance)
                lengthMismatchCount++;
        }

        var extraVessels = new List<string>();
        foreach (var kv in world.VesselLabelToEntity)
        {
            if (!jsonLabels.Contains(kv.Key))
                extraVessels.Add(kv.Key);
        }

        var missingNodes = new List<int>();
        foreach (int nodeId in jsonNodes)
        {
            if (!world.NodeIdToEntity.ContainsKey(nodeId))
                missingNodes.Add(nodeId);
        }

        metrics.endpointMismatchCount = endpointMismatchCount;
        metrics.lengthMismatchCount = lengthMismatchCount;
        metrics.missingVesselCount = missingVessels.Count;
        metrics.extraVesselCount = extraVessels.Count;
        metrics.missingNodeCount = missingNodes.Count;
        metrics.duplicateLabelCount = duplicateLabels.Count;
        metrics.duplicateLabels = duplicateLabels.ToArray();
        metrics.missingVessels = missingVessels.ToArray();
        metrics.extraVessels = extraVessels.ToArray();
        metrics.missingNodes = missingNodes.ToArray();
        return metrics;
    }

    OntologyMetrics MeasureOntology(EcsWorld world)
    {
        var metrics = new OntologyMetrics();
        var regionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sideCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int sideSuffixKnown = 0;
        int sideSuffixMatch = 0;

        foreach (var kv in world.VesselTopology)
        {
            var topo = kv.Value;
            var ont = VesselOntologyParser.Parse(topo.label);
            string region = string.IsNullOrWhiteSpace(ont.region) ? "unknown" : ont.region.ToLowerInvariant();
            string side = string.IsNullOrWhiteSpace(ont.side) ? "unknown" : ont.side.ToLowerInvariant();

            Inc(regionCounts, region);
            Inc(sideCounts, side);

            string inferred = InferSideFromLabel(topo.label);
            if (inferred != "unknown")
            {
                sideSuffixKnown++;
                if (string.Equals(inferred, side, StringComparison.OrdinalIgnoreCase))
                    sideSuffixMatch++;
            }
        }

        metrics.vesselCount = world.VesselTopology.Count;
        metrics.sideSuffixKnownCount = sideSuffixKnown;
        metrics.sideSuffixMatchCount = sideSuffixMatch;
        metrics.sideSuffixMatchPct = Percent(sideSuffixMatch, Mathf.Max(1, sideSuffixKnown));
        metrics.regionCounts = ToEntries(regionCounts);
        metrics.sideCounts = ToEntries(sideCounts);
        return metrics;
    }

    LayoutMetrics MeasureLayout(EcsWorld world)
    {
        var metrics = new LayoutMetrics();

        // Collect resolved segments with anchors.
        var segments = new List<ResolvedSegment>(world.VesselTopology.Count);
        foreach (var kv in world.VesselTopology)
        {
            int vesselEntity = kv.Key;
            var topo = kv.Value;

            if (!world.NodeIdToEntity.TryGetValue(topo.snNodeId, out int snE)) continue;
            if (!world.NodeIdToEntity.TryGetValue(topo.tnNodeId, out int tnE)) continue;
            if (!world.Nodes.TryGetValue(snE, out var sn) || sn.anchor == null) continue;
            if (!world.Nodes.TryGetValue(tnE, out var tn) || tn.anchor == null) continue;

            Vector3 dir = tn.anchor.position - sn.anchor.position;
            if (dir.sqrMagnitude < kEpsilon) continue;

            var ont = VesselOntologyParser.Parse(topo.label);
            segments.Add(new ResolvedSegment
            {
                label = topo.label,
                sn = topo.snNodeId,
                tn = topo.tnNodeId,
                lengthL = Mathf.Max(kEpsilon, topo.lengthL),
                start = sn.anchor.position,
                end = tn.anchor.position,
                direction = dir.normalized,
                region = (ont.region ?? "unknown").ToLowerInvariant(),
                side = (ont.side ?? "midline").ToLowerInvariant(),
                entity = vesselEntity
            });
        }

        metrics.segmentWithAnchorsCount = segments.Count;
        if (segments.Count == 0) return metrics;

        float branchBase = (chainMappingSystem != null) ? Mathf.Max(0f, chainMappingSystem.branchAngleDeg) : 25f;
        float branchStep = (chainMappingSystem != null) ? Mathf.Max(0f, chainMappingSystem.branchAngleStepDeg) : 12f;
        Vector3 lateral = (chainMappingSystem != null && chainMappingSystem.skeletonRoot != null)
            ? chainMappingSystem.skeletonRoot.right
            : Vector3.right;

        int bifCount = 0;
        int verticalEdgeCount = 0;
        int verticalUpPassCount = 0;
        int sideRuleChecks = 0;
        int sideRulePass = 0;
        int torsoPerpChecks = 0;
        float torsoPerpAbsErrSum = 0f;
        int expectedAngleChecks = 0;
        float expectedAngleAbsErrSum = 0f;
        float primaryAlignAbsErrSum = 0f;

        foreach (var g in segments.GroupBy(s => GroupKey(s.region, s.side)))
        {
            var groupSegments = g.ToList();
            var outgoingByNode = groupSegments
                .GroupBy(s => s.sn)
                .ToDictionary(x => x.Key, x => x.ToList());
            var incomingByNode = groupSegments
                .GroupBy(s => s.tn)
                .ToDictionary(x => x.Key, x => x.ToList());
            var memo = new Dictionary<int, float>();

            Vector3 groupAxis = GuessGroupAxis(g.Key.region, g.Key.side);

            // Vertical-up compliance for head/neck edges.
            if (g.Key.region == "head" || g.Key.region == "neck")
            {
                foreach (var s in groupSegments)
                {
                    verticalEdgeCount++;
                    if (Vector3.Dot(s.direction, Vector3.up) > 0f) verticalUpPassCount++;
                }
            }

            foreach (var kvp in outgoingByNode)
            {
                var outs = kvp.Value;
                if (outs.Count <= 1) continue;

                bifCount++;
                int nodeId = kvp.Key;

                Vector3 trunk = groupAxis;
                if (incomingByNode.TryGetValue(nodeId, out var incoming) && incoming.Count > 0)
                    trunk = incoming[0].direction;
                if (trunk.sqrMagnitude < kEpsilon)
                    trunk = groupAxis.sqrMagnitude > kEpsilon ? groupAxis : Vector3.down;
                trunk.Normalize();

                var ordered = outs
                    .OrderByDescending(e => e.lengthL + LongestPathFromNode(e.tn, outgoingByNode, memo, new HashSet<int>()))
                    .ToList();

                var primary = ordered[0];
                float pAngle = Vector3.Angle(trunk, primary.direction);
                primaryAlignAbsErrSum += Mathf.Abs(pAngle);

                for (int i = 1; i < ordered.Count; i++)
                {
                    var sec = ordered[i];
                    float angle = Vector3.Angle(trunk, sec.direction);

                    bool isSide = (g.Key.side == "left" || g.Key.side == "right");
                    bool isTorsoMidline = IsTorsoRegion(g.Key.region) && g.Key.side == "midline";

                    if (isSide)
                    {
                        Vector3 outward = lateral * ((g.Key.side == "left") ? -1f : 1f);
                        outward = Vector3.ProjectOnPlane(outward, trunk).normalized;
                        Vector3 secProjected = Vector3.ProjectOnPlane(sec.direction, trunk).normalized;
                        sideRuleChecks++;
                        if (outward.sqrMagnitude > kEpsilon && secProjected.sqrMagnitude > kEpsilon &&
                            Vector3.Dot(secProjected, outward) > 0f)
                        {
                            sideRulePass++;
                        }
                    }

                    if (isTorsoMidline)
                    {
                        torsoPerpChecks++;
                        torsoPerpAbsErrSum += Mathf.Abs(angle - 90f);
                    }
                    else
                    {
                        int sideIndex = i;
                        int magnitudeTier = (sideIndex + 1) / 2;
                        float expected = branchBase + Mathf.Max(0, magnitudeTier - 1) * branchStep;
                        expectedAngleChecks++;
                        expectedAngleAbsErrSum += Mathf.Abs(angle - expected);
                    }
                }
            }
        }

        metrics.bifurcationCount = bifCount;
        metrics.primaryAlignmentMeanAbsErrorDeg = (bifCount > 0) ? (primaryAlignAbsErrSum / bifCount) : 0f;
        metrics.verticalUpEdgeCount = verticalEdgeCount;
        metrics.verticalUpCompliancePct = Percent(verticalUpPassCount, Mathf.Max(1, verticalEdgeCount));
        metrics.sideRuleCheckCount = sideRuleChecks;
        metrics.sideRuleCompliancePct = Percent(sideRulePass, Mathf.Max(1, sideRuleChecks));
        metrics.torsoPerpendicularCheckCount = torsoPerpChecks;
        metrics.torsoPerpendicularMeanAbsErrorDeg = (torsoPerpChecks > 0) ? (torsoPerpAbsErrSum / torsoPerpChecks) : 0f;
        metrics.branchAngleCheckCount = expectedAngleChecks;
        metrics.branchAngleMeanAbsErrorDeg = (expectedAngleChecks > 0) ? (expectedAngleAbsErrSum / expectedAngleChecks) : 0f;
        return metrics;
    }

    GeometryMetrics MeasureGeometry(EcsWorld world)
    {
        var metrics = new GeometryMetrics();

        var segments = BuildSegmentLines(world);
        metrics.segmentCount = segments.Count;
        if (segments.Count < 2) return metrics;

        int crossings = 0;
        int overlaps = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var a = segments[i];
            for (int j = i + 1; j < segments.Count; j++)
            {
                var b = segments[j];
                if (ShareEndpoint(a, b)) continue;

                if (SegmentDistance(a.start, a.end, b.start, b.end) < overlapDistanceThreshold)
                    overlaps++;

                if (Intersects2D(Project2D(a.start), Project2D(a.end), Project2D(b.start), Project2D(b.end)))
                    crossings++;
            }
        }

        metrics.overlapCount = overlaps;
        metrics.crossingCount2D = crossings;
        return metrics;
    }

    IEnumerator MeasurePerformance(EvaluationReport report)
    {
        var metrics = new PerformanceMetrics();

        int warmup = Mathf.Max(0, performanceWarmupFrames);
        for (int i = 0; i < warmup; i++)
            yield return null;

        long monoStart = Profiler.GetMonoUsedSizeLong();
        var frameTimes = new List<float>(1024);

        float elapsed = 0f;
        while (elapsed < Mathf.Max(0.1f, performanceSampleSeconds))
        {
            yield return null;
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            frameTimes.Add(dt);
        }

        long monoEnd = Profiler.GetMonoUsedSizeLong();

        frameTimes.Sort();
        float mean = frameTimes.Count > 0 ? frameTimes.Average() : 0f;
        float p95 = frameTimes.Count > 0 ? Percentile(frameTimes, 0.95f) : 0f;
        float p99 = frameTimes.Count > 0 ? Percentile(frameTimes, 0.99f) : 0f;

        metrics.sampleSeconds = elapsed;
        metrics.frameCount = frameTimes.Count;
        metrics.meanFrameMs = mean * 1000f;
        metrics.p95FrameMs = p95 * 1000f;
        metrics.p99FrameMs = p99 * 1000f;
        metrics.meanFps = (mean > kEpsilon) ? (1f / mean) : 0f;
        metrics.monoMemoryDeltaMb = (monoEnd - monoStart) / (1024f * 1024f);
        report.performance = metrics;
    }

    string WriteReport(EvaluationReport report)
    {
        string json = JsonUtility.ToJson(report, true);
        string dir = Path.Combine(Application.persistentDataPath, "Evaluation");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, outputFileName);
        File.WriteAllText(path, json);
        return path;
    }

    void LogSummary(EvaluationReport report, string outPath)
    {
        Debug.Log(
            "EvaluationRunner:\n" +
            $"- Report: {outPath}\n" +
            $"- Topology coverage: vessels={report.topology.vesselCoveragePct:F1}% nodes={report.topology.nodeCoveragePct:F1}%\n" +
            $"- Layout: bifurcations={report.layout.bifurcationCount} primary_err={report.layout.primaryAlignmentMeanAbsErrorDeg:F2} deg\n" +
            $"- Geometry: crossings2D={report.geometry.crossingCount2D} overlaps={report.geometry.overlapCount}\n" +
            $"- Performance: mean_fps={report.performance.meanFps:F1} p95_ms={report.performance.p95FrameMs:F2}");
    }

    VesselArray LoadTopologyJson()
    {
        try
        {
            string json;
            if (topologyJsonOverride != null)
            {
                json = topologyJsonOverride.text;
            }
            else
            {
                string path = Path.Combine(Application.streamingAssetsPath, topologyJsonFileName);
                if (!File.Exists(path))
                    return new VesselArray { vessels = Array.Empty<Vessel>() };
                json = File.ReadAllText(path);
            }

            var parsed = JsonUtility.FromJson<VesselArray>(json);
            if (parsed == null || parsed.vessels == null)
                return new VesselArray { vessels = Array.Empty<Vessel>() };
            return parsed;
        }
        catch
        {
            return new VesselArray { vessels = Array.Empty<Vessel>() };
        }
    }

    string InferSideFromLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return "unknown";
        string lower = label.ToLowerInvariant();
        if (lower.EndsWith("_r") || lower.EndsWith(".r") || lower.EndsWith("_right") || lower.EndsWith("-r")) return "right";
        if (lower.EndsWith("_l") || lower.EndsWith(".l") || lower.EndsWith("_left") || lower.EndsWith("-l")) return "left";
        if (lower.Contains("_r_") || lower.Contains("_right")) return "right";
        if (lower.Contains("_l_") || lower.Contains("_left")) return "left";
        return "unknown";
    }

    Vector3 GuessGroupAxis(string region, string side)
    {
        if (chainMappingSystem != null && chainMappingSystem.bodyParts != null)
        {
            ChainMappingSystem.BodyPart found = chainMappingSystem.bodyParts.FirstOrDefault(
                bp => bp != null &&
                      bp.partStart != null &&
                      bp.partEnd != null &&
                      string.Equals(bp.regionName, region, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(bp.sideName ?? "midline", side, StringComparison.OrdinalIgnoreCase));

            if (found == null)
            {
                found = chainMappingSystem.bodyParts.FirstOrDefault(
                    bp => bp != null &&
                          bp.partStart != null &&
                          bp.partEnd != null &&
                          string.Equals(bp.regionName, region, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(bp.sideName ?? "midline", "midline", StringComparison.OrdinalIgnoreCase));
            }

            if (found != null)
            {
                Vector3 d = found.partEnd.position - found.partStart.position;
                if (IsVerticalRegion(region))
                    return Vector3.up;
                if (d.sqrMagnitude > kEpsilon) return d.normalized;
            }
        }

        if (IsVerticalRegion(region)) return Vector3.up;
        return Vector3.down;
    }

    static bool IsVerticalRegion(string region) =>
        string.Equals(region, "head", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(region, "neck", StringComparison.OrdinalIgnoreCase);

    static bool IsTorsoRegion(string region) =>
        string.Equals(region, "upper_chest", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(region, "chest", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(region, "spine", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(region, "hips", StringComparison.OrdinalIgnoreCase);

    float LongestPathFromNode(
        int nodeId,
        Dictionary<int, List<ResolvedSegment>> outgoingBySn,
        Dictionary<int, float> memo,
        HashSet<int> stack)
    {
        if (memo.TryGetValue(nodeId, out float cached)) return cached;
        if (!stack.Add(nodeId)) return 0f;

        float best = 0f;
        if (outgoingBySn.TryGetValue(nodeId, out var outs))
        {
            for (int i = 0; i < outs.Count; i++)
            {
                float d = outs[i].lengthL + LongestPathFromNode(outs[i].tn, outgoingBySn, memo, stack);
                if (d > best) best = d;
            }
        }

        stack.Remove(nodeId);
        memo[nodeId] = best;
        return best;
    }

    List<SegmentLine> BuildSegmentLines(EcsWorld world)
    {
        var lines = new List<SegmentLine>(world.VesselTopology.Count);
        foreach (var kv in world.VesselTopology)
        {
            var topo = kv.Value;
            if (!world.NodeIdToEntity.TryGetValue(topo.snNodeId, out int snE)) continue;
            if (!world.NodeIdToEntity.TryGetValue(topo.tnNodeId, out int tnE)) continue;
            if (!world.Nodes.TryGetValue(snE, out var sn) || sn.anchor == null) continue;
            if (!world.Nodes.TryGetValue(tnE, out var tn) || tn.anchor == null) continue;
            if ((tn.anchor.position - sn.anchor.position).sqrMagnitude < kEpsilon) continue;

            lines.Add(new SegmentLine
            {
                sn = topo.snNodeId,
                tn = topo.tnNodeId,
                start = sn.anchor.position,
                end = tn.anchor.position
            });
        }
        return lines;
    }

    Vector2 Project2D(Vector3 worldPos)
    {
        if (projectionCamera != null)
        {
            Vector3 p = projectionCamera.WorldToViewportPoint(worldPos);
            return new Vector2(p.x, p.y);
        }
        return new Vector2(worldPos.x, worldPos.y);
    }

    static bool ShareEndpoint(SegmentLine a, SegmentLine b) =>
        a.sn == b.sn || a.sn == b.tn || a.tn == b.sn || a.tn == b.tn;

    static bool Intersects2D(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        float d1 = Direction(a1, a2, b1);
        float d2 = Direction(a1, a2, b2);
        float d3 = Direction(b1, b2, a1);
        float d4 = Direction(b1, b2, a2);

        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
               ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    static float Direction(Vector2 a, Vector2 b, Vector2 c) =>
        (c.x - a.x) * (b.y - a.y) - (c.y - a.y) * (b.x - a.x);

    // Segment-segment shortest distance (3D) for overlap detection.
    static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1;
        Vector3 d2 = q2 - p2;
        Vector3 r = p1 - p2;
        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);

        float s, t;
        if (a <= kEpsilon && e <= kEpsilon) return (p1 - p2).magnitude;
        if (a <= kEpsilon)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= kEpsilon)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;
                s = denom != 0f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }

        Vector3 c1 = p1 + d1 * s;
        Vector3 c2 = p2 + d2 * t;
        return (c1 - c2).magnitude;
    }

    static float Percent(int num, int den) =>
        den <= 0 ? 0f : (100f * num / den);

    static void Inc(Dictionary<string, int> dict, string key)
    {
        if (!dict.ContainsKey(key)) dict[key] = 0;
        dict[key]++;
    }

    static StringIntEntry[] ToEntries(Dictionary<string, int> dict) =>
        dict.OrderBy(kv => kv.Key)
            .Select(kv => new StringIntEntry { key = kv.Key, value = kv.Value })
            .ToArray();

    static float Percentile(List<float> sortedAscending, float p)
    {
        if (sortedAscending == null || sortedAscending.Count == 0) return 0f;
        if (sortedAscending.Count == 1) return sortedAscending[0];

        p = Mathf.Clamp01(p);
        float idx = (sortedAscending.Count - 1) * p;
        int lo = Mathf.FloorToInt(idx);
        int hi = Mathf.CeilToInt(idx);
        if (lo == hi) return sortedAscending[lo];
        float t = idx - lo;
        return Mathf.Lerp(sortedAscending[lo], sortedAscending[hi], t);
    }

    static (string region, string side) GroupKey(string region, string side) =>
        (region?.ToLowerInvariant() ?? "unknown", side?.ToLowerInvariant() ?? "midline");

    struct ResolvedSegment
    {
        public int entity;
        public string label;
        public int sn;
        public int tn;
        public float lengthL;
        public Vector3 start;
        public Vector3 end;
        public Vector3 direction;
        public string region;
        public string side;
    }

    struct SegmentLine
    {
        public int sn;
        public int tn;
        public Vector3 start;
        public Vector3 end;
    }
}

[Serializable]
public class EvaluationReport
{
    public string generatedAtUtc;
    public string unityVersion;
    public string sceneName;
    public TopologyMetrics topology;
    public OntologyMetrics ontology;
    public LayoutMetrics layout;
    public GeometryMetrics geometry;
    public PerformanceMetrics performance;
}

[Serializable]
public class TopologyMetrics
{
    public int jsonVesselCount;
    public int worldVesselCount;
    public int jsonNodeCount;
    public int worldNodeCount;
    public float vesselCoveragePct;
    public float nodeCoveragePct;
    public int missingVesselCount;
    public int extraVesselCount;
    public int missingNodeCount;
    public int duplicateLabelCount;
    public int endpointMismatchCount;
    public int lengthMismatchCount;
    public string[] missingVessels;
    public string[] extraVessels;
    public int[] missingNodes;
    public string[] duplicateLabels;
}

[Serializable]
public class OntologyMetrics
{
    public int vesselCount;
    public int sideSuffixKnownCount;
    public int sideSuffixMatchCount;
    public float sideSuffixMatchPct;
    public StringIntEntry[] regionCounts;
    public StringIntEntry[] sideCounts;
}

[Serializable]
public class LayoutMetrics
{
    public int segmentWithAnchorsCount;
    public int bifurcationCount;
    public float primaryAlignmentMeanAbsErrorDeg;
    public int branchAngleCheckCount;
    public float branchAngleMeanAbsErrorDeg;
    public int torsoPerpendicularCheckCount;
    public float torsoPerpendicularMeanAbsErrorDeg;
    public int sideRuleCheckCount;
    public float sideRuleCompliancePct;
    public int verticalUpEdgeCount;
    public float verticalUpCompliancePct;
}

[Serializable]
public class GeometryMetrics
{
    public int segmentCount;
    public int crossingCount2D;
    public int overlapCount;
}

[Serializable]
public class PerformanceMetrics
{
    public float sampleSeconds;
    public int frameCount;
    public float meanFrameMs;
    public float p95FrameMs;
    public float p99FrameMs;
    public float meanFps;
    public float monoMemoryDeltaMb;
}

[Serializable]
public class StringIntEntry
{
    public string key;
    public int value;
}
