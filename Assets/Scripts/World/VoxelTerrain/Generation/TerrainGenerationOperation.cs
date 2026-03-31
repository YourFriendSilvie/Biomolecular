using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

internal struct TerrainGenerationNumericConfig
{
    public int totalSamplesX;
    public int totalSamplesY;
    public int totalSamplesZ;
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
    public bool shapeAsIsland;
    public float islandCoreRadiusNormalized;
    public float coastalShelfWidthNormalized;
    public float islandShapeNoiseScale;
    public float islandShapeNoiseStrength;
    public float beachHeightMeters;
    public float oceanFloorDepthMeters;
    public float oceanFloorVariationMeters;
    public float oceanFloorNoiseScaleMeters;
    public float materialBoundaryNoiseScale;
    public float materialBoundaryNoiseAmplitude;
    public float materialBeachBoundaryNoiseAmplitude;
    public float materialBeachBoundaryNoiseScale;
    // Domain warp — perturb XZ before fractal noise for organic coastlines/ridges.
    public float domainWarpScaleMeters;   // wavelength of the warp noise
    public float domainWarpStrengthMeters;// max XZ displacement in metres
    // Olympic Mountain ridges — ridged multifractal noise creates knife-edge peaks.
    // Blended in by elevation so rainforest valley floors stay flat.
    public float mountainRidgeScaleMeters;      // XZ wavelength of ridge features
    public float mountainRidgeAmplitudeMeters;  // max height added by ridge noise (m)
    public float mountainBaseHeightMeters;      // terrain height below which no mountains appear
    public float mountainBlendRangeMeters;      // vertical blend distance (m) from base to full mountains
    public Vector3 totalWorldSize;
    public float3 playerPosSnapshot; // The thread-safe player location
    public float lodStepDistance;    // Pass the inspector value here
    public int maxLod;
}

/// <summary>
/// Static utility class containing the Burst-compatible versions of the terrain generation
/// math functions used inside <c>IJobParallelFor</c> jobs. Methods here mirror the logic in
/// <see cref="VoxelTerrainGenerator"/> but accept <c>NativeArray</c> parameters and
/// <c>in</c>-ref <see cref="TerrainGenerationNumericConfig"/> structs to avoid managed
/// allocations inside Burst jobs.
/// </summary>
internal static class TerrainGenerationJobsUtility
{
    public static int GetSurfacePrepassIndex(int sampleX, int sampleZ, int totalSamplesX)
    {
        return (sampleZ * totalSamplesX) + sampleX;
    }

    public static int GetColumnPrepassIndex(int cellX, int cellZ, int totalCellsX)
    {
        return (cellZ * totalCellsX) + cellX;
    }

