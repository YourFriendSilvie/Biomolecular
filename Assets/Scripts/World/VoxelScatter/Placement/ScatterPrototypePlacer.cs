using System;
using System.Collections.Generic;
using UnityEngine;

internal static class ScatterPrototypePlacer
{
    private const int ProgressUpdateAttemptInterval = 32;
    private static readonly Dictionary<string, Material> AutoMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);

    /// <summary>
    /// Places all instances of a single scatter prototype onto the terrain.
    /// </summary>
    /// <param name="random">Seeded RNG shared across the scatter pass.</param>
    /// <param name="terrain">The live voxel terrain used for surface sampling.</param>
    /// <param name="bounds">World-space bounds of the gameplay terrain.</param>
    /// <param name="resolvedWaterSystem">Optional water system; candidates under water are rejected.</param>
    /// <param name="generatedRoot">Parent transform that receives the spawned objects.</param>
    /// <param name="prototype">Prototype configuration for this placement pass.</param>
    /// <param name="resolvedComposition">Composition to assign to each harvested object.</param>
    /// <param name="createdObjects">Collection that receives each successfully placed GameObject.</param>
    /// <param name="prototypeIndex">Zero-based index of this prototype in the full prototype list.</param>
    /// <param name="totalPrototypeCount">Total number of prototypes in the scatter pass (used for progress).</param>
    /// <param name="waterExclusionPaddingMeters">Extra vertical padding applied to water-exclusion checks.</param>
    /// <param name="gameObjectLayer">Unity layer assigned to spawned GameObjects.</param>
    /// <param name="ownerName">Display name of the owning MonoBehaviour (for warning messages).</param>
    /// <param name="reportProgressCallback">Invoked with (statusText, progress01) whenever progress changes.</param>
    /// <param name="computePrototypeProgress">
    ///     Given the active prototype's internal progress in [0,1], returns the overall scatter progress in [0,1].
    ///     The facade captures <c>prototypeIndex</c> and <c>totalPrototypeCount</c> in this delegate so the
    ///     static class does not need to know about setup work-unit counts.
    /// </param>
    /// <param name="metrics">Optional per-prototype timing/count metrics; may be null.</param>
    /// <returns>Number of instances that were successfully placed.</returns>
    public static int PlacePrototypeInstances(
        System.Random random,
        ProceduralVoxelTerrain terrain,
        Bounds bounds,
        ProceduralVoxelTerrainWaterSystem resolvedWaterSystem,
        Transform generatedRoot,
        TerrainScatterPrototype prototype,
        CompositionInfo resolvedComposition,
        ICollection<GameObject> createdObjects,
        int prototypeIndex,
        int totalPrototypeCount,
        float waterExclusionPaddingMeters,
        int gameObjectLayer,
        string ownerName,
        Action<string, float> reportProgressCallback,
        Func<float, float> computePrototypeProgress,
        PrototypeGenerationMetrics metrics)
    {
        bool collectTimings = metrics != null;
        long prototypeTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
        int effectiveSpawnCount = prototype.ComputeEffectiveSpawnCount(terrain, bounds);
        List<Vector3> placedPositions = new List<Vector3>(effectiveSpawnCount);
        int placedCount = 0;
        int attempts = 0;
        int baseAttemptBudget = effectiveSpawnCount * prototype.maxPlacementAttemptsPerInstance;
        int maxAttempts = SpatialPlacementSolver.CalculateAdaptiveMaxAttempts(prototype, bounds, effectiveSpawnCount);
        float densityOffsetX = SpatialPlacementSolver.NextFloat(random, -10000f, 10000f);
        float densityOffsetZ = SpatialPlacementSolver.NextFloat(random, -10000f, 10000f);
        float scatterOffsetX = SpatialPlacementSolver.NextFloat(random, 0f, 1f);
        float scatterOffsetZ = SpatialPlacementSolver.NextFloat(random, 0f, 1f);
        int scatterSequenceStartIndex = random.Next(1, 4096);

        if (collectTimings)
        {
            metrics.baseAttemptBudget = baseAttemptBudget;
            metrics.maxAttempts = maxAttempts;
        }

        reportProgressCallback(
            BuildPrototypeProgressStatus(prototype, prototypeIndex, totalPrototypeCount, placedCount, attempts, maxAttempts, effectiveSpawnCount),
            computePrototypeProgress(0f));

        void ReportPrototypeProgress(bool force)
        {
            if (!force &&
                attempts > 0 &&
                attempts != maxAttempts &&
                (attempts % ProgressUpdateAttemptInterval) != 0)
            {
                return;
            }

            reportProgressCallback(
                BuildPrototypeProgressStatus(prototype, prototypeIndex, totalPrototypeCount, placedCount, attempts, maxAttempts, effectiveSpawnCount),
                computePrototypeProgress(CalculatePrototypeProgress01(effectiveSpawnCount, placedCount, attempts, maxAttempts)));
        }

        while (placedCount < effectiveSpawnCount && attempts < maxAttempts)
        {
            attempts++;
            float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, SpatialPlacementSolver.NextScatterSample01(scatterSequenceStartIndex + attempts, 2, scatterOffsetX));
            float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, SpatialPlacementSolver.NextScatterSample01(scatterSequenceStartIndex + attempts, 3, scatterOffsetZ));

            long stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            bool usedCachedSurface = terrain.TryGetCachedSurfacePointWorld(
                worldX,
                worldZ,
                out Vector3 screeningSurfacePoint,
                out Vector3 screeningSurfaceNormal);
            if (collectTimings)
            {
                if (usedCachedSurface)
                {
                    metrics.cachedScreeningSamples++;
                }
                else
                {
                    metrics.liveScreeningFallbackSamples++;
                }
            }
            if (!usedCachedSurface)
            {
                if (!terrain.TrySampleSurfaceWorld(worldX, worldZ, out RaycastHit fallbackHit))
                {
                    if (collectTimings)
                    {
                        ScatterTimingUtility.EndTiming(ref metrics.screeningSurfaceTicks, stepTimingStart);
                        metrics.rejectedScreeningSurfaceMiss++;
                    }
                    ReportPrototypeProgress(false);
                    continue;
                }

                screeningSurfacePoint = fallbackHit.point;
                screeningSurfaceNormal = fallbackHit.normal;
            }
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref metrics.screeningSurfaceTicks, stepTimingStart);
            }

            screeningSurfaceNormal = SpatialPlacementSolver.NormalizeSurfaceNormal(screeningSurfaceNormal);
            stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            if (!SpatialPlacementSolver.IsWithinPrototypeSurfaceConstraints(screeningSurfacePoint, screeningSurfaceNormal, prototype, bounds))
            {
                if (collectTimings)
                {
                    ScatterTimingUtility.EndTiming(ref metrics.surfaceConstraintTicks, stepTimingStart);
                    metrics.rejectedScreeningConstraints++;
                }
                ReportPrototypeProgress(false);
                continue;
            }
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref metrics.surfaceConstraintTicks, stepTimingStart);
            }

            stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            float density = Mathf.PerlinNoise(
                (screeningSurfacePoint.x + densityOffsetX) / prototype.densityNoiseScale,
                (screeningSurfacePoint.z + densityOffsetZ) / prototype.densityNoiseScale);
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref metrics.densityTicks, stepTimingStart);
            }
            if (density < prototype.densityThreshold)
            {
                if (collectTimings)
                {
                    metrics.rejectedDensity++;
                }
                ReportPrototypeProgress(false);
                continue;
            }

            stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            if (!SpatialPlacementSolver.IsFarEnoughFromExisting(screeningSurfacePoint, placedPositions, prototype.minimumSpacingMeters))
            {
                if (collectTimings)
                {
                    ScatterTimingUtility.EndTiming(ref metrics.spacingTicks, stepTimingStart);
                    metrics.rejectedSpacing++;
                }
                ReportPrototypeProgress(false);
                continue;
            }
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref metrics.spacingTicks, stepTimingStart);
            }

            Vector3 surfacePoint = screeningSurfacePoint;
            Vector3 surfaceNormal = screeningSurfaceNormal;
            if (usedCachedSurface)
            {
                // Cheap cached screening keeps most attempts off the physics path, but the final placement
                // still snaps to the live terrain surface so post-carve scatter stays glued to the mesh.
                stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
                if (!terrain.TrySampleSurfaceWorld(worldX, worldZ, out RaycastHit hit))
                {
                    if (collectTimings)
                    {
                        ScatterTimingUtility.EndTiming(ref metrics.liveSurfaceTicks, stepTimingStart);
                        metrics.rejectedLiveSurfaceMiss++;
                    }
                    ReportPrototypeProgress(false);
                    continue;
                }

                surfacePoint = hit.point;
                surfaceNormal = SpatialPlacementSolver.NormalizeSurfaceNormal(hit.normal);
                if (collectTimings)
                {
                    ScatterTimingUtility.EndTiming(ref metrics.liveSurfaceTicks, stepTimingStart);
                }
            }

            if (usedCachedSurface)
            {
                stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
                if (!SpatialPlacementSolver.IsFarEnoughFromExisting(surfacePoint, placedPositions, prototype.minimumSpacingMeters))
                {
                    if (collectTimings)
                    {
                        ScatterTimingUtility.EndTiming(ref metrics.spacingTicks, stepTimingStart);
                        metrics.rejectedSpacing++;
                    }
                    ReportPrototypeProgress(false);
                    continue;
                }

                if (collectTimings)
                {
                    ScatterTimingUtility.EndTiming(ref metrics.spacingTicks, stepTimingStart);
                }
            }

            stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            if (!SpatialPlacementSolver.IsWithinPrototypeSurfaceConstraints(surfacePoint, surfaceNormal, prototype, bounds))
            {
                if (collectTimings)
                {
                    ScatterTimingUtility.EndTiming(ref metrics.surfaceConstraintTicks, stepTimingStart);
                    metrics.rejectedLiveConstraints++;
                }
                ReportPrototypeProgress(false);
                continue;
            }
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref metrics.surfaceConstraintTicks, stepTimingStart);
            }

            stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            if (resolvedWaterSystem != null && resolvedWaterSystem.IsPointUnderWater(surfacePoint, waterExclusionPaddingMeters))
            {
                if (collectTimings)
                {
                    ScatterTimingUtility.EndTiming(ref metrics.waterTicks, stepTimingStart);
                    metrics.rejectedWater++;
                }
                ReportPrototypeProgress(false);
                continue;
            }
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref metrics.waterTicks, stepTimingStart);
            }

            stepTimingStart = ScatterTimingUtility.BeginTiming(collectTimings);
            GameObject placeholder = GameObject.CreatePrimitive(prototype.primitiveType);
            placeholder.name = $"{prototype.ResolveDisplayName()}_{placedCount + 1:000}";
            placeholder.layer = gameObjectLayer;
            placeholder.transform.SetParent(generatedRoot, true);

            Vector3 scale = SpatialPlacementSolver.NextVector3(random, prototype.minScale, prototype.maxScale);
            Vector3 upAxis = prototype.alignToSurfaceNormal ? surfaceNormal : Vector3.up;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, upAxis);
            if (prototype.randomizeYaw)
            {
                rotation = Quaternion.AngleAxis(SpatialPlacementSolver.NextFloat(random, 0f, 360f), upAxis) * rotation;
            }

            float halfHeight = SpatialPlacementSolver.GetPrimitiveHalfHeight(prototype.primitiveType, scale);
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
                SpatialPlacementSolver.NextFloat(random, prototype.totalMassRangeGrams.x, prototype.totalMassRangeGrams.y),
                prototype.harvestEfficiency,
                prototype.destroyOnHarvest,
                prototype.harvestsRequired,
                prototype.randomizeCompositionOnStart);

            createdObjects?.Add(placeholder);
            placedPositions.Add(surfacePoint);
            placedCount++;
            if (collectTimings)
            {
                ScatterTimingUtility.EndTiming(ref metrics.instantiationTicks, stepTimingStart);
                metrics.acceptedCount = placedCount;
                metrics.attempts = attempts;
            }
            ReportPrototypeProgress(true);
        }

        if (collectTimings)
        {
            metrics.totalTicks = ScatterTimingUtility.GetElapsedTicks(prototypeTimingStart);
            metrics.acceptedCount = placedCount;
            metrics.attempts = attempts;
        }

        if (placedCount < effectiveSpawnCount)
        {
            Debug.LogWarning(
                $"{ownerName} only placed {placedCount} of {effectiveSpawnCount} requested voxel instances for {prototype.ResolveDisplayName()} after {attempts} of {maxAttempts} attempts.");
        }

        return placedCount;
    }

    private static Material ResolveRenderMaterial(TerrainScatterPrototype prototype)
    {
        if (prototype != null && ProceduralRenderMaterialUtility.CanUseAssignedMaterial(prototype.material))
        {
            return prototype.material;
        }

        Color colorTint = prototype != null ? prototype.colorTint : Color.white;
        string displayName = prototype != null ? prototype.ResolveDisplayName() : "Voxel Placeholder";
        string cacheKey = $"{displayName}_{ColorUtility.ToHtmlStringRGBA(colorTint)}";
        if (AutoMaterials.TryGetValue(cacheKey, out Material cachedMaterial) && cachedMaterial != null)
        {
            return cachedMaterial;
        }

        Material material = ProceduralRenderMaterialUtility.CreateOpaqueMaterial(
            $"{displayName} Auto Material",
            colorTint,
            0.18f,
            0f);
        if (material == null)
        {
            return null;
        }

        AutoMaterials[cacheKey] = material;
        return material;
    }

    private static float CalculatePrototypeProgress01(int requestedCount, int placedCount, int attempts, int maxAttempts)
    {
        float placementProgress = requestedCount <= 0
            ? 1f
            : Mathf.Clamp01(placedCount / (float)Mathf.Max(1, requestedCount));
        float attemptProgress = maxAttempts <= 0
            ? 1f
            : Mathf.Clamp01(attempts / (float)Mathf.Max(1, maxAttempts));
        return Mathf.Max(placementProgress, attemptProgress);
    }

    private static string BuildPrototypeProgressStatus(
        TerrainScatterPrototype prototype,
        int prototypeIndex,
        int totalPrototypeCount,
        int placedCount,
        int attempts,
        int maxAttempts,
        int effectiveSpawnCount)
    {
        string prototypeName = prototype != null ? prototype.ResolveDisplayName() : "Scatter Prototype";
        return $"Scatter generation: prototype {prototypeIndex + 1}/{Mathf.Max(1, totalPrototypeCount)} {prototypeName} ({placedCount}/{effectiveSpawnCount}) attempts {attempts}/{Mathf.Max(1, maxAttempts)}";
    }
}
