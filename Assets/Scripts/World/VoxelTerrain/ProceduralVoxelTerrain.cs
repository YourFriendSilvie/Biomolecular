using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Threading;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Facade MonoBehaviour for the procedural voxel terrain system.
/// Manages the full lifecycle of a chunked, density-field terrain: generation (sync or async),
/// runtime editing (excavation, material painting, basin carving), surface-height queries,
/// region snapshots, and integration with the harvesting and water systems.
/// <para>
/// This is a partial class — each concern lives in its own file under
/// <c>Assets/Scripts/World/VoxelTerrain/</c>. Always program to this facade; never call
/// internal sub-systems directly from outside code.
/// </para>
/// </summary>
public partial class ProceduralVoxelTerrain : MonoBehaviour, IRaycastHarvestableProvider
{
    private const string GeneratedRootName = "Generated Voxel Terrain";
    // Main thread id recorded at Awake/Start so background jobs can detect main-thread context
    public static int MainThreadId;
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
    [SerializeField, Min(0f)] private float surfaceAmplitudeMeters = 28f;
    [SerializeField, Min(1f)] private float ridgeNoiseScaleMeters = 46f;
    [SerializeField, Min(0f)] private float ridgeAmplitudeMeters = 18f;
    [SerializeField, Min(1f)] private float detailNoiseScaleMeters = 24f;
    [SerializeField, Min(0f)] private float detailAmplitudeMeters = 1.1f;
    [SerializeField, Min(1f)] private float caveNoiseScaleMeters = 18f;
    [SerializeField, Range(0f, 1f)] private float caveNoiseThreshold = 0.74f;
    [SerializeField, Min(0f)] private float caveCarveStrengthMeters = 12f;
    [SerializeField, Min(0f)] private float caveStartDepthMeters = 7f;

    [Header("Domain Warp")]
    [SerializeField, Min(1f), Tooltip("World-space wavelength (m) of the domain-warp noise. Larger = smoother warped ridges.")]
    private float domainWarpScaleMeters = 120f;
    [SerializeField, Min(0f), Tooltip("Max XZ displacement (m) applied by domain warp. 0 = disabled.")]
    private float domainWarpStrengthMeters = 25f;

    [Header("Island Profile")]
    [SerializeField] private bool shapeAsIsland = true;
    [SerializeField, Range(0.2f, 0.8f)] private float islandCoreRadiusNormalized = 0.5f;
    [SerializeField, Range(0.05f, 0.4f)] private float coastalShelfWidthNormalized = 0.16f;
    [SerializeField, Range(0f, 0.6f), Tooltip("How much Perlin noise displaces the island's radial distance for irregular San-Juan-style coastlines. 0 = perfect circle.")]
    private float islandShapeNoiseStrength = 0.3f;
    [SerializeField, Range(0f, 0.05f), Tooltip("World-space frequency of the coastline noise (1/meters). ~0.006 gives bumps every ~167m.")]
    private float islandShapeNoiseScale = 0.006f;
    [SerializeField, Min(0f)] private float beachHeightMeters = 0.95f;
    [SerializeField, Min(0.25f)] private float oceanFloorDepthMeters = 3.4f;
    [SerializeField, Min(0f)] private float oceanFloorVariationMeters = 0.8f;
    [SerializeField, Min(1f)] private float oceanFloorNoiseScaleMeters = 42f;

    [Header("Material Transitions")]
    [SerializeField, Range(0.5f, 8f), Tooltip("World-space XZ scale (meters) of the noise used to jitter material boundaries.")]
    private float materialBoundaryNoiseScale = 2f;
    [SerializeField, Range(0f, 1f), Tooltip("How much (meters) subsurface horizon boundaries are displaced by noise along a column. 0 = flat. Using XZ-only noise so horizons never bleed through each other.")]
    private float materialBoundaryNoiseAmplitude = 0.25f;


    [Header("Debug")]
    [Tooltip("Debug shader view: 0=off, 1=albedo, 2=normals, 3=NdotL")]
    [SerializeField] private float debugViewMode = 0f;
    [Tooltip("When enabled, shader samples the CPU per-column debug texture instead of vertex.color (for debugging)")]
    [SerializeField] private bool debugCellMaterial = false;

    private float materialBeachBoundaryNoiseAmplitude = 0.08f;
    [SerializeField, Range(4f, 40f), Tooltip("World-space XZ scale (meters) of the beach boundary noise. Larger values create organic patches; small values create speckle.")]
    private float materialBeachBoundaryNoiseScale = 15f;

    [Header("Lake Pre-baking")]
    [SerializeField, Min(8f), Tooltip("Grid spacing (m) for scanning candidate basin positions. Larger = fewer lakes.")]
    private float lakeScanGridSpacingMeters = 32f;
    [SerializeField, Min(0f), Tooltip("Minimum elevation (m) above sea level for a valid lake candidate.")]
    private float lakeScanMinElevationAboveSeaLevel = 2f;
    [SerializeField, Min(0f), Tooltip("Minimum world-space distance (m) between generated lake basins.")]
    private float lakeScanMinSpacingMeters = 80f;
    [SerializeField, Min(1f), Tooltip("Default carved basin radius (m).")]
    private float lakeScanMaxRadiusMeters = 18f;
    [SerializeField, Min(0.1f), Tooltip("Default carved basin depth (m).")]
    private float lakeScanDefaultDepthMeters = 2.5f;

