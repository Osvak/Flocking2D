using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

partial struct BirdMovementSystem : ISystem
{
    private const float maxDeltaTime = 0.05f;
    private const float minRadius = 0.01f;
    private const int maxNeighborCount = 16;
    private const float targetBirdsPerCell = 4f;
    private const float minCellFractionOfRadius = 0.125f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScreenBoundsData>();
        state.RequireForUpdate<SimulationConfigData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = math.min(SystemAPI.Time.DeltaTime, maxDeltaTime);

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
        NativeArray<float> velocities = new NativeArray<float>(birdCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeParallelMultiHashMap<int, int> grid = new NativeParallelMultiHashMap<int, int>(birdCount, Allocator.TempJob);

        float searchRadius = math.max(math.max(config.neighborsRadius, config.personalRadius), minRadius);
        float2 worldSize = bounds.max - bounds.min;
        float cellSize = math.clamp(math.sqrt(worldSize.x * worldSize.y * targetBirdsPerCell / birdCount), searchRadius * minCellFractionOfRadius, searchRadius);
        float inverseCellSize = 1f / cellSize;

        JobHandle buildHandle = new BuildGridJob
        {
            positions = positions,
            forwards = forwards,
            velocities = velocities,
            gridWriter = grid.AsParallelWriter(),
            inverseCellSize = inverseCellSize
        }.ScheduleParallel(state.Dependency);

        JobHandle neighborsHandle = new FindNeighborsJob
        {
            positions = positions,
            forwards = forwards,
            velocities = velocities,
            grid = grid,
            cellSize = cellSize,
            inverseCellSize = inverseCellSize,
            neighborsRadiusSq = config.neighborsRadius * config.neighborsRadius,
            searchRadius = searchRadius,
            searchRadiusSq = searchRadius * searchRadius,
            viewCosThreshold = math.cos(math.radians(config.fieldOfViewDegrees * 0.5f)),
            maxNeighbors = maxNeighborCount
        }.ScheduleParallel(buildHandle);

        JobHandle disposeHandle = JobHandle.CombineDependencies(
            positions.Dispose(neighborsHandle),
            forwards.Dispose(neighborsHandle),
            velocities.Dispose(neighborsHandle));

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
            neighborsRadius = math.max(config.neighborsRadius, minRadius),
            personalRadius = math.max(config.personalRadius, minRadius),
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
    public float neighborsRadius;
    public float personalRadius;
    public float turnFactor;
    public float speedDecay;

    private void Execute(ref BirdMovement birdMovement, ref LocalTransform localTransform, in DynamicBuffer<CloseNeighbors> neighbors)
    {
        float3 position = localTransform.Position;
        float3 forward = math.mul(localTransform.Rotation, new float3(0f, 1f, 0f));

        float3 groupHeading = CalculateGroupHeading(forward, neighbors);
        float3 separation = CalculateSeparation(position, personalRadius, neighbors);
        float3 awayFromBorder = CalculateOutOfBounds(position, boundsMin, boundsMax, config.edgeMargin);
        float3 cohesion = CalculateCohesion(position, neighborsRadius, neighbors);
        float3 mouseAttraction = CalculateMouseAttraction(position, mouse, config.mouseRadius);

        float3 desiredDirection = math.normalizesafe(
            forward
            + groupHeading * config.alignmentWeight
            + separation * config.separationWeight
            + awayFromBorder * config.edgeWeight
            + cohesion * config.cohesionWeight
            + mouseAttraction * config.mouseWeight,
            forward);

        float3 newForward = math.normalizesafe(math.lerp(forward, desiredDirection, turnFactor), forward);

        float targetSpeed = CalculateTargetSpeed(position, forward, separation, neighborsRadius, neighbors, config);
        float speed = targetSpeed + (birdMovement.velocity - targetSpeed) * speedDecay;

        float angle = math.atan2(newForward.y, newForward.x) - math.PI * 0.5f;

        birdMovement.velocity = speed;
        localTransform.Rotation = quaternion.RotateZ(angle);
        localTransform.Position += newForward * speed * deltaTime;
    }

    private static float CalculateTargetSpeed(float3 position, float3 forward, float3 separation, float neighborsRadius, in DynamicBuffer<CloseNeighbors> neighbors, in SimulationConfigData config)
    {
        float3 nearbyPositionSum = float3.zero;
        float visibleSpeedSum = 0f;
        int nearbyCount = 0;
        int visibleCount = 0;
        float radiusSq = neighborsRadius * neighborsRadius;

        for (int i = 0; i < neighbors.Length; i++)
        {
            CloseNeighbors neighbor = neighbors[i];

            if (math.distancesq(position, neighbor.position) <= radiusSq)
            {
                nearbyPositionSum += neighbor.position;
                nearbyCount++;
            }

            if (!neighbor.isVisible)
                continue;

            visibleSpeedSum += neighbor.velocity;
            visibleCount++;
        }

        float targetSpeed = config.cruiseSpeed;

        if (visibleCount > 0)
            targetSpeed += config.speedMatchWeight * (visibleSpeedSum / visibleCount - config.cruiseSpeed);

        if (nearbyCount > 0)
        {
            float3 centroidOffset = nearbyPositionSum / nearbyCount - position;

            targetSpeed += config.catchUpGain * math.dot(centroidOffset, forward) / neighborsRadius;
        }

        // brake when crowded ahead, speed up when crowded behind
        targetSpeed += config.separationWeight * math.dot(separation, forward);

        return math.clamp(targetSpeed, config.minSpeed, config.maxSpeed);
    }

    private static float3 CalculateMouseAttraction(float3 position, in MouseInputData mouse, float mouseRadius)
    {
        if (!mouse.isActive || mouseRadius <= 0f)
            return float3.zero;

        float3 offset = new float3(mouse.position, position.z) - position;

        if (math.lengthsq(offset) >= mouseRadius * mouseRadius)
            return float3.zero;

        return offset / mouseRadius;
    }

    private static float3 CalculateGroupHeading(float3 direction, in DynamicBuffer<CloseNeighbors> neighbors)
    {
        float3 groupHeading = direction;

        for (int i = 0; i < neighbors.Length; i++)
        {
            if (!neighbors[i].isVisible)
                continue;

            groupHeading += neighbors[i].forward;
        }

        return math.normalizesafe(groupHeading, direction);
    }

    private static float3 CalculateSeparation(float3 position, float personalRadius, in DynamicBuffer<CloseNeighbors> neighbors)
    {
        float3 separation = float3.zero;

        for (int i = 0; i < neighbors.Length; i++)
        {
            float3 awayFrom = position - neighbors[i].position;
            float distance = math.length(awayFrom);

            if (distance >= personalRadius)
                continue;

            separation += (awayFrom / distance) * (1f - distance / personalRadius);
        }

        return separation;
    }

    private static float3 CalculateOutOfBounds(float3 position, float2 min, float2 max, float margin)
    {
        float3 newDirection = float3.zero;

        if (position.x < min.x + margin)
            newDirection.x = (min.x + margin - position.x) / margin;

        if (position.x > max.x - margin)
            newDirection.x = (max.x - margin - position.x) / margin;

        if (position.y < min.y + margin)
            newDirection.y = (min.y + margin - position.y) / margin;

        if (position.y > max.y - margin)
            newDirection.y = (max.y - margin - position.y) / margin;

        return newDirection;
    }

    private static float3 CalculateCohesion(float3 position, float neighborsRadius, in DynamicBuffer<CloseNeighbors> neighbors)
    {
        float3 neighborsPositionSum = float3.zero;
        int visibleCount = 0;

        for (int i = 0; i < neighbors.Length; i++)
        {
            if (!neighbors[i].isVisible)
                continue;

            neighborsPositionSum += neighbors[i].position;
            visibleCount++;
        }

        if (visibleCount <= 0)
            return float3.zero;

        float3 centroidPosition = neighborsPositionSum / visibleCount;

        return (centroidPosition - position) / neighborsRadius;
    }
}
