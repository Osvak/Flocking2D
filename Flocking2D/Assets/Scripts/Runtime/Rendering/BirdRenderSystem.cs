using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class BirdRenderSystem : SystemBase
{
    private const int MaxBatchSize = 1023;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private EntityQuery birdQuery;
    private BirdRenderSettings settings;
    private MaterialPropertyBlock propertyBlock;

    private NativeArray<float4x4> matrices;
    private NativeArray<Vector4> colors;
    private Vector4[] colorBatch;
    private int capacity;

    protected override void OnCreate()
    {
        RequireForUpdate<SimulationConfigData>();
        RequireForUpdate<ScreenBoundsData>();

        birdQuery = GetEntityQuery(
            ComponentType.ReadOnly<BirdData>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<BirdMovement>());

        propertyBlock = new MaterialPropertyBlock();
        colorBatch = new Vector4[MaxBatchSize];
    }

    protected override void OnDestroy()
    {
        if (matrices.IsCreated)
            matrices.Dispose();

        if (colors.IsCreated)
            colors.Dispose();
    }

    protected override void OnUpdate()
    {
        if (settings == null)
            settings = Object.FindAnyObjectByType<BirdRenderSettings>();

        if (settings == null || settings.mesh == null || settings.material == null)
            return;

        int birdCount = birdQuery.CalculateEntityCount();

        if (birdCount <= 0)
            return;

        EnsureCapacity(birdCount);

        SimulationConfigData config = SystemAPI.GetSingleton<SimulationConfigData>();

        Dependency = new BuildInstancesJob
        {
            matrices = matrices,
            colors = colors,
            cruiseSpeed = config.cruiseSpeed,
            inverseColorSpan = 0.5f / settings.colorSpeedRange,
            slowColor = ToShaderColor(settings.slowColor),
            fastColor = ToShaderColor(settings.fastColor)
        }.ScheduleParallel(birdQuery, Dependency);

        Dependency.Complete();

        Draw(birdCount, SystemAPI.GetSingleton<ScreenBoundsData>());
    }

    private void Draw(int birdCount, ScreenBoundsData screenBounds)
    {
        float2 center = (screenBounds.min + screenBounds.max) * 0.5f;
        float2 size = (screenBounds.max - screenBounds.min) * 2f;

        RenderParams renderParams = new RenderParams(settings.material)
        {
            matProps = propertyBlock,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            worldBounds = new Bounds(new Vector3(center.x, center.y, 0f), new Vector3(size.x, size.y, 10f))
        };

        NativeArray<Matrix4x4> instanceMatrices = matrices.Reinterpret<Matrix4x4>();

        for (int start = 0; start < birdCount; start += MaxBatchSize)
        {
            int batchCount = math.min(MaxBatchSize, birdCount - start);

            NativeArray<Vector4>.Copy(colors, start, colorBatch, 0, batchCount);
            propertyBlock.SetVectorArray(BaseColorId, colorBatch);

            Graphics.RenderMeshInstanced(renderParams, settings.mesh, 0, instanceMatrices, batchCount, start);
        }
    }

    private void EnsureCapacity(int birdCount)
    {
        if (birdCount <= capacity)
            return;

        if (matrices.IsCreated)
            matrices.Dispose();

        if (colors.IsCreated)
            colors.Dispose();

        capacity = math.max(birdCount, capacity * 2);

        matrices = new NativeArray<float4x4>(capacity, Allocator.Persistent);
        colors = new NativeArray<Vector4>(capacity, Allocator.Persistent);
    }

    private static float4 ToShaderColor(Color color)
    {
        Color converted = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;

        return new float4(converted.r, converted.g, converted.b, converted.a);
    }
}

[BurstCompile]
partial struct BuildInstancesJob : IJobEntity
{
    [NativeDisableParallelForRestriction] public NativeArray<float4x4> matrices;
    [NativeDisableParallelForRestriction] public NativeArray<Vector4> colors;

    public float cruiseSpeed;
    public float inverseColorSpan;
    public float4 slowColor;
    public float4 fastColor;

    void Execute([EntityIndexInQuery] int index, in LocalTransform localTransform, in BirdMovement birdMovement)
    {
        float t = math.saturate(0.5f + (birdMovement.speed - cruiseSpeed) * inverseColorSpan);
        float4 color = math.lerp(slowColor, fastColor, t);

        matrices[index] = localTransform.ToMatrix();
        colors[index] = new Vector4(color.x, color.y, color.z, color.w);
    }
}
