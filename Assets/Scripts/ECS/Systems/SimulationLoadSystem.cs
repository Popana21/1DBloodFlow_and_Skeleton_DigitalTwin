// Assets/Scripts/ECS/SimulationLoadSystem.cs
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class SimulationLoadSystem : MonoBehaviour
{
    [Header("Last files folder (StreamingAssets)")]
    public string lastFilesSubfolder = "LastFiles";

    [Header("Run")]
    public bool loadOnStart = true;

    struct SignalRange
    {
        public float min;
        public float max;
        public bool valid;
    }

    void Start()
    {
        if (!loadOnStart) return;

        var world = EcsWorldBootstrap.World;
        if (world == null) { Debug.LogError("SimulationLoadSystem: missing EcsWorld."); return; }

        string basePath = Path.Combine(Application.streamingAssetsPath, lastFilesSubfolder);
        if (!Directory.Exists(basePath))
        {
            Debug.LogError($"SimulationLoadSystem: folder not found: {basePath}");
            return;
        }

        // Accumulate project-wide ranges once so playback can normalize consistently.
        float gmin = float.MaxValue;
        float gmax = float.MinValue;
        float qmin = float.MaxValue;
        float qmax = float.MinValue;
        var negativeFlowSamples = new List<float>(4096);
        var positiveFlowSamples = new List<float>(4096);
        var absoluteFlowSamples = new List<float>(4096);

        foreach (var kv in world.VesselTopology)
        {
            int vesselE = kv.Key;
            var topo = kv.Value;

            string areaPath = Path.Combine(basePath, topo.label + "_A.last");
            string pressPath = Path.Combine(basePath, topo.label + "_P.last");
            string flowPath = Path.Combine(basePath, topo.label + "_Q.last");

            var area = SimulationDataLoader.LoadLastFile(areaPath);
            var press = SimulationDataLoader.LoadLastFile(pressPath);
            var flow = SimulationDataLoader.LoadLastFile(flowPath);

            bool hasAny = (area.values.Count > 0) || (press.values.Count > 0) || (flow.values.Count > 0);
            if (!hasAny) continue;

            // Load all three signal files into the bundle attached to this vessel entity.
            if (!world.VesselSim.TryGetValue(vesselE, out var sim))
                sim = new VesselSimComponent();

            sim.bundle = new VesselSimulationBundle { area = area, pressure = press, flow = flow };
            sim.currentFrame = 0;
            sim.timer = 0f;

            SignalRange pressureRange = ComputeSignalRange(press);
            sim.localMinPressure = pressureRange.valid ? pressureRange.min : 0f;
            sim.localMaxPressure = pressureRange.valid ? pressureRange.max : 1f;
            if (pressureRange.valid)
            {
                if (pressureRange.min < gmin) gmin = pressureRange.min;
                if (pressureRange.max > gmax) gmax = pressureRange.max;
            }

            SignalRange flowRange = ComputeSignalRange(flow);
            sim.localMinFlow = flowRange.valid ? flowRange.min : 0f;
            sim.localMaxFlow = flowRange.valid ? flowRange.max : 1f;
            if (flowRange.valid)
            {
                if (flowRange.min < qmin) qmin = flowRange.min;
                if (flowRange.max > qmax) qmax = flowRange.max;
                CollectSignedFlowSamples(flow, negativeFlowSamples, positiveFlowSamples);
                CollectAbsoluteFlowSamples(flow, absoluteFlowSamples);
            }

            sim.useGlobalPressureRange = true;
            sim.useGlobalFlowRange = true;
            world.VesselSim[vesselE] = sim;
        }

        // Finalize the global ranges only after every vessel has contributed its samples.
        world.GlobalPressureRange.valid = (gmax > gmin);
        world.GlobalPressureRange.minP = world.GlobalPressureRange.valid ? gmin : 0f;
        world.GlobalPressureRange.maxP = world.GlobalPressureRange.valid ? gmax : 1f;
        world.GlobalFlowRange.valid = (qmax > qmin);
        world.GlobalFlowRange.minQ = world.GlobalFlowRange.valid ? qmin : 0f;
        world.GlobalFlowRange.maxQ = world.GlobalFlowRange.valid ? qmax : 1f;
        world.GlobalFlowRange.sortedNegativeQ = negativeFlowSamples.ToArray();
        world.GlobalFlowRange.sortedPositiveQ = positiveFlowSamples.ToArray();
        System.Array.Sort(world.GlobalFlowRange.sortedNegativeQ);
        System.Array.Sort(world.GlobalFlowRange.sortedPositiveQ);
        absoluteFlowSamples.Sort();
        // Percentile-based thresholds keep signed flow coloring readable near zero and at peaks.
        world.GlobalFlowRange.symlogLinearQ = ComputePercentile(absoluteFlowSamples, 0.10f);
        world.GlobalFlowRange.symlogClipQ = ComputePercentile(absoluteFlowSamples, 0.95f);
        if (world.GlobalFlowRange.symlogLinearQ <= 0f)
            world.GlobalFlowRange.symlogLinearQ = Mathf.Max(Mathf.Abs(world.GlobalFlowRange.minQ), Mathf.Abs(world.GlobalFlowRange.maxQ)) * 0.05f;
        if (world.GlobalFlowRange.symlogClipQ <= 0f)
            world.GlobalFlowRange.symlogClipQ = Mathf.Max(Mathf.Abs(world.GlobalFlowRange.minQ), Mathf.Abs(world.GlobalFlowRange.maxQ));
        if (world.GlobalFlowRange.symlogClipQ < world.GlobalFlowRange.symlogLinearQ)
            world.GlobalFlowRange.symlogClipQ = world.GlobalFlowRange.symlogLinearQ;

        Debug.Log($"SimulationLoadSystem: globalPressureRange valid={world.GlobalPressureRange.valid} [{world.GlobalPressureRange.minP}, {world.GlobalPressureRange.maxP}]");
        Debug.Log($"SimulationLoadSystem: globalFlowRange valid={world.GlobalFlowRange.valid} [{world.GlobalFlowRange.minQ}, {world.GlobalFlowRange.maxQ}]");
        Debug.Log($"SimulationLoadSystem: signed flow samples negatives={world.GlobalFlowRange.sortedNegativeQ.Length} positives={world.GlobalFlowRange.sortedPositiveQ.Length}");
        Debug.Log($"SimulationLoadSystem: symlog flow parameters linearQ(P10)={world.GlobalFlowRange.symlogLinearQ} clipQ(P95)={world.GlobalFlowRange.symlogClipQ}");
    }

    SignalRange ComputeSignalRange(VesselSimulationData data)
    {
        if (data.values == null || data.values.Count == 0)
            return new SignalRange { min = 0f, max = 1f, valid = false };

        float min = float.MaxValue;
        float max = float.MinValue;

        foreach (var frame in data.values)
        {
            if (frame == null) continue;
            for (int i = 0; i < frame.Length; i++)
            {
                float v = frame[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        bool valid = max > min;
        return new SignalRange
        {
            min = valid ? min : 0f,
            max = valid ? max : 1f,
            valid = valid
        };
    }

    void CollectSignedFlowSamples(VesselSimulationData data, List<float> negatives, List<float> positives)
    {
        if (data.values == null || data.values.Count == 0) return;

        foreach (var frame in data.values)
        {
            if (frame == null) continue;
            for (int i = 0; i < frame.Length; i++)
            {
                float v = frame[i];
                if (v < 0f) negatives.Add(v);
                else if (v > 0f) positives.Add(v);
            }
        }
    }

    void CollectAbsoluteFlowSamples(VesselSimulationData data, List<float> absValues)
    {
        if (data.values == null || data.values.Count == 0) return;

        foreach (var frame in data.values)
        {
            if (frame == null) continue;
            for (int i = 0; i < frame.Length; i++)
            {
                float a = Mathf.Abs(frame[i]);
                if (a > 0f) absValues.Add(a);
            }
        }
    }

    float ComputePercentile(List<float> sortedValues, float percentile)
    {
        if (sortedValues == null || sortedValues.Count == 0)
            return 0f;

        percentile = Mathf.Clamp01(percentile);
        if (sortedValues.Count == 1)
            return sortedValues[0];

        float pos = percentile * (sortedValues.Count - 1);
        int i0 = Mathf.FloorToInt(pos);
        int i1 = Mathf.Min(i0 + 1, sortedValues.Count - 1);
        float t = pos - i0;
        return Mathf.Lerp(sortedValues[i0], sortedValues[i1], t);
    }
}