    [Header("Mountains")]
    [SerializeField, Min(10f), Tooltip("XZ wavelength (m) of ridged-multifractal mountain noise. ~40m gives Olympic-scale ridge spacing.")]
    private float mountainRidgeScaleMeters = 40f;
    [SerializeField, Min(0f), Tooltip("Maximum height (m) added by mountain ridges. 0 = no mountains.")]
    private float mountainRidgeAmplitudeMeters = 15f;
    [SerializeField, Tooltip("Terrain height (m) below which no mountain ridges appear. Keeps valley floors flat.")]
    private float mountainBaseHeightMeters = 15f;
    [SerializeField, Min(1f), Tooltip("Vertical blend range (m): mountain ridge fades in from mountainBaseHeightMeters over this distance.")]
    private float mountainBlendRangeMeters = 8f;

    [Header("Meshing")]
    [SerializeField, Min(1), Tooltip("Cell stride used when sampling the density field for smooth analytical normals. Higher = smoother shading at the cost of less micro-detail. 4 is recommended.")]
    private int normalSampleCells = 4;

    [Header("LOD")]
    [SerializeField, Min(1), Tooltip("Maximum number of LOD levels (0 = highest detail)")]
    private int maxLodLevels = 3;
    [SerializeField, Min(0.5f), Tooltip("Distance multiplier in multiples of chunk size used to step LOD levels (higher = fewer LOD steps)")]
    private float lodDistanceFactor = 2.0f; // LOD changes every chunkWorldSize * lodDistanceFactor meters.
    [SerializeField, Range(0f, 1f), Tooltip("LOD hysteresis fraction to prevent frequent LOD toggling (0=no hysteresis, 1=max)")]
    private float lodHysteresisFraction = 0.25f;
    [Header("LOD Runtime Refresh")]
    [SerializeField, Min(0.05f), Tooltip("Seconds between LOD refresh checks while runtime streaming is active.")]
    private float lodRefreshIntervalSeconds = 0.25f;
    [SerializeField, Min(0), Tooltip("Horizontal chunk radius around the anchor to check for LOD changes during each refresh.")]
    private int lodRefreshChunkRadius = 6;
    [SerializeField, Min(1), Tooltip("Maximum number of chunk rebuilds allowed per refresh to avoid frame spikes.")]
    private int maxLodRebuildsPerRefresh = 2;
    [NonSerialized] private float lastLodRefreshTime = 0f;


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
    [NonSerialized] internal GenerationLakeBasin[] generationLakeBasins;
    [NonSerialized] private TerrainGenerationOperation activeTerrainGenerationOperation;
    private readonly ChunkStreamingManager streamingManager = new ChunkStreamingManager();
    private readonly Dictionary<Vector3Int, ProceduralVoxelTerrainChunk> generatedChunks = new Dictionary<Vector3Int, ProceduralVoxelTerrainChunk>();
    private readonly HashSet<Vector3Int> bulkEditedChunkCoordinates = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> bulkGeometryChunkCoordinates = new HashSet<Vector3Int>();
    private readonly List<Action<bool>> terrainGenerationCompletionCallbacks = new List<Action<bool>>();
    internal Material sharedTerrainMaterial;
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

    /// <summary>
    /// Opens a bulk-edit transaction. While a bulk edit is active, chunk rebuilds and
    /// <see cref="GeometryChanged"/> events are deferred and batched until <see cref="EndBulkEdit"/>
    /// is called. Nest calls are reference-counted; the batch flushes when the depth returns to zero.
    /// </summary>
    /// <param name="suppressExcavationLogs">When <c>true</c>, suppresses per-voxel excavation log spam during the edit.</param>
    public void BeginBulkEdit(bool suppressExcavationLogs = true)
    {
        bulkEditDepth++;
        if (suppressExcavationLogs)
        {
            suppressExcavationDebugDuringBulkEdit = true;
        }
    }

    /// <summary>
    /// Closes a bulk-edit transaction opened by <see cref="BeginBulkEdit"/>. When the nesting
    /// depth reaches zero, flushes all deferred chunk rebuilds and fires the batched
    /// <see cref="GeometryChanged"/> event covering the full union of affected bounds.
    /// </summary>
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

