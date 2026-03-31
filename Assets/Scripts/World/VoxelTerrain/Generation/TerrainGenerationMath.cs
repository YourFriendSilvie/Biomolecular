using Unity.Collections;
using UnityEngine;

internal struct TerrainGenerationSettings
{
    public Vector3 totalWorldSize;
    public float voxelSizeMeters;
    public float baseSurfaceHeightMeters;
    public float seaLevelMeters;
    public float surfaceNoiseScaleMeters;
    public int surfaceNoiseOctaves;
    public float surfaceNoisePersistence;
    public float surfaceNoiseLacunarity;
    public float surfaceAmplitudeMeters;
    public float ridgeNoiseScaleMeters;
    public float ridgeAmplitudeMeters;
    public float detailNoiseScaleMeters;
    public float detailAmplitudeMeters;
    public float caveNoiseScaleMeters;
    public float caveNoiseThreshold;
    public float caveCarveStrengthMeters;
    public float caveStartDepthMeters;
    public float islandCoreRadiusNormalized;
    public float coastalShelfWidthNormalized;
    public float islandShapeNoiseScale;
    public float islandShapeNoiseStrength;
    public float beachHeightMeters;
    public float oceanFloorDepthMeters;
    public float oceanFloorVariationMeters;
    public float oceanFloorNoiseScaleMeters;
    public byte shapeAsIsland;
}

internal struct TerrainColumnMaterialProfileJobData
{
    public float surfaceHeight;
    public float beachSandBoundary;
    public float beachGravelBoundary;
    public float organicThickness;
    public float topsoilBoundary;
    public float eluviationBoundary;
    public float subsoilBoundary;
    public float parentBoundary;
    public float weatheredBoundary;
    public float oceanWaterDepth;
    public byte isBeachLand;
    public byte isOceanFloor;
    public float slopeFactor;

    public ColumnMaterialProfile ToManagedProfile()
    {
        return new ColumnMaterialProfile
        {
            surfaceHeight = surfaceHeight,
            beachSandBoundary = beachSandBoundary,
            beachGravelBoundary = beachGravelBoundary,
            organicThickness = organicThickness,
            topsoilBoundary = topsoilBoundary,
            eluviationBoundary = eluviationBoundary,
            subsoilBoundary = subsoilBoundary,
            parentBoundary = parentBoundary,
            weatheredBoundary = weatheredBoundary,
            oceanWaterDepth = oceanWaterDepth,
            isBeachLand = isBeachLand != 0,
            isOceanFloor = isOceanFloor != 0,
            slopeFactor = slopeFactor
        };
    }

    public static TerrainColumnMaterialProfileJobData FromManagedProfile(ColumnMaterialProfile profile)
    {
        return new TerrainColumnMaterialProfileJobData
        {
            surfaceHeight = profile.surfaceHeight,
            beachSandBoundary = profile.beachSandBoundary,
            beachGravelBoundary = profile.beachGravelBoundary,
            organicThickness = profile.organicThickness,
            topsoilBoundary = profile.topsoilBoundary,
            eluviationBoundary = profile.eluviationBoundary,
            subsoilBoundary = profile.subsoilBoundary,
            parentBoundary = profile.parentBoundary,
            weatheredBoundary = profile.weatheredBoundary,
            oceanWaterDepth = profile.oceanWaterDepth,
            isBeachLand = profile.isBeachLand ? (byte)1 : (byte)0,
            isOceanFloor = profile.isOceanFloor ? (byte)1 : (byte)0,
            slopeFactor = profile.slopeFactor
        };
    }
}