    public static byte DetermineCellMaterialIndex(
        float localX,
        float localY,
        float localZ,
        in GenerationContext context,
        in ColumnMaterialProfile columnProfile,
        in TerrainGenerationNumericConfig config,
        in GenerationMaterialIndices indices,
        NativeArray<TerrainMaterialJobEntry> materialEntries)
    {
        float depthBelowSurface = columnProfile.surfaceHeight - localY;
        float normalizedHeight = config.totalWorldSize.y <= 0.0001f ? 0f : Mathf.Clamp01(localY / config.totalWorldSize.y);

        // XZ-only noise: same value for all Y in a column, so horizon boundaries shift
        // uniformly and can never bleed through each other.
        float bj = config.materialBoundaryNoiseAmplitude > 0f
            ? (VoxelTerrainGenerator.EvaluateMaterialNoise2D(
                localX, localZ, context,
                2573.1f, 1891.7f, // arbitrary offsets — decouple from other noise layers; changing breaks world
                config.materialBoundaryNoiseScale) * 2f - 1f)
              * config.materialBoundaryNoiseAmplitude
            : 0f;

        if (columnProfile.isOceanFloor)
        {
            // Match the shader basin stack exactly: use the column's pre-computed
            // oceanWaterDepth (how deep the seafloor is below sea level) as the sole
            // depth variable — same thresholds as the lake basin (0.9/2.5/4.5m).
            float owd = columnProfile.oceanWaterDepth;
            if (owd < 0.9f && indices.basinGravelIndex >= 0) return (byte)indices.basinGravelIndex;
            if (owd < 2.5f && indices.basinSandIndex >= 0) return (byte)indices.basinSandIndex;
            if (owd < 4.5f && indices.lakeMudIndex >= 0) return (byte)indices.lakeMudIndex;
            if (indices.clayDepositIndex >= 0) return (byte)indices.clayDepositIndex;
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

        // Cliff override: on steep columns, strip shallow soil and show rock on faces.
        // columnProfile.slopeFactor is [0,1] where ~1 is near-vertical. Use two tiers:
        // - Very steep (slopeFactor >= 0.75): expose weathered rock for all near-surface cells
        // - Moderately steep (slopeFactor >= 0.5): prevent organic/topsoil from appearing by
        //   replacing those layers with weathered rock while preserving deeper horizons.
        float slopeF = columnProfile.slopeFactor;
        bool isVeryCliff = slopeF >= 0.75f;
        bool isModerateCliff = slopeF >= 0.5f && slopeF < 0.75f;

        if (isVeryCliff)
        {
            // Expose weathered stone for anything above the weathered boundary if available.
            if (indices.weatheredStoneIndex >= 0 && depthBelowSurface <= columnProfile.weatheredBoundary + bj)
            {
                return (byte)indices.weatheredStoneIndex;
            }
        }
        else if (isModerateCliff)
        {
            // Replace very shallow horizons (organic/topsoil) with weathered stone to avoid
            // soil on near-vertical surfaces.
            if ((indices.organicLayerIndex >= 0 && depthBelowSurface <= columnProfile.organicThickness + bj)
                || (indices.topsoilIndex >= 0 && depthBelowSurface <= columnProfile.topsoilBoundary + bj))
            {
                if (indices.weatheredStoneIndex >= 0)
                    return (byte)indices.weatheredStoneIndex;
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

        // Ore veins: subsurface only (depth >= 4m below surface).
        if (depthBelowSurface >= 4f)
        {
            float ironVeinNoise = VoxelTerrainGenerator.EvaluateMaterialNoise3D(localX, localY, localZ, context, 2917.5f, 22f);
            float copperVeinNoise = VoxelTerrainGenerator.EvaluateMaterialNoise3D(localX, localY, localZ, context, 3301.9f, 26f);

            // Iron: common in greywacke bands — lower threshold than copper.
            if (indices.ironVeinIndex >= 0 &&
                depthBelowSurface <= 18f + bj &&
                ironVeinNoise > 0.74f)
            {
                return (byte)indices.ironVeinIndex;
            }

            // Copper: rare basalt-hosted clusters — only where iron probability isn't met.
            if (indices.copperVeinIndex >= 0 &&
                depthBelowSurface >= 8f + bj &&
                copperVeinNoise > 0.80f)
            {
                return (byte)indices.copperVeinIndex;
            }
        }

        if (indices.weatheredStoneIndex >= 0 && depthBelowSurface <= columnProfile.weatheredBoundary + bj)
        {
            return (byte)indices.weatheredStoneIndex;
        }

        for (int i = 0; i < materialEntries.Length; i++)
        {
            TerrainMaterialJobEntry entry = materialEntries[i];
            if (depthBelowSurface < entry.depthRangeMeters.x + bj || depthBelowSurface > entry.depthRangeMeters.y + bj)
            {
                continue;
            }

            if (normalizedHeight < entry.normalizedHeightRange.x || normalizedHeight > entry.normalizedHeightRange.y)
            {
                continue;
            }

            float materialNoise = VoxelTerrainGenerator.EvaluatePerlin3D(
                (localX + context.materialOffsetX + entry.noiseOffsetX) / entry.distributionNoiseScaleMeters,
                (localY + context.materialOffsetY + entry.noiseOffsetY) / entry.distributionNoiseScaleMeters,
                (localZ + context.materialOffsetZ + entry.noiseOffsetZ) / entry.distributionNoiseScaleMeters);
            if (materialNoise >= entry.distributionNoiseThreshold)
            {
                return (byte)entry.materialIndex;
            }
        }

        if (indices.bedrockIndex >= 0)
        {
            return (byte)indices.bedrockIndex;
        }

        return indices.fallbackIndex;
    }

    public static float SampleSurfaceHeightPrepass(
        float localX,
        float localZ,
        NativeArray<float> surfaceHeightPrepass,
        in TerrainGenerationNumericConfig config)
    {
        if (!surfaceHeightPrepass.IsCreated || surfaceHeightPrepass.Length == 0)
        {
            return 0f;
        }

        float sampleX = Mathf.Clamp(localX / config.voxelSizeMeters, 0f, config.totalSamplesX - 1f);
        float sampleZ = Mathf.Clamp(localZ / config.voxelSizeMeters, 0f, config.totalSamplesZ - 1f);
        int minX = Mathf.Clamp(Mathf.FloorToInt(sampleX), 0, config.totalSamplesX - 1);
        int maxX = Mathf.Clamp(minX + 1, 0, config.totalSamplesX - 1);
        int minZ = Mathf.Clamp(Mathf.FloorToInt(sampleZ), 0, config.totalSamplesZ - 1);
        int maxZ = Mathf.Clamp(minZ + 1, 0, config.totalSamplesZ - 1);
        float blendX = sampleX - minX;
        float blendZ = sampleZ - minZ;

        float bottom = Mathf.Lerp(
            surfaceHeightPrepass[GetSurfacePrepassIndex(minX, minZ, config.totalSamplesX)],
            surfaceHeightPrepass[GetSurfacePrepassIndex(maxX, minZ, config.totalSamplesX)],
            blendX);
        float top = Mathf.Lerp(
            surfaceHeightPrepass[GetSurfacePrepassIndex(minX, maxZ, config.totalSamplesX)],
            surfaceHeightPrepass[GetSurfacePrepassIndex(maxX, maxZ, config.totalSamplesX)],
            blendX);
        return Mathf.Lerp(bottom, top, blendZ);
    }

    public static float EvaluateDensity(
        float localX,
        float localY,
        float localZ,
        float surfaceHeight,
        in TerrainGenerationNumericConfig config,
        in GenerationContext context)
    {
        float density = surfaceHeight - localY;

        float depthBelowSurface = surfaceHeight - localY;
        if (depthBelowSurface > config.caveStartDepthMeters)
        {
            float caveNoise = VoxelTerrainGenerator.EvaluatePerlin3D(
                (localX + context.caveOffsetX) / config.caveNoiseScaleMeters,
                (localY + context.caveOffsetY) / config.caveNoiseScaleMeters,
                (localZ + context.caveOffsetZ) / config.caveNoiseScaleMeters);
            if (caveNoise > config.caveNoiseThreshold)
            {
                float carveAmount = (caveNoise - config.caveNoiseThreshold) / Mathf.Max(0.0001f, 1f - config.caveNoiseThreshold);
                density -= carveAmount * config.caveCarveStrengthMeters;
            }
        }

        return density;
    }

    /// <summary>
    /// Computes the terrain surface height (local Y in metres) at a given XZ column.
    /// Applies domain warp → fractal base → ridge noise → detail noise → mountain ridges
    /// → island radial profile in sequence, then clamps to world height bounds.
    /// </summary>
    /// <param name="localX">Local X coordinate of the column in metres.</param>
    /// <param name="localZ">Local Z coordinate of the column in metres.</param>
    /// <param name="config">Numeric configuration snapshot from the <see cref="ProceduralVoxelTerrain"/> inspector.</param>
    /// <param name="context">Seeded noise offsets for this terrain instance.</param>
    /// <returns>Terrain surface height in metres in local space, clamped to [0, worldHeight).</returns>
    public static float EvaluateSurfaceHeight(
        float localX,
        float localZ,
        in TerrainGenerationNumericConfig config,
        in GenerationContext context)
    {
        // Domain warp: perturb XZ inputs before fractal noise for organic, meander-free ridges.
        float warpedX = localX;
        float warpedZ = localZ;
        if (config.domainWarpStrengthMeters > 0f && config.domainWarpScaleMeters > 0.0001f)
        {
            float wx = (noise.cnoise(new float2(
                (localX + context.domainWarpOffsetX) / config.domainWarpScaleMeters,
                (localZ + context.domainWarpOffsetZ) / config.domainWarpScaleMeters)) + 1f) * 0.5f;
            float wz = (noise.cnoise(new float2(
                (localX + context.domainWarpOffsetX + 5.2f) / config.domainWarpScaleMeters,
                (localZ + context.domainWarpOffsetZ + 1.3f) / config.domainWarpScaleMeters)) + 1f) * 0.5f;
            warpedX = localX + (wx * 2f - 1f) * config.domainWarpStrengthMeters;
            warpedZ = localZ + (wz * 2f - 1f) * config.domainWarpStrengthMeters;
        }

        float fractalNoise = VoxelTerrainGenerator.EvaluateFractalNoise(
            (warpedX + context.surfaceOffsetX) / config.surfaceNoiseScaleMeters,
            (warpedZ + context.surfaceOffsetZ) / config.surfaceNoiseScaleMeters,
            config.surfaceNoiseOctaves,
            config.surfaceNoisePersistence,
            config.surfaceNoiseLacunarity);
        float ridgeNoise = (noise.cnoise(new float2(
            (warpedX + context.ridgeOffsetX) / config.ridgeNoiseScaleMeters,
            (warpedZ + context.ridgeOffsetZ) / config.ridgeNoiseScaleMeters)) + 1f) * 0.5f;
        ridgeNoise = 1f - Mathf.Abs((ridgeNoise * 2f) - 1f);
        ridgeNoise *= ridgeNoise;
        float detailNoise = (noise.cnoise(new float2(
            (warpedX + context.detailOffsetX) / config.detailNoiseScaleMeters,
            (warpedZ + context.detailOffsetZ) / config.detailNoiseScaleMeters)) + 1f) * 0.5f;
        detailNoise = (detailNoise - 0.5f) * 2f;

        float landHeight = config.baseSurfaceHeightMeters
            + ((fractalNoise - 0.5f) * 2f * config.surfaceAmplitudeMeters)
            + (ridgeNoise * config.ridgeAmplitudeMeters)
            + (detailNoise * config.detailAmplitudeMeters);

        // Olympic Mountain ridges: ridged multifractal noise layered onto elevated terrain.
        // Uses domain-warped XZ so ridges follow organic curves rather than grid artifacts.
        // Only activates where terrain is already high (blend gate), keeping valley floors flat.
        if (config.mountainRidgeAmplitudeMeters > 0f && config.mountainRidgeScaleMeters > 0.0001f)
        {
            float mountainRidge = VoxelTerrainGenerator.EvaluateRidgedMultifractal(
                (warpedX + context.mountainRidgeOffsetX) / config.mountainRidgeScaleMeters,
                (warpedZ + context.mountainRidgeOffsetZ) / config.mountainRidgeScaleMeters,
                4, 0.5f, 2.1f);
            float mountainBlend = VoxelTerrainGenerator.SmoothStep01(
                (landHeight - config.mountainBaseHeightMeters)
                / Mathf.Max(0.001f, config.mountainBlendRangeMeters));
            landHeight += mountainRidge * config.mountainRidgeAmplitudeMeters * mountainBlend;
        }

        // Clamp land height to at least sea level + beach height so interior terrain
        // never dips below sea level and gets incorrectly painted with ocean-floor materials.
        float minLandHeight = config.seaLevelMeters + config.beachHeightMeters;
        landHeight = Mathf.Max(landHeight, minLandHeight);

        if (!config.shapeAsIsland)
        {
            return Mathf.Clamp(landHeight, 0f, config.totalWorldSize.y - config.voxelSizeMeters);
        }

        float islandDistance = VoxelTerrainGenerator.EvaluateIslandDistanceShaped(localX, localZ, config.totalWorldSize, config.islandShapeNoiseScale, config.islandShapeNoiseStrength, context.islandShapeOffsetX, context.islandShapeOffsetZ);
        float shelfBlend = VoxelTerrainGenerator.SmoothStep01((islandDistance - config.islandCoreRadiusNormalized) / Mathf.Max(0.0001f, config.coastalShelfWidthNormalized));
        float shorelineTargetHeight = config.seaLevelMeters + config.beachHeightMeters;
        float shelfHeight = Mathf.Lerp(landHeight, shorelineTargetHeight, shelfBlend);

        float oceanNoise = (noise.cnoise(new float2(
            (localX + context.oceanFloorOffsetX) / config.oceanFloorNoiseScaleMeters,
            (localZ + context.oceanFloorOffsetZ) / config.oceanFloorNoiseScaleMeters)) + 1f) * 0.5f;
        float oceanFloorHeight = Mathf.Max(
            0f,
            config.seaLevelMeters
            - config.oceanFloorDepthMeters
            + ((oceanNoise - 0.5f) * 2f * config.oceanFloorVariationMeters));
        float oceanBlend = VoxelTerrainGenerator.SmoothStep01(
            (islandDistance - (config.islandCoreRadiusNormalized + config.coastalShelfWidthNormalized))
            / Mathf.Max(0.0001f, 1f - (config.islandCoreRadiusNormalized + config.coastalShelfWidthNormalized)));
        float worldHeight = Mathf.Lerp(shelfHeight, oceanFloorHeight, oceanBlend);
        return Mathf.Clamp(worldHeight, 0f, config.totalWorldSize.y - config.voxelSizeMeters);
    }

    /// <summary>
    /// Builds the full per-column material profile for a single XZ column.
    /// Evaluates moisture, soil-thickness, and slope noise; determines whether the column
    /// is beach, ocean floor, or inland; then computes cumulative depth boundaries for every
    /// soil horizon (organic → topsoil → eluviation → subsoil → parent → weathered).
    /// This Burst-compatible overload reads the pre-computed surface-height prepass via a
    /// <see cref="Unity.Collections.NativeArray{T}"/> for slope calculation.
    /// </summary>
    /// <param name="localX">Local X coordinate of the column in metres.</param>
    /// <param name="localZ">Local Z coordinate of the column in metres.</param>
    /// <param name="surfaceHeight">Pre-computed surface height for this column (from <see cref="EvaluateSurfaceHeight"/>).</param>
    /// <param name="config">Numeric configuration snapshot from the <see cref="ProceduralVoxelTerrain"/> inspector.</param>
    /// <param name="context">Seeded noise offsets for this terrain instance.</param>
    /// <param name="surfaceHeightPrepass">Flat NativeArray of surface heights indexed by column, used for slope finite differences.</param>
    /// <returns>A fully populated <see cref="ColumnMaterialProfile"/> for this column.</returns>
    public static ColumnMaterialProfile BuildColumnMaterialProfile(
        float localX,
        float localZ,
        float surfaceHeight,
        in TerrainGenerationNumericConfig config,
        in GenerationContext context,
        NativeArray<float> surfaceHeightPrepass)
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

        float normalizedSurfaceHeight = config.totalWorldSize.y <= 0.0001f ? 0f : Mathf.Clamp01(surfaceHeight / config.totalWorldSize.y);
        float uplandFactor = Mathf.Clamp01((normalizedSurfaceHeight - 0.18f) / 0.72f);
        float soilRetention = Mathf.Lerp(1.08f, 0.76f, uplandFactor);
        float islandDistance = VoxelTerrainGenerator.EvaluateIslandDistanceShaped(localX, localZ, config.totalWorldSize, config.islandShapeNoiseScale, config.islandShapeNoiseStrength, context.islandShapeOffsetX, context.islandShapeOffsetZ);
        float coastalRingStart = config.islandCoreRadiusNormalized - (config.coastalShelfWidthNormalized * 0.45f);
        float coastalRingBlend = config.shapeAsIsland
            ? VoxelTerrainGenerator.SmoothStep01((islandDistance - coastalRingStart) / Mathf.Max(0.0001f, config.coastalShelfWidthNormalized * 1.15f))
            : 0f;
        float shorelineHeightRange = Mathf.Max(config.beachHeightMeters + (config.voxelSizeMeters * 3f), config.voxelSizeMeters * 4f);
        float shorelineHeightBlend = 1f - Mathf.Clamp01((surfaceHeight - config.seaLevelMeters) / shorelineHeightRange);
        float coastalFactor = Mathf.Clamp01(coastalRingBlend * shorelineHeightBlend);
        float topsoilReduction = Mathf.Lerp(1f, 0.72f, coastalFactor);
        float eluviationReduction = Mathf.Lerp(1f, 0.7f, coastalFactor);
        float subsoilReduction = Mathf.Lerp(1f, 0.68f, coastalFactor);
        float parentReduction = Mathf.Lerp(1f, 0.62f, coastalFactor);
        float oceanWaterDepth = Mathf.Max(0f, config.seaLevelMeters - surfaceHeight);
        bool isOceanFloor = config.shapeAsIsland &&
                            coastalRingBlend > 0.22f &&
                            oceanWaterDepth > config.voxelSizeMeters * 0.12f;
        float beachTransitionHeightRange = Mathf.Max(config.beachHeightMeters + (config.voxelSizeMeters * 2.6f), config.voxelSizeMeters * 3.8f);
        float beachHeightBlend = 1f - Mathf.Clamp01((surfaceHeight - config.seaLevelMeters) / beachTransitionHeightRange);
        float beachFactor = Mathf.Clamp01(coastalRingBlend * beachHeightBlend);
        float beachBoundaryNoise = config.materialBeachBoundaryNoiseAmplitude > 0f
            ? (VoxelTerrainGenerator.EvaluateMaterialNoise2D(
                localX, localZ, context,
                3079.3f, 2417.8f, // arbitrary offsets — decouple from other noise layers; changing breaks world
                config.materialBeachBoundaryNoiseScale) * 2f - 1f)
              * config.materialBeachBoundaryNoiseAmplitude
            : 0f;
        bool isBeachLand = config.shapeAsIsland &&
                           !isOceanFloor &&
                           beachFactor + beachBoundaryNoise > 0.18f &&
                           surfaceHeight >= config.seaLevelMeters + (config.voxelSizeMeters * 0.08f);
        float coastalLandHeightRange = Mathf.Max(config.beachHeightMeters + (config.voxelSizeMeters * 4.5f), config.voxelSizeMeters * 5.5f);
        bool isCoastalLand = config.shapeAsIsland &&
                             !isOceanFloor &&
                             coastalFactor > 0.12f &&
                             surfaceHeight <= config.seaLevelMeters + coastalLandHeightRange;

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
                config.voxelSizeMeters * 0.9f,
                Mathf.Lerp(config.voxelSizeMeters * 0.9f, config.voxelSizeMeters * 1.35f, moistureNoise) * coastalSoilRetention);
            topsoilThickness = Mathf.Max(
                config.voxelSizeMeters * 0.85f,
                Mathf.Lerp(config.voxelSizeMeters * 0.85f, config.voxelSizeMeters * 1.55f, soilThicknessNoise) * coastalSoilRetention);
            eluviationThickness = Mathf.Max(
                config.voxelSizeMeters * 0.8f,
                Mathf.Lerp(config.voxelSizeMeters * 0.8f, config.voxelSizeMeters * 1.35f, eluviationNoise) * Mathf.Lerp(1.01f, 0.92f, coastalFactor));
            subsoilThickness = Mathf.Lerp(config.voxelSizeMeters * 1.8f, config.voxelSizeMeters * 3.4f, moistureNoise) * Mathf.Lerp(0.96f, 0.8f, coastalFactor);
            parentThickness = Mathf.Lerp(config.voxelSizeMeters * 2.4f, config.voxelSizeMeters * 5.1f, parentNoise) * Mathf.Lerp(0.94f, 0.76f, coastalFactor);
            weatheredThickness = Mathf.Lerp(config.voxelSizeMeters * 4.2f, config.voxelSizeMeters * 8.2f, outcropNoise) * Mathf.Lerp(1.04f, 1.18f, coastalFactor);
        }
        else
        {
            organicThickness = Mathf.Max(
                config.voxelSizeMeters * 0.95f,
                Mathf.Lerp(config.voxelSizeMeters * 0.95f, config.voxelSizeMeters * 1.55f, moistureNoise) * soilRetention);
            topsoilThickness = Mathf.Max(
                config.voxelSizeMeters * 0.9f,
                Mathf.Lerp(config.voxelSizeMeters * 0.9f, config.voxelSizeMeters * 2.1f, soilThicknessNoise) * soilRetention * topsoilReduction);
            eluviationThickness = Mathf.Max(
                config.voxelSizeMeters * 0.85f,
                Mathf.Lerp(config.voxelSizeMeters * 0.85f, config.voxelSizeMeters * 1.9f, eluviationNoise) * Mathf.Lerp(1.04f, 0.9f, uplandFactor) * eluviationReduction);
            subsoilThickness = Mathf.Lerp(config.voxelSizeMeters * 2f, config.voxelSizeMeters * 4.2f, moistureNoise) * subsoilReduction;
            parentThickness = Mathf.Lerp(config.voxelSizeMeters * 3f, config.voxelSizeMeters * 6.4f, parentNoise) * parentReduction;
            weatheredThickness = Mathf.Lerp(config.voxelSizeMeters * 4f, config.voxelSizeMeters * 7.4f, outcropNoise);
        }

        // Basin carving is reflected in the prepass, so slopes at lake walls drive
        // correct soil-erosion without requiring a separate noise-only evaluation.
        float dhS = config.voxelSizeMeters;
        float hPX = SampleSurfaceHeightPrepass(localX + dhS, localZ, surfaceHeightPrepass, config);
        float hNX = SampleSurfaceHeightPrepass(localX - dhS, localZ, surfaceHeightPrepass, config);
        float hPZ = SampleSurfaceHeightPrepass(localX, localZ + dhS, surfaceHeightPrepass, config);
        float hNZ = SampleSurfaceHeightPrepass(localX, localZ - dhS, surfaceHeightPrepass, config);
        float gx = (hPX - hNX) / (2f * dhS);
        float gz = (hPZ - hNZ) / (2f * dhS);
        float slope = Mathf.Sqrt(gx * gx + gz * gz);
        float slopeFactor = Mathf.Clamp01(slope / 2.0f);

        // Lake-basin columns sit below sea level but are not ocean floor.
        // Their quartic rim walls have high gradients that would otherwise strip all sediment,
        // leaving bare bedrock at the waterline. Cap erosion so mud/sand/gravel can survive.
        bool isLakeBasin = !isOceanFloor && surfaceHeight < config.seaLevelMeters - config.voxelSizeMeters * 0.5f;
        if (isLakeBasin) slopeFactor = Mathf.Min(slopeFactor, 0.3f);

        // Coastal cliffs (high slope + near ocean) lose ALL soil — bare rock only.
        float coastalCliffFactor = slopeFactor * Mathf.Clamp01(coastalRingBlend * 2f);
        float effectiveSlopeFactor = Mathf.Max(slopeFactor, coastalCliffFactor);

        // Slope-driven erosion: steep faces lose soil, exposing weathered/bedrock.
        float slopeErosion = 1f - effectiveSlopeFactor * 0.99f;
        float slopeSoilMult = slopeErosion * slopeErosion; // quadratic falloff
        organicThickness *= slopeErosion;
        topsoilThickness *= slopeSoilMult;
        eluviationThickness *= slopeSoilMult;
        subsoilThickness *= slopeSoilMult;
        parentThickness *= slopeSoilMult;
        // Weathered stone thickens on steep slopes (exposed rock face).
        weatheredThickness = Mathf.Lerp(weatheredThickness, weatheredThickness * 2.2f, effectiveSlopeFactor);

        if (isBeachLand)
        {
            beachSandThickness = Mathf.Max(
                config.voxelSizeMeters * 0.95f,
                Mathf.Lerp(config.voxelSizeMeters * 0.95f, config.voxelSizeMeters * 1.45f, beachFactor));
            beachGravelThickness = Mathf.Max(
                config.voxelSizeMeters * 0.65f,
                Mathf.Lerp(config.voxelSizeMeters * 0.65f, config.voxelSizeMeters * 1.35f, beachFactor * 0.9f));
        }

        // Beach, ocean-floor, and lake-basin columns never show the shader rock overlay.
        // Zeroing here prevents high gradient slopeFactor from cliff neighbours bleeding in.
        if (isBeachLand || isOceanFloor || isLakeBasin) slopeFactor = 0f;

        // Per-column boundary noise: same formula as DetermineCellMaterialIndex so visual
        // and logical soil horizon boundaries are always co-located.
        float bj = config.materialBoundaryNoiseAmplitude > 0f
            ? (VoxelTerrainGenerator.EvaluateMaterialNoise2D(
                localX, localZ, context,
                2573.1f, 1891.7f, // arbitrary offsets — decouple from other noise layers; changing breaks world
                config.materialBoundaryNoiseScale) * 2f - 1f)
              * config.materialBoundaryNoiseAmplitude
            : 0f;

        ColumnMaterialProfile profile = new ColumnMaterialProfile
        {
            surfaceHeight = surfaceHeight,
            beachSandBoundary = beachSandThickness,
            beachGravelBoundary = beachSandThickness + beachGravelThickness,
            organicThickness = organicThickness,
            oceanWaterDepth = oceanWaterDepth,
            isBeachLand = isBeachLand,
            isOceanFloor = isOceanFloor,
            beachFactor = beachFactor,
            oceanFactor = coastalRingBlend,
            slopeFactor = slopeFactor,
            materialBoundaryNoise = bj
        };
        profile.topsoilBoundary = profile.organicThickness + topsoilThickness;
        profile.eluviationBoundary = profile.topsoilBoundary + eluviationThickness;
        profile.subsoilBoundary = profile.eluviationBoundary + subsoilThickness;
        profile.parentBoundary = profile.subsoilBoundary + parentThickness;
        profile.weatheredBoundary = profile.parentBoundary + weatheredThickness;
        return profile;
    }
}

