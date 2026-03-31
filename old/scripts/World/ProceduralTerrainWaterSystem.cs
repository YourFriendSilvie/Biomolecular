using System;
using System.Collections.Generic;
using UnityEngine;

public class ProceduralTerrainWaterSystem : MonoBehaviour
{
    private const string DefaultGeneratedRootName = "Generated Water";
    private const float DefaultHarvestedWaterMassGrams = 500f;
    private static Material cachedFreshWaterMaterial;
    private static Material cachedSaltWaterMaterial;

    private struct GeneratedLake
    {
        public Vector3 center;
        public float radius;
        public float surfaceY;
    }

    private struct GeneratedRiverSegment
    {
        public Vector3 start;
        public Vector3 end;
        public float width;
        public float surfaceY;
    }

    [Header("Terrain")]
    [SerializeField] private ProceduralTerrainGenerator terrainGenerator;
    [SerializeField] private Terrain targetTerrain;
    [SerializeField] private bool generateTerrainBeforeWater = true;

    [Header("Generation")]
    [SerializeField] private int seed = 35791;
    [SerializeField] private bool randomizeSeed = false;
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private string generatedRootName = DefaultGeneratedRootName;

    [Header("Ocean")]
    [SerializeField] private bool generateOcean = true;
    [SerializeField, Min(0f)] private float seaLevelMeters = 4.8f;
    [SerializeField, Min(1f)] private float oceanPaddingMeters = 220f;
    [SerializeField, Min(0.1f)] private float oceanDepthEquivalentMeters = 12f;

    [Header("Freshwater")]
    [SerializeField] private bool generateFreshwater = true;
    [SerializeField, Range(0, 4)] private int lakeCount = 2;
    [SerializeField] private Vector2 lakeRadiusRangeMeters = new Vector2(10f, 18f);
    [SerializeField, Min(0.25f)] private float lakeDepthMeters = 1.8f;
    [SerializeField, Range(0, 3)] private int riverCount = 1;
    [SerializeField] private Vector2 riverWidthRangeMeters = new Vector2(4f, 8f);
    [SerializeField, Min(0.25f)] private float riverDepthMeters = 1.2f;
    [SerializeField, Range(8, 64)] private int riverSampleCount = 24;
    [SerializeField] private Vector2 riverSourceHeightRangeNormalized = new Vector2(0.38f, 0.85f);

    [Header("Rendering")]
    [SerializeField, Min(0.05f)] private float waterSurfaceThicknessMeters = 0.2f;
    [SerializeField] private Material freshWaterMaterial;
    [SerializeField] private Material saltWaterMaterial;
    [SerializeField] private Color freshWaterColor = new Color(0.23f, 0.5f, 0.64f, 0.82f);
    [SerializeField] private Color saltWaterColor = new Color(0.16f, 0.34f, 0.58f, 0.84f);
    [SerializeField] private bool useTriggerColliders = true;

    private readonly List<GeneratedLake> generatedLakes = new List<GeneratedLake>();
    private readonly List<GeneratedRiverSegment> generatedRiverSegments = new List<GeneratedRiverSegment>();
    private GameObject generatedOceanObject;
    private FinitePlaneWaterHarvestable generatedOceanHarvestable;

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;
    public float SeaLevelMeters => GetCurrentOceanSurfaceY();

    private void Reset()
    {
        ApplyCoastalRainforestWaterPreset();
    }

