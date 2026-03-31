using System.Collections.Generic;
using UnityEngine;

internal static class StartAreaGenerator
{
    private static readonly Dictionary<string, Material> AutoMaterials = new Dictionary<string, Material>(System.StringComparer.Ordinal);

    // -------------------------------------------------------------------------
    // Candidate search (public entry point – tries with freshwater first, then
    // falls back to a search that relaxes the freshwater requirement).
    // -------------------------------------------------------------------------

    public static bool TryFindBestCandidate(
        System.Random random,
        IReadOnlyList<Vector3> scatterPositions,
        ProceduralVoxelTerrain terrain,
        ProceduralVoxelTerrainWaterSystem waterSystem,
        int candidateSamples,
        float edgePaddingMeters,
        float maxSlopeDegrees,
        Vector2 freshwaterDistanceRangeMeters,
        float preferredFreshwaterDistanceMeters,
        float localPatchCheckRadiusMeters,
        float maxPatchHeightVariationMeters,
        float clearingRadiusMeters,
        out StartAreaCandidate bestCandidate)
    {
        if (TryFindBestCandidate(
            random, scatterPositions, terrain, waterSystem,
            candidateSamples, edgePaddingMeters, maxSlopeDegrees,
            freshwaterDistanceRangeMeters, preferredFreshwaterDistanceMeters,
            localPatchCheckRadiusMeters, maxPatchHeightVariationMeters,
            clearingRadiusMeters, requireFreshwater: true, out bestCandidate))
        {
            return true;
        }

        return TryFindBestCandidate(
            random, scatterPositions, terrain, waterSystem,
            candidateSamples, edgePaddingMeters, maxSlopeDegrees,
            freshwaterDistanceRangeMeters, preferredFreshwaterDistanceMeters,
            localPatchCheckRadiusMeters, maxPatchHeightVariationMeters,
            clearingRadiusMeters, requireFreshwater: false, out bestCandidate);
    }