[BurstCompile]
internal struct TerrainSurfacePrepassJob : IJobParallelFor
{
    public NativeArray<float> SurfaceHeights;

    [ReadOnly] public TerrainGenerationNumericConfig Config;
    [ReadOnly] public GenerationContext Context;

    public void Execute(int index)
    {
        int sampleX = index % Config.totalSamplesX;
        int sampleZ = index / Config.totalSamplesX;
        float localX = sampleX * Config.voxelSizeMeters;
        float localZ = sampleZ * Config.voxelSizeMeters;
        SurfaceHeights[index] =
            TerrainGenerationJobsUtility.EvaluateSurfaceHeight(localX, localZ, Config, Context);
    }
}

[BurstCompile]
internal struct TerrainColumnProfileJob : IJobParallelFor
{
    [WriteOnly] public NativeArray<ColumnMaterialProfile> Profiles;

    [ReadOnly] public NativeArray<float> SurfaceHeights;
    [ReadOnly] public TerrainGenerationNumericConfig Config;
    [ReadOnly] public GenerationContext Context;

    public int TotalCellsX;

    public void Execute(int index)
    {
        int cellX = index % TotalCellsX;
        int cellZ = index / TotalCellsX;
        float localX = (cellX + 0.5f) * Config.voxelSizeMeters;
        float localZ = (cellZ + 0.5f) * Config.voxelSizeMeters;
        float surfaceHeight = TerrainGenerationJobsUtility.SampleSurfaceHeightPrepass(localX, localZ, SurfaceHeights, Config);
        Profiles[index] = TerrainGenerationJobsUtility.BuildColumnMaterialProfile(localX, localZ, surfaceHeight, Config, Context, SurfaceHeights);
    }
}

