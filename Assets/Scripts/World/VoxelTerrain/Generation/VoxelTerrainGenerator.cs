using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// -------------------------------------------------------------------------
// Supporting structs – moved here from ProceduralVoxelTerrain so that
// generation-related code that needs them can live in its own files.
// These remain internal to the assembly; they are not public API.
// -------------------------------------------------------------------------

internal struct GenerationContext
{
    public float surfaceOffsetX;
    public float surfaceOffsetZ;
    public float ridgeOffsetX;
    public float ridgeOffsetZ;
    public float detailOffsetX;
    public float detailOffsetZ;
    public float caveOffsetX;
    public float caveOffsetY;
    public float caveOffsetZ;
    public float materialOffsetX;
    public float materialOffsetY;
    public float materialOffsetZ;
    public float oceanFloorOffsetX;
    public float oceanFloorOffsetZ;
    public float islandShapeOffsetX;
    public float islandShapeOffsetZ;
    public float domainWarpOffsetX;
    public float domainWarpOffsetZ;
    // Olympic Mountain ridge offsets (ridged multifractal peaks).
    public float mountainRidgeOffsetX;
    public float mountainRidgeOffsetZ;
}

internal struct GenerationMaterialIndices
{
    public byte fallbackIndex;
    public int organicLayerIndex;
    public int topsoilIndex;
    public int eluviationLayerIndex;
    public int subsoilIndex;
    public int parentMaterialIndex;
    public int weatheredStoneIndex;
    public int basinSandIndex;
    public int basinGravelIndex;
    public int lakeMudIndex;
    public int clayDepositIndex;
    public int ironVeinIndex;
    public int copperVeinIndex;
    public int bedrockIndex;
}

internal struct GenerationLakeBasin
{
    public float worldX;
    public float worldZ;
    public float surfaceY;
    public float radiusMeters;
    public float depthMeters;
}

/// <summary>
/// Holds all per-column pre-baked terrain data used during mesh generation.
/// One profile is computed per XZ column at generation time and stored in the
/// <c>columnProfilePrepass</c> array. <see cref="ChunkMeshBuilder"/> reads these
/// profiles to assign vertex colors without re-running noise functions.
/// </summary>
internal struct ColumnMaterialProfile
{
    /// <summary>World-space Y of the terrain surface for this column.</summary>
    public float surfaceHeight;
    /// <summary>Depth below the surface at which beach sand gives way to gravel.</summary>
    public float beachSandBoundary;
    /// <summary>Depth below the surface at which beach gravel gives way to soil layers.</summary>
    public float beachGravelBoundary;
    /// <summary>Thickness of the organic (humus) layer.</summary>
    public float organicThickness;
    /// <summary>Cumulative depth at the base of the topsoil layer.</summary>
    public float topsoilBoundary;
    /// <summary>Cumulative depth at the base of the eluviation (leached) horizon.</summary>
    public float eluviationBoundary;
    /// <summary>Cumulative depth at the base of the subsoil horizon.</summary>
    public float subsoilBoundary;
    /// <summary>Cumulative depth at the base of the parent material layer.</summary>
    public float parentBoundary;
    /// <summary>Cumulative depth at the base of the weathered stone layer, above bedrock.</summary>
    public float weatheredBoundary;
    /// <summary>How many metres of water are above the ocean floor at this column (0 on dry land).</summary>
    public float oceanWaterDepth;
    /// <summary><c>true</c> when this column is a coastal beach above sea level.</summary>
    public bool isBeachLand;
    /// <summary><c>true</c> when this column is submerged ocean floor.</summary>
    public bool isOceanFloor;
    // Continuous factors [0,1] for smooth color blending — not used by material index pipeline.
    public float beachFactor;
    public float oceanFactor;
    // [0,1] steepness: 0=flat soil, 1=near-vertical bare rock cliff face
    public float slopeFactor;
    // Local Y of the nearest lake surface above this column; 0 = no lake.
    // Written by RegisterLakeBasin → used by ChunkMeshBuilder.SampleProfile for basin colors.
    public float lakeSurfaceY;
    /// <summary>Per-column boundary noise offset (metres). Added to every soil horizon boundary
    /// to match the jagged logical layer transitions from DetermineCellMaterialIndex.</summary>
    public float materialBoundaryNoise;
}

