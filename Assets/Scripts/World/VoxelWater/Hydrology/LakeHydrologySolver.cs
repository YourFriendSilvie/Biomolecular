using System;
using System.Collections.Generic;
using static WaterMathUtility;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Handles lake surface solving, flood-fill, overflow expansion, and terrain patch building.
/// Stateless between calls except for configuration values injected at construction.
/// </summary>
internal sealed class LakeHydrologySolver
{
    internal const float MinimumRenderableLakeVolumeCubicMeters = 0.001f;
    private const int MaxLakeOverflowExpansionPasses = 6;

    private readonly float waterSurfaceThicknessMeters;
    private readonly float lakeDepthMeters;
    private readonly float lakeDynamicExpansionMeters;
    private readonly float waterUpdatePaddingMeters;
    private readonly Action<string> debugLogger;

    internal LakeHydrologySolver(
        float waterSurfaceThicknessMeters,
        float lakeDepthMeters,
        float lakeDynamicExpansionMeters,
        float waterUpdatePaddingMeters,
        Action<string> debugLogger = null)
    {
        this.waterSurfaceThicknessMeters = waterSurfaceThicknessMeters;
        this.lakeDepthMeters = lakeDepthMeters;
        this.lakeDynamicExpansionMeters = lakeDynamicExpansionMeters;
        this.waterUpdatePaddingMeters = waterUpdatePaddingMeters;
        this.debugLogger = debugLogger;
    }

    // =========================================================================
    // Main entry points (called from ProceduralVoxelTerrainWaterSystem)
    // =========================================================================

    internal bool TryEvaluateLakeAtFixedSurfaceWithOverflowExpansion(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        float surfaceY,
        Bounds? expansionBounds,
        out LakeTerrainPatch terrainPatch,
        out LakeSolveResult solveResult,
        float? maxCaptureRadiusOverride = null)
    {
        terrainPatch = null;
        solveResult = null;
        if (terrain == null || lake == null)
        {
            return false;
        }

        float maxCaptureRadius = maxCaptureRadiusOverride.HasValue
            ? Mathf.Max(lake.captureRadius, maxCaptureRadiusOverride.Value)
            : GetMaxPossibleLakeCaptureRadius(terrain, lake.center);
        for (int expansionPass = 0; expansionPass < MaxLakeOverflowExpansionPasses; expansionPass++)
        {
            if (!TryBuildLakeTerrainPatch(terrain, lake, out terrainPatch, expansionBounds) ||
                !TryEvaluateLakeAtSurface(terrain, terrainPatch, lake.center, surfaceY, out solveResult))
            {
                return false;
            }

            if (!solveResult.touchesOpenBoundary)
            {
                return true;
            }

            if (lake.captureRadius >= maxCaptureRadius - Mathf.Max(terrain.VoxelSizeMeters * 0.5f, 0.25f))
            {
                return true;
            }

            float expandedCaptureRadius = ComputeOverflowExpandedCaptureRadius(terrain, lake, terrainPatch, solveResult);
            if (expandedCaptureRadius <= lake.captureRadius + Mathf.Max(terrain.VoxelSizeMeters * 0.5f, 0.25f))
            {
                return true;
            }

            float previousCaptureRadius = lake.captureRadius;
            lake.captureRadius = Mathf.Min(maxCaptureRadius, expandedCaptureRadius);
            if (lake.captureRadius <= previousCaptureRadius + 0.001f)
            {
                return true;
            }

            Log($"Expanded overflow capture for {DescribeLake(lake)} from {previousCaptureRadius:F2}m to {lake.captureRadius:F2}m at fixed surface {surfaceY:F3}.");
        }

        return terrainPatch != null && solveResult != null;
    }

    internal bool TrySolveLakeForTargetVolumeWithOverflowExpansion(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        float targetVolumeCubicMeters,
        Bounds? expansionBounds,
        out LakeTerrainPatch terrainPatch,
        out LakeSolveResult solveResult)
    {
        terrainPatch = null;
        solveResult = null;
        if (terrain == null || lake == null)
        {
            return false;
        }

        float maxCaptureRadius = GetMaxPossibleLakeCaptureRadius(terrain, lake.center);
        for (int expansionPass = 0; expansionPass < MaxLakeOverflowExpansionPasses; expansionPass++)
        {
            if (!TryBuildLakeTerrainPatch(terrain, lake, out terrainPatch, expansionBounds) ||
                !TrySolveLakeSurfaceForTargetVolume(terrain, terrainPatch, lake, targetVolumeCubicMeters, out solveResult))
            {
                return false;
            }

            if (!solveResult.touchesOpenBoundary)
            {
                return true;
            }

            float expandedCaptureRadius = ComputeOverflowExpandedCaptureRadius(terrain, lake, terrainPatch, solveResult);
            if (expandedCaptureRadius <= lake.captureRadius + Mathf.Max(terrain.VoxelSizeMeters * 0.5f, 0.25f))
            {
                return true;
            }

            float previousCaptureRadius = lake.captureRadius;
            lake.captureRadius = Mathf.Min(maxCaptureRadius, expandedCaptureRadius);
            if (lake.captureRadius <= previousCaptureRadius + 0.001f)
            {
                return true;
            }

            Log($"Expanded overflow capture for {DescribeLake(lake)} from {previousCaptureRadius:F2}m to {lake.captureRadius:F2}m while solving target volume {targetVolumeCubicMeters:F3} m^3.");
        }

        return terrainPatch != null && solveResult != null;
    }

