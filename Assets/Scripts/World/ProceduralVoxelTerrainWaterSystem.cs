using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProceduralVoxelTerrainWaterSystem : MonoBehaviour, IRaycastHarvestableProvider
{
    private const string DefaultGeneratedRootName = "Generated Voxel Water";
    private const float DefaultHarvestedWaterMassGrams = 5000000f;
    private const float MinimumRenderableLakeVolumeCubicMeters = LakeHydrologySolver.MinimumRenderableLakeVolumeCubicMeters;

    /// <summary>
    /// Optional shared configuration asset. When assigned the config's tuning values
    /// take precedence over the per-scene inline fields below during water generation
    /// and refresh scheduling. Leave unassigned to preserve the original per-scene
    /// inspector workflow.
    /// </summary>
    [Header("Configuration Asset (optional)")]
    [SerializeField] private WaterSimulationConfig simulationConfig;

    [Header("Voxel Terrain")]
    [SerializeField] private ProceduralVoxelTerrain voxelTerrain;
    [SerializeField] private bool generateTerrainBeforeWater = true;

    [Header("Generation")]
    [SerializeField] private int seed = 35791;
    [SerializeField] private bool randomizeSeed = false;
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private string generatedRootName = DefaultGeneratedRootName;

    [Header("Ocean")]
    [SerializeField] private bool generateOcean = true;
    [SerializeField, Min(0f)] private float seaLevelMeters = 4.8f;
    [SerializeField, Min(1f)] private float oceanPaddingMeters = 220f;
    [SerializeField, Min(0.1f)] private float oceanDepthEquivalentMeters = 12f;

    [Header("Freshwater")]
    [SerializeField] private bool generateFreshwater = true;
    [SerializeField] private bool generateLakes = true;
    [SerializeField] private bool generatePonds = true;
    [SerializeField] private bool generateRivers = false;
    [SerializeField, Range(0, 4)] private int lakeCount = 2;
    [SerializeField] private Vector2 lakeRadiusRangeMeters = new Vector2(8f, 14f);
    [SerializeField, Min(0.25f)] private float lakeDepthMeters = 1.5f;
    [SerializeField, Range(0, 6)] private int pondCount = 3;
    [SerializeField] private Vector2 pondRadiusRangeMeters = new Vector2(3.5f, 6.5f);
    [SerializeField, Min(0.25f)] private float pondDepthMeters = 0.75f;
    [SerializeField, Range(0, 3)] private int riverCount = 0;
    [SerializeField] private Vector2 riverWidthRangeMeters = new Vector2(4f, 8f);
    [SerializeField, Min(0.25f)] private float riverDepthMeters = 1.2f;
    [SerializeField, Range(8, 64)] private int riverSampleCount = 24;
    [SerializeField] private Vector2 riverSourceHeightRangeNormalized = new Vector2(0.38f, 0.85f);
    [SerializeField, Min(100f)] private float freshwaterHarvestMassGrams = DefaultHarvestedWaterMassGrams;

    [Header("Voxel Carving")]
    [SerializeField] private bool carveTerrainForWater = true;
    [SerializeField, Min(0.25f)] private float riverCarveStepMeters = 3f;

    [Header("Rendering")]
    [SerializeField, Min(0.05f)] private float waterSurfaceThicknessMeters = 0.2f;
    [SerializeField] private Material freshWaterMaterial;
    [SerializeField] private Material saltWaterMaterial;
    [SerializeField] private Color freshWaterColor = new Color(0.23f, 0.5f, 0.64f, 0.82f);
    [SerializeField] private Color saltWaterColor = new Color(0.16f, 0.34f, 0.58f, 0.84f);
    [SerializeField] private bool useTriggerColliders = true;

    [Header("Dynamic Updates")]
    [SerializeField] private bool updateWaterOnTerrainDeform = true;
    [SerializeField, Range(12, 72)] private int lakeOutlineSampleCount = 40;
    [SerializeField, Min(0.25f)] private float lakeVolumeSampleSpacingMeters = 1f;
    [SerializeField, Min(0f)] private float lakeDynamicExpansionMeters = 8f;
    [SerializeField, Min(0f)] private float waterUpdatePaddingMeters = 2f;
    [SerializeField, Min(0f)] private float terrainRefreshDebounceSeconds = 0.03f;

    [Header("Debug")]
    [SerializeField] private bool logLakeDebug = false;
    [SerializeField] private bool logGenerationTimings = false;

    private readonly List<GeneratedLake> generatedLakes = new List<GeneratedLake>();
    private readonly List<GeneratedRiverSegment> generatedRiverSegments = new List<GeneratedRiverSegment>();
    private readonly List<GeneratedRiver> generatedRivers = new List<GeneratedRiver>();
    private WaterGenerationProfiler generationProfiler;
    private WaterRenderFactory renderFactory;
    private WaterDeformationUpdater deformationUpdater;
    private FreshwaterGenerator freshwaterGenerator;
    private WaterInteractionSystem interactionSystem;
    private LakeHydrologySolver hydrologySolver;

    private WaterGenerationProfiler GenerationProfiler => generationProfiler ??= new WaterGenerationProfiler();

    private WaterRenderFactory RenderFactory
    {
        get
        {
            if (renderFactory != null)
            {
                return renderFactory;
            }

            voxelTerrain ??= GetComponent<ProceduralVoxelTerrain>();
            renderFactory = new WaterRenderFactory(
                gameObject.layer,
                waterSurfaceThicknessMeters,
                seaLevelMeters,
                oceanPaddingMeters,
                oceanDepthEquivalentMeters,
                useTriggerColliders,
                voxelTerrain != null ? voxelTerrain.VoxelSizeMeters : 1f,
                ResolveFreshWaterMaterial,
                ResolveSaltWaterMaterial,
                ResolveFreshWaterComposition,
                ResolveSeaWaterComposition,
                LogLakeDebug);
            return renderFactory;
        }
    }

    private WaterDeformationUpdater DeformationUpdater =>
        deformationUpdater ??= new WaterDeformationUpdater(
            generatedLakes,
            generatedRivers,
            generatedRiverSegments,
            HydrologySolver,
            MinimumRenderableLakeVolumeCubicMeters,
            lakeDepthMeters,
            riverDepthMeters,
            lakeDynamicExpansionMeters,
            waterUpdatePaddingMeters,
            terrainRefreshDebounceSeconds,
            seaLevelMeters,
            EnsureGeneratedRoot,
            CreateLakeObject,
            CreateRiverObject,
            LogLakeDebug);

    private FreshwaterGenerator FreshwaterGeneratorHelper =>
        freshwaterGenerator ??= new FreshwaterGenerator(
            BuildFreshwaterGeneratorConfig(),
            generatedLakes,
            generatedRivers,
            generatedRiverSegments,
            HydrologySolver,
            UpdateWaterGenerationProgress,
            UpdateLakeInfluenceBounds,
            UpdateRiverInfluenceBounds,
            () => gameObject.name,
            LogLakeDebug);

    private WaterInteractionSystem InteractionSystem =>
        interactionSystem ??= new WaterInteractionSystem(
            freshwaterHarvestMassGrams,
            MinimumRenderableLakeVolumeCubicMeters,
            HydrologySolver,
            ResolveComposition,
            EnsureGeneratedRoot,
            CreateLakeObject,
            UpdateLakeInfluenceBounds,
            () => ResolveVoxelTerrainAndMaybeGenerate(false),
            LogLakeDebug);

    private LakeHydrologySolver HydrologySolver =>
        hydrologySolver ??= new LakeHydrologySolver(
            waterSurfaceThicknessMeters,
            lakeDepthMeters,
            lakeDynamicExpansionMeters,
            waterUpdatePaddingMeters,
            LogLakeDebug);

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;
    public bool HasGeneratedWater => !IsWaterGenerationInProgress &&
        ((renderFactory != null && renderFactory.GeneratedOceanObject != null) || generatedLakes.Count > 0 || generatedRivers.Count > 0);
    public bool IsWaterGenerationInProgress => GenerationProfiler.IsGenerating;
    public float WaterGenerationProgress01 => IsWaterGenerationInProgress
        ? GenerationProfiler.Progress01
        : (HasGeneratedWater ? 1f : 0f);
    public string WaterGenerationStatus => IsWaterGenerationInProgress ? GenerationProfiler.Status : string.Empty;
    public IReadOnlyList<WaterGenerationTimingEntry> LastWaterGenerationTimings => GenerationProfiler.Timings;
    public long LastWaterGenerationTotalMilliseconds => GenerationProfiler.TotalMilliseconds;
    public string LastWaterGenerationSummary => GenerationProfiler.Summary;
    public float SeaLevelMeters => renderFactory != null ? renderFactory.GetCurrentOceanSurfaceY() : seaLevelMeters;

    /// <summary>
    /// The optional shared configuration asset driving this system.
    /// Returns <c>null</c> when the system is using its own inline inspector fields.
    /// </summary>
    public WaterSimulationConfig SimulationConfig => simulationConfig;

    private void Reset()
    {
        ApplyCoastalRainforestWaterPreset();
    }

    private void OnEnable()
    {
        SubscribeToTerrainChanges();
    }

    private void OnDisable()
    {
        UnsubscribeFromTerrainChanges();
        ClearPendingTerrainRefresh();
    }

    private void LateUpdate()
    {
        ProcessPendingTerrainRefresh();
    }

    private IEnumerator Start()
    {
        if (!Application.isPlaying || !generateOnStart)
        {
            yield break;
        }

        voxelTerrain ??= GetComponent<ProceduralVoxelTerrain>();
        yield return null;
        while (voxelTerrain != null)
        {
            if (HasTerrainReadyForWater(voxelTerrain))
            {
                break;
            }

            if (!voxelTerrain.IsTerrainGenerationInProgress)
            {
                if (generateTerrainBeforeWater && voxelTerrain.IsRuntimeStreamingModeActive)
                {
                    voxelTerrain.GenerateTerrainWithConfiguredMode(voxelTerrain.ClearExistingBeforeGenerate);
                    yield return null;
                    continue;
                }

                break;
            }

            yield return null;
        }

        GenerateWater(clearExistingBeforeGenerate);
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
        pondCount = Mathf.Clamp(pondCount, 0, 6);
        pondRadiusRangeMeters = new Vector2(
            Mathf.Max(1f, Mathf.Min(pondRadiusRangeMeters.x, pondRadiusRangeMeters.y)),
            Mathf.Max(1f, Mathf.Max(pondRadiusRangeMeters.x, pondRadiusRangeMeters.y)));
        pondDepthMeters = Mathf.Max(0.25f, pondDepthMeters);
        riverCount = Mathf.Clamp(riverCount, 0, 3);
        riverWidthRangeMeters = new Vector2(
            Mathf.Max(1f, Mathf.Min(riverWidthRangeMeters.x, riverWidthRangeMeters.y)),
            Mathf.Max(1f, Mathf.Max(riverWidthRangeMeters.x, riverWidthRangeMeters.y)));
        riverDepthMeters = Mathf.Max(0.25f, riverDepthMeters);
        riverSampleCount = Mathf.Clamp(riverSampleCount, 8, 64);
        riverSourceHeightRangeNormalized = new Vector2(
            Mathf.Clamp01(Mathf.Min(riverSourceHeightRangeNormalized.x, riverSourceHeightRangeNormalized.y)),
            Mathf.Clamp01(Mathf.Max(riverSourceHeightRangeNormalized.x, riverSourceHeightRangeNormalized.y)));
        freshwaterHarvestMassGrams = Mathf.Max(100f, freshwaterHarvestMassGrams);
        riverCarveStepMeters = Mathf.Max(0.25f, riverCarveStepMeters);
        waterSurfaceThicknessMeters = Mathf.Max(0.05f, waterSurfaceThicknessMeters);
        lakeOutlineSampleCount = Mathf.Clamp(lakeOutlineSampleCount, 12, 72);
        lakeVolumeSampleSpacingMeters = Mathf.Max(0.25f, lakeVolumeSampleSpacingMeters);
        lakeDynamicExpansionMeters = Mathf.Max(0f, lakeDynamicExpansionMeters);
        waterUpdatePaddingMeters = Mathf.Max(0f, waterUpdatePaddingMeters);
        terrainRefreshDebounceSeconds = Mathf.Max(0f, terrainRefreshDebounceSeconds);
        InvalidateWaterHelpers();
        SubscribeToTerrainChanges();
    }

    [ContextMenu("Apply Coastal Rainforest Water Preset")]
    public void ApplyCoastalRainforestWaterPreset()
    {
        seaLevelMeters = 4.8f;
        oceanPaddingMeters = 220f;
        oceanDepthEquivalentMeters = 12f;
        generateFreshwater = true;
        generateLakes = true;
        generatePonds = true;
        generateRivers = false;
        lakeCount = 2;
        lakeRadiusRangeMeters = new Vector2(8f, 14f);
        lakeDepthMeters = 1.5f;
        pondCount = 3;
        pondRadiusRangeMeters = new Vector2(3.5f, 6.5f);
        pondDepthMeters = 0.75f;
        riverCount = 0;
        riverWidthRangeMeters = new Vector2(4f, 8f);
        riverDepthMeters = 1.2f;
        riverSampleCount = 24;
        riverSourceHeightRangeNormalized = new Vector2(0.38f, 0.85f);
        freshwaterHarvestMassGrams = DefaultHarvestedWaterMassGrams;
        riverCarveStepMeters = 3f;
        waterSurfaceThicknessMeters = 0.2f;
        InvalidateWaterHelpers();
    }

    [ContextMenu("Generate Voxel Water")]
    public void GenerateWaterFromContextMenu()
    {
        GenerateWater(clearExistingBeforeGenerate);
    }

    [ContextMenu("Clear Voxel Water")]
    public void ClearGeneratedWaterFromContextMenu()
    {
        ClearGeneratedWater();
    }

    public bool GenerateWater(bool clearExisting)
    {
        if (IsWaterGenerationInProgress)
        {
            LogLakeDebug("GenerateWater ignored because generation is already in progress.");
            return false;
        }

        System.Diagnostics.Stopwatch totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool success = false;
        string finalStatus = "Voxel water generation did not complete.";
        try
        {
            ResetWaterGenerationMonitoring("Preparing voxel water");
            UpdateWaterGenerationProgress("Preparing voxel water", 0f, true);

            System.Diagnostics.Stopwatch prepareStopwatch = System.Diagnostics.Stopwatch.StartNew();
            ProceduralVoxelTerrain resolvedTerrain = ResolveVoxelTerrainAndMaybeGenerate();
            if (!HasTerrainReadyForWater(resolvedTerrain))
            {
                finalStatus = $"{gameObject.name} could not generate voxel water because no voxel terrain is available.";
                Debug.LogWarning(finalStatus);
            }
            else
            {
                if (randomizeSeed)
                {
                    seed = Environment.TickCount;
                }

                if (clearExisting)
                {
                    UpdateWaterGenerationProgress("Clearing existing voxel water", 0.5f);
                    ClearGeneratedWaterContents();
                }

                generatedLakes.Clear();
                generatedRiverSegments.Clear();
                generatedRivers.Clear();

                System.Random random = new System.Random(seed);
                Bounds bounds = resolvedTerrain.WorldBounds;
                if (!resolvedTerrain.TryGetGameplayBounds(out bounds))
                {
                    bounds = resolvedTerrain.WorldBounds;
                }

                prepareStopwatch.Stop();
                RecordWaterGenerationStep("Prepare", prepareStopwatch.ElapsedMilliseconds, $"seed={seed}, clearExisting={clearExisting}");
                CompleteWaterGenerationStep();

                if (ShouldGenerateLakes())
                {
                    UpdateWaterGenerationProgress("Generating lakes", 0f, true);
                    System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    FreshwaterGenerationStats lakeStats = FreshwaterGeneratorHelper.GenerateLakes(random, resolvedTerrain, bounds, lakeCount);
                    stopwatch.Stop();
                    RecordWaterGenerationStep("Lakes", stopwatch.ElapsedMilliseconds, lakeStats.BuildSummary());
                    CompleteWaterGenerationStep();
                }

                if (ShouldGeneratePonds())
                {
                    UpdateWaterGenerationProgress("Generating ponds", 0f, true);
                    System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    FreshwaterGenerationStats pondStats = FreshwaterGeneratorHelper.GeneratePonds(random, resolvedTerrain, bounds, pondCount);
                    stopwatch.Stop();
                    RecordWaterGenerationStep("Ponds", stopwatch.ElapsedMilliseconds, pondStats.BuildSummary());
                    CompleteWaterGenerationStep();
                }

                if (ShouldGenerateRivers())
                {
                    UpdateWaterGenerationProgress("Generating rivers", 0f, true);
                    System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    string riverSummary = FreshwaterGeneratorHelper.GenerateRivers(random, resolvedTerrain, bounds, riverCount);
                    stopwatch.Stop();
                    RecordWaterGenerationStep("Rivers", stopwatch.ElapsedMilliseconds, riverSummary);
                    CompleteWaterGenerationStep();
                }

                Transform generatedRoot = EnsureGeneratedRoot();
                if (generateOcean)
                {
                    UpdateWaterGenerationProgress("Generating ocean water", 0f, true);
                    System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    CreateOceanObject(resolvedTerrain.WorldBounds, generatedRoot);
                    stopwatch.Stop();
                    RecordWaterGenerationStep(
                        "Ocean",
                        stopwatch.ElapsedMilliseconds,
                        $"surface={GetCurrentOceanSurfaceY():F2}m, padding={oceanPaddingMeters:F1}m");
                    CompleteWaterGenerationStep();
                }

                UpdateWaterGenerationProgress("Creating water objects", 0f, true);
                System.Diagnostics.Stopwatch objectStopwatch = System.Diagnostics.Stopwatch.StartNew();
                int totalObjectsToCreate = generatedLakes.Count + generatedRivers.Count;
                int createdObjectCount = 0;
                for (int i = 0; i < generatedLakes.Count; i++)
                {
                    CreateLakeObject(generatedLakes[i], generatedRoot);
                    createdObjectCount++;
                    UpdateWaterGenerationProgress(
                        $"Creating water objects ({createdObjectCount}/{Mathf.Max(1, totalObjectsToCreate)})",
                        totalObjectsToCreate <= 0 ? 1f : createdObjectCount / (float)totalObjectsToCreate);
                }

                for (int i = 0; i < generatedRivers.Count; i++)
                {
                    CreateRiverObject(generatedRivers[i], generatedRoot, i);
                    createdObjectCount++;
                    UpdateWaterGenerationProgress(
                        $"Creating water objects ({createdObjectCount}/{Mathf.Max(1, totalObjectsToCreate)})",
                        totalObjectsToCreate <= 0 ? 1f : createdObjectCount / (float)totalObjectsToCreate);
                }

                objectStopwatch.Stop();
                RecordWaterGenerationStep(
                    "Objects",
                    objectStopwatch.ElapsedMilliseconds,
                    $"lakeObjects={generatedLakes.Count}, riverObjects={generatedRivers.Count}");
                CompleteWaterGenerationStep();

                SubscribeToTerrainChanges();
                success = true;
                finalStatus = "Voxel water generation complete.";
            }
        }
        finally
        {
            totalStopwatch.Stop();
            FinalizeWaterGenerationMonitoring(totalStopwatch.ElapsedMilliseconds, success, finalStatus);
            ClearWaterGenerationEditorProgress();
        }

        if (success && logGenerationTimings)
        {
            Debug.Log(
                $"[{nameof(ProceduralVoxelTerrainWaterSystem)}:{name}] GenerateWater timings: {LastWaterGenerationSummary}. Generated lakes={generatedLakes.Count}, rivers={generatedRivers.Count}.",
                this);
        }

        return success;
    }

    public bool ClearGeneratedWater()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
            generatedLakes.Clear();
            generatedRiverSegments.Clear();
            generatedRivers.Clear();
            renderFactory?.ClearOceanState();
            SubscribeToTerrainChanges();
            return false;
        }

        ClearGeneratedWaterContents();

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
        generatedRivers.Clear();
        renderFactory?.ClearOceanState();
        SubscribeToTerrainChanges();
        return true;
    }

    public bool IsPointUnderWater(Vector3 worldPoint, float paddingMeters = 0f)
    {
        ProceduralVoxelTerrain resolvedTerrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (!HasTerrainReadyForWater(resolvedTerrain))
        {
            return false;
        }

        bool hasOcean = TryGetOceanBounds(out Bounds oceanBounds);
        return WaterSpatialQueryUtility.IsPointUnderWater(
            worldPoint,
            paddingMeters,
            generatedLakes,
            generatedRiverSegments,
            MinimumRenderableLakeVolumeCubicMeters,
            hasOcean,
            oceanBounds,
            GetCurrentOceanSurfaceY());
    }

    public bool TryGetTerrainExcavationBlockReason(Vector3 worldPoint, float excavationRadiusMeters, out string blockReason)
    {
        blockReason = string.Empty;

        ProceduralVoxelTerrain terrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (!HasTerrainReadyForWater(terrain))
        {
            return false;
        }

        return InteractionSystem.TryGetTerrainExcavationBlockReason(worldPoint, excavationRadiusMeters, generatedLakes, terrain, out blockReason);
    }

    public bool TryGetNearestFreshwaterPoint(Vector3 worldPoint, out Vector3 nearestPoint, out float distanceMeters)
    {
        return WaterSpatialQueryUtility.TryGetNearestFreshwaterPoint(
            worldPoint,
            generatedLakes,
            generatedRiverSegments,
            MinimumRenderableLakeVolumeCubicMeters,
            out nearestPoint,
            out distanceMeters);
    }

    public string GetLakeDebugSummaryAtPoint(Vector3 worldPoint, float pointPaddingMeters = 0.75f)
    {
        return WaterDebugUtility.GetLakeDebugSummaryAtPoint(
            generatedLakes,
            worldPoint,
            pointPaddingMeters,
            MinimumRenderableLakeVolumeCubicMeters);
    }

    public string GetAllLakeDebugSummary()
    {
        return WaterDebugUtility.GetAllLakeDebugSummary(generatedLakes, MinimumRenderableLakeVolumeCubicMeters);
    }

    public bool TryAddWaterFromRaycast(RaycastHit hit, float waterMassGrams, float pointPaddingMeters = 0.75f)
    {
        return InteractionSystem.TryAddWaterFromRaycast(hit, generatedLakes, waterMassGrams, pointPaddingMeters);
    }

    public bool TryAddWaterAtPoint(Vector3 worldPoint, float waterMassGrams, float pointPaddingMeters = 0.75f)
    {
        return InteractionSystem.TryAddWaterAtPoint(worldPoint, generatedLakes, waterMassGrams, pointPaddingMeters);
    }

    public bool TryCreateDebugLakeAtPoint(Vector3 worldPoint, float radiusMeters)
    {
        ProceduralVoxelTerrain terrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (!HasTerrainReadyForWater(terrain))
        {
            return false;
        }

        if (!FreshwaterGeneratorHelper.TryCreateDebugLakeAtPoint(terrain, worldPoint, radiusMeters, out GeneratedLake createdLake))
        {
            return false;
        }

        generatedLakes.Add(createdLake);
        CreateLakeObject(createdLake, EnsureGeneratedRoot());
        return true;
    }

    public bool TryCreateMergeTestLakePairAtPoint(
        Vector3 worldPoint,
        Vector3 lateralDirection,
        float radiusMeters,
        float shorelineGapMeters,
        float ridgeHeightMeters)
    {
        ProceduralVoxelTerrain terrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (!HasTerrainReadyForWater(terrain))
        {
            return false;
        }

        if (!FreshwaterGeneratorHelper.TryCreateMergeTestLakePairAtPoint(
                terrain,
                worldPoint,
                lateralDirection,
                radiusMeters,
                shorelineGapMeters,
                ridgeHeightMeters,
                out GeneratedLake firstLake,
                out GeneratedLake secondLake))
        {
            return false;
        }

        generatedLakes.Add(firstLake);
        generatedLakes.Add(secondLake);

        Transform generatedRoot = EnsureGeneratedRoot();
        CreateLakeObject(firstLake, generatedRoot);
        CreateLakeObject(secondLake, generatedRoot);
        return true;
    }

    public Transform GetGeneratedRoot()
    {
        return transform.Find(string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName);
    }

    public void RefreshWaterForChangedBounds(Bounds changedBounds)
    {
        RefreshWaterForChangedBounds(changedBounds, null);
    }

    public void RefreshWaterForChangedBounds(Bounds changedBounds, IReadOnlyCollection<Vector3Int> changedChunkCoordinates)
    {
        if (IsWaterGenerationInProgress)
        {
            LogLakeDebug("RefreshWaterForChangedBounds ignored because water generation is in progress.");
            return;
        }

        if (!updateWaterOnTerrainDeform || !generateFreshwater || generatedLakes.Count == 0 && generatedRivers.Count == 0)
        {
            LogLakeDebug($"RefreshWaterForChangedBounds ignored. updateWaterOnTerrainDeform={updateWaterOnTerrainDeform}, generateFreshwater={generateFreshwater}, lakes={generatedLakes.Count}, rivers={generatedRivers.Count}.");
            return;
        }

        ProceduralVoxelTerrain terrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (!HasTerrainReadyForWater(terrain))
        {
            LogLakeDebug("RefreshWaterForChangedBounds aborted because voxel terrain is unavailable.");
            return;
        }

        DeformationUpdater.RefreshWaterForChangedBounds(terrain, changedBounds, changedChunkCoordinates);
    }

    public bool TryGetHarvestable(RaycastHit hit, out IHarvestable harvestable)
    {
        return InteractionSystem.TryGetHarvestable(hit, generatedLakes, generatedRivers, out harvestable);
    }

    private int GetConfiguredWaterGenerationStepCount()
    {
        int stepCount = 2;
        if (ShouldGenerateLakes())
        {
            stepCount++;
        }

        if (ShouldGeneratePonds())
        {
            stepCount++;
        }

        if (ShouldGenerateRivers())
        {
            stepCount++;
        }

        if (generateOcean)
        {
            stepCount++;
        }

        return stepCount;
    }

    private void ResetWaterGenerationMonitoring(string initialStatus)
    {
        GenerationProfiler.Reset(GetConfiguredWaterGenerationStepCount(), initialStatus, name);
    }

    private void UpdateWaterGenerationProgress(string status, float currentStepProgress01 = 0f, bool forceEditorRefresh = false)
    {
        GenerationProfiler.UpdateProgress(status, currentStepProgress01, forceEditorRefresh, name);
    }

    private void RecordWaterGenerationStep(string label, long milliseconds, string details = null)
    {
        GenerationProfiler.RecordStep(label, milliseconds, details);
    }

    private void CompleteWaterGenerationStep()
    {
        GenerationProfiler.CompleteStep(name);
    }

    private void FinalizeWaterGenerationMonitoring(long totalMilliseconds, bool success, string finalStatus)
    {
        GenerationProfiler.Finalize(totalMilliseconds, success, finalStatus, name);
    }

    private void ClearWaterGenerationEditorProgress()
    {
        GenerationProfiler.ClearEditorProgress();
    }

    private void ClearGeneratedWaterContents()
    {
        generatedLakes.Clear();
        generatedRiverSegments.Clear();
        generatedRivers.Clear();
        renderFactory?.ClearOceanState();

        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
            return;
        }

        List<GameObject> children = new List<GameObject>();
        foreach (Transform child in generatedRoot)
        {
            if (child != null)
            {
                children.Add(child.gameObject);
            }
        }

        for (int i = 0; i < children.Count; i++)
        {
            GameObject childObject = children[i];
            if (childObject == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                childObject.SetActive(false);
                Destroy(childObject);
            }
            else
            {
                DestroyImmediate(childObject);
            }
        }
    }

    private bool ShouldGenerateLakes()
    {
        return generateFreshwater && generateLakes && lakeCount > 0;
    }

    private bool ShouldGeneratePonds()
    {
        return generateFreshwater && generatePonds && pondCount > 0;
    }

    private bool ShouldGenerateRivers()
    {
        return generateFreshwater && generateRivers && riverCount > 0;
    }

    private ProceduralVoxelTerrain ResolveVoxelTerrainAndMaybeGenerate(bool regenerateWhenRequested = true)
    {
        if (voxelTerrain == null)
        {
            voxelTerrain = GetComponent<ProceduralVoxelTerrain>();
        }

        if (voxelTerrain != null &&
            generateTerrainBeforeWater &&
            regenerateWhenRequested &&
            !voxelTerrain.HasReadyGameplayTerrain &&
            !voxelTerrain.IsTerrainGenerationInProgress)
        {
            if (voxelTerrain.IsRuntimeStreamingModeActive)
            {
                voxelTerrain.GenerateTerrainWithConfiguredMode(voxelTerrain.ClearExistingBeforeGenerate);
            }
            else
            {
                voxelTerrain.GenerateTerrain(voxelTerrain.ClearExistingBeforeGenerate);
            }
        }

        return voxelTerrain;
    }

    private static bool HasTerrainReadyForWater(ProceduralVoxelTerrain terrain)
    {
        return terrain != null && terrain.HasReadyGameplayTerrain;
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

    private void SubscribeToTerrainChanges()
    {
        if (voxelTerrain == null)
        {
            voxelTerrain = GetComponent<ProceduralVoxelTerrain>();
        }

        if (voxelTerrain == null)
        {
            return;
        }

        voxelTerrain.GeometryChanged -= HandleTerrainGeometryChanged;
        if (ShouldHandleTerrainGeometryChanges())
        {
            voxelTerrain.GeometryChanged += HandleTerrainGeometryChanged;
            return;
        }

        ClearPendingTerrainRefresh();
    }

    private void UnsubscribeFromTerrainChanges()
    {
        if (voxelTerrain != null)
        {
            voxelTerrain.GeometryChanged -= HandleTerrainGeometryChanged;
        }
    }

    private bool ShouldHandleTerrainGeometryChanges()
    {
        return updateWaterOnTerrainDeform &&
               generateFreshwater &&
               generatedRivers.Count > 0;
    }

    private void ClearPendingTerrainRefresh()
    {
        deformationUpdater?.ClearPendingTerrainRefresh();
    }

    private void HandleTerrainGeometryChanged(VoxelTerrainGeometryChangedEventArgs changeArgs)
    {
        if (!ShouldHandleTerrainGeometryChanges())
        {
            ClearPendingTerrainRefresh();
            return;
        }

        DeformationUpdater.HandleTerrainGeometryChanged(changeArgs);
    }

    private void ProcessPendingTerrainRefresh()
    {
        if (!ShouldHandleTerrainGeometryChanges())
        {
            ClearPendingTerrainRefresh();
            return;
        }

        if (deformationUpdater == null)
        {
            return;
        }

        if (!deformationUpdater.TryConsumePendingRefresh(out Bounds changedBounds, out List<Vector3Int> changedChunks))
        {
            return;
        }

        RefreshWaterForChangedBounds(changedBounds, changedChunks);
    }

    private void CreateOceanObject(Bounds bounds, Transform generatedRoot)
    {
        RenderFactory.CreateOceanObject(bounds, generatedRoot);
    }

    private void CreateLakeObject(GeneratedLake lake, Transform generatedRoot)
    {
        RenderFactory.CreateOrUpdateLakeObject(lake, generatedRoot, MinimumRenderableLakeVolumeCubicMeters);
    }

    private void CreateRiverObject(GeneratedRiver river, Transform generatedRoot, int index)
    {
        RenderFactory.CreateOrUpdateRiverObject(river, generatedRoot, index);
    }

    private float GetCurrentOceanSurfaceY()
    {
        return renderFactory != null ? renderFactory.GetCurrentOceanSurfaceY() : seaLevelMeters;
    }

    private bool TryGetOceanBounds(out Bounds oceanBounds)
    {
        oceanBounds = default;
        return generateOcean && renderFactory != null && renderFactory.TryGetOceanBounds(out oceanBounds);
    }

    private void UpdateLakeInfluenceBounds(ProceduralVoxelTerrain terrain, GeneratedLake lake)
    {
        DeformationUpdater.UpdateLakeInfluenceBounds(terrain, lake);
    }

    private void UpdateRiverInfluenceBounds(ProceduralVoxelTerrain terrain, GeneratedRiver river)
    {
        DeformationUpdater.UpdateRiverInfluenceBounds(terrain, river);
    }

    private CompositionInfo ResolveFreshWaterComposition()
    {
        return ResolveComposition("Fresh Water");
    }

    private CompositionInfo ResolveSeaWaterComposition()
    {
        return ResolveComposition("Sea Water");
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

    private Material ResolveFreshWaterMaterial()
    {
        return WaterMaterialUtility.ResolveWaterMaterial(
            freshWaterMaterial,
            ref WaterMaterialUtility.CachedFreshWaterMaterial,
            freshWaterColor,
            "Voxel Fresh Water Auto Material");
    }

    private Material ResolveSaltWaterMaterial()
    {
        return WaterMaterialUtility.ResolveWaterMaterial(
            saltWaterMaterial,
            ref WaterMaterialUtility.CachedSaltWaterMaterial,
            saltWaterColor,
            "Voxel Salt Water Auto Material");
    }

    private FreshwaterGenerator.FreshwaterGeneratorConfig BuildFreshwaterGeneratorConfig()
    {
        return new FreshwaterGenerator.FreshwaterGeneratorConfig
        {
            seaLevelMeters = seaLevelMeters,
            lakeDepthMeters = lakeDepthMeters,
            pondDepthMeters = pondDepthMeters,
            lakeDynamicExpansionMeters = lakeDynamicExpansionMeters,
            waterUpdatePaddingMeters = waterUpdatePaddingMeters,
            riverCarveStepMeters = riverCarveStepMeters,
            riverDepthMeters = riverDepthMeters,
            carveTerrainForWater = carveTerrainForWater,
            lakeRadiusRangeMeters = lakeRadiusRangeMeters,
            pondRadiusRangeMeters = pondRadiusRangeMeters,
            riverWidthRangeMeters = riverWidthRangeMeters,
            riverSampleCount = riverSampleCount,
            riverSourceHeightRangeNormalized = riverSourceHeightRangeNormalized,
            minRenderableVolumeCubicMeters = MinimumRenderableLakeVolumeCubicMeters
        };
    }

    private void InvalidateWaterHelpers()
    {
        renderFactory = null;
        deformationUpdater = null;
        freshwaterGenerator = null;
        interactionSystem = null;
        hydrologySolver = null;
    }

    private void LogLakeDebug(string message)
    {
        if (!logLakeDebug)
        {
            return;
        }

        Debug.Log($"[{nameof(ProceduralVoxelTerrainWaterSystem)}:{name}] {message}", this);
    }
}
