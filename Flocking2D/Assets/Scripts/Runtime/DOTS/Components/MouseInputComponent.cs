using Unity.Entities;
using Unity.Mathematics;

public struct MouseInputData : IComponentData
{
    public float2 position;
    public bool isActive;
}
