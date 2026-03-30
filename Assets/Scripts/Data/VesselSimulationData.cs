// Assets/Scripts/VesselSimulationData.cs
using System.Collections.Generic;

[System.Serializable]
public class VesselSimulationData
{
    // One timestamp per solver sample row.
    public List<float> timestamps = new();
    // Each entry stores the spatial samples along the vessel at that timestamp.
    public List<float[]> values = new();
}

[System.Serializable]
public class VesselSimulationBundle
{
    // Bundle the three signals so runtime systems can fetch them per vessel entity.
    public VesselSimulationData pressure = new();
    public VesselSimulationData flow = new();
    public VesselSimulationData area = new();
}