// -------------------------------------------------------------------------
// VoxelTerrainGenerator – pure static helpers for terrain generation math.
// Noise evaluation, surface/density/material routines, and the generation
// context factory are gathered here so ProceduralVoxelTerrain can delegate
// to them instead of containing all the math itself.
// -------------------------------------------------------------------------

/// <summary>
/// Pure static utility class containing all terrain-generation math: noise evaluation,
/// surface height computation, density field sampling, material classification helpers,
/// and the <see cref="GenerationContext"/> factory. Stateless — all inputs are passed as
/// parameters so methods are safe to call from Burst jobs via <c>TerrainGenerationJobsUtility</c>.
/// </summary>
internal static class VoxelTerrainGenerator
{
    // -------------------------------------------------------------------------
    // GenerationContext factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="GenerationContext"/> by seeding a <see cref="System.Random"/> and
    /// generating deterministic XZ/XYZ offsets for every noise layer. Using large random offsets
    /// prevents noise octaves from aliasing with each other at the origin.
    /// </summary>
    /// <param name="seed">Integer seed that uniquely determines the terrain layout.</param>
    /// <returns>A fully populated <see cref="GenerationContext"/> for the given seed.</returns>
    public static GenerationContext BuildGenerationContext(int seed)
    {
        System.Random random = new System.Random(seed);
        return new GenerationContext
        {
            surfaceOffsetX = NextFloat(random, -10000f, 10000f),
            surfaceOffsetZ = NextFloat(random, -10000f, 10000f),
            ridgeOffsetX = NextFloat(random, -10000f, 10000f),
            ridgeOffsetZ = NextFloat(random, -10000f, 10000f),
            detailOffsetX = NextFloat(random, -10000f, 10000f),
            detailOffsetZ = NextFloat(random, -10000f, 10000f),
            caveOffsetX = NextFloat(random, -10000f, 10000f),
            caveOffsetY = NextFloat(random, -10000f, 10000f),
            caveOffsetZ = NextFloat(random, -10000f, 10000f),
            materialOffsetX = NextFloat(random, -10000f, 10000f),
            materialOffsetY = NextFloat(random, -10000f, 10000f),
            materialOffsetZ = NextFloat(random, -10000f, 10000f),
            oceanFloorOffsetX = NextFloat(random, -10000f, 10000f),
            oceanFloorOffsetZ = NextFloat(random, -10000f, 10000f),
            islandShapeOffsetX = NextFloat(random, -10000f, 10000f),
            islandShapeOffsetZ = NextFloat(random, -10000f, 10000f),
            domainWarpOffsetX = NextFloat(random, -10000f, 10000f),
            domainWarpOffsetZ = NextFloat(random, -10000f, 10000f),
            mountainRidgeOffsetX = NextFloat(random, -10000f, 10000f),
            mountainRidgeOffsetZ = NextFloat(random, -10000f, 10000f)
        };
    }

    // -------------------------------------------------------------------------
    // Fundamental noise / math primitives
    // -------------------------------------------------------------------------