    private void Awake()
    {
        // record main thread id so background workers can defer Unity API calls safely
        MainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    private void Start()
    {
        // ensure MainThreadId is set even if Awake wasn't called for some reason
        MainThreadId = Thread.CurrentThread.ManagedThreadId;

        // Apply any debug shader flags serialized in the inspector so the scene reflects
        // the chosen debug mode without needing Console commands.
        if (sharedTerrainMaterial != null)
        {
            sharedTerrainMaterial.SetFloat("_DebugViewMode", debugViewMode);
            sharedTerrainMaterial.SetFloat("_DebugCellMaterial", debugCellMaterial ? 1f : 0f);
        }
        Shader.SetGlobalFloat("_DebugViewMode", debugViewMode);
        Shader.SetGlobalFloat("_DebugCellMaterial", debugCellMaterial ? 1f : 0f);

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
        islandShapeNoiseStrength = Mathf.Clamp01(islandShapeNoiseStrength);
        islandShapeNoiseScale = Mathf.Max(0f, islandShapeNoiseScale);
        beachHeightMeters = Mathf.Max(0f, beachHeightMeters);
        oceanFloorDepthMeters = Mathf.Max(0.25f, oceanFloorDepthMeters);
        oceanFloorVariationMeters = Mathf.Max(0f, oceanFloorVariationMeters);
        oceanFloorNoiseScaleMeters = Mathf.Max(1f, oceanFloorNoiseScaleMeters);
        materialBoundaryNoiseScale = Mathf.Max(0.1f, materialBoundaryNoiseScale);
        materialBoundaryNoiseAmplitude = Mathf.Max(0f, materialBoundaryNoiseAmplitude);
        materialBeachBoundaryNoiseAmplitude = Mathf.Max(0f, materialBeachBoundaryNoiseAmplitude);
        materialBeachBoundaryNoiseScale = Mathf.Max(0.1f, materialBeachBoundaryNoiseScale);
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

    /// <summary>
    /// Applies the Olympic Rainforest preset, configuring a 768 × 128 × 768 m world
    /// with temperate rainforest terrain, Olympic-style mountain ridges, and coastal beaches.
    /// Also rebuilds the default material definitions. Safe to call from the Inspector context menu.
    /// </summary>
    [ContextMenu("Apply Olympic Rainforest Voxel Preset")]
    public void ApplyOlympicRainforestPreset()
    {
        // 48×8×48 chunks × 16 cells × 1m voxels = 768m × 128m × 768m world.
        // Mountains peak at ~110m; at human scale (1.8m player) a 60m cliff is enormous.
        chunkCounts = new Vector3Int(48, 8, 48);
        cellsPerChunkAxis = 16;
        voxelSizeMeters = 1f;

        // Height / shape
        baseSurfaceHeightMeters = 35f;
        seaLevelMeters = 11f;

        // Large-scale fractal base
        surfaceNoiseScaleMeters = 180f;
        surfaceNoiseOctaves = 5;
        surfaceNoisePersistence = 0.55f;
        surfaceNoiseLacunarity = 2f;
        surfaceAmplitudeMeters = 18f;

        // Ridge noise
        ridgeNoiseScaleMeters = 110f;
        ridgeAmplitudeMeters = 12f;

        // Fine surface detail
        detailNoiseScaleMeters = 28f;
        detailAmplitudeMeters = 2.5f;

        // Caves
        caveNoiseScaleMeters = 18f;
        caveNoiseThreshold = 0.74f;
        caveCarveStrengthMeters = 12f;
        caveStartDepthMeters = 7f;

        // Island shaping
        shapeAsIsland = true;
        islandCoreRadiusNormalized = 0.62f;
        coastalShelfWidthNormalized = 0.05f;
        islandShapeNoiseStrength = 0.25f;
        islandShapeNoiseScale = 0.003f;
        beachHeightMeters = 1.2f;
        oceanFloorDepthMeters = 5f;
        oceanFloorVariationMeters = 1.5f;
        oceanFloorNoiseScaleMeters = 80f;

        // Material transitions
        materialBoundaryNoiseScale = 3f;
        materialBoundaryNoiseAmplitude = 0.35f;
        materialBeachBoundaryNoiseAmplitude = 0.1f;
        materialBeachBoundaryNoiseScale = 25f;

        // Lake scanning
        lakeScanGridSpacingMeters = 60f;
        lakeScanMinElevationAboveSeaLevel = 4f;
        lakeScanMinSpacingMeters = 150f;
        lakeScanMaxRadiusMeters = 24f;
        lakeScanDefaultDepthMeters = 3.5f;

        // Domain warp
        domainWarpScaleMeters = 200f;
        domainWarpStrengthMeters = 35f;

        // Olympic Mountain ridges — only activates on already-elevated terrain
        mountainRidgeScaleMeters = 130f;
        mountainRidgeAmplitudeMeters = 38f;
        mountainBaseHeightMeters = 60f;
        mountainBlendRangeMeters = 20f;

        // Mining
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

    /// <summary>
    /// Generates the terrain synchronously, blocking until all chunks are built.
    /// Prefer <see cref="GenerateTerrainWithConfiguredMode"/> at runtime to avoid frame stalls.
    /// </summary>
    /// <param name="clearExisting">When <c>true</c>, destroys any previously generated terrain first.</param>
    /// <returns><c>true</c> if generation completed successfully.</returns>
    public bool GenerateTerrain(bool clearExisting)
    {
        return GenerateTerrainSynchronously(clearExisting, null);
    }

    /// <summary>
    /// Generates terrain using whichever pipeline is configured in the Inspector
    /// (<c>useAsyncBuildPipeline</c>). In play mode this starts an async coroutine;
    /// in the Editor it uses <c>EditorApplication.update</c> to stay responsive.
    /// If generation is already in progress, <paramref name="onComplete"/> is queued.
    /// </summary>
    /// <param name="clearExisting">When <c>true</c>, destroys any previously generated terrain first.</param>
    /// <param name="onComplete">Optional callback invoked with <c>true</c> on success or <c>false</c> on failure.</param>
    /// <returns><c>true</c> if generation was started or was already in progress.</returns>
    public bool GenerateTerrainWithConfiguredMode(bool clearExisting, Action<bool> onComplete = null)
    {
        return ShouldUseAsyncTerrainGeneration()
            ? GenerateTerrainAsync(clearExisting, onComplete)
            : GenerateTerrainSynchronously(clearExisting, onComplete);
    }

    /// <summary>
    /// Starts terrain generation as a time-sliced async coroutine, spreading work across
    /// frames using the configured <c>asyncGenerationFrameBudgetMilliseconds</c>.
    /// If generation is already running, <paramref name="onComplete"/> is queued for when it finishes.
    /// </summary>
    /// <param name="clearExisting">When <c>true</c>, destroys any previously generated terrain first.</param>
    /// <param name="onComplete">Optional callback invoked with <c>true</c> on success or <c>false</c> on failure.</param>
    /// <returns><c>true</c> if the coroutine was started or generation was already in progress.</returns>
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

    /// <summary>
    /// Cancels any in-progress terrain generation (async coroutine or Editor driver) and
    /// disposes the active <see cref="TerrainGenerationOperation"/>. Already-built chunk meshes
    /// remain visible; only the incomplete operation is aborted.
    /// </summary>
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


    /// <summary>
    /// Cancels any in-progress generation and destroys all generated chunk GameObjects.
    /// Resets density samples, material indices, and surface-height prepass data.
    /// </summary>
    /// <returns><c>true</c> if there was generated terrain to clear; <c>false</c> if nothing existed.</returns>
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
        sharedTerrainMaterial = null;
        densitySamples = null;
        cellMaterialIndices = null;
        surfaceHeightPrepass = null;
        surfaceHeightPrepassReady = false;
        columnProfilePrepass = null;
        ResetRuntimeStreamingStateForGeneration();
    }

    /// <summary>
    /// Casts a downward ray at normalized XZ coordinates within the world bounds to find the
    /// terrain surface. Uses Unity physics so water colliders are excluded via layer masking.
    /// </summary>
    /// <param name="normalizedX">Horizontal position in [0, 1] across the world width.</param>
    /// <param name="normalizedZ">Depth position in [0, 1] across the world depth.</param>
    /// <param name="hit">The resulting <see cref="RaycastHit"/> if terrain was found.</param>
    /// <returns><c>true</c> if the ray hit terrain.</returns>
    public bool TrySampleSurface(float normalizedX, float normalizedZ, out RaycastHit hit)
    {
        Bounds bounds = WorldBounds;
        float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(normalizedX));
        float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, Mathf.Clamp01(normalizedZ));
        return TrySampleSurfaceWorld(worldX, worldZ, out hit);
    }

