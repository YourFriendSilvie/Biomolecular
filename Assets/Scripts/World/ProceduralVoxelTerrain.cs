using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class ProceduralVoxelTerrain : MonoBehaviour, IRaycastHarvestableProvider
{
    private const string GeneratedRootName = "Generated Voxel Terrain";
    private static readonly Dictionary<string, Material> AutoMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);
    // Supporting types and extracted helpers now live under Assets\Scripts\World\VoxelTerrain\...
    // partials so this facade stays focused on inspector configuration, lifecycle,
    // and the public terrain API.

    private sealed class HarvestTarget : IHarvestable
    {
        private readonly ProceduralVoxelTerrain owner;
        private readonly Vector3 hitPoint;
        private readonly int sampledMaterialIndex;

        public HarvestTarget(ProceduralVoxelTerrain owner, Vector3 hitPoint, int sampledMaterialIndex)
        {
            this.owner = owner;
            this.hitPoint = hitPoint;
            this.sampledMaterialIndex = sampledMaterialIndex;
        }

        public bool Harvest(Inventory playerInventory)
        {
            return owner != null && owner.ExcavateSphere(hitPoint, playerInventory);
        }

        public string GetHarvestDisplayName()
        {
            return owner != null ? owner.GetHarvestDisplayName(sampledMaterialIndex) : "Terrain";
        }

        public string GetHarvestPreview()
        {
            return owner != null ? owner.GetHarvestPreview(sampledMaterialIndex, hitPoint) : "Nothing to harvest";
        }
    }


    [Header("Layout")]
    [SerializeField] private Vector3Int chunkCounts = new Vector3Int(4, 2, 4);
    [SerializeField, Min(4)] private int cellsPerChunkAxis = 16;
    [SerializeField, Min(0.1f)] private float voxelSizeMeters = 1f;
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private bool generateOnStart = true;

    [Header("Generation")]
    [SerializeField] private bool useAsyncBuildPipeline = true;
    [SerializeField, Min(0.25f)] private float asyncGenerationFrameBudgetMilliseconds = 4f;
    [SerializeField, Min(1)] private int asyncChunkBuildQueueSize = 2;

    [Header("Runtime Streaming")]
    [SerializeField] private bool enableRuntimeStreamingMode = false;
    [SerializeField] private Transform runtimeStreamingAnchor;
    [SerializeField, Min(0)] private int runtimeStreamingStartupChunkRadius = 2;

    [Header("Terrain Shape")]
    [SerializeField] private int seed = 24681357;
    [SerializeField] private bool randomizeSeed = false;
    [SerializeField, Min(0f)] private float baseSurfaceHeightMeters = 18f;
    [SerializeField, Min(0f)] private float seaLevelMeters = 4.8f;
    [SerializeField, Min(1f)] private float surfaceNoiseScaleMeters = 84f;
    [SerializeField, Min(1)] private int surfaceNoiseOctaves = 4;
    [SerializeField, Range(0f, 1f)] private float surfaceNoisePersistence = 0.5f;
    [SerializeField, Min(1f)] private float surfaceNoiseLacunarity = 2f;
    [SerializeField, Min(0f)] private float surfaceAmplitudeMeters = 10f;
    [SerializeField, Min(1f)] private float ridgeNoiseScaleMeters = 46f;
    [SerializeField, Min(0f)] private float ridgeAmplitudeMeters = 3.2f;
    [SerializeField, Min(1f)] private float detailNoiseScaleMeters = 24f;
    [SerializeField, Min(0f)] private float detailAmplitudeMeters = 1.1f;
    [SerializeField, Min(1f)] private float caveNoiseScaleMeters = 18f;
    [SerializeField, Range(0f, 1f)] private float caveNoiseThreshold = 0.74f;
    [SerializeField, Min(0f)] private float caveCarveStrengthMeters = 12f;
    [SerializeField, Min(0f)] private float caveStartDepthMeters = 7f;

    [Header("Island Profile")]
    [SerializeField] private bool shapeAsIsland = true;
    [SerializeField, Range(0.2f, 0.8f)] private float islandCoreRadiusNormalized = 0.5f;
    [SerializeField, Range(0.05f, 0.4f)] private float coastalShelfWidthNormalized = 0.16f;
    [SerializeField, Min(0f)] private float beachHeightMeters = 0.95f;
    [SerializeField, Min(0.25f)] private float oceanFloorDepthMeters = 3.4f;
    [SerializeField, Min(0f)] private float oceanFloorVariationMeters = 0.8f;
    [SerializeField, Min(1f)] private float oceanFloorNoiseScaleMeters = 42f;

    [Header("Mining")]
    [SerializeField, Min(0.1f)] private float miningRadiusMeters = 1.35f;
    [SerializeField, Min(0.05f)] private float excavationStrengthMeters = 2.1f;
    [SerializeField, Min(0.1f)] private float harvestedMassPerSolidCellGrams = 850f;
    [SerializeField, Min(0f)] private float minimumMineableElevationMeters = 1.5f;

    [Header("Materials")]
    [SerializeField] private List<VoxelTerrainMaterialDefinition> materialDefinitions = new List<VoxelTerrainMaterialDefinition>();

    [Header("Debug")]
    [SerializeField] private bool drawWorldBoundsGizmos = true;
    [SerializeField] private bool drawChunkBoundsGizmos = false;
    [SerializeField] private Color boundsGizmoColor = new Color(0.36f, 0.76f, 0.92f, 0.85f);
    [SerializeField] private bool logGenerationTimings = false;
    [SerializeField] private bool logExcavationDebug = false;

    [NonSerialized] private float[] densitySamples;
    [NonSerialized] private byte[] cellMaterialIndices;
    [NonSerialized] private float[] surfaceHeightPrepass;
    [NonSerialized] private bool surfaceHeightPrepassReady;
    [NonSerialized] private ColumnMaterialProfile[] columnProfilePrepass;
    [NonSerialized] private TerrainGenerationOperation activeTerrainGenerationOperation;
    private readonly ChunkStreamingManager streamingManager = new ChunkStreamingManager();
    private readonly Dictionary<Vector3Int, ProceduralVoxelTerrainChunk> generatedChunks = new Dictionary<Vector3Int, ProceduralVoxelTerrainChunk>();
    private readonly HashSet<Vector3Int> bulkEditedChunkCoordinates = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> bulkGeometryChunkCoordinates = new HashSet<Vector3Int>();
    private readonly List<Action<bool>> terrainGenerationCompletionCallbacks = new List<Action<bool>>();
    private Material[] sharedChunkMaterials = Array.Empty<Material>();
    private ProceduralVoxelTerrainWaterSystem cachedWaterSystem;
    private GenerationMaterialIndices generationMaterialIndices;
    private Coroutine activeTerrainGenerationCoroutine;
    private Bounds bulkGeometryWorldBounds;
    private int bulkEditDepth;
    private bool hasBulkGeometryWorldBounds;
    private bool suppressExcavationDebugDuringBulkEdit;