    public static float SmoothStep01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - (2f * value));
    }

    /// <summary>
    /// Evaluates fBm (fractal Brownian motion) Perlin noise summed over multiple octaves.
    /// Each octave doubles frequency and halves amplitude (governed by <paramref name="persistence"/>
    /// and <paramref name="lacunarity"/>). Returns a normalised value in approximately [0, 1].
    /// </summary>
    /// <param name="x">Scaled X input coordinate.</param>
    /// <param name="z">Scaled Z input coordinate.</param>
    /// <param name="octaves">Number of noise octaves to sum.</param>
    /// <param name="persistence">Amplitude multiplier per octave (typically 0.5).</param>
    /// <param name="lacunarity">Frequency multiplier per octave (typically 2.0).</param>
    public static float EvaluateFractalNoise(float x, float z, int octaves, float persistence, float lacunarity)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float total = 0f;
        float normalization = 0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            total += ((noise.cnoise(new float2(x * frequency, z * frequency)) + 1f) * 0.5f) * amplitude;
            normalization += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return normalization <= 0.0001f ? 0f : total / normalization;
    }

    // Ridged multifractal noise — produces sharp knife-edge mountain ridges.
    // Each octave is inverted-absolute Perlin so ridges compound into mountain chains.
    // Returns [0, 1] with peaks at 1 and valleys at 0.
    public static float EvaluateRidgedMultifractal(float x, float z, int octaves, float persistence, float lacunarity)
    {
        float freq = 1f, amp = 1f, total = 0f, norm = 0f, prev = 1f;
        for (int i = 0; i < octaves; i++)
        {
            float n = (noise.cnoise(new float2(x * freq, z * freq)) + 1f) * 0.5f;
            float ridge = 1f - Mathf.Abs(n * 2f - 1f); // invert-abs: peaks at 1
            ridge *= ridge;  // sharpen the ridge crest
            ridge *= prev;   // weight by previous ridge so chains reinforce
            total += ridge * amp;
            norm  += amp;
            prev   = ridge;
            amp   *= persistence;
            freq  *= lacunarity;
        }
        return norm > 0.0001f ? total / norm : 0f;
    }

    public static float EvaluatePerlin3D(float x, float y, float z)
    {
        float xy = (noise.cnoise(new float2(x, y)) + 1f) * 0.5f;
        float yz = (noise.cnoise(new float2(y, z)) + 1f) * 0.5f;
        float xz = (noise.cnoise(new float2(x, z)) + 1f) * 0.5f;
        float yx = (noise.cnoise(new float2(y, x)) + 1f) * 0.5f;
        float zy = (noise.cnoise(new float2(z, y)) + 1f) * 0.5f;
        float zx = (noise.cnoise(new float2(z, x)) + 1f) * 0.5f;
        return (xy + yz + xz + yx + zy + zx) / 6f;
    }

    public static float EvaluateMaterialNoise2D(
        float localX, float localZ,
        GenerationContext context,
        float offsetX, float offsetZ, float scaleMeters)
    {
        return (noise.cnoise(new float2(
            (localX + context.materialOffsetX + offsetX) / Mathf.Max(0.0001f, scaleMeters),
            (localZ + context.materialOffsetZ + offsetZ) / Mathf.Max(0.0001f, scaleMeters))) + 1f) * 0.5f;
    }

    public static float EvaluateMaterialNoise3D(
        float localX, float localY, float localZ,
        GenerationContext context,
        float offset, float scaleMeters)
    {
        return EvaluatePerlin3D(
            (localX + context.materialOffsetX + offset) / Mathf.Max(0.0001f, scaleMeters),
            (localY + context.materialOffsetY + (offset * 0.37f)) / Mathf.Max(0.0001f, scaleMeters),
            (localZ + context.materialOffsetZ + (offset * 0.61f)) / Mathf.Max(0.0001f, scaleMeters));
    }

    public static float EvaluateIslandDistanceNormalized(float localX, float localZ, Vector3 worldSize)
    {
        float normalizedX = worldSize.x <= 0.0001f ? 0f : ((localX / worldSize.x) - 0.5f) * 2f;
        float normalizedZ = worldSize.z <= 0.0001f ? 0f : ((localZ / worldSize.z) - 0.5f) * 2f;
        return Mathf.Sqrt((normalizedX * normalizedX) + (normalizedZ * normalizedZ));
    }

    // Irregular island shape: displaces the radial distance with 2-octave Perlin noise for San-Juan-style coastlines.
    public static float EvaluateIslandDistanceShaped(
        float localX, float localZ, Vector3 worldSize,
        float noiseScale, float noiseStrength, float offsetX, float offsetZ)
    {
        float normalizedX = worldSize.x <= 0.0001f ? 0f : ((localX / worldSize.x) - 0.5f) * 2f;
        float normalizedZ = worldSize.z <= 0.0001f ? 0f : ((localZ / worldSize.z) - 0.5f) * 2f;
        float rawDistance = Mathf.Sqrt((normalizedX * normalizedX) + (normalizedZ * normalizedZ));
        if (noiseStrength <= 0f || noiseScale <= 0f)
        {
            return rawDistance;
        }

        float n1 = ((noise.cnoise(new float2((localX + offsetX) * noiseScale, (localZ + offsetZ) * noiseScale)) + 1f) * 0.5f) * 2f - 1f;
        float n2 = ((noise.cnoise(new float2((localX + offsetX + 500f) * (noiseScale * 2.3f), (localZ + offsetZ + 300f) * (noiseScale * 2.3f))) + 1f) * 0.5f) * 2f - 1f;
        float displacement = (n1 + n2 * 0.45f) / 1.45f * noiseStrength;
        return Mathf.Max(0f, rawDistance * (1f + displacement));
    }

    public static float NextFloat(System.Random random, float minInclusive, float maxInclusive)
    {
        if (Mathf.Approximately(minInclusive, maxInclusive))
        {
            return minInclusive;
        }

        float min = Mathf.Min(minInclusive, maxInclusive);
        float max = Mathf.Max(minInclusive, maxInclusive);
        return (float)(min + (random.NextDouble() * (max - min)));
    }

    // -------------------------------------------------------------------------
    // Mesh / isosurface helpers
    // -------------------------------------------------------------------------

    public static bool CubeIntersectsSurface(float[] cubeDensities, float isoLevel)
    {
        bool hasPositive = false;
        bool hasNegative = false;
        for (int i = 0; i < 8; i++)
        {
            if (cubeDensities[i] > isoLevel)
            {
                hasPositive = true;
            }
            else
            {
                hasNegative = true;
            }

            if (hasPositive && hasNegative)
            {
                return true;
            }
        }

        return false;
    }

    public static Vector3 InterpolateEdge(Vector3 pointA, Vector3 pointB, float densityA, float densityB, float isoLevel)
    {
        float denominator = densityB - densityA;
        if (Mathf.Abs(denominator) <= 0.0001f)
        {
            return Vector3.Lerp(pointA, pointB, 0.5f);
        }

        float t = Mathf.Clamp01((isoLevel - densityA) / denominator);
        return Vector3.Lerp(pointA, pointB, t);
    }

    // -------------------------------------------------------------------------
    // Material classification helpers
    // -------------------------------------------------------------------------

    public static bool IsReservedDefaultMaterialIndex(int index, in GenerationMaterialIndices indices)
    {
        return index == indices.organicLayerIndex ||
               index == indices.topsoilIndex ||
               index == indices.eluviationLayerIndex ||
               index == indices.subsoilIndex ||
               index == indices.parentMaterialIndex ||
               index == indices.weatheredStoneIndex ||
               index == indices.basinSandIndex ||
               index == indices.basinGravelIndex ||
               index == indices.lakeMudIndex ||
               index == indices.clayDepositIndex ||
               index == indices.ironVeinIndex ||
               index == indices.copperVeinIndex ||
               index == indices.bedrockIndex;
    }

    public static bool IsExcludedFromBaseTerrainNoise(VoxelTerrainMaterialDefinition definition)
    {
        return MatchesMaterialAlias(definition, "Basin Sand") ||
               MatchesMaterialAlias(definition, "Basin Gravel") ||
               MatchesMaterialAlias(definition, "Lake Mud") ||
               MatchesMaterialAlias(definition, "Clay Deposit", "Alluvial Clay", "Clay Subsoil");
    }

    public static bool MatchesMaterialAlias(VoxelTerrainMaterialDefinition definition, params string[] aliases)
    {
        if (definition == null || aliases == null)
        {
            return false;
        }

        string displayName = definition.ResolveDisplayName();
        string compositionName = definition.compositionItemName;
        for (int i = 0; i < aliases.Length; i++)
        {
            string alias = aliases[i];
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            if (string.Equals(displayName, alias, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(compositionName, alias, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
