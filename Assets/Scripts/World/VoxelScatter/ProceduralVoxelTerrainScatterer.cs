using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ProceduralVoxelTerrainScatterer : MonoBehaviour
{
    private const string DefaultGeneratedRootName = "Generated Voxel Biome Props";
    private const int ScatterSetupWorkUnitCount = 5;


    [Header("Voxel Terrain")]
    [SerializeField] private ProceduralVoxelTerrain voxelTerrain;
    [SerializeField] private bool generateTerrainBeforeScattering = true;

    [Header("Water Exclusion")]
    [SerializeField] private ProceduralVoxelTerrainWaterSystem waterSystem;
    [SerializeField] private bool generateWaterBeforeScattering = true;
    [SerializeField, Min(0f)] private float waterExclusionPaddingMeters = 1f;

    [Header("Scatter Generation")]
    [SerializeField] private int seed = 24680;
    [SerializeField] private bool randomizeSeed = false;
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private string generatedRootName = DefaultGeneratedRootName;

    [Header("Biome Prototypes")]
    [SerializeField] private List<TerrainScatterPrototype> prototypes = new List<TerrainScatterPrototype>();

    [Header("Debug")]
    [SerializeField] private bool logGenerationTimings = false;

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;
    public bool HasGeneratedScatter => HasGeneratedChildren(GetGeneratedRoot()) && !IsScatterGenerationInProgress;
    public bool IsScatterGenerationInProgress => isScatterGenerationInProgress;
    public float ScatterGenerationProgress01 => isScatterGenerationInProgress
        ? scatterGenerationProgress01
        : (HasGeneratedScatter ? 1f : 0f);
    public string ScatterGenerationStatus => isScatterGenerationInProgress ? scatterGenerationStatus : string.Empty;

    [NonSerialized] private bool isScatterGenerationInProgress;
    [NonSerialized] private float scatterGenerationProgress01;
    [NonSerialized] private string scatterGenerationStatus = string.Empty;

    private void Reset()
    {
        ApplyOlympicRainforestPreset();
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
            if (voxelTerrain.HasReadyGameplayTerrain)
            {
                break;
            }

            if (!voxelTerrain.IsTerrainGenerationInProgress)
            {
                if (generateTerrainBeforeScattering && voxelTerrain.IsRuntimeStreamingModeActive)
                {
                    voxelTerrain.GenerateTerrainWithConfiguredMode(voxelTerrain.ClearExistingBeforeGenerate);
                    yield return null;
                    continue;
                }

                break;
            }

            yield return null;
        }

        GenerateScatter(clearExistingBeforeGenerate);
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
        prototypes = OlympicRainforestPreset.Build();
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
        if (isScatterGenerationInProgress)
        {
            Debug.LogWarning($"{gameObject.name} is already generating voxel scatter.", this);
            return new List<GameObject>();
        }

        ScatterGenerationMetrics generationMetrics = logGenerationTimings ? new ScatterGenerationMetrics() : null;
        bool collectTimings = generationMetrics != null;
        long totalTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
        List<GameObject> createdObjects = new List<GameObject>();
        bool success = false;
        int prototypeCount = prototypes != null ? prototypes.Count : 0;

        BeginScatterGenerationProgress();

        try
        {
            UpdateScatterGenerationProgress(
                "Resolving voxel terrain for scatter",
                ComputeScatterProgress01(0, 0, 0f, prototypeCount));
            long stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            ProceduralVoxelTerrain resolvedTerrain = ResolveVoxelTerrainAndMaybeGenerate();
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref generationMetrics.resolveTerrainTicks, stepTimingStart);
            }

            if (randomizeSeed)
            {
                seed = Environment.TickCount;
            }

            UpdateScatterGenerationProgress(
                "Resolving water exclusion for scatter",
                ComputeScatterProgress01(1, 0, 0f, prototypeCount));
            stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            ProceduralVoxelTerrainWaterSystem resolvedWaterSystem = ResolveWaterSystemAndMaybeGenerate();
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref generationMetrics.resolveWaterTicks, stepTimingStart);
            }

            if (resolvedTerrain == null)
            {
                resolvedTerrain = voxelTerrain != null ? voxelTerrain : GetComponent<ProceduralVoxelTerrain>();
            }

            if (resolvedTerrain == null || !resolvedTerrain.HasReadyGameplayTerrain)
            {
                Debug.LogWarning($"{gameObject.name} could not scatter voxel placeholders because no voxel terrain is available.");
                return createdObjects;
            }

            UpdateScatterGenerationProgress(
                clearExisting ? "Clearing existing voxel scatter" : "Retaining existing voxel scatter",
                ComputeScatterProgress01(2, 0, 0f, prototypeCount));
            stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            if (clearExisting)
            {
                ClearGeneratedScatterContents();
            }
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref generationMetrics.clearTicks, stepTimingStart);
            }

            UpdateScatterGenerationProgress(
                "Resolving voxel scatter bounds",
                ComputeScatterProgress01(3, 0, 0f, prototypeCount));
            stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            bool hasBounds = resolvedTerrain.TryGetGameplayBounds(out Bounds bounds);
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref generationMetrics.boundsTicks, stepTimingStart);
            }
            if (!hasBounds)
            {
                Debug.LogWarning($"{gameObject.name} could not scatter voxel placeholders because no ready gameplay terrain bounds are available.");
                return createdObjects;
            }

            UpdateScatterGenerationProgress(
                "Preparing voxel scatter root",
                ComputeScatterProgress01(4, 0, 0f, prototypeCount));
            stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            Transform generatedRoot = EnsureGeneratedRoot();
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref generationMetrics.rootTicks, stepTimingStart);
            }

            System.Random random = new System.Random(seed);

            for (int prototypeIndex = 0; prototypeIndex < prototypeCount; prototypeIndex++)
            {
                TerrainScatterPrototype prototype = prototypes[prototypeIndex];
                if (prototype == null)
                {
                    UpdateScatterGenerationProgress(
                        $"Skipping empty voxel scatter prototype {prototypeIndex + 1}/{prototypeCount}",
                        ComputeScatterProgress01(ScatterSetupWorkUnitCount, prototypeIndex + 1, 0f, prototypeCount));
                    continue;
                }

                prototype.Sanitize();
                string prototypeName = prototype.ResolveDisplayName();
                CompositionInfo resolvedComposition = prototype.ResolveComposition();
                if (resolvedComposition == null)
                {
                    Debug.LogWarning($"{gameObject.name} could not resolve a CompositionInfo for voxel scatter prototype {prototypeName}.");
                    UpdateScatterGenerationProgress(
                        $"Skipping unresolved voxel scatter prototype {prototypeIndex + 1}/{prototypeCount}: {prototypeName}",
                        ComputeScatterProgress01(ScatterSetupWorkUnitCount, prototypeIndex + 1, 0f, prototypeCount));
                    continue;
                }

                PrototypeGenerationMetrics prototypeMetrics = collectTimings
                    ? new PrototypeGenerationMetrics
                    {
                        prototypeName = prototypeName,
                        requestedCount = prototype.ComputeEffectiveSpawnCount(resolvedTerrain, bounds)
                    }
                    : null;
                int placedCount = ScatterPrototypePlacer.PlacePrototypeInstances(
                    random,
                    resolvedTerrain,
                    bounds,
                    resolvedWaterSystem,
                    generatedRoot,
                    prototype,
                    resolvedComposition,
                    createdObjects,
                    prototypeIndex,
                    prototypeCount,
                    waterExclusionPaddingMeters,
                    gameObject.layer,
                    gameObject.name,
                    UpdateScatterGenerationProgress,
                    activeProgress01 => ComputeScatterProgress01(ScatterSetupWorkUnitCount, prototypeIndex, activeProgress01, prototypeCount),
                    prototypeMetrics);
                if (prototypeMetrics != null)
                {
                    prototypeMetrics.acceptedCount = placedCount;
                    generationMetrics.prototypeMetrics.Add(prototypeMetrics);
                }

                UpdateScatterGenerationProgress(
                    $"Scatter generation: prototype {prototypeIndex + 1}/{prototypeCount} {prototypeName} complete ({placedCount}/{prototype.ComputeEffectiveSpawnCount(resolvedTerrain, bounds)})",
                    ComputeScatterProgress01(ScatterSetupWorkUnitCount, prototypeIndex + 1, 0f, prototypeCount));
            }

            success = true;
            UpdateScatterGenerationProgress("Voxel scatter generation complete", 1f);

            if (collectTimings)
            {
                generationMetrics.totalTicks = ScatterTimingUtility.GetElapsedTicks(totalTimingStart);
                LogScatterGenerationTimingsSummary(generationMetrics, createdObjects.Count);
            }

            return createdObjects;
        }
        finally
        {
            FinishScatterGenerationProgress(success);
        }
    }

    public bool ClearGeneratedScatter()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
            return false;
        }

        ClearGeneratedScatterContents();

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

    private void ClearGeneratedScatterContents()
    {
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

    public Transform GetGeneratedRoot()
    {
        return transform.Find(string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName);
    }

    private ProceduralVoxelTerrain ResolveVoxelTerrainAndMaybeGenerate()
    {
        if (voxelTerrain == null)
        {
            voxelTerrain = GetComponent<ProceduralVoxelTerrain>();
        }

        if (voxelTerrain != null &&
            generateTerrainBeforeScattering &&
            (waterSystem == null || !generateWaterBeforeScattering) &&
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

    private ProceduralVoxelTerrainWaterSystem ResolveWaterSystemAndMaybeGenerate()
    {
        if (waterSystem == null)
        {
            waterSystem = GetComponent<ProceduralVoxelTerrainWaterSystem>();
        }

        if (waterSystem != null &&
            generateWaterBeforeScattering &&
            !HasGeneratedChildren(waterSystem.GetGeneratedRoot()))
        {
            waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
        }

        return waterSystem;
    }

    private static bool HasGeneratedChildren(Transform root)
    {
        if (root == null)
        {
            return false;
        }

        foreach (Transform _ in root)
        {
            return true;
        }

        return false;
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

    private void BeginScatterGenerationProgress()
    {
        isScatterGenerationInProgress = true;
        scatterGenerationProgress01 = 0f;
        scatterGenerationStatus = "Preparing voxel scatter generation";
        UpdateEditorScatterProgressSurface();
    }

    private void FinishScatterGenerationProgress(bool success)
    {
        if (success)
        {
            scatterGenerationProgress01 = 1f;
            scatterGenerationStatus = "Voxel scatter generation complete";
        }

        UpdateEditorScatterProgressSurface();
        isScatterGenerationInProgress = false;
        UpdateEditorScatterProgressSurface(clearProgressBar: true);

        if (!success)
        {
            scatterGenerationProgress01 = 0f;
            scatterGenerationStatus = string.Empty;
        }
    }

    private void UpdateScatterGenerationProgress(string status, float progress01)
    {
        scatterGenerationStatus = string.IsNullOrWhiteSpace(status)
            ? "Preparing voxel scatter generation"
            : status;
        scatterGenerationProgress01 = Mathf.Clamp01(progress01);
        UpdateEditorScatterProgressSurface();
    }

    private void UpdateEditorScatterProgressSurface(bool clearProgressBar = false)
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            return;
        }

        if (clearProgressBar)
        {
            EditorUtility.ClearProgressBar();
            SceneView.RepaintAll();
            return;
        }

        if (!isScatterGenerationInProgress)
        {
            return;
        }

        EditorUtility.DisplayProgressBar(
            $"Generating {name}",
            scatterGenerationStatus,
            scatterGenerationProgress01);
        SceneView.RepaintAll();
#endif
    }

    private static float ComputeScatterProgress01(int completedSetupSteps, int completedPrototypeCount, float activePrototypeProgress01, int totalPrototypeCount)
    {
        float totalWorkUnits = ScatterSetupWorkUnitCount + Mathf.Max(0, totalPrototypeCount);
        float completedWorkUnits = Mathf.Clamp(completedSetupSteps, 0, ScatterSetupWorkUnitCount)
            + Mathf.Clamp(completedPrototypeCount, 0, Mathf.Max(0, totalPrototypeCount))
            + Mathf.Clamp01(activePrototypeProgress01);
        return totalWorkUnits <= 0.0001f
            ? 1f
            : Mathf.Clamp01(completedWorkUnits / totalWorkUnits);
    }

    private void LogScatterGenerationTimingsSummary(ScatterGenerationMetrics generationMetrics, int createdObjectCount)
    {
        if (generationMetrics == null)
        {
            return;
        }

        long prototypeTicks = 0L;
        for (int i = 0; i < generationMetrics.prototypeMetrics.Count; i++)
        {
            PrototypeGenerationMetrics prototypeMetrics = generationMetrics.prototypeMetrics[i];
            prototypeTicks += prototypeMetrics.totalTicks;
            Debug.Log(ScatterTimingUtility.BuildPrototypeTimingSummary(prototypeMetrics), this);
        }

        Debug.Log(
            $"[{nameof(ProceduralVoxelTerrainScatterer)}:{name}] GenerateScatter completed in {ScatterTimingUtility.FormatTimingMilliseconds(generationMetrics.totalTicks)}. " +
            $"Spawned {createdObjectCount} objects. Steps: terrain={ScatterTimingUtility.FormatTimingMilliseconds(generationMetrics.resolveTerrainTicks)}, " +
            $"water={ScatterTimingUtility.FormatTimingMilliseconds(generationMetrics.resolveWaterTicks)}, clear={ScatterTimingUtility.FormatTimingMilliseconds(generationMetrics.clearTicks)}, " +
            $"bounds={ScatterTimingUtility.FormatTimingMilliseconds(generationMetrics.boundsTicks)}, root={ScatterTimingUtility.FormatTimingMilliseconds(generationMetrics.rootTicks)}, " +
            $"prototypes={ScatterTimingUtility.FormatTimingMilliseconds(prototypeTicks)}.",
            this);
    }

}
