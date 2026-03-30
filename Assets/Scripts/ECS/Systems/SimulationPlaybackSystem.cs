// Assets/Scripts/ECS/Systems/SimulationPlaybackSystem.cs
using System.Collections.Generic;
using UnityEngine;

public class SimulationPlaybackSystem : MonoBehaviour
{
    public enum ColorMetric
    {
        Pressure = 0,
        Flow = 1
    }

    [Header("Global diameter scale (ECS global setting)")]
    [Range(0.0001f, 10f)]
    public float globalDiameterScale = 1f;

    [Header("Color metric")]
    public ColorMetric colorMetric = ColorMetric.Pressure;

    [Header("Flow normalization")]
    [Range(0.1f, 2f)]
    public float flowColorGamma = 1f;

    MaterialPropertyBlock _mpb;
    static readonly Color kFlowNegative = new Color32(0x21, 0x66, 0xAC, 0xFF); // min flow
    static readonly Color kFlowNeutral = Color.white;                            // zero flow
    static readonly Color kFlowPositive = new Color32(0xB2, 0x18, 0x2B, 0xFF); // max flow

    // reuse list to avoid allocations each frame
    static readonly List<int> _keys = new List<int>(1024);

    // Cached reference (Unity 2023+ API)
    private HeartbeatController _hb;

    void Awake()
    {
        _mpb = new MaterialPropertyBlock();
    }

    void Start()
    {
        _hb = FindAnyObjectByType<HeartbeatController>();
    }

    void Update()
    {
        var world = EcsWorldBootstrap.World;
        if (world == null) return;

        // If controller was not found at Start (scene order), try again
        if (_hb == null) _hb = FindAnyObjectByType<HeartbeatController>();
        bool hasHeartbeat = (_hb != null);

        // Iterate over a stable snapshot to avoid collection modification issues mid-frame.
        _keys.Clear();
        _keys.AddRange(world.VesselSim.Keys);

        for (int i = 0; i < _keys.Count; i++)
        {
            int vesselE = _keys[i];

            if (!world.VesselSim.TryGetValue(vesselE, out var sim))
                continue;

            // Need AREA to compute diameter
            var areaSeries = sim.bundle?.area?.values;
            if (areaSeries == null || areaSeries.Count < 2)
                continue;

            int nA = areaSeries.Count;

            // Color series is optional and depends on selected metric.
            var colorSeries = GetColorSeries(sim);
            int nC = (colorSeries != null) ? colorSeries.Count : 0;
            bool hasColorSeries = (colorSeries != null && nC >= 2);

            // The heartbeat controller is the master timeline when present.
            float phase01;
            if (hasHeartbeat)
            {
                phase01 = Mathf.Clamp01(_hb.Phase01);
            }
            else
            {
                // Fallback to old stepping if no heartbeat controller exists
                sim.timer += Time.deltaTime;
                if (sim.timer < sim.frameDuration)
                {
                    world.VesselSim[vesselE] = sim;
                    continue;
                }
                sim.timer = 0f;
                sim.currentFrame = (sim.currentFrame + 1) % nA;
                phase01 = (nA <= 1) ? 0f : (sim.currentFrame / (float)(nA - 1));
            }

            // Diameter and color may sample different series lengths, so index them separately.
            // --- Area indices (for diameter) ---
            float xA = phase01 * (nA - 1);
            int a0 = Mathf.FloorToInt(xA);
            int a1 = Mathf.Min(a0 + 1, nA - 1);
            float tA = xA - a0;

            // Keep currentFrame coherent with sampling (use a0)
            sim.currentFrame = a0;

            // --- Color indices (for color), independent length ---
            int c0 = 0, c1 = 0;
            float tC = 0f;
            if (hasColorSeries)
            {
                float xC = phase01 * (nC - 1);
                c0 = Mathf.FloorToInt(xC);
                c1 = Mathf.Min(c0 + 1, nC - 1);
                tC = xC - c0;
            }

            // --------------------------
            // Diameter update (IMPORTANT):
            // apply thickness to mesh child axes that are NOT lengthAxis
            // --------------------------
            int partCountForColor = 1;
            if (world.SegmentRender.TryGetValue(vesselE, out var seg) && seg.meshChildren != null)
            {
                int axis = Mathf.Clamp(seg.lengthAxis, 0, 2);
                int ax0, ax1;
                if (axis == 0) { ax0 = 1; ax1 = 2; }
                else if (axis == 1) { ax0 = 0; ax1 = 2; }
                else { ax0 = 0; ax1 = 1; }

                int partCount = Mathf.Max(1, seg.subSegmentCount);
                partCountForColor = partCount;
                for (int sIdx = 0; sIdx < seg.meshChildren.Length; sIdx++)
                {
                    var child = seg.meshChildren[sIdx];
                    if (child == null) continue;
                    if (sIdx >= seg.meshChildInitialLocalScale.Length) continue;

                    float x01 = (sIdx + 0.5f) / partCount;
                    float diameter = ComputeDiameterInterpolated(sim, a0, a1, tA, x01, globalDiameterScale);

                    Vector3 s = child.localScale;
                    if (s.sqrMagnitude < 1e-12f) s = seg.meshChildInitialLocalScale[sIdx];

                    s[ax0] = diameter;
                    s[ax1] = diameter;
                    child.localScale = s;
                }
            }

            // --------------------------
            // Color update
            // --------------------------
            if (world.VesselMaterial.TryGetValue(vesselE, out var mat))
            {
                ApplyColors(world, sim, hasColorSeries, c0, c1, tC, mat, partCountForColor);
            }

            world.VesselSim[vesselE] = sim;
        }
    }

