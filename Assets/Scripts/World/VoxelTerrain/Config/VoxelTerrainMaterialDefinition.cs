using System;
using UnityEngine;

[Serializable]
public class VoxelTerrainMaterialDefinition
{
    public string displayName = "Silicate Stone";
    public string compositionItemName = "Stone (Silicate-Rich)";
    public CompositionInfo compositionOverride;
    public Material material;
    public Color colorTint = new Color(0.55f, 0.57f, 0.6f, 1f);
    public bool isFallbackMaterial = false;
    public Vector2 depthRangeMeters = new Vector2(0f, 64f);
    public Vector2 normalizedHeightRange = new Vector2(0f, 1f);
    [Min(0.5f)] public float distributionNoiseScaleMeters = 18f;
    [Range(0f, 1f)] public float distributionNoiseThreshold = 0.5f;

    public void Sanitize()
    {
        displayName = string.IsNullOrWhiteSpace(displayName) ? "Terrain Material" : displayName.Trim();
        depthRangeMeters = new Vector2(
            Mathf.Max(0f, Mathf.Min(depthRangeMeters.x, depthRangeMeters.y)),
            Mathf.Max(0f, Mathf.Max(depthRangeMeters.x, depthRangeMeters.y)));
        normalizedHeightRange = new Vector2(
            Mathf.Clamp01(Mathf.Min(normalizedHeightRange.x, normalizedHeightRange.y)),
            Mathf.Clamp01(Mathf.Max(normalizedHeightRange.x, normalizedHeightRange.y)));
        distributionNoiseScaleMeters = Mathf.Max(0.5f, distributionNoiseScaleMeters);
        distributionNoiseThreshold = Mathf.Clamp01(distributionNoiseThreshold);
    }

    public string ResolveDisplayName()
    {
        return string.IsNullOrWhiteSpace(displayName) ? "Terrain Material" : displayName.Trim();
    }

    public CompositionInfo ResolveComposition()
    {
        if (compositionOverride != null)
        {
            return compositionOverride;
        }

        if (!string.IsNullOrWhiteSpace(compositionItemName) &&
            CompositionInfoRegistry.TryGetByItemName(compositionItemName, out CompositionInfo composition))
        {
            return composition;
        }

        return null;
    }
}
