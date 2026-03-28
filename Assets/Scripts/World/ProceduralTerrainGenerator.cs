using System;
using UnityEngine;

public class ProceduralTerrainGenerator : MonoBehaviour
{
    private const string GeneratedTerrainObjectName = "Generated Terrain";

    [Header("Terrain Size")]
    [SerializeField] private Vector3 terrainSizeMeters = new Vector3(384f, 56f, 384f);
    [SerializeField, Min(33)] private int heightmapResolution = 257;

    [Header("Generation")]
    [SerializeField] private int seed = 12345;
    [SerializeField] private bool randomizeSeed = false;

    [Header("Terrain Shape")]
    [SerializeField, Min(1f)] private float macroNoiseScale = 220f;
    [SerializeField, Min(1)] private int macroNoiseOctaves = 5;
    [SerializeField, Range(0f, 1f)] private float macroNoisePersistence = 0.46f;
    [SerializeField, Min(1f)] private float macroNoiseLacunarity = 2.08f;
    [SerializeField, Min(1f)] private float ridgeNoiseScale = 180f;
    [SerializeField, Range(0f, 1f)] private float ridgeNoiseStrength = 0.18f;
    [SerializeField, Min(1f)] private float detailNoiseScale = 52f;
    [SerializeField, Range(0f, 1f)] private float detailNoiseStrength = 0.06f;
    [SerializeField, Range(0.1f, 4f)] private float heightExponent = 1.5f;
    [SerializeField, Range(0f, 0.5f)] private float baseHeightOffset = 0.04f;
    [SerializeField, Range(0f, 1f)] private float edgeFalloffStart = 0.82f;
    [SerializeField, Range(0f, 1f)] private float edgeFalloffStrength = 0.65f;

    [Header("Terrain Rendering")]
    [SerializeField, Min(1f)] private float basemapDistance = 1600f;
    [SerializeField, Range(1f, 200f)] private float heightmapPixelError = 6f;

    private Terrain generatedTerrain;
    private TerrainData generatedTerrainData;

    public Terrain GetGeneratedTerrain()
    {
        if (generatedTerrain != null)
        {
            return generatedTerrain;
        }

        Transform existingTerrainTransform = transform.Find(GeneratedTerrainObjectName);
        if (existingTerrainTransform != null)
        {
            generatedTerrain = existingTerrainTransform.GetComponent<Terrain>();
        }

        return generatedTerrain;
    }

    public TerrainData GetGeneratedTerrainData()
    {
        if (generatedTerrainData != null)
        {
            return generatedTerrainData;
        }

        Terrain terrain = GetGeneratedTerrain();
        if (terrain != null)
        {
            generatedTerrainData = terrain.terrainData;
        }

        return generatedTerrainData;
    }

    private void Reset()
    {
        ApplyOlympicRainforestPreset();
    }

    private void OnValidate()
    {
        terrainSizeMeters = new Vector3(
            Mathf.Max(8f, Mathf.Abs(terrainSizeMeters.x)),
            Mathf.Max(1f, Mathf.Abs(terrainSizeMeters.y)),
            Mathf.Max(8f, Mathf.Abs(terrainSizeMeters.z)));
        heightmapResolution = GetSanitizedHeightmapResolution(heightmapResolution);
        macroNoiseScale = Mathf.Max(1f, macroNoiseScale);
        macroNoiseOctaves = Mathf.Max(1, macroNoiseOctaves);
        macroNoisePersistence = Mathf.Clamp01(macroNoisePersistence);
        macroNoiseLacunarity = Mathf.Max(1f, macroNoiseLacunarity);
        ridgeNoiseScale = Mathf.Max(1f, ridgeNoiseScale);
        ridgeNoiseStrength = Mathf.Clamp01(ridgeNoiseStrength);
        detailNoiseScale = Mathf.Max(1f, detailNoiseScale);
        detailNoiseStrength = Mathf.Clamp01(detailNoiseStrength);
        heightExponent = Mathf.Clamp(heightExponent, 0.1f, 4f);
        baseHeightOffset = Mathf.Clamp(baseHeightOffset, 0f, 0.5f);
        edgeFalloffStart = Mathf.Clamp01(edgeFalloffStart);
        edgeFalloffStrength = Mathf.Clamp01(edgeFalloffStrength);
        basemapDistance = Mathf.Max(1f, basemapDistance);
        heightmapPixelError = Mathf.Clamp(heightmapPixelError, 1f, 200f);
    }

