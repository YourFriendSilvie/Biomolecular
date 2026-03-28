using System.Collections.Generic;
using UnityEngine;

internal static class OlympicRainforestPreset
{
    internal static List<TerrainScatterPrototype> Build()
    {
        return new List<TerrainScatterPrototype>
        {
            new TerrainScatterPrototype
            {
                displayName = "Douglas-fir Placeholder",
                primitiveType = PrimitiveType.Cylinder,
                compositionItemName = "Foliage (Needles)",
                colorTint = new Color(0.18f, 0.33f, 0.17f),
                minScale = new Vector3(0.65f, 5.5f, 0.65f),
                maxScale = new Vector3(1.2f, 9.25f, 1.2f),
                spawnCount = 120,
                totalMassRangeGrams = new Vector2(420f, 900f),
                normalizedHeightRange = new Vector2(0.16f, 0.95f),
                slopeDegreesRange = new Vector2(2f, 36f),
                densityNoiseScale = 62f,
                densityThreshold = 0.44f,
                minimumSpacingMeters = 7f
            },
            new TerrainScatterPrototype
            {
                displayName = "Western Red Cedar Placeholder",
                primitiveType = PrimitiveType.Cylinder,
                compositionItemName = "Foliage (Needles)",
                colorTint = new Color(0.16f, 0.44f, 0.28f),
                minScale = new Vector3(0.9f, 4.2f, 0.9f),
                maxScale = new Vector3(1.6f, 7.2f, 1.6f),
                spawnCount = 78,
                totalMassRangeGrams = new Vector2(350f, 780f),
                normalizedHeightRange = new Vector2(0.04f, 0.52f),
                slopeDegreesRange = new Vector2(0f, 24f),
                densityNoiseScale = 46f,
                densityThreshold = 0.53f,
                minimumSpacingMeters = 7.5f
            },
            new TerrainScatterPrototype
            {
                displayName = "Red Alder Placeholder",
                primitiveType = PrimitiveType.Cube,
                compositionItemName = "Foliage (Broadleaves)",
                colorTint = new Color(0.43f, 0.62f, 0.29f),
                minScale = new Vector3(1.6f, 4f, 1.6f),
                maxScale = new Vector3(3.2f, 7f, 3.2f),
                spawnCount = 58,
                totalMassRangeGrams = new Vector2(320f, 760f),
                normalizedHeightRange = new Vector2(0.03f, 0.34f),
                slopeDegreesRange = new Vector2(0f, 16f),
                densityNoiseScale = 36f,
                densityThreshold = 0.6f,
                minimumSpacingMeters = 9f
            },
            new TerrainScatterPrototype
            {
                displayName = "Bigleaf Maple Placeholder",
                primitiveType = PrimitiveType.Cube,
                compositionItemName = "Foliage (Broadleaves)",
                colorTint = new Color(0.31f, 0.55f, 0.2f),
                minScale = new Vector3(1.8f, 4.5f, 1.8f),
                maxScale = new Vector3(3.6f, 7.8f, 3.6f),
                spawnCount = 28,
                totalMassRangeGrams = new Vector2(380f, 900f),
                normalizedHeightRange = new Vector2(0.04f, 0.42f),
                slopeDegreesRange = new Vector2(0f, 22f),
                densityNoiseScale = 52f,
                densityThreshold = 0.66f,
                minimumSpacingMeters = 10f
            },
            new TerrainScatterPrototype
            {
                displayName = "Serviceberry Placeholder",
                primitiveType = PrimitiveType.Cube,
                compositionItemName = "Serviceberry Foliage",
                colorTint = new Color(0.53f, 0.73f, 0.35f),
                minScale = new Vector3(0.8f, 1f, 0.8f),
                maxScale = new Vector3(1.4f, 2f, 1.4f),
                spawnCount = 88,
                totalMassRangeGrams = new Vector2(120f, 280f),
                normalizedHeightRange = new Vector2(0.1f, 0.72f),
                slopeDegreesRange = new Vector2(0f, 28f),
                densityNoiseScale = 28f,
                densityThreshold = 0.55f,
                minimumSpacingMeters = 3.5f
            }
        };
    }
}
