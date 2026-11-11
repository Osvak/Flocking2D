using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

partial struct BirdSpawnerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpawnerData>();
        state.RequireForUpdate<ScreenBoundsData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity spawnerEntity = SystemAPI.GetSingletonEntity<SpawnerData>();
        SpawnerData spawner = SystemAPI.GetComponent<SpawnerData>(spawnerEntity);
        ScreenBoundsData bounds = SystemAPI.GetSingleton<ScreenBoundsData>();

        float2 size = bounds.max - bounds.min;
        float2 spawnMin = bounds.min + size * spawner.cameraOffset;
        float2 spawnMax = bounds.max - size * spawner.cameraOffset;

        Random random = Random.CreateFromIndex(math.max(1u, spawner.seed));

        LocalTransform prefabTransform = state.EntityManager.GetComponentData<LocalTransform>(spawner.prefab);

        NativeArray<Entity> birds = state.EntityManager.Instantiate(spawner.prefab, math.clamp(spawner.birdAmount, 1, SpawnerData.MaxBirdAmount), Allocator.Temp);

        for (int i = 0; i < birds.Length; i++)
        {
            float2 spawnPos = random.NextFloat2(spawnMin, spawnMax);
            quaternion rotation = quaternion.RotateZ(random.NextFloat(0f, math.PI2));

            state.EntityManager.SetComponentData(birds[i], prefabTransform.WithPosition(new float3(spawnPos, 0f)).WithRotation(rotation));
            state.EntityManager.SetComponentData(birds[i], new BirdData { ID = i });
        }

        birds.Dispose();

        state.Enabled = false;
    }
}