    internal bool TrySolveLakeSurfaceForTargetVolume(
        ProceduralVoxelTerrain terrain,
        LakeTerrainPatch terrainPatch,
        GeneratedLake lake,
        float targetVolume,
        out LakeSolveResult resolvedResult)
    {
        resolvedResult = null;
        if (terrainPatch == null || lake == null || terrainPatch.triangles.Count == 0)
        {
            return false;
        }

        float sampleStep = terrain != null ? Mathf.Max(terrain.VoxelSizeMeters, 0.25f) : 0.25f;
        targetVolume = Mathf.Max(0f, targetVolume);
        if (targetVolume <= MinimumRenderableLakeVolumeCubicMeters)
        {
            resolvedResult = CreateEmptyLakeSolveResult(terrain, terrainPatch, lake.center, terrainPatch.minHeight);
            return true;
        }

        float quickAcceptVolumeTolerance = Mathf.Max(0.02f, sampleStep * sampleStep * 0.05f);
        if (TryEvaluateLakeAtSurface(terrain, terrainPatch, lake.center, lake.surfaceY, out LakeSolveResult currentSurfaceResult) &&
            !currentSurfaceResult.touchesOpenBoundary &&
            Mathf.Abs(currentSurfaceResult.volumeCubicMeters - targetVolume) <= quickAcceptVolumeTolerance)
        {
            resolvedResult = currentSurfaceResult;
            return true;
        }

        float lowerBound = terrainPatch.minHeight - sampleStep;
        float upperBound = Mathf.Max(
            lake.surfaceY + sampleStep,
            terrainPatch.maxHeight + Mathf.Max(lakeDepthMeters, sampleStep * 2f));
        float absoluteUpperBound = terrain != null
            ? terrain.WorldBounds.max.y - Mathf.Max(terrain.VoxelSizeMeters * 0.5f, 0.25f)
            : upperBound;
        upperBound = Mathf.Min(upperBound, absoluteUpperBound);
        if (upperBound <= lowerBound + 0.05f)
        {
            upperBound = Mathf.Min(absoluteUpperBound, lowerBound + sampleStep);
        }

        if (!TryEvaluateLakeAtSurface(terrain, terrainPatch, lake.center, lowerBound, out LakeSolveResult lowResult) ||
            !TryEvaluateLakeAtSurface(terrain, terrainPatch, lake.center, upperBound, out LakeSolveResult highResult))
        {
            return false;
        }

        float volumeTolerance = Mathf.Max(0.001f, sampleStep * sampleStep * 0.005f);
        int expansionCount = 0;
        while (highResult.volumeCubicMeters < targetVolume - volumeTolerance &&
               upperBound < absoluteUpperBound - 0.01f &&
               expansionCount < 8)
        {
            upperBound = Mathf.Min(absoluteUpperBound, upperBound + Mathf.Max(lakeDepthMeters, sampleStep * 2f));
            if (!TryEvaluateLakeAtSurface(terrain, terrainPatch, lake.center, upperBound, out highResult))
            {
                return false;
            }

            expansionCount++;
        }

        if (highResult.volumeCubicMeters <= targetVolume + volumeTolerance)
        {
            resolvedResult = highResult;
            return true;
        }

        if (lowResult.volumeCubicMeters >= targetVolume - volumeTolerance)
        {
            resolvedResult = lowResult;
            return true;
        }

        float low = lowerBound;
        float high = upperBound;
        for (int iteration = 0; iteration < 14; iteration++)
        {
            float mid = Mathf.Lerp(low, high, 0.5f);
            if (!TryEvaluateLakeAtSurface(terrain, terrainPatch, lake.center, mid, out LakeSolveResult midResult))
            {
                return false;
            }

            if (midResult.volumeCubicMeters >= targetVolume)
            {
                high = mid;
                highResult = midResult;
            }
            else
            {
                low = mid;
                lowResult = midResult;
            }
        }

        if (highResult.volumeCubicMeters <= MinimumRenderableLakeVolumeCubicMeters)
        {
            resolvedResult = lowResult;
        }
        else if (lowResult.volumeCubicMeters <= MinimumRenderableLakeVolumeCubicMeters)
        {
            resolvedResult = highResult;
        }
        else
        {
            float lowDelta = Mathf.Abs(lowResult.volumeCubicMeters - targetVolume);
            float highDelta = Mathf.Abs(highResult.volumeCubicMeters - targetVolume);
            resolvedResult = lowDelta <= highDelta ? lowResult : highResult;
        }

        return true;
    }

