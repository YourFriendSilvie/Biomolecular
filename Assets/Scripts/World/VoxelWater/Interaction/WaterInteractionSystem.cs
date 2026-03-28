using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages player interaction with freshwater bodies: harvesting, water-volume queries,
/// excavation-block checks, and hit resolution.
/// </summary>
internal sealed class WaterInteractionSystem
{
    private const float WaterDensityGramsPerCubicMeter = 1000000f;

    private readonly float freshwaterHarvestMassGrams;
    private readonly float minRenderableVolume;
    private readonly LakeHydrologySolver hydrologySolver;
    private readonly Func<string, CompositionInfo> resolveComposition;
    private readonly Func<Transform> getOrCreateRoot;
    private readonly Action<GeneratedLake, Transform> onLakeObjectUpdated;
    private readonly Action<ProceduralVoxelTerrain, GeneratedLake> onLakeInfluenceUpdated;
    private readonly Func<ProceduralVoxelTerrain> resolveTerrain;
    private readonly Action<string> logDebug;

    public WaterInteractionSystem(
        float freshwaterHarvestMassGrams,
        float minRenderableVolume,
        LakeHydrologySolver hydrologySolver,
        Func<string, CompositionInfo> resolveComposition,
        Func<Transform> getOrCreateRoot,
        Action<GeneratedLake, Transform> onLakeObjectUpdated,
        Action<ProceduralVoxelTerrain, GeneratedLake> onLakeInfluenceUpdated,
        Func<ProceduralVoxelTerrain> resolveTerrain,
        Action<string> logDebug)
    {
        this.freshwaterHarvestMassGrams = freshwaterHarvestMassGrams;
        this.minRenderableVolume = minRenderableVolume;
        this.hydrologySolver = hydrologySolver;
        this.resolveComposition = resolveComposition;
        this.getOrCreateRoot = getOrCreateRoot;
        this.onLakeObjectUpdated = onLakeObjectUpdated;
        this.onLakeInfluenceUpdated = onLakeInfluenceUpdated;
        this.resolveTerrain = resolveTerrain;
        this.logDebug = logDebug;
    }

    public bool TryGetHarvestable(
        RaycastHit hit,
        IReadOnlyList<GeneratedLake> lakes,
        IReadOnlyList<GeneratedRiver> rivers,
        out IHarvestable harvestable)
    {
        harvestable = null;
        if (hit.collider == null)
        {
            return false;
        }

        if (TryResolveLakeFromHit(hit, lakes, out GeneratedLake lake))
        {
            harvestable = new FreshWaterHarvestTarget(this, lake);
            return true;
        }

        if (TryResolveRiverFromHit(hit, rivers, out GeneratedRiver river))
        {
            harvestable = new FreshWaterHarvestTarget(this, river);
            return true;
        }

        return false;
    }

    public bool TryAddWaterFromRaycast(
        RaycastHit hit,
        IReadOnlyList<GeneratedLake> lakes,
        float waterMassGrams,
        float pointPaddingMeters = 0.75f)
    {
        if (waterMassGrams <= 0.001f || hit.collider == null)
        {
            return false;
        }

        if (TryResolveLakeFromHit(hit, lakes, out GeneratedLake lake))
        {
            return TryAddWaterToLake(lake, waterMassGrams, $"raycast hit at {hit.point}");
        }

        return TryAddWaterAtPoint(hit.point, lakes, waterMassGrams, pointPaddingMeters);
    }

    public bool TryAddWaterAtPoint(
        Vector3 worldPoint,
        IReadOnlyList<GeneratedLake> lakes,
        float waterMassGrams,
        float pointPaddingMeters = 0.75f)
    {
        if (waterMassGrams <= 0.001f)
        {
            return false;
        }

        if (!WaterSpatialQueryUtility.TryResolveLakeNearPoint(lakes, worldPoint, pointPaddingMeters, minRenderableVolume, out GeneratedLake lake))
        {
            logDebug?.Invoke($"Add water skipped at {worldPoint} because no active lake was found within {Mathf.Max(0f, pointPaddingMeters):F2}m.");
            return false;
        }

        return TryAddWaterToLake(lake, waterMassGrams, $"point {worldPoint}");
    }