    private static bool TryFindBestCandidate(
        System.Random random,
        IReadOnlyList<Vector3> scatterPositions,
        ProceduralVoxelTerrain terrain,
        ProceduralVoxelTerrainWaterSystem waterSystem,
        int candidateSamples,
        float edgePaddingMeters,
        float maxSlopeDegrees,
        Vector2 freshwaterDistanceRangeMeters,
        float preferredFreshwaterDistanceMeters,
        float localPatchCheckRadiusMeters,
        float maxPatchHeightVariationMeters,
        float clearingRadiusMeters,
        bool requireFreshwater,
        out StartAreaCandidate bestCandidate)
    {
        bestCandidate = default;
        float bestScore = float.MinValue;
        if (!terrain.TryGetGameplayBounds(out Bounds bounds))
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
            float worldX = VoxelBrushUtility.NextFloat(random, minX, maxX);
            float worldZ = VoxelBrushUtility.NextFloat(random, minZ, maxZ);
            bool usedCachedSurface = terrain.TryGetCachedSurfacePointWorld(
                worldX,
                worldZ,
                out Vector3 screeningCenter,
                out Vector3 screeningNormal);
            RaycastHit hit = default;
            if (!usedCachedSurface)
            {
                if (!terrain.TrySampleSurfaceWorld(worldX, worldZ, out hit))
                {
                    continue;
                }

                screeningCenter = hit.point;
                screeningNormal = hit.normal;
            }

            screeningNormal = VoxelBrushUtility.NormalizeSurfaceNormal(screeningNormal);
            if (Vector3.Angle(screeningNormal, Vector3.up) > maxSlopeDegrees)
            {
                continue;
            }

            if (!TryEvaluateLocalPatchScreening(
                screeningCenter,
                usedCachedSurface,
                terrain,
                waterSystem,
                localPatchCheckRadiusMeters,
                maxPatchHeightVariationMeters,
                maxSlopeDegrees,
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
                if (!terrain.TrySampleSurfaceWorld(worldX, worldZ, out hit))
                {
                    continue;
                }

                center = hit.point;
                surfaceNormal = VoxelBrushUtility.NormalizeSurfaceNormal(hit.normal);
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
                !TryEvaluateLocalPatch(center, terrain, waterSystem, localPatchCheckRadiusMeters, maxPatchHeightVariationMeters, maxSlopeDegrees, out heightVariation, out averageSlope))
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
            float shelterScore = EvaluateShelterScore(center, scatterPositions, clearingRadiusMeters);
            float edgeScore = VoxelBrushUtility.EvaluateEdgeScore(center, bounds);
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

    // -------------------------------------------------------------------------
    // Local patch evaluation helpers
    // -------------------------------------------------------------------------

    private static bool TryEvaluateLocalPatch(
        Vector3 center,
        ProceduralVoxelTerrain terrain,
        ProceduralVoxelTerrainWaterSystem waterSystem,
        float localPatchCheckRadiusMeters,
        float maxPatchHeightVariationMeters,
        float maxSlopeDegrees,
        out float heightVariation,
        out float averageSlope)
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
            if (!terrain.TrySampleSurfaceWorld(center.x + offset.x, center.z + offset.z, out RaycastHit hit))
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

    private static bool TryEvaluateLocalPatchScreening(
        Vector3 center,
        bool usedCachedSurface,
        ProceduralVoxelTerrain terrain,
        ProceduralVoxelTerrainWaterSystem waterSystem,
        float localPatchCheckRadiusMeters,
        float maxPatchHeightVariationMeters,
        float maxSlopeDegrees,
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
            return TryEvaluateLocalPatch(center, terrain, waterSystem, localPatchCheckRadiusMeters, maxPatchHeightVariationMeters, maxSlopeDegrees, out heightVariation, out averageSlope);
        }

        if (TryEvaluateLocalPatchCached(center, terrain, localPatchCheckRadiusMeters, maxPatchHeightVariationMeters, maxSlopeDegrees, out heightVariation, out averageSlope, out bool hadCompleteCachedPatch))
        {
            return true;
        }

        if (hadCompleteCachedPatch)
        {
            return false;
        }

        // A sparse cached prepass should fall back to live sampling instead of rejecting a valid start area.
        usedLivePatchEvaluation = true;
        return TryEvaluateLocalPatch(center, terrain, waterSystem, localPatchCheckRadiusMeters, maxPatchHeightVariationMeters, maxSlopeDegrees, out heightVariation, out averageSlope);
    }

    private static bool TryEvaluateLocalPatchCached(
        Vector3 center,
        ProceduralVoxelTerrain terrain,
        float localPatchCheckRadiusMeters,
        float maxPatchHeightVariationMeters,
        float maxSlopeDegrees,
        out float heightVariation,
        out float averageSlope,
        out bool hadCompleteCachedPatch)
    {
        heightVariation = 0f;
        averageSlope = 0f;
        hadCompleteCachedPatch = false;
        if (terrain == null)
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
            if (!terrain.TryGetCachedSurfacePointWorld(center.x + offset.x, center.z + offset.z, out Vector3 samplePoint, out Vector3 sampleNormal))
            {
                return false;
            }

            minHeight = Mathf.Min(minHeight, samplePoint.y);
            maxHeight = Mathf.Max(maxHeight, samplePoint.y);
            slopeSum += Vector3.Angle(VoxelBrushUtility.NormalizeSurfaceNormal(sampleNormal), Vector3.up);
            sampleCount++;
        }

        hadCompleteCachedPatch = true;
        heightVariation = maxHeight - minHeight;
        averageSlope = sampleCount <= 0 ? 0f : slopeSum / sampleCount;
        return heightVariation <= maxPatchHeightVariationMeters && averageSlope <= maxSlopeDegrees;
    }

    private static float EvaluateShelterScore(Vector3 center, IReadOnlyList<Vector3> scatterPositions, float clearingRadiusMeters)
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

    // -------------------------------------------------------------------------
    // Scatter helpers
    // -------------------------------------------------------------------------

    public static List<Vector3> GatherScatterPositions(ProceduralVoxelTerrainScatterer scatterer)
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