[BurstCompile]
internal struct TerrainSampleColumnProfileJob : IJobParallelFor
{
    [WriteOnly] public NativeArray<ColumnMaterialProfile> SampleProfiles;
    [ReadOnly] public NativeArray<float> SurfaceHeights;
    [ReadOnly] public TerrainGenerationNumericConfig Config;
    [ReadOnly] public GenerationContext Context;
    public int TotalSamplesX;

    public void Execute(int index)
    {
        int sampleX = index % TotalSamplesX;
        int sampleZ = index / TotalSamplesX;
        float localX = sampleX * Config.voxelSizeMeters;
        float localZ = sampleZ * Config.voxelSizeMeters;
        SampleProfiles[index] = TerrainGenerationJobsUtility.BuildColumnMaterialProfile(
            localX, localZ, SurfaceHeights[index], Config, Context, SurfaceHeights);
    }
}

[BurstCompile]
internal struct TerrainSampleMaterialsJob : IJobParallelFor
{
    [WriteOnly] public NativeArray<byte> SampleMaterials;
    [ReadOnly] public NativeArray<ColumnMaterialProfile> SampleProfiles;
    [ReadOnly] public NativeArray<TerrainMaterialJobEntry> MaterialEntries;
    [ReadOnly] public TerrainGenerationNumericConfig Config;
    [ReadOnly] public GenerationContext Context;
    [ReadOnly] public GenerationMaterialIndices MaterialIndices;
    public int TotalSamplesX;
    public int TotalSamplesY;

    public void Execute(int index)
    {
        int sampleX = index % TotalSamplesX;
        int yz = index / TotalSamplesX;
        int sampleY = yz % TotalSamplesY;
        int sampleZ = yz / TotalSamplesY;
        float localX = sampleX * Config.voxelSizeMeters;
        float localY = sampleY * Config.voxelSizeMeters;
        float localZ = sampleZ * Config.voxelSizeMeters;
        int columnIndex = TerrainGenerationJobsUtility.GetSurfacePrepassIndex(sampleX, sampleZ, TotalSamplesX);
        SampleMaterials[index] = TerrainGenerationJobsUtility.DetermineCellMaterialIndex(
            localX, localY, localZ, Context, SampleProfiles[columnIndex], Config, MaterialIndices, MaterialEntries);
    }
}

[BurstCompile]
internal struct TerrainDensityRowJob : IJobParallelFor
{
    [WriteOnly] public NativeArray<float> DensityRow;

    [ReadOnly] public NativeArray<float> SurfaceHeights;
    [ReadOnly] public TerrainGenerationNumericConfig Config;
    [ReadOnly] public GenerationContext Context;

    public void Execute(int index)
    {
        int sampleX = index % Config.totalSamplesX;
        int yz = index / Config.totalSamplesX;
        int sampleY = yz % Config.totalSamplesY;
        int sampleZ = yz / Config.totalSamplesY;
        float localX = sampleX * Config.voxelSizeMeters;
        float localY = sampleY * Config.voxelSizeMeters;
        float localZ = sampleZ * Config.voxelSizeMeters;
        float surfaceHeight = SurfaceHeights[TerrainGenerationJobsUtility.GetSurfacePrepassIndex(sampleX, sampleZ, Config.totalSamplesX)];
        DensityRow[index] = TerrainGenerationJobsUtility.EvaluateDensity(localX, localY, localZ, surfaceHeight, Config, Context);
    }
}

internal struct TerrainMaterialJobEntry
{
    public int materialIndex;
    public Vector2 depthRangeMeters;
    public Vector2 normalizedHeightRange;
    public float distributionNoiseScaleMeters;
    public float distributionNoiseThreshold;
    public float noiseOffsetX;
    public float noiseOffsetY;
    public float noiseOffsetZ;
}

[BurstCompile]
internal struct TerrainCellMaterialsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<ColumnMaterialProfile> ColumnProfiles;
    [WriteOnly] public NativeArray<byte> CellMaterials;
    [ReadOnly] public NativeArray<float> SurfaceHeights;
    [ReadOnly] public NativeArray<TerrainMaterialJobEntry> MaterialEntries;
    [ReadOnly] public TerrainGenerationNumericConfig Config;
    [ReadOnly] public GenerationContext Context;
    [ReadOnly] public GenerationMaterialIndices MaterialIndices;
    public int TotalCellsX;
    public int TotalCellsY;

    public void Execute(int index)
    {
        int cellX = index % TotalCellsX;
        int yz = index / TotalCellsX;
        int cellY = yz % TotalCellsY;
        int cellZ = yz / TotalCellsY;
        float localX = (cellX + 0.5f) * Config.voxelSizeMeters;
        float localY = (cellY + 0.5f) * Config.voxelSizeMeters;
        float localZ = (cellZ + 0.5f) * Config.voxelSizeMeters;
        int columnIndex = TerrainGenerationJobsUtility.GetColumnPrepassIndex(cellX, cellZ, TotalCellsX);
        ColumnMaterialProfile columnProfile = ColumnProfiles[columnIndex];
        CellMaterials[index] = TerrainGenerationJobsUtility.DetermineCellMaterialIndex(
            localX, localY, localZ, Context, columnProfile, Config, MaterialIndices, MaterialEntries);
    }
}

