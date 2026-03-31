using System;
using UnityEngine;

[Serializable]
public class TerrainScatterPrototype
{
    public string displayName = "Placeholder Resource";
    public PrimitiveType primitiveType = PrimitiveType.Cube;
    public string compositionItemName = "Foliage (Broadleaves)";
    public CompositionInfo compositionOverride;
    public Material material;
    public Color colorTint = Color.white;
    public Vector3 minScale = new Vector3(0.6f, 0.8f, 0.6f);
    public Vector3 maxScale = new Vector3(1.2f, 1.8f, 1.2f);
    [Min(1)] public int spawnCount = 120;
    [Min(0f), Tooltip("When > 0, overrides spawnCount with a density-scaled value: instances per 16×16 chunk region. Set to 0 to use spawnCount directly.")]
    public float spawnDensityPer16x16Chunks = 0f;
    [Min(1)] public int maxPlacementAttemptsPerInstance = 12;
    public Vector2 totalMassRangeGrams = new Vector2(80f, 180f);
    [Range(0f, 1f)] public float harvestEfficiency = 0.85f;
    public bool destroyOnHarvest = true;
    [Min(1)] public int harvestsRequired = 1;
    public bool randomizeCompositionOnStart = true;
    public Vector2 normalizedHeightRange = new Vector2(0.05f, 0.95f);
    public Vector2 slopeDegreesRange = new Vector2(0f, 32f);
    [Min(0.5f)] public float densityNoiseScale = 28f;
    [Range(0f, 1f)] public float densityThreshold = 0.45f;
    [Min(0f)] public float minimumSpacingMeters = 1f;
    public bool alignToSurfaceNormal = false;
    public bool randomizeYaw = true;

    public void Sanitize()
    {
        displayName = string.IsNullOrWhiteSpace(displayName) ? primitiveType.ToString() : displayName.Trim();
        maxScale = new Vector3(
            Mathf.Max(0.05f, maxScale.x),
            Mathf.Max(0.05f, maxScale.y),
            Mathf.Max(0.05f, maxScale.z));
        minScale = new Vector3(
            Mathf.Clamp(minScale.x, 0.05f, maxScale.x),
            Mathf.Clamp(minScale.y, 0.05f, maxScale.y),
            Mathf.Clamp(minScale.z, 0.05f, maxScale.z));
        spawnCount = Mathf.Max(1, spawnCount);
        spawnDensityPer16x16Chunks = Mathf.Max(0f, spawnDensityPer16x16Chunks);
        maxPlacementAttemptsPerInstance = Mathf.Max(1, maxPlacementAttemptsPerInstance);
        totalMassRangeGrams = new Vector2(
            Mathf.Max(0.1f, Mathf.Min(totalMassRangeGrams.x, totalMassRangeGrams.y)),
            Mathf.Max(0.1f, Mathf.Max(totalMassRangeGrams.x, totalMassRangeGrams.y)));
        harvestEfficiency = Mathf.Clamp01(harvestEfficiency);
        harvestsRequired = Mathf.Max(1, harvestsRequired);
        densityNoiseScale = Mathf.Max(0.5f, densityNoiseScale);
        densityThreshold = Mathf.Clamp01(densityThreshold);
        minimumSpacingMeters = Mathf.Max(0f, minimumSpacingMeters);
        normalizedHeightRange = new Vector2(
            Mathf.Clamp01(Mathf.Min(normalizedHeightRange.x, normalizedHeightRange.y)),
            Mathf.Clamp01(Mathf.Max(normalizedHeightRange.x, normalizedHeightRange.y)));
        slopeDegreesRange = new Vector2(
            Mathf.Clamp(Mathf.Min(slopeDegreesRange.x, slopeDegreesRange.y), 0f, 90f),
            Mathf.Clamp(Mathf.Max(slopeDegreesRange.x, slopeDegreesRange.y), 0f, 90f));
    }

    public string ResolveDisplayName()
    {
        return string.IsNullOrWhiteSpace(displayName) ? primitiveType.ToString() : displayName.Trim();
    }

    /// <summary>
    /// Returns the effective spawn count for this prototype given the terrain.
    /// When <see cref="spawnDensityPer16x16Chunks"/> is greater than 0, the count is
    /// computed proportionally to the terrain area relative to a 16×16 chunk reference region.
    /// Otherwise, <see cref="spawnCount"/> is used directly.
    /// </summary>
    public int ComputeEffectiveSpawnCount(ProceduralVoxelTerrain terrain, Bounds bounds)
    {
        if (spawnDensityPer16x16Chunks > 0f && terrain != null)
        {
            float referenceRegionSide = 16f * terrain.ChunkWorldSizeMeters;
            float referenceRegionArea = referenceRegionSide * referenceRegionSide;
            float terrainArea = bounds.size.x * bounds.size.z;
            return Mathf.Max(1, Mathf.RoundToInt(spawnDensityPer16x16Chunks * terrainArea / referenceRegionArea));
        }
        return Mathf.Max(1, spawnCount);
    }

    public CompositionInfo ResolveComposition()
    {
        if (compositionOverride != null)
        {
            return compositionOverride;
        }

        if (!string.IsNullOrWhiteSpace(compositionItemName) &&
            CompositionInfoRegistry.TryGetByItemName(compositionItemName, out CompositionInfo resolvedComposition))
        {
            return resolvedComposition;
        }

        return null;
    }
}
