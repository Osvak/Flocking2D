using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public static class BirdGrid
{
    private const int CellOffset = 32768;

    public static int2 GetCell(float3 position, float inverseCellSize)
    {
        return (int2)math.floor(position.xy * inverseCellSize);
    }

    public static int GetKey(int2 cell)
    {
        int2 shifted = math.clamp(cell, -CellOffset, CellOffset - 1) + CellOffset;

        return shifted.x | (shifted.y << 16);
    }
}

[BurstCompile]
[WithAll(typeof(BirdData), typeof(CloseNeighbors))]
public partial struct BuildGridJob : IJobEntity
{
    [NativeDisableParallelForRestriction] public NativeArray<float3> positions;
    [NativeDisableParallelForRestriction] public NativeArray<float3> forwards;
    [NativeDisableParallelForRestriction] public NativeArray<float> speeds;
    public NativeParallelMultiHashMap<int, int>.ParallelWriter gridWriter;
    public float inverseCellSize;

    private void Execute(in LocalTransform localTransform, in BirdMovement birdMovement, [EntityIndexInQuery] int entityIndex)
    {
        positions[entityIndex] = localTransform.Position;
        forwards[entityIndex] = math.mul(localTransform.Rotation, new float3(0f, 1f, 0f));
        speeds[entityIndex] = birdMovement.speed;

        int key = BirdGrid.GetKey(BirdGrid.GetCell(localTransform.Position, inverseCellSize));

        gridWriter.Add(key, entityIndex);
    }
}

[BurstCompile]
[WithAll(typeof(BirdData))]
public partial struct FindNeighborsJob : IJobEntity
{
    [ReadOnly] public NativeArray<float3> positions;
    [ReadOnly] public NativeArray<float3> forwards;
    [ReadOnly] public NativeArray<float> speeds;
    [ReadOnly] public NativeParallelMultiHashMap<int, int> grid;
    public float cellSize;
    public float inverseCellSize;
    public float neighborRadiusSq;
    public float searchRadius;
    public float searchRadiusSq;
    public float viewCosThreshold;
    public int maxNeighbors;

    private void Execute(in LocalTransform localTransform, DynamicBuffer<CloseNeighbors> neighbors)
    {
        neighbors.Clear();

        float3 position = localTransform.Position;
        float3 forward = math.mul(localTransform.Rotation, new float3(0f, 1f, 0f));
        int2 cell = BirdGrid.GetCell(position, inverseCellSize);

        float2 positionInCell = position.xy * inverseCellSize - cell;
        float distanceToCellEdge = math.cmin(math.min(positionInCell, 1f - positionInCell));

        FixedList128Bytes<float> distancesSq = default;
        int worstIndex = -1;
        float worstDistanceSq = 0f;

        for (int ring = 0; ; ring++)
        {
            for (int y = -ring; y <= ring; y++)
            {
                int step = math.abs(y) == ring ? 1 : 2 * ring;

                for (int x = -ring; x <= ring; x += step)
                {
                    ScanCell(BirdGrid.GetKey(cell + new int2(x, y)), position, forward, neighbors, ref distancesSq, ref worstIndex, ref worstDistanceSq);
                }
            }

            // birds closer than this are already scanned
            float scannedRadius = (ring + distanceToCellEdge) * cellSize;

            if (scannedRadius >= searchRadius)
                break;

            if (neighbors.Length >= maxNeighbors && worstDistanceSq <= scannedRadius * scannedRadius)
                break;
        }
    }

    private void ScanCell(int key, float3 position, float3 forward, DynamicBuffer<CloseNeighbors> neighbors, ref FixedList128Bytes<float> distancesSq, ref int worstIndex, ref float worstDistanceSq)
    {
        if (!grid.TryGetFirstValue(key, out int index, out NativeParallelMultiHashMapIterator<int> iterator))
            return;

        do
        {
            float3 offset = positions[index] - position;
            float distanceSq = math.lengthsq(offset);

            if (distanceSq > searchRadiusSq || distanceSq < 1e-8f)
                continue;

            bool isFull = neighbors.Length >= maxNeighbors;

            if (isFull && distanceSq >= worstDistanceSq)
                continue;

            CloseNeighbors neighbor = new CloseNeighbors
            {
                position = positions[index],
                forward = forwards[index],
                speed = speeds[index],
                isVisible = distanceSq <= neighborRadiusSq
                    && math.dot(forward, offset) / math.sqrt(distanceSq) >= viewCosThreshold
            };

            if (!isFull)
            {
                neighbors.Add(neighbor);
                distancesSq.Add(distanceSq);

                if (distanceSq > worstDistanceSq)
                {
                    worstDistanceSq = distanceSq;
                    worstIndex = neighbors.Length - 1;
                }
            }
            else
            {
                neighbors[worstIndex] = neighbor;
                distancesSq[worstIndex] = distanceSq;

                worstDistanceSq = 0f;

                for (int i = 0; i < distancesSq.Length; i++)
                {
                    if (distancesSq[i] > worstDistanceSq)
                    {
                        worstDistanceSq = distancesSq[i];
                        worstIndex = i;
                    }
                }
            }
        }
        while (grid.TryGetNextValue(out index, ref iterator));
    }
}
