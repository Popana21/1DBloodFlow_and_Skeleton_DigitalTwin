// Assets/Scripts/ECS/NetworkLoadSystem.cs
using System.IO;
using UnityEngine;

public class NetworkLoadSystem : MonoBehaviour
{
    [Header("Topology JSON Source")]
    public TextAsset vesselsJsonTextAsset;
    public string streamingAssetsJsonFileName = "vessels.json";

    [Header("Run")]
    public bool loadOnStart = true;

    void Start()
    {
        if (!loadOnStart) return;

        var world = EcsWorldBootstrap.World;
        if (world == null)
        {
            Debug.LogError("NetworkLoadSystem: EcsWorld not ready (missing EcsWorldBootstrap?).");
            return;
        }

        string json = null;

        if (vesselsJsonTextAsset != null)
        {
            // Editor-friendly path: drag a TextAsset directly into the inspector.
            json = vesselsJsonTextAsset.text;
        }
        else
        {
            // Build/runtime path: load the generated topology contract from StreamingAssets.
            string path = Path.Combine(Application.streamingAssetsPath, streamingAssetsJsonFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"NetworkLoadSystem: JSON not found at {path}");
                return;
            }
            json = File.ReadAllText(path);
        }

        var arr = JsonUtility.FromJson<VesselArray>(json);
        if (arr == null || arr.vessels == null)
        {
            Debug.LogError("NetworkLoadSystem: Failed parsing JSON. Expect {\"vessels\":[...]}");
            return;
        }

        // Create vessel entities and lazily create the endpoint node entities they reference.
        foreach (var v in arr.vessels)
        {
            if (v == null || string.IsNullOrEmpty(v.label)) continue;

            EnsureNodeEntity(world, v.sn);
            EnsureNodeEntity(world, v.tn);

            int vesselE = world.CreateEntity();
            world.VesselLabelToEntity[v.label] = vesselE;
            world.VesselTopology[vesselE] = new VesselTopologyComponent
            {
                label = v.label,
                snNodeId = v.sn,
                tnNodeId = v.tn,
                lengthL = v.L
            };
        }

        Debug.Log($"NetworkLoadSystem: Loaded vessels={arr.vessels.Length}, nodes={world.NodeIdToEntity.Count}");
    }

    static int EnsureNodeEntity(EcsWorld world, int nodeId)
    {
        if (world.NodeIdToEntity.TryGetValue(nodeId, out int e))
            return e;

        e = world.CreateEntity();
        world.NodeIdToEntity[nodeId] = e;

        // Mapping creates the actual anchor transform once the skeleton context is available.
        world.Nodes[e] = new NodeComponent { nodeId = nodeId, anchor = null };
        return e;
    }
}