    internal bool TryBuildLakeTerrainPatch(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        out LakeTerrainPatch terrainPatch,
        Bounds? expansionBounds = null,
        float? captureRadiusOverride = null)
    {
        terrainPatch = null;
        float effectiveCaptureRadius = captureRadiusOverride.HasValue
            ? captureRadiusOverride.Value
            : (lake != null ? lake.captureRadius : 0f);
        if (terrain == null || lake == null || effectiveCaptureRadius <= 0.1f)
        {
            return false;
        }

        LakeTerrainPatch patch = new LakeTerrainPatch();
        Dictionary<LakeEdgeKey, List<LakePendingEdge>> edgeMap = new Dictionary<LakeEdgeKey, List<LakePendingEdge>>();
        Bounds captureBounds = BuildLakeCaptureBounds(terrain, lake, expansionBounds, effectiveCaptureRadius);
        Vector2 lakeCenterXZ = new Vector2(lake.center.x, lake.center.z);
        float inclusionRadius = effectiveCaptureRadius + Mathf.Max(terrain.VoxelSizeMeters * 1.5f, 1f);
        terrain.GetChunkCoordinateRange(captureBounds, out Vector3Int minChunkCoordinate, out Vector3Int maxChunkCoordinate);
        List<Vector3> vertexBuffer = new List<Vector3>(512);
        List<int> indexBuffer = new List<int>(768);

        // Phase 1: extract all world-space triangle vertices from terrain meshes.
        // Mesh API (GetVertices/GetTriangles) and Transform access must stay on the main thread.
        // The local→world transform is applied here so the filter job only sees pure float data.
        List<Vector3> rawWorldVertices = new List<Vector3>(2048);
        for (int z = minChunkCoordinate.z; z <= maxChunkCoordinate.z; z++)
        {
            for (int y = minChunkCoordinate.y; y <= maxChunkCoordinate.y; y++)
            {
                for (int x = minChunkCoordinate.x; x <= maxChunkCoordinate.x; x++)
                {
                    Vector3Int chunkCoordinate = new Vector3Int(x, y, z);
                    if (!terrain.TryGetGeneratedChunk(chunkCoordinate, out ProceduralVoxelTerrainChunk chunk) ||
                        !terrain.GetChunkWorldBounds(chunkCoordinate).Intersects(captureBounds))
                    {
                        continue;
                    }

                    patch.chunkCoordinates.Add(chunkCoordinate);

                    MeshFilter meshFilter = chunk.GetComponent<MeshFilter>();
                    Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                    if (mesh == null || mesh.vertexCount == 0)
                    {
                        continue;
                    }

                    vertexBuffer.Clear();
                    mesh.GetVertices(vertexBuffer);
                    if (vertexBuffer.Count == 0)
                    {
                        continue;
                    }

                    Matrix4x4 localToWorld = chunk.transform.localToWorldMatrix;
                    for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                    {
                        indexBuffer.Clear();
                        mesh.GetTriangles(indexBuffer, subMeshIndex);
                        for (int i = 0; i + 2 < indexBuffer.Count; i += 3)
                        {
                            rawWorldVertices.Add(localToWorld.MultiplyPoint3x4(vertexBuffer[indexBuffer[i]]));
                            rawWorldVertices.Add(localToWorld.MultiplyPoint3x4(vertexBuffer[indexBuffer[i + 1]]));
                            rawWorldVertices.Add(localToWorld.MultiplyPoint3x4(vertexBuffer[indexBuffer[i + 2]]));
                        }
                    }
                }
            }
        }

        int rawTriangleCount = rawWorldVertices.Count / 3;
        if (rawTriangleCount == 0)
        {
            return false;
        }

        // Phase 2: filter triangles — reject steep-normal faces and those outside
        // the capture radius. Uses a Burst-compiled parallel job above the threshold.
        bool[] accepted = FilterTerrainTriangles(rawWorldVertices, rawTriangleCount, lakeCenterXZ, inclusionRadius);

        // Phase 3: collect accepted triangles into the patch and build the edge adjacency map.
        for (int i = 0; i < rawTriangleCount; i++)
        {
            if (!accepted[i])
            {
                continue;
            }

            Vector3 worldA = rawWorldVertices[i * 3];
            Vector3 worldB = rawWorldVertices[i * 3 + 1];
            Vector3 worldC = rawWorldVertices[i * 3 + 2];
            int triangleIndex = patch.triangles.Count;
            patch.triangles.Add(new LakeTerrainTriangle
            {
                a = worldA,
                b = worldB,
                c = worldC
            });

            patch.minHeight = Mathf.Min(patch.minHeight, Mathf.Min(worldA.y, Mathf.Min(worldB.y, worldC.y)));
            patch.maxHeight = Mathf.Max(patch.maxHeight, Mathf.Max(worldA.y, Mathf.Max(worldB.y, worldC.y)));
            Bounds triangleBounds = new Bounds(worldA, Vector3.zero);
            triangleBounds.Encapsulate(worldB);
            triangleBounds.Encapsulate(worldC);
            if (!patch.hasBounds)
            {
                patch.bounds = triangleBounds;
                patch.hasBounds = true;
            }
            else
            {
                patch.bounds.Encapsulate(triangleBounds.min);
                patch.bounds.Encapsulate(triangleBounds.max);
            }

            AddLakeTerrainTriangleEdge(edgeMap, triangleIndex, worldA, worldB);
            AddLakeTerrainTriangleEdge(edgeMap, triangleIndex, worldB, worldC);
            AddLakeTerrainTriangleEdge(edgeMap, triangleIndex, worldC, worldA);
        }

        if (patch.triangles.Count == 0 || float.IsPositiveInfinity(patch.minHeight) || float.IsNegativeInfinity(patch.maxHeight))
        {
            return false;
        }

        foreach (KeyValuePair<LakeEdgeKey, List<LakePendingEdge>> edgeEntry in edgeMap)
        {
            List<LakePendingEdge> sharedEdges = edgeEntry.Value;
            if (sharedEdges == null || sharedEdges.Count == 0)
            {
                continue;
            }

            if (sharedEdges.Count == 1)
            {
                LakePendingEdge boundaryEdge = sharedEdges[0];
                patch.triangles[boundaryEdge.triangleIndex].boundaryEdges.Add(new LakeTriangleEdge(boundaryEdge.edgeA, boundaryEdge.edgeB));
                continue;
            }

            for (int i = 0; i < sharedEdges.Count; i++)
            {
                for (int j = i + 1; j < sharedEdges.Count; j++)
                {
                    LakePendingEdge first = sharedEdges[i];
                    LakePendingEdge second = sharedEdges[j];
                    patch.triangles[first.triangleIndex].neighbors.Add(new LakeTriangleNeighbor(second.triangleIndex, first.edgeA, first.edgeB));
                    patch.triangles[second.triangleIndex].neighbors.Add(new LakeTriangleNeighbor(first.triangleIndex, first.edgeA, first.edgeB));
                }
            }
        }

        terrainPatch = patch;
        return true;
    }

    internal void ApplyLakeSolveResult(GeneratedLake lake, LakeSolveResult solveResult)
    {
        if (lake == null || solveResult == null)
        {
            return;
        }

        lake.surfaceY = solveResult.surfaceY;
        lake.storedVolumeCubicMeters = Mathf.Max(0f, solveResult.volumeCubicMeters);
        lake.gridCellSize = 0f;
        lake.gridCountPerAxis = 0;
        lake.gridOriginXZ = Vector2.zero;
        lake.cellHeights = Array.Empty<float>();
        lake.floodedCells = Array.Empty<bool>();
        lake.surfaceVertices = solveResult.surfaceVertices ?? Array.Empty<Vector3>();
        lake.surfaceTriangles = solveResult.surfaceTriangles ?? Array.Empty<int>();
        lake.surfaceBounds = solveResult.surfaceBounds;
        lake.floodedCellCount = Mathf.Max(0, solveResult.floodedCellCount);
        lake.shorelineRadii = Array.Empty<float>();
    }

