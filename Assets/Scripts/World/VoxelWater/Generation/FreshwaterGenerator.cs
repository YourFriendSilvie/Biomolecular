using System;
using System.Collections.Generic;
using UnityEngine;
using static WaterMathUtility;

internal sealed class FreshwaterGenerator
{
    private const int RiverRenderSubdivisionsPerSegment = 3;

    // Pre-allocated buffers for PassesCachedFreshwaterBasinGuard – reused every call to
    // avoid per-attempt heap allocations.  MaxBasinGuardHalfCellCount drives the maximum
    // grid dimension (halfCount*2+1)^2.  Keeping it at 18 (grid 37×37 = 1 369 cells) gives
    // the same containment accuracy as the old cap of 32 (65×65 = 4 225 cells) because the
    // cell size is now chosen relative to the evaluation radius, not the lake radius, so
    // coarser cells still give the same physical coverage.
    private const int MaxBasinGuardHalfCellCount = 18;
    private const int MaxBasinGuardGridCount = MaxBasinGuardHalfCellCount * 2 + 1;
    private const int MaxBasinGuardTotalCellCount = MaxBasinGuardGridCount * MaxBasinGuardGridCount;
    private readonly float[] _basinGuardHeightBuffer = new float[MaxBasinGuardTotalCellCount];
    private readonly bool[] _basinGuardVisitedBuffer = new bool[MaxBasinGuardTotalCellCount];
    private readonly Queue<int> _basinGuardQueue = new Queue<int>(MaxBasinGuardGridCount * 2);

    internal struct FreshwaterGeneratorConfig
    {
        public float seaLevelMeters;
        public float lakeDepthMeters;
        public float pondDepthMeters;
        public float lakeDynamicExpansionMeters;
        public float waterUpdatePaddingMeters;
        public float riverCarveStepMeters;
        public float riverDepthMeters;
        public bool carveTerrainForWater;
        public Vector2 lakeRadiusRangeMeters;
        public Vector2 pondRadiusRangeMeters;
        public Vector2 riverWidthRangeMeters;
        public int riverSampleCount;
        public Vector2 riverSourceHeightRangeNormalized;
        public float minRenderableVolumeCubicMeters;
    }

    private readonly float seaLevelMeters;
    private readonly float lakeDepthMeters;
    private readonly float pondDepthMeters;
    private readonly float lakeDynamicExpansionMeters;
    private readonly float waterUpdatePaddingMeters;
    private readonly float riverCarveStepMeters;
    private readonly float riverDepthMeters;
    private readonly bool carveTerrainForWater;
    private readonly Vector2 lakeRadiusRangeMeters;
    private readonly Vector2 pondRadiusRangeMeters;
    private readonly Vector2 riverWidthRangeMeters;
    private readonly int riverSampleCount;
    private readonly Vector2 riverSourceHeightRangeNormalized;
    private readonly float minRenderableVolumeCubicMeters;
    private readonly List<GeneratedLake> generatedLakes;
    private readonly List<GeneratedRiver> generatedRivers;
    private readonly List<GeneratedRiverSegment> generatedRiverSegments;
    private readonly LakeHydrologySolver hydrologySolver;
    private readonly Action<string, float, bool> updateGenerationProgress;
    private readonly Action<ProceduralVoxelTerrain, GeneratedLake> updateLakeInfluenceBounds;
    private readonly Action<ProceduralVoxelTerrain, GeneratedRiver> updateRiverInfluenceBounds;
    private readonly Func<string> getOwnerName;
    private readonly Action<string> logDebug;

    public FreshwaterGenerator(
        FreshwaterGeneratorConfig config,
        List<GeneratedLake> generatedLakes,
        List<GeneratedRiver> generatedRivers,
        List<GeneratedRiverSegment> generatedRiverSegments,
        LakeHydrologySolver hydrologySolver,
        Action<string, float, bool> updateGenerationProgress,
        Action<ProceduralVoxelTerrain, GeneratedLake> updateLakeInfluenceBounds,
        Action<ProceduralVoxelTerrain, GeneratedRiver> updateRiverInfluenceBounds,
        Func<string> getOwnerName,
        Action<string> logDebug)
    {
        seaLevelMeters = config.seaLevelMeters;
        lakeDepthMeters = config.lakeDepthMeters;
        pondDepthMeters = config.pondDepthMeters;
        lakeDynamicExpansionMeters = config.lakeDynamicExpansionMeters;
        waterUpdatePaddingMeters = config.waterUpdatePaddingMeters;
        riverCarveStepMeters = config.riverCarveStepMeters;
        riverDepthMeters = config.riverDepthMeters;
        carveTerrainForWater = config.carveTerrainForWater;
        lakeRadiusRangeMeters = config.lakeRadiusRangeMeters;
        pondRadiusRangeMeters = config.pondRadiusRangeMeters;
        riverWidthRangeMeters = config.riverWidthRangeMeters;
        riverSampleCount = config.riverSampleCount;
        riverSourceHeightRangeNormalized = config.riverSourceHeightRangeNormalized;
        minRenderableVolumeCubicMeters = config.minRenderableVolumeCubicMeters;
        this.generatedLakes = generatedLakes ?? new List<GeneratedLake>();
        this.generatedRivers = generatedRivers ?? new List<GeneratedRiver>();
        this.generatedRiverSegments = generatedRiverSegments ?? new List<GeneratedRiverSegment>();
        this.hydrologySolver = hydrologySolver;
        this.updateGenerationProgress = updateGenerationProgress;
        this.updateLakeInfluenceBounds = updateLakeInfluenceBounds;
        this.updateRiverInfluenceBounds = updateRiverInfluenceBounds;
        this.getOwnerName = getOwnerName;
        this.logDebug = logDebug;
    }

    public FreshwaterGenerationStats GenerateLakes(System.Random random, ProceduralVoxelTerrain terrain, Bounds bounds, int lakeCount)
    {
        return GenerateFreshwaterBodies(random, terrain, bounds, lakeCount, lakeRadiusRangeMeters, lakeDepthMeters, false);
    }

    public FreshwaterGenerationStats GeneratePonds(System.Random random, ProceduralVoxelTerrain terrain, Bounds bounds, int pondCount)
    {
        return GenerateFreshwaterBodies(random, terrain, bounds, pondCount, pondRadiusRangeMeters, pondDepthMeters, true);
    }

    public string GenerateRivers(System.Random random, ProceduralVoxelTerrain terrain, Bounds bounds, int riverCount)
    {
        if (riverCount <= 0)
        {
            UpdateWaterGenerationProgress("Generating rivers", 1f, true);
            return "Rivers disabled";
        }

        int generatedCount = 0;
        int attempts = 0;
        int maxAttempts = riverCount * 20;
        while (generatedCount < riverCount && attempts < maxAttempts)
        {
            attempts++;
            UpdateWaterGenerationProgress(
                $"Generating rivers ({generatedCount}/{riverCount}, attempt {attempts}/{maxAttempts})",
                CalculateGenerationAttemptProgress(generatedCount, riverCount, attempts, maxAttempts),
                false);

            if (!TryFindRiverSource(random, terrain, bounds, out Vector3 source))
            {
                continue;
            }

            Vector3 mouth = CreateRiverMouth(random, bounds);
            float width = NextFloat(random, riverWidthRangeMeters.x, riverWidthRangeMeters.y);
            List<Vector3> samples = BuildRiverPath(random, terrain, bounds, source, mouth, width);
            if (samples.Count < 4)
            {
                continue;
            }

            if (carveTerrainForWater)
            {
                CarveRiver(terrain, samples, width, riverDepthMeters);
            }

            List<Vector3> waterPath = BuildRiverWaterPath(terrain, samples, riverDepthMeters, seaLevelMeters);
            if (waterPath.Count < 2)
            {
                continue;
            }

            List<float> widthProfile = BuildRiverWidthProfile(waterPath.Count, width);
            GeneratedRiver generatedRiver = BuildRenderableRiver(waterPath, widthProfile, width, terrain);
            if (generatedRiver == null)
            {
                continue;
            }

            generatedRivers.Add(generatedRiver);
            generatedCount++;
            UpdateWaterGenerationProgress(
                $"Generating rivers ({generatedCount}/{riverCount}, attempt {attempts}/{maxAttempts})",
                CalculateGenerationAttemptProgress(generatedCount, riverCount, attempts, maxAttempts),
                false);
        }

        RebuildRiverSegmentCache(generatedRivers, generatedRiverSegments);
        UpdateWaterGenerationProgress($"Generating rivers ({generatedCount}/{riverCount})", 1f, true);
        return $"rivers accepted={generatedCount}/{riverCount}, attempts={attempts}/{maxAttempts}";
    }

