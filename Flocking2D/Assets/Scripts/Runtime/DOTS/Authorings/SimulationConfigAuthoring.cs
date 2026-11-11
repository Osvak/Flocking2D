using Unity.Entities;
using UnityEngine;

class SimulationConfigAuthoring : MonoBehaviour
{
    [SerializeField]
    private SimulationConfigData config = new SimulationConfigData
    {
        alignmentWeight = 1f,
        separationWeight = 1.5f,
        cohesionWeight = 1f,
        edgeWeight = 3f,
        edgeMargin = 2f,
        turnRate = 4f,
        fieldOfViewDegrees = 240f,
        neighborsRadius = 1.5f,
        personalRadius = 0.4f,
        cruiseSpeed = 3f,
        minSpeed = 1.5f,
        maxSpeed = 4.5f,
        speedMatchWeight = 0.5f,
        catchUpGain = 2f,
        speedResponse = 3f,
        mouseRadius = 3f,
        mouseWeight = 3f,
        showMouseRadius = true,
        showPersonalRadius = true,
        showNeighborRadius = true,
        showFieldOfView = true
    };

    class SimulationConfigAuthoringBaker : Baker<SimulationConfigAuthoring>
    {
        public override void Bake(SimulationConfigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, authoring.config);
        }
    }
}