    internal static float ClampLakeStoredVolume(float targetVolumeCubicMeters, LakeSolveResult solveResult)
    {
        if (solveResult == null || solveResult.volumeCubicMeters <= MinimumRenderableLakeVolumeCubicMeters)
        {
            return 0f;
        }

        return Mathf.Min(
            Mathf.Max(0f, targetVolumeCubicMeters),
            Mathf.Max(0f, solveResult.volumeCubicMeters));
    }

    internal bool TrySolveExistingLakeForStoredVolumeFast(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        float targetVolume,
        out LakeTerrainPatch terrainPatch,
        out LakeSolveResult solveResult)
    {
        terrainPatch = lake != null ? lake.terrainPatch : null;
        solveResult = null;
        if (terrain == null || lake == null)
        {
            return false;
        }

        if (terrainPatch == null && !TryBuildLakeTerrainPatch(terrain, lake, out terrainPatch))
        {
            return false;
        }

        if (terrainPatch == null)
        {
            return false;
        }

        if (targetVolume <= MinimumRenderableLakeVolumeCubicMeters)
        {
            solveResult = CreateEmptyLakeSolveResult(terrain, terrainPatch, lake.center, terrainPatch.minHeight);
            return true;
        }

        if (TrySolveLakeSurfaceForTargetVolumeFromSurfaceEstimate(terrain, terrainPatch, lake, targetVolume, out solveResult))
        {
            return true;
        }

        return TrySolveLakeSurfaceForTargetVolume(terrain, terrainPatch, lake, targetVolume, out solveResult);
    }

    internal bool TrySolveLakeForStoredVolume(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        float targetVolume,
        out LakeTerrainPatch terrainPatch,
        out LakeSolveResult solveResult)
    {
        terrainPatch = lake != null ? lake.terrainPatch : null;
        solveResult = null;
        if (terrain == null || lake == null)
        {
            return false;
        }

        if (terrainPatch == null && !TryBuildLakeTerrainPatch(terrain, lake, out terrainPatch))
        {
            return false;
        }

        if (terrainPatch == null)
        {
            return false;
        }

        if (targetVolume <= MinimumRenderableLakeVolumeCubicMeters)
        {
            solveResult = CreateEmptyLakeSolveResult(terrain, terrainPatch, lake.center, terrainPatch.minHeight);
            return true;
        }

        return TrySolveLakeForTargetVolumeWithOverflowExpansion(
            terrain,
            lake,
            targetVolume,
            null,
            out terrainPatch,
            out solveResult);
    }

    internal Bounds BuildLakeHorizontalBounds(ProceduralVoxelTerrain terrain, Vector3 center, float radius, float horizontalPadding)
    {
        return new Bounds(
            new Vector3(center.x, terrain.WorldBounds.center.y, center.z),
            new Vector3(
                Mathf.Max(terrain.VoxelSizeMeters, (radius + horizontalPadding) * 2f),
                terrain.WorldSize.y,
                Mathf.Max(terrain.VoxelSizeMeters, (radius + horizontalPadding) * 2f)));
    }