    public bool TryCreateDebugLakeAtPoint(ProceduralVoxelTerrain terrain, Vector3 worldPoint, float radiusMeters, out GeneratedLake createdLake)
    {
        createdLake = null;
        if (terrain == null)
        {
            return false;
        }

        radiusMeters = Mathf.Max(terrain.VoxelSizeMeters * 4f, radiusMeters);
        if (!terrain.TrySampleSurfaceWorld(worldPoint.x, worldPoint.z, out RaycastHit anchorHit))
        {
            LogLakeDebug($"Debug lake skipped because terrain could not be sampled at {worldPoint}.");
            return false;
        }

        if (!TrySampleLakeShorelineStats(terrain, anchorHit.point, radiusMeters, out float minimumShoreHeight, out float averageShoreHeight))
        {
            LogLakeDebug($"Debug lake skipped because shoreline sampling failed near {anchorHit.point}.");
            return false;
        }

        float surfaceY = Mathf.Max(
            seaLevelMeters + 0.55f,
            Mathf.Min(minimumShoreHeight - 0.08f, averageShoreHeight - 0.05f));
        if (!TryRefineLakeSurfaceY(terrain, anchorHit.point, radiusMeters, surfaceY, lakeDepthMeters, false, carveTerrainForWater, out surfaceY))
        {
            LogLakeDebug($"Debug lake skipped because surface refinement failed near {anchorHit.point}.");
            return false;
        }

        if (carveTerrainForWater)
        {
            CarveLake(terrain, anchorHit.point, radiusMeters, surfaceY, lakeDepthMeters, false, false);
        }

        if (!TryCreateLakeAtSurface(terrain, anchorHit.point, radiusMeters, surfaceY, false, out createdLake))
        {
            LogLakeDebug($"Debug lake failed to initialize near {anchorHit.point}.");
            return false;
        }

        if (carveTerrainForWater)
        {
            PaintFreshwaterBasinMaterials(terrain, anchorHit.point, radiusMeters, surfaceY, lakeDepthMeters, false);
        }

        LogLakeDebug($"Created debug lake near {anchorHit.point}. Radius={radiusMeters:F2}m, surfaceY={surfaceY:F2}.");
        return true;
    }

    public bool TryCreateMergeTestLakePairAtPoint(
        ProceduralVoxelTerrain terrain,
        Vector3 worldPoint,
        Vector3 lateralDirection,
        float radiusMeters,
        float shorelineGapMeters,
        float ridgeHeightMeters,
        out GeneratedLake firstLake,
        out GeneratedLake secondLake)
    {
        firstLake = null;
        secondLake = null;
        if (terrain == null)
        {
            return false;
        }

        radiusMeters = Mathf.Max(terrain.VoxelSizeMeters * 4f, radiusMeters);
        shorelineGapMeters = Mathf.Max(terrain.VoxelSizeMeters * 2f, shorelineGapMeters);
        ridgeHeightMeters = Mathf.Max(terrain.VoxelSizeMeters, ridgeHeightMeters);

        Vector3 lateralAxis = Vector3.ProjectOnPlane(lateralDirection, Vector3.up);
        if (lateralAxis.sqrMagnitude <= 0.0001f)
        {
            lateralAxis = Vector3.right;
        }

        lateralAxis.Normalize();
        if (!terrain.TrySampleSurfaceWorld(worldPoint.x, worldPoint.z, out RaycastHit anchorHit))
        {
            LogLakeDebug($"Merge-test lake pair skipped because terrain could not be sampled at {worldPoint}.");
            return false;
        }

        float effectiveRadius = radiusMeters * 0.96f;
        float centerDistance = (effectiveRadius * 2f) + shorelineGapMeters;
        Vector3 firstProbe = anchorHit.point - (lateralAxis * (centerDistance * 0.5f));
        Vector3 secondProbe = anchorHit.point + (lateralAxis * (centerDistance * 0.5f));
        if (!terrain.TrySampleSurfaceWorld(firstProbe.x, firstProbe.z, out RaycastHit firstHit) ||
            !terrain.TrySampleSurfaceWorld(secondProbe.x, secondProbe.z, out RaycastHit secondHit))
        {
            LogLakeDebug($"Merge-test lake pair skipped because one or both lake centers could not be sampled near {anchorHit.point}.");
            return false;
        }

        if (!TrySampleLakeShorelineStats(terrain, firstHit.point, radiusMeters, out float firstMinimumShoreHeight, out float firstAverageShoreHeight) ||
            !TrySampleLakeShorelineStats(terrain, secondHit.point, radiusMeters, out float secondMinimumShoreHeight, out float secondAverageShoreHeight))
        {
            LogLakeDebug($"Merge-test lake pair skipped because shoreline sampling failed near {anchorHit.point}.");
            return false;
        }

        float sharedSurfaceY = Mathf.Max(
            seaLevelMeters + 0.55f,
            Mathf.Min(firstMinimumShoreHeight, secondMinimumShoreHeight) - 0.08f);
        sharedSurfaceY = Mathf.Min(sharedSurfaceY, Mathf.Min(firstAverageShoreHeight, secondAverageShoreHeight) - 0.05f);
        sharedSurfaceY = Mathf.Max(seaLevelMeters + 0.55f, sharedSurfaceY);

        if (carveTerrainForWater)
        {
            CarveLake(terrain, firstHit.point, radiusMeters, sharedSurfaceY, lakeDepthMeters, false, false);
            CarveLake(terrain, secondHit.point, radiusMeters, sharedSurfaceY, lakeDepthMeters, false, false);
            BuildMergeTestRidge(terrain, firstHit.point, secondHit.point, sharedSurfaceY, shorelineGapMeters, ridgeHeightMeters);
        }

        if (!TryCreateLakeAtSurface(terrain, firstHit.point, radiusMeters, sharedSurfaceY, false, out firstLake) ||
            !TryCreateLakeAtSurface(terrain, secondHit.point, radiusMeters, sharedSurfaceY, false, out secondLake))
        {
            LogLakeDebug($"Merge-test lake pair failed to initialize near {anchorHit.point}.");
            return false;
        }

        if (carveTerrainForWater)
        {
            PaintFreshwaterBasinMaterials(terrain, firstHit.point, radiusMeters, sharedSurfaceY, lakeDepthMeters, false);
            PaintFreshwaterBasinMaterials(terrain, secondHit.point, radiusMeters, sharedSurfaceY, lakeDepthMeters, false);
        }

        LogLakeDebug(
            $"Created lake pair near {anchorHit.point}. Radius={radiusMeters:F2}m, gap={shorelineGapMeters:F2}m, ridge={ridgeHeightMeters:F2}m. Automatic merge resolution is disabled in the simplified lake runtime.");
        return true;
    }

    internal static List<Vector3> BuildRiverWaterPath(
        ProceduralVoxelTerrain terrain,
        IReadOnlyList<Vector3> carvedPath,
        float depthMeters,
        float seaLevelMeters)
    {
        List<Vector3> waterPath = new List<Vector3>(carvedPath != null ? carvedPath.Count : 0);
        if (terrain == null || carvedPath == null || carvedPath.Count == 0)
        {
            return waterPath;
        }

        for (int i = 0; i < carvedPath.Count; i++)
        {
            if (!terrain.TrySampleSurfaceWorld(carvedPath[i].x, carvedPath[i].z, out RaycastHit hit))
            {
                continue;
            }

            float waterY = hit.point.y + Mathf.Max(0.16f, depthMeters * 0.72f);
            if (waterPath.Count > 0)
            {
                Vector3 previousPoint = waterPath[waterPath.Count - 1];
                float stepDistance = Vector3.Distance(
                    new Vector3(previousPoint.x, 0f, previousPoint.z),
                    new Vector3(hit.point.x, 0f, hit.point.z));
                float maxAllowedY = previousPoint.y - Mathf.Max(0.012f, stepDistance * 0.006f);
                waterY = Mathf.Min(waterY, maxAllowedY);
            }

            waterY = Mathf.Max(seaLevelMeters + 0.04f, waterY);
            waterPath.Add(new Vector3(hit.point.x, waterY, hit.point.z));
        }

        return waterPath;
    }

    internal static List<float> BuildRiverWidthProfile(int sampleCount, float baseWidth)
    {
        List<float> widths = new List<float>(Mathf.Max(0, sampleCount));
        if (sampleCount <= 0)
        {
            return widths;
        }

        for (int i = 0; i < sampleCount; i++)
        {
            float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
            widths.Add(Mathf.Lerp(baseWidth * 0.88f, baseWidth * 1.08f, t));
        }

        return widths;
    }