    private void OnValidate()
    {
        generatedRootName = string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName.Trim();
        seaLevelMeters = Mathf.Max(0f, seaLevelMeters);
        oceanPaddingMeters = Mathf.Max(1f, oceanPaddingMeters);
        oceanDepthEquivalentMeters = Mathf.Max(0.1f, oceanDepthEquivalentMeters);
        lakeCount = Mathf.Clamp(lakeCount, 0, 4);
        lakeRadiusRangeMeters = new Vector2(
            Mathf.Max(1f, Mathf.Min(lakeRadiusRangeMeters.x, lakeRadiusRangeMeters.y)),
            Mathf.Max(1f, Mathf.Max(lakeRadiusRangeMeters.x, lakeRadiusRangeMeters.y)));
        lakeDepthMeters = Mathf.Max(0.25f, lakeDepthMeters);
        riverCount = Mathf.Clamp(riverCount, 0, 3);
        riverWidthRangeMeters = new Vector2(
            Mathf.Max(1f, Mathf.Min(riverWidthRangeMeters.x, riverWidthRangeMeters.y)),
            Mathf.Max(1f, Mathf.Max(riverWidthRangeMeters.x, riverWidthRangeMeters.y)));
        riverDepthMeters = Mathf.Max(0.25f, riverDepthMeters);
        riverSampleCount = Mathf.Clamp(riverSampleCount, 8, 64);
        riverSourceHeightRangeNormalized = new Vector2(
            Mathf.Clamp01(Mathf.Min(riverSourceHeightRangeNormalized.x, riverSourceHeightRangeNormalized.y)),
            Mathf.Clamp01(Mathf.Max(riverSourceHeightRangeNormalized.x, riverSourceHeightRangeNormalized.y)));
        waterSurfaceThicknessMeters = Mathf.Max(0.05f, waterSurfaceThicknessMeters);
    }

    [ContextMenu("Apply Coastal Rainforest Water Preset")]
    public void ApplyCoastalRainforestWaterPreset()
    {
        seaLevelMeters = 4.8f;
        oceanPaddingMeters = 220f;
        oceanDepthEquivalentMeters = 12f;
        lakeCount = 2;
        lakeRadiusRangeMeters = new Vector2(10f, 18f);
        lakeDepthMeters = 1.8f;
        riverCount = 1;
        riverWidthRangeMeters = new Vector2(4f, 8f);
        riverDepthMeters = 1.2f;
        riverSampleCount = 24;
        riverSourceHeightRangeNormalized = new Vector2(0.38f, 0.85f);
        waterSurfaceThicknessMeters = 0.2f;
    }

    [ContextMenu("Generate Water")]
    public void GenerateWaterFromContextMenu()
    {
        GenerateWater(clearExistingBeforeGenerate);
    }

    [ContextMenu("Clear Water")]
    public void ClearGeneratedWaterFromContextMenu()
    {
        ClearGeneratedWater();
    }

    public bool GenerateWater(bool clearExisting)
    {
        Terrain terrain = ResolveTerrain();
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning($"{gameObject.name} could not generate water because no terrain is available.");
            return false;
        }

        if (randomizeSeed)
        {
            seed = Environment.TickCount;
        }

        if (clearExisting)
        {
            ClearGeneratedWater();
        }

        generatedLakes.Clear();
        generatedRiverSegments.Clear();

        System.Random random = new System.Random(seed);
        TerrainData terrainData = terrain.terrainData;
        float[,] heights = terrainData.GetHeights(0, 0, terrainData.heightmapResolution, terrainData.heightmapResolution);
        bool heightsChanged = false;

        if (generateFreshwater)
        {
            heightsChanged |= GenerateLakes(random, terrain, terrainData, heights);
            heightsChanged |= GenerateRivers(random, terrain, terrainData, heights);
        }

        if (heightsChanged)
        {
            terrainData.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        Transform generatedRoot = EnsureGeneratedRoot();
        if (generateOcean)
        {
            CreateOceanObject(terrain, terrainData, generatedRoot);
        }

        foreach (GeneratedLake lake in generatedLakes)
        {
            CreateLakeObject(lake, generatedRoot);
        }

        for (int i = 0; i < generatedRiverSegments.Count; i++)
        {
            CreateRiverSegmentObject(generatedRiverSegments[i], generatedRoot, i);
        }

        return true;
    }