    // The lake interaction BoxCollider is expanded beyond the lake surface triangles by up to
    // Mathf.Max(voxelSizeMeters, 0.5f) / 2 metres on each side (see WaterRenderFactory).
    // The surface query padding must be at least this large so that hits anywhere on the
    // collider resolve to a valid lake rather than being silently rejected.
    private const float LakeSurfaceHitQueryPaddingMeters = 1.0f;

    public bool TryResolveLakeFromHit(RaycastHit hit, IReadOnlyList<GeneratedLake> lakes, out GeneratedLake lake)
    {
        lake = null;
        if (hit.collider == null || lakes == null)
        {
            return false;
        }

        Transform hitTransform = hit.collider.transform;
        for (int i = 0; i < lakes.Count; i++)
        {
            GeneratedLake candidate = lakes[i];
            if (!WaterSpatialQueryUtility.IsLakeActive(candidate, minRenderableVolume) ||
                candidate.waterObject == null ||
                !hitTransform.IsChildOf(candidate.waterObject.transform))
            {
                continue;
            }

            if (!WaterSpatialQueryUtility.ContainsPointOnLakeSurface(candidate, hit.point.x, hit.point.z, LakeSurfaceHitQueryPaddingMeters))
            {
                continue;
            }

            lake = candidate;
            return true;
        }

        return false;
    }

    public bool TryResolveRiverFromHit(RaycastHit hit, IReadOnlyList<GeneratedRiver> rivers, out GeneratedRiver river)
    {
        river = null;
        if (hit.collider == null || rivers == null)
        {
            return false;
        }

        Transform hitTransform = hit.collider.transform;
        for (int i = 0; i < rivers.Count; i++)
        {
            GeneratedRiver candidate = rivers[i];
            if (candidate?.waterObject == null || !hitTransform.IsChildOf(candidate.waterObject.transform))
            {
                continue;
            }

            river = candidate;
            return true;
        }

        return false;
    }

    public bool TryGetTerrainExcavationBlockReason(
        Vector3 worldPoint,
        float excavationRadiusMeters,
        IReadOnlyList<GeneratedLake> lakes,
        ProceduralVoxelTerrain terrain,
        out string blockReason)
    {
        blockReason = string.Empty;
        if (terrain == null || lakes == null)
        {
            return false;
        }

        float excavationRadius = Mathf.Max(0f, excavationRadiusMeters);
        float verticalPadding = Mathf.Max(terrain.VoxelSizeMeters * 0.5f, 0.25f);
        float excavationMinY = worldPoint.y - Mathf.Max(excavationRadius, verticalPadding);
        float shorelinePadding = excavationRadius + verticalPadding;
        for (int i = 0; i < lakes.Count; i++)
        {
            GeneratedLake lake = lakes[i];
            if (!WaterSpatialQueryUtility.IsLakeActive(lake, minRenderableVolume) ||
                excavationMinY > lake.surfaceY + verticalPadding)
            {
                continue;
            }

            if (!WaterSpatialQueryUtility.ContainsPointOnLakeSurface(lake, worldPoint.x, worldPoint.z, shorelinePadding))
            {
                continue;
            }

            blockReason = "Shoreline mining is disabled while the nearby lake still contains water. Drain the lake first.";
            return true;
        }

        return false;
    }