    internal static void PopulateRenderableRiverPath(GeneratedRiver river)
    {
        if (river == null)
        {
            return;
        }

        river.points.Clear();
        river.widths.Clear();
        if (river.waterPath.Count < 2 || river.widthProfile.Count != river.waterPath.Count)
        {
            return;
        }

        for (int i = 0; i < river.waterPath.Count - 1; i++)
        {
            for (int subdivision = 0; subdivision < RiverRenderSubdivisionsPerSegment; subdivision++)
            {
                float t = subdivision / (float)RiverRenderSubdivisionsPerSegment;
                river.points.Add(Vector3.Lerp(river.waterPath[i], river.waterPath[i + 1], t));
                river.widths.Add(Mathf.Lerp(river.widthProfile[i], river.widthProfile[i + 1], t));
            }
        }

        river.points.Add(river.waterPath[river.waterPath.Count - 1]);
        river.widths.Add(river.widthProfile[river.widthProfile.Count - 1]);
    }

    internal static void RebuildRiverSegmentCache(IReadOnlyList<GeneratedRiver> rivers, List<GeneratedRiverSegment> riverSegments)
    {
        if (rivers == null || riverSegments == null)
        {
            return;
        }

        riverSegments.Clear();
        for (int riverIndex = 0; riverIndex < rivers.Count; riverIndex++)
        {
            GeneratedRiver river = rivers[riverIndex];
            if (river == null || river.waterPath.Count < 2 || river.widthProfile.Count != river.waterPath.Count)
            {
                continue;
            }

            for (int i = 1; i < river.waterPath.Count; i++)
            {
                riverSegments.Add(new GeneratedRiverSegment
                {
                    start = river.waterPath[i - 1],
                    end = river.waterPath[i],
                    width = Mathf.Lerp(river.widthProfile[i - 1], river.widthProfile[i], 0.5f),
                    surfaceY = Mathf.Lerp(river.waterPath[i - 1].y, river.waterPath[i].y, 0.5f)
                });
            }
        }
    }

    private static float CalculateGenerationAttemptProgress(int generatedCount, int targetCount, int attempts, int maxAttempts)
    {
        float acceptanceProgress = targetCount <= 0 ? 1f : Mathf.Clamp01(generatedCount / (float)targetCount);
        float attemptProgress = maxAttempts <= 0 ? 1f : Mathf.Clamp01(attempts / (float)maxAttempts);
        return Mathf.Max(acceptanceProgress, attemptProgress);
    }

    private bool TryInitializeLakeState(ProceduralVoxelTerrain terrain, GeneratedLake lake, float? maxCaptureRadiusOverride = null)
    {
        if (terrain == null || lake == null || hydrologySolver == null)
        {
            return false;
        }

        if (!hydrologySolver.TryEvaluateLakeAtFixedSurfaceWithOverflowExpansion(terrain, lake, lake.surfaceY, null, out LakeTerrainPatch terrainPatch, out LakeSolveResult solveResult, maxCaptureRadiusOverride) ||
            solveResult.volumeCubicMeters <= minRenderableVolumeCubicMeters)
        {
            return false;
        }

        if (solveResult.touchesOpenBoundary)
        {
            LogLakeDebug($"Rejected {WaterDebugUtility.DescribeLake(lake)} because its solved surface still touched an open boundary.");
            return false;
        }

        lake.terrainPatch = terrainPatch;
        hydrologySolver.ApplyLakeSolveResult(lake, solveResult);
        if (!IsReasonableGeneratedFreshwaterSurface(terrain, lake))
        {
            LogLakeDebug($"Rejected {WaterDebugUtility.DescribeLake(lake)} because the solved freshwater footprint was too large for its requested radius.");
            return false;
        }

        TrimLakeCaptureRadiusToSolvedSurface(terrain, lake);
        updateLakeInfluenceBounds?.Invoke(terrain, lake);
        return WaterSpatialQueryUtility.IsLakeActive(lake, minRenderableVolumeCubicMeters);
    }

    private GeneratedLake CreateFreshwaterCandidateSeed(Vector3 center, float radiusMeters, float surfaceY, bool isPond)
    {
        float effectiveRadius = radiusMeters * 0.96f;
        return new GeneratedLake
        {
            center = center,
            radius = effectiveRadius,
            surfaceY = surfaceY,
            captureRadius = effectiveRadius + lakeDynamicExpansionMeters,
            isPond = isPond
        };
    }

    private bool TrySolveCarvedLakeLocally(
        ProceduralVoxelTerrain terrain,
        GeneratedLake previewLake,
        Vector3 center,
        float radiusMeters,
        float surfaceY,
        bool isPond,
        out GeneratedLake lake)
    {
        lake = null;
        if (terrain == null || previewLake == null)
        {
            return false;
        }

        GeneratedLake carvedLakeSeed = CreateFreshwaterCandidateSeed(center, radiusMeters, surfaceY, isPond);
        float localRefreshPadding = Mathf.Max(
            terrain.VoxelSizeMeters * 2f,
            radiusMeters * (isPond ? 0.18f : 0.24f),
            lakeDynamicExpansionMeters * 0.5f);
        float maxCaptureRadius = Mathf.Max(carvedLakeSeed.radius, previewLake.captureRadius + localRefreshPadding);
        carvedLakeSeed.captureRadius = maxCaptureRadius;
        if (!TryInitializeLakeState(terrain, carvedLakeSeed, maxCaptureRadius))
        {
            return false;
        }

        lake = carvedLakeSeed;
        return true;
    }

    private bool TryCreateLakeAtSurface(ProceduralVoxelTerrain terrain, GeneratedLake candidateLake, out GeneratedLake lake)
    {
        lake = null;
        if (terrain == null || candidateLake == null)
        {
            return false;
        }

        float maxInitialCaptureRadius = GetMaxInitialGeneratedFreshwaterCaptureRadius(terrain, candidateLake);
        candidateLake.captureRadius = Mathf.Min(candidateLake.captureRadius, maxInitialCaptureRadius);
        if (!TryInitializeLakeState(terrain, candidateLake, maxInitialCaptureRadius))
        {
            return false;
        }

        lake = candidateLake;
        return true;
    }

    private bool TryCreateLakeAtSurface(ProceduralVoxelTerrain terrain, Vector3 center, float radiusMeters, float surfaceY, bool isPond, out GeneratedLake lake)
    {
        return TryCreateLakeAtSurface(terrain, CreateFreshwaterCandidateSeed(center, radiusMeters, surfaceY, isPond), out lake);
    }