    List<float[]> GetColorSeries(VesselSimComponent sim)
    {
        return colorMetric == ColorMetric.Flow
            ? sim.bundle?.flow?.values
            : sim.bundle?.pressure?.values;
    }

    float ComputeDiameterInterpolated(VesselSimComponent sim, int i0, int i1, float t, float x01, float globalScale)
    {
        var areaSeries = sim.bundle.area.values;
        var f0 = areaSeries[i0];
        var f1 = areaSeries[i1];

        // Strong fallback so vessels never vanish visually
        if (f0 == null || f1 == null || f0.Length == 0 || f1.Length == 0)
            return Mathf.Max(sim.minDiameter, 0.01f);

        float area0 = SampleSpatialValue(f0, x01);
        float area1 = SampleSpatialValue(f1, x01);
        float area = Mathf.Lerp(area0, area1, t);

        float radius = Mathf.Sqrt(Mathf.Max(0f, area) / Mathf.PI);

        // First compute the physically-driven diameter and clamp it to the vessel's native bounds.
        // The user-facing global slider is then applied afterward so it always has a visible effect.
        float baseDiameter = 2f * radius * sim.diameterScaleFactor;
        baseDiameter = Mathf.Clamp(baseDiameter, sim.minDiameter, sim.maxDiameter);

        float diameter = baseDiameter * globalScale;

        // Extra safety clamp
        if (float.IsNaN(diameter) || float.IsInfinity(diameter))
            diameter = Mathf.Max(sim.minDiameter, 0.01f);

        return Mathf.Max(diameter, 0.0001f);
    }

    Color ComputeColorInterpolated(EcsWorld world, VesselSimComponent sim, int i0, int i1, float t)
    {
        return ComputeColorInterpolated(world, sim, i0, i1, t, 0.5f);
    }

    Color ComputeColorInterpolated(EcsWorld world, VesselSimComponent sim, int i0, int i1, float t, float x01)
    {
        var series = GetColorSeries(sim);
        if (series == null) return Color.white;

        var f0 = series[i0];
        var f1 = series[i1];
        if (f0 == null || f1 == null || f0.Length == 0 || f1.Length == 0) return Color.white;

        float v0 = SampleSpatialValue(f0, x01);
        float v1 = SampleSpatialValue(f1, x01);
        float v = Mathf.Lerp(v0, v1, t);

        if (colorMetric == ColorMetric.Flow)
            return ComputeFlowColor(world, sim, v);

        float norm = ComputeNormalizedColorValue(world, sim, v);
        return Color.Lerp(Color.blue, Color.red, norm);
    }

    float ComputeNormalizedColorValue(EcsWorld world, VesselSimComponent sim, float value)
    {
        if (colorMetric == ColorMetric.Flow)
        {
            // Flow can be normalized per-vessel or globally depending on the loaded ranges.
            if (sim.useGlobalFlowRange && world.GlobalFlowRange.valid)
                return Mathf.InverseLerp(world.GlobalFlowRange.minQ, world.GlobalFlowRange.maxQ, value);

            if (sim.localMaxFlow > sim.localMinFlow)
                return Mathf.InverseLerp(sim.localMinFlow, sim.localMaxFlow, value);

            return 0f;
        }

        if (sim.useGlobalPressureRange && world.GlobalPressureRange.valid)
            return Mathf.InverseLerp(world.GlobalPressureRange.minP, world.GlobalPressureRange.maxP, value);

        if (sim.localMaxPressure > sim.localMinPressure)
            return Mathf.InverseLerp(sim.localMinPressure, sim.localMaxPressure, value);

        return 0f;
    }

