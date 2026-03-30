// Assets/Scripts/ECS/Systems/VesselBuildSystem.cs
using UnityEngine;

public class VesselBuildSystem : MonoBehaviour
{
    // Split each logical vessel into several pieces so playback can vary color/diameter along its length.
    const int kSubSegmentCount = 4;

    [Header("Prefab + Root")]
    public GameObject vesselPrefab;
    public Transform vesselRoot;

    [Header("Segment Settings")]
    [Tooltip("0 = X, 1 = Y, 2 = Z (axis along which the mesh is 'long')")]
    public int lengthAxis = 1;

    [Tooltip("Minimum visual length in world units to avoid degeneracy.")]
    public float minLength = 0.001f;

    [Header("Run")]
    public bool buildOnStart = true;

    void Start()
    {
        if (!buildOnStart) return;

        var world = EcsWorldBootstrap.World;
        if (world == null) { Debug.LogError("VesselBuildSystem: missing EcsWorld."); return; }
        if (vesselPrefab == null) { Debug.LogError("VesselBuildSystem: vesselPrefab not assigned."); return; }
        if (vesselRoot == null) vesselRoot = this.transform;

        int built = 0;
        int axis = Mathf.Clamp(lengthAxis, 0, 2);

        foreach (var kv in world.VesselTopology)
        {
            int vesselE = kv.Key;
            var topo = kv.Value;

            if (!world.NodeIdToEntity.TryGetValue(topo.snNodeId, out int snE)) continue;
            if (!world.NodeIdToEntity.TryGetValue(topo.tnNodeId, out int tnE)) continue;

            var sn = world.Nodes[snE];
            var tn = world.Nodes[tnE];
            if (sn.anchor == null || tn.anchor == null) continue;

            // One empty root per vessel keeps later alignment and picking logic simple.
            var rootGo = new GameObject(topo.label);
            rootGo.transform.SetParent(vesselRoot, false);

            var seg = new SegmentRenderComponent
            {
                vesselRootTransform = rootGo.transform,
                lengthAxis = axis,
                minLength = minLength,
                subSegmentCount = kSubSegmentCount,
                subSegmentRoots = new Transform[kSubSegmentCount],
                meshChildren = new Transform[kSubSegmentCount],
                meshBoundsSizeLocal = new Vector3[kSubSegmentCount],
                meshChildInitialLocalScale = new Vector3[kSubSegmentCount],
                meshChildInitialLocalPosition = new Vector3[kSubSegmentCount],
                initialAxisWorldLength = new float[kSubSegmentCount]
            };

            var renderers = new Renderer[kSubSegmentCount];
            bool builtAll = true;

            for (int s = 0; s < kSubSegmentCount; s++)
            {
                var piece = Instantiate(vesselPrefab, rootGo.transform);
                piece.name = $"{topo.label}_seg{s}";
                seg.subSegmentRoots[s] = piece.transform;

                if (!TryInitSegmentRender(piece.transform, axis, out Transform meshChild, out Vector3 meshBoundsLocal, out Vector3 initialScale, out float axisWorldLength))
                {
                    builtAll = false;
                    break;
                }

                seg.meshChildren[s] = meshChild;
                seg.meshBoundsSizeLocal[s] = meshBoundsLocal;
                seg.meshChildInitialLocalScale[s] = initialScale;
                seg.meshChildInitialLocalPosition[s] = meshChild.localPosition;
                seg.initialAxisWorldLength[s] = axisWorldLength;

                EnsureRaycastCollider(meshChild);

                renderers[s] = piece.GetComponent<Renderer>() ?? piece.GetComponentInChildren<Renderer>(true);
            }

            if (!builtAll)
            {
                Debug.LogWarning($"VesselBuildSystem: prefab '{vesselPrefab.name}' has no mesh child (MeshFilter/SkinnedMeshRenderer).");
                Destroy(rootGo);
                continue;
            }

            world.SegmentRender[vesselE] = seg;

            world.VesselMaterial[vesselE] = new VesselMaterialComponent
            {
                renderers = renderers,
                baseColorPropId = Shader.PropertyToID("_BaseColor") // URP
            };

            // Initialize defaults now; SimulationLoadSystem will replace the bundle later.
            world.VesselSim[vesselE] = new VesselSimComponent
            {
                bundle = null,
                currentFrame = 0,
                timer = 0f,
                frameDuration = 0.02f,

                localMinPressure = float.MaxValue,
                localMaxPressure = float.MinValue,
                localMinFlow = float.MaxValue,
                localMaxFlow = float.MinValue,

                diameterScaleFactor = 1f,
                minDiameter = 0.0001f,
                maxDiameter = 3f,

                useGlobalPressureRange = true,
                useGlobalFlowRange = true
            };

            built++;
        }

        Debug.Log($"VesselBuildSystem: built vessels={built}");
    }

    static bool TryInitSegmentRender(Transform vesselRoot, int lengthAxis, out Transform meshChild, out Vector3 meshBoundsSizeLocal, out Vector3 meshChildInitialLocalScale, out float initialAxisWorldLength)
    {
        meshChild = null;
        meshBoundsSizeLocal = default;
        meshChildInitialLocalScale = default;
        initialAxisWorldLength = 1f;
        lengthAxis = Mathf.Clamp(lengthAxis, 0, 2);

        // Prefer MeshFilter because most vessel prefabs are static meshes.
        var mf = vesselRoot.GetComponentInChildren<MeshFilter>(true);
        if (mf != null && mf.sharedMesh != null)
        {
            meshChild = mf.transform;
            meshBoundsSizeLocal = mf.sharedMesh.bounds.size;
            meshChildInitialLocalScale = meshChild.localScale;
            initialAxisWorldLength = ComputeInitialAxisWorldLength(meshChild, meshBoundsSizeLocal, lengthAxis);
            return true;
        }

        // Fallback keeps the system compatible with skinned vessel assets.
        var smr = vesselRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null && smr.sharedMesh != null)
        {
            meshChild = smr.transform;
            meshBoundsSizeLocal = smr.sharedMesh.bounds.size;
            meshChildInitialLocalScale = meshChild.localScale;
            initialAxisWorldLength = ComputeInitialAxisWorldLength(meshChild, meshBoundsSizeLocal, lengthAxis);
            return true;
        }

        return false;
    }

    static float ComputeInitialAxisWorldLength(Transform meshChild, Vector3 boundsSizeLocal, int axis)
    {
        axis = Mathf.Clamp(axis, 0, 2);

        float axisSizeLocal = boundsSizeLocal[axis];
        if (axisSizeLocal <= 1e-6f) return 1f;

        Vector3 lossy = meshChild.lossyScale;
        float axisWorldScale =
            (axis == 0) ? lossy.x :
            (axis == 2) ? lossy.z :
                          lossy.y;

        return Mathf.Max(1e-6f, axisWorldScale * axisSizeLocal);
    }

    static void EnsureRaycastCollider(Transform meshChild)
    {
        if (meshChild == null) return;

        if (meshChild.GetComponent<Collider>() != null)
            return;

        var mc = meshChild.gameObject.AddComponent<MeshCollider>();
        mc.convex = false;
    }
}