    private bool PassesCachedFreshwaterBasinGuard(
        ProceduralVoxelTerrain terrain,
        GeneratedLake candidateLake,
        float depthMeters,
        bool willCarveTerrain)
    {
        if (terrain == null || candidateLake == null)
        {
            return false;
        }

        float maxAllowedSurfaceRadius = GetMaxAllowedGeneratedFreshwaterSurfaceRadius(terrain, candidateLake);
        float evaluationRadius = Mathf.Max(candidateLake.radius, GetMaxInitialGeneratedFreshwaterCaptureRadius(terrain, candidateLake));
        // Cell size is now the larger of (a) a minimum voxel-relative floor, (b) a fraction of
        // the evaluation radius so the grid adapts to the area being sampled, and (c) a fraction
        // of the lake radius to keep adequate centre resolution.  This avoids the previous
        // mismatch where radius/3.75 produced tiny cells that still covered the full (much
        // larger) evaluation radius, leading to O(evaluationRadius²/radius²) grid cells.
        float desiredCellSize = Mathf.Max(
            terrain.VoxelSizeMeters * 1.5f,
            evaluationRadius / 14f,
            candidateLake.radius / (candidateLake.isPond ? 4f : 5f));
        int halfCellCount = Mathf.Clamp(
            Mathf.CeilToInt(evaluationRadius / Mathf.Max(desiredCellSize, 0.001f)),
            4,
            MaxBasinGuardHalfCellCount);
        float cellSize = evaluationRadius / Mathf.Max(halfCellCount, 1);
        int gridCount = (halfCellCount * 2) + 1;
        int totalCellCount = gridCount * gridCount;
        // Reuse pre-allocated buffer instead of a fresh heap allocation on every attempt.
        float[] sampledHeights = _basinGuardHeightBuffer;
        float originX = candidateLake.center.x - (halfCellCount * cellSize);
        float originZ = candidateLake.center.z - (halfCellCount * cellSize);
        for (int cellZ = 0; cellZ < gridCount; cellZ++)
        {
            float worldZ = originZ + (cellZ * cellSize);
            for (int cellX = 0; cellX < gridCount; cellX++)
            {
                float worldX = originX + (cellX * cellSize);
                sampledHeights[GetCachedFreshwaterCandidateCellIndex(cellX, cellZ, gridCount)] =
                    EstimateCachedFreshwaterCandidateHeight(terrain, candidateLake, depthMeters, willCarveTerrain, worldX, worldZ);
            }
        }

        float floodDepthMargin = Mathf.Max(0.05f, Mathf.Min(depthMeters * 0.18f, terrain.VoxelSizeMeters * 0.4f));
        float floodedHeightThreshold = candidateLake.surfaceY - floodDepthMargin;
        if (!TryFindCachedFreshwaterSeedCell(candidateLake, sampledHeights, gridCount, halfCellCount, cellSize, floodedHeightThreshold, out int seedIndex))
        {
            return true;
        }

        bool[] visited = _basinGuardVisitedBuffer;
        Array.Clear(visited, 0, totalCellCount);
        Queue<int> queue = _basinGuardQueue;
        queue.Clear();
        queue.Enqueue(seedIndex);
        visited[seedIndex] = true;
        float allowedFloodRadius = maxAllowedSurfaceRadius + (cellSize * (candidateLake.isPond ? 1.5f : 2f));
        float allowedFloodRadiusSquared = allowedFloodRadius * allowedFloodRadius;
        while (queue.Count > 0)
        {
            int cellIndex = queue.Dequeue();
            int cellZ = cellIndex / gridCount;
            int cellX = cellIndex - (cellZ * gridCount);
            if (cellX == 0 || cellX == gridCount - 1 || cellZ == 0 || cellZ == gridCount - 1)
            {
                return false;
            }

            float offsetX = (cellX - halfCellCount) * cellSize;
            float offsetZ = (cellZ - halfCellCount) * cellSize;
            if ((offsetX * offsetX) + (offsetZ * offsetZ) > allowedFloodRadiusSquared)
            {
                return false;
            }

            TryEnqueueCachedFreshwaterNeighbor(cellX + 1, cellZ, gridCount, sampledHeights, floodedHeightThreshold, visited, queue);
            TryEnqueueCachedFreshwaterNeighbor(cellX - 1, cellZ, gridCount, sampledHeights, floodedHeightThreshold, visited, queue);
            TryEnqueueCachedFreshwaterNeighbor(cellX, cellZ + 1, gridCount, sampledHeights, floodedHeightThreshold, visited, queue);
            TryEnqueueCachedFreshwaterNeighbor(cellX, cellZ - 1, gridCount, sampledHeights, floodedHeightThreshold, visited, queue);
        }

        return true;
    }

    private static int GetCachedFreshwaterCandidateCellIndex(int cellX, int cellZ, int gridCount)
    {
        return (cellZ * gridCount) + cellX;
    }

    private bool TryFindCachedFreshwaterSeedCell(
        GeneratedLake candidateLake,
        float[] sampledHeights,
        int gridCount,
        int halfCellCount,
        float cellSize,
        float floodedHeightThreshold,
        out int seedIndex)
    {
        seedIndex = -1;
        if (candidateLake == null || sampledHeights == null || sampledHeights.Length == 0)
        {
            return false;
        }

        int centerCellIndex = GetCachedFreshwaterCandidateCellIndex(halfCellCount, halfCellCount, gridCount);
        if (centerCellIndex >= 0 &&
            centerCellIndex < sampledHeights.Length &&
            sampledHeights[centerCellIndex] < floodedHeightThreshold)
        {
            seedIndex = centerCellIndex;
            return true;
        }

        float seedSearchRadius = candidateLake.radius * (candidateLake.isPond ? 0.42f : 0.48f);
        int searchCellRadius = Mathf.Max(1, Mathf.CeilToInt(seedSearchRadius / Mathf.Max(cellSize, 0.001f)));
        float maxSearchDistanceSquared = seedSearchRadius * seedSearchRadius;
        float lowestHeight = float.PositiveInfinity;
        for (int offsetZ = -searchCellRadius; offsetZ <= searchCellRadius; offsetZ++)
        {
            int cellZ = halfCellCount + offsetZ;
            if (cellZ < 0 || cellZ >= gridCount)
            {
                continue;
            }

            float worldOffsetZ = offsetZ * cellSize;
            for (int offsetX = -searchCellRadius; offsetX <= searchCellRadius; offsetX++)
            {
                int cellX = halfCellCount + offsetX;
                if (cellX < 0 || cellX >= gridCount)
                {
                    continue;
                }

                float worldOffsetX = offsetX * cellSize;
                if ((worldOffsetX * worldOffsetX) + (worldOffsetZ * worldOffsetZ) > maxSearchDistanceSquared)
                {
                    continue;
                }

                int cellIndex = GetCachedFreshwaterCandidateCellIndex(cellX, cellZ, gridCount);
                float sampledHeight = sampledHeights[cellIndex];
                if (sampledHeight >= floodedHeightThreshold || sampledHeight >= lowestHeight)
                {
                    continue;
                }

                lowestHeight = sampledHeight;
                seedIndex = cellIndex;
            }
        }

        return seedIndex >= 0;
    }

    private static void TryEnqueueCachedFreshwaterNeighbor(
        int cellX,
        int cellZ,
        int gridCount,
        float[] sampledHeights,
        float floodedHeightThreshold,
        bool[] visited,
        Queue<int> queue)
    {
        if (cellX < 0 ||
            cellX >= gridCount ||
            cellZ < 0 ||
            cellZ >= gridCount ||
            sampledHeights == null ||
            visited == null ||
            queue == null)
        {
            return;
        }

        int cellIndex = GetCachedFreshwaterCandidateCellIndex(cellX, cellZ, gridCount);
        if (cellIndex < 0 ||
            cellIndex >= sampledHeights.Length ||
            cellIndex >= visited.Length ||
            visited[cellIndex] ||
            sampledHeights[cellIndex] >= floodedHeightThreshold)
        {
            return;
        }

        visited[cellIndex] = true;
        queue.Enqueue(cellIndex);
    }

    private float EstimateCachedFreshwaterCandidateHeight(
        ProceduralVoxelTerrain terrain,
        GeneratedLake candidateLake,
        float depthMeters,
        bool willCarveTerrain,
        float worldX,
        float worldZ)
    {
        if (terrain == null || candidateLake == null)
        {
            return float.PositiveInfinity;
        }

        if (!terrain.TryGetCachedSurfaceHeightWorld(worldX, worldZ, out float sampledHeight))
        {
            return float.NegativeInfinity;
        }

        if (willCarveTerrain)
        {
            sampledHeight -= EstimateCachedFreshwaterCarveDepth(candidateLake, depthMeters, worldX, worldZ);
        }

        return sampledHeight;
    }

    private static float EstimateCachedFreshwaterCarveDepth(GeneratedLake candidateLake, float depthMeters, float worldX, float worldZ)
    {
        if (candidateLake == null || depthMeters <= 0f || candidateLake.radius <= 0.0001f)
        {
            return 0f;
        }

        float offsetX = worldX - candidateLake.center.x;
        float offsetZ = worldZ - candidateLake.center.z;
        float normalizedDistance = Mathf.Sqrt((offsetX * offsetX) + (offsetZ * offsetZ)) / candidateLake.radius;
        float conservativeOuterRadius = candidateLake.isPond ? 0.88f : 0.9f;
        if (normalizedDistance >= conservativeOuterRadius)
        {
            return 0f;
        }

        float bowl = 1f - Mathf.Clamp01(normalizedDistance / conservativeOuterRadius);
        bowl *= bowl;
        return depthMeters * (candidateLake.isPond ? 0.58f : 0.52f) * bowl;
    }

    private float GetMaxAllowedGeneratedFreshwaterSurfaceRadius(ProceduralVoxelTerrain terrain, GeneratedLake lake)
    {
        if (terrain == null || lake == null)
        {
            return 0f;
        }

        return lake.isPond
            ? Mathf.Max(lake.radius * 3.25f, lake.radius + Mathf.Max(lakeDynamicExpansionMeters, terrain.VoxelSizeMeters * 8f))
            : Mathf.Max(lake.radius * 5.5f, lake.radius + Mathf.Max(lakeDynamicExpansionMeters * 1.6f, terrain.VoxelSizeMeters * 16f));
    }