    [ContextMenu("Apply Olympic Rainforest Preset")]
    public void ApplyOlympicRainforestPreset()
    {
        terrainSizeMeters = new Vector3(384f, 56f, 384f);
        heightmapResolution = 257;
        macroNoiseScale = 220f;
        macroNoiseOctaves = 5;
        macroNoisePersistence = 0.46f;
        macroNoiseLacunarity = 2.08f;
        ridgeNoiseScale = 180f;
        ridgeNoiseStrength = 0.18f;
        detailNoiseScale = 52f;
        detailNoiseStrength = 0.06f;
        heightExponent = 1.5f;
        baseHeightOffset = 0.04f;
        edgeFalloffStart = 0.82f;
        edgeFalloffStrength = 0.65f;
        basemapDistance = 1600f;
        heightmapPixelError = 6f;
    }

    [ContextMenu("Generate Terrain")]
    public Terrain GenerateTerrain()
    {
        if (randomizeSeed)
        {
            seed = Environment.TickCount;
        }

        Terrain terrain = EnsureTerrainObject();
        TerrainData terrainData = EnsureTerrainData(terrain);

        float[,] heights = BuildHeights(terrainData.heightmapResolution, seed);
        terrainData.SetHeights(0, 0, heights);
        ApplyTerrainRenderingSettings(terrain);
        terrain.Flush();
        return terrain;
    }

    [ContextMenu("Flatten Terrain")]
    public void FlattenTerrain()
    {
        Terrain terrain = EnsureTerrainObject();
        TerrainData terrainData = EnsureTerrainData(terrain);
        float[,] heights = new float[terrainData.heightmapResolution, terrainData.heightmapResolution];
        terrainData.SetHeights(0, 0, heights);
        ApplyTerrainRenderingSettings(terrain);
        terrain.Flush();
    }

    private Terrain EnsureTerrainObject()
    {
        Terrain terrain = GetGeneratedTerrain();
        if (terrain == null)
        {
            GameObject terrainObject = new GameObject(GeneratedTerrainObjectName);
            terrainObject.layer = gameObject.layer;
            terrainObject.transform.SetParent(transform, false);
            terrainObject.transform.localPosition = Vector3.zero;
            terrainObject.transform.localRotation = Quaternion.identity;
            terrainObject.transform.localScale = Vector3.one;

            terrain = terrainObject.AddComponent<Terrain>();
            terrainObject.AddComponent<TerrainCollider>();
            generatedTerrain = terrain;
        }

        ApplyTerrainRenderingSettings(terrain);
        return terrain;
    }

    private TerrainData EnsureTerrainData(Terrain terrain)
    {
        if (terrain == null)
        {
            return null;
        }

        TerrainData terrainData = GetGeneratedTerrainData();
        if (terrainData == null)
        {
            terrainData = new TerrainData
            {
                name = $"{gameObject.name} Terrain Data"
            };
            generatedTerrainData = terrainData;
        }

        int sanitizedResolution = GetSanitizedHeightmapResolution(heightmapResolution);
        if (terrainData.heightmapResolution != sanitizedResolution)
        {
            terrainData.heightmapResolution = sanitizedResolution;
        }

        terrainData.size = terrainSizeMeters;
        terrain.terrainData = terrainData;

        TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (terrainCollider == null)
        {
            terrainCollider = terrain.gameObject.AddComponent<TerrainCollider>();
        }

        terrainCollider.terrainData = terrainData;
        generatedTerrainData = terrainData;
        return terrainData;
    }

    private void ApplyTerrainRenderingSettings(Terrain terrain)
    {
        if (terrain == null)
        {
            return;
        }

        terrain.drawInstanced = true;
        terrain.basemapDistance = basemapDistance;
        terrain.heightmapPixelError = heightmapPixelError;
        terrain.allowAutoConnect = false;
        terrain.groupingID = 0;
    }