#if UNITY_EDITOR
    private EditorTerrainGenerationDriver activeEditorTerrainGenerationDriver;
#endif

    private bool HasReadySurfaceHeightPrepass => surfaceHeightPrepassReady &&
        surfaceHeightPrepass != null &&
        surfaceHeightPrepass.Length == TotalSamplesX * TotalSamplesZ;

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;
    public bool IsRuntimeStreamingModeActive => Application.isPlaying && enableRuntimeStreamingMode;
    public bool HasRuntimeStreamingStartupAreaReady => streamingManager.StartupChunksReady && HasReadySurfaceHeightPrepass;
    public bool HasReadyGameplayTerrain => HasGeneratedTerrain || HasRuntimeStreamingStartupAreaReady;
    public bool HasGeneratedTerrain => densitySamples != null && generatedChunks.Count > 0 && !IsTerrainGenerationInProgress;
    public bool IsTerrainGenerationInProgress => activeTerrainGenerationOperation != null;
    public float TerrainGenerationProgress01 => activeTerrainGenerationOperation != null
        ? activeTerrainGenerationOperation.Progress01
        : (HasGeneratedTerrain ? 1f : 0f);
    public string TerrainGenerationStatus => activeTerrainGenerationOperation?.Status ?? string.Empty;
    public Vector3 WorldSize => TotalWorldSize;
    public Bounds WorldBounds => new Bounds(transform.position + (TotalWorldSize * 0.5f), TotalWorldSize);
    public float SeaLevelMeters => seaLevelMeters;
    public float VoxelSizeMeters => voxelSizeMeters;
    public float ChunkWorldSizeMeters => cellsPerChunkAxis * voxelSizeMeters;
    public event Action<VoxelTerrainGeometryChangedEventArgs> GeometryChanged;
    public event Action<bool> TerrainGenerationCompleted;

    public void BeginBulkEdit(bool suppressExcavationLogs = true)
    {
        bulkEditDepth++;
        if (suppressExcavationLogs)
        {
            suppressExcavationDebugDuringBulkEdit = true;
        }
    }

    public void EndBulkEdit()
    {
        if (bulkEditDepth <= 0)
        {
            return;
        }

        bulkEditDepth--;
        if (bulkEditDepth > 0)
        {
            return;
        }

        foreach (Vector3Int chunkCoordinate in bulkEditedChunkCoordinates)
        {
            RebuildChunk(chunkCoordinate);
        }

        if (hasBulkGeometryWorldBounds && bulkGeometryChunkCoordinates.Count > 0)
        {
            GeometryChanged?.Invoke(new VoxelTerrainGeometryChangedEventArgs(
                bulkGeometryWorldBounds,
                new List<Vector3Int>(bulkGeometryChunkCoordinates)));
        }

        bulkEditedChunkCoordinates.Clear();
        bulkGeometryChunkCoordinates.Clear();
        bulkGeometryWorldBounds = default;
        bulkEditDepth = 0;
        hasBulkGeometryWorldBounds = false;
        suppressExcavationDebugDuringBulkEdit = false;
    }

    private int TotalCellsX => Mathf.Max(1, chunkCounts.x) * Mathf.Max(4, cellsPerChunkAxis);
    private int TotalCellsY => Mathf.Max(1, chunkCounts.y) * Mathf.Max(4, cellsPerChunkAxis);
    private int TotalCellsZ => Mathf.Max(1, chunkCounts.z) * Mathf.Max(4, cellsPerChunkAxis);
    private int TotalSamplesX => TotalCellsX + 1;
    private int TotalSamplesY => TotalCellsY + 1;
    private int TotalSamplesZ => TotalCellsZ + 1;
    private int TotalChunkCount => Mathf.Max(1, chunkCounts.x) * Mathf.Max(1, chunkCounts.y) * Mathf.Max(1, chunkCounts.z);
    private Vector3 TotalWorldSize => new Vector3(TotalCellsX * voxelSizeMeters, TotalCellsY * voxelSizeMeters, TotalCellsZ * voxelSizeMeters);

    private void Reset()
    {
        ApplyOlympicRainforestPreset();
    }

    private void Start()
    {
        if (Application.isPlaying && generateOnStart)
        {
            GenerateTerrainWithConfiguredMode(clearExistingBeforeGenerate);
        }
    }

    private void OnValidate()
    {
        chunkCounts = new Vector3Int(
            Mathf.Max(1, chunkCounts.x),
            Mathf.Max(1, chunkCounts.y),
            Mathf.Max(1, chunkCounts.z));
        cellsPerChunkAxis = Mathf.Max(4, cellsPerChunkAxis);
        voxelSizeMeters = Mathf.Max(0.1f, voxelSizeMeters);
        asyncGenerationFrameBudgetMilliseconds = Mathf.Max(0.25f, asyncGenerationFrameBudgetMilliseconds);
        asyncChunkBuildQueueSize = Mathf.Max(1, asyncChunkBuildQueueSize);
        baseSurfaceHeightMeters = Mathf.Max(0f, baseSurfaceHeightMeters);
        seaLevelMeters = Mathf.Max(0f, seaLevelMeters);
        surfaceNoiseScaleMeters = Mathf.Max(1f, surfaceNoiseScaleMeters);
        surfaceNoiseOctaves = Mathf.Max(1, surfaceNoiseOctaves);
        surfaceNoisePersistence = Mathf.Clamp01(surfaceNoisePersistence);
        surfaceNoiseLacunarity = Mathf.Max(1f, surfaceNoiseLacunarity);
        surfaceAmplitudeMeters = Mathf.Max(0f, surfaceAmplitudeMeters);
        ridgeNoiseScaleMeters = Mathf.Max(1f, ridgeNoiseScaleMeters);
        ridgeAmplitudeMeters = Mathf.Max(0f, ridgeAmplitudeMeters);
        detailNoiseScaleMeters = Mathf.Max(1f, detailNoiseScaleMeters);
        detailAmplitudeMeters = Mathf.Max(0f, detailAmplitudeMeters);
        caveNoiseScaleMeters = Mathf.Max(1f, caveNoiseScaleMeters);
        caveNoiseThreshold = Mathf.Clamp01(caveNoiseThreshold);
        caveCarveStrengthMeters = Mathf.Max(0f, caveCarveStrengthMeters);
        caveStartDepthMeters = Mathf.Max(0f, caveStartDepthMeters);
        runtimeStreamingStartupChunkRadius = Mathf.Max(0, runtimeStreamingStartupChunkRadius);
        islandCoreRadiusNormalized = Mathf.Clamp(islandCoreRadiusNormalized, 0.2f, 0.8f);
        coastalShelfWidthNormalized = Mathf.Clamp(coastalShelfWidthNormalized, 0.05f, 0.4f);
        beachHeightMeters = Mathf.Max(0f, beachHeightMeters);
        oceanFloorDepthMeters = Mathf.Max(0.25f, oceanFloorDepthMeters);
        oceanFloorVariationMeters = Mathf.Max(0f, oceanFloorVariationMeters);
        oceanFloorNoiseScaleMeters = Mathf.Max(1f, oceanFloorNoiseScaleMeters);
        miningRadiusMeters = Mathf.Max(0.1f, miningRadiusMeters);
        excavationStrengthMeters = Mathf.Max(0.05f, excavationStrengthMeters);
        harvestedMassPerSolidCellGrams = Mathf.Max(0.1f, harvestedMassPerSolidCellGrams);
        minimumMineableElevationMeters = Mathf.Max(0f, minimumMineableElevationMeters);
        materialDefinitions ??= new List<VoxelTerrainMaterialDefinition>();
        if (materialDefinitions.Count == 0)
        {
            ApplyOlympicRainforestPreset();
            return;
        }

        EnsureDefaultMaterialDefinitionsPresent();
        foreach (VoxelTerrainMaterialDefinition definition in materialDefinitions)
        {
            definition?.Sanitize();
        }
    }

    [ContextMenu("Apply Olympic Rainforest Voxel Preset")]
    public void ApplyOlympicRainforestPreset()
    {
        chunkCounts = new Vector3Int(4, 2, 4);
        cellsPerChunkAxis = 16;
        voxelSizeMeters = 1f;
        baseSurfaceHeightMeters = 18f;
        seaLevelMeters = 5f;
        surfaceNoiseScaleMeters = 84f;
        surfaceNoiseOctaves = 4;
        surfaceNoisePersistence = 0.5f;
        surfaceNoiseLacunarity = 2f;
        surfaceAmplitudeMeters = 10f;
        ridgeNoiseScaleMeters = 46f;
        ridgeAmplitudeMeters = 3.2f;
        detailNoiseScaleMeters = 24f;
        detailAmplitudeMeters = 1.1f;
        caveNoiseScaleMeters = 18f;
        caveNoiseThreshold = 0.74f;
        caveCarveStrengthMeters = 12f;
        caveStartDepthMeters = 7f;
        shapeAsIsland = true;
        islandCoreRadiusNormalized = 0.5f;
        coastalShelfWidthNormalized = 0.16f;
        beachHeightMeters = 0.95f;
        oceanFloorDepthMeters = 3.5f;
        oceanFloorVariationMeters = 0.75f;
        oceanFloorNoiseScaleMeters = 42f;
        miningRadiusMeters = 1.35f;
        excavationStrengthMeters = 2.1f;
        harvestedMassPerSolidCellGrams = 850f;
        minimumMineableElevationMeters = 1.5f;
        generateOnStart = true;
        useAsyncBuildPipeline = true;
        asyncGenerationFrameBudgetMilliseconds = 4f;
        asyncChunkBuildQueueSize = 2;
        materialDefinitions = BuildDefaultMaterialDefinitions();
    }

    [ContextMenu("Generate Voxel Terrain")]
    public void GenerateTerrainFromContextMenu()
    {
        GenerateTerrainWithConfiguredMode(clearExistingBeforeGenerate);
    }

    [ContextMenu("Clear Voxel Terrain")]
    public void ClearGeneratedTerrainFromContextMenu()
    {
        ClearGeneratedTerrain();
    }

    public bool GenerateTerrain(bool clearExisting)
    {
        return GenerateTerrainSynchronously(clearExisting, null);
    }

    public bool GenerateTerrainWithConfiguredMode(bool clearExisting, Action<bool> onComplete = null)
    {
        return ShouldUseAsyncTerrainGeneration()
            ? GenerateTerrainAsync(clearExisting, onComplete)
            : GenerateTerrainSynchronously(clearExisting, onComplete);
    }

    public bool GenerateTerrainAsync(bool clearExisting, Action<bool> onComplete = null)
    {
        if (IsTerrainGenerationInProgress)
        {
            if (onComplete != null)
            {
                terrainGenerationCompletionCallbacks.Add(onComplete);
            }

            return true;
        }

        CancelTerrainGenerationInternal(true);

        if (Application.isPlaying && !isActiveAndEnabled)
        {
            return GenerateTerrainSynchronously(clearExisting, onComplete);
        }

        TerrainGenerationOperation operation = new TerrainGenerationOperation(this, clearExisting);
        BeginTerrainGeneration(operation, onComplete);

        if (Application.isPlaying)
        {
            activeTerrainGenerationCoroutine = StartCoroutine(RunTerrainGenerationAsync(operation));
            return true;
        }

#if UNITY_EDITOR
        activeEditorTerrainGenerationDriver = new EditorTerrainGenerationDriver(this, operation);
        activeEditorTerrainGenerationDriver.Start();
        return true;
#else
        return GenerateTerrainSynchronously(clearExisting, onComplete);
#endif
    }

    public void CancelTerrainGeneration()
    {
        CancelTerrainGenerationInternal(true);
    }

    private bool GenerateTerrainSynchronously(bool clearExisting, Action<bool> onComplete)
    {
        CancelTerrainGenerationInternal(true);

        TerrainGenerationOperation operation = new TerrainGenerationOperation(this, clearExisting);
        BeginTerrainGeneration(operation, onComplete);

        try
        {
            while (activeTerrainGenerationOperation == operation && !operation.IsDone)
            {
                operation.Step();
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            FinishTerrainGeneration(operation, false);
            return false;
        }

        if (activeTerrainGenerationOperation == operation)
        {
            FinishTerrainGeneration(operation, operation.Success);
        }

        return operation.Success;
    }


    public bool ClearGeneratedTerrain()
    {
        CancelTerrainGenerationInternal(true);
        ClearGeneratedTerrainForRegeneration();

        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot != null)
        {
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

        return false;
    }

    private void ClearGeneratedTerrainForRegeneration()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot != null)
        {
            foreach (Transform child in generatedRoot)
            {
                ProceduralVoxelTerrainChunk chunk = child.GetComponent<ProceduralVoxelTerrainChunk>();
                if (chunk != null)
                {
                    chunk.ClearMesh();
                }
            }
        }

        generatedChunks.Clear();
        sharedChunkMaterials = Array.Empty<Material>();
        densitySamples = null;
        cellMaterialIndices = null;
        surfaceHeightPrepass = null;
        surfaceHeightPrepassReady = false;
        columnProfilePrepass = null;
        ResetRuntimeStreamingStateForGeneration();
    }

    public bool TrySampleSurface(float normalizedX, float normalizedZ, out RaycastHit hit)
    {
        Bounds bounds = WorldBounds;
        float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(normalizedX));
        float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, Mathf.Clamp01(normalizedZ));
        return TrySampleSurfaceWorld(worldX, worldZ, out hit);
    }

    public bool TrySampleSurfaceWorld(float worldX, float worldZ, out RaycastHit hit)
    {
        hit = default;
        if (!HasReadyGameplayTerrain)
        {
            return false;
        }

        Bounds bounds = WorldBounds;
        Ray ray = new Ray(
            new Vector3(worldX, bounds.max.y + Mathf.Max(4f, voxelSizeMeters * 4f), worldZ),
            Vector3.down);
        float maxDistance = bounds.size.y + Mathf.Max(8f, voxelSizeMeters * 8f);
        bool foundHit = false;
        float closestDistance = float.MaxValue;

        foreach (ProceduralVoxelTerrainChunk chunk in generatedChunks.Values)
        {
            if (chunk == null)
            {
                continue;
            }

            MeshCollider collider = chunk.GetComponent<MeshCollider>();
            if (collider == null || !collider.enabled || collider.sharedMesh == null)
            {
                continue;
            }

            if (collider.Raycast(ray, out RaycastHit candidateHit, maxDistance) && candidateHit.distance < closestDistance)
            {
                closestDistance = candidateHit.distance;
                hit = candidateHit;
                foundHit = true;
            }
        }

        return foundHit;
    }

    public bool TryGetCachedSurfaceHeight(float normalizedX, float normalizedZ, out float heightMeters)
    {
        Bounds bounds = WorldBounds;
        float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(normalizedX));
        float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, Mathf.Clamp01(normalizedZ));
        return TryGetCachedSurfaceHeightWorld(worldX, worldZ, out heightMeters);
    }

    public bool TryGetCachedSurfaceHeightWorld(float worldX, float worldZ, out float heightMeters)
    {
        heightMeters = 0f;
        if (!HasReadySurfaceHeightPrepass)
        {
            return false;
        }

        Vector3 localPoint = transform.InverseTransformPoint(new Vector3(worldX, transform.position.y, worldZ));
        Vector3 worldSize = TotalWorldSize;
        if (localPoint.x < 0f || localPoint.x > worldSize.x || localPoint.z < 0f || localPoint.z > worldSize.z)
        {
            return false;
        }

        float localHeight = SampleSurfaceHeightPrepass(localPoint.x, localPoint.z);
        heightMeters = transform.TransformPoint(new Vector3(localPoint.x, localHeight, localPoint.z)).y;
        return true;
    }

    public bool TryGetCachedSurfacePoint(float normalizedX, float normalizedZ, out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        Bounds bounds = WorldBounds;
        float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(normalizedX));
        float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, Mathf.Clamp01(normalizedZ));
        return TryGetCachedSurfacePointWorld(worldX, worldZ, out surfacePoint, out surfaceNormal);
    }

    public bool TryGetCachedSurfacePointWorld(float worldX, float worldZ, out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        surfacePoint = default;
        surfaceNormal = Vector3.up;
        if (!HasReadySurfaceHeightPrepass)
        {
            return false;
        }

        Vector3 localPoint = transform.InverseTransformPoint(new Vector3(worldX, transform.position.y, worldZ));
        Vector3 worldSize = TotalWorldSize;
        if (localPoint.x < 0f || localPoint.x > worldSize.x || localPoint.z < 0f || localPoint.z > worldSize.z)
        {
            return false;
        }

        float localHeight = SampleSurfaceHeightPrepass(localPoint.x, localPoint.z);
        Vector3 localSurfacePoint = new Vector3(localPoint.x, localHeight, localPoint.z);
        surfacePoint = transform.TransformPoint(localSurfacePoint);
        surfaceNormal = transform.TransformDirection(EvaluateSurfaceNormalPrepassLocal(localPoint.x, localPoint.z)).normalized;
        return true;
    }


    private int GetSurfacePrepassIndex(int sampleX, int sampleZ)
        => VoxelDataStore.GetSurfacePrepassIndex(sampleX, sampleZ, TotalSamplesX);


    public bool ApplyDensityBrushWorld(Vector3 worldPoint, float radiusMeters, float densityDeltaMeters, bool notifyGeometryChange = true)
    {
        return ModifyDensitySphere(worldPoint, radiusMeters, densityDeltaMeters, null, false, notifyGeometryChange);
    }

    public Bounds GetChunkWorldBounds(Vector3Int chunkCoordinate)
    {
        Vector3 chunkWorldSize = Vector3.one * ChunkWorldSizeMeters;
        Vector3 localMin = Vector3.Scale(chunkCoordinate, chunkWorldSize);
        Vector3 localCenter = localMin + (chunkWorldSize * 0.5f);
        return new Bounds(transform.TransformPoint(localCenter), chunkWorldSize);
    }

    public bool TryGetGeneratedChunk(Vector3Int chunkCoordinate, out ProceduralVoxelTerrainChunk chunk)
    {
        return generatedChunks.TryGetValue(chunkCoordinate, out chunk) && chunk != null;
    }

    public void GetChunkCoordinateRange(Bounds worldBounds, out Vector3Int minChunkCoordinate, out Vector3Int maxChunkCoordinate)
    {
        Vector3 localMin = transform.InverseTransformPoint(worldBounds.min);
        Vector3 localMax = transform.InverseTransformPoint(worldBounds.max);
        Vector3 min = Vector3.Min(localMin, localMax);
        Vector3 max = Vector3.Max(localMin, localMax);
        float chunkWorldSize = ChunkWorldSizeMeters;

        minChunkCoordinate = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(min.x / chunkWorldSize), 0, chunkCounts.x - 1),
            Mathf.Clamp(Mathf.FloorToInt(min.y / chunkWorldSize), 0, chunkCounts.y - 1),
            Mathf.Clamp(Mathf.FloorToInt(min.z / chunkWorldSize), 0, chunkCounts.z - 1));
        maxChunkCoordinate = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt(max.x / chunkWorldSize) - 1, 0, chunkCounts.x - 1),
            Mathf.Clamp(Mathf.CeilToInt(max.y / chunkWorldSize) - 1, 0, chunkCounts.y - 1),
                Mathf.Clamp(Mathf.CeilToInt(max.z / chunkWorldSize) - 1, 0, chunkCounts.z - 1));
    }

    public Vector3Int WorldPositionToChunkCoordinate(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        float chunkWorldSize = Mathf.Max(0.0001f, ChunkWorldSizeMeters);
        return new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(localPosition.x / chunkWorldSize), 0, Mathf.Max(0, chunkCounts.x - 1)),
            Mathf.Clamp(Mathf.FloorToInt(localPosition.y / chunkWorldSize), 0, Mathf.Max(0, chunkCounts.y - 1)),
            Mathf.Clamp(Mathf.FloorToInt(localPosition.z / chunkWorldSize), 0, Mathf.Max(0, chunkCounts.z - 1)));
    }

    public bool TryGetGameplayBounds(out Bounds bounds)
    {
        if (HasGeneratedTerrain)
        {
            bounds = WorldBounds;
            return true;
        }

        return TryGetRuntimeStreamingStartupBounds(out bounds);
    }

    public int FindMaterialIndex(string materialOrCompositionName)
    {
        if (string.IsNullOrWhiteSpace(materialOrCompositionName) || materialDefinitions == null)
        {
            return -1;
        }

        for (int i = 0; i < materialDefinitions.Count; i++)
        {
            VoxelTerrainMaterialDefinition definition = materialDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            if (string.Equals(definition.ResolveDisplayName(), materialOrCompositionName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(definition.compositionItemName, materialOrCompositionName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public bool ApplyMaterialBrushWorld(Vector3 worldPoint, float radiusMeters, float verticalHalfExtentMeters, int materialIndex, bool notifyGeometryChange = true)
    {
        if (cellMaterialIndices == null || materialDefinitions == null || materialIndex < 0 || materialIndex >= materialDefinitions.Count)
        {
            return false;
        }

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        Vector3 worldSize = TotalWorldSize;
        if (localPoint.x < 0f || localPoint.x > worldSize.x || localPoint.z < 0f || localPoint.z > worldSize.z)
        {
            return false;
        }

        radiusMeters = Mathf.Max(voxelSizeMeters * 0.5f, radiusMeters);
        verticalHalfExtentMeters = Mathf.Max(voxelSizeMeters * 0.5f, verticalHalfExtentMeters);

        Vector3Int minCell = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt((localPoint.x - radiusMeters) / voxelSizeMeters), 0, TotalCellsX - 1),
            Mathf.Clamp(Mathf.FloorToInt((localPoint.y - verticalHalfExtentMeters) / voxelSizeMeters), 0, TotalCellsY - 1),
            Mathf.Clamp(Mathf.FloorToInt((localPoint.z - radiusMeters) / voxelSizeMeters), 0, TotalCellsZ - 1));
        Vector3Int maxCell = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt((localPoint.x + radiusMeters) / voxelSizeMeters), 0, TotalCellsX - 1),
            Mathf.Clamp(Mathf.CeilToInt((localPoint.y + verticalHalfExtentMeters) / voxelSizeMeters), 0, TotalCellsY - 1),
            Mathf.Clamp(Mathf.CeilToInt((localPoint.z + radiusMeters) / voxelSizeMeters), 0, TotalCellsZ - 1));

        float radiusSquared = radiusMeters * radiusMeters;
        bool modified = false;
        HashSet<Vector3Int> affectedChunkCoordinates = new HashSet<Vector3Int>();
        for (int z = minCell.z; z <= maxCell.z; z++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    Vector3 cellCenter = new Vector3(
                        (x + 0.5f) * voxelSizeMeters,
                        (y + 0.5f) * voxelSizeMeters,
                        (z + 0.5f) * voxelSizeMeters);
                    if (Mathf.Abs(cellCenter.y - localPoint.y) > verticalHalfExtentMeters)
                    {
                        continue;
                    }

                    Vector3 planarDelta = Vector3.ProjectOnPlane(cellCenter - localPoint, Vector3.up);
                    if (planarDelta.sqrMagnitude > radiusSquared)
                    {
                        continue;
                    }

                    int cellIndex = GetCellIndex(x, y, z);
                    if (cellMaterialIndices[cellIndex] == materialIndex)
                    {
                        continue;
                    }

                    cellMaterialIndices[cellIndex] = (byte)materialIndex;
                    affectedChunkCoordinates.Add(GetChunkCoordinateForCell(x, y, z));
                    modified = true;
                }
            }
        }

        if (!modified)
        {
            return false;
        }

        if (IsBulkEditActive)
        {
            QueueBulkEditedChunks(affectedChunkCoordinates);
        }
        else
        {
            foreach (Vector3Int chunkCoordinate in affectedChunkCoordinates)
            {
                RebuildChunk(chunkCoordinate);
            }
        }

        if (notifyGeometryChange)
        {
            if (IsBulkEditActive)
            {
                QueueBulkGeometryChange(
                    BuildChangedWorldBounds(localPoint, radiusMeters, verticalHalfExtentMeters),
                    affectedChunkCoordinates);
            }
            else
            {
                NotifyGeometryChanged(localPoint, radiusMeters, affectedChunkCoordinates);
            }
        }

        return true;
    }

    public TerrainRegionSnapshot CaptureRegionSnapshot(Bounds worldBounds)
    {
        if (densitySamples == null || cellMaterialIndices == null)
        {
            return null;
        }

        GetRegionSnapshotBounds(worldBounds, out Vector3Int minSample, out Vector3Int maxSample, out Vector3Int minCell, out Vector3Int maxCell);

        int sampleCountX = (maxSample.x - minSample.x) + 1;
        int sampleCountY = (maxSample.y - minSample.y) + 1;
        int sampleCountZ = (maxSample.z - minSample.z) + 1;
        int materialCountX = (maxCell.x - minCell.x) + 1;
        int materialCountY = (maxCell.y - minCell.y) + 1;
        int materialCountZ = (maxCell.z - minCell.z) + 1;

        float[] densityValues = new float[sampleCountX * sampleCountY * sampleCountZ];
        int densityOffset = 0;
        for (int z = minSample.z; z <= maxSample.z; z++)
        {
            for (int y = minSample.y; y <= maxSample.y; y++)
            {
                for (int x = minSample.x; x <= maxSample.x; x++)
                {
                    densityValues[densityOffset++] = densitySamples[GetSampleIndex(x, y, z)];
                }
            }
        }

        byte[] materialValues = new byte[materialCountX * materialCountY * materialCountZ];
        int materialOffset = 0;
        HashSet<Vector3Int> affectedChunkCoordinates = new HashSet<Vector3Int>();
        for (int z = minCell.z; z <= maxCell.z; z++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    materialValues[materialOffset++] = cellMaterialIndices[GetCellIndex(x, y, z)];
                    affectedChunkCoordinates.Add(GetChunkCoordinateForCell(x, y, z));
                }
            }
        }

        return new TerrainRegionSnapshot(
            minSample,
            maxSample,
            minCell,
            maxCell,
            densityValues,
            materialValues,
            BuildRegionChangedWorldBounds(minCell, maxCell),
            new List<Vector3Int>(affectedChunkCoordinates));
    }

    public bool RestoreRegionSnapshot(TerrainRegionSnapshot snapshot, bool notifyGeometryChange = true)
    {
        if (snapshot == null || densitySamples == null || cellMaterialIndices == null)
        {
            return false;
        }

        int expectedDensityCount =
            ((snapshot.MaxSample.x - snapshot.MinSample.x) + 1) *
            ((snapshot.MaxSample.y - snapshot.MinSample.y) + 1) *
            ((snapshot.MaxSample.z - snapshot.MinSample.z) + 1);
        int expectedMaterialCount =
            ((snapshot.MaxCell.x - snapshot.MinCell.x) + 1) *
            ((snapshot.MaxCell.y - snapshot.MinCell.y) + 1) *
            ((snapshot.MaxCell.z - snapshot.MinCell.z) + 1);
        if (snapshot.DensityValues.Length != expectedDensityCount ||
            snapshot.MaterialValues.Length != expectedMaterialCount)
        {
            return false;
        }

        int densityOffset = 0;
        for (int z = snapshot.MinSample.z; z <= snapshot.MaxSample.z; z++)
        {
            for (int y = snapshot.MinSample.y; y <= snapshot.MaxSample.y; y++)
            {
                for (int x = snapshot.MinSample.x; x <= snapshot.MaxSample.x; x++)
                {
                    densitySamples[GetSampleIndex(x, y, z)] = snapshot.DensityValues[densityOffset++];
                }
            }
        }

        int materialOffset = 0;
        for (int z = snapshot.MinCell.z; z <= snapshot.MaxCell.z; z++)
        {
            for (int y = snapshot.MinCell.y; y <= snapshot.MaxCell.y; y++)
            {
                for (int x = snapshot.MinCell.x; x <= snapshot.MaxCell.x; x++)
                {
                    cellMaterialIndices[GetCellIndex(x, y, z)] = snapshot.MaterialValues[materialOffset++];
                }
            }
        }

        HashSet<Vector3Int> affectedChunkCoordinates = snapshot.AffectedChunkCoordinates != null
            ? new HashSet<Vector3Int>(snapshot.AffectedChunkCoordinates)
            : new HashSet<Vector3Int>();
        if (affectedChunkCoordinates.Count == 0)
        {
            return false;
        }

        if (IsBulkEditActive)
        {
            QueueBulkEditedChunks(affectedChunkCoordinates);
        }
        else
        {
            foreach (Vector3Int chunkCoordinate in affectedChunkCoordinates)
            {
                RebuildChunk(chunkCoordinate);
            }
        }

        if (!notifyGeometryChange)
        {
            return true;
        }

        if (IsBulkEditActive)
        {
            QueueBulkGeometryChange(snapshot.ChangedWorldBounds, affectedChunkCoordinates);
        }
        else
        {
            GeometryChanged?.Invoke(new VoxelTerrainGeometryChangedEventArgs(
                snapshot.ChangedWorldBounds,
                new List<Vector3Int>(affectedChunkCoordinates)));
        }

        return true;
    }

    private void NotifyGeometryChanged(Vector3 localPoint, float radiusMeters, HashSet<Vector3Int> affectedChunkCoordinates)
    {
        if (affectedChunkCoordinates == null || affectedChunkCoordinates.Count == 0)
        {
            LogExcavationDebug("NotifyGeometryChanged skipped because no chunks were affected.");
            return;
        }

        Vector3 worldCenter = transform.TransformPoint(localPoint);
        Bounds changedBounds = BuildChangedWorldBounds(localPoint, radiusMeters, radiusMeters);

        LogExcavationDebug(
            $"NotifyGeometryChanged at world {worldCenter} radius {radiusMeters:F2}. Bounds center={changedBounds.center}, size={changedBounds.size}, affected chunks={affectedChunkCoordinates.Count}.");

        GeometryChanged?.Invoke(new VoxelTerrainGeometryChangedEventArgs(
            changedBounds,
            new List<Vector3Int>(affectedChunkCoordinates)));
    }

    public bool TryGetHarvestable(RaycastHit hit, out IHarvestable harvestable)
    {
        harvestable = null;
        if (densitySamples == null || cellMaterialIndices == null || hit.collider == null)
        {
            return false;
        }

        ProceduralVoxelTerrainChunk chunk = hit.collider.GetComponentInParent<ProceduralVoxelTerrainChunk>();
        if (chunk == null)
        {
            return false;
        }

        Vector3 localPoint = transform.InverseTransformPoint(hit.point);
        if (!IsWithinWorld(localPoint))
        {
            return false;
        }

        int materialIndex = GetMaterialIndexAtLocalPoint(localPoint);
        harvestable = new HarvestTarget(this, hit.point, materialIndex);
        return true;
    }


    private void OnDrawGizmosSelected()
    {
        if (!drawWorldBoundsGizmos && !drawChunkBoundsGizmos)
        {
            return;
        }

        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 worldSize = TotalWorldSize;
        if (drawWorldBoundsGizmos)
        {
            Gizmos.color = boundsGizmoColor;
            Gizmos.DrawWireCube(worldSize * 0.5f, worldSize);
        }

        if (drawChunkBoundsGizmos)
        {
            Gizmos.color = new Color(boundsGizmoColor.r, boundsGizmoColor.g, boundsGizmoColor.b, 0.4f);
            Vector3 chunkWorldSize = Vector3.one * (cellsPerChunkAxis * voxelSizeMeters);
            for (int z = 0; z < chunkCounts.z; z++)
            {
                for (int y = 0; y < chunkCounts.y; y++)
                {
                    for (int x = 0; x < chunkCounts.x; x++)
                    {
                        Vector3 center = Vector3.Scale(new Vector3(x, y, z), chunkWorldSize) + (chunkWorldSize * 0.5f);
                        Gizmos.DrawWireCube(center, chunkWorldSize);
                    }
                }
            }
        }
    }
}