    private float GetMaxInitialGeneratedFreshwaterCaptureRadius(ProceduralVoxelTerrain terrain, GeneratedLake lake)
    {
        if (terrain == null || lake == null)
        {
            return 0f;
        }

        float capturePadding = Mathf.Max(lakeDynamicExpansionMeters, terrain.VoxelSizeMeters * 3f);
        return Mathf.Max(lake.captureRadius, GetMaxAllowedGeneratedFreshwaterSurfaceRadius(terrain, lake) + capturePadding);
    }

    private bool IsReasonableGeneratedFreshwaterSurface(ProceduralVoxelTerrain terrain, GeneratedLake lake)
    {
        if (terrain == null || lake == null || !WaterSpatialQueryUtility.HasLakeSurfaceGeometry(lake))
        {
            return false;
        }

        float surfaceRadius = ComputeBoundsCornerRadiusXZ(lake.center, lake.surfaceBounds);
        return surfaceRadius <= GetMaxAllowedGeneratedFreshwaterSurfaceRadius(terrain, lake);
    }

    private void TrimLakeCaptureRadiusToSolvedSurface(ProceduralVoxelTerrain terrain, GeneratedLake lake)
    {
        if (terrain == null || lake == null || !WaterSpatialQueryUtility.HasLakeSurfaceGeometry(lake))
        {
            return;
        }

        float solvedSurfaceRadius = ComputeBoundsCornerRadiusXZ(lake.center, lake.surfaceBounds);
        float capturePadding = Mathf.Max(lakeDynamicExpansionMeters, terrain.VoxelSizeMeters * 3f);
        lake.captureRadius = Mathf.Max(lake.radius, solvedSurfaceRadius + capturePadding);
    }

    private FreshwaterGenerationStats GenerateFreshwaterBodies(
        System.Random random,
        ProceduralVoxelTerrain terrain,
        Bounds bounds,
        int targetCount,
        Vector2 radiusRangeMeters,
        float depthMeters,
        bool isPond)
    {
        string bodyType = isPond ? "ponds" : "lakes";
        FreshwaterGenerationStats stats = new FreshwaterGenerationStats(isPond ? "Ponds" : "Lakes", targetCount, targetCount * (isPond ? 72 : 64));
        if (targetCount <= 0)
        {
            UpdateWaterGenerationProgress($"Generating {bodyType}", 1f, true);
            return stats;
        }

        int generatedCount = 0;
        int attempts = 0;
        int maxAttempts = stats.maxAttempts;
        float minimumCandidateSurfaceY = seaLevelMeters + Mathf.Max(isPond ? 0.35f : 0.7f, depthMeters * 0.6f);
        float maximumCandidateSurfaceY = bounds.max.y - Mathf.Max(1.25f, depthMeters);
        TryResolveLakeCandidateHeightRange(terrain, bounds, depthMeters, out minimumCandidateSurfaceY, out maximumCandidateSurfaceY);

        while (generatedCount < targetCount && attempts < maxAttempts)
        {
            attempts++;
            stats.attempts = attempts;
            UpdateWaterGenerationProgress(
                $"Generating {bodyType} ({generatedCount}/{targetCount}, attempt {attempts}/{maxAttempts})",
                CalculateGenerationAttemptProgress(generatedCount, targetCount, attempts, maxAttempts),
                false);

            System.Diagnostics.Stopwatch candidateStopwatch = System.Diagnostics.Stopwatch.StartNew();
            float attemptProgress = maxAttempts <= 1 ? 1f : (attempts - 1f) / (maxAttempts - 1f);
            float candidateSurfaceFloor = Mathf.Lerp(
                minimumCandidateSurfaceY,
                seaLevelMeters + Mathf.Max(isPond ? 0.2f : 0.45f, depthMeters * 0.3f),
                attemptProgress * 0.35f);
            float candidateSurfaceCeiling = Mathf.Lerp(
                maximumCandidateSurfaceY,
                bounds.max.y - Mathf.Max(0.75f, depthMeters * 0.5f),
                attemptProgress);
            float radius = NextFloat(random, radiusRangeMeters.x, radiusRangeMeters.y);
            float maxAllowedSlopeDegrees = Mathf.Lerp(isPond ? 24f : 20f, isPond ? 34f : 30f, attemptProgress);
            float inlandSamplingMargin = Mathf.Lerp(isPond ? 0.24f : 0.22f, 0.18f, attemptProgress * 0.75f);
            float minSampleX = bounds.min.x + (bounds.size.x * inlandSamplingMargin);
            float maxSampleX = bounds.max.x - (bounds.size.x * inlandSamplingMargin);
            float minSampleZ = bounds.min.z + (bounds.size.z * inlandSamplingMargin);
            float maxSampleZ = bounds.max.z - (bounds.size.z * inlandSamplingMargin);
            if (maxSampleX <= minSampleX || maxSampleZ <= minSampleZ)
            {
                candidateStopwatch.Stop();
                stats.candidateAnalysisMilliseconds += candidateStopwatch.ElapsedMilliseconds;
                stats.rejectedMissingSurface++;
                continue;
            }

            float worldX = NextFloat(random, minSampleX, maxSampleX);
            float worldZ = NextFloat(random, minSampleZ, maxSampleZ);
            if (!terrain.TryGetCachedSurfacePointWorld(worldX, worldZ, out Vector3 surfacePoint, out Vector3 surfaceNormal))
            {
                candidateStopwatch.Stop();
                stats.candidateAnalysisMilliseconds += candidateStopwatch.ElapsedMilliseconds;
                stats.rejectedMissingSurface++;
                continue;
            }

            if (surfacePoint.y <= candidateSurfaceFloor || surfacePoint.y >= candidateSurfaceCeiling)
            {
                candidateStopwatch.Stop();
                stats.candidateAnalysisMilliseconds += candidateStopwatch.ElapsedMilliseconds;
                stats.rejectedElevation++;
                continue;
            }

            float slopeSampleDistance = Mathf.Max(terrain.VoxelSizeMeters * 1.5f, radius * (isPond ? 0.2f : 0.24f));
            float slope = TryEstimateLocalSurfaceSlopeDegreesCached(terrain, surfacePoint, slopeSampleDistance, out float sampledSlope)
                ? sampledSlope
                : Vector3.Angle(surfaceNormal, Vector3.up);
            if (slope > maxAllowedSlopeDegrees)
            {
                candidateStopwatch.Stop();
                stats.candidateAnalysisMilliseconds += candidateStopwatch.ElapsedMilliseconds;
                stats.rejectedSlope++;
                continue;
            }

            Vector3 center = surfacePoint;
            if (IsTooCloseToExistingWater(center, radius * (isPond ? 1.2f : 1.8f)))
            {
                candidateStopwatch.Stop();
                stats.candidateAnalysisMilliseconds += candidateStopwatch.ElapsedMilliseconds;
                stats.rejectedSpacing++;
                continue;
            }

            if (!TrySampleLakeShorelineStatsCached(terrain, center, radius, out float minimumShoreHeight, out float averageShoreHeight))
            {
                candidateStopwatch.Stop();
                stats.candidateAnalysisMilliseconds += candidateStopwatch.ElapsedMilliseconds;
                stats.rejectedShoreline++;
                continue;
            }

            float surfaceY = Mathf.Max(
                seaLevelMeters + (isPond ? 0.35f : 0.55f),
                Mathf.Min(
                    minimumShoreHeight - (isPond ? 0.04f : 0.08f),
                    averageShoreHeight - (isPond ? 0.025f : 0.05f)));
            // Pass the already-computed minimumShoreHeight to avoid re-sampling the same 12
            // shoreline points inside TryRefineLakeSurfaceYCached.
            if (!TryRefineLakeSurfaceYCached(terrain, center, radius, surfaceY, depthMeters, isPond, carveTerrainForWater, minimumShoreHeight, out surfaceY))
            {
                candidateStopwatch.Stop();
                stats.candidateAnalysisMilliseconds += candidateStopwatch.ElapsedMilliseconds;
                stats.rejectedRefine++;
                continue;
            }

            GeneratedLake candidateLake = CreateFreshwaterCandidateSeed(center, radius, surfaceY, isPond);
            if (!PassesCachedFreshwaterBasinGuard(terrain, candidateLake, depthMeters, carveTerrainForWater))
            {
                candidateStopwatch.Stop();
                stats.candidateAnalysisMilliseconds += candidateStopwatch.ElapsedMilliseconds;
                stats.rejectedCachedBasin++;
                continue;
            }

            candidateStopwatch.Stop();
            stats.candidateAnalysisMilliseconds += candidateStopwatch.ElapsedMilliseconds;

            System.Diagnostics.Stopwatch previewSolveStopwatch = System.Diagnostics.Stopwatch.StartNew();
            if (!TryCreateLakeAtSurface(terrain, candidateLake, out GeneratedLake generatedLake))
            {
                previewSolveStopwatch.Stop();
                stats.previewSolveMilliseconds += previewSolveStopwatch.ElapsedMilliseconds;
                stats.rejectedProfile++;
                continue;
            }

            previewSolveStopwatch.Stop();
            stats.previewSolveMilliseconds += previewSolveStopwatch.ElapsedMilliseconds;

            if (carveTerrainForWater)
            {
                System.Diagnostics.Stopwatch carveStopwatch = System.Diagnostics.Stopwatch.StartNew();
                CarveLake(terrain, center, radius, surfaceY, depthMeters, isPond, false);
                carveStopwatch.Stop();
                stats.carveMilliseconds += carveStopwatch.ElapsedMilliseconds;

                System.Diagnostics.Stopwatch finalizeSolveStopwatch = System.Diagnostics.Stopwatch.StartNew();
                if (TrySolveCarvedLakeLocally(terrain, generatedLake, center, radius, surfaceY, isPond, out GeneratedLake carvedLake))
                {
                    generatedLake = carvedLake;
                }
                else
                {
                    finalizeSolveStopwatch.Stop();
                    stats.finalizeSolveMilliseconds += finalizeSolveStopwatch.ElapsedMilliseconds;

                    System.Diagnostics.Stopwatch rollbackStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    RestoreCarvedLake(terrain, center, radius, surfaceY, depthMeters, isPond);
                    rollbackStopwatch.Stop();
                    stats.rollbackMilliseconds += rollbackStopwatch.ElapsedMilliseconds;

                    LogLakeDebug($"Rejected carved freshwater candidate after local refresh failed for {WaterDebugUtility.DescribeLake(candidateLake)}.");
                    stats.rejectedProfile++;
                    continue;
                }

                finalizeSolveStopwatch.Stop();
                stats.finalizeSolveMilliseconds += finalizeSolveStopwatch.ElapsedMilliseconds;

                System.Diagnostics.Stopwatch basinMaterialStopwatch = System.Diagnostics.Stopwatch.StartNew();
                PaintFreshwaterBasinMaterials(terrain, center, radius, surfaceY, depthMeters, isPond);
                basinMaterialStopwatch.Stop();
                stats.basinMaterialMilliseconds += basinMaterialStopwatch.ElapsedMilliseconds;
            }

            generatedLakes.Add(generatedLake);
            generatedCount++;
            stats.generatedCount = generatedCount;
        }

        stats.generatedCount = generatedCount;
        stats.attempts = attempts;
        UpdateWaterGenerationProgress($"Generating {bodyType} ({generatedCount}/{targetCount})", 1f, true);

        if (generatedCount <= 0)
        {
            string ownerName = getOwnerName != null ? getOwnerName() : "Voxel Water";
            Debug.LogWarning(
                $"{ownerName} could not place any {bodyType}. Candidate elevation window was {minimumCandidateSurfaceY:F1}m to {maximumCandidateSurfaceY:F1}m with {attempts} attempts. " +
                $"Rejections: missing-surface={stats.rejectedMissingSurface}, elevation={stats.rejectedElevation}, slope={stats.rejectedSlope}, overlap={stats.rejectedSpacing}, shoreline={stats.rejectedShoreline}, refine={stats.rejectedRefine}, cached-basin={stats.rejectedCachedBasin}, profile={stats.rejectedProfile}.");
        }

        return stats;
    }