    public static int RemoveScatterWithinRadius(ProceduralVoxelTerrainScatterer scatterer, Vector3 center, float radiusMeters)
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
            if (UnityEngine.Application.isPlaying)
            {
                toRemove[i].SetActive(false);
                UnityEngine.Object.Destroy(toRemove[i]);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(toRemove[i]);
            }
        }

        return toRemove.Count;
    }

    // -------------------------------------------------------------------------
    // Landmark creation
    // -------------------------------------------------------------------------

    public static void CreateLandmarks(
        System.Random random,
        StartAreaCandidate candidate,
        Transform generatedRoot,
        ProceduralVoxelTerrain terrain,
        ProceduralVoxelTerrainWaterSystem waterSystem,
        int landmarkCount,
        float landmarkRingRadiusMeters,
        int layer,
        Color boulderColor,
        Color logColor,
        Color snagColor)
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
            float angle = baseYaw + angleOffsets[i % angleOffsets.Length] + VoxelBrushUtility.NextFloat(random, -16f, 16f);
            float radius = landmarkRingRadiusMeters + VoxelBrushUtility.NextFloat(random, -1.2f, 1.2f);
            Vector3 samplePosition = candidate.center + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
            if (!terrain.TrySampleSurfaceWorld(samplePosition.x, samplePosition.z, out RaycastHit hit))
            {
                continue;
            }

            if (waterSystem != null && waterSystem.IsPointUnderWater(hit.point, 0.35f))
            {
                continue;
            }

            if (!VoxelBrushUtility.IsFarEnoughFromPlaced(hit.point, placedPositions, 3.2f))
            {
                continue;
            }

            switch (i % 3)
            {
                case 0:
                    CreateBoulderLandmark(hit.point, hit.normal, generatedRoot, random, i, layer, boulderColor);
                    break;

                case 1:
                    CreateFallenLogLandmark(hit.point, hit.normal, generatedRoot, random, i, layer, logColor, waterDirection);
                    break;

                default:
                    CreateSnagLandmark(hit.point, hit.normal, generatedRoot, random, i, layer, snagColor);
                    break;
            }

            placedPositions.Add(hit.point);
        }
    }

    private static void CreateBoulderLandmark(Vector3 point, Vector3 normal, Transform generatedRoot, System.Random random, int index, int layer, Color boulderColor)
    {
        GameObject boulder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        boulder.name = $"Start Area Boulder {index + 1:00}";
        boulder.layer = layer;
        boulder.transform.SetParent(generatedRoot, true);
        Vector3 scale = new Vector3(
            VoxelBrushUtility.NextFloat(random, 1.8f, 2.8f),
            VoxelBrushUtility.NextFloat(random, 1.2f, 2f),
            VoxelBrushUtility.NextFloat(random, 1.6f, 2.6f));
        boulder.transform.localScale = scale;
        boulder.transform.position = point + (normal.normalized * (scale.y * 0.32f));
        boulder.transform.rotation = Quaternion.Euler(
            VoxelBrushUtility.NextFloat(random, 0f, 360f),
            VoxelBrushUtility.NextFloat(random, 0f, 360f),
            VoxelBrushUtility.NextFloat(random, 0f, 360f));

        MeshRenderer renderer = boulder.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = ResolveAutoMaterial("Start Area Boulder", boulderColor, 0.28f);
        }
    }

    private static void CreateFallenLogLandmark(Vector3 point, Vector3 normal, Transform generatedRoot, System.Random random, int index, int layer, Color logColor, Vector3 waterDirection)
    {
        GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        log.name = $"Start Area Log {index + 1:00}";
        log.layer = layer;
        log.transform.SetParent(generatedRoot, true);
        float radius = VoxelBrushUtility.NextFloat(random, 0.28f, 0.46f);
        float halfLength = VoxelBrushUtility.NextFloat(random, 2.4f, 3.6f);
        log.transform.localScale = new Vector3(radius, halfLength, radius);

        Vector3 logDirection = waterDirection.sqrMagnitude > 0.0001f
            ? Vector3.Cross(Vector3.up, waterDirection.normalized)
            : new Vector3(Mathf.Cos(index), 0f, Mathf.Sin(index)).normalized;
        if (logDirection.sqrMagnitude <= 0.0001f)
        {
            logDirection = Vector3.right;
        }

        log.transform.rotation = Quaternion.FromToRotation(Vector3.up, logDirection.normalized) * Quaternion.AngleAxis(VoxelBrushUtility.NextFloat(random, -12f, 12f), logDirection.normalized);
        log.transform.position = point + (normal.normalized * (radius + 0.05f));

        MeshRenderer renderer = log.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = ResolveAutoMaterial("Start Area Log", logColor, 0.18f);
        }
    }

    private static void CreateSnagLandmark(Vector3 point, Vector3 normal, Transform generatedRoot, System.Random random, int index, int layer, Color snagColor)
    {
        GameObject snag = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        snag.name = $"Start Area Snag {index + 1:00}";
        snag.layer = layer;
        snag.transform.SetParent(generatedRoot, true);
        float radius = VoxelBrushUtility.NextFloat(random, 0.32f, 0.55f);
        float halfHeight = VoxelBrushUtility.NextFloat(random, 2.1f, 3.8f);
        snag.transform.localScale = new Vector3(radius, halfHeight, radius);
        snag.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal.normalized) * Quaternion.AngleAxis(VoxelBrushUtility.NextFloat(random, 0f, 360f), normal.normalized);
        snag.transform.position = point + (normal.normalized * halfHeight);

        MeshRenderer renderer = snag.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = ResolveAutoMaterial("Start Area Snag", snagColor, 0.14f);
        }
    }

    // -------------------------------------------------------------------------
    // Material cache
    // -------------------------------------------------------------------------

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
}