    public bool ClearGeneratedWater()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
            generatedOceanObject = null;
            generatedOceanHarvestable = null;
            return false;
        }

        if (Application.isPlaying)
        {
            Destroy(generatedRoot.gameObject);
        }
        else
        {
            DestroyImmediate(generatedRoot.gameObject);
        }

        generatedLakes.Clear();
        generatedRiverSegments.Clear();
        generatedOceanObject = null;
        generatedOceanHarvestable = null;
        return true;
    }

    public bool IsPointUnderWater(Vector3 worldPoint, float paddingMeters = 0f)
    {
        Terrain terrain = ResolveTerrain();
        if (terrain == null || terrain.terrainData == null)
        {
            return false;
        }

        Bounds terrainBounds = new Bounds(
            terrain.transform.position + new Vector3(terrain.terrainData.size.x * 0.5f, terrain.terrainData.size.y * 0.5f, terrain.terrainData.size.z * 0.5f),
            terrain.terrainData.size);

        if (TryGetOceanBounds(out Bounds oceanBounds) &&
            worldPoint.x >= oceanBounds.min.x &&
            worldPoint.x <= oceanBounds.max.x &&
            worldPoint.z >= oceanBounds.min.z &&
            worldPoint.z <= oceanBounds.max.z &&
            worldPoint.y <= GetCurrentOceanSurfaceY() + paddingMeters)
        {
            return true;
        }

        for (int i = 0; i < generatedLakes.Count; i++)
        {
            Vector2 pointXZ = new Vector2(worldPoint.x, worldPoint.z);
            Vector2 lakeXZ = new Vector2(generatedLakes[i].center.x, generatedLakes[i].center.z);
            if (Vector2.Distance(pointXZ, lakeXZ) <= generatedLakes[i].radius + paddingMeters &&
                worldPoint.y <= generatedLakes[i].surfaceY + paddingMeters)
            {
                return true;
            }
        }

        for (int i = 0; i < generatedRiverSegments.Count; i++)
        {
            if (DistanceToSegmentXZ(worldPoint, generatedRiverSegments[i].start, generatedRiverSegments[i].end) <= (generatedRiverSegments[i].width * 0.5f) + paddingMeters &&
                worldPoint.y <= generatedRiverSegments[i].surfaceY + paddingMeters)
            {
                return true;
            }
        }

        return false;
    }

    public Transform GetGeneratedRoot()
    {
        return transform.Find(string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName);
    }

    private Terrain ResolveTerrain()
    {
        if (terrainGenerator == null)
        {
            terrainGenerator = GetComponent<ProceduralTerrainGenerator>();
        }

        if (terrainGenerator != null)
        {
            targetTerrain = generateTerrainBeforeWater
                ? terrainGenerator.GenerateTerrain()
                : terrainGenerator.GetGeneratedTerrain();
        }

        if (targetTerrain == null)
        {
            targetTerrain = GetComponentInChildren<Terrain>();
        }

        return targetTerrain;
    }

    private Transform EnsureGeneratedRoot()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot != null)
        {
            return generatedRoot;
        }

        GameObject rootObject = new GameObject(generatedRootName);
        rootObject.layer = gameObject.layer;
        rootObject.transform.SetParent(transform, false);
        rootObject.transform.localPosition = Vector3.zero;
        rootObject.transform.localRotation = Quaternion.identity;
        rootObject.transform.localScale = Vector3.one;
        return rootObject.transform;
    }

    private bool GenerateLakes(System.Random random, Terrain terrain, TerrainData terrainData, float[,] heights)
    {
        if (lakeCount <= 0)
        {
            return false;
        }

        int generatedCount = 0;
        int attempts = 0;
        int maxAttempts = lakeCount * 24;
        float localSeaLevel = seaLevelMeters - terrain.transform.position.y;
        float seaLevelNormalized = terrainData.size.y <= 0.0001f ? 0f : Mathf.Clamp01(localSeaLevel / terrainData.size.y);

        while (generatedCount < lakeCount && attempts < maxAttempts)
        {
            attempts++;
            float normalizedX = NextFloat(random, 0.14f, 0.86f);
            float normalizedZ = NextFloat(random, 0.14f, 0.86f);
            float localHeight = terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
            float normalizedHeight = terrainData.size.y <= 0.0001f ? 0f : Mathf.Clamp01(localHeight / terrainData.size.y);
            if (normalizedHeight <= seaLevelNormalized + 0.03f || normalizedHeight >= 0.3f)
            {
                continue;
            }

            if (terrainData.GetSteepness(normalizedX, normalizedZ) > 8f)
            {
                continue;
            }

            float radius = NextFloat(random, lakeRadiusRangeMeters.x, lakeRadiusRangeMeters.y);
            Vector3 center = new Vector3(
                terrain.transform.position.x + (normalizedX * terrainData.size.x),
                terrain.transform.position.y + localHeight,
                terrain.transform.position.z + (normalizedZ * terrainData.size.z));
            if (IsTooCloseToExistingWater(center, radius * 1.8f))
            {
                continue;
            }

            float surfaceY = Mathf.Max(seaLevelMeters + 0.6f, center.y - (lakeDepthMeters * 0.12f));
            CarveLake(terrain, terrainData, heights, center, radius, surfaceY, lakeDepthMeters);
            generatedLakes.Add(new GeneratedLake
            {
                center = center,
                radius = radius,
                surfaceY = surfaceY
            });
            generatedCount++;
        }

        return generatedCount > 0;
    }

    private bool GenerateRivers(System.Random random, Terrain terrain, TerrainData terrainData, float[,] heights)
    {
        if (riverCount <= 0)
        {
            return false;
        }

        int generatedCount = 0;
        int attempts = 0;
        int maxAttempts = riverCount * 20;
        while (generatedCount < riverCount && attempts < maxAttempts)
        {
            attempts++;
            if (!TryFindRiverSource(random, terrain, terrainData, out Vector3 source))
            {
                continue;
            }

            Vector3 mouth = CreateRiverMouth(random, terrain, terrainData);
            float width = NextFloat(random, riverWidthRangeMeters.x, riverWidthRangeMeters.y);
            List<Vector3> samples = BuildRiverPath(random, source, mouth);
            float sourceSurfaceY = Mathf.Max(seaLevelMeters + riverDepthMeters + 0.6f, source.y - 0.2f);

            for (int i = 0; i < samples.Count; i++)
            {
                float t = samples.Count <= 1 ? 1f : i / (float)(samples.Count - 1);
                float surfaceY = Mathf.Lerp(sourceSurfaceY, seaLevelMeters + 0.05f, Mathf.SmoothStep(0f, 1f, t));
                CarveCircularChannel(terrain, terrainData, heights, samples[i], width * 0.58f, surfaceY, riverDepthMeters);

                if (i <= 0)
                {
                    continue;
                }

                float previousT = (i - 1) / (float)(samples.Count - 1);
                float previousSurfaceY = Mathf.Lerp(sourceSurfaceY, seaLevelMeters + 0.05f, Mathf.SmoothStep(0f, 1f, previousT));
                generatedRiverSegments.Add(new GeneratedRiverSegment
                {
                    start = new Vector3(samples[i - 1].x, previousSurfaceY, samples[i - 1].z),
                    end = new Vector3(samples[i].x, surfaceY, samples[i].z),
                    width = width,
                    surfaceY = Mathf.Lerp(previousSurfaceY, surfaceY, 0.5f)
                });
            }

            generatedCount++;
        }

        return generatedCount > 0;
    }

    private bool TryFindRiverSource(System.Random random, Terrain terrain, TerrainData terrainData, out Vector3 source)
    {
        source = Vector3.zero;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            float normalizedX = NextFloat(random, 0.18f, 0.82f);
            float normalizedZ = NextFloat(random, 0.22f, 0.82f);
            float localHeight = terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
            float normalizedHeight = terrainData.size.y <= 0.0001f ? 0f : Mathf.Clamp01(localHeight / terrainData.size.y);
            float slope = terrainData.GetSteepness(normalizedX, normalizedZ);
            if (normalizedHeight < riverSourceHeightRangeNormalized.x || normalizedHeight > riverSourceHeightRangeNormalized.y)
            {
                continue;
            }

            if (slope < 2f || slope > 24f)
            {
                continue;
            }

            source = new Vector3(
                terrain.transform.position.x + (normalizedX * terrainData.size.x),
                terrain.transform.position.y + localHeight,
                terrain.transform.position.z + (normalizedZ * terrainData.size.z));
            return true;
        }

        return false;
    }

    private Vector3 CreateRiverMouth(System.Random random, Terrain terrain, TerrainData terrainData)
    {
        float edgeSelector = NextFloat(random, 0f, 4f);
        float x;
        float z;

        if (edgeSelector < 1f)
        {
            x = 0f;
            z = NextFloat(random, 0.12f, 0.88f);
        }
        else if (edgeSelector < 2f)
        {
            x = 1f;
            z = NextFloat(random, 0.12f, 0.88f);
        }
        else if (edgeSelector < 3f)
        {
            x = NextFloat(random, 0.12f, 0.88f);
            z = 0f;
        }
        else
        {
            x = NextFloat(random, 0.12f, 0.88f);
            z = 1f;
        }

        return new Vector3(
            terrain.transform.position.x + (x * terrainData.size.x),
            seaLevelMeters,
            terrain.transform.position.z + (z * terrainData.size.z));
    }

    private List<Vector3> BuildRiverPath(System.Random random, Vector3 source, Vector3 mouth)
    {
        int sampleCount = Mathf.Max(8, riverSampleCount);
        Vector3 flattenedSource = new Vector3(source.x, 0f, source.z);
        Vector3 flattenedMouth = new Vector3(mouth.x, 0f, mouth.z);
        Vector3 direction = (flattenedMouth - flattenedSource).normalized;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }

        Vector3 perpendicular = Vector3.Cross(Vector3.up, direction);
        float terrainSpan = Vector3.Distance(flattenedSource, flattenedMouth);
        Vector3 controlOne = Vector3.Lerp(source, mouth, 0.28f) + (perpendicular * NextFloat(random, -terrainSpan * 0.18f, terrainSpan * 0.18f));
        Vector3 controlTwo = Vector3.Lerp(source, mouth, 0.68f) + (perpendicular * NextFloat(random, -terrainSpan * 0.22f, terrainSpan * 0.22f));

        List<Vector3> samples = new List<Vector3>(sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            float t = sampleCount <= 1 ? 1f : i / (float)(sampleCount - 1);
            samples.Add(EvaluateCubicBezier(source, controlOne, controlTwo, mouth, t));
        }

        return samples;
    }

    private void CarveLake(
        Terrain terrain,
        TerrainData terrainData,
        float[,] heights,
        Vector3 center,
        float radiusMeters,
        float surfaceY,
        float depthMeters)
    {
        int resolution = terrainData.heightmapResolution;
        Vector3 terrainOrigin = terrain.transform.position;
        int centerX = Mathf.RoundToInt(((center.x - terrainOrigin.x) / terrainData.size.x) * (resolution - 1));
        int centerZ = Mathf.RoundToInt(((center.z - terrainOrigin.z) / terrainData.size.z) * (resolution - 1));
        int radiusX = Mathf.CeilToInt((radiusMeters / terrainData.size.x) * (resolution - 1));
        int radiusZ = Mathf.CeilToInt((radiusMeters / terrainData.size.z) * (resolution - 1));

        float centerHeight = Mathf.Clamp01((surfaceY - depthMeters - terrainOrigin.y) / terrainData.size.y);
        float shoreHeight = Mathf.Clamp01((surfaceY - terrainOrigin.y) / terrainData.size.y);

        for (int z = Mathf.Max(0, centerZ - radiusZ - 2); z <= Mathf.Min(resolution - 1, centerZ + radiusZ + 2); z++)
        {
            for (int x = Mathf.Max(0, centerX - radiusX - 2); x <= Mathf.Min(resolution - 1, centerX + radiusX + 2); x++)
            {
                float worldX = terrainOrigin.x + (x / (float)(resolution - 1)) * terrainData.size.x;
                float worldZ = terrainOrigin.z + (z / (float)(resolution - 1)) * terrainData.size.z;
                float distance = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(center.x, center.z));
                if (distance > radiusMeters * 1.1f)
                {
                    continue;
                }

                float t = Mathf.Clamp01(distance / Mathf.Max(0.001f, radiusMeters));
                float basin = Mathf.SmoothStep(1f, 0f, t);
                float targetHeight = Mathf.Lerp(shoreHeight, centerHeight, basin);
                if (heights[z, x] > targetHeight)
                {
                    heights[z, x] = targetHeight;
                }
            }
        }
    }

    private void CarveCircularChannel(
        Terrain terrain,
        TerrainData terrainData,
        float[,] heights,
        Vector3 center,
        float radiusMeters,
        float surfaceY,
        float depthMeters)
    {
        int resolution = terrainData.heightmapResolution;
        Vector3 terrainOrigin = terrain.transform.position;
        int centerX = Mathf.RoundToInt(((center.x - terrainOrigin.x) / terrainData.size.x) * (resolution - 1));
        int centerZ = Mathf.RoundToInt(((center.z - terrainOrigin.z) / terrainData.size.z) * (resolution - 1));
        int radiusX = Mathf.CeilToInt((radiusMeters / terrainData.size.x) * (resolution - 1));
        int radiusZ = Mathf.CeilToInt((radiusMeters / terrainData.size.z) * (resolution - 1));

        float channelBottom = Mathf.Clamp01((surfaceY - depthMeters - terrainOrigin.y) / terrainData.size.y);
        float bankHeight = Mathf.Clamp01((surfaceY - terrainOrigin.y) / terrainData.size.y);

        for (int z = Mathf.Max(0, centerZ - radiusZ - 2); z <= Mathf.Min(resolution - 1, centerZ + radiusZ + 2); z++)
        {
            for (int x = Mathf.Max(0, centerX - radiusX - 2); x <= Mathf.Min(resolution - 1, centerX + radiusX + 2); x++)
            {
                float worldX = terrainOrigin.x + (x / (float)(resolution - 1)) * terrainData.size.x;
                float worldZ = terrainOrigin.z + (z / (float)(resolution - 1)) * terrainData.size.z;
                float distance = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(center.x, center.z));
                if (distance > radiusMeters * 1.3f)
                {
                    continue;
                }

                float t = Mathf.Clamp01(distance / Mathf.Max(0.001f, radiusMeters));
                float channelStrength = Mathf.SmoothStep(1f, 0f, t);
                float targetHeight = Mathf.Lerp(bankHeight, channelBottom, channelStrength);
                if (heights[z, x] > targetHeight)
                {
                    heights[z, x] = targetHeight;
                }
            }
        }
    }

    private void CreateOceanObject(Terrain terrain, TerrainData terrainData, Transform generatedRoot)
    {
        CompositionInfo seaWaterComposition = ResolveComposition("Sea Water");
        GameObject ocean = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ocean.name = "Ocean";
        ocean.layer = gameObject.layer;
        ocean.transform.SetParent(generatedRoot, true);
        ocean.transform.position = terrain.transform.position + new Vector3(
            terrainData.size.x * 0.5f,
            seaLevelMeters - (waterSurfaceThicknessMeters * 0.5f),
            terrainData.size.z * 0.5f);
        ocean.transform.localScale = new Vector3(
            terrainData.size.x + (oceanPaddingMeters * 2f),
            waterSurfaceThicknessMeters,
            terrainData.size.z + (oceanPaddingMeters * 2f));

        ConfigureWaterObject(ocean, ResolveSaltWaterMaterial(), seaWaterComposition, false);
        generatedOceanObject = ocean;
        generatedOceanHarvestable = EnsureFinitePlaneWaterHarvestable(
            ocean,
            seaWaterComposition,
            DefaultHarvestedWaterMassGrams,
            ocean.transform.localScale.x * ocean.transform.localScale.z * oceanDepthEquivalentMeters,
            seaLevelMeters,
            ocean.transform.localScale.x * ocean.transform.localScale.z,
            waterSurfaceThicknessMeters);
    }

    private void CreateLakeObject(GeneratedLake lake, Transform generatedRoot)
    {
        CompositionInfo freshWaterComposition = ResolveComposition("Fresh Water");
        GameObject lakeObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lakeObject.name = "Freshwater Lake";
        lakeObject.layer = gameObject.layer;
        lakeObject.transform.SetParent(generatedRoot, true);
        lakeObject.transform.position = new Vector3(
            lake.center.x,
            lake.surfaceY - (waterSurfaceThicknessMeters * 0.5f),
            lake.center.z);
        lakeObject.transform.localScale = new Vector3(
            lake.radius * 2f,
            waterSurfaceThicknessMeters * 0.5f,
            lake.radius * 2f);

        ConfigureWaterObject(lakeObject, ResolveFreshWaterMaterial(), freshWaterComposition);
    }

    private void CreateRiverSegmentObject(GeneratedRiverSegment segment, Transform generatedRoot, int index)
    {
        CompositionInfo freshWaterComposition = ResolveComposition("Fresh Water");
        Vector3 delta = segment.end - segment.start;
        Vector3 flatDelta = new Vector3(delta.x, 0f, delta.z);
        if (flatDelta.sqrMagnitude < 0.0001f)
        {
            return;
        }

        GameObject riverObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        riverObject.name = $"River Segment {index + 1:000}";
        riverObject.layer = gameObject.layer;
        riverObject.transform.SetParent(generatedRoot, true);
        riverObject.transform.position = new Vector3(
            Mathf.Lerp(segment.start.x, segment.end.x, 0.5f),
            segment.surfaceY - (waterSurfaceThicknessMeters * 0.5f),
            Mathf.Lerp(segment.start.z, segment.end.z, 0.5f));
        riverObject.transform.rotation = Quaternion.LookRotation(flatDelta.normalized, Vector3.up);
        riverObject.transform.localScale = new Vector3(
            segment.width,
            waterSurfaceThicknessMeters,
            flatDelta.magnitude + (segment.width * 0.45f));

        ConfigureWaterObject(riverObject, ResolveFreshWaterMaterial(), freshWaterComposition);
    }

    private void ConfigureWaterObject(GameObject waterObject, Material material, CompositionInfo composition, bool attachHarvestable = true)
    {
        if (waterObject == null)
        {
            return;
        }

        MeshRenderer renderer = waterObject.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        Collider collider = waterObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = useTriggerColliders;
        }

        if (attachHarvestable && composition != null)
        {
            HarvestableObject harvestable = waterObject.AddComponent<HarvestableObject>();
            harvestable.Configure(
                composition,
                DefaultHarvestedWaterMassGrams,
                1f,
                false,
                1,
                false);
        }
    }

    private FinitePlaneWaterHarvestable EnsureFinitePlaneWaterHarvestable(
        GameObject waterObject,
        CompositionInfo composition,
        float harvestMassGrams,
        float totalVolumeCubicMeters,
        float surfaceY,
        float surfaceAreaSquareMeters,
        float visibleThicknessMeters)
    {
        if (waterObject == null || composition == null)
        {
            return null;
        }

        HarvestableObject staticHarvestable = waterObject.GetComponent<HarvestableObject>();
        if (staticHarvestable != null)
        {
            if (Application.isPlaying)
            {
                Destroy(staticHarvestable);
            }
            else
            {
                DestroyImmediate(staticHarvestable);
            }
        }

        FinitePlaneWaterHarvestable harvestable = waterObject.GetComponent<FinitePlaneWaterHarvestable>();
        if (harvestable == null)
        {
            harvestable = waterObject.AddComponent<FinitePlaneWaterHarvestable>();
        }

        harvestable.Configure(
            composition,
            harvestMassGrams,
            totalVolumeCubicMeters,
            surfaceY,
            surfaceAreaSquareMeters,
            visibleThicknessMeters);
        return harvestable;
    }

    private float GetCurrentOceanSurfaceY()
    {
        return generatedOceanHarvestable != null
            ? generatedOceanHarvestable.CurrentSurfaceY
            : seaLevelMeters;
    }

    private bool TryGetOceanBounds(out Bounds oceanBounds)
    {
        oceanBounds = default;
        if (!generateOcean || generatedOceanHarvestable == null || !generatedOceanHarvestable.HasWater || generatedOceanObject == null)
        {
            return false;
        }

        Collider oceanCollider = generatedOceanObject.GetComponent<Collider>();
        if (oceanCollider == null || !oceanCollider.enabled)
        {
            return false;
        }

        oceanBounds = oceanCollider.bounds;
        return true;
    }

    private Material ResolveFreshWaterMaterial()
    {
        return ResolveWaterMaterial(freshWaterMaterial, ref cachedFreshWaterMaterial, freshWaterColor, "Fresh Water Auto Material");
    }

    private Material ResolveSaltWaterMaterial()
    {
        return ResolveWaterMaterial(saltWaterMaterial, ref cachedSaltWaterMaterial, saltWaterColor, "Salt Water Auto Material");
    }

    private CompositionInfo ResolveComposition(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        CompositionInfoRegistry.TryGetByItemName(itemName, out CompositionInfo compositionInfo);
        return compositionInfo;
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

    private static float DistanceToSegmentXZ(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector2 pointXZ = new Vector2(point.x, point.z);
        Vector2 startXZ = new Vector2(start.x, start.z);
        Vector2 endXZ = new Vector2(end.x, end.z);
        Vector2 segment = endXZ - startXZ;
        float segmentLengthSquared = segment.sqrMagnitude;
        if (segmentLengthSquared <= 0.0001f)
        {
            return Vector2.Distance(pointXZ, startXZ);
        }

        float t = Mathf.Clamp01(Vector2.Dot(pointXZ - startXZ, segment) / segmentLengthSquared);
        Vector2 projection = startXZ + (segment * t);
        return Vector2.Distance(pointXZ, projection);
    }

    private static Vector3 EvaluateCubicBezier(Vector3 start, Vector3 controlOne, Vector3 controlTwo, Vector3 end, float t)
    {
        float oneMinusT = 1f - t;
        return (oneMinusT * oneMinusT * oneMinusT * start)
            + (3f * oneMinusT * oneMinusT * t * controlOne)
            + (3f * oneMinusT * t * t * controlTwo)
            + (t * t * t * end);
    }

    private static Material ResolveWaterMaterial(Material assignedMaterial, ref Material cachedMaterial, Color color, string materialName)
    {
        if (assignedMaterial != null)
        {
            return assignedMaterial;
        }

        if (cachedMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            cachedMaterial = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave
            };

            if (cachedMaterial.HasProperty("_BaseColor"))
            {
                cachedMaterial.SetColor("_BaseColor", color);
            }

            if (cachedMaterial.HasProperty("_Color"))
            {
                cachedMaterial.SetColor("_Color", color);
            }

            if (cachedMaterial.HasProperty("_Surface"))
            {
                cachedMaterial.SetFloat("_Surface", 1f);
            }
        }

        return cachedMaterial;
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