    private bool TryResolveLakeCandidateHeightRange(ProceduralVoxelTerrain terrain, Bounds bounds, float waterDepthMeters, out float minimumSurfaceY, out float maximumSurfaceY)
    {
        minimumSurfaceY = seaLevelMeters + Mathf.Max(0.7f, waterDepthMeters * 0.6f);
        maximumSurfaceY = bounds.max.y - Mathf.Max(1.25f, waterDepthMeters);
        if (terrain == null)
        {
            return false;
        }

        const int sampleGridSize = 8;
        const float inlandSampleMin = 0.2f;
        const float inlandSampleMax = 0.8f;
        List<float> sampledHeights = new List<float>(sampleGridSize * sampleGridSize);
        for (int z = 0; z < sampleGridSize; z++)
        {
            float worldZ = Mathf.Lerp(
                bounds.min.z + (bounds.size.z * inlandSampleMin),
                bounds.min.z + (bounds.size.z * inlandSampleMax),
                (z + 0.5f) / sampleGridSize);
            for (int x = 0; x < sampleGridSize; x++)
            {
                float worldX = Mathf.Lerp(
                    bounds.min.x + (bounds.size.x * inlandSampleMin),
                    bounds.min.x + (bounds.size.x * inlandSampleMax),
                    (x + 0.5f) / sampleGridSize);
                if (!terrain.TryGetCachedSurfaceHeightWorld(worldX, worldZ, out float sampledHeight))
                {
                    continue;
                }

                sampledHeights.Add(sampledHeight);
            }
        }

        if (sampledHeights.Count == 0)
        {
            return false;
        }

        sampledHeights.Sort();
        float lowerPercentileHeight = GetSortedSampleValue(sampledHeights, 0.18f);
        float upperPercentileHeight = GetSortedSampleValue(sampledHeights, 0.78f);
        minimumSurfaceY = Mathf.Max(
            seaLevelMeters + Mathf.Max(0.7f, waterDepthMeters * 0.6f),
            lowerPercentileHeight - terrain.VoxelSizeMeters);
        maximumSurfaceY = Mathf.Min(
            bounds.max.y - Mathf.Max(1.25f, waterDepthMeters),
            upperPercentileHeight + (terrain.VoxelSizeMeters * 2f));

        float minimumRange = Mathf.Max(3f, waterDepthMeters * 2f);
        if (maximumSurfaceY <= minimumSurfaceY + minimumRange)
        {
            maximumSurfaceY = Mathf.Min(
                bounds.max.y - Mathf.Max(1.25f, waterDepthMeters),
                minimumSurfaceY + minimumRange);
        }

        return maximumSurfaceY > minimumSurfaceY;
    }

    private static float GetSortedSampleValue(List<float> sortedValues, float percentile)
    {
        if (sortedValues == null || sortedValues.Count == 0)
        {
            return 0f;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        float scaledIndex = Mathf.Clamp01(percentile) * (sortedValues.Count - 1);
        int lowerIndex = Mathf.FloorToInt(scaledIndex);
        int upperIndex = Mathf.Min(sortedValues.Count - 1, lowerIndex + 1);
        float t = scaledIndex - lowerIndex;
        return Mathf.Lerp(sortedValues[lowerIndex], sortedValues[upperIndex], t);
    }

    private bool TryFindRiverSource(System.Random random, ProceduralVoxelTerrain terrain, Bounds bounds, out Vector3 source)
    {
        source = Vector3.zero;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            float worldX = NextFloat(random, Mathf.Lerp(bounds.min.x, bounds.max.x, 0.18f), Mathf.Lerp(bounds.min.x, bounds.max.x, 0.82f));
            float worldZ = NextFloat(random, Mathf.Lerp(bounds.min.z, bounds.max.z, 0.22f), Mathf.Lerp(bounds.min.z, bounds.max.z, 0.82f));
            if (!terrain.TrySampleSurfaceWorld(worldX, worldZ, out RaycastHit hit))
            {
                continue;
            }

            float normalizedHeight = bounds.size.y <= 0.0001f ? 0f : Mathf.Clamp01((hit.point.y - bounds.min.y) / bounds.size.y);
            float slope = Vector3.Angle(hit.normal, Vector3.up);
            if (normalizedHeight < riverSourceHeightRangeNormalized.x || normalizedHeight > riverSourceHeightRangeNormalized.y)
            {
                continue;
            }

            if (slope < 2f || slope > 24f)
            {
                continue;
            }

            source = hit.point;
            return true;
        }

        return false;
    }

    private Vector3 CreateRiverMouth(System.Random random, Bounds bounds)
    {
        float edgeSelector = NextFloat(random, 0f, 4f);
        float x;
        float z;

        if (edgeSelector < 1f)
        {
            x = bounds.min.x;
            z = Mathf.Lerp(bounds.min.z, bounds.max.z, NextFloat(random, 0.12f, 0.88f));
        }
        else if (edgeSelector < 2f)
        {
            x = bounds.max.x;
            z = Mathf.Lerp(bounds.min.z, bounds.max.z, NextFloat(random, 0.12f, 0.88f));
        }
        else if (edgeSelector < 3f)
        {
            x = Mathf.Lerp(bounds.min.x, bounds.max.x, NextFloat(random, 0.12f, 0.88f));
            z = bounds.min.z;
        }
        else
        {
            x = Mathf.Lerp(bounds.min.x, bounds.max.x, NextFloat(random, 0.12f, 0.88f));
            z = bounds.max.z;
        }

        return new Vector3(x, seaLevelMeters, z);
    }

