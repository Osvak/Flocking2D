using System;
using Unity.Entities;

[Serializable]
public struct SimulationConfigData : IComponentData
{
    public float alignmentWeight;
    public float separationWeight;
    public float cohesionWeight;
    public float edgeWeight;
    public float edgeMargin;

    public float turnRate;
    public float fieldOfViewDegrees;
    public float neighborsRadius;
    public float personalRadius;

    public float cruiseSpeed;
    public float minSpeed;
    public float maxSpeed;
    public float speedMatchWeight;
    public float catchUpGain;
    public float speedResponse;

    public float mouseRadius;
    public float mouseWeight;
    public bool showMouseRadius;
    public bool showPersonalRadius;
    public bool showNeighborRadius;
    public bool showFieldOfView;
}

public struct ResetSimulationRequest : IComponentData
{
    public uint seed;
}
