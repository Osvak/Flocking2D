using Unity.Entities;
using UnityEngine;

public class BirdSpawnerAuthoring : MonoBehaviour
{
    [SerializeField] private float cameraOffset;
    [SerializeField] private GameObject birdPrefab;
    [SerializeField, Range(1, SpawnerData.MaxBirdAmount)] private int birdAmount;

    private class BirdSpawnerBaker : Baker<BirdSpawnerAuthoring>
    {
        public override void Bake(BirdSpawnerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new SpawnerData
            {
                prefab = GetEntity(authoring.birdPrefab, TransformUsageFlags.Dynamic),
                cameraOffset = authoring.cameraOffset,
                birdAmount = authoring.birdAmount
            });
        }
    }
}