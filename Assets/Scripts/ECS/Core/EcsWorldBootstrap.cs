// Assets/Scripts/ECS/EcsWorldBootstrap.cs
using UnityEngine;

public class EcsWorldBootstrap : MonoBehaviour
{
    public static EcsWorld World { get; private set; }

    void Awake()
    {
        // Bootstrap early so the loader/build systems can share one runtime store.
        World = new EcsWorld();
    }
}
