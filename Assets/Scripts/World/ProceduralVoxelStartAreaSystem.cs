using System;
using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

[DefaultExecutionOrder(1000)]
public class ProceduralVoxelStartAreaSystem : MonoBehaviour
{
    private const string DefaultGeneratedRootName = "Generated Voxel Start Area";
    private static readonly Dictionary<string, Material> AutoMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);

    private struct StartAreaCandidate
    {
        public Vector3 center;
        public Vector3 surfaceNormal;
        public Vector3 nearestFreshwaterPoint;
        public float freshwaterDistanceMeters;
        public float score;
    }

    [Header("References")]
    [SerializeField] private ProceduralVoxelTerrain voxelTerrain;
    [SerializeField] private ProceduralVoxelTerrainWaterSystem waterSystem;
    [SerializeField] private ProceduralVoxelTerrainScatterer scatterer;
    [SerializeField] private Transform playerRoot;

    [Header("Generation")]
    [SerializeField] private int seed = 48621;
    [SerializeField] private bool randomizeSeed = false;
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool repositionPlayerOnStart = true;
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private string generatedRootName = DefaultGeneratedRootName;
    [SerializeField] private bool generateTerrainBeforeStartArea = true;
    [SerializeField] private bool generateWaterBeforeStartArea = true;
    [SerializeField] private bool generateScatterBeforeStartArea = true;

    [Header("Candidate Search")]
    [SerializeField, Min(16)] private int candidateSamples = 120;
    [SerializeField, Min(4f)] private float edgePaddingMeters = 18f;
    [SerializeField, Range(0f, 25f)] private float maxSlopeDegrees = 10f;
    [SerializeField] private Vector2 freshwaterDistanceRangeMeters = new Vector2(8f, 36f);
    [SerializeField, Min(1f)] private float preferredFreshwaterDistanceMeters = 18f;
    [SerializeField, Min(1f)] private float localPatchCheckRadiusMeters = 6f;
    [SerializeField, Min(0.1f)] private float maxPatchHeightVariationMeters = 1.6f;

    [Header("Clearing")]
    [SerializeField, Min(3f)] private float clearingRadiusMeters = 9f;
    [SerializeField, Min(0f)] private float clearingRemovalPaddingMeters = 2.5f;

    [Header("Landmarks")]
    [SerializeField] private bool createNaturalLandmarks = true;
    [SerializeField, Range(1, 4)] private int landmarkCount = 3;
    [SerializeField, Min(4f)] private float landmarkRingRadiusMeters = 11f;
    [SerializeField] private Color boulderColor = new Color(0.46f, 0.5f, 0.45f, 1f);
    [SerializeField] private Color logColor = new Color(0.38f, 0.27f, 0.16f, 1f);
    [SerializeField] private Color snagColor = new Color(0.29f, 0.21f, 0.14f, 1f);

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color clearingGizmoColor = new Color(0.52f, 0.86f, 0.58f, 0.85f);
    [SerializeField] private Color spawnGizmoColor = new Color(0.95f, 0.88f, 0.42f, 0.95f);

    [NonSerialized] private bool hasGeneratedStartArea;
    [NonSerialized] private Vector3 lastClearingCenter;
    [NonSerialized] private Vector3 lastSuggestedLookTarget;
    [NonSerialized] private Vector3 lastSpawnPosition;

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;

    private void Reset()
    {
        ApplyCoastalRainforestStartPreset();
    }

    private void OnValidate()
    {
        generatedRootName = string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName.Trim();
        candidateSamples = Mathf.Max(16, candidateSamples);
        edgePaddingMeters = Mathf.Max(4f, edgePaddingMeters);
        maxSlopeDegrees = Mathf.Clamp(maxSlopeDegrees, 0f, 25f);
        freshwaterDistanceRangeMeters = new Vector2(
            Mathf.Max(0f, Mathf.Min(freshwaterDistanceRangeMeters.x, freshwaterDistanceRangeMeters.y)),
            Mathf.Max(0f, Mathf.Max(freshwaterDistanceRangeMeters.x, freshwaterDistanceRangeMeters.y)));
        preferredFreshwaterDistanceMeters = Mathf.Max(1f, preferredFreshwaterDistanceMeters);
        localPatchCheckRadiusMeters = Mathf.Max(1f, localPatchCheckRadiusMeters);
        maxPatchHeightVariationMeters = Mathf.Max(0.1f, maxPatchHeightVariationMeters);
        clearingRadiusMeters = Mathf.Max(3f, clearingRadiusMeters);
        clearingRemovalPaddingMeters = Mathf.Max(0f, clearingRemovalPaddingMeters);
        landmarkCount = Mathf.Clamp(landmarkCount, 1, 4);
        landmarkRingRadiusMeters = Mathf.Max(clearingRadiusMeters + 1f, landmarkRingRadiusMeters);
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
                if (generateTerrainBeforeStartArea && voxelTerrain.IsRuntimeStreamingModeActive)
                {
                    voxelTerrain.GenerateTerrainWithConfiguredMode(voxelTerrain.ClearExistingBeforeGenerate);
                    yield return null;
                    continue;
                }

                break;
            }

            yield return null;
        }

        GenerateStartArea(repositionPlayerOnStart);
    }

    [ContextMenu("Apply Coastal Rainforest Start Preset")]
    public void ApplyCoastalRainforestStartPreset()
    {
        candidateSamples = 120;
        edgePaddingMeters = 18f;
        maxSlopeDegrees = 10f;
        freshwaterDistanceRangeMeters = new Vector2(8f, 36f);
        preferredFreshwaterDistanceMeters = 18f;
        localPatchCheckRadiusMeters = 6f;
        maxPatchHeightVariationMeters = 1.6f;
        clearingRadiusMeters = 9f;
        clearingRemovalPaddingMeters = 2.5f;
        landmarkCount = 3;
        landmarkRingRadiusMeters = 11f;
    }

    [ContextMenu("Generate Start Area")]
    public void GenerateStartAreaFromContextMenu()
    {
        GenerateStartArea(false);
    }

    [ContextMenu("Clear Generated Start Area")]
    public void ClearGeneratedStartAreaFromContextMenu()
    {
        ClearGeneratedStartArea();
    }

    public bool GenerateStartArea(bool repositionPlayer)
    {
        if (!ResolveDependencies())
        {
            return false;
        }

        if (randomizeSeed)
        {
            seed = Environment.TickCount;
        }

        if (clearExistingBeforeGenerate)
        {
            ClearGeneratedStartAreaContents();
        }

        List<Vector3> scatterPositions = GatherScatterPositions();
        System.Random random = new System.Random(seed);
        if (!TryFindBestCandidate(random, scatterPositions, out StartAreaCandidate candidate))
        {
            Debug.LogWarning($"{gameObject.name} could not find a suitable voxel starting area candidate.");
            return false;
        }

        RemoveScatterWithinRadius(candidate.center, clearingRadiusMeters + clearingRemovalPaddingMeters);

        Transform generatedRoot = EnsureGeneratedRoot();
        if (createNaturalLandmarks)
        {
            CreateLandmarks(random, candidate, generatedRoot);
        }

        lastClearingCenter = candidate.center;
        lastSuggestedLookTarget = candidate.nearestFreshwaterPoint;
        lastSpawnPosition = candidate.center;
        hasGeneratedStartArea = true;

        if (repositionPlayer)
        {
            RepositionPlayer(candidate);
        }

        return true;
    }

    public bool ClearGeneratedStartArea()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
            hasGeneratedStartArea = false;
            return false;
        }

        ClearGeneratedStartAreaContents();
        if (Application.isPlaying)
        {
            Destroy(generatedRoot.gameObject);
        }
        else
        {
            DestroyImmediate(generatedRoot.gameObject);
        }

        hasGeneratedStartArea = false;
        return true;
    }

    public Transform GetGeneratedRoot()
    {
        return transform.Find(string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName);
    }

    private bool ResolveDependencies()
    {
        voxelTerrain ??= GetComponent<ProceduralVoxelTerrain>();
        waterSystem ??= GetComponent<ProceduralVoxelTerrainWaterSystem>();
        scatterer ??= GetComponent<ProceduralVoxelTerrainScatterer>();

        if (voxelTerrain == null)
        {
            Debug.LogWarning($"{gameObject.name} requires a {nameof(ProceduralVoxelTerrain)} to generate a starting area.");
            return false;
        }

        if (generateTerrainBeforeStartArea && !voxelTerrain.HasReadyGameplayTerrain)
        {
            if (!voxelTerrain.IsTerrainGenerationInProgress)
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
        }

        if (!voxelTerrain.HasReadyGameplayTerrain)
        {
            Debug.LogWarning($"{gameObject.name} could not generate a starting area because voxel terrain is not available.");
            return false;
        }

        if (waterSystem != null && generateWaterBeforeStartArea && !HasGeneratedChildren(waterSystem.GetGeneratedRoot()))
        {
            waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
        }

        if (scatterer != null && generateScatterBeforeStartArea && !HasGeneratedChildren(scatterer.GetGeneratedRoot()))
        {
            scatterer.GenerateScatter(scatterer.ClearExistingBeforeGenerate);
        }

        return true;
    }

    private bool TryFindBestCandidate(System.Random random, IReadOnlyList<Vector3> scatterPositions, out StartAreaCandidate bestCandidate)
    {
        if (TryFindBestCandidate(random, scatterPositions, true, out bestCandidate))
        {
            return true;
        }

        return TryFindBestCandidate(random, scatterPositions, false, out bestCandidate);
    }

    private bool TryFindBestCandidate(System.Random random, IReadOnlyList<Vector3> scatterPositions, bool requireFreshwater, out StartAreaCandidate bestCandidate)
    {
        bestCandidate = default;
        float bestScore = float.MinValue;
        if (!voxelTerrain.TryGetGameplayBounds(out Bounds bounds))
        {
            return false;
        }

        float minX = bounds.min.x + edgePaddingMeters;
        float maxX = bounds.max.x - edgePaddingMeters;
        float minZ = bounds.min.z + edgePaddingMeters;
        float maxZ = bounds.max.z - edgePaddingMeters;
        if (minX >= maxX || minZ >= maxZ)
        {
            return false;
        }

        bool found = false;
        for (int attempt = 0; attempt < candidateSamples; attempt++)
        {
            float worldX = NextFloat(random, minX, maxX);
            float worldZ = NextFloat(random, minZ, maxZ);
            bool usedCachedSurface = voxelTerrain.TryGetCachedSurfacePointWorld(
                worldX,
                worldZ,
                out Vector3 screeningCenter,
                out Vector3 screeningNormal);
            RaycastHit hit = default;
            if (!usedCachedSurface)
            {
                if (!voxelTerrain.TrySampleSurfaceWorld(worldX, worldZ, out hit))
                {
                    continue;
                }

                screeningCenter = hit.point;
                screeningNormal = hit.normal;
            }

            screeningNormal = NormalizeSurfaceNormal(screeningNormal);
            if (Vector3.Angle(screeningNormal, Vector3.up) > maxSlopeDegrees)
            {
                continue;
            }

            if (!TryEvaluateLocalPatchScreening(
                screeningCenter,
                usedCachedSurface,
                out float screeningHeightVariation,
                out float screeningAverageSlope,
                out bool screeningUsedLivePatch))
            {
                continue;
            }

            Vector3 center = screeningCenter;
            Vector3 surfaceNormal = screeningNormal;
            if (usedCachedSurface)
            {
                // Use the cached terrain prepass for the noisy candidate search, then confirm the final
                // center against the live terrain so carved shorelines and riverbanks stay accurate.
                if (!voxelTerrain.TrySampleSurfaceWorld(worldX, worldZ, out hit))
                {
                    continue;
                }

                center = hit.point;
                surfaceNormal = NormalizeSurfaceNormal(hit.normal);
            }

            if (waterSystem != null && waterSystem.IsPointUnderWater(center, 0.75f))
            {
                continue;
            }

            float slope = Vector3.Angle(surfaceNormal, Vector3.up);
            if (slope > maxSlopeDegrees)
            {
                continue;
            }

            bool hasConfirmedLivePatch = screeningUsedLivePatch && !usedCachedSurface;
            float heightVariation = screeningHeightVariation;
            float averageSlope = screeningAverageSlope;
            if (!hasConfirmedLivePatch &&
                !TryEvaluateLocalPatch(center, out heightVariation, out averageSlope))
            {
                continue;
            }

            Vector3 nearestFreshwaterPoint = center + Vector3.forward;
            float freshwaterDistanceMeters = float.PositiveInfinity;
            bool hasFreshwater = waterSystem != null &&
                waterSystem.TryGetNearestFreshwaterPoint(center, out nearestFreshwaterPoint, out freshwaterDistanceMeters);
            if (requireFreshwater && !hasFreshwater)
            {
                continue;
            }

            if (requireFreshwater &&
                (freshwaterDistanceMeters < freshwaterDistanceRangeMeters.x || freshwaterDistanceMeters > freshwaterDistanceRangeMeters.y))
            {
                continue;
            }

            float slopeScore = 1f - Mathf.Clamp01(averageSlope / Mathf.Max(0.001f, maxSlopeDegrees));
            float patchScore = 1f - Mathf.Clamp01(heightVariation / Mathf.Max(0.001f, maxPatchHeightVariationMeters));
            float shelterScore = EvaluateShelterScore(center, scatterPositions);
            float edgeScore = EvaluateEdgeScore(center, bounds);
            float freshwaterScore = hasFreshwater
                ? 1f - Mathf.Clamp01(Mathf.Abs(freshwaterDistanceMeters - preferredFreshwaterDistanceMeters) / Mathf.Max(1f, freshwaterDistanceRangeMeters.y - freshwaterDistanceRangeMeters.x))
                : 0.2f;
            float totalScore = (freshwaterScore * 2.2f) + (patchScore * 1.8f) + (slopeScore * 1.4f) + (shelterScore * 1.1f) + (edgeScore * 0.6f);
            if (totalScore <= bestScore)
            {
                continue;
            }

            bestScore = totalScore;
            bestCandidate = new StartAreaCandidate
            {
                center = center,
                surfaceNormal = surfaceNormal,
                nearestFreshwaterPoint = hasFreshwater ? nearestFreshwaterPoint : center + Vector3.forward,
                freshwaterDistanceMeters = hasFreshwater ? freshwaterDistanceMeters : float.PositiveInfinity,
                score = totalScore
            };
            found = true;
        }

        return found;
    }

    private bool TryEvaluateLocalPatch(Vector3 center, out float heightVariation, out float averageSlope)
    {
        heightVariation = 0f;
        averageSlope = 0f;

        float minHeight = center.y;
        float maxHeight = center.y;
        float slopeSum = 0f;
        int sampleCount = 0;
        for (int i = 0; i < 8; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / 8f;
            Vector3 offset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * localPatchCheckRadiusMeters;
            if (!voxelTerrain.TrySampleSurfaceWorld(center.x + offset.x, center.z + offset.z, out RaycastHit hit))
            {
                return false;
            }

            if (waterSystem != null && waterSystem.IsPointUnderWater(hit.point, 0.35f))
            {
                return false;
            }

            minHeight = Mathf.Min(minHeight, hit.point.y);
            maxHeight = Mathf.Max(maxHeight, hit.point.y);
            slopeSum += Vector3.Angle(hit.normal, Vector3.up);
            sampleCount++;
        }

        heightVariation = maxHeight - minHeight;
        averageSlope = sampleCount <= 0 ? 0f : slopeSum / sampleCount;
        return heightVariation <= maxPatchHeightVariationMeters && averageSlope <= maxSlopeDegrees;
    }

    private bool TryEvaluateLocalPatchScreening(
        Vector3 center,
        bool usedCachedSurface,
        out float heightVariation,
        out float averageSlope,
        out bool usedLivePatchEvaluation)
    {
        heightVariation = 0f;
        averageSlope = 0f;
        usedLivePatchEvaluation = false;

        if (!usedCachedSurface)
        {
            usedLivePatchEvaluation = true;
            return TryEvaluateLocalPatch(center, out heightVariation, out averageSlope);
        }

        if (TryEvaluateLocalPatchCached(center, out heightVariation, out averageSlope, out bool hadCompleteCachedPatch))
        {
            return true;
        }

        if (hadCompleteCachedPatch)
        {
            return false;
        }

        // A sparse cached prepass should fall back to live sampling instead of rejecting a valid start area.
        usedLivePatchEvaluation = true;
        return TryEvaluateLocalPatch(center, out heightVariation, out averageSlope);
    }

    private bool TryEvaluateLocalPatchCached(
        Vector3 center,
        out float heightVariation,
        out float averageSlope,
        out bool hadCompleteCachedPatch)
    {
        heightVariation = 0f;
        averageSlope = 0f;
        hadCompleteCachedPatch = false;
        if (voxelTerrain == null)
        {
            return false;
        }

        float minHeight = center.y;
        float maxHeight = center.y;
        float slopeSum = 0f;
        int sampleCount = 0;
        for (int i = 0; i < 8; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / 8f;
            Vector3 offset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * localPatchCheckRadiusMeters;
            if (!voxelTerrain.TryGetCachedSurfacePointWorld(center.x + offset.x, center.z + offset.z, out Vector3 samplePoint, out Vector3 sampleNormal))
            {
                return false;
            }

            minHeight = Mathf.Min(minHeight, samplePoint.y);
            maxHeight = Mathf.Max(maxHeight, samplePoint.y);
            slopeSum += Vector3.Angle(NormalizeSurfaceNormal(sampleNormal), Vector3.up);
            sampleCount++;
        }

        hadCompleteCachedPatch = true;
        heightVariation = maxHeight - minHeight;
        averageSlope = sampleCount <= 0 ? 0f : slopeSum / sampleCount;
        return heightVariation <= maxPatchHeightVariationMeters && averageSlope <= maxSlopeDegrees;
    }

    private float EvaluateShelterScore(Vector3 center, IReadOnlyList<Vector3> scatterPositions)
    {
        if (scatterPositions == null || scatterPositions.Count == 0)
        {
            return 0.45f;
        }

        float ringInnerRadius = clearingRadiusMeters + 2f;
        float ringOuterRadius = clearingRadiusMeters + 12f;
        float ringInnerSquared = ringInnerRadius * ringInnerRadius;
        float ringOuterSquared = ringOuterRadius * ringOuterRadius;
        int ringCount = 0;

        for (int i = 0; i < scatterPositions.Count; i++)
        {
            Vector3 planarDelta = Vector3.ProjectOnPlane(scatterPositions[i] - center, Vector3.up);
            float distanceSquared = planarDelta.sqrMagnitude;
            if (distanceSquared >= ringInnerSquared && distanceSquared <= ringOuterSquared)
            {
                ringCount++;
            }
        }

        return Mathf.Clamp01(ringCount / 9f);
    }

    private static float EvaluateEdgeScore(Vector3 position, Bounds bounds) => VoxelBrushUtility.EvaluateEdgeScore(position, bounds);

    private List<Vector3> GatherScatterPositions()
    {
        List<Vector3> positions = new List<Vector3>();
        Transform generatedRoot = scatterer != null ? scatterer.GetGeneratedRoot() : null;
        if (generatedRoot == null)
        {
            return positions;
        }

        foreach (Transform child in generatedRoot)
        {
            if (child != null)
            {
                positions.Add(child.position);
            }
        }

        return positions;
    }

    private int RemoveScatterWithinRadius(Vector3 center, float radiusMeters)
    {
        Transform generatedRoot = scatterer != null ? scatterer.GetGeneratedRoot() : null;
        if (generatedRoot == null)
        {
            return 0;
        }

        List<GameObject> toRemove = new List<GameObject>();
        float radiusSquared = radiusMeters * radiusMeters;
        foreach (Transform child in generatedRoot)
        {
            if (child == null)
            {
                continue;
            }

            Vector3 planarDelta = Vector3.ProjectOnPlane(child.position - center, Vector3.up);
            if (planarDelta.sqrMagnitude <= radiusSquared)
            {
                toRemove.Add(child.gameObject);
            }
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            if (Application.isPlaying)
            {
                toRemove[i].SetActive(false);
                Destroy(toRemove[i]);
            }
            else
            {
                DestroyImmediate(toRemove[i]);
            }
        }

        return toRemove.Count;
    }

    private void CreateLandmarks(System.Random random, StartAreaCandidate candidate, Transform generatedRoot)
    {
        float baseYaw = 0f;
        Vector3 waterDirection = Vector3.ProjectOnPlane(candidate.nearestFreshwaterPoint - candidate.center, Vector3.up);
        if (waterDirection.sqrMagnitude > 0.0001f)
        {
            baseYaw = Mathf.Atan2(waterDirection.z, waterDirection.x) * Mathf.Rad2Deg;
        }

        float[] angleOffsets = { -120f, 140f, 180f, 75f };
        List<Vector3> placedPositions = new List<Vector3>();
        for (int i = 0; i < landmarkCount; i++)
        {
            float angle = baseYaw + angleOffsets[i % angleOffsets.Length] + NextFloat(random, -16f, 16f);
            float radius = landmarkRingRadiusMeters + NextFloat(random, -1.2f, 1.2f);
            Vector3 samplePosition = candidate.center + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
            if (!voxelTerrain.TrySampleSurfaceWorld(samplePosition.x, samplePosition.z, out RaycastHit hit))
            {
                continue;
            }

            if (waterSystem != null && waterSystem.IsPointUnderWater(hit.point, 0.35f))
            {
                continue;
            }

            if (!IsFarEnoughFromPlaced(hit.point, placedPositions, 3.2f))
            {
                continue;
            }

            switch (i % 3)
            {
                case 0:
                    CreateBoulderLandmark(hit.point, hit.normal, generatedRoot, random, i);
                    break;

                case 1:
                    CreateFallenLogLandmark(hit.point, hit.normal, generatedRoot, random, i, waterDirection);
                    break;

                default:
                    CreateSnagLandmark(hit.point, hit.normal, generatedRoot, random, i);
                    break;
            }

            placedPositions.Add(hit.point);
        }
    }

    private void CreateBoulderLandmark(Vector3 point, Vector3 normal, Transform generatedRoot, System.Random random, int index)
    {
        GameObject boulder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        boulder.name = $"Start Area Boulder {index + 1:00}";
        boulder.layer = gameObject.layer;
        boulder.transform.SetParent(generatedRoot, true);
        Vector3 scale = new Vector3(
            NextFloat(random, 1.8f, 2.8f),
            NextFloat(random, 1.2f, 2f),
            NextFloat(random, 1.6f, 2.6f));
        boulder.transform.localScale = scale;
        boulder.transform.position = point + (normal.normalized * (scale.y * 0.32f));
        boulder.transform.rotation = Quaternion.Euler(NextFloat(random, 0f, 360f), NextFloat(random, 0f, 360f), NextFloat(random, 0f, 360f));

        MeshRenderer renderer = boulder.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = ResolveAutoMaterial("Start Area Boulder", boulderColor, 0.28f);
        }
    }

    private void CreateFallenLogLandmark(Vector3 point, Vector3 normal, Transform generatedRoot, System.Random random, int index, Vector3 waterDirection)
    {
        GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        log.name = $"Start Area Log {index + 1:00}";
        log.layer = gameObject.layer;
        log.transform.SetParent(generatedRoot, true);
        float radius = NextFloat(random, 0.28f, 0.46f);
        float halfLength = NextFloat(random, 2.4f, 3.6f);
        log.transform.localScale = new Vector3(radius, halfLength, radius);

        Vector3 logDirection = waterDirection.sqrMagnitude > 0.0001f
            ? Vector3.Cross(Vector3.up, waterDirection.normalized)
            : new Vector3(Mathf.Cos(index), 0f, Mathf.Sin(index)).normalized;
        if (logDirection.sqrMagnitude <= 0.0001f)
        {
            logDirection = Vector3.right;
        }

        log.transform.rotation = Quaternion.FromToRotation(Vector3.up, logDirection.normalized) * Quaternion.AngleAxis(NextFloat(random, -12f, 12f), logDirection.normalized);
        log.transform.position = point + (normal.normalized * (radius + 0.05f));

        MeshRenderer renderer = log.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = ResolveAutoMaterial("Start Area Log", logColor, 0.18f);
        }
    }

    private void CreateSnagLandmark(Vector3 point, Vector3 normal, Transform generatedRoot, System.Random random, int index)
    {
        GameObject snag = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        snag.name = $"Start Area Snag {index + 1:00}";
        snag.layer = gameObject.layer;
        snag.transform.SetParent(generatedRoot, true);
        float radius = NextFloat(random, 0.32f, 0.55f);
        float halfHeight = NextFloat(random, 2.1f, 3.8f);
        snag.transform.localScale = new Vector3(radius, halfHeight, radius);
        snag.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal.normalized) * Quaternion.AngleAxis(NextFloat(random, 0f, 360f), normal.normalized);
        snag.transform.position = point + (normal.normalized * halfHeight);

        MeshRenderer renderer = snag.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = ResolveAutoMaterial("Start Area Snag", snagColor, 0.14f);
        }
    }

    private void RepositionPlayer(StartAreaCandidate candidate)
    {
        Transform resolvedPlayerRoot = ResolvePlayerRoot();
        if (resolvedPlayerRoot == null)
        {
            Debug.LogWarning($"{gameObject.name} could not find a player object to reposition. Assign Player Root if auto-detection is not enough.");
            return;
        }

        CharacterController controller = resolvedPlayerRoot.GetComponent<CharacterController>();
        Rigidbody body = resolvedPlayerRoot.GetComponent<Rigidbody>();

        Vector3 spawnPosition = candidate.center + Vector3.up * 0.05f;
        if (controller != null)
        {
            spawnPosition += Vector3.up * ((controller.height * 0.5f) - controller.center.y + controller.skinWidth + 0.05f);
            controller.enabled = false;
        }

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        resolvedPlayerRoot.position = spawnPosition;

        Vector3 lookDirection = Vector3.ProjectOnPlane(candidate.nearestFreshwaterPoint - candidate.center, Vector3.up);
        if (lookDirection.sqrMagnitude > 0.001f)
        {
            resolvedPlayerRoot.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }

        if (controller != null)
        {
            controller.enabled = true;
        }

        lastSpawnPosition = spawnPosition;
    }

    private Transform ResolvePlayerRoot()
    {
        if (playerRoot != null)
        {
            return playerRoot;
        }

        FirstPersonController firstPersonController = FindAnyObjectByType<FirstPersonController>();
        if (firstPersonController != null)
        {
            playerRoot = firstPersonController.transform;
            return playerRoot;
        }

        PlayerInteraction interaction = FindAnyObjectByType<PlayerInteraction>();
        if (interaction != null)
        {
            playerRoot = interaction.transform;
            return playerRoot;
        }

        CharacterController characterController = FindAnyObjectByType<CharacterController>();
        if (characterController != null)
        {
            playerRoot = characterController.transform;
            return playerRoot;
        }

        return null;
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

    private void ClearGeneratedStartAreaContents()
    {
        hasGeneratedStartArea = false;
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
            if (Application.isPlaying)
            {
                children[i].SetActive(false);
                Destroy(children[i]);
            }
            else
            {
                DestroyImmediate(children[i]);
            }
        }
    }

    private static bool HasGeneratedChildren(Transform root) => VoxelBrushUtility.HasGeneratedChildren(root);

    private static bool IsFarEnoughFromPlaced(Vector3 position, IReadOnlyList<Vector3> existingPositions, float minimumSpacingMeters)
        => VoxelBrushUtility.IsFarEnoughFromPlaced(position, existingPositions, minimumSpacingMeters);

    private static Vector3 NormalizeSurfaceNormal(Vector3 surfaceNormal) => VoxelBrushUtility.NormalizeSurfaceNormal(surfaceNormal);

    private static float NextFloat(System.Random random, float minInclusive, float maxInclusive)
        => VoxelBrushUtility.NextFloat(random, minInclusive, maxInclusive);

    private static Material ResolveAutoMaterial(string materialName, Color color, float smoothness)
    {
        string cacheKey = $"{materialName}_{ColorUtility.ToHtmlStringRGBA(color)}_{smoothness:F2}";
        if (AutoMaterials.TryGetValue(cacheKey, out Material cachedMaterial) && cachedMaterial != null)
        {
            return cachedMaterial;
        }

        Material material = ProceduralRenderMaterialUtility.CreateOpaqueMaterial(materialName, color, smoothness, 0f);
        if (material == null)
        {
            return null;
        }

        AutoMaterials[cacheKey] = material;
        return material;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !hasGeneratedStartArea)
        {
            return;
        }

        Gizmos.color = clearingGizmoColor;
        Gizmos.DrawWireSphere(lastClearingCenter, clearingRadiusMeters);

        Gizmos.color = spawnGizmoColor;
        Gizmos.DrawSphere(lastSpawnPosition, 0.35f);

        if ((lastSuggestedLookTarget - lastClearingCenter).sqrMagnitude > 0.01f)
        {
            Gizmos.DrawLine(lastClearingCenter + Vector3.up * 0.35f, lastSuggestedLookTarget + Vector3.up * 0.35f);
        }
    }
}
