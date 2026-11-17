using Unity.Entities;
using Unity.Mathematics;

public struct SpawnerData : IComponentData
{
    public const int MaxBirdAmount = 10000;

    public Entity prefab;
    public float cameraOffset;
    public int birdAmount;
    public uint seed;
}

public struct ScreenBoundsData : IComponentData
{
    public float2 min;
    public float2 max;
}

public struct BirdData : IComponentData
{
    public int id;
}

public struct BirdMovement : IComponentData
{
    public float speed;
}

// matches BirdMovementSystem.MaxNeighborCount so the list always fits inside the chunk
[InternalBufferCapacity(BirdMovementSystem.MaxNeighborCount)]
public struct CloseNeighbors : IBufferElementData
{
    public float3 position;
    public float3 forward;
    public float speed;
    public bool isVisible;
}