public partial class ProceduralVoxelTerrain
{
    private enum TerrainGenerationPhase
    {
        None,
        SurfacePrepass,
        DensityField,
        LakePositionPrepass,
        BasinCarvingPrepass,
        ColumnPrepass,
        CellMaterials,
        ChunkObjects,
        ChunkMeshBuildData,
        ChunkMeshCommit,
        Complete
    }

    private sealed class ChunkMeshBuildResult
    {
        public readonly Vector3Int chunkCoordinate;
        public readonly ChunkMeshBuilder.MeshBuildData buildData;

        public ChunkMeshBuildResult(Vector3Int chunkCoordinate, ChunkMeshBuilder.MeshBuildData buildData)
        {
            this.chunkCoordinate = chunkCoordinate;
            this.buildData = buildData;
        }
    }

    private sealed class TerrainGenerationOperation
    {
        private const int TerrainGenerationJobBatchSize = 64;

        private readonly ProceduralVoxelTerrain owner;
        private readonly bool clearExisting;
        private readonly Queue<ChunkMeshBuildResult> pendingChunkCommits = new Queue<ChunkMeshBuildResult>();
        private readonly List<Vector3Int> chunkCoordinates = new List<Vector3Int>();
        private readonly bool collectTimings;
        private readonly bool useParallelGenerationJobs;
        private readonly System.Diagnostics.Stopwatch totalStopwatch;
        private readonly System.Diagnostics.Stopwatch phaseStopwatch;
        private readonly float totalWorkUnits;

        private GenerationContext context;
        private TerrainGenerationNumericConfig numericConfig;
        private HashSet<Vector3Int> surfaceChunkSet;
        private int surfacePrepassZ;
        private int columnProfileZ;
        private int densityZ;
        private int materialZ;
        private bool lakePositionPrepassDone;
        private bool basinCarvingPrepassDone;
        private int builtChunkCount;
        private int committedChunkCount;
        private bool chunkObjectsCreated;
        private bool initialized;
        private int runtimeStreamingStartupChunkTarget;
        private bool runtimeStreamingStartupChunksNotified;
        private NativeArray<float> nativeSurfaceHeightPrepass;
        private NativeArray<ColumnMaterialProfile> nativeColumnProfiles;
        private NativeArray<float> nativeDensitySamples;
        private NativeArray<byte> nativeCellMaterials;
        private NativeArray<TerrainMaterialJobEntry> nativeMaterialEntries;

        public TerrainGenerationOperation(ProceduralVoxelTerrain owner, bool clearExisting)
        {
            this.owner = owner;
            this.clearExisting = clearExisting;
            collectTimings = owner.logGenerationTimings;
            useParallelGenerationJobs = owner.ShouldUseParallelGenerationJobs();
            totalStopwatch = collectTimings ? System.Diagnostics.Stopwatch.StartNew() : null;
            phaseStopwatch = collectTimings ? System.Diagnostics.Stopwatch.StartNew() : null;
            totalWorkUnits = Mathf.Max(
                1f,
                owner.TotalSamplesZ    // SurfacePrepass
                + owner.TotalSamplesZ  // DensityField
                + 1f                   // LakePositionPrepass
                + 1f                   // BasinCarvingPrepass
                + owner.TotalCellsZ    // ColumnPrepass
                + owner.TotalCellsZ    // CellMaterials
                + 1f                   // ChunkObjects
                + (owner.TotalChunkCount * 2f));

            Phase = TerrainGenerationPhase.None;
            Status = "Preparing voxel terrain generation";
        }

        public TerrainGenerationPhase Phase { get; private set; }
        public string Status { get; private set; }
        public float Progress01 { get; private set; }
        public bool IsDone { get; private set; }
        public bool Success { get; private set; }
        public long PrepassMilliseconds { get; private set; }
        public long DensityMilliseconds { get; private set; }
        public long MaterialMilliseconds { get; private set; }
        public long ChunkObjectMilliseconds { get; private set; }
        public long MeshMilliseconds { get; private set; }
        public long TotalMilliseconds => totalStopwatch?.ElapsedMilliseconds ?? 0L;

        public void Dispose()
        {
            // If a background mesh-build task is still running, wait for it to finish
            // before releasing NativeArrays so the task doesn't reference freed memory.
            if (pendingParallelMeshTask != null && !pendingParallelMeshTask.IsCompleted)
            {
                try { pendingParallelMeshTask.Wait(); } catch { /* swallow — generation was cancelled */ }
            }
            pendingParallelMeshTask = null;

            // Wait for any in-flight Physics.BakeMesh tasks so they don't race with cleanup.
            while (pendingColliderBakes.Count > 0)
            {
                (_, _, Task bakeTask) = pendingColliderBakes.Dequeue();
                if (!bakeTask.IsCompleted)
                {
                    try { bakeTask.Wait(); } catch { /* swallow — generation was cancelled */ }
                }
            }

            if (nativeSurfaceHeightPrepass.IsCreated)
            {
                nativeSurfaceHeightPrepass.Dispose();
            }

            if (nativeColumnProfiles.IsCreated)
            {
                nativeColumnProfiles.Dispose();
            }

            if (nativeDensitySamples.IsCreated)
            {
                nativeDensitySamples.Dispose();
            }

            if (nativeCellMaterials.IsCreated)
            {
                nativeCellMaterials.Dispose();
            }

            if (nativeMaterialEntries.IsCreated)
            {
                nativeMaterialEntries.Dispose();
            }
        }

        public void Step()
        {
            if (IsDone)
            {
                return;
            }

            if (!initialized)
            {
                Initialize();
            }

            switch (Phase)
            {
                case TerrainGenerationPhase.SurfacePrepass:
                    StepSurfacePrepass();
                    break;
                case TerrainGenerationPhase.DensityField:
                    StepDensityField();
                    break;
                case TerrainGenerationPhase.LakePositionPrepass:
                    StepLakePositionPrepass();
                    break;
                case TerrainGenerationPhase.BasinCarvingPrepass:
                    StepBasinCarvingPrepass();
                    break;
                case TerrainGenerationPhase.ColumnPrepass:
                    StepColumnProfiles();
                    break;
                case TerrainGenerationPhase.CellMaterials:
                    StepCellMaterials();
                    break;
                case TerrainGenerationPhase.ChunkObjects:
                    StepChunkObjects();
                    break;
                case TerrainGenerationPhase.ChunkMeshBuildData:
                    StepChunkMeshBuildData();
                    break;
                case TerrainGenerationPhase.ChunkMeshCommit:
                    StepChunkMeshCommit();
                    break;
                case TerrainGenerationPhase.Complete:
                    Complete();
                    break;
            }

            UpdateProgress();
        }