/// <summary>
/// Internal static class containing the managed (non-Burst) overloads of terrain generation
/// math shared between <see cref="TerrainGenerationOrchestration"/> and Editor generation.
/// For the Burst-compatible versions used in parallel jobs, see <see cref="TerrainGenerationJobsUtility"/>.
/// </summary>
internal static class TerrainGenerationMath
{
    public static float SampleSurfaceHeightPrepass(
        float[] surfaceHeightPrepass,
        float localX,
        float localZ,
        int totalSamplesX,
        int totalSamplesZ,
        float voxelSizeMeters)
    {
        if (surfaceHeightPrepass == null || surfaceHeightPrepass.Length == 0)
        {
            return 0f;
        }

        float safeVoxelSize = Mathf.Max(0.0001f, voxelSizeMeters);
        float sampleX = Mathf.Clamp(localX / safeVoxelSize, 0f, totalSamplesX - 1f);
        float sampleZ = Mathf.Clamp(localZ / safeVoxelSize, 0f, totalSamplesZ - 1f);
        int minX = Mathf.Clamp(Mathf.FloorToInt(sampleX), 0, totalSamplesX - 1);
        int maxX = Mathf.Clamp(minX + 1, 0, totalSamplesX - 1);
        int minZ = Mathf.Clamp(Mathf.FloorToInt(sampleZ), 0, totalSamplesZ - 1);
        int maxZ = Mathf.Clamp(minZ + 1, 0, totalSamplesZ - 1);
        float blendX = sampleX - minX;
        float blendZ = sampleZ - minZ;

        float bottom = Mathf.Lerp(
            surfaceHeightPrepass[VoxelDataStore.GetSurfacePrepassIndex(minX, minZ, totalSamplesX)],
            surfaceHeightPrepass[VoxelDataStore.GetSurfacePrepassIndex(maxX, minZ, totalSamplesX)],
            blendX);
        float top = Mathf.Lerp(
            surfaceHeightPrepass[VoxelDataStore.GetSurfacePrepassIndex(minX, maxZ, totalSamplesX)],
            surfaceHeightPrepass[VoxelDataStore.GetSurfacePrepassIndex(maxX, maxZ, totalSamplesX)],
            blendX);
        return Mathf.Lerp(bottom, top, blendZ);
    }

    public static float SampleSurfaceHeightPrepass(
        NativeArray<float> surfaceHeightPrepass,
        float localX,
        float localZ,
        int totalSamplesX,
        int totalSamplesZ,
        float voxelSizeMeters)
    {
        if (!surfaceHeightPrepass.IsCreated || surfaceHeightPrepass.Length == 0)
        {
            return 0f;
        }

        float safeVoxelSize = Mathf.Max(0.0001f, voxelSizeMeters);
        float sampleX = Mathf.Clamp(localX / safeVoxelSize, 0f, totalSamplesX - 1f);
        float sampleZ = Mathf.Clamp(localZ / safeVoxelSize, 0f, totalSamplesZ - 1f);
        int minX = Mathf.Clamp(Mathf.FloorToInt(sampleX), 0, totalSamplesX - 1);
        int maxX = Mathf.Clamp(minX + 1, 0, totalSamplesX - 1);
        int minZ = Mathf.Clamp(Mathf.FloorToInt(sampleZ), 0, totalSamplesZ - 1);
        int maxZ = Mathf.Clamp(minZ + 1, 0, totalSamplesZ - 1);
        float blendX = sampleX - minX;
        float blendZ = sampleZ - minZ;

        float bottom = Mathf.Lerp(
            surfaceHeightPrepass[VoxelDataStore.GetSurfacePrepassIndex(minX, minZ, totalSamplesX)],
            surfaceHeightPrepass[VoxelDataStore.GetSurfacePrepassIndex(maxX, minZ, totalSamplesX)],
            blendX);
        float top = Mathf.Lerp(
            surfaceHeightPrepass[VoxelDataStore.GetSurfacePrepassIndex(minX, maxZ, totalSamplesX)],
            surfaceHeightPrepass[VoxelDataStore.GetSurfacePrepassIndex(maxX, maxZ, totalSamplesX)],
            blendX);
        return Mathf.Lerp(bottom, top, blendZ);
    }