    private List<Vector3> BuildRiverPath(System.Random random, ProceduralVoxelTerrain terrain, Bounds bounds, Vector3 source, Vector3 mouth, float width)
    {
        List<Vector3> samples = new List<Vector3>(Mathf.Max(8, riverSampleCount))
        {
            source
        };

        float stepLength = Mathf.Max(riverCarveStepMeters, width * 0.82f);
        Vector3 current = source;
        Vector3 currentDirection = Vector3.ProjectOnPlane(mouth - source, Vector3.up).normalized;
        if (currentDirection.sqrMagnitude < 0.0001f)
        {
            currentDirection = Vector3.forward;
        }

        int maxSteps = Mathf.Max(8, riverSampleCount);
        for (int step = 1; step < maxSteps; step++)
        {
            float remainingDistance = Vector3.Distance(
                new Vector3(current.x, 0f, current.z),
                new Vector3(mouth.x, 0f, mouth.z));
            if (remainingDistance <= stepLength * 1.35f || current.y <= seaLevelMeters + 0.4f)
            {
                break;
            }

            if (!TryAdvanceRiverPoint(terrain, bounds, samples, current, mouth, currentDirection, stepLength, out Vector3 nextPoint))
            {
                break;
            }

            currentDirection = Vector3.ProjectOnPlane(nextPoint - current, Vector3.up).normalized;
            if (currentDirection.sqrMagnitude < 0.0001f)
            {
                currentDirection = Vector3.ProjectOnPlane(mouth - nextPoint, Vector3.up).normalized;
            }

            current = nextPoint;
            samples.Add(current);
        }

        float mouthX = Mathf.Clamp(mouth.x, bounds.min.x + 1f, bounds.max.x - 1f);
        float mouthZ = Mathf.Clamp(mouth.z, bounds.min.z + 1f, bounds.max.z - 1f);
        if (terrain.TrySampleSurfaceWorld(mouthX, mouthZ, out RaycastHit mouthHit) &&
            Vector3.Distance(samples[samples.Count - 1], mouthHit.point) > stepLength * 0.45f)
        {
            samples.Add(mouthHit.point);
        }

        return samples;
    }

    private bool TryAdvanceRiverPoint(
        ProceduralVoxelTerrain terrain,
        Bounds bounds,
        IReadOnlyList<Vector3> currentPath,
        Vector3 current,
        Vector3 mouth,
        Vector3 currentDirection,
        float stepLength,
        out Vector3 nextPoint)
    {
        nextPoint = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        float[] candidateAngles = { 0f, -25f, 25f, -50f, 50f, -80f, 80f };
        Vector3 toMouth = Vector3.ProjectOnPlane(mouth - current, Vector3.up).normalized;
        if (toMouth.sqrMagnitude < 0.0001f)
        {
            toMouth = currentDirection.sqrMagnitude > 0.0001f ? currentDirection : Vector3.forward;
        }

        for (int i = 0; i < candidateAngles.Length; i++)
        {
            Vector3 baseDirection = currentDirection.sqrMagnitude > 0.0001f ? currentDirection : toMouth;
            Vector3 candidateDirection = Quaternion.AngleAxis(candidateAngles[i], Vector3.up) * baseDirection;
            candidateDirection = Vector3.ProjectOnPlane((candidateDirection * 0.65f) + (toMouth * 0.35f), Vector3.up).normalized;
            if (candidateDirection.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Vector3 candidatePoint = current + (candidateDirection * stepLength);
            if (candidatePoint.x <= bounds.min.x + 0.5f ||
                candidatePoint.x >= bounds.max.x - 0.5f ||
                candidatePoint.z <= bounds.min.z + 0.5f ||
                candidatePoint.z >= bounds.max.z - 0.5f)
            {
                continue;
            }

            if (!terrain.TrySampleSurfaceWorld(candidatePoint.x, candidatePoint.z, out RaycastHit hit))
            {
                continue;
            }

            if (IsTooCloseToRiverPath(hit.point, currentPath, stepLength * 0.55f))
            {
                continue;
            }

            float remainingDistance = Vector3.Distance(
                new Vector3(current.x, 0f, current.z),
                new Vector3(mouth.x, 0f, mouth.z));
            float candidateRemainingDistance = Vector3.Distance(
                new Vector3(hit.point.x, 0f, hit.point.z),
                new Vector3(mouth.x, 0f, mouth.z));
            float progress = remainingDistance - candidateRemainingDistance;
            float heightDrop = current.y - hit.point.y;
            float uphillPenalty = Mathf.Max(0f, hit.point.y - current.y);
            float slopePenalty = Mathf.Max(0f, Vector3.Angle(hit.normal, Vector3.up) - 35f) * 0.03f;
            float directionality = Vector3.Dot(candidateDirection, toMouth);
            float score = (heightDrop * 2.8f) + (progress * 0.55f) + (directionality * 1.1f) - (uphillPenalty * 4.5f) - slopePenalty;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            nextPoint = hit.point;
        }

        return bestScore > float.NegativeInfinity;
    }

    private static bool IsTooCloseToRiverPath(Vector3 point, IReadOnlyList<Vector3> currentPath, float minimumSpacingMeters)
    {
        if (currentPath == null || currentPath.Count <= 1 || minimumSpacingMeters <= 0f)
        {
            return false;
        }

        float minimumSpacingSquared = minimumSpacingMeters * minimumSpacingMeters;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Vector3 planarDelta = Vector3.ProjectOnPlane(point - currentPath[i], Vector3.up);
            if (planarDelta.sqrMagnitude < minimumSpacingSquared)
            {
                return true;
            }
        }

        return false;
    }

    private GeneratedRiver BuildRenderableRiver(IReadOnlyList<Vector3> waterPath, IReadOnlyList<float> widths, float baseWidth, ProceduralVoxelTerrain terrain)
    {
        if (waterPath == null || widths == null || waterPath.Count < 2 || waterPath.Count != widths.Count)
        {
            return null;
        }

        GeneratedRiver river = new GeneratedRiver
        {
            baseWidth = baseWidth
        };
        river.waterPath.AddRange(waterPath);
        river.widthProfile.AddRange(widths);
        PopulateRenderableRiverPath(river);
        updateRiverInfluenceBounds?.Invoke(terrain, river);
        return river.points.Count >= 2 ? river : null;
    }

    private bool TrySampleLakeShorelineStats(ProceduralVoxelTerrain terrain, Vector3 center, float radiusMeters, out float minimumHeight, out float averageHeight)
    {
        minimumHeight = float.PositiveInfinity;
        averageHeight = 0f;
        int sampleCount = 12;
        int validSamples = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / sampleCount;
            Vector3 shorelinePoint = center + new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * (radiusMeters * 0.92f);
            if (!terrain.TryGetCachedSurfaceHeightWorld(shorelinePoint.x, shorelinePoint.z, out float shorelineHeight))
            {
                return false;
            }

            minimumHeight = Mathf.Min(minimumHeight, shorelineHeight);
            averageHeight += shorelineHeight;
            validSamples++;
        }

        if (validSamples <= 0)
        {
            return false;
        }