    internal bool TryBuildLakeSurfaceHorizontalBounds(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        float horizontalPadding,
        out Bounds bounds)
    {
        bounds = default;
        if (terrain == null || !HasSurfaceGeometry(lake))
        {
            return false;
        }

        bounds = new Bounds(
            new Vector3(lake.surfaceBounds.center.x, terrain.WorldBounds.center.y, lake.surfaceBounds.center.z),
            new Vector3(
                Mathf.Max(terrain.VoxelSizeMeters, lake.surfaceBounds.size.x + (horizontalPadding * 2f)),
                terrain.WorldSize.y,
                Mathf.Max(terrain.VoxelSizeMeters, lake.surfaceBounds.size.z + (horizontalPadding * 2f))));
        return true;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private bool TryEvaluateLakeAtSurface(
        ProceduralVoxelTerrain terrain,
        LakeTerrainPatch terrainPatch,
        Vector3 lakeCenter,
        float surfaceY,
        out LakeSolveResult result)
    {
        result = null;
        if (terrainPatch == null || terrainPatch.triangles.Count == 0)
        {
            return false;
        }

        result = CreateEmptyLakeSolveResult(terrain, terrainPatch, lakeCenter, surfaceY);
        int seedIndex = FindLakeSeedTriangleIndex(terrainPatch, lakeCenter, surfaceY);
        if (seedIndex < 0)
        {
            return true;
        }

        bool[] visitedTriangles = new bool[terrainPatch.triangles.Count];
        if (!TryEvaluateLakeSurfaceComponentFromSeed(terrain, terrainPatch, surfaceY, seedIndex, visitedTriangles, out LakeSurfaceComponent component))
        {
            return true;
        }

        result = component.solveResult;
        return true;
    }

    private bool TryEvaluateLakeComponentsAtSurface(
        ProceduralVoxelTerrain terrain,
        LakeTerrainPatch terrainPatch,
        float surfaceY,
        out List<LakeSurfaceComponent> components)
    {
        components = null;
        if (terrainPatch == null || terrainPatch.triangles.Count == 0)
        {
            return false;
        }

        components = new List<LakeSurfaceComponent>(4);
        bool[] visitedTriangles = new bool[terrainPatch.triangles.Count];
        List<Vector3> clippedPolygon = new List<Vector3>(4);
        for (int triangleIndex = 0; triangleIndex < terrainPatch.triangles.Count; triangleIndex++)
        {
            if (visitedTriangles[triangleIndex])
            {
                continue;
            }

            LakeTerrainTriangle triangle = terrainPatch.triangles[triangleIndex];
            if (!TryClipTriangleBelowSurface(triangle.a, triangle.b, triangle.c, surfaceY, clippedPolygon))
            {
                visitedTriangles[triangleIndex] = true;
                continue;
            }

            if (TryEvaluateLakeSurfaceComponentFromSeed(terrain, terrainPatch, surfaceY, triangleIndex, visitedTriangles, out LakeSurfaceComponent component))
            {
                components.Add(component);
            }
        }

        return true;
    }

    private bool TryEvaluateLakeSurfaceComponentFromSeed(
        ProceduralVoxelTerrain terrain,
        LakeTerrainPatch terrainPatch,
        float surfaceY,
        int seedIndex,
        bool[] visitedTriangles,
        out LakeSurfaceComponent component)
    {
        component = null;
        if (terrainPatch == null ||
            visitedTriangles == null ||
            seedIndex < 0 ||
            seedIndex >= terrainPatch.triangles.Count)
        {
            return false;
        }

        Queue<int> queue = new Queue<int>(Mathf.Max(16, terrainPatch.triangles.Count / 3));
        List<Vector3> clippedPolygon = new List<Vector3>(4);
        List<Vector3> surfaceVertices = new List<Vector3>(terrainPatch.triangles.Count * 3);
        List<int> surfaceTriangles = new List<int>(terrainPatch.triangles.Count * 3);
        float volumeCubicMeters = 0f;
        int floodedTriangleCount = 0;
        bool touchesOpenBoundary = false;
        bool hasBounds = false;
        bool hasRepresentativePoint = false;
        Bounds surfaceBounds = default;
        Vector3 representativePoint = default;

        visitedTriangles[seedIndex] = true;
        queue.Enqueue(seedIndex);
        while (queue.Count > 0)
        {
            int triangleIndex = queue.Dequeue();
            LakeTerrainTriangle triangle = terrainPatch.triangles[triangleIndex];
            if (!TryClipTriangleBelowSurface(triangle.a, triangle.b, triangle.c, surfaceY, clippedPolygon))
            {
                continue;
            }

            if (!hasRepresentativePoint && clippedPolygon.Count > 0)
            {
                representativePoint = new Vector3(clippedPolygon[0].x, surfaceY, clippedPolygon[0].z);
                hasRepresentativePoint = true;
            }

            if (!TryAppendClippedLakePolygon(surfaceY, clippedPolygon, surfaceVertices, surfaceTriangles, ref volumeCubicMeters, ref surfaceBounds, ref hasBounds))
            {
                continue;
            }

            floodedTriangleCount++;
            for (int boundaryEdgeIndex = 0; boundaryEdgeIndex < triangle.boundaryEdges.Count; boundaryEdgeIndex++)
            {
                LakeTriangleEdge boundaryEdge = triangle.boundaryEdges[boundaryEdgeIndex];
                if (Mathf.Min(boundaryEdge.edgeA.y, boundaryEdge.edgeB.y) < surfaceY - 0.01f)
                {
                    touchesOpenBoundary = true;
                    break;
                }
            }

            for (int neighborIndex = 0; neighborIndex < triangle.neighbors.Count; neighborIndex++)
            {
                LakeTriangleNeighbor neighbor = triangle.neighbors[neighborIndex];
                if (neighbor.triangleIndex < 0 ||
                    neighbor.triangleIndex >= visitedTriangles.Length ||
                    visitedTriangles[neighbor.triangleIndex] ||
                    Mathf.Min(neighbor.edgeA.y, neighbor.edgeB.y) >= surfaceY - 0.01f)
                {
                    continue;
                }

                visitedTriangles[neighbor.triangleIndex] = true;
                queue.Enqueue(neighbor.triangleIndex);
            }
        }

        if (!hasRepresentativePoint ||
            surfaceTriangles.Count == 0 ||
            volumeCubicMeters <= MinimumRenderableLakeVolumeCubicMeters)
        {
            return false;
        }

        LakeSolveResult solveResult = CreateEmptyLakeSolveResult(terrain, terrainPatch, representativePoint, surfaceY);
        solveResult.volumeCubicMeters = volumeCubicMeters;
        solveResult.surfaceVertices = surfaceVertices.ToArray();
        solveResult.surfaceTriangles = surfaceTriangles.ToArray();
        solveResult.surfaceBounds = hasBounds
            ? surfaceBounds
            : new Bounds(representativePoint, new Vector3(0f, Mathf.Max(waterSurfaceThicknessMeters, 0.05f), 0f));
        solveResult.floodedCellCount = floodedTriangleCount;
        solveResult.touchesOpenBoundary = touchesOpenBoundary;

        component = new LakeSurfaceComponent
        {
            representativePoint = representativePoint,
            solveResult = solveResult
        };
        return true;
    }

    private LakeSolveResult CreateEmptyLakeSolveResult(ProceduralVoxelTerrain terrain, LakeTerrainPatch terrainPatch, Vector3 center, float surfaceY)
    {
        float cellSize = terrain != null ? Mathf.Max(terrain.VoxelSizeMeters, 0.25f) : 0.25f;
        return new LakeSolveResult
        {
            surfaceY = surfaceY,
            volumeCubicMeters = 0f,
            cellSize = cellSize,
            cellCountPerAxis = 0,
            originXZ = Vector2.zero,
            cellHeights = Array.Empty<float>(),
            floodedCells = Array.Empty<bool>(),
            surfaceVertices = Array.Empty<Vector3>(),
            surfaceTriangles = Array.Empty<int>(),
            surfaceBounds = new Bounds(
                new Vector3(center.x, surfaceY, center.z),
                new Vector3(0f, Mathf.Max(0.05f, waterSurfaceThicknessMeters), 0f)),
            floodedCellCount = 0,
            touchesOpenBoundary = false
        };
    }

    private float ComputeOverflowExpandedCaptureRadius(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        LakeTerrainPatch terrainPatch,
        LakeSolveResult solveResult)
    {
        float expansionStep = Mathf.Max(
            lakeDynamicExpansionMeters,
            terrain != null ? terrain.ChunkWorldSizeMeters * 0.5f : 4f,
            terrain != null ? terrain.VoxelSizeMeters * 4f : 1f);
        float expandedRadius = lake != null ? lake.captureRadius + expansionStep : expansionStep;
        if (lake != null && terrainPatch != null && terrainPatch.hasBounds)
        {
            expandedRadius = Mathf.Max(expandedRadius, ComputeBoundsCornerRadiusXZ(lake.center, terrainPatch.bounds) + expansionStep);
        }

        if (lake != null && solveResult != null)
        {
            expandedRadius = Mathf.Max(expandedRadius, ComputeBoundsCornerRadiusXZ(lake.center, solveResult.surfaceBounds) + expansionStep);
        }

        return expandedRadius;
    }

    private float GetMaxPossibleLakeCaptureRadius(ProceduralVoxelTerrain terrain, Vector3 center)
    {
        if (terrain == null)
        {
            return float.PositiveInfinity;
        }

        Bounds worldBounds = terrain.WorldBounds;
        Vector2 centerXZ = new Vector2(center.x, center.z);
        Vector2[] corners =
        {
            new Vector2(worldBounds.min.x, worldBounds.min.z),
            new Vector2(worldBounds.min.x, worldBounds.max.z),
            new Vector2(worldBounds.max.x, worldBounds.min.z),
            new Vector2(worldBounds.max.x, worldBounds.max.z)
        };

        float maxRadius = 0f;
        for (int i = 0; i < corners.Length; i++)
        {
            maxRadius = Mathf.Max(maxRadius, Vector2.Distance(centerXZ, corners[i]));
        }

        return maxRadius + Mathf.Max(terrain.VoxelSizeMeters, waterUpdatePaddingMeters);
    }

    private Bounds BuildLakeCaptureBounds(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        Bounds? expansionBounds = null,
        float? captureRadiusOverride = null)
    {
        float verticalPadding = Mathf.Max(terrain.VoxelSizeMeters * 2f, waterUpdatePaddingMeters);
        float captureRadius = captureRadiusOverride.HasValue ? captureRadiusOverride.Value : lake.captureRadius;
        float minY = lake.surfaceY - Mathf.Max(terrain.ChunkWorldSizeMeters, lakeDepthMeters * 2.5f);
        float maxY = lake.surfaceY + Mathf.Max(terrain.ChunkWorldSizeMeters, lakeDepthMeters * 2f);
        if (lake.terrainPatch != null && lake.terrainPatch.hasBounds)
        {
            minY = Mathf.Min(minY, lake.terrainPatch.bounds.min.y - verticalPadding);
            maxY = Mathf.Max(maxY, lake.terrainPatch.bounds.max.y + verticalPadding);
        }

        if (expansionBounds.HasValue)
        {
            Bounds changedBounds = expansionBounds.Value;
            minY = Mathf.Min(minY, changedBounds.min.y - verticalPadding);
            maxY = Mathf.Max(maxY, changedBounds.max.y + verticalPadding);
        }

        Bounds terrainBounds = terrain.WorldBounds;
        minY = Mathf.Clamp(minY, terrainBounds.min.y, terrainBounds.max.y);
        maxY = Mathf.Clamp(maxY, terrainBounds.min.y, terrainBounds.max.y);
        if (maxY <= minY + terrain.VoxelSizeMeters)
        {
            maxY = Mathf.Min(terrainBounds.max.y, minY + Mathf.Max(terrain.VoxelSizeMeters, terrain.ChunkWorldSizeMeters));
        }

        return new Bounds(
            new Vector3(lake.center.x, (minY + maxY) * 0.5f, lake.center.z),
            new Vector3(
                captureRadius * 2f,
                Mathf.Max(terrain.VoxelSizeMeters, maxY - minY),
                captureRadius * 2f));
    }

    private bool TrySolveLakeSurfaceForTargetVolumeFromSurfaceEstimate(
        ProceduralVoxelTerrain terrain,
        LakeTerrainPatch terrainPatch,
        GeneratedLake lake,
        float targetVolume,
        out LakeSolveResult solveResult)
    {
        solveResult = null;
        if (terrainPatch == null ||
            lake == null ||
            terrainPatch.triangles.Count == 0 ||
            !HasSurfaceGeometry(lake))
        {
            return false;
        }

        float currentSurfaceArea = ComputeLakeSurfaceAreaXZ(lake.surfaceVertices, lake.surfaceTriangles);
        if (currentSurfaceArea <= 0.0001f)
        {
            return false;
        }

        float sampleStep = terrain != null ? Mathf.Max(terrain.VoxelSizeMeters, 0.25f) : 0.25f;
        float currentVolume = Mathf.Max(lake.storedVolumeCubicMeters, 0f);
        float volumeTolerance = Mathf.Max(0.01f, sampleStep * sampleStep * 0.01f);
        float minimumSurfaceY = terrainPatch.minHeight - sampleStep;
        float maximumSurfaceY = Mathf.Max(
            lake.surfaceY + sampleStep,
            terrainPatch.maxHeight + Mathf.Max(lakeDepthMeters, sampleStep * 2f));
        if (terrain != null)
        {
            maximumSurfaceY = Mathf.Min(
                maximumSurfaceY,
                terrain.WorldBounds.max.y - Mathf.Max(terrain.VoxelSizeMeters * 0.5f, 0.25f));
        }

        if (maximumSurfaceY <= minimumSurfaceY + 0.05f)
        {
            maximumSurfaceY = minimumSurfaceY + sampleStep;
        }

        float estimatedSurfaceY = Mathf.Clamp(
            lake.surfaceY + ((targetVolume - currentVolume) / currentSurfaceArea),
            minimumSurfaceY,
            maximumSurfaceY);
        if (!TryEvaluateLakeAtSurface(terrain, terrainPatch, lake.center, estimatedSurfaceY, out LakeSolveResult estimatedResult))
        {
            return false;
        }

        if (Mathf.Abs(estimatedResult.volumeCubicMeters - targetVolume) <= volumeTolerance)
        {
            solveResult = estimatedResult;
            return true;
        }

        float previousSurfaceY = lake.surfaceY;
        float previousVolume = currentVolume;
        float currentSurfaceY = estimatedSurfaceY;
        LakeSolveResult currentResult = estimatedResult;
        for (int iteration = 0; iteration < 3; iteration++)
        {
            float denominator = currentResult.volumeCubicMeters - previousVolume;
            if (Mathf.Abs(denominator) <= 0.0001f)
            {
                break;
            }

            float nextSurfaceY = currentSurfaceY + ((targetVolume - currentResult.volumeCubicMeters) * (currentSurfaceY - previousSurfaceY) / denominator);
            nextSurfaceY = Mathf.Clamp(nextSurfaceY, minimumSurfaceY, maximumSurfaceY);
            if (Mathf.Abs(nextSurfaceY - currentSurfaceY) <= 0.0001f)
            {
                break;
            }

            if (!TryEvaluateLakeAtSurface(terrain, terrainPatch, lake.center, nextSurfaceY, out LakeSolveResult nextResult))
            {
                return false;
            }

            if (Mathf.Abs(nextResult.volumeCubicMeters - targetVolume) <= volumeTolerance)
            {
                solveResult = nextResult;
                return true;
            }

            previousSurfaceY = currentSurfaceY;
            previousVolume = currentResult.volumeCubicMeters;
            currentSurfaceY = nextSurfaceY;
            currentResult = nextResult;
        }

        return false;
    }

    private static float ComputeLakeSurfaceAreaXZ(Vector3[] surfaceVertices, int[] surfaceTriangles)
    {
        if (surfaceVertices == null || surfaceTriangles == null || surfaceTriangles.Length < 3)
        {
            return 0f;
        }

        float area = 0f;
        for (int triangleOffset = 0; triangleOffset + 2 < surfaceTriangles.Length; triangleOffset += 3)
        {
            area += TriangleAreaXZ(
                surfaceVertices[surfaceTriangles[triangleOffset]],
                surfaceVertices[surfaceTriangles[triangleOffset + 1]],
                surfaceVertices[surfaceTriangles[triangleOffset + 2]]);
        }

        return area;
    }

    private bool TryAppendClippedLakePolygon(
        float surfaceY,
        List<Vector3> clippedPolygon,
        List<Vector3> surfaceVertices,
        List<int> surfaceTriangles,
        ref float volumeCubicMeters,
        ref Bounds surfaceBounds,
        ref bool hasBounds)
    {
        if (clippedPolygon == null || clippedPolygon.Count < 3)
        {
            return false;
        }

        bool appended = false;
        for (int i = 1; i < clippedPolygon.Count - 1; i++)
        {
            Vector3 terrainA = clippedPolygon[0];
            Vector3 terrainB = clippedPolygon[i];
            Vector3 terrainC = clippedPolygon[i + 1];
            float projectedArea = TriangleAreaXZ(terrainA, terrainB, terrainC);
            if (projectedArea <= 0.0001f)
            {
                continue;
            }

            float depthA = Mathf.Max(0f, surfaceY - terrainA.y);
            float depthB = Mathf.Max(0f, surfaceY - terrainB.y);
            float depthC = Mathf.Max(0f, surfaceY - terrainC.y);
            volumeCubicMeters += projectedArea * ((depthA + depthB + depthC) / 3f);

            Vector3 topA = new Vector3(terrainA.x, surfaceY, terrainA.z);
            Vector3 topB = new Vector3(terrainB.x, surfaceY, terrainB.z);
            Vector3 topC = new Vector3(terrainC.x, surfaceY, terrainC.z);
            int vertexStart = surfaceVertices.Count;
            surfaceVertices.Add(topA);
            surfaceVertices.Add(topB);
            surfaceVertices.Add(topC);
            surfaceTriangles.Add(vertexStart);
            surfaceTriangles.Add(vertexStart + 1);
            surfaceTriangles.Add(vertexStart + 2);

            if (!hasBounds)
            {
                surfaceBounds = new Bounds(topA, Vector3.zero);
                hasBounds = true;
            }

            surfaceBounds.Encapsulate(topA);
            surfaceBounds.Encapsulate(topB);
            surfaceBounds.Encapsulate(topC);
            appended = true;
        }

        return appended;
    }

    private int FindLakeSeedTriangleIndex(LakeTerrainPatch terrainPatch, Vector3 lakeCenter, float surfaceY)
    {
        if (terrainPatch == null || terrainPatch.triangles.Count == 0)
        {
            return -1;
        }

        Vector2 lakeCenterXZ = new Vector2(lakeCenter.x, lakeCenter.z);
        List<Vector3> clippedPolygon = new List<Vector3>(4);
        float bestDistanceSquared = float.PositiveInfinity;
        int bestTriangleIndex = -1;
        for (int triangleIndex = 0; triangleIndex < terrainPatch.triangles.Count; triangleIndex++)
        {
            LakeTerrainTriangle triangle = terrainPatch.triangles[triangleIndex];
            if (!TryClipTriangleBelowSurface(triangle.a, triangle.b, triangle.c, surfaceY, clippedPolygon))
            {
                continue;
            }

            for (int i = 1; i < clippedPolygon.Count - 1; i++)
            {
                Vector2 closestPoint = ClosestPointOnTriangleXZ(lakeCenterXZ, clippedPolygon[0], clippedPolygon[i], clippedPolygon[i + 1]);
                float distanceSquared = (closestPoint - lakeCenterXZ).sqrMagnitude;
                if (distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                bestTriangleIndex = triangleIndex;
                if (distanceSquared <= 0.0001f)
                {
                    return triangleIndex;
                }
            }
        }

        return bestTriangleIndex;
    }

    // Minimum triangle count before the overhead of a Burst job is worthwhile.
    private const int JobTriangleThreshold = 64;

    /// <summary>
    /// Filters <paramref name="rawWorldVertices"/> (3 per triangle, flat) against the
    /// normal-direction and XZ-distance criteria used by
    /// <see cref="TryBuildLakeTerrainPatch"/>.
    ///
    /// Dispatches <see cref="LakeTriangleFilterJob"/> when
    /// <paramref name="triangleCount"/> &gt;= <see cref="JobTriangleThreshold"/>;
    /// otherwise falls back to an equivalent sequential loop to avoid job
    /// scheduling overhead for small meshes.
    /// </summary>
    private static bool[] FilterTerrainTriangles(
        List<Vector3> rawWorldVertices,
        int triangleCount,
        Vector2 lakeCenterXZ,
        float inclusionRadius)
    {
        bool[] accepted = new bool[triangleCount];

        if (triangleCount < JobTriangleThreshold)
        {
            // Sequential path – avoids NativeArray allocation + job schedule
            // overhead for small captures (single chunk / few triangles).
            for (int i = 0; i < triangleCount; i++)
            {
                Vector3 worldA = rawWorldVertices[i * 3];
                Vector3 worldB = rawWorldVertices[i * 3 + 1];
                Vector3 worldC = rawWorldVertices[i * 3 + 2];
                Vector3 normal = Vector3.Cross(worldB - worldA, worldC - worldA);
                float normalMagnitude = normal.magnitude;
                if (normalMagnitude <= 0.0001f || (normal.y / normalMagnitude) < 0.12f)
                {
                    continue;
                }

                if (DistancePointToTriangleXZ(lakeCenterXZ, worldA, worldB, worldC) > inclusionRadius)
                {
                    continue;
                }

                accepted[i] = true;
            }

            return accepted;
        }

        // Job path – Burst-compiled parallel filter across all chunks in one batch.
        int vertexCount = triangleCount * 3;
        NativeArray<float3> nativeVerts = new NativeArray<float3>(
            vertexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<bool> nativeAccepted = new NativeArray<bool>(
            triangleCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        try
        {
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 v = rawWorldVertices[i];
                nativeVerts[i] = new float3(v.x, v.y, v.z);
            }

            new LakeTriangleFilterJob
            {
                WorldVertices = nativeVerts,
                LakeCenterXZ = new float2(lakeCenterXZ.x, lakeCenterXZ.y),
                InclusionRadius = inclusionRadius,
                Accepted = nativeAccepted
            }.Schedule(triangleCount, 32).Complete();

            nativeAccepted.CopyTo(accepted);
        }
        finally
        {
            nativeVerts.Dispose();
            nativeAccepted.Dispose();
        }

        return accepted;
    }

    private static void AddLakeTerrainTriangleEdge(
        Dictionary<LakeEdgeKey, List<LakePendingEdge>> edgeMap,
        int triangleIndex,
        Vector3 edgeA,
        Vector3 edgeB)
    {
        if (edgeMap == null)
        {
            return;
        }

        LakeEdgeKey edgeKey = GetLakeEdgeKey(edgeA, edgeB);
        if (!edgeMap.TryGetValue(edgeKey, out List<LakePendingEdge> sharedEdges))
        {
            sharedEdges = new List<LakePendingEdge>(2);
            edgeMap[edgeKey] = sharedEdges;
        }

        sharedEdges.Add(new LakePendingEdge(triangleIndex, edgeA, edgeB));
    }

    private static LakeEdgeKey GetLakeEdgeKey(Vector3 a, Vector3 b)
    {
        return new LakeEdgeKey(GetLakeVertexKey(a), GetLakeVertexKey(b));
    }

    private static LakeVertexKey GetLakeVertexKey(Vector3 point)
    {
        return new LakeVertexKey(
            Mathf.RoundToInt(point.x * 1000f),
            Mathf.RoundToInt(point.y * 1000f),
            Mathf.RoundToInt(point.z * 1000f));
    }

    private static bool TryClipTriangleBelowSurface(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        float surfaceY,
        List<Vector3> clippedPolygon)
    {
        if (clippedPolygon == null)
        {
            return false;
        }

        clippedPolygon.Clear();
        ClipLakeTriangleEdge(c, a, surfaceY, clippedPolygon);
        ClipLakeTriangleEdge(a, b, surfaceY, clippedPolygon);
        ClipLakeTriangleEdge(b, c, surfaceY, clippedPolygon);
        return clippedPolygon.Count >= 3;
    }

    private static void ClipLakeTriangleEdge(Vector3 start, Vector3 end, float surfaceY, List<Vector3> clippedPolygon)
    {
        bool startInside = start.y < surfaceY - 0.0001f;
        bool endInside = end.y < surfaceY - 0.0001f;
        if (startInside && endInside)
        {
            clippedPolygon.Add(end);
            return;
        }

        if (startInside && !endInside)
        {
            clippedPolygon.Add(IntersectLakeEdgeAtSurface(start, end, surfaceY));
            return;
        }

        if (!startInside && endInside)
        {
            clippedPolygon.Add(IntersectLakeEdgeAtSurface(start, end, surfaceY));
            clippedPolygon.Add(end);
        }
    }

    private static Vector3 IntersectLakeEdgeAtSurface(Vector3 start, Vector3 end, float surfaceY)
    {
        float heightDelta = end.y - start.y;
        if (Mathf.Abs(heightDelta) <= 0.0001f)
        {
            return new Vector3(end.x, surfaceY, end.z);
        }

        float t = Mathf.Clamp01((surfaceY - start.y) / heightDelta);
        Vector3 point = Vector3.Lerp(start, end, t);
        point.y = surfaceY;
        return point;
    }

    private static bool HasSurfaceGeometry(GeneratedLake lake)
    {
        return lake != null &&
               lake.surfaceVertices != null &&
               lake.surfaceTriangles != null &&
               lake.surfaceTriangles.Length >= 3;
    }

    private static string DescribeLake(GeneratedLake lake)
    {
        if (lake == null)
        {
            return "Lake<null>";
        }

        string bodyType = lake.isPond ? "Pond" : "Lake";
        return $"{bodyType}(center=({lake.center.x:F1}, {lake.center.z:F1}), surfaceY={lake.surfaceY:F2}, captureRadius={lake.captureRadius:F2})";
    }

    private void Log(string message) => debugLogger?.Invoke(message);
}