    public static float EvaluateDensity(
        float localX,
        float localY,
        float localZ,
        float surfaceHeight,
        GenerationContext context,
        TerrainGenerationSettings settings)
    {
        // FLOAT PRECISION NOTE: All noise sampling uses world-space float coordinates
        // (localX + context.surfaceOffsetX, etc.). At distances >10,000m from origin,
        // float precision degrades (~0.001m error at 10km), causing visible terrain
        // jitter and noise pattern seams. Future mitigation: use chunk-relative
        // coordinates for noise sampling, or store offsets as doubles.
        float density = surfaceHeight - localY;

        float depthBelowSurface = surfaceHeight - localY;
        if (depthBelowSurface > settings.caveStartDepthMeters)
        {
            float caveNoise = VoxelTerrainGenerator.EvaluatePerlin3D(
                (localX + context.caveOffsetX) / Mathf.Max(0.0001f, settings.caveNoiseScaleMeters),
                (localY + context.caveOffsetY) / Mathf.Max(0.0001f, settings.caveNoiseScaleMeters),
                (localZ + context.caveOffsetZ) / Mathf.Max(0.0001f, settings.caveNoiseScaleMeters));
            if (caveNoise > settings.caveNoiseThreshold)
            {
                float carveAmount = (caveNoise - settings.caveNoiseThreshold) / Mathf.Max(0.0001f, 1f - settings.caveNoiseThreshold);
                density -= carveAmount * settings.caveCarveStrengthMeters;
            }
        }

        return density;
    }

    public static float EvaluateSurfaceHeight(
        float localX,
        float localZ,
        GenerationContext context,
        TerrainGenerationSettings settings)
    {
        // FLOAT PRECISION NOTE: All noise sampling uses world-space float coordinates
        // (localX + context.surfaceOffsetX, etc.). At distances >10,000m from origin,
        // float precision degrades (~0.001m error at 10km), causing visible terrain
        // jitter and noise pattern seams. Future mitigation: use chunk-relative
        // coordinates for noise sampling, or store offsets as doubles.
        float fractalNoise = VoxelTerrainGenerator.EvaluateFractalNoise(
            (localX + context.surfaceOffsetX) / Mathf.Max(0.0001f, settings.surfaceNoiseScaleMeters),
            (localZ + context.surfaceOffsetZ) / Mathf.Max(0.0001f, settings.surfaceNoiseScaleMeters),
            settings.surfaceNoiseOctaves,
            settings.surfaceNoisePersistence,
            settings.surfaceNoiseLacunarity);
        float ridgeNoise = Mathf.PerlinNoise(
            (localX + context.ridgeOffsetX) / Mathf.Max(0.0001f, settings.ridgeNoiseScaleMeters),
            (localZ + context.ridgeOffsetZ) / Mathf.Max(0.0001f, settings.ridgeNoiseScaleMeters));
        ridgeNoise = 1f - Mathf.Abs((ridgeNoise * 2f) - 1f);
        ridgeNoise *= ridgeNoise;
        float detailNoise = Mathf.PerlinNoise(
            (localX + context.detailOffsetX) / Mathf.Max(0.0001f, settings.detailNoiseScaleMeters),
            (localZ + context.detailOffsetZ) / Mathf.Max(0.0001f, settings.detailNoiseScaleMeters));
        detailNoise = (detailNoise - 0.5f) * 2f;

        float landHeight = settings.baseSurfaceHeightMeters
            + ((fractalNoise - 0.5f) * 2f * settings.surfaceAmplitudeMeters)
            + (ridgeNoise * settings.ridgeAmplitudeMeters)
            + (detailNoise * settings.detailAmplitudeMeters);
        if (settings.shapeAsIsland == 0)
        {
            return Mathf.Clamp(landHeight, 0f, settings.totalWorldSize.y - settings.voxelSizeMeters);
        }

        float islandDistance = VoxelTerrainGenerator.EvaluateIslandDistanceShaped(localX, localZ, settings.totalWorldSize, settings.islandShapeNoiseScale, settings.islandShapeNoiseStrength, context.islandShapeOffsetX, context.islandShapeOffsetZ);
        float shoreBlendStart = settings.islandCoreRadiusNormalized;
        float shelfBlend = VoxelTerrainGenerator.SmoothStep01((islandDistance - shoreBlendStart) / Mathf.Max(0.0001f, settings.coastalShelfWidthNormalized));
        float shorelineTargetHeight = settings.seaLevelMeters + settings.beachHeightMeters;
        float shelfHeight = Mathf.Lerp(landHeight, shorelineTargetHeight, shelfBlend);

        float oceanNoise = Mathf.PerlinNoise(
            (localX + context.oceanFloorOffsetX) / Mathf.Max(0.0001f, settings.oceanFloorNoiseScaleMeters),
            (localZ + context.oceanFloorOffsetZ) / Mathf.Max(0.0001f, settings.oceanFloorNoiseScaleMeters));
        float oceanFloorHeight = Mathf.Max(
            0f,
            settings.seaLevelMeters
            - settings.oceanFloorDepthMeters
            + ((oceanNoise - 0.5f) * 2f * settings.oceanFloorVariationMeters));
        float oceanBlend = VoxelTerrainGenerator.SmoothStep01(
            (islandDistance - (settings.islandCoreRadiusNormalized + settings.coastalShelfWidthNormalized))
            / Mathf.Max(0.0001f, 1f - (settings.islandCoreRadiusNormalized + settings.coastalShelfWidthNormalized)));
        float worldHeight = Mathf.Lerp(shelfHeight, oceanFloorHeight, oceanBlend);
        return Mathf.Clamp(worldHeight, 0f, settings.totalWorldSize.y - settings.voxelSizeMeters);
    }

