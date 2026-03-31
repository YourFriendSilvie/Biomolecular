using System;
using System.Collections.Generic;
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

public class ProceduralTerrainScatterer : MonoBehaviour
{
    private const string DefaultGeneratedRootName = "Generated Biome Props";
    private static readonly Dictionary<string, Material> AutoMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);

    [Header("Terrain")]
    [SerializeField] private ProceduralTerrainGenerator terrainGenerator;
    [SerializeField] private Terrain targetTerrain;
    [SerializeField] private bool generateTerrainBeforeScattering = true;

    [Header("Water Exclusion")]
    [SerializeField] private ProceduralTerrainWaterSystem waterSystem;
    [SerializeField] private bool generateWaterBeforeScattering = true;
    [SerializeField, Min(0f)] private float waterExclusionPaddingMeters = 1f;

    [Header("Scatter Generation")]
    [SerializeField] private int seed = 24680;
    [SerializeField] private bool randomizeSeed = false;
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private string generatedRootName = DefaultGeneratedRootName;

    [Header("Biome Prototypes")]
    [SerializeField] private List<TerrainScatterPrototype> prototypes = new List<TerrainScatterPrototype>();

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;

    private void Reset()
    {
        ApplyOlympicRainforestPreset();
    }

    private void OnValidate()
    {
        generatedRootName = string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName.Trim();
        prototypes ??= new List<TerrainScatterPrototype>();

        if (prototypes.Count == 0)
        {
            ApplyOlympicRainforestPreset();
            return;
        }

        foreach (TerrainScatterPrototype prototype in prototypes)
        {
            prototype?.Sanitize();
        }
    }

    [ContextMenu("Apply Olympic Rainforest Preset")]
    public void ApplyOlympicRainforestPreset()
    {
        generatedRootName = DefaultGeneratedRootName;
        prototypes = BuildOlympicRainforestPrototypes();
    }

    [ContextMenu("Generate Scatter")]
    public void GenerateScatterFromContextMenu()
    {
        GenerateScatter(clearExistingBeforeGenerate);
    }

    [ContextMenu("Clear Generated Scatter")]
    public void ClearGeneratedScatterFromContextMenu()
    {
        ClearGeneratedScatter();
    }

    public List<GameObject> GenerateScatter(bool clearExisting)
    {
        List<GameObject> createdObjects = new List<GameObject>();
        Terrain terrain = ResolveTerrain();
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning($"{gameObject.name} could not scatter placeholders because no terrain is available.");
            return createdObjects;
        }

        if (randomizeSeed)
        {
            seed = Environment.TickCount;
        }

        ProceduralTerrainWaterSystem resolvedWaterSystem = ResolveWaterSystemAndMaybeGenerate();

        if (clearExisting)
        {
            ClearGeneratedScatter();
        }

        Transform generatedRoot = EnsureGeneratedRoot();
        TerrainData terrainData = terrain.terrainData;
        System.Random random = new System.Random(seed);

        foreach (TerrainScatterPrototype prototype in prototypes)
        {
            if (prototype == null)
            {
                continue;
            }

            prototype.Sanitize();
            CompositionInfo resolvedComposition = prototype.ResolveComposition();
            if (resolvedComposition == null)
            {
                Debug.LogWarning(
                    $"{gameObject.name} could not resolve a CompositionInfo for placeholder prototype {prototype.ResolveDisplayName()}.");
                continue;
            }

            GeneratePrototypeInstances(
                random,
                terrain,
                terrainData,
                resolvedWaterSystem,
                generatedRoot,
                prototype,
                resolvedComposition,
                createdObjects);
        }

        return createdObjects;
    }

    public bool ClearGeneratedScatter()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
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

        return true;
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
            targetTerrain = generateTerrainBeforeScattering
                ? terrainGenerator.GenerateTerrain()
                : terrainGenerator.GetGeneratedTerrain();
        }

        if (targetTerrain == null)
        {
            targetTerrain = GetComponentInChildren<Terrain>();
        }

        return targetTerrain;
    }

    private ProceduralTerrainWaterSystem ResolveWaterSystemAndMaybeGenerate()
    {
        if (waterSystem == null)
        {
            waterSystem = GetComponent<ProceduralTerrainWaterSystem>();
        }

        if (waterSystem != null && generateWaterBeforeScattering)
        {
            waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
        }

        return waterSystem;
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

    private void GeneratePrototypeInstances(
        System.Random random,
        Terrain terrain,
        TerrainData terrainData,
        ProceduralTerrainWaterSystem resolvedWaterSystem,
        Transform generatedRoot,
        TerrainScatterPrototype prototype,
        CompositionInfo resolvedComposition,
        ICollection<GameObject> createdObjects)
    {
        List<Vector3> placedPositions = new List<Vector3>(prototype.spawnCount);
        int placedCount = 0;
        int attempts = 0;
        int maxAttempts = prototype.spawnCount * prototype.maxPlacementAttemptsPerInstance;
        float densityOffsetX = NextFloat(random, -10000f, 10000f);
        float densityOffsetZ = NextFloat(random, -10000f, 10000f);

        while (placedCount < prototype.spawnCount && attempts < maxAttempts)
        {
            attempts++;

            float normalizedX = NextFloat(random, 0f, 1f);
            float normalizedZ = NextFloat(random, 0f, 1f);
            float localHeight = terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
            float normalizedHeight = terrainData.size.y <= 0.0001f
                ? 0f
                : Mathf.Clamp01(localHeight / terrainData.size.y);
            if (normalizedHeight < prototype.normalizedHeightRange.x || normalizedHeight > prototype.normalizedHeightRange.y)
            {
                continue;
            }

            float slope = terrainData.GetSteepness(normalizedX, normalizedZ);
            if (slope < prototype.slopeDegreesRange.x || slope > prototype.slopeDegreesRange.y)
            {
                continue;
            }

            Vector3 terrainPosition = terrain.transform.position;
            float worldX = terrainPosition.x + (normalizedX * terrainData.size.x);
            float worldZ = terrainPosition.z + (normalizedZ * terrainData.size.z);
            float density = Mathf.PerlinNoise(
                (worldX + densityOffsetX) / prototype.densityNoiseScale,
                (worldZ + densityOffsetZ) / prototype.densityNoiseScale);
            if (density < prototype.densityThreshold)
            {
                continue;
            }

            Vector3 surfaceNormal = terrainData.GetInterpolatedNormal(normalizedX, normalizedZ);
            Vector3 surfacePoint = new Vector3(worldX, terrainPosition.y + localHeight, worldZ);
            if (resolvedWaterSystem != null && resolvedWaterSystem.IsPointUnderWater(surfacePoint, waterExclusionPaddingMeters))
            {
                continue;
            }

            if (!IsFarEnoughFromExisting(surfacePoint, placedPositions, prototype.minimumSpacingMeters))
            {
                continue;
            }

            GameObject placeholder = GameObject.CreatePrimitive(prototype.primitiveType);
            placeholder.name = $"{prototype.ResolveDisplayName()}_{placedCount + 1:000}";
            placeholder.layer = gameObject.layer;
            placeholder.transform.SetParent(generatedRoot, true);

            Vector3 scale = NextVector3(random, prototype.minScale, prototype.maxScale);
            Vector3 upAxis = prototype.alignToSurfaceNormal && surfaceNormal.sqrMagnitude > 0.0001f
                ? surfaceNormal.normalized
                : Vector3.up;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, upAxis);
            if (prototype.randomizeYaw)
            {
                rotation = Quaternion.AngleAxis(NextFloat(random, 0f, 360f), upAxis) * rotation;
            }

            float halfHeight = GetPrimitiveHalfHeight(prototype.primitiveType, scale);
            placeholder.transform.rotation = rotation;
            placeholder.transform.localScale = scale;
            placeholder.transform.position = surfacePoint + (upAxis * halfHeight);

            MeshRenderer renderer = placeholder.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = ResolveRenderMaterial(prototype);
            }

            HarvestableObject harvestable = placeholder.AddComponent<HarvestableObject>();
            harvestable.Configure(
                resolvedComposition,
                NextFloat(random, prototype.totalMassRangeGrams.x, prototype.totalMassRangeGrams.y),
                prototype.harvestEfficiency,
                prototype.destroyOnHarvest,
                prototype.harvestsRequired,
                prototype.randomizeCompositionOnStart);

            createdObjects?.Add(placeholder);
            placedPositions.Add(surfacePoint);
            placedCount++;
        }

        if (placedCount < prototype.spawnCount)
        {
            Debug.LogWarning(
                $"{gameObject.name} only placed {placedCount} of {prototype.spawnCount} requested instances for {prototype.ResolveDisplayName()}.");
        }
    }

    private static Material ResolveRenderMaterial(TerrainScatterPrototype prototype)
    {
        if (prototype != null && prototype.material != null)
        {
            return prototype.material;
        }

        Color colorTint = prototype != null ? prototype.colorTint : Color.white;
        string displayName = prototype != null ? prototype.ResolveDisplayName() : "Placeholder";
        string cacheKey = $"{displayName}_{ColorUtility.ToHtmlStringRGBA(colorTint)}";
        if (AutoMaterials.TryGetValue(cacheKey, out Material cachedMaterial) && cachedMaterial != null)
        {
            return cachedMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader)
        {
            name = $"{displayName} Auto Material",
            hideFlags = HideFlags.HideAndDontSave
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", colorTint);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", colorTint);
        }

        AutoMaterials[cacheKey] = material;
        return material;
    }

    private static bool IsFarEnoughFromExisting(Vector3 position, IReadOnlyList<Vector3> existingPositions, float minimumSpacingMeters)
    {
        if (minimumSpacingMeters <= 0f || existingPositions == null || existingPositions.Count == 0)
        {
            return true;
        }

        float minimumSpacingSquared = minimumSpacingMeters * minimumSpacingMeters;
        for (int i = 0; i < existingPositions.Count; i++)
        {
            Vector3 planarDelta = Vector3.ProjectOnPlane(position - existingPositions[i], Vector3.up);
            if (planarDelta.sqrMagnitude < minimumSpacingSquared)
            {
                return false;
            }
        }

        return true;
    }

    private static float GetPrimitiveHalfHeight(PrimitiveType primitiveType, Vector3 scale)
    {
        switch (primitiveType)
        {
            case PrimitiveType.Cylinder:
            case PrimitiveType.Capsule:
                return scale.y;

            default:
                return scale.y * 0.5f;
        }
    }

    private static Vector3 NextVector3(System.Random random, Vector3 min, Vector3 max)
    {
        return new Vector3(
            NextFloat(random, min.x, max.x),
            NextFloat(random, min.y, max.y),
            NextFloat(random, min.z, max.z));
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

    private static List<TerrainScatterPrototype> BuildOlympicRainforestPrototypes()
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
