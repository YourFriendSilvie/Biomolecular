using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class ProceduralVoxelTerrain
{
private void BeginTerrainGeneration(TerrainGenerationOperation operation, Action<bool> onComplete)
    {

        ResetRuntimeStreamingStateForGeneration();
        surfaceHeightPrepassReady = false;
        activeTerrainGenerationOperation = operation;
        activeTerrainGenerationCoroutine = null;

        if (onComplete != null)
        {
            terrainGenerationCompletionCallbacks.Add(onComplete);
        }
    }
    private IEnumerator RunTerrainGenerationAsync(TerrainGenerationOperation operation)
    {
        while (activeTerrainGenerationOperation == operation && !operation.IsDone)
        {
            AdvanceTerrainGenerationWithinBudget(operation);
            if (activeTerrainGenerationOperation != operation || operation.IsDone)
            {
                yield break;
            }

            yield return null;
        }
    }

    private void AdvanceTerrainGenerationWithinBudget(TerrainGenerationOperation operation)
    {
        if (operation == null || activeTerrainGenerationOperation != operation)
        {
            return;
        }

        float budgetMilliseconds = Mathf.Max(0.25f, asyncGenerationFrameBudgetMilliseconds);
        System.Diagnostics.Stopwatch frameStopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            do
            {
                operation.Step();
            }
            while (activeTerrainGenerationOperation == operation &&
                   !operation.IsDone &&
                   frameStopwatch.Elapsed.TotalMilliseconds < budgetMilliseconds);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            FinishTerrainGeneration(operation, false);
            return;
        }

        if (activeTerrainGenerationOperation == operation && operation.IsDone)
        {
            FinishTerrainGeneration(operation, operation.Success);
        }
    }

    private void FinishTerrainGeneration(TerrainGenerationOperation operation, bool success)
    {
        if (activeTerrainGenerationOperation != operation)
        {
            return;
        }

        activeTerrainGenerationCoroutine = null;
#if UNITY_EDITOR
        if (activeEditorTerrainGenerationDriver != null)
        {
            activeEditorTerrainGenerationDriver.Dispose();
            activeEditorTerrainGenerationDriver = null;
        }
#endif

        if (!success)
        {
            ClearGeneratedTerrainForRegeneration();
        }

        activeTerrainGenerationOperation = null;
        operation?.Dispose();

        if (success)
        {
            LogGenerationTimingsSummary(operation);
        }

        TerrainGenerationCompleted?.Invoke(success);
        InvokeTerrainGenerationCallbacks(success);
    }

    private void CancelTerrainGenerationInternal(bool notifyCallbacks)
    {
        TerrainGenerationOperation operation = activeTerrainGenerationOperation;
        bool hadActiveGeneration = operation != null;

        if (activeTerrainGenerationCoroutine != null)
        {
            StopCoroutine(activeTerrainGenerationCoroutine);
            activeTerrainGenerationCoroutine = null;
        }

#if UNITY_EDITOR
        if (activeEditorTerrainGenerationDriver != null)
        {
            activeEditorTerrainGenerationDriver.Dispose();
            activeEditorTerrainGenerationDriver = null;
        }
#endif

        activeTerrainGenerationOperation = null;
        operation?.Dispose();

        if (hadActiveGeneration)
        {
            ClearGeneratedTerrainForRegeneration();
            if (notifyCallbacks)
            {
                TerrainGenerationCompleted?.Invoke(false);
                InvokeTerrainGenerationCallbacks(false);
                return;
            }
        }

        if (!notifyCallbacks)
        {
            terrainGenerationCompletionCallbacks.Clear();
        }
    }

    private void InvokeTerrainGenerationCallbacks(bool success)
    {
        if (terrainGenerationCompletionCallbacks.Count == 0)
        {
            return;
        }

        Action<bool>[] callbacks = terrainGenerationCompletionCallbacks.ToArray();
        terrainGenerationCompletionCallbacks.Clear();
        for (int i = 0; i < callbacks.Length; i++)
        {
            callbacks[i]?.Invoke(success);
        }
    }

    private void UploadTerrainProfileTextures()
    {
        // No-op: profile textures are retired. Terrain color is baked into vertex.color
        // by ChunkMeshBuilder.ComputeVertexColors using columnProfilePrepass directly.
    }

    private void LogGenerationTimingsSummary(TerrainGenerationOperation operation)
    {
        if (!logGenerationTimings || operation == null)
        {
            return;
        }

        Debug.Log(
            $"[{nameof(ProceduralVoxelTerrain)}:{name}] GenerateTerrain timings: prepass={operation.PrepassMilliseconds}ms, density={operation.DensityMilliseconds}ms, materials={operation.MaterialMilliseconds}ms, chunk-objects={operation.ChunkObjectMilliseconds}ms, meshing={operation.MeshMilliseconds}ms, total={operation.TotalMilliseconds}ms.",
            this);
    }

    private TerrainGenerationNumericConfig BuildTerrainGenerationNumericConfig()
    {
        return new TerrainGenerationNumericConfig
        {
            totalSamplesX = TotalSamplesX,
            totalSamplesY = TotalSamplesY,
            totalSamplesZ = TotalSamplesZ,
            voxelSizeMeters = voxelSizeMeters,
            baseSurfaceHeightMeters = baseSurfaceHeightMeters,
            seaLevelMeters = seaLevelMeters,
            surfaceNoiseScaleMeters = surfaceNoiseScaleMeters,
            surfaceNoiseOctaves = surfaceNoiseOctaves,
            surfaceNoisePersistence = surfaceNoisePersistence,
            surfaceNoiseLacunarity = surfaceNoiseLacunarity,
            surfaceAmplitudeMeters = surfaceAmplitudeMeters,
            ridgeNoiseScaleMeters = ridgeNoiseScaleMeters,
            ridgeAmplitudeMeters = ridgeAmplitudeMeters,
            detailNoiseScaleMeters = detailNoiseScaleMeters,
            detailAmplitudeMeters = detailAmplitudeMeters,
            caveNoiseScaleMeters = caveNoiseScaleMeters,
            caveNoiseThreshold = caveNoiseThreshold,
            caveCarveStrengthMeters = caveCarveStrengthMeters,
            caveStartDepthMeters = caveStartDepthMeters,
            shapeAsIsland = shapeAsIsland,
            islandCoreRadiusNormalized = islandCoreRadiusNormalized,
            coastalShelfWidthNormalized = coastalShelfWidthNormalized,
            islandShapeNoiseScale = islandShapeNoiseScale,
            islandShapeNoiseStrength = islandShapeNoiseStrength,
            beachHeightMeters = beachHeightMeters,
            oceanFloorDepthMeters = oceanFloorDepthMeters,
            oceanFloorVariationMeters = oceanFloorVariationMeters,
            oceanFloorNoiseScaleMeters = oceanFloorNoiseScaleMeters,
            materialBoundaryNoiseScale = materialBoundaryNoiseScale,
            materialBoundaryNoiseAmplitude = materialBoundaryNoiseAmplitude,
            materialBeachBoundaryNoiseAmplitude = materialBeachBoundaryNoiseAmplitude,
            materialBeachBoundaryNoiseScale = materialBeachBoundaryNoiseScale,
            totalWorldSize = TotalWorldSize,
            domainWarpScaleMeters = domainWarpScaleMeters,
            domainWarpStrengthMeters = domainWarpStrengthMeters,
            mountainRidgeScaleMeters = mountainRidgeScaleMeters,
            mountainRidgeAmplitudeMeters = mountainRidgeAmplitudeMeters,
            mountainBaseHeightMeters = mountainBaseHeightMeters,
            mountainBlendRangeMeters = mountainBlendRangeMeters
        };
    }

    internal TerrainGenerationSettings BuildTerrainGenerationSettings()
    {
        return new TerrainGenerationSettings
        {
            totalWorldSize                  = TotalWorldSize,
            voxelSizeMeters                 = voxelSizeMeters,
            baseSurfaceHeightMeters         = baseSurfaceHeightMeters,
            seaLevelMeters                  = seaLevelMeters,
            surfaceNoiseScaleMeters         = surfaceNoiseScaleMeters,
            surfaceNoiseOctaves             = surfaceNoiseOctaves,
            surfaceNoisePersistence         = surfaceNoisePersistence,
            surfaceNoiseLacunarity          = surfaceNoiseLacunarity,
            surfaceAmplitudeMeters          = surfaceAmplitudeMeters,
            ridgeNoiseScaleMeters           = ridgeNoiseScaleMeters,
            ridgeAmplitudeMeters            = ridgeAmplitudeMeters,
            detailNoiseScaleMeters          = detailNoiseScaleMeters,
            detailAmplitudeMeters           = detailAmplitudeMeters,
            caveNoiseScaleMeters            = caveNoiseScaleMeters,
            caveNoiseThreshold              = caveNoiseThreshold,
            caveCarveStrengthMeters         = caveCarveStrengthMeters,
            caveStartDepthMeters            = caveStartDepthMeters,
            shapeAsIsland                   = (byte)(shapeAsIsland ? 1 : 0),
            islandCoreRadiusNormalized      = islandCoreRadiusNormalized,
            coastalShelfWidthNormalized     = coastalShelfWidthNormalized,
            islandShapeNoiseScale           = islandShapeNoiseScale,
            islandShapeNoiseStrength        = islandShapeNoiseStrength,
            beachHeightMeters               = beachHeightMeters,
            oceanFloorDepthMeters           = oceanFloorDepthMeters,
            oceanFloorVariationMeters       = oceanFloorVariationMeters,
            oceanFloorNoiseScaleMeters      = oceanFloorNoiseScaleMeters
        };
    }

    private void BuildTerrainPrepass(GenerationContext context)
    {
        surfaceHeightPrepassReady = false;
        surfaceHeightPrepass = new float[TotalSamplesX * TotalSamplesZ];
        for (int z = 0; z < TotalSamplesZ; z++)
        {
            float localZ = z * voxelSizeMeters;
            for (int x = 0; x < TotalSamplesX; x++)
            {
                float localX = x * voxelSizeMeters;
                surfaceHeightPrepass[GetSurfacePrepassIndex(x, z)] = EvaluateSurfaceHeight(localX, localZ, context);
            }
        }
        surfaceHeightPrepassReady = true;

        columnProfilePrepass = new ColumnMaterialProfile[TotalCellsX * TotalCellsZ];
        for (int z = 0; z < TotalCellsZ; z++)
        {
            float localZ = (z + 0.5f) * voxelSizeMeters;
            for (int x = 0; x < TotalCellsX; x++)
            {
                float localX = (x + 0.5f) * voxelSizeMeters;
                float surfaceHeight = SampleSurfaceHeightPrepass(localX, localZ);
                columnProfilePrepass[GetColumnPrepassIndex(x, z)] = BuildColumnMaterialProfile(localX, localZ, surfaceHeight, context);
            }
        }
    }

    private int GetColumnPrepassIndex(int cellX, int cellZ)
        => VoxelDataStore.GetColumnPrepassIndex(cellX, cellZ, TotalCellsX);

    private float GetSurfaceHeightFromPrepass(int sampleX, int sampleZ)
    {
        return surfaceHeightPrepass[GetSurfacePrepassIndex(sampleX, sampleZ)];
    }

    private float SampleSurfaceHeightPrepass(float localX, float localZ)
    {
        if (surfaceHeightPrepass == null || surfaceHeightPrepass.Length == 0)
        {
            return 0f;
        }

        float sampleX = Mathf.Clamp(localX / voxelSizeMeters, 0f, TotalSamplesX - 1f);
        float sampleZ = Mathf.Clamp(localZ / voxelSizeMeters, 0f, TotalSamplesZ - 1f);
        int minX = Mathf.Clamp(Mathf.FloorToInt(sampleX), 0, TotalSamplesX - 1);
        int maxX = Mathf.Clamp(minX + 1, 0, TotalSamplesX - 1);
        int minZ = Mathf.Clamp(Mathf.FloorToInt(sampleZ), 0, TotalSamplesZ - 1);
        int maxZ = Mathf.Clamp(minZ + 1, 0, TotalSamplesZ - 1);
        float blendX = sampleX - minX;
        float blendZ = sampleZ - minZ;

        float bottom = Mathf.Lerp(GetSurfaceHeightFromPrepass(minX, minZ), GetSurfaceHeightFromPrepass(maxX, minZ), blendX);
        float top = Mathf.Lerp(GetSurfaceHeightFromPrepass(minX, maxZ), GetSurfaceHeightFromPrepass(maxX, maxZ), blendX);
        return Mathf.Lerp(bottom, top, blendZ);
    }

    private Vector3 EvaluateSurfaceNormalPrepassLocal(float localX, float localZ)
    {
        float sampleDelta = Mathf.Max(voxelSizeMeters, 0.25f);
        float left = SampleSurfaceHeightPrepass(localX - sampleDelta, localZ);
        float right = SampleSurfaceHeightPrepass(localX + sampleDelta, localZ);
        float back = SampleSurfaceHeightPrepass(localX, localZ - sampleDelta);
        float forward = SampleSurfaceHeightPrepass(localX, localZ + sampleDelta);
        Vector3 tangentX = new Vector3(sampleDelta * 2f, right - left, 0f);
        Vector3 tangentZ = new Vector3(0f, forward - back, sampleDelta * 2f);
        Vector3 normal = Vector3.Cross(tangentZ, tangentX).normalized;
        return normal.y < 0f ? -normal : normal;
    }

    private void PopulateDensityField(GenerationContext context)
    {
        for (int z = 0; z < TotalSamplesZ; z++)
        {
            float localZ = z * voxelSizeMeters;
            for (int x = 0; x < TotalSamplesX; x++)
            {
                float localX = x * voxelSizeMeters;
                float surfaceHeight = surfaceHeightPrepass != null && surfaceHeightPrepass.Length > 0
                    ? GetSurfaceHeightFromPrepass(x, z)
                    : EvaluateSurfaceHeight(localX, localZ, context);
                for (int y = 0; y < TotalSamplesY; y++)
                {
                    float localY = y * voxelSizeMeters;
                    densitySamples[GetSampleIndex(x, y, z)] = EvaluateDensity(localX, localY, localZ, surfaceHeight, context);
                }
            }
        }
    }

    private void PopulateCellMaterials(GenerationContext context)
    {
        for (int z = 0; z < TotalCellsZ; z++)
        {
            float localZ = (z + 0.5f) * voxelSizeMeters;
            for (int x = 0; x < TotalCellsX; x++)
            {
                float localX = (x + 0.5f) * voxelSizeMeters;
                ColumnMaterialProfile columnProfile =
                    columnProfilePrepass != null && columnProfilePrepass.Length > 0
                        ? columnProfilePrepass[GetColumnPrepassIndex(x, z)]
                        : BuildColumnMaterialProfile(localX, localZ, EvaluateSurfaceHeight(localX, localZ, context), context);
                for (int y = 0; y < TotalCellsY; y++)
                {
                    float localY = (y + 0.5f) * voxelSizeMeters;
                    cellMaterialIndices[GetCellIndex(x, y, z)] = DetermineCellMaterialIndex(localX, localY, localZ, context, columnProfile);
                }
            }
        }
    }

    private float EvaluateDensity(float localX, float localY, float localZ, float surfaceHeight, GenerationContext context)
    {
        float density = surfaceHeight - localY;

        float depthBelowSurface = surfaceHeight - localY;
        if (depthBelowSurface > caveStartDepthMeters)
        {
            float caveNoise = VoxelTerrainGenerator.EvaluatePerlin3D(
                (localX + context.caveOffsetX) / caveNoiseScaleMeters,
                (localY + context.caveOffsetY) / caveNoiseScaleMeters,
                (localZ + context.caveOffsetZ) / caveNoiseScaleMeters);
            if (caveNoise > caveNoiseThreshold)
            {
                float carveAmount = (caveNoise - caveNoiseThreshold) / Mathf.Max(0.0001f, 1f - caveNoiseThreshold);
                density -= carveAmount * caveCarveStrengthMeters;
            }
        }

        return density;
    }

    private float EvaluateSurfaceHeight(float localX, float localZ, GenerationContext context)
    {
        // Domain warp: perturb XZ before fractal noise for organic, meander-free ridges.
        float warpedX = localX;
        float warpedZ = localZ;
        if (domainWarpStrengthMeters > 0f && domainWarpScaleMeters > 0.0001f)
        {
            float wx = Mathf.PerlinNoise(
                (localX + context.domainWarpOffsetX) / domainWarpScaleMeters,
                (localZ + context.domainWarpOffsetZ) / domainWarpScaleMeters);
            float wz = Mathf.PerlinNoise(
                (localX + context.domainWarpOffsetX + 5.2f) / domainWarpScaleMeters,
                (localZ + context.domainWarpOffsetZ + 1.3f) / domainWarpScaleMeters);
            warpedX = localX + (wx * 2f - 1f) * domainWarpStrengthMeters;
            warpedZ = localZ + (wz * 2f - 1f) * domainWarpStrengthMeters;
        }

        float fractalNoise = VoxelTerrainGenerator.EvaluateFractalNoise(
            (warpedX + context.surfaceOffsetX) / surfaceNoiseScaleMeters,
            (warpedZ + context.surfaceOffsetZ) / surfaceNoiseScaleMeters,
            surfaceNoiseOctaves,
            surfaceNoisePersistence,
            surfaceNoiseLacunarity);
        float ridgeNoise = Mathf.PerlinNoise(
            (warpedX + context.ridgeOffsetX) / ridgeNoiseScaleMeters,
            (warpedZ + context.ridgeOffsetZ) / ridgeNoiseScaleMeters);
        ridgeNoise = 1f - Mathf.Abs((ridgeNoise * 2f) - 1f);
        ridgeNoise *= ridgeNoise;
        float detailNoise = Mathf.PerlinNoise(
            (warpedX + context.detailOffsetX) / detailNoiseScaleMeters,
            (warpedZ + context.detailOffsetZ) / detailNoiseScaleMeters);
        detailNoise = (detailNoise - 0.5f) * 2f;

        float landHeight = baseSurfaceHeightMeters
            + ((fractalNoise - 0.5f) * 2f * surfaceAmplitudeMeters)
            + (ridgeNoise * ridgeAmplitudeMeters)
            + (detailNoise * detailAmplitudeMeters);

        float minLandHeight = seaLevelMeters + beachHeightMeters;
        landHeight = Mathf.Max(landHeight, minLandHeight);

        if (!shapeAsIsland)
        {
            return Mathf.Clamp(landHeight, 0f, TotalWorldSize.y - voxelSizeMeters);
        }

        float islandDistance = VoxelTerrainGenerator.EvaluateIslandDistanceShaped(localX, localZ, TotalWorldSize, islandShapeNoiseScale, islandShapeNoiseStrength, context.islandShapeOffsetX, context.islandShapeOffsetZ);
        float shoreBlendStart = islandCoreRadiusNormalized;
        float shelfBlend = VoxelTerrainGenerator.SmoothStep01((islandDistance - shoreBlendStart) / Mathf.Max(0.0001f, coastalShelfWidthNormalized));
        float shorelineTargetHeight = seaLevelMeters + beachHeightMeters;
        float shelfHeight = Mathf.Lerp(landHeight, shorelineTargetHeight, shelfBlend);

        float oceanNoise = Mathf.PerlinNoise(
            (localX + context.oceanFloorOffsetX) / oceanFloorNoiseScaleMeters,
            (localZ + context.oceanFloorOffsetZ) / oceanFloorNoiseScaleMeters);
        float oceanFloorHeight = Mathf.Max(
            0f,
            seaLevelMeters
            - oceanFloorDepthMeters
            + ((oceanNoise - 0.5f) * 2f * oceanFloorVariationMeters));
        float oceanBlend = VoxelTerrainGenerator.SmoothStep01(
            (islandDistance - (islandCoreRadiusNormalized + coastalShelfWidthNormalized))
            / Mathf.Max(0.0001f, 1f - (islandCoreRadiusNormalized + coastalShelfWidthNormalized)));
        float worldHeight = Mathf.Lerp(shelfHeight, oceanFloorHeight, oceanBlend);
        return Mathf.Clamp(worldHeight, 0f, TotalWorldSize.y - voxelSizeMeters);
    }

    private byte DetermineCellMaterialIndex(float localX, float localY, float localZ, GenerationContext context, ColumnMaterialProfile columnProfile)
    {
        float depthBelowSurface = columnProfile.surfaceHeight - localY;
        float normalizedHeight = TotalWorldSize.y <= 0.0001f ? 0f : Mathf.Clamp01(localY / TotalWorldSize.y);
        GenerationMaterialIndices indices = generationMaterialIndices;

        // XZ-only noise: same value for all Y in a column, so horizon boundaries shift
        // uniformly and can never bleed through each other.
        float bj = materialBoundaryNoiseAmplitude > 0f
            ? (VoxelTerrainGenerator.EvaluateMaterialNoise2D(
                localX, localZ, context, 2573.1f, 1891.7f,
                materialBoundaryNoiseScale) * 2f - 1f)
              * materialBoundaryNoiseAmplitude
            : 0f;

        if (columnProfile.isOceanFloor)
        {
            float shallowWaterBlend = VoxelTerrainGenerator.SmoothStep01(columnProfile.oceanWaterDepth / Mathf.Max(voxelSizeMeters * 4f, 4f));

            if (indices.basinGravelIndex >= 0 &&
                columnProfile.oceanWaterDepth <= Mathf.Max(voxelSizeMeters * 1.15f, 1.15f) &&
                depthBelowSurface <= Mathf.Max(voxelSizeMeters * 0.9f, 0.9f) + bj)
            {
                return (byte)indices.basinGravelIndex;
            }

            if (indices.basinSandIndex >= 0 &&
                depthBelowSurface <= Mathf.Lerp(
                    Mathf.Max(voxelSizeMeters * 1.2f, 1.2f),
                    Mathf.Max(voxelSizeMeters * 2.3f, 2.3f),
                    shallowWaterBlend) + bj)
            {
                return (byte)indices.basinSandIndex;
            }

            if (indices.clayDepositIndex >= 0 &&
                columnProfile.oceanWaterDepth > Mathf.Max(voxelSizeMeters * 0.75f, 0.75f) &&
                depthBelowSurface <= Mathf.Max(voxelSizeMeters * 3.6f, 3.6f) + bj)
            {
                return (byte)indices.clayDepositIndex;
            }
        }

        if (columnProfile.isBeachLand)
        {
            if (indices.basinSandIndex >= 0 && depthBelowSurface <= columnProfile.beachSandBoundary + bj)
            {
                return (byte)indices.basinSandIndex;
            }

            if (indices.basinGravelIndex >= 0 && depthBelowSurface <= columnProfile.beachGravelBoundary + bj)
            {
                return (byte)indices.basinGravelIndex;
            }
        }

        if (indices.organicLayerIndex >= 0 && depthBelowSurface <= columnProfile.organicThickness + bj)
        {
            return (byte)indices.organicLayerIndex;
        }

        if (indices.topsoilIndex >= 0 && depthBelowSurface <= columnProfile.topsoilBoundary + bj)
        {
            return (byte)indices.topsoilIndex;
        }

        if (indices.eluviationLayerIndex >= 0 && depthBelowSurface <= columnProfile.eluviationBoundary + bj)
        {
            return (byte)indices.eluviationLayerIndex;
        }

        if (indices.subsoilIndex >= 0 && depthBelowSurface <= columnProfile.subsoilBoundary + bj)
        {
            return (byte)indices.subsoilIndex;
        }

        if (indices.parentMaterialIndex >= 0 && depthBelowSurface <= columnProfile.parentBoundary + bj)
        {
            return (byte)indices.parentMaterialIndex;
        }

        if (depthBelowSurface > 4f)
        {
            float ironVeinNoise = VoxelTerrainGenerator.EvaluateMaterialNoise3D(localX, localY, localZ, context, 2917.5f, 22f);
            float copperVeinNoise = VoxelTerrainGenerator.EvaluateMaterialNoise3D(localX, localY, localZ, context, 3301.9f, 26f);

            if (indices.ironVeinIndex >= 0 &&
                depthBelowSurface <= 18f + bj &&
                ironVeinNoise > 0.78f)
            {
                return (byte)indices.ironVeinIndex;
            }

            if (indices.copperVeinIndex >= 0 &&
                depthBelowSurface >= 8f + bj &&
                copperVeinNoise > 0.81f)
            {
                return (byte)indices.copperVeinIndex;
            }
        }

        if (indices.weatheredStoneIndex >= 0 && depthBelowSurface <= columnProfile.weatheredBoundary + bj)
        {
            return (byte)indices.weatheredStoneIndex;
        }

        for (int i = 0; i < materialDefinitions.Count; i++)
        {
            VoxelTerrainMaterialDefinition definition = materialDefinitions[i];
            if (definition == null || definition.isFallbackMaterial || VoxelTerrainGenerator.IsReservedDefaultMaterialIndex(i, generationMaterialIndices) || VoxelTerrainGenerator.IsExcludedFromBaseTerrainNoise(definition))
            {
                continue;
            }

            if (depthBelowSurface < definition.depthRangeMeters.x + bj || depthBelowSurface > definition.depthRangeMeters.y + bj)
            {
                continue;
            }

            if (normalizedHeight < definition.normalizedHeightRange.x || normalizedHeight > definition.normalizedHeightRange.y)
            {
                continue;
            }

            float materialNoise = VoxelTerrainGenerator.EvaluatePerlin3D(
                (localX + context.materialOffsetX + (i * 137.17f)) / definition.distributionNoiseScaleMeters,
                (localY + context.materialOffsetY + (i * 59.11f)) / definition.distributionNoiseScaleMeters,
                (localZ + context.materialOffsetZ + (i * 83.37f)) / definition.distributionNoiseScaleMeters);
            if (materialNoise >= definition.distributionNoiseThreshold)
            {
                return (byte)i;
            }
        }

        if (indices.bedrockIndex >= 0)
        {
            return (byte)indices.bedrockIndex;
        }

        return indices.fallbackIndex;
    }

    private ColumnMaterialProfile BuildColumnMaterialProfile(float localX, float localZ, float surfaceHeight, GenerationContext context)
    {
        float moistureNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(localX, localZ, context, 421.3f, 217.9f, 68f);
        float soilThicknessNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(localX, localZ, context, 811.1f, 1043.7f, 88f);
        float eluviationNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(localX, localZ, context, 1337.3f, 553.1f, 104f);
        float parentNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(localX, localZ, context, 1739.7f, 947.2f, 96f);
        float outcropNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(localX, localZ, context, 2099.3f, 1373.4f, 62f);

        float normalizedSurfaceHeight = TotalWorldSize.y <= 0.0001f ? 0f : Mathf.Clamp01(surfaceHeight / TotalWorldSize.y);
        float uplandFactor = Mathf.Clamp01((normalizedSurfaceHeight - 0.18f) / 0.72f);
        float soilRetention = Mathf.Lerp(1.08f, 0.76f, uplandFactor);
        float islandDistance = VoxelTerrainGenerator.EvaluateIslandDistanceShaped(localX, localZ, TotalWorldSize, islandShapeNoiseScale, islandShapeNoiseStrength, context.islandShapeOffsetX, context.islandShapeOffsetZ);
        float coastalRingStart = islandCoreRadiusNormalized - (coastalShelfWidthNormalized * 0.45f);
        float coastalRingBlend = shapeAsIsland
            ? VoxelTerrainGenerator.SmoothStep01((islandDistance - coastalRingStart) / Mathf.Max(0.0001f, coastalShelfWidthNormalized * 1.15f))
            : 0f;
        float shorelineHeightRange = Mathf.Max(beachHeightMeters + (voxelSizeMeters * 3f), voxelSizeMeters * 4f);
        float shorelineHeightBlend = 1f - Mathf.Clamp01((surfaceHeight - seaLevelMeters) / shorelineHeightRange);
        float coastalFactor = Mathf.Clamp01(coastalRingBlend * shorelineHeightBlend);
        float topsoilReduction = Mathf.Lerp(1f, 0.72f, coastalFactor);
        float eluviationReduction = Mathf.Lerp(1f, 0.7f, coastalFactor);
        float subsoilReduction = Mathf.Lerp(1f, 0.68f, coastalFactor);
        float parentReduction = Mathf.Lerp(1f, 0.62f, coastalFactor);
        float oceanWaterDepth = Mathf.Max(0f, seaLevelMeters - surfaceHeight);
        bool isOceanFloor = shapeAsIsland &&
                            coastalRingBlend > 0.22f &&
                            oceanWaterDepth > voxelSizeMeters * 0.12f;
        float beachTransitionHeightRange = Mathf.Max(beachHeightMeters + (voxelSizeMeters * 2.6f), voxelSizeMeters * 3.8f);
        float beachHeightBlend = 1f - Mathf.Clamp01((surfaceHeight - seaLevelMeters) / beachTransitionHeightRange);
        float beachFactor = Mathf.Clamp01(coastalRingBlend * beachHeightBlend);
        float beachBoundaryNoise = materialBeachBoundaryNoiseAmplitude > 0f
            ? (VoxelTerrainGenerator.EvaluateMaterialNoise2D(localX, localZ, context, 3079.3f, 2417.8f, materialBeachBoundaryNoiseScale) * 2f - 1f)
              * materialBeachBoundaryNoiseAmplitude
            : 0f;
        bool isBeachLand = shapeAsIsland &&
                           !isOceanFloor &&
                           beachFactor + beachBoundaryNoise > 0.18f &&
                           surfaceHeight >= seaLevelMeters + (voxelSizeMeters * 0.08f);
        float coastalLandHeightRange = Mathf.Max(beachHeightMeters + (voxelSizeMeters * 4.5f), voxelSizeMeters * 5.5f);
        bool isCoastalLand = shapeAsIsland &&
                             !isOceanFloor &&
                             coastalFactor > 0.12f &&
                             surfaceHeight <= seaLevelMeters + coastalLandHeightRange;

        float organicThickness;
        float topsoilThickness;
        float eluviationThickness;
        float subsoilThickness;
        float parentThickness;
        float weatheredThickness;
        float beachSandThickness = 0f;
        float beachGravelThickness = 0f;

        if (isCoastalLand)
        {
            float coastalSoilRetention = Mathf.Lerp(1.02f, 0.9f, coastalFactor);
            organicThickness = Mathf.Max(
                voxelSizeMeters * 0.9f,
                Mathf.Lerp(voxelSizeMeters * 0.9f, voxelSizeMeters * 1.35f, moistureNoise) * coastalSoilRetention);
            topsoilThickness = Mathf.Max(
                voxelSizeMeters * 0.85f,
                Mathf.Lerp(voxelSizeMeters * 0.85f, voxelSizeMeters * 1.55f, soilThicknessNoise) * coastalSoilRetention);
            eluviationThickness = Mathf.Max(
                voxelSizeMeters * 0.8f,
                Mathf.Lerp(voxelSizeMeters * 0.8f, voxelSizeMeters * 1.35f, eluviationNoise) * Mathf.Lerp(1.01f, 0.92f, coastalFactor));
            subsoilThickness = Mathf.Lerp(voxelSizeMeters * 1.8f, voxelSizeMeters * 3.4f, moistureNoise) * Mathf.Lerp(0.96f, 0.8f, coastalFactor);
            parentThickness = Mathf.Lerp(voxelSizeMeters * 2.4f, voxelSizeMeters * 5.1f, parentNoise) * Mathf.Lerp(0.94f, 0.76f, coastalFactor);
            weatheredThickness = Mathf.Lerp(voxelSizeMeters * 4.2f, voxelSizeMeters * 8.2f, outcropNoise) * Mathf.Lerp(1.04f, 1.18f, coastalFactor);
        }
        else
        {
            organicThickness = Mathf.Max(
                voxelSizeMeters * 0.95f,
                Mathf.Lerp(voxelSizeMeters * 0.95f, voxelSizeMeters * 1.55f, moistureNoise) * soilRetention);
            topsoilThickness = Mathf.Max(
                voxelSizeMeters * 0.9f,
                Mathf.Lerp(voxelSizeMeters * 0.9f, voxelSizeMeters * 2.1f, soilThicknessNoise) * soilRetention * topsoilReduction);
            eluviationThickness = Mathf.Max(
                voxelSizeMeters * 0.85f,
                Mathf.Lerp(voxelSizeMeters * 0.85f, voxelSizeMeters * 1.9f, eluviationNoise) * Mathf.Lerp(1.04f, 0.9f, uplandFactor) * eluviationReduction);
            subsoilThickness = Mathf.Lerp(voxelSizeMeters * 2f, voxelSizeMeters * 4.2f, moistureNoise) * subsoilReduction;
            parentThickness = Mathf.Lerp(voxelSizeMeters * 3f, voxelSizeMeters * 6.4f, parentNoise) * parentReduction;
            weatheredThickness = Mathf.Lerp(voxelSizeMeters * 4f, voxelSizeMeters * 7.4f, outcropNoise);
        }

        // Prepass-aware slope: sample neighbouring surface heights so that basin walls
        // and cliff edges (which are baked into surfaceHeightPrepass by this point) drive
        // correct soil-erosion instead of reading the raw noise-only surface.
        float dhSA = voxelSizeMeters;
        float hPXA = SampleSurfaceHeightPrepass(localX + dhSA, localZ);
        float hNXA = SampleSurfaceHeightPrepass(localX - dhSA, localZ);
        float hPZA = SampleSurfaceHeightPrepass(localX, localZ + dhSA);
        float hNZA = SampleSurfaceHeightPrepass(localX, localZ - dhSA);
        float gxA = (hPXA - hNXA) / (2f * dhSA);
        float gzA = (hPZA - hNZA) / (2f * dhSA);
        float localSlopeFactor = Mathf.Clamp01(Mathf.Sqrt(gxA * gxA + gzA * gzA) / 2.0f);

        // Lake-basin columns sit below sea level but are not ocean floor.
        // Their quartic rim walls have high gradients that would otherwise strip all sediment,
        // leaving bare bedrock at the waterline. Cap erosion so mud/sand/gravel can survive.
        bool isLakeBasin = !isOceanFloor && surfaceHeight < seaLevelMeters - voxelSizeMeters * 0.5f;
        if (isLakeBasin) localSlopeFactor = Mathf.Min(localSlopeFactor, 0.3f);

        // Coastal cliffs (high slope + near ocean) lose ALL soil — bare rock only.
        float coastalCliffFactorA = localSlopeFactor * Mathf.Clamp01(coastalRingBlend * 2f);
        float effectiveSlopeFactorA = Mathf.Max(localSlopeFactor, coastalCliffFactorA);

        float slopeErosionA  = 1f - effectiveSlopeFactorA * 0.99f;
        float slopeSoilMultA = slopeErosionA * slopeErosionA;
        organicThickness    *= slopeErosionA;
        topsoilThickness    *= slopeSoilMultA;
        eluviationThickness *= slopeSoilMultA;
        subsoilThickness    *= slopeSoilMultA;
        parentThickness     *= slopeSoilMultA;
        weatheredThickness   = Mathf.Lerp(weatheredThickness, weatheredThickness * 2.2f, effectiveSlopeFactorA);

        if (isBeachLand)
        {
            beachSandThickness = Mathf.Max(
                voxelSizeMeters * 0.95f,
                Mathf.Lerp(voxelSizeMeters * 0.95f, voxelSizeMeters * 1.45f, beachFactor));
            beachGravelThickness = Mathf.Max(
                voxelSizeMeters * 0.65f,
                Mathf.Lerp(voxelSizeMeters * 0.65f, voxelSizeMeters * 1.35f, beachFactor * 0.9f));
        }
        else if (beachFactor > 0.05f)
        {
            // Compute boundaries for smooth color blending even below the isBeachLand threshold.
            beachSandThickness = Mathf.Max(
                voxelSizeMeters * 0.95f,
                Mathf.Lerp(voxelSizeMeters * 0.95f, voxelSizeMeters * 1.45f, beachFactor));
            beachGravelThickness = Mathf.Max(
                voxelSizeMeters * 0.65f,
                Mathf.Lerp(voxelSizeMeters * 0.65f, voxelSizeMeters * 1.35f, beachFactor * 0.9f));
        }

        // Beach, ocean-floor, and lake-basin columns never show the shader rock overlay.
        // Zeroing here prevents high gradient slopeFactor from cliff neighbours bleeding in.
        if (isBeachLand || isOceanFloor || isLakeBasin) localSlopeFactor = 0f;

        // Continuous color factors: beach and ocean floor cross-fade smoothly at the shoreline.
        float waterDepthFactor  = Mathf.Clamp01(oceanWaterDepth / (voxelSizeMeters * 2f));
        float shallowFactor     = 1f - waterDepthFactor;
        float colorBeachFactor  = shapeAsIsland
            ? Mathf.Clamp01((beachFactor - 0.18f) / 0.62f) * shallowFactor
            : 0f;
        float colorOceanFactor  = shapeAsIsland
            ? Mathf.Clamp01((coastalRingBlend - 0.18f) / 0.28f) * waterDepthFactor
            : 0f;

        ColumnMaterialProfile profile = new ColumnMaterialProfile
        {
            surfaceHeight       = surfaceHeight,
            beachSandBoundary   = beachSandThickness,
            beachGravelBoundary = beachSandThickness + beachGravelThickness,
            organicThickness    = organicThickness,
            oceanWaterDepth     = oceanWaterDepth,
            isBeachLand         = isBeachLand,
            isOceanFloor        = isOceanFloor,
            // Store raw continuous values for sub-voxel smooth shader thresholding.
            // beachFactor = coastalRingBlend * beachHeightBlend (threshold at 0.18 = isBeachLand)
            // oceanFactor = coastalRingBlend (threshold at 0.22 combined with oceanWaterDepth)
            beachFactor         = beachFactor,
            oceanFactor         = coastalRingBlend,
            slopeFactor         = localSlopeFactor,
            // Per-column boundary noise: same formula as DetermineCellMaterialIndex so visual
            // and logical soil horizon boundaries are always co-located.
            materialBoundaryNoise = materialBoundaryNoiseAmplitude > 0f
                ? (VoxelTerrainGenerator.EvaluateMaterialNoise2D(
                    localX, localZ, context,
                    2573.1f, 1891.7f, // arbitrary offsets — decouple from other noise layers; changing breaks world
                    materialBoundaryNoiseScale) * 2f - 1f)
                  * materialBoundaryNoiseAmplitude
                : 0f
        };
        profile.topsoilBoundary = profile.organicThickness + topsoilThickness;
        profile.eluviationBoundary = profile.topsoilBoundary + eluviationThickness;
        profile.subsoilBoundary = profile.eluviationBoundary + subsoilThickness;
        profile.parentBoundary = profile.subsoilBoundary + parentThickness;
        profile.weatheredBoundary = profile.parentBoundary + weatheredThickness;
        return profile;
    }

    private bool ShouldUseAsyncTerrainGeneration()
    {
        return useAsyncBuildPipeline || IsRuntimeStreamingModeActive;
    }

    private bool ShouldUseParallelGenerationJobs()
    {
        return SystemInfo.processorCount > 1 &&
               (long)TotalSamplesX * TotalSamplesY >= 8192L;
    }

    private void ResetRuntimeStreamingStateForGeneration()
    {
        streamingManager.ResetForGeneration(ResolveRuntimeStreamingAnchorChunk());
    }

    private bool MarkRuntimeStreamingStartupChunksReady()
    {
        return streamingManager.TryMarkStartupChunksReady(IsRuntimeStreamingModeActive);
    }

    private Vector3Int GetRuntimeStreamingGenerationAnchorChunk()
    {
        return streamingManager.GetOrResolveGenerationAnchorChunk(
            activeTerrainGenerationOperation != null,
            ResolveRuntimeStreamingAnchorChunk());
    }

    private Vector3Int ResolveRuntimeStreamingAnchorChunk()
    {
        Transform anchorTransform = ResolveRuntimeStreamingAnchorTransform();
        if (anchorTransform != null)
        {
            return WorldPositionToChunkCoordinate(anchorTransform.position);
        }

        return new Vector3Int(
            Mathf.Clamp(chunkCounts.x / 2, 0, Mathf.Max(0, chunkCounts.x - 1)),
            Mathf.Clamp(chunkCounts.y / 2, 0, Mathf.Max(0, chunkCounts.y - 1)),
            Mathf.Clamp(chunkCounts.z / 2, 0, Mathf.Max(0, chunkCounts.z - 1)));
    }

    private Transform ResolveRuntimeStreamingAnchorTransform()
    {
        if (runtimeStreamingAnchor != null)
        {
            return runtimeStreamingAnchor;
        }

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    private bool TryGetRuntimeStreamingStartupBounds(out Bounds bounds)
    {
        return streamingManager.TryGetStartupBounds(
            HasRuntimeStreamingStartupAreaReady,
            HasGeneratedTerrain,
            GetRuntimeStreamingGenerationAnchorChunk(),
            runtimeStreamingStartupChunkRadius,
            chunkCounts,
            GetChunkWorldBounds,
            out bounds);
    }

    private int CountRuntimeStreamingStartupChunks(IReadOnlyList<Vector3Int> chunkCoordinates)
    {
        return streamingManager.CountStartupChunks(
            chunkCoordinates,
            GetRuntimeStreamingGenerationAnchorChunk(),
            runtimeStreamingStartupChunkRadius,
            IsRuntimeStreamingModeActive);
    }
}
