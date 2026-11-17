using Unity.Entities;
using UnityEngine;

class BirdAuthoring : MonoBehaviour
{
    [SerializeField] private float speed;

    class BirdAuthoringBaker : Baker<BirdAuthoring>
    {
        public override void Bake(BirdAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new BirdData());
            AddComponent(entity, new BirdMovement { speed = authoring.speed });
            AddBuffer<CloseNeighbors>(entity);
        }
    }
}