        averageHeight /= validSamples;
        return true;
    }

    private bool TrySampleLakeShorelineStatsCached(ProceduralVoxelTerrain terrain, Vector3 center, float radiusMeters, out float minimumHeight, out float averageHeight)
    {
        minimumHeight = float.PositiveInfinity;
        averageHeight = 0f;
        int sampleCount = 12;
        int validSamples = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / sampleCount;
            Vector3 shorelinePoint = center + new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * (radiusMeters * 0.92f);
            if (!terrain.TryGetCachedSurfaceHeightWorld(shorelinePoint.x, shorelinePoint.z, out float shorelineHeight))
            {
                return false;
            }

            minimumHeight = Mathf.Min(minimumHeight, shorelineHeight);
            averageHeight += shorelineHeight;
            validSamples++;
        }

        if (validSamples <= 0)
        {
            return false;
        }

        averageHeight /= validSamples;
        return true;
    }

    private bool TryEstimateLocalSurfaceSlopeDegreesCached(ProceduralVoxelTerrain terrain, Vector3 center, float sampleDistance, out float slopeDegrees)
    {
        slopeDegrees = 0f;
        if (terrain == null ||
            sampleDistance <= 0.0001f ||
            !terrain.TryGetCachedSurfaceHeightWorld(center.x, center.z, out float centerHeight))
        {
            return false;
        }

        Vector2[] sampleDirections =
        {
            Vector2.right,
            Vector2.left,
            Vector2.up,
            Vector2.down
        };

        float maxGradient = 0f;
        float gradientSum = 0f;
        int validSamples = 0;
        for (int i = 0; i < sampleDirections.Length; i++)
        {
            Vector2 direction = sampleDirections[i];
            Vector3 samplePoint = center + new Vector3(direction.x, 0f, direction.y) * sampleDistance;
            if (!terrain.TryGetCachedSurfaceHeightWorld(samplePoint.x, samplePoint.z, out float sampleHeight))
            {
                continue;
            }

            float gradient = Mathf.Abs(sampleHeight - centerHeight) / sampleDistance;
            maxGradient = Mathf.Max(maxGradient, gradient);
            gradientSum += gradient;
            validSamples++;
        }

        if (validSamples <= 0)
        {
            return false;
        }

        float averageGradient = gradientSum / validSamples;
        float effectiveGradient = Mathf.Lerp(averageGradient, maxGradient, 0.35f);
        slopeDegrees = Mathf.Atan(effectiveGradient) * Mathf.Rad2Deg;
        return true;
    }

    private bool TryRefineLakeSurfaceY(
        ProceduralVoxelTerrain terrain,
        Vector3 center,
        float radiusMeters,
        float proposedSurfaceY,
        float depthMeters,
        bool isPond,
        bool willCarveTerrain,
        out float refinedSurfaceY)
    {
        refinedSurfaceY = proposedSurfaceY;
        if (!TrySampleLakeShorelineStats(terrain, center, radiusMeters, out float minimumShoreHeight, out _) ||
            !terrain.TrySampleSurfaceWorld(center.x, center.z, out RaycastHit centerHit))
        {
            return false;
        }

        float upperBound = minimumShoreHeight - (isPond ? 0.04f : 0.06f);
        float lowerBound = willCarveTerrain
            ? seaLevelMeters + (isPond ? 0.25f : 0.45f)
            : Mathf.Max(
                seaLevelMeters + (isPond ? 0.35f : 0.55f),
                centerHit.point.y + Mathf.Max(0.24f, depthMeters * 0.52f));
        if (upperBound <= lowerBound)
        {
            if (willCarveTerrain)
            {
                float emergencyMinimumSurface = seaLevelMeters + (isPond ? 0.18f : 0.32f);
                if (upperBound > emergencyMinimumSurface)
                {
                    refinedSurfaceY = upperBound;
                    return true;
                }
            }

            return false;
        }

        refinedSurfaceY = Mathf.Clamp(proposedSurfaceY, lowerBound, upperBound);
        return true;
    }

    private bool TryRefineLakeSurfaceYCached(
        ProceduralVoxelTerrain terrain,
        Vector3 center,
        float radiusMeters,
        float proposedSurfaceY,
        float depthMeters,
        bool isPond,
        bool willCarveTerrain,
        out float refinedSurfaceY)
    {
        refinedSurfaceY = proposedSurfaceY;
        if (!TrySampleLakeShorelineStatsCached(terrain, center, radiusMeters, out float minimumShoreHeight, out _) ||
            !terrain.TryGetCachedSurfaceHeightWorld(center.x, center.z, out float centerHeight))
        {
            return false;
        }

        float upperBound = minimumShoreHeight - (isPond ? 0.04f : 0.06f);
        float lowerBound = willCarveTerrain
            ? seaLevelMeters + (isPond ? 0.25f : 0.45f)
            : Mathf.Max(
                seaLevelMeters + (isPond ? 0.35f : 0.55f),
                centerHeight + Mathf.Max(0.24f, depthMeters * 0.52f));
        if (upperBound <= lowerBound)
        {
            if (willCarveTerrain)
            {
                float emergencyMinimumSurface = seaLevelMeters + (isPond ? 0.18f : 0.32f);
                if (upperBound > emergencyMinimumSurface)
                {
                    refinedSurfaceY = upperBound;
                    return true;
                }
            }

            return false;
        }

        refinedSurfaceY = Mathf.Clamp(proposedSurfaceY, lowerBound, upperBound);
        return true;
    }

    // Overload that accepts the pre-computed minimum shore height so the caller does not need to
    // re-sample the 12 shoreline points when it has already done so.  Saves ~12 cached terrain
    // lookups per candidate that reaches the refine step.
    private bool TryRefineLakeSurfaceYCached(
        ProceduralVoxelTerrain terrain,
        Vector3 center,
        float radiusMeters,
        float proposedSurfaceY,
        float depthMeters,
        bool isPond,
        bool willCarveTerrain,
        float precomputedMinimumShoreHeight,
        out float refinedSurfaceY)
    {
        refinedSurfaceY = proposedSurfaceY;
        if (!terrain.TryGetCachedSurfaceHeightWorld(center.x, center.z, out float centerHeight))
        {
            return false;
        }

        float minimumShoreHeight = precomputedMinimumShoreHeight;
        float upperBound = minimumShoreHeight - (isPond ? 0.04f : 0.06f);
        float lowerBound = willCarveTerrain
            ? seaLevelMeters + (isPond ? 0.25f : 0.45f)
            : Mathf.Max(
                seaLevelMeters + (isPond ? 0.35f : 0.55f),
                centerHeight + Mathf.Max(0.24f, depthMeters * 0.52f));
        if (upperBound <= lowerBound)
        {
            if (willCarveTerrain)
            {
                float emergencyMinimumSurface = seaLevelMeters + (isPond ? 0.18f : 0.32f);
                if (upperBound > emergencyMinimumSurface)
                {
                    refinedSurfaceY = upperBound;
                    return true;
                }
            }

            return false;
        }

        refinedSurfaceY = Mathf.Clamp(proposedSurfaceY, lowerBound, upperBound);
        return true;
    }

    private bool IsTooCloseToExistingWater(Vector3 point, float minimumDistance)
    {
        for (int i = 0; i < generatedLakes.Count; i++)
        {
            if (Vector3.Distance(point, generatedLakes[i].center) <= generatedLakes[i].radius + minimumDistance)
            {
                return true;
            }
        }

        for (int i = 0; i < generatedRiverSegments.Count; i++)
        {
            if (DistanceToSegmentXZ(point, generatedRiverSegments[i].start, generatedRiverSegments[i].end) <= (generatedRiverSegments[i].width * 0.5f) + minimumDistance)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateWaterGenerationProgress(string status, float currentStepProgress01, bool forceEditorRefresh)
    {
        updateGenerationProgress?.Invoke(status, currentStepProgress01, forceEditorRefresh);
    }

    private void LogLakeDebug(string message)
    {
        logDebug?.Invoke(message);
    }

    private void CarveLake(ProceduralVoxelTerrain terrain, Vector3 center, float radiusMeters, float surfaceY, float depthMeters, bool isPond, bool paintBasinMaterials = true)
    {
        WaterTerrainCarver.CarveLake(terrain, center, radiusMeters, surfaceY, depthMeters, isPond, paintBasinMaterials);
    }

    private void RestoreCarvedLake(ProceduralVoxelTerrain terrain, Vector3 center, float radiusMeters, float surfaceY, float depthMeters, bool isPond)
    {
        WaterTerrainCarver.RestoreCarvedLake(terrain, center, radiusMeters, surfaceY, depthMeters, isPond);
    }

    private void BuildMergeTestRidge(ProceduralVoxelTerrain terrain, Vector3 firstCenter, Vector3 secondCenter, float surfaceY, float shorelineGapMeters, float ridgeHeightMeters)
    {
        WaterTerrainCarver.BuildMergeTestRidge(terrain, firstCenter, secondCenter, surfaceY, shorelineGapMeters, ridgeHeightMeters);
    }

    private void CarveRiver(ProceduralVoxelTerrain terrain, IReadOnlyList<Vector3> samples, float widthMeters, float depthMeters)
    {
        WaterTerrainCarver.CarveRiver(terrain, samples, widthMeters, depthMeters, riverCarveStepMeters);
    }

    private void PaintFreshwaterBasinMaterials(ProceduralVoxelTerrain terrain, Vector3 center, float radiusMeters, float surfaceY, float depthMeters, bool isPond)
    {
        WaterTerrainCarver.PaintFreshwaterBasinMaterials(terrain, center, radiusMeters, surfaceY, depthMeters, isPond);
    }
}
