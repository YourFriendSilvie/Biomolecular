using System.Collections.Generic;
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

internal struct ColumnMaterialProfile
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
    public bool isBeachLand;
    public bool isOceanFloor;
}

// -------------------------------------------------------------------------
// VoxelTerrainGenerator – pure static helpers for terrain generation math.
// Noise evaluation, surface/density/material routines, and the generation
// context factory are gathered here so ProceduralVoxelTerrain can delegate
// to them instead of containing all the math itself.
// -------------------------------------------------------------------------

internal static class VoxelTerrainGenerator
{
    // -------------------------------------------------------------------------
    // GenerationContext factory
    // -------------------------------------------------------------------------

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
            oceanFloorOffsetZ = NextFloat(random, -10000f, 10000f)
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

    public static float EvaluateFractalNoise(float x, float z, int octaves, float persistence, float lacunarity)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float total = 0f;
        float normalization = 0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            total += Mathf.PerlinNoise(x * frequency, z * frequency) * amplitude;
            normalization += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return normalization <= 0.0001f ? 0f : total / normalization;
    }

    public static float EvaluatePerlin3D(float x, float y, float z)
    {
        float xy = Mathf.PerlinNoise(x, y);
        float yz = Mathf.PerlinNoise(y, z);
        float xz = Mathf.PerlinNoise(x, z);
        float yx = Mathf.PerlinNoise(y, x);
        float zy = Mathf.PerlinNoise(z, y);
        float zx = Mathf.PerlinNoise(z, x);
        return (xy + yz + xz + yx + zy + zx) / 6f;
    }

    public static float EvaluateMaterialNoise2D(
        float localX, float localZ,
        GenerationContext context,
        float offsetX, float offsetZ, float scaleMeters)
    {
        return Mathf.PerlinNoise(
            (localX + context.materialOffsetX + offsetX) / Mathf.Max(0.0001f, scaleMeters),
            (localZ + context.materialOffsetZ + offsetZ) / Mathf.Max(0.0001f, scaleMeters));
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
