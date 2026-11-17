using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

partial struct BirdMovementSystem : ISystem
{
    public const int MaxNeighborCount = 16;

    private const float MaxDeltaTime = 0.05f;
    private const float MinRadius = 0.01f;
    private const float TargetBirdsPerCell = 4f;
    private const float MinCellFractionOfRadius = 0.125f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScreenBoundsData>();
        state.RequireForUpdate<SimulationConfigData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = math.min(SystemAPI.Time.DeltaTime, MaxDeltaTime);

        SimulationConfigData config = SystemAPI.GetSingleton<SimulationConfigData>();
        ScreenBoundsData bounds = SystemAPI.GetSingleton<ScreenBoundsData>();

        UpdateNeighbors(ref state, config, bounds);
        MoveBirds(ref state, config, bounds, deltaTime);
    }

    private void UpdateNeighbors(ref SystemState state, SimulationConfigData config, ScreenBoundsData bounds)
    {
        int birdCount = SystemAPI.QueryBuilder().WithAll<BirdData, LocalTransform, BirdMovement, CloseNeighbors>().Build().CalculateEntityCount();

        if (birdCount == 0)
            return;

        NativeArray<float3> positions = new NativeArray<float3>(birdCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float3> forwards = new NativeArray<float3>(birdCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float> speeds = new NativeArray<float>(birdCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeParallelMultiHashMap<int, int> grid = new NativeParallelMultiHashMap<int, int>(birdCount, Allocator.TempJob);

        float searchRadius = math.max(math.max(config.neighborRadius, config.personalRadius), MinRadius);
        float2 worldSize = bounds.max - bounds.min;
        float cellSize = math.clamp(math.sqrt(worldSize.x * worldSize.y * TargetBirdsPerCell / birdCount), searchRadius * MinCellFractionOfRadius, searchRadius);
        float inverseCellSize = 1f / cellSize;

        JobHandle buildHandle = new BuildGridJob
        {
            positions = positions,
            forwards = forwards,
            speeds = speeds,
            gridWriter = grid.AsParallelWriter(),
            inverseCellSize = inverseCellSize
        }.ScheduleParallel(state.Dependency);

        JobHandle neighborsHandle = new FindNeighborsJob
        {
            positions = positions,
            forwards = forwards,
            speeds = speeds,
            grid = grid,
            cellSize = cellSize,
            inverseCellSize = inverseCellSize,
            neighborRadiusSq = config.neighborRadius * config.neighborRadius,
            searchRadius = searchRadius,
            searchRadiusSq = searchRadius * searchRadius,
            viewCosThreshold = math.cos(math.radians(config.fieldOfViewDegrees * 0.5f)),
            maxNeighbors = MaxNeighborCount
        }.ScheduleParallel(buildHandle);

        JobHandle disposeHandle = JobHandle.CombineDependencies(
            positions.Dispose(neighborsHandle),
            forwards.Dispose(neighborsHandle),
            speeds.Dispose(neighborsHandle));

        state.Dependency = JobHandle.CombineDependencies(disposeHandle, grid.Dispose(neighborsHandle));
    }

    private void MoveBirds(ref SystemState state, SimulationConfigData config, ScreenBoundsData bounds, float deltaTime)
    {
        SystemAPI.TryGetSingleton<MouseInputData>(out MouseInputData mouse);
        mouse.position = math.clamp(mouse.position, bounds.min + config.edgeMargin, bounds.max - config.edgeMargin);

        state.Dependency = new MoveBirdsJob
        {
            config = config,
            boundsMin = bounds.min,
            boundsMax = bounds.max,
            mouse = mouse,
            deltaTime = deltaTime,
            neighborRadius = math.max(config.neighborRadius, MinRadius),
            personalRadius = math.max(config.personalRadius, MinRadius),
            edgeMargin = math.max(config.edgeMargin, MinRadius),
            turnFactor = 1f - math.exp(-config.turnRate * deltaTime),
            speedDecay = math.exp(-config.speedResponse * deltaTime)
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(BirdData))]
public partial struct MoveBirdsJob : IJobEntity
{
    public SimulationConfigData config;
    public float2 boundsMin;
    public float2 boundsMax;
    public MouseInputData mouse;
    public float deltaTime;
    public float neighborRadius;
    public float personalRadius;
    public float edgeMargin;
    public float turnFactor;
    public float speedDecay;

    private struct NeighborSums
    {
        public float3 headingSum;
        public float3 separation;

        // birds inside the field of view
        public float3 visiblePositionSum;
        public float visibleSpeedSum;
        public int visibleCount;

        // birds inside the neighbor radius, seen or not
        public float3 nearbyPositionSum;
        public int nearbyCount;
    }

    private void Execute(ref BirdMovement birdMovement, ref LocalTransform localTransform, in DynamicBuffer<CloseNeighbors> neighbors)
    {
        float3 position = localTransform.Position;
        float3 forward = math.mul(localTransform.Rotation, new float3(0f, 1f, 0f));

        NeighborSums sums = SumNeighbors(position, forward, neighbors);

        float3 groupHeading = math.normalizesafe(sums.headingSum, forward);
        float3 cohesion = CalculateCohesion(position, sums);
        float3 awayFromBorder = CalculateOutOfBounds(position);
        float3 mouseAttraction = CalculateMouseAttraction(position);

        float3 desiredDirection = math.normalizesafe(
            forward
            + groupHeading * config.alignmentWeight
            + sums.separation * config.separationWeight
            + awayFromBorder * config.edgeWeight
            + cohesion * config.cohesionWeight
            + mouseAttraction * config.mouseWeight,
            forward);

        float3 newForward = math.normalizesafe(math.lerp(forward, desiredDirection, turnFactor), forward);

        float targetSpeed = CalculateTargetSpeed(position, forward, sums);
        float speed = targetSpeed + (birdMovement.speed - targetSpeed) * speedDecay;

        float angle = math.atan2(newForward.y, newForward.x) - math.PI * 0.5f;

        birdMovement.speed = speed;
        localTransform.Rotation = quaternion.RotateZ(angle);
        localTransform.Position += newForward * speed * deltaTime;
    }

    private NeighborSums SumNeighbors(float3 position, float3 forward, in DynamicBuffer<CloseNeighbors> neighbors)
    {
        NeighborSums sums = default;
        sums.headingSum = forward;

        float neighborRadiusSq = neighborRadius * neighborRadius;

        for (int i = 0; i < neighbors.Length; i++)
        {
            CloseNeighbors neighbor = neighbors[i];
            float3 awayFrom = position - neighbor.position;
            float distance = math.length(awayFrom);

            // separation ignores the field of view so birds never fly into each other from behind
            if (distance < personalRadius)
                sums.separation += (awayFrom / distance) * (1f - distance / personalRadius);

            if (distance * distance <= neighborRadiusSq)
            {
                sums.nearbyPositionSum += neighbor.position;
                sums.nearbyCount++;
            }

            if (!neighbor.isVisible)
                continue;

            sums.headingSum += neighbor.forward;
            sums.visiblePositionSum += neighbor.position;
            sums.visibleSpeedSum += neighbor.speed;
            sums.visibleCount++;
        }

        return sums;
    }

    private float CalculateTargetSpeed(float3 position, float3 forward, in NeighborSums sums)
    {
        float targetSpeed = config.cruiseSpeed;

        if (sums.visibleCount > 0)
            targetSpeed += config.speedMatchWeight * (sums.visibleSpeedSum / sums.visibleCount - config.cruiseSpeed);

        if (sums.nearbyCount > 0)
        {
            float3 centroidOffset = sums.nearbyPositionSum / sums.nearbyCount - position;

            targetSpeed += config.catchUpGain * math.dot(centroidOffset, forward) / neighborRadius;
        }

        // brake when crowded ahead, speed up when crowded behind
        targetSpeed += config.separationWeight * math.dot(sums.separation, forward);

        return math.clamp(targetSpeed, config.minSpeed, config.maxSpeed);
    }

    private float3 CalculateCohesion(float3 position, in NeighborSums sums)
    {
        if (sums.visibleCount <= 0)
            return float3.zero;

        float3 centroidPosition = sums.visiblePositionSum / sums.visibleCount;

        return (centroidPosition - position) / neighborRadius;
    }

    private float3 CalculateMouseAttraction(float3 position)
    {
        if (!mouse.isActive || config.mouseRadius <= 0f)
            return float3.zero;

        float3 offset = new float3(mouse.position, position.z) - position;

        if (math.lengthsq(offset) >= config.mouseRadius * config.mouseRadius)
            return float3.zero;

        return offset / config.mouseRadius;
    }

    private float3 CalculateOutOfBounds(float3 position)
    {
        float3 newDirection = float3.zero;

        if (position.x < boundsMin.x + edgeMargin)
            newDirection.x = (boundsMin.x + edgeMargin - position.x) / edgeMargin;

        if (position.x > boundsMax.x - edgeMargin)
            newDirection.x = (boundsMax.x - edgeMargin - position.x) / edgeMargin;

        if (position.y < boundsMin.y + edgeMargin)
            newDirection.y = (boundsMin.y + edgeMargin - position.y) / edgeMargin;

        if (position.y > boundsMax.y - edgeMargin)
            newDirection.y = (boundsMax.y - edgeMargin - position.y) / edgeMargin;

        return newDirection;
    }
}