    /// <summary>
    /// Builds the full per-column material profile for a single XZ column (managed overload).
    /// Evaluates moisture, soil-thickness, and slope noise; determines beach/ocean-floor status;
    /// and computes cumulative depth boundaries for every soil horizon. Uses the managed
    /// <see cref="TerrainGenerationSettings"/> struct, making it suitable for the Editor
    /// generation path and non-job runtime calls.
    /// </summary>
    /// <param name="localX">Local X coordinate of the column in metres.</param>
    /// <param name="localZ">Local Z coordinate of the column in metres.</param>
    /// <param name="surfaceHeight">Pre-computed surface height for this column.</param>
    /// <param name="context">Seeded noise offsets for this terrain instance.</param>
    /// <param name="settings">Terrain generation settings snapshot.</param>
    /// <returns>A fully populated <see cref="ColumnMaterialProfile"/> for this column.</returns>
    public static ColumnMaterialProfile BuildColumnMaterialProfile(
        float localX,
        float localZ,
        float surfaceHeight,
        GenerationContext context,
        TerrainGenerationSettings settings)
    {
        float moistureNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(
            localX, localZ, context,
            421.3f, 217.9f,   // arbitrary offsets — decouple from other noise layers; changing breaks world
            68f);              // noise spatial scale in metres
        float soilThicknessNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(
            localX, localZ, context,
            811.1f, 1043.7f,  // arbitrary offsets — decouple from other noise layers; changing breaks world
            88f);              // noise spatial scale in metres
        float eluviationNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(
            localX, localZ, context,
            1337.3f, 553.1f,  // arbitrary offsets — decouple from other noise layers; changing breaks world
            104f);             // noise spatial scale in metres
        float parentNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(
            localX, localZ, context,
            1739.7f, 947.2f,  // arbitrary offsets — decouple from other noise layers; changing breaks world
            96f);              // noise spatial scale in metres
        float outcropNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(
            localX, localZ, context,
            2099.3f, 1373.4f, // arbitrary offsets — decouple from other noise layers; changing breaks world
            62f);              // noise spatial scale in metres

        float normalizedSurfaceHeight = settings.totalWorldSize.y<= 0.0001f ? 0f : Mathf.Clamp01(surfaceHeight / settings.totalWorldSize.y);
        float uplandFactor = Mathf.Clamp01((normalizedSurfaceHeight - 0.18f) / 0.72f);
        float soilRetention = Mathf.Lerp(1.08f, 0.76f, uplandFactor);
        float islandDistance = VoxelTerrainGenerator.EvaluateIslandDistanceShaped(localX, localZ, settings.totalWorldSize, settings.islandShapeNoiseScale, settings.islandShapeNoiseStrength, context.islandShapeOffsetX, context.islandShapeOffsetZ);
        float coastalRingStart = settings.islandCoreRadiusNormalized - (settings.coastalShelfWidthNormalized * 0.45f);
        float coastalRingBlend = settings.shapeAsIsland != 0
            ? VoxelTerrainGenerator.SmoothStep01((islandDistance - coastalRingStart) / Mathf.Max(0.0001f, settings.coastalShelfWidthNormalized * 1.15f))
            : 0f;
        float shorelineHeightRange = Mathf.Max(settings.beachHeightMeters + (settings.voxelSizeMeters * 3f), settings.voxelSizeMeters * 4f);
        float shorelineHeightBlend = 1f - Mathf.Clamp01((surfaceHeight - settings.seaLevelMeters) / shorelineHeightRange);
        float coastalFactor = Mathf.Clamp01(coastalRingBlend * shorelineHeightBlend);
        float topsoilReduction = Mathf.Lerp(1f, 0.72f, coastalFactor);
        float eluviationReduction = Mathf.Lerp(1f, 0.7f, coastalFactor);
        float subsoilReduction = Mathf.Lerp(1f, 0.68f, coastalFactor);
        float parentReduction = Mathf.Lerp(1f, 0.62f, coastalFactor);
        float oceanWaterDepth = Mathf.Max(0f, settings.seaLevelMeters - surfaceHeight);
        bool isOceanFloor = settings.shapeAsIsland != 0 &&
                            coastalRingBlend > 0.22f &&
                            oceanWaterDepth > settings.voxelSizeMeters * 0.12f;
        float beachTransitionHeightRange = Mathf.Max(settings.beachHeightMeters + (settings.voxelSizeMeters * 2.6f), settings.voxelSizeMeters * 3.8f);
        float beachHeightBlend = 1f - Mathf.Clamp01((surfaceHeight - settings.seaLevelMeters) / beachTransitionHeightRange);
        float beachFactor = Mathf.Clamp01(coastalRingBlend * beachHeightBlend);
        bool isBeachLand = settings.shapeAsIsland != 0 &&
                           !isOceanFloor &&
                           beachFactor > 0.18f &&
                           surfaceHeight >= settings.seaLevelMeters + (settings.voxelSizeMeters * 0.08f);
        float coastalLandHeightRange = Mathf.Max(settings.beachHeightMeters + (settings.voxelSizeMeters * 4.5f), settings.voxelSizeMeters * 5.5f);
        bool isCoastalLand = settings.shapeAsIsland != 0 &&
                             !isOceanFloor &&
                             coastalFactor > 0.12f &&
                             surfaceHeight <= settings.seaLevelMeters + coastalLandHeightRange;

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
                settings.voxelSizeMeters * 0.9f,
                Mathf.Lerp(settings.voxelSizeMeters * 0.9f, settings.voxelSizeMeters * 1.35f, moistureNoise) * coastalSoilRetention);
            topsoilThickness = Mathf.Max(
                settings.voxelSizeMeters * 0.85f,
                Mathf.Lerp(settings.voxelSizeMeters * 0.85f, settings.voxelSizeMeters * 1.55f, soilThicknessNoise) * coastalSoilRetention);
            eluviationThickness = Mathf.Max(
                settings.voxelSizeMeters * 0.8f,
                Mathf.Lerp(settings.voxelSizeMeters * 0.8f, settings.voxelSizeMeters * 1.35f, eluviationNoise) * Mathf.Lerp(1.01f, 0.92f, coastalFactor));
            subsoilThickness = Mathf.Lerp(settings.voxelSizeMeters * 1.8f, settings.voxelSizeMeters * 3.4f, moistureNoise) * Mathf.Lerp(0.96f, 0.8f, coastalFactor);
            parentThickness = Mathf.Lerp(settings.voxelSizeMeters * 2.4f, settings.voxelSizeMeters * 5.1f, parentNoise) * Mathf.Lerp(0.94f, 0.76f, coastalFactor);
            weatheredThickness = Mathf.Lerp(settings.voxelSizeMeters * 4.2f, settings.voxelSizeMeters * 8.2f, outcropNoise) * Mathf.Lerp(1.04f, 1.18f, coastalFactor);
        }
        else
        {
            organicThickness = Mathf.Max(
                settings.voxelSizeMeters * 0.95f,
                Mathf.Lerp(settings.voxelSizeMeters * 0.95f, settings.voxelSizeMeters * 1.55f, moistureNoise) * soilRetention);
            topsoilThickness = Mathf.Max(
                settings.voxelSizeMeters * 0.9f,
                Mathf.Lerp(settings.voxelSizeMeters * 0.9f, settings.voxelSizeMeters * 2.1f, soilThicknessNoise) * soilRetention * topsoilReduction);
            eluviationThickness = Mathf.Max(
                settings.voxelSizeMeters * 0.85f,
                Mathf.Lerp(settings.voxelSizeMeters * 0.85f, settings.voxelSizeMeters * 1.9f, eluviationNoise) * Mathf.Lerp(1.04f, 0.9f, uplandFactor) * eluviationReduction);
            subsoilThickness = Mathf.Lerp(settings.voxelSizeMeters * 2f, settings.voxelSizeMeters * 4.2f, moistureNoise) * subsoilReduction;
            parentThickness = Mathf.Lerp(settings.voxelSizeMeters * 3f, settings.voxelSizeMeters * 6.4f, parentNoise) * parentReduction;
            weatheredThickness = Mathf.Lerp(settings.voxelSizeMeters * 4f, settings.voxelSizeMeters * 7.4f, outcropNoise);
        }

        // Compute beach sand/gravel boundaries for any non-zero beachFactor so smooth color
        // blending in ChunkMeshBuilder can use them even at partial beach transitions.
        if (beachFactor > 0.05f)
        {
            beachSandThickness = Mathf.Max(
                settings.voxelSizeMeters * 0.95f,
                Mathf.Lerp(settings.voxelSizeMeters * 0.95f, settings.voxelSizeMeters * 1.45f, beachFactor));
            beachGravelThickness = Mathf.Max(
                settings.voxelSizeMeters * 0.65f,
                Mathf.Lerp(settings.voxelSizeMeters * 0.65f, settings.voxelSizeMeters * 1.35f, beachFactor * 0.9f));
        }

        // Continuous color factors: beach and ocean floor cross-fade smoothly at the shoreline.
        // waterDepthFactor ramps 0→1 as the surface goes from sea-level to 1m underwater,
        // so beach coloring fades out while ocean-floor coloring fades in over the same distance.
        float waterDepthFactor  = Mathf.Clamp01(oceanWaterDepth / (settings.voxelSizeMeters * 2f));
        float shallowFactor     = 1f - waterDepthFactor;
        float colorBeachFactor  = settings.shapeAsIsland != 0
            ? Mathf.Clamp01((beachFactor - 0.18f) / 0.62f) * shallowFactor
            : 0f;
        // Widen the coastal-ring ramp (was 0.15 → 0.28) to spread the ocean-floor blend
        // over more terrain, and remove the hard isOceanFloor gate.
        float colorOceanFactor  = settings.shapeAsIsland != 0
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
            oceanFactor         = coastalRingBlend
        };
        profile.topsoilBoundary = profile.organicThickness + topsoilThickness;
        profile.eluviationBoundary = profile.topsoilBoundary + eluviationThickness;
        profile.subsoilBoundary = profile.eluviationBoundary + subsoilThickness;
        profile.parentBoundary = profile.subsoilBoundary + parentThickness;
        profile.weatheredBoundary = profile.parentBoundary + weatheredThickness;
        return profile;
    }

    public static TerrainColumnMaterialProfileJobData BuildColumnMaterialProfileJobData(
        float localX,
        float localZ,
        float surfaceHeight,
        GenerationContext context,
        TerrainGenerationSettings settings)
    {
        return TerrainColumnMaterialProfileJobData.FromManagedProfile(
            BuildColumnMaterialProfile(localX, localZ, surfaceHeight, context, settings));
    }
}