    private float[,] BuildHeights(int resolution, int generationSeed)
    {
        float[,] heights = new float[resolution, resolution];
        System.Random random = new System.Random(generationSeed);
        float macroOffsetX = NextFloat(random, -10000f, 10000f);
        float macroOffsetZ = NextFloat(random, -10000f, 10000f);
        float detailOffsetX = NextFloat(random, -10000f, 10000f);
        float detailOffsetZ = NextFloat(random, -10000f, 10000f);

        for (int z = 0; z < resolution; z++)
        {
            float normalizedZ = resolution <= 1 ? 0f : z / (float)(resolution - 1);

            for (int x = 0; x < resolution; x++)
            {
                float normalizedX = resolution <= 1 ? 0f : x / (float)(resolution - 1);
                float worldX = normalizedX * terrainSizeMeters.x;
                float worldZ = normalizedZ * terrainSizeMeters.z;

                float macroNoise = EvaluateFractalNoise(
                    worldX + macroOffsetX,
                    worldZ + macroOffsetZ,
                    macroNoiseScale,
                    macroNoiseOctaves,
                    macroNoisePersistence,
                    macroNoiseLacunarity);
                float ridgeNoise = Mathf.PerlinNoise(
                    (worldX - (macroOffsetX * 0.47f)) / ridgeNoiseScale,
                    (worldZ + (macroOffsetZ * 0.39f)) / ridgeNoiseScale);
                ridgeNoise = 1f - Mathf.Abs((ridgeNoise * 2f) - 1f);
                ridgeNoise *= ridgeNoise;

                float detailNoise = Mathf.PerlinNoise(
                    (worldX + detailOffsetX) / detailNoiseScale,
                    (worldZ + detailOffsetZ) / detailNoiseScale);
                detailNoise = (detailNoise - 0.5f) * 2f;

                float height = Mathf.Clamp01(baseHeightOffset + (macroNoise * 0.78f) + (ridgeNoise * ridgeNoiseStrength));
                height = Mathf.Pow(height, heightExponent);
                height = Mathf.Clamp01(height + (detailNoise * detailNoiseStrength));
                height = ApplyEdgeFalloff(height, normalizedX, normalizedZ);
                heights[z, x] = Mathf.Clamp01(height);
            }
        }

        return heights;
    }

    private float ApplyEdgeFalloff(float height, float normalizedX, float normalizedZ)
    {
        float edgeX = Mathf.Abs((normalizedX * 2f) - 1f);
        float edgeZ = Mathf.Abs((normalizedZ * 2f) - 1f);
        float edgeDistance = Mathf.Max(edgeX, edgeZ);
        float falloffT = edgeDistance <= edgeFalloffStart
            ? 0f
            : Mathf.InverseLerp(edgeFalloffStart, 1f, edgeDistance);
        return Mathf.Lerp(height, height * (1f - edgeFalloffStrength), falloffT);
    }

    private static float EvaluateFractalNoise(
        float x,
        float z,
        float scale,
        int octaves,
        float persistence,
        float lacunarity)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float noiseSum = 0f;
        float amplitudeSum = 0f;

        for (int octave = 0; octave < Mathf.Max(1, octaves); octave++)
        {
            float sampleX = x / Mathf.Max(1f, scale) * frequency;
            float sampleZ = z / Mathf.Max(1f, scale) * frequency;
            float value = Mathf.PerlinNoise(sampleX, sampleZ);
            noiseSum += value * amplitude;
            amplitudeSum += amplitude;
            amplitude *= Mathf.Clamp01(persistence);
            frequency *= Mathf.Max(1f, lacunarity);
        }

        if (amplitudeSum <= 0f)
        {
            return 0f;
        }

        return noiseSum / amplitudeSum;
    }

    private static int GetSanitizedHeightmapResolution(int requestedResolution)
    {
        int clampedBase = Mathf.Clamp(Mathf.Max(32, requestedResolution - 1), 32, 4096);
        return Mathf.NextPowerOfTwo(clampedBase) + 1;
    }

    private static float NextFloat(System.Random random, float minInclusive, float maxInclusive)
    {
        if (Mathf.Approximately(minInclusive, maxInclusive))
        {
            return minInclusive;
        }

        float min = Mathf.Min(minInclusive, maxInclusive);
        float max = Mathf.Max(minInclusive, maxInclusive);
        return (float)(min + (random.NextDouble() * (max - min)));
    }
}
