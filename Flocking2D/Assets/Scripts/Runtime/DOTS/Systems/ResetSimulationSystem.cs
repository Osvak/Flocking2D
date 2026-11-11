using Unity.Burst;
using Unity.Entities;

[UpdateBefore(typeof(BirdMovementSystem))]
partial struct ResetSimulationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ResetSimulationRequest>();
        state.RequireForUpdate<SpawnerData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        uint seed = 1u;

        foreach (var request in SystemAPI.Query<RefRO<ResetSimulationRequest>>())
        {
            seed = request.ValueRO.seed;
        }

        state.EntityManager.DestroyEntity(SystemAPI.QueryBuilder().WithAll<ResetSimulationRequest>().Build());
        state.EntityManager.DestroyEntity(SystemAPI.QueryBuilder().WithAll<BirdData>().Build());

        SystemAPI.GetSingletonRW<SpawnerData>().ValueRW.seed = seed;

        state.WorldUnmanaged.GetExistingSystemState<BirdSpawnerSystem>().Enabled = true;
    }
}