    Color ComputeFlowColor(EcsWorld world, VesselSimComponent sim, float value)
    {
        // Use a signed symlog transform so both flow direction and small-magnitude changes remain visible.
        float linearThreshold = GetFlowSymlogLinearThreshold(world, sim);
        float clipThreshold = GetFlowSymlogClipThreshold(world, sim);
        if (clipThreshold <= 1e-12f)
            return kFlowNeutral;

        float transformed = SignedSymlog(value, linearThreshold);
        float transformedMax = SignedSymlog(clipThreshold, linearThreshold);
        if (transformedMax <= 1e-12f)
            return kFlowNeutral;

        float t = Mathf.Clamp01(Mathf.Abs(transformed) / Mathf.Abs(transformedMax));
        t = Mathf.Pow(Mathf.Clamp01(t), Mathf.Clamp(flowColorGamma, 0.1f, 2f));

        if (Mathf.Abs(value) <= 1e-12f)
            return kFlowNeutral;

        return value < 0f
            ? Color.Lerp(kFlowNeutral, kFlowNegative, t)
            : Color.Lerp(kFlowNeutral, kFlowPositive, t);
    }

    float GetFlowSymlogLinearThreshold(EcsWorld world, VesselSimComponent sim)
    {
        if (sim.useGlobalFlowRange && world.GlobalFlowRange.valid)
            return Mathf.Max(world.GlobalFlowRange.symlogLinearQ, 1e-12f);

        float maxAbs = Mathf.Max(Mathf.Abs(sim.localMinFlow), Mathf.Abs(sim.localMaxFlow));
        return Mathf.Max(maxAbs * 0.10f, 1e-12f);
    }

    float GetFlowSymlogClipThreshold(EcsWorld world, VesselSimComponent sim)
    {
        if (sim.useGlobalFlowRange && world.GlobalFlowRange.valid)
            return Mathf.Max(world.GlobalFlowRange.symlogClipQ, 1e-12f);

        return Mathf.Max(Mathf.Abs(sim.localMinFlow), Mathf.Abs(sim.localMaxFlow));
    }

    float SignedSymlog(float value, float linearThreshold)
    {
        if (Mathf.Approximately(value, 0f))
            return 0f;

        float scale = Mathf.Max(linearThreshold, 1e-12f);
        return Mathf.Sign(value) * Mathf.Log(1f + Mathf.Abs(value) / scale);
    }

    public void SetColorMetric(int metricIndex)
    {
        if (metricIndex == 1) colorMetric = ColorMetric.Flow;
        else colorMetric = ColorMetric.Pressure;
    }

    float SampleSpatialValue(float[] samples, float x01)
    {
        if (samples == null || samples.Length == 0) return 0f;
        if (samples.Length == 1) return samples[0];

        x01 = Mathf.Clamp01(x01);
        float x = x01 * (samples.Length - 1);
        int i0 = Mathf.FloorToInt(x);
        int i1 = Mathf.Min(i0 + 1, samples.Length - 1);
        float t = x - i0;
        return Mathf.Lerp(samples[i0], samples[i1], t);
    }

    void ApplyColors(EcsWorld world, VesselSimComponent sim, bool hasColorSeries, int i0, int i1, float t, VesselMaterialComponent mat, int partCount)
    {
        if (mat.renderers == null || mat.renderers.Length == 0) return;

        int n = Mathf.Max(1, partCount);
        for (int s = 0; s < mat.renderers.Length; s++)
        {
            var r = mat.renderers[s];
            if (r == null) continue;

            Color c = Color.white;
            if (hasColorSeries)
            {
                float x01 = (s + 0.5f) / n;
                c = ComputeColorInterpolated(world, sim, i0, i1, t, x01);
            }
            ApplyColor(r, mat.baseColorPropId, c);
        }
    }

    void ApplyColor(Renderer renderer, int colorPropId, Color c)
    {
        if (renderer == null) return;
        renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(colorPropId, c);
        renderer.SetPropertyBlock(_mpb);
    }
}
