using UnityEngine;

internal static class VoxelTerrainSpatialUtilities
{
    public static Vector3Int WorldPositionToChunkCoordinate(Vector3 localPosition, Vector3 chunkWorldSize, Vector3Int chunkCounts)
    {
        float chunkWorldSizeX = Mathf.Max(0.0001f, chunkWorldSize.x);
        float chunkWorldSizeY = Mathf.Max(0.0001f, chunkWorldSize.y);
        float chunkWorldSizeZ = Mathf.Max(0.0001f, chunkWorldSize.z);

        return new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(localPosition.x / chunkWorldSizeX), 0, Mathf.Max(0, chunkCounts.x - 1)),
            Mathf.Clamp(Mathf.FloorToInt(localPosition.y / chunkWorldSizeY), 0, Mathf.Max(0, chunkCounts.y - 1)),
            Mathf.Clamp(Mathf.FloorToInt(localPosition.z / chunkWorldSizeZ), 0, Mathf.Max(0, chunkCounts.z - 1)));
    }

    public static void GetChunkCoordinateRange(
        Bounds worldBounds,
        Transform transform,
        Vector3 chunkWorldSize,
        Vector3Int chunkCounts,
        out Vector3Int minChunkCoordinate,
        out Vector3Int maxChunkCoordinate)
    {
        Vector3 localMin = transform.InverseTransformPoint(worldBounds.min);
        Vector3 localMax = transform.InverseTransformPoint(worldBounds.max);
        Vector3 min = Vector3.Min(localMin, localMax);
        Vector3 max = Vector3.Max(localMin, localMax);

        minChunkCoordinate = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(min.x / chunkWorldSize.x), 0, chunkCounts.x - 1),
            Mathf.Clamp(Mathf.FloorToInt(min.y / chunkWorldSize.y), 0, chunkCounts.y - 1),
            Mathf.Clamp(Mathf.FloorToInt(min.z / chunkWorldSize.z), 0, chunkCounts.z - 1));

        maxChunkCoordinate = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt(max.x / chunkWorldSize.x) - 1, 0, chunkCounts.x - 1),
            Mathf.Clamp(Mathf.CeilToInt(max.y / chunkWorldSize.y) - 1, 0, chunkCounts.y - 1),
            Mathf.Clamp(Mathf.CeilToInt(max.z / chunkWorldSize.z) - 1, 0, chunkCounts.z - 1));
    }

    public static Bounds GetChunkWorldBounds(Vector3Int chunkCoordinate, Vector3 chunkWorldSize, Transform transform)
    {
        Vector3 localMin = Vector3.Scale(chunkCoordinate, chunkWorldSize);
        Vector3 localCenter = localMin + (chunkWorldSize * 0.5f);
        return new Bounds(transform.TransformPoint(localCenter), chunkWorldSize);
    }

    public static Vector3Int GetDir(int index)
    {
        switch (index)
        {
            case 0: return new Vector3Int(-1, 0, 0);
            case 1: return new Vector3Int(1, 0, 0);
            case 2: return new Vector3Int(0, -1, 0);
            case 3: return new Vector3Int(0, 1, 0);
            case 4: return new Vector3Int(0, 0, -1);
            case 5: return new Vector3Int(0, 0, 1);
            default: return Vector3Int.zero;
        }
    }
}
