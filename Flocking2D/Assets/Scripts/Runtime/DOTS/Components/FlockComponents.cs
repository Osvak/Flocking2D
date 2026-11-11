using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

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
    public int ID;
}

public struct BirdMovement : IComponentData
{
    public float velocity;
}

[InternalBufferCapacity(8)]
public struct CloseNeighbors : IBufferElementData
{
    public float3 position;
    public float3 forward;
    public float velocity;
    public bool isVisible;
}