        private void Initialize()
        {
            if (owner.randomizeSeed)
            {
                owner.seed = Environment.TickCount;
            }

            if (clearExisting)
            {
                owner.ClearGeneratedTerrainForRegeneration();
            }

            if (owner.materialDefinitions == null || owner.materialDefinitions.Count == 0)
            {
                owner.ApplyOlympicRainforestPreset();
            }
            else
            {
                owner.EnsureDefaultMaterialDefinitionsPresent();
            }

            owner.generationMaterialIndices = owner.BuildGenerationMaterialIndices();
            owner.sharedTerrainMaterial = owner.BuildSharedMaterial();
            owner.densitySamples = new float[owner.TotalSamplesX * owner.TotalSamplesY * owner.TotalSamplesZ];
            owner.cellMaterialIndices = new byte[owner.TotalCellsX * owner.TotalCellsY * owner.TotalCellsZ];
            owner.surfaceHeightPrepassReady = false;
            owner.surfaceHeightPrepass = new float[owner.TotalSamplesX * owner.TotalSamplesZ];
            owner.columnProfilePrepass = new ColumnMaterialProfile[owner.TotalCellsX * owner.TotalCellsZ];
            context = VoxelTerrainGenerator.BuildGenerationContext(owner.seed);
            numericConfig = owner.BuildTerrainGenerationNumericConfig();
            owner.lastGenerationContext = context;
            owner.lastGenerationSettings = owner.BuildTerrainGenerationSettings();
            if (useParallelGenerationJobs)
            {
                nativeSurfaceHeightPrepass = new NativeArray<float>(owner.surfaceHeightPrepass.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                nativeColumnProfiles = new NativeArray<ColumnMaterialProfile>(owner.TotalCellsX * owner.TotalCellsZ, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                nativeDensitySamples = new NativeArray<float>(owner.TotalSamplesX * owner.TotalSamplesY * owner.TotalSamplesZ, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                nativeCellMaterials = new NativeArray<byte>(owner.TotalCellsX * owner.TotalCellsY * owner.TotalCellsZ, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                GenerationMaterialIndices matIndices = owner.generationMaterialIndices;
                var entries = new System.Collections.Generic.List<TerrainMaterialJobEntry>();
                for (int i = 0; i < owner.materialDefinitions.Count; i++)
                {
                    VoxelTerrainMaterialDefinition def = owner.materialDefinitions[i];
                    if (def == null || def.isFallbackMaterial ||
                        VoxelTerrainGenerator.IsReservedDefaultMaterialIndex(i, matIndices) ||
                        VoxelTerrainGenerator.IsExcludedFromBaseTerrainNoise(def))
                    {
                        continue;
                    }

                    entries.Add(new TerrainMaterialJobEntry
                    {
                        materialIndex = i,
                        depthRangeMeters = def.depthRangeMeters,
                        normalizedHeightRange = def.normalizedHeightRange,
                        distributionNoiseScaleMeters = def.distributionNoiseScaleMeters,
                        distributionNoiseThreshold = def.distributionNoiseThreshold,
                        noiseOffsetX = i * 137.17f,
                        noiseOffsetY = i * 59.11f,
                        noiseOffsetZ = i * 83.37f,
                    });
                }

                nativeMaterialEntries = new NativeArray<TerrainMaterialJobEntry>(entries.Count, Allocator.Persistent);
                for (int i = 0; i < entries.Count; i++)
                {
                    nativeMaterialEntries[i] = entries[i];
                }
            }

            Phase = TerrainGenerationPhase.SurfacePrepass;
            initialized = true;
            UpdateProgress();
        }

        private void StepSurfacePrepass()
        {
            if (useParallelGenerationJobs)
            {
                new TerrainSurfacePrepassJob
                {
                    SurfaceHeights = nativeSurfaceHeightPrepass,
                    Config = numericConfig,
                    Context = context,
                }.Schedule(owner.TotalSamplesX * owner.TotalSamplesZ, owner.TotalSamplesX).Complete();
                nativeSurfaceHeightPrepass.CopyTo(owner.surfaceHeightPrepass);
                owner.surfaceHeightPrepassReady = true;
                surfacePrepassZ = owner.TotalSamplesZ;
                Phase = TerrainGenerationPhase.DensityField;
                return;
            }

            float localZ = surfacePrepassZ * owner.voxelSizeMeters;
            for (int x = 0; x < owner.TotalSamplesX; x++)
            {
                float localX = x * owner.voxelSizeMeters;
                owner.surfaceHeightPrepass[owner.GetSurfacePrepassIndex(x, surfacePrepassZ)] = owner.EvaluateSurfaceHeight(localX, localZ, context);
            }

            surfacePrepassZ++;
            if (surfacePrepassZ >= owner.TotalSamplesZ)
            {
                owner.surfaceHeightPrepassReady = true;
                Phase = TerrainGenerationPhase.DensityField;
            }
        }

        private void StepColumnProfiles()
        {
            if (useParallelGenerationJobs)
            {
                new TerrainColumnProfileJob
                {
                    Profiles = nativeColumnProfiles,
                    SurfaceHeights = nativeSurfaceHeightPrepass,
                    Config = numericConfig,
                    Context = context,
                    TotalCellsX = owner.TotalCellsX,
                }.Schedule(owner.TotalCellsX * owner.TotalCellsZ, owner.TotalCellsX).Complete();
                nativeColumnProfiles.CopyTo(owner.columnProfilePrepass);
                columnProfileZ = owner.TotalCellsZ;
                if (collectTimings && phaseStopwatch != null)
                {
                    PrepassMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                    phaseStopwatch.Restart();
                }

                Phase = TerrainGenerationPhase.CellMaterials;
                return;
            }

            float localZ = (columnProfileZ + 0.5f) * owner.voxelSizeMeters;
            for (int x = 0; x < owner.TotalCellsX; x++)
            {
                float localX = (x + 0.5f) * owner.voxelSizeMeters;
                float surfaceHeight = owner.SampleSurfaceHeightPrepass(localX, localZ);
                owner.columnProfilePrepass[owner.GetColumnPrepassIndex(x, columnProfileZ)] =
                    owner.BuildColumnMaterialProfile(localX, localZ, surfaceHeight, context);
            }

            columnProfileZ++;
            if (columnProfileZ >= owner.TotalCellsZ)
            {
                if (collectTimings && phaseStopwatch != null)
                {
                    PrepassMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                    phaseStopwatch.Restart();
                }

                Phase = TerrainGenerationPhase.CellMaterials;
            }
        }

        private void StepDensityField()
        {
            if (useParallelGenerationJobs)
            {
                new TerrainDensityRowJob
                {
                    DensityRow = nativeDensitySamples,
                    SurfaceHeights = nativeSurfaceHeightPrepass,
                    Config = numericConfig,
                    Context = context,
                }.Schedule(owner.TotalSamplesX * owner.TotalSamplesY * owner.TotalSamplesZ, owner.TotalSamplesX * owner.TotalSamplesY).Complete();
                nativeDensitySamples.CopyTo(owner.densitySamples);
                densityZ = owner.TotalSamplesZ;
                if (collectTimings && phaseStopwatch != null)
                {
                    DensityMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                    phaseStopwatch.Restart();
                }

                Phase = TerrainGenerationPhase.LakePositionPrepass;
                return;
            }

            float localZ = densityZ * owner.voxelSizeMeters;
            for (int x = 0; x < owner.TotalSamplesX; x++)
            {
                float localX = x * owner.voxelSizeMeters;
                float surfaceHeight = owner.GetSurfaceHeightFromPrepass(x, densityZ);
                for (int y = 0; y < owner.TotalSamplesY; y++)
                {
                    float localY = y * owner.voxelSizeMeters;
                    owner.densitySamples[owner.GetSampleIndex(x, y, densityZ)] =
                        owner.EvaluateDensity(localX, localY, localZ, surfaceHeight, context);
                }
            }

            densityZ++;
            if (densityZ >= owner.TotalSamplesZ)
            {
                if (collectTimings && phaseStopwatch != null)
                {
                    DensityMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                    phaseStopwatch.Restart();
                }

                Phase = TerrainGenerationPhase.LakePositionPrepass;
            }
        }

        private void StepCellMaterials()
        {
            if (useParallelGenerationJobs)
            {
                new TerrainCellMaterialsJob
                {
                    ColumnProfiles = nativeColumnProfiles,
                    CellMaterials = nativeCellMaterials,
                    SurfaceHeights = nativeSurfaceHeightPrepass,
                    MaterialEntries = nativeMaterialEntries,
                    Config = numericConfig,
                    Context = context,
                    MaterialIndices = owner.generationMaterialIndices,
                    TotalCellsX = owner.TotalCellsX,
                    TotalCellsY = owner.TotalCellsY,
                }.Schedule(owner.TotalCellsX * owner.TotalCellsY * owner.TotalCellsZ, owner.TotalCellsX * owner.TotalCellsY).Complete();
                nativeCellMaterials.CopyTo(owner.cellMaterialIndices);

                // Sanity check: ensure the managed array was populated with the expected length.
                int expectedCells = owner.TotalCellsX * owner.TotalCellsY * owner.TotalCellsZ;
                if (owner.cellMaterialIndices == null || owner.cellMaterialIndices.Length != expectedCells)
                {
                    Debug.LogError($"[TerrainGenerationOperation] cellMaterialIndices length mismatch: expected {expectedCells}, got {(owner.cellMaterialIndices == null ? 0 : owner.cellMaterialIndices.Length)}");
                }

                materialZ = owner.TotalCellsZ;
                if (collectTimings && phaseStopwatch != null)
                {
                    MaterialMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                    phaseStopwatch.Restart();
                }

                Phase = TerrainGenerationPhase.ChunkObjects;
                return;
            }

            float localZ = (materialZ + 0.5f) * owner.voxelSizeMeters;
            for (int x = 0; x < owner.TotalCellsX; x++)
            {
                float localX = (x + 0.5f) * owner.voxelSizeMeters;
                ColumnMaterialProfile columnProfile = owner.columnProfilePrepass[owner.GetColumnPrepassIndex(x, materialZ)];
                for (int y = 0; y < owner.TotalCellsY; y++)
                {
                    float localY = (y + 0.5f) * owner.voxelSizeMeters;
                    owner.cellMaterialIndices[owner.GetCellIndex(x, y, materialZ)] =
                        owner.DetermineCellMaterialIndex(localX, localY, localZ, context, columnProfile);
                }
            }

            materialZ++;
            if (materialZ >= owner.TotalCellsZ)
            {
                if (collectTimings && phaseStopwatch != null)
                {
                    MaterialMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                    phaseStopwatch.Restart();
                }

                // Sanity check for serial path as well.
                int expectedCells = owner.TotalCellsX * owner.TotalCellsY * owner.TotalCellsZ;
                if (owner.cellMaterialIndices == null || owner.cellMaterialIndices.Length != expectedCells)
                {
                    Debug.LogError($"[TerrainGenerationOperation] cellMaterialIndices length mismatch: expected {expectedCells}, got {(owner.cellMaterialIndices == null ? 0 : owner.cellMaterialIndices.Length)}");
                }

                Phase = TerrainGenerationPhase.ChunkObjects;
            }
        }

        private void StepLakePositionPrepass()
        {
            float voxelSize = owner.voxelSizeMeters;
            float seaLevel = owner.SeaLevelMeters;
            float minElevation = owner.lakeScanMinElevationAboveSeaLevel;
            float gridSpacing = Mathf.Max(voxelSize, owner.lakeScanGridSpacingMeters);
            float minSpacing = owner.lakeScanMinSpacingMeters;
            float radius = owner.lakeScanMaxRadiusMeters;
            float depth = owner.lakeScanDefaultDepthMeters;
            int gridStep = Mathf.Max(1, Mathf.RoundToInt(gridSpacing / voxelSize));

            var candidates = new List<GenerationLakeBasin>();
            for (int sz = gridStep; sz < owner.TotalSamplesZ - gridStep; sz += gridStep)
            {
                for (int sx = gridStep; sx < owner.TotalSamplesX - gridStep; sx += gridStep)
                {
                    float h = owner.surfaceHeightPrepass[owner.GetSurfacePrepassIndex(sx, sz)];
                    if (h < seaLevel + minElevation)
                    {
                        continue;
                    }

                    bool isLocalMin = true;
                    for (int dz = -1; dz <= 1 && isLocalMin; dz++)
                    {
                        for (int dx = -1; dx <= 1 && isLocalMin; dx++)
                        {
                            if (dx == 0 && dz == 0)
                            {
                                continue;
                            }

                            int nx = Mathf.Clamp(sx + dx * gridStep, 0, owner.TotalSamplesX - 1);
                            int nz = Mathf.Clamp(sz + dz * gridStep, 0, owner.TotalSamplesZ - 1);
                            if (owner.surfaceHeightPrepass[owner.GetSurfacePrepassIndex(nx, nz)] <= h)
                            {
                                isLocalMin = false;
                            }
                        }
                    }

                    if (!isLocalMin)
                    {
                        continue;
                    }

                    float worldX = sx * voxelSize;
                    float worldZ = sz * voxelSize;

                    bool tooClose = false;
                    foreach (GenerationLakeBasin existing in candidates)
                    {
                        float dxW = worldX - existing.worldX;
                        float dzW = worldZ - existing.worldZ;
                        if (dxW * dxW + dzW * dzW < minSpacing * minSpacing)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (tooClose)
                    {
                        continue;
                    }

                    candidates.Add(new GenerationLakeBasin
                    {
                        worldX = worldX,
                        worldZ = worldZ,
                        surfaceY = h,
                        radiusMeters = radius,
                        depthMeters = depth
                    });
                }
            }

            owner.generationLakeBasins = candidates.ToArray();
            lakePositionPrepassDone = true;
            Phase = TerrainGenerationPhase.BasinCarvingPrepass;
        }

        private void StepBasinCarvingPrepass()
        {
            if (owner.generationLakeBasins == null || owner.generationLakeBasins.Length == 0)
            {
                basinCarvingPrepassDone = true;
                Phase = TerrainGenerationPhase.ColumnPrepass;
                return;
            }

            float voxelSize = owner.voxelSizeMeters;
            foreach (GenerationLakeBasin basin in owner.generationLakeBasins)
            {
                float radius = basin.radiusMeters;
                float depth = basin.depthMeters;
                // Expand the bounding box by 10 % to accommodate the +9 % rim noise below.
                float outerRadius = radius * 1.1f;
                float outerRadiusSq = outerRadius * outerRadius;
                int minSX = Mathf.Max(0, Mathf.FloorToInt((basin.worldX - outerRadius) / voxelSize));
                int maxSX = Mathf.Min(owner.TotalSamplesX - 1, Mathf.CeilToInt((basin.worldX + outerRadius) / voxelSize));
                int minSZ = Mathf.Max(0, Mathf.FloorToInt((basin.worldZ - outerRadius) / voxelSize));
                int maxSZ = Mathf.Min(owner.TotalSamplesZ - 1, Mathf.CeilToInt((basin.worldZ + outerRadius) / voxelSize));

                for (int sz = minSZ; sz <= maxSZ; sz++)
                {
                    for (int sx = minSX; sx <= maxSX; sx++)
                    {
                        float wx = sx * voxelSize;
                        float wz = sz * voxelSize;
                        float dxW = wx - basin.worldX;
                        float dzW = wz - basin.worldZ;
                        float distSq = dxW * dxW + dzW * dzW;
                        if (distSq > outerRadiusSq)
                        {
                            continue;
                        }

                        // Fractal rim noise: perturbs the effective radius ±9 % per column so
                        // the shoreline feels organic rather than a perfect mathematical circle.
                        float rimNoise = VoxelTerrainGenerator.EvaluateMaterialNoise2D(
                            wx, wz, context, 2891.7f, 1447.3f, basin.radiusMeters * 0.4f);
                        float effectiveRadius = basin.radiusMeters * (1f + (rimNoise - 0.5f) * 0.18f);

                        float dist = Mathf.Sqrt(distSq);
                        if (dist >= effectiveRadius)
                        {
                            continue;
                        }

                        // C2-smooth quartic bowl: full depth at centre, zero at rim.
                        float t = dist / effectiveRadius;
                        float rim = 1f - t;
                        float bowlDepth = depth * rim * rim * rim * rim;

                        int prepassIdx = owner.GetSurfacePrepassIndex(sx, sz);
                        float oldSurface = owner.surfaceHeightPrepass[prepassIdx];
                        float newSurface = oldSurface - bowlDepth;
                        owner.surfaceHeightPrepass[prepassIdx] = newSurface;

                        for (int sy = 0; sy < owner.TotalSamplesY; sy++)
                        {
                            float localY = sy * voxelSize;
                            if (localY <= newSurface || localY > oldSurface)
                            {
                                continue;
                            }

                            owner.densitySamples[owner.GetSampleIndex(sx, sy, sz)] = newSurface - localY;
                        }
                    }
                }
            }

            if (useParallelGenerationJobs)
            {
                nativeSurfaceHeightPrepass.CopyFrom(owner.surfaceHeightPrepass);
                nativeDensitySamples.CopyFrom(owner.densitySamples);
            }

            basinCarvingPrepassDone = true;
            Phase = TerrainGenerationPhase.ColumnPrepass;
        }

        private void StepChunkObjects()
        {
            surfaceChunkSet = ComputeSurfaceChunkSet();
            owner.EnsureChunkObjects(surfaceChunkSet);
            chunkCoordinates.Clear();
            chunkCoordinates.AddRange(owner.BuildChunkCoordinatesInGenerationOrder(surfaceChunkSet));
            runtimeStreamingStartupChunkTarget = owner.CountRuntimeStreamingStartupChunks(chunkCoordinates);
            if (runtimeStreamingStartupChunkTarget == 0)
            {
                runtimeStreamingStartupChunksNotified = owner.MarkRuntimeStreamingStartupChunksReady();
            }
            chunkObjectsCreated = true;

            if (collectTimings && phaseStopwatch != null)
            {
                ChunkObjectMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                phaseStopwatch.Restart();
            }

            Phase = chunkCoordinates.Count > 0
                ? TerrainGenerationPhase.ChunkMeshBuildData
                : TerrainGenerationPhase.Complete;

            if (chunkCoordinates.Count == 0)
            {
                Complete();
            }
        }

        private Task<ChunkMeshBuildResult[]> pendingParallelMeshTask;
        private readonly Queue<(Mesh mesh, MeshCollider collider, Task bakeTask)> pendingColliderBakes
            = new Queue<(Mesh, MeshCollider, Task)>();

        private void StepChunkMeshBuildData()
        {
            if (builtChunkCount >= chunkCoordinates.Count)
            {
                Phase = pendingChunkCommits.Count > 0
                    ? TerrainGenerationPhase.ChunkMeshCommit
                    : TerrainGenerationPhase.Complete;

                if (Phase == TerrainGenerationPhase.Complete)
                {
                    Complete();
                }
                return;
            }

            // 1. PRECOMPUTE TRANSITION MASKS for this frame.
            // We avoid baking LOD decisions into chunk state during generation.
            // Runtime LOD/hysteresis remains the source of truth for visual updates.

            // 2. PARALLEL PATH
            if (useParallelGenerationJobs && chunkCoordinates.Count - builtChunkCount > 1)
            {
                if (pendingParallelMeshTask == null)
                {
                    int startIndex = builtChunkCount;
                    int count = chunkCoordinates.Count - startIndex;
                    ChunkMeshBuildResult[] results = new ChunkMeshBuildResult[count];
                    List<Vector3Int> coords = chunkCoordinates;
                    ProceduralVoxelTerrain ownerRef = owner;

                    // Precompute transition masks for the batch.
                    int[] precomputedMasks = new int[count];
                    for (int mi = 0; mi < count; mi++)
                    {
                        Vector3Int cc = coords[startIndex + mi];
                        precomputedMasks[mi] = owner.ComputeChunkTransitionMask(cc);
                    }

                    pendingParallelMeshTask = Task.Run(() =>
                    {
                        Parallel.For(0, count, i =>
                        {
                            Vector3Int coord = coords[startIndex + i];
                            results[i] = new ChunkMeshBuildResult(coord, ownerRef.BuildChunkMesh(coord, precomputedMasks[i]));
                        });
                        return results;
                    });
                    return;
                }

                if (!pendingParallelMeshTask.IsCompleted) return;

                ChunkMeshBuildResult[] completedResults = pendingParallelMeshTask.Result;
                pendingParallelMeshTask = null;
                for (int i = 0; i < completedResults.Length; i++)
                    pendingChunkCommits.Enqueue(completedResults[i]);

                builtChunkCount = chunkCoordinates.Count;
                Phase = TerrainGenerationPhase.ChunkMeshCommit;
                return;
            }

            // 3. SINGLE-THREADED PATH
            Vector3Int chunkCoordinate = chunkCoordinates[builtChunkCount];

            // Calculate mask for this single chunk using runtime transition rules.
            int singleMask = owner.ComputeChunkTransitionMask(chunkCoordinate);

            pendingChunkCommits.Enqueue(new ChunkMeshBuildResult(chunkCoordinate, owner.BuildChunkMesh(chunkCoordinate, singleMask)));
            builtChunkCount++;

            if (pendingChunkCommits.Count >= owner.asyncChunkBuildQueueSize || builtChunkCount >= chunkCoordinates.Count)
            {
                Phase = TerrainGenerationPhase.ChunkMeshCommit;
            }
        }

        private Vector3Int GetDir(int index)
        {
            switch (index)
            {
                case 0: return new Vector3Int(-1, 0, 0); // -X
                case 1: return new Vector3Int(1, 0, 0);  // +X
                case 2: return new Vector3Int(0, -1, 0); // -Y
                case 3: return new Vector3Int(0, 1, 0);  // +Y
                case 4: return new Vector3Int(0, 0, -1); // -Z
                case 5: return new Vector3Int(0, 0, 1);  // +Z
                default: return Vector3Int.zero;
            }
        }

        private void DrainPendingColliderBakes()
        {
            while (pendingColliderBakes.Count > 0 && pendingColliderBakes.Peek().bakeTask.IsCompleted)
            {
                (Mesh mesh, MeshCollider collider, Task bakeTask) = pendingColliderBakes.Dequeue();
                bakeTask.GetAwaiter().GetResult(); // rethrow on fault
                if (collider != null)
                    collider.sharedMesh = mesh;
            }
        }

        private void StepChunkMeshCommit()
        {
            // Drain any collider bake tasks that have already completed this frame.
            DrainPendingColliderBakes();

            if (pendingChunkCommits.Count == 0)
            {
                if (builtChunkCount >= chunkCoordinates.Count)
                {
                    // Don't complete until all background physics bakes have been applied.
                    if (pendingColliderBakes.Count == 0)
                    {
                        Complete();
                    }

                    return;
                }

                Phase = TerrainGenerationPhase.ChunkMeshBuildData;
                return;
            }

            ChunkMeshBuildResult chunkBuildResult = pendingChunkCommits.Dequeue();
            owner.CommitChunkMesh(chunkBuildResult.chunkCoordinate, chunkBuildResult.buildData, pendingColliderBakes);
            committedChunkCount++;
            if (!runtimeStreamingStartupChunksNotified &&
                runtimeStreamingStartupChunkTarget > 0 &&
                committedChunkCount >= runtimeStreamingStartupChunkTarget)
            {
                runtimeStreamingStartupChunksNotified = owner.MarkRuntimeStreamingStartupChunksReady();
            }

            if (committedChunkCount >= chunkCoordinates.Count &&
                builtChunkCount >= chunkCoordinates.Count &&
                pendingChunkCommits.Count == 0 &&
                pendingColliderBakes.Count == 0)
            {
                Complete();
                return;
            }

            if (builtChunkCount < chunkCoordinates.Count && pendingChunkCommits.Count < owner.asyncChunkBuildQueueSize)
            {
                Phase = TerrainGenerationPhase.ChunkMeshBuildData;
            }
        }

        private void Complete()
        {
            if (IsDone)
            {
                return;
            }

            if (collectTimings)
            {
                MeshMilliseconds = phaseStopwatch?.ElapsedMilliseconds ?? 0L;
                totalStopwatch?.Stop();
            }

            if (!runtimeStreamingStartupChunksNotified && runtimeStreamingStartupChunkTarget > 0)
            {
                runtimeStreamingStartupChunksNotified = owner.MarkRuntimeStreamingStartupChunksReady();
            }

            Phase = TerrainGenerationPhase.Complete;
            Progress01 = 1f;
            Status = "Voxel terrain generation complete";
            Success = true;
            IsDone = true;
        }

        // Builds a set of chunk coordinates that straddle the isosurface and therefore need
        // GameObjects and mesh generation.  Chunks fully above the terrain surface (all air)
        // or deep below it (all solid) are omitted, which can eliminate 60-80 % of chunk work
        // on tall terrain sizes like 50×12×50.
        private HashSet<Vector3Int> ComputeSurfaceChunkSet()
        {
            var set = new HashSet<Vector3Int>();
            int chunkSize = owner.cellsPerChunkAxis;
            float voxel = owner.voxelSizeMeters;
            float chunkWorldH = chunkSize * voxel;
            int sampleStride = owner.TotalSamplesX;
            // Allow a modest buffer below the lowest surface sample in each column so that
            // cave-carved geometry (which sits a few voxels below the surface) is not excluded.
            float caveBuffer = voxel * 6f;

            for (int cz = 0; cz < owner.chunkCounts.z; cz++)
            {
                for (int cy = 0; cy < owner.chunkCounts.y; cy++)
                {
                    float chunkMinY = cy * chunkWorldH;
                    float chunkMaxY = (cy + 1) * chunkWorldH;

                    for (int cx = 0; cx < owner.chunkCounts.x; cx++)
                    {
                        int sxStart = cx * chunkSize;
                        int sxEnd = Mathf.Min(sxStart + chunkSize + 1, owner.TotalSamplesX);
                        int szStart = cz * chunkSize;
                        int szEnd = Mathf.Min(szStart + chunkSize + 1, owner.TotalSamplesZ);

                        float minSurface = float.MaxValue;
                        float maxSurface = float.MinValue;
                        for (int sz = szStart; sz < szEnd; sz++)
                        {
                            int rowBase = sz * sampleStride;
                            for (int sx = sxStart; sx < sxEnd; sx++)
                            {
                                float h = owner.surfaceHeightPrepass[rowBase + sx];
                                if (h < minSurface) minSurface = h;
                                if (h > maxSurface) maxSurface = h;
                            }
                        }

                        // Include chunk if any surface height falls within its Y range (plus cave buffer below).
                        if (maxSurface > chunkMinY && minSurface < chunkMaxY + caveBuffer)
                        {
                            set.Add(new Vector3Int(cx, cy, cz));
                        }
                    }
                }
            }

            return set;
        }

        private void UpdateProgress()
        {
            if (IsDone)
            {
                return;
            }

            float completedUnits = surfacePrepassZ
                + densityZ
                + (lakePositionPrepassDone ? 1f : 0f)
                + (basinCarvingPrepassDone ? 1f : 0f)
                + columnProfileZ
                + materialZ
                + (chunkObjectsCreated ? 1f : 0f)
                + builtChunkCount
                + committedChunkCount;

            Progress01 = totalWorkUnits <= 0.0001f
                ? 1f
                : Mathf.Clamp01(completedUnits / totalWorkUnits);
            Status = BuildStatus();
        }

        private string BuildStatus()
        {
            switch (Phase)
            {
                case TerrainGenerationPhase.SurfacePrepass:
                    return $"Terrain data prep: surface prepass {Mathf.Min(surfacePrepassZ, owner.TotalSamplesZ)}/{owner.TotalSamplesZ}";
                case TerrainGenerationPhase.DensityField:
                    return $"Terrain data prep: density field {Mathf.Min(densityZ, owner.TotalSamplesZ)}/{owner.TotalSamplesZ}";
                case TerrainGenerationPhase.LakePositionPrepass:
                    return "Terrain data prep: scanning lake candidates";
                case TerrainGenerationPhase.BasinCarvingPrepass:
                    return "Terrain data prep: carving lake basins";
                case TerrainGenerationPhase.ColumnPrepass:
                    return $"Terrain data prep: column profiles {Mathf.Min(columnProfileZ, owner.TotalCellsZ)}/{owner.TotalCellsZ}";
                case TerrainGenerationPhase.CellMaterials:
                    return $"Terrain data prep: cell materials {Mathf.Min(materialZ, owner.TotalCellsZ)}/{owner.TotalCellsZ}";
                case TerrainGenerationPhase.ChunkObjects:
                    return "Terrain data prep: chunk objects";
                case TerrainGenerationPhase.ChunkMeshBuildData:
                    return $"Chunk mesh build data {Mathf.Min(builtChunkCount, chunkCoordinates.Count)}/{chunkCoordinates.Count}";
                case TerrainGenerationPhase.ChunkMeshCommit:
                    return $"Chunk mesh commit {Mathf.Min(committedChunkCount, chunkCoordinates.Count)}/{chunkCoordinates.Count}";
                case TerrainGenerationPhase.Complete:
                    return "Voxel terrain generation complete";
                default:
                    return "Preparing voxel terrain generation";
            }
        }
    }
}
