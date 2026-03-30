// Assets/Scripts/ECS/BoneFollowSystem.cs
using UnityEngine;

public class BoneFollowSystem : MonoBehaviour
{
    void LateUpdate()
    {
        var world = EcsWorldBootstrap.World;
        if (world == null) return;

        // Run late so anchors track the final bone pose for the current frame.
        foreach (var kv in world.BoneFollows)
        {
            int entity = kv.Key;
            var bf = kv.Value;

            if (!world.Nodes.TryGetValue(entity, out var node)) continue;
            if (node.anchor == null || bf.bone == null) continue;

            node.anchor.position = bf.bone.TransformPoint(bf.localPosOffset);
            if (bf.applyRotation)
                node.anchor.rotation = bf.bone.rotation * Quaternion.Euler(bf.localEulerOffset);
        }
    }
}
