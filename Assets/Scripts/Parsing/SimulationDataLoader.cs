// Assets/Scripts/SimulationDataLoader.cs
using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class SimulationDataLoader
{
    public static VesselSimulationData LoadLastFile(string filePath)
    {
        var data = new VesselSimulationData
        {
            timestamps = new List<float>(),
            values = new List<float[]>()
        };

        if (!File.Exists(filePath))
        {
            Debug.LogWarning("SimulationDataLoader: file not found: " + filePath);
            return data;
        }

        var lines = File.ReadAllLines(filePath);
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var line = raw.Trim();
            if (line.StartsWith("#") || line.StartsWith("//")) continue;

            // Accept the whitespace/comma-separated export variants encountered in .last files.
            var tokens = line.Split(new char[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;

            if (!float.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float t))
                continue;

            var vals = new float[tokens.Length - 1];
            bool ok = true;
            for (int i = 1; i < tokens.Length; i++)
            {
                if (!float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                {
                    ok = false; break;
                }
                vals[i - 1] = v;
            }
            if (!ok) continue;

            // Preserve the original time -> sampled-values structure from OpenBF exports.
            data.timestamps.Add(t);
            data.values.Add(vals);
        }

        return data;
    }
}
