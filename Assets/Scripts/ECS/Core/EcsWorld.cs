// Assets/Scripts/ECS/EcsWorld.cs
using System;
using System.Collections.Generic;

public sealed class EcsWorld
{
    public int NextEntityId { get; private set; } = 1;

    // Each dictionary acts as a lightweight ECS component store keyed by entity id.
    public readonly Dictionary<int, NodeComponent> Nodes = new();
    public readonly Dictionary<int, BoneFollowComponent> BoneFollows = new();

    public readonly Dictionary<int, VesselTopologyComponent> VesselTopology = new();
    public readonly Dictionary<int, SegmentRenderComponent> SegmentRender = new();
    public readonly Dictionary<int, VesselSimComponent> VesselSim = new();
    public readonly Dictionary<int, VesselMaterialComponent> VesselMaterial = new();

    // Simple lookup from domain keys
    public readonly Dictionary<int, int> NodeIdToEntity = new();           // nodeId -> entityId
    public readonly Dictionary<string, int> VesselLabelToEntity = new(StringComparer.OrdinalIgnoreCase);

    // Global ranges are stored once because playback normalizes across the whole network.
    public GlobalPressureRangeComponent GlobalPressureRange = new()
    {
        minP = 0f,
        maxP = 1f,
        valid = false
    };

    public GlobalFlowRangeComponent GlobalFlowRange = new()
    {
        minQ = 0f,
        maxQ = 1f,
        valid = false
    };

    public int CreateEntity() => NextEntityId++;
}