    public bool HarvestLake(GeneratedLake lake, Inventory playerInventory)
    {
        if (lake == null || playerInventory == null)
        {
            return false;
        }

        CompositionInfo freshWaterComposition = resolveComposition("Fresh Water");
        if (freshWaterComposition == null)
        {
            return false;
        }

        ProceduralVoxelTerrain terrain = resolveTerrain();
        if (terrain == null || !terrain.HasReadyGameplayTerrain)
        {
            return false;
        }

        float availableMass = ConvertVolumeToMassGrams(lake.storedVolumeCubicMeters);
        float harvestMass = Mathf.Min(freshwaterHarvestMassGrams, availableMass);
        if (harvestMass <= 0.001f)
        {
            logDebug?.Invoke($"Harvest skipped for {WaterDebugUtility.DescribeLake(lake)} because available harvest mass was {harvestMass:F3} g.");
            return false;
        }

        if (!playerInventory.AddItem(
                freshWaterComposition,
                1,
                harvestMass,
                CompositionInfo.CopyComposition(freshWaterComposition.composition)))
        {
            logDebug?.Invoke($"Harvest failed for {WaterDebugUtility.DescribeLake(lake)} because inventory rejected {harvestMass:F0} g.");
            return false;
        }

        float previousSurfaceY = lake.surfaceY;
        float previousStoredVolume = lake.storedVolumeCubicMeters;
        float targetVolume = Mathf.Max(0f, lake.storedVolumeCubicMeters - ConvertMassToVolumeCubicMeters(harvestMass));
        logDebug?.Invoke($"Harvesting {WaterDebugUtility.DescribeLake(lake)}. Starting surfaceY={previousSurfaceY:F3}, stored volume={previousStoredVolume:F3} m^3, harvest mass={harvestMass:F0} g, target volume={targetVolume:F3} m^3.");
        if (!hydrologySolver.TrySolveExistingLakeForStoredVolumeFast(terrain, lake, targetVolume, out LakeTerrainPatch terrainPatch, out LakeSolveResult solveResult))
        {
            logDebug?.Invoke($"Harvest solve failed for {WaterDebugUtility.DescribeLake(lake)} at target volume {targetVolume:F3} m^3.");
            return false;
        }

        lake.terrainPatch = terrainPatch;
        hydrologySolver.ApplyLakeSolveResult(lake, solveResult);
        lake.storedVolumeCubicMeters = targetVolume <= minRenderableVolume
            ? 0f
            : LakeHydrologySolver.ClampLakeStoredVolume(targetVolume, solveResult);
        if (lake.storedVolumeCubicMeters <= minRenderableVolume)
        {
            if (lake.waterObject != null)
            {
                lake.waterObject.SetActive(false);
            }

            logDebug?.Invoke($"Harvest emptied {WaterDebugUtility.DescribeLake(lake)}. Previous surfaceY={previousSurfaceY:F3}, previous volume={previousStoredVolume:F3} m^3.");
            return true;
        }

        logDebug?.Invoke($"Harvest solve succeeded for {WaterDebugUtility.DescribeLake(lake)}. SurfaceY {previousSurfaceY:F3}->{lake.surfaceY:F3}, volume {previousStoredVolume:F3}->{lake.storedVolumeCubicMeters:F3} m^3, solved volume={solveResult.volumeCubicMeters:F3} m^3, triangles={lake.surfaceTriangles.Length / 3}.");
        onLakeInfluenceUpdated?.Invoke(terrain, lake);
        onLakeObjectUpdated?.Invoke(lake, getOrCreateRoot?.Invoke());
        return true;
    }

    public bool HarvestRiver(GeneratedRiver river, Inventory playerInventory)
    {
        if (river == null || playerInventory == null)
        {
            return false;
        }

        CompositionInfo freshWaterComposition = resolveComposition("Fresh Water");
        if (freshWaterComposition == null)
        {
            return false;
        }

        return playerInventory.AddItem(
            freshWaterComposition,
            1,
            freshwaterHarvestMassGrams,
            CompositionInfo.CopyComposition(freshWaterComposition.composition));
    }

    public bool TryAddWaterToLake(GeneratedLake lake, float waterMassGrams, string sourceDescription)
    {
        if (lake == null || waterMassGrams <= 0.001f)
        {
            return false;
        }

        ProceduralVoxelTerrain terrain = resolveTerrain();
        if (terrain == null || !terrain.HasReadyGameplayTerrain)
        {
            return false;
        }

        float previousSurfaceY = lake.surfaceY;
        float previousStoredVolume = lake.storedVolumeCubicMeters;
        float targetVolume = lake.storedVolumeCubicMeters + ConvertMassToVolumeCubicMeters(waterMassGrams);
        if (!hydrologySolver.TrySolveExistingLakeForStoredVolumeFast(terrain, lake, targetVolume, out LakeTerrainPatch terrainPatch, out LakeSolveResult solveResult))
        {
            logDebug?.Invoke($"Add water failed for {WaterDebugUtility.DescribeLake(lake)} from {sourceDescription} at target volume {targetVolume:F3} m^3.");
            return false;
        }

        float overflowTolerance = Mathf.Max(0.01f, terrain.VoxelSizeMeters * terrain.VoxelSizeMeters * 0.01f);
        if (targetVolume > solveResult.volumeCubicMeters + overflowTolerance)
        {
            logDebug?.Invoke(
                $"Add water to {WaterDebugUtility.DescribeLake(lake)} from {sourceDescription} exceeded the static basin capacity. " +
                $"Target volume={targetVolume:F3} m^3, retained single-basin volume={solveResult.volumeCubicMeters:F3} m^3.");
        }

        lake.terrainPatch = terrainPatch;
        hydrologySolver.ApplyLakeSolveResult(lake, solveResult);
        lake.storedVolumeCubicMeters = LakeHydrologySolver.ClampLakeStoredVolume(targetVolume, solveResult);
        logDebug?.Invoke($"Added {waterMassGrams:F0} g of water to {WaterDebugUtility.DescribeLake(lake)} from {sourceDescription}. SurfaceY {previousSurfaceY:F3}->{lake.surfaceY:F3}, volume {previousStoredVolume:F3}->{lake.storedVolumeCubicMeters:F3} m^3, solved volume={solveResult.volumeCubicMeters:F3} m^3, triangles={lake.surfaceTriangles.Length / 3}.");
        onLakeInfluenceUpdated?.Invoke(terrain, lake);
        onLakeObjectUpdated?.Invoke(lake, getOrCreateRoot?.Invoke());
        return true;
    }

