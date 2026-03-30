// Assets/Scripts/ECS/EcsComponents.cs
using UnityEngine;

public struct NodeComponent
{
    // Anchor is the world-space transform other systems move and align against.
    public int nodeId;
    public Transform anchor;
}

public struct BoneFollowComponent
{
    // Keeps a mapped node attached to a skeleton bone after initial placement.
    public Transform bone;
    public Vector3 localPosOffset;
    public Vector3 localEulerOffset;
    public bool applyRotation;
}

public struct VesselTopologyComponent
{
    // Minimal topology info needed at runtime after JSON import.
    public string label;
    public int snNodeId;
    public int tnNodeId;
    public float lengthL;
}

public struct SegmentRenderComponent
{
    public Transform vesselRootTransform; // instantiated prefab root
    public Transform[] subSegmentRoots;
    public Transform[] meshChildren;
    public Vector3[] meshBoundsSizeLocal;
    public Vector3[] meshChildInitialLocalScale;
    public Vector3[] meshChildInitialLocalPosition;
    public int lengthAxis;              // 0=X,1=Y,2=Z
    public float[] initialAxisWorldLength;
    public float minLength;
    public int subSegmentCount;
}

public struct VesselSimComponent
{
    // Stores playback state plus per-vessel ranges derived during loading.
    public VesselSimulationBundle bundle;
    public int currentFrame;
    public float timer;
    public float frameDuration;

    public float localMinPressure;
    public float localMaxPressure;
    public float localMinFlow;
    public float localMaxFlow;

    public float diameterScaleFactor;
    public float minDiameter;
    public float maxDiameter;

    public bool useGlobalPressureRange;
    public bool useGlobalFlowRange;
}

public struct VesselMaterialComponent
{
    // Cache renderer handles and shader property IDs for per-frame color updates.
    public Renderer[] renderers;
    public int baseColorPropId;
}

public struct GlobalPressureRangeComponent
{
    public float minP;
    public float maxP;
    public bool valid;
}

public struct GlobalFlowRangeComponent
{
    // Extra arrays/thresholds support signed symlog normalization for flow colors.
    public float minQ;
    public float maxQ;
    public bool valid;
    public float[] sortedNegativeQ;
    public float[] sortedPositiveQ;
    public float symlogLinearQ;
    public float symlogClipQ;
}