    /// <summary>
    /// Casts a downward ray at the given world-space XZ position to find the terrain surface.
    /// Requires <see cref="HasReadyGameplayTerrain"/> to be <c>true</c> and terrain colliders to be active.
    /// </summary>
    /// <param name="worldX">World-space X coordinate.</param>
    /// <param name="worldZ">World-space Z coordinate.</param>
    /// <param name="hit">The resulting <see cref="RaycastHit"/> if terrain was found.</param>
    /// <returns><c>true</c> if the ray hit terrain.</returns>
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
        int terrainLayerMask = 1 << gameObject.layer;
        return Physics.Raycast(ray, out hit, maxDistance, terrainLayerMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// Reads the surface height at normalized XZ coordinates from the pre-baked
    /// <c>surfaceHeightPrepass</c> array (no raycasting). Faster than <see cref="TrySampleSurface"/>
    /// for non-physics queries such as scatterer placement.
    /// </summary>
    /// <param name="normalizedX">Horizontal position in [0, 1] across the world width.</param>
    /// <param name="normalizedZ">Depth position in [0, 1] across the world depth.</param>
    /// <param name="heightMeters">World-space Y height of the terrain surface in metres.</param>
    /// <returns><c>true</c> if the prepass is ready and the XZ position is within bounds.</returns>
    public bool TryGetCachedSurfaceHeight(float normalizedX, float normalizedZ, out float heightMeters)
    {
        Bounds bounds = WorldBounds;
        float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(normalizedX));
        float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, Mathf.Clamp01(normalizedZ));
        return TryGetCachedSurfaceHeightWorld(worldX, worldZ, out heightMeters);
    }

    /// <summary>
    /// Reads the surface height at a world-space XZ position from the pre-baked prepass array.
    /// </summary>
    /// <param name="worldX">World-space X coordinate.</param>
    /// <param name="worldZ">World-space Z coordinate.</param>
    /// <param name="heightMeters">World-space Y height of the terrain surface in metres.</param>
    /// <returns><c>true</c> if the prepass is ready and the position is within world bounds.</returns>
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

    /// <summary>
    /// Returns both the world-space surface position and the terrain normal at normalized XZ coordinates,
    /// sampled from the pre-baked prepass (no physics). The normal is derived from the height-map gradient.
    /// </summary>
    /// <param name="normalizedX">Horizontal position in [0, 1].</param>
    /// <param name="normalizedZ">Depth position in [0, 1].</param>
    /// <param name="surfacePoint">World-space position on the terrain surface.</param>
    /// <param name="surfaceNormal">Approximate world-space surface normal.</param>
    /// <returns><c>true</c> if the prepass is ready and the position is within bounds.</returns>
    public bool TryGetCachedSurfacePoint(float normalizedX, float normalizedZ, out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        Bounds bounds = WorldBounds;
        float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(normalizedX));
        float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, Mathf.Clamp01(normalizedZ));
        return TryGetCachedSurfacePointWorld(worldX, worldZ, out surfacePoint, out surfaceNormal);
    }

    /// <summary>
    /// Returns the world-space surface position and normal at a world-space XZ position
    /// using the pre-baked surface-height prepass.
    /// </summary>
    /// <param name="worldX">World-space X coordinate.</param>
    /// <param name="worldZ">World-space Z coordinate.</param>
    /// <param name="surfacePoint">World-space position on the terrain surface.</param>
    /// <param name="surfaceNormal">Approximate world-space surface normal derived from the height gradient.</param>
    /// <returns><c>true</c> if the prepass is ready and the position is within world bounds.</returns>
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


    /// <summary>
    /// Adds a signed density delta to all samples within a sphere, effectively carving or
    /// filling terrain. Positive delta fills (adds solid); negative delta carves (removes solid).
    /// Rebuilds affected chunks and optionally fires <see cref="GeometryChanged"/>.
    /// </summary>
    /// <param name="worldPoint">Centre of the density brush sphere in world space.</param>
    /// <param name="radiusMeters">Radius of the sphere in metres.</param>
    /// <param name="densityDeltaMeters">Signed density change; negative values carve, positive values fill.</param>
    /// <param name="notifyGeometryChange">When <c>true</c>, fires <see cref="GeometryChanged"/> after rebuilding.</param>
    /// <returns><c>true</c> if any density values were modified.</returns>
    public bool ApplyDensityBrushWorld(Vector3 worldPoint, float radiusMeters, float densityDeltaMeters, bool notifyGeometryChange = true)
    {
        return ModifyDensitySphere(worldPoint, radiusMeters, densityDeltaMeters, null, false, notifyGeometryChange);
    }

    /// <summary>
    /// Returns the world-space axis-aligned bounds of a single chunk identified by its 3-D grid coordinate.
    /// </summary>
    /// <param name="chunkCoordinate">The chunk's integer grid coordinate (X, Y, Z indices).</param>
    public Bounds GetChunkWorldBounds(Vector3Int chunkCoordinate)
    {
        Vector3 chunkWorldSize = Vector3.one * ChunkWorldSizeMeters;
        return VoxelTerrainSpatialUtilities.GetChunkWorldBounds(chunkCoordinate, chunkWorldSize, transform);
    }

    /// <summary>
    /// Looks up a generated chunk by its grid coordinate.
    /// </summary>
    /// <param name="chunkCoordinate">The chunk's integer grid coordinate.</param>
    /// <param name="chunk">The <see cref="ProceduralVoxelTerrainChunk"/> component if found and non-null.</param>
    /// <returns><c>true</c> if the chunk exists and is non-null.</returns>
    public bool TryGetGeneratedChunk(Vector3Int chunkCoordinate, out ProceduralVoxelTerrainChunk chunk)
    {
        return generatedChunks.TryGetValue(chunkCoordinate, out chunk) && chunk != null;
    }

    private void Update()
    {
        if (!Application.isPlaying || !IsRuntimeStreamingModeActive) return;
        if (Time.unscaledTime - lastLodRefreshTime < lodRefreshIntervalSeconds) return;
        lastLodRefreshTime = Time.unscaledTime;

        Transform anchorTransform = ResolveRuntimeStreamingAnchorTransform();
        if (anchorTransform == null) return;
        Vector3Int anchorChunk = WorldPositionToChunkCoordinate(anchorTransform.position);

        int minX = Mathf.Max(0, anchorChunk.x - lodRefreshChunkRadius);
        int maxX = Mathf.Min(chunkCounts.x - 1, anchorChunk.x + lodRefreshChunkRadius);
        int minZ = Mathf.Max(0, anchorChunk.z - lodRefreshChunkRadius);
        int maxZ = Mathf.Min(chunkCounts.z - 1, anchorChunk.z + lodRefreshChunkRadius);

        int rebuilds = 0;
        for (int x = minX; x <= maxX && rebuilds < maxLodRebuildsPerRefresh; x++)
        {
            for (int z = minZ; z <= maxZ && rebuilds < maxLodRebuildsPerRefresh; z++)
            {
                for (int y = 0; y < chunkCounts.y && rebuilds < maxLodRebuildsPerRefresh; y++)
                {
                    Vector3Int coord = new Vector3Int(x, y, z);
                    if (!generatedChunks.TryGetValue(coord, out ProceduralVoxelTerrainChunk chunk) || chunk == null)
                        continue;
                    int newLod = GetChunkLod(coord);
                    if (chunk.currentLod != newLod)
                    {
                        RebuildChunk(coord);
                        rebuilds++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Computes the inclusive range of chunk grid coordinates that overlap the given world-space bounds.
    /// Useful for iterating all chunks touched by a water body, explosion, or other area effect.
    /// </summary>
    /// <param name="worldBounds">The world-space AABB to test against.</param>
    /// <param name="minChunkCoordinate">The smallest chunk coordinate that overlaps the bounds.</param>
    /// <param name="maxChunkCoordinate">The largest chunk coordinate that overlaps the bounds.</param>
    public void GetChunkCoordinateRange(Bounds worldBounds, out Vector3Int minChunkCoordinate, out Vector3Int maxChunkCoordinate)
    {
        Vector3 chunkWorldSize = Vector3.one * ChunkWorldSizeMeters;
        VoxelTerrainSpatialUtilities.GetChunkCoordinateRange(worldBounds, transform, chunkWorldSize, chunkCounts, out minChunkCoordinate, out maxChunkCoordinate);
    }

    /// <summary>
    /// Converts a world-space position to the grid coordinate of the chunk that contains it.
    /// The result is clamped to valid chunk bounds.
    /// </summary>
    /// <param name="worldPosition">Any world-space position.</param>
    /// <returns>The chunk grid coordinate containing <paramref name="worldPosition"/>.</returns>
    public Vector3Int WorldPositionToChunkCoordinate(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector3 chunkWorldSize = Vector3.one * ChunkWorldSizeMeters;
        return VoxelTerrainSpatialUtilities.WorldPositionToChunkCoordinate(localPosition, chunkWorldSize, chunkCounts);
    }

    /// <summary>
    /// Returns the playable world bounds: the full <see cref="WorldBounds"/> when fully generated,
    /// or the runtime streaming startup area bounds when streaming mode is active.
    /// </summary>
    /// <param name="bounds">The usable world bounds, if available.</param>
    /// <returns><c>true</c> if gameplay-ready terrain exists and bounds were populated.</returns>
    public bool TryGetGameplayBounds(out Bounds bounds)
    {
        if (HasGeneratedTerrain)
        {
            bounds = WorldBounds;
            return true;
        }

        return TryGetRuntimeStreamingStartupBounds(out bounds);
    }

    /// <summary>
    /// Looks up a material slot index by the material's display name or composition item name.
    /// Case-insensitive. Returns <c>-1</c> if not found.
    /// </summary>
    /// <param name="materialOrCompositionName">The display name or composition item name to search for.</param>
    /// <returns>Zero-based index into <c>materialDefinitions</c>, or <c>-1</c> if no match.</returns>
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


    /// <summary>
    /// Returns a multi-line debug string showing profile texture data and actual cell material
    /// at <paramref name="worldPoint"/>. Intended for use by <c>VoxelRaycastDebugTool</c>.
    /// </summary>
    /// <param name="worldPoint">The world-space point to inspect.</param>
    public string GetTerrainProfileDebugInfo(Vector3 worldPoint)
    {
        string profileInfo = GetTerrainProfileDebugInfo_Internal(worldPoint);

        // Append actual cellMaterialIndices value if available.
        string actualMaterial = "N/A";
        if (cellMaterialIndices != null && materialDefinitions != null)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            int cellX = Mathf.Clamp(Mathf.FloorToInt(local.x / voxelSizeMeters), 0, TotalCellsX - 1);
            int cellY = Mathf.Clamp(Mathf.FloorToInt(local.y / voxelSizeMeters), 0, TotalCellsY - 1);
            int cellZ = Mathf.Clamp(Mathf.FloorToInt(local.z / voxelSizeMeters), 0, TotalCellsZ - 1);
            int idx = cellMaterialIndices[GetCellIndex(cellX, cellY, cellZ)];
            if (idx >= 0 && idx < materialDefinitions.Count && materialDefinitions[idx] != null)
                actualMaterial = materialDefinitions[idx].ResolveDisplayName();
            else
                actualMaterial = $"index {idx}";
        }

        return profileInfo + $"\nActual cellMaterial: {actualMaterial}";
    }

    /// <summary>
    /// Paints a cylindrical region of cells with a single material index, identified by a
    /// horizontal XZ radius and a vertical half-extent around <paramref name="worldPoint"/>.
    /// Rebuilds affected chunks and optionally fires <see cref="GeometryChanged"/>.
    /// </summary>
    /// <param name="worldPoint">Centre of the painted volume in world space.</param>
    /// <param name="radiusMeters">Horizontal (XZ) radius of the cylinder in metres.</param>
    /// <param name="verticalHalfExtentMeters">Half-height of the cylinder in metres.</param>
    /// <param name="materialIndex">Index into <c>materialDefinitions</c> to assign.</param>
    /// <param name="notifyGeometryChange">When <c>true</c>, fires <see cref="GeometryChanged"/> after rebuilding.</param>
    /// <returns><c>true</c> if any cells were changed.</returns>
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

    /// <summary>
    /// Paints cell material indices within a cylinder based on each cell's depth below
    /// <paramref name="worldSurfaceY"/>, matching the shader's basin material stack exactly
    /// (gravel → sand → mud → clay). Cells at or above the water surface are left unchanged.
    /// Call inside a <see cref="BeginBulkEdit"/>/<see cref="EndBulkEdit"/> block when
    /// painting alongside geometry carving.
    /// </summary>
    /// <param name="worldCenter">World-space XZ centre of the basin cylinder.</param>
    /// <param name="radiusMeters">Horizontal radius of the painted area in metres.</param>
    /// <param name="worldSurfaceY">World-space Y of the water surface; cells below this receive basin materials.</param>
    /// <param name="gravelIndex">Material index for the shallow rim (depth &lt; 0.9 m).</param>
    /// <param name="sandIndex">Material index for the mid zone (depth &lt; 2.5 m).</param>
    /// <param name="mudIndex">Material index for the deep zone (depth &lt; 4.5 m).</param>
    /// <param name="clayIndex">Material index for the deepest zone (depth ≥ 4.5 m).</param>
    /// <returns><c>true</c> if any cells were modified.</returns>
    public bool ApplyBasinMaterialsByDepth(
        Vector3 worldCenter, float radiusMeters, float worldSurfaceY,
        int gravelIndex, int sandIndex, int mudIndex, int clayIndex)
    {
        if (cellMaterialIndices == null || materialDefinitions == null)
            return false;

        Vector3 origin = transform.position;
        float localCX = worldCenter.x - origin.x;
        float localSurfY = worldSurfaceY - origin.y;
        float localCZ = worldCenter.z - origin.z;
        float vs = voxelSizeMeters;
        // Expand by one voxel so that wall cells just outside the carved area (which sit one
        // cell width beyond the outermost carved density brush at ~radius*0.98) are also
        // repainted.  The waterDepth <= 0 guard below still prevents above-water land cells
        // from being incorrectly assigned basin materials.
        float paintRadius = radiusMeters + vs;
        float rSq = paintRadius * paintRadius;

        int minCellX = Mathf.Clamp(Mathf.FloorToInt((localCX - paintRadius) / vs), 0, TotalCellsX - 1);
        int maxCellX = Mathf.Clamp(Mathf.CeilToInt((localCX + paintRadius) / vs), 0, TotalCellsX - 1);
        int minCellZ = Mathf.Clamp(Mathf.FloorToInt((localCZ - paintRadius) / vs), 0, TotalCellsZ - 1);
        int maxCellZ = Mathf.Clamp(Mathf.CeilToInt((localCZ + paintRadius) / vs), 0, TotalCellsZ - 1);
        // Only paint cells below the water surface.
        int maxCellY = Mathf.Clamp(Mathf.CeilToInt(localSurfY / vs), 0, TotalCellsY - 1);

        bool modified = false;
        HashSet<Vector3Int> affectedChunks = new HashSet<Vector3Int>();

        for (int z = minCellZ; z <= maxCellZ; z++)
        {
            for (int x = minCellX; x <= maxCellX; x++)
            {
                float cx = (x + 0.5f) * vs;
                float cz = (z + 0.5f) * vs;
                float dx = cx - localCX;
                float dz = cz - localCZ;
                if (dx * dx + dz * dz > rSq)
                    continue;

                for (int y = 0; y <= maxCellY; y++)
                {
                    float cy = (y + 0.5f) * vs;
                    float waterDepth = localSurfY - cy;
                    if (waterDepth <= 0f)
                        continue;

                    // Noise offset from the pre-baked array (same values the shader reads from tex3.r).
                    // Falls back to 0 if array isn't ready (before first terrain generation).
                    float noiseOffset = (basinNoiseOffsets != null && x < TotalSamplesX && z < TotalSamplesZ)
                        ? basinNoiseOffsets[z * TotalSamplesX + x]
                        : 0f;
                    float perturbedDepth = waterDepth + noiseOffset;

                    byte matIdx;
                    if (perturbedDepth < BasinGravelDepth) matIdx = (byte)(gravelIndex >= 0 ? gravelIndex : 0);
                    else if (perturbedDepth < BasinSandDepth) matIdx = (byte)(sandIndex >= 0 ? sandIndex : 0);
                    else if (perturbedDepth < BasinMudDepth) matIdx = (byte)(mudIndex >= 0 ? mudIndex : 0);
                    else matIdx = (byte)(clayIndex >= 0 ? clayIndex : 0);

                    int idx = GetCellIndex(x, y, z);
                    if (cellMaterialIndices[idx] == matIdx)
                        continue;

                    cellMaterialIndices[idx] = matIdx;
                    affectedChunks.Add(GetChunkCoordinateForCell(x, y, z));
                    modified = true;
                }
            }
        }

        if (!modified)
            return false;

        if (IsBulkEditActive)
            QueueBulkEditedChunks(affectedChunks);
        else
            foreach (Vector3Int coord in affectedChunks)
                RebuildChunk(coord);

        return true;
    }


    /// <summary>
    /// Captures a snapshot of the density and material data within <paramref name="worldBounds"/>.
    /// Use with <see cref="RestoreRegionSnapshot"/> to implement undo or preview/rollback workflows.
    /// </summary>
    /// <param name="worldBounds">World-space AABB defining the region to capture.</param>
    /// <returns>
    /// A <see cref="TerrainRegionSnapshot"/> containing the frozen density and material arrays,
    /// or <c>null</c> if terrain data is not yet available.
    /// </returns>
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

    /// <summary>
    /// Writes a previously captured <see cref="TerrainRegionSnapshot"/> back into the live terrain,
    /// rebuilding affected chunks and optionally firing <see cref="GeometryChanged"/>.
    /// </summary>
    /// <param name="snapshot">The snapshot to restore. Must originate from this terrain instance.</param>
    /// <param name="notifyGeometryChange">When <c>true</c>, fires <see cref="GeometryChanged"/> after restoring.</param>
    /// <returns><c>true</c> if the snapshot was valid and the terrain was updated.</returns>
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

    /// <summary>
    /// Resolves a physics raycast hit against this terrain into an <see cref="IHarvestable"/> target.
    /// Implements <see cref="IRaycastHarvestableProvider"/> so <c>PlayerInteraction</c> can route
    /// terrain hits to the mining/excavation workflow.
    /// </summary>
    /// <param name="hit">The raycast hit to evaluate.</param>
    /// <param name="harvestable">The <see cref="IHarvestable"/> at the hit location, or <c>null</c>.</param>
    /// <returns><c>true</c> if the hit is on this terrain and a harvestable target was created.</returns>
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
        if (!drawWorldBoundsGizmos && !drawChunkBoundsGizmos) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 worldSize = TotalWorldSize;
        // Using your exact field names!
        float chunkSide = cellsPerChunkAxis * voxelSizeMeters;
        Vector3 chunkWorldSize = Vector3.one * chunkSide;

        if (drawWorldBoundsGizmos)
        {
            Gizmos.color = boundsGizmoColor;
            Gizmos.DrawWireCube(worldSize * 0.5f, worldSize);
        }

        if (drawChunkBoundsGizmos)
        {
            // Use the anchor for 'Live' LOD calculations
            Vector3 anchor = (runtimeStreamingAnchor != null) ? runtimeStreamingAnchor.position : transform.position;
            anchor = transform.InverseTransformPoint(anchor);

            for (int z = 0; z < chunkCounts.z; z++)
            {
                for (int y = 0; y < chunkCounts.y; y++)
                {
                    for (int x = 0; x < chunkCounts.x; x++)
                    {
                        Vector3Int coord = new Vector3Int(x, y, z);
                        Vector3 center = Vector3.Scale(new Vector3(x, y, z), chunkWorldSize) + (chunkWorldSize * 0.5f);

                        // 1. LIVE LOD COLOR (What SHOULD be happening)
                        float dist = Vector3.Distance(center, anchor);
                        int liveLod = TerrainLODUtility.ComputeChunkLod(center, anchor, chunkSide, maxLodLevels, lodDistanceFactor);
                        Color lodColor = liveLod == 0 ? Color.green : (liveLod == 1 ? Color.yellow : Color.red);

                        Gizmos.color = new Color(lodColor.r, lodColor.g, lodColor.b, 0.15f);
                        Gizmos.DrawCube(center, chunkWorldSize * 0.95f);

                        // 2. WIREFRAME (Your original wireframes)
                        Gizmos.color = new Color(boundsGizmoColor.r, boundsGizmoColor.g, boundsGizmoColor.b, 0.4f);
                        Gizmos.DrawWireCube(center, chunkWorldSize);

                        // 3. BAKED MASK CHECK (Now using 'generatedChunks'!)
                        if (generatedChunks != null && generatedChunks.TryGetValue(coord, out var chunk))
                        {
                            if (chunk != null && chunk.transitionMask != 0)
                            {
                                Gizmos.color = Color.cyan;
                                for (int i = 0; i < 6; i++)
                                {
                                    if ((chunk.transitionMask & (1 << i)) != 0)
                                    {
                                        // Draw a ray pointing toward the neighbor that triggered the mask
                                        Gizmos.DrawRay(center, (Vector3)GetDir(i) * (chunkSide * 0.45f));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    private Vector3Int GetDir(int index)
    {
        return VoxelTerrainSpatialUtilities.GetDir(index);
    }
}