    public string GetFreshWaterDisplayName()
    {
        CompositionInfo freshWaterComposition = resolveComposition("Fresh Water");
        return freshWaterComposition != null ? freshWaterComposition.itemName : "Fresh Water";
    }

    public string GetLakeHarvestPreview(GeneratedLake lake)
    {
        if (lake == null)
        {
            return "Nothing to harvest";
        }

        float volumeDisplay = lake.storedVolumeCubicMeters > 0f ? lake.storedVolumeCubicMeters : 0f;
        float harvestMass = Mathf.Min(freshwaterHarvestMassGrams, ConvertVolumeToMassGrams(volumeDisplay));
        return GetFreshWaterHarvestPreview(harvestMass, volumeDisplay);
    }

    public string GetFreshWaterHarvestPreview(float harvestMass, float volumeCubicMeters)
    {
        CompositionInfo freshWaterComposition = resolveComposition("Fresh Water");
        string name = freshWaterComposition != null ? freshWaterComposition.itemName : "Fresh Water";
        if (volumeCubicMeters > 0f)
        {
            return $"{name}\nYield: {harvestMass:F0} g\nVolume: {volumeCubicMeters:F1} m³";
        }

        return $"{name}\nYield: {harvestMass:F0} g";
    }

    private static float ConvertMassToVolumeCubicMeters(float massGrams)
    {
        return Mathf.Max(0f, massGrams) / WaterDensityGramsPerCubicMeter;
    }

    private static float ConvertVolumeToMassGrams(float volumeCubicMeters)
    {
        return Mathf.Max(0f, volumeCubicMeters) * WaterDensityGramsPerCubicMeter;
    }

    private sealed class FreshWaterHarvestTarget : IHarvestable
    {
        private readonly WaterInteractionSystem owner;
        private readonly GeneratedLake lake;
        private readonly GeneratedRiver river;

        public FreshWaterHarvestTarget(WaterInteractionSystem owner, GeneratedLake lake)
        {
            this.owner = owner;
            this.lake = lake;
        }

        public FreshWaterHarvestTarget(WaterInteractionSystem owner, GeneratedRiver river)
        {
            this.owner = owner;
            this.river = river;
        }

        public bool Harvest(Inventory playerInventory)
        {
            if (owner == null)
            {
                return false;
            }

            if (lake != null)
            {
                return owner.HarvestLake(lake, playerInventory);
            }

            if (river != null)
            {
                return owner.HarvestRiver(river, playerInventory);
            }

            return false;
        }

        public string GetHarvestDisplayName()
        {
            return owner != null ? owner.GetFreshWaterDisplayName() : "Fresh Water";
        }

        public string GetHarvestPreview()
        {
            if (owner == null)
            {
                return "Nothing to harvest";
            }

            if (lake != null)
            {
                return owner.GetLakeHarvestPreview(lake);
            }

            if (river != null)
            {
                return owner.GetFreshWaterHarvestPreview(owner.freshwaterHarvestMassGrams, 0f);
            }

            return "Nothing to harvest";
        }
    }
}
