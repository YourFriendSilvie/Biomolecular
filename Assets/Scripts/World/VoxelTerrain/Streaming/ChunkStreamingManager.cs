using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns runtime chunk-streaming state and all streaming-order/distance decisions.
/// The orchestrator (ProceduralVoxelTerrain) holds an instance and delegates to it;
/// no back-reference to the facade is held here.
/// </summary>
public sealed class ChunkStreamingManager
{
    private bool startupChunksReady;
    private Vector3Int generationAnchorChunk;

    public bool StartupChunksReady => startupChunksReady;
    public Vector3Int GenerationAnchorChunk => generationAnchorChunk;

    /// <summary>
    /// Call when a new terrain generation begins. Locks in the anchor chunk for the
    /// duration of generation so streaming order stays stable mid-generation.
    /// </summary>
    public void ResetForGeneration(Vector3Int resolvedAnchorChunk)
    {
        startupChunksReady = false;
        generationAnchorChunk = resolvedAnchorChunk;
    }

    /// <summary>
    /// Marks startup chunks as ready for gameplay queries. Returns true only when
    /// runtime streaming mode is active; in non-streaming mode this is a no-op.
    /// </summary>
    public bool TryMarkStartupChunksReady(bool isRuntimeStreamingModeActive)
    {
        if (!isRuntimeStreamingModeActive)
        {
            return false;
        }

        startupChunksReady = true;
        return true;
    }

    /// <summary>
    /// Returns the generation anchor that was locked in for the current generation pass,
    /// or the freshly resolved anchor when not generating.
    /// </summary>
    public Vector3Int GetOrResolveGenerationAnchorChunk(bool isGenerating, Vector3Int resolvedAnchorChunk)
    {
        return isGenerating ? generationAnchorChunk : resolvedAnchorChunk;
    }

    /// <summary>
    /// Builds the ordered list of chunk coordinates for a runtime-streaming generation pass,
    /// sorted nearest-to-farthest from the anchor.
    /// </summary>
    public List<Vector3Int> BuildRuntimeStreamingChunkOrder(
        Vector3Int anchorChunk,
        Vector3Int chunkCounts,
        IReadOnlyDictionary<Vector3Int, ProceduralVoxelTerrainChunk> generatedChunks)
    {
        int countX = Mathf.Max(1, chunkCounts.x);
        int countY = Mathf.Max(1, chunkCounts.y);
        int countZ = Mathf.Max(1, chunkCounts.z);

        List<Vector3Int> chunkCoordinates = new List<Vector3Int>(countX * countY * countZ);
        List<Vector2Int> chunkColumns = new List<Vector2Int>(countX * countZ);
        for (int z = 0; z < countZ; z++)
        {
            for (int x = 0; x < countX; x++)
            {
                chunkColumns.Add(new Vector2Int(x, z));
            }
        }

        chunkColumns.Sort((a, b) =>
        {
            int distCmp = GetHorizontalDistance(a, anchorChunk)
                .CompareTo(GetHorizontalDistance(b, anchorChunk));
            if (distCmp != 0)
            {
                return distCmp;
            }

            int manhattanCmp = GetHorizontalManhattanDistance(a, anchorChunk)
                .CompareTo(GetHorizontalManhattanDistance(b, anchorChunk));
            if (manhattanCmp != 0)
            {
                return manhattanCmp;
            }

            int zCmp = a.y.CompareTo(b.y);
            return zCmp != 0 ? zCmp : a.x.CompareTo(b.x);
        });

        for (int i = 0; i < chunkColumns.Count; i++)
        {
            Vector2Int col = chunkColumns[i];
            for (int y = countY - 1; y >= 0; y--)
            {
                Vector3Int coord = new Vector3Int(col.x, y, col.y);
                if (generatedChunks.ContainsKey(coord))
                {
                    chunkCoordinates.Add(coord);
                }
            }
        }

        return chunkCoordinates;
    }

    /// <summary>
    /// Counts how many of the supplied chunk coordinates fall within the startup
    /// radius. Returns 0 when runtime streaming is not active.
    /// </summary>
    public int CountStartupChunks(
        IReadOnlyList<Vector3Int> coordinates,
        Vector3Int anchorChunk,
        int startupRadius,
        bool isRuntimeStreamingModeActive)
    {
        if (!isRuntimeStreamingModeActive || coordinates == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < coordinates.Count; i++)
        {
            if (GetHorizontalDistance(coordinates[i], anchorChunk) <= startupRadius)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Returns the bounding box covering the startup chunk radius around the anchor.
    /// </summary>
    public bool TryGetStartupBounds(
        bool hasStartupAreaReady,
        bool hasGeneratedTerrain,
        Vector3Int anchorChunk,
        int startupRadius,
        Vector3Int chunkCounts,
        Func<Vector3Int, Bounds> getChunkWorldBounds,
        out Bounds bounds)
    {
        bounds = default;
        if (!hasStartupAreaReady && !hasGeneratedTerrain)
        {
            return false;
        }

        int maxChunkX = Mathf.Max(0, chunkCounts.x - 1);
        int maxChunkY = Mathf.Max(0, chunkCounts.y - 1);
        int maxChunkZ = Mathf.Max(0, chunkCounts.z - 1);

        int minChunkX = Mathf.Clamp(anchorChunk.x - startupRadius, 0, maxChunkX);
        int maxChunkClampedX = Mathf.Clamp(anchorChunk.x + startupRadius, 0, maxChunkX);
        int minChunkZ = Mathf.Clamp(anchorChunk.z - startupRadius, 0, maxChunkZ);
        int maxChunkClampedZ = Mathf.Clamp(anchorChunk.z + startupRadius, 0, maxChunkZ);

        Bounds startupBounds = getChunkWorldBounds(new Vector3Int(minChunkX, 0, minChunkZ));
        Bounds oppositeBounds = getChunkWorldBounds(new Vector3Int(maxChunkClampedX, maxChunkY, maxChunkClampedZ));
        startupBounds.Encapsulate(oppositeBounds.min);
        startupBounds.Encapsulate(oppositeBounds.max);
        bounds = startupBounds;
        return true;
    }

    // -------------------------------------------------------------------------
    // Static distance helpers
    // -------------------------------------------------------------------------

    public static int GetHorizontalDistance(Vector2Int column, Vector3Int anchor)
    {
        return Mathf.Max(Mathf.Abs(column.x - anchor.x), Mathf.Abs(column.y - anchor.z));
    }

    public static int GetHorizontalDistance(Vector3Int coord, Vector3Int anchor)
    {
        return Mathf.Max(Mathf.Abs(coord.x - anchor.x), Mathf.Abs(coord.z - anchor.z));
    }

    public static int GetHorizontalManhattanDistance(Vector2Int column, Vector3Int anchor)
    {
        return Mathf.Abs(column.x - anchor.x) + Mathf.Abs(column.y - anchor.z);
    }
}
