using System;
using System.Collections.Generic;
using UnityEngine;
using static WaterMathUtility;

internal sealed class WaterDeformationUpdater
{
    private readonly List<GeneratedLake> generatedLakes;
    private readonly List<GeneratedRiver> generatedRivers;
    private readonly List<GeneratedRiverSegment> generatedRiverSegments;
    private readonly LakeHydrologySolver hydrologySolver;
    private readonly float minRenderableVolumeCubicMeters;
    private readonly float lakeDepthMeters;
    private readonly float riverDepthMeters;
    private readonly float lakeDynamicExpansionMeters;
    private readonly float waterUpdatePaddingMeters;
    private readonly float terrainRefreshDebounceSeconds;
    private readonly float seaLevelMeters;
    private readonly Func<Transform> ensureGeneratedRoot;
    private readonly Action<GeneratedLake, Transform> createLakeObject;
    private readonly Action<GeneratedRiver, Transform, int> createRiverObject;
    private readonly Action<string> logDebug;
    private readonly WaterUpdateTickManager terrainRefreshQueue = new WaterUpdateTickManager();

    public WaterDeformationUpdater(
        List<GeneratedLake> generatedLakes,
        List<GeneratedRiver> generatedRivers,
        List<GeneratedRiverSegment> generatedRiverSegments,
        LakeHydrologySolver hydrologySolver,
        float minRenderableVolumeCubicMeters,
        float lakeDepthMeters,
        float riverDepthMeters,
        float lakeDynamicExpansionMeters,
        float waterUpdatePaddingMeters,
        float terrainRefreshDebounceSeconds,
        float seaLevelMeters,
        Func<Transform> ensureGeneratedRoot,
        Action<GeneratedLake, Transform> createLakeObject,
        Action<GeneratedRiver, Transform, int> createRiverObject,
        Action<string> logDebug)
    {
        this.generatedLakes = generatedLakes ?? new List<GeneratedLake>();
        this.generatedRivers = generatedRivers ?? new List<GeneratedRiver>();
        this.generatedRiverSegments = generatedRiverSegments ?? new List<GeneratedRiverSegment>();
        this.hydrologySolver = hydrologySolver;
        this.minRenderableVolumeCubicMeters = minRenderableVolumeCubicMeters;
        this.lakeDepthMeters = lakeDepthMeters;
        this.riverDepthMeters = riverDepthMeters;
        this.lakeDynamicExpansionMeters = lakeDynamicExpansionMeters;
        this.waterUpdatePaddingMeters = waterUpdatePaddingMeters;
        this.terrainRefreshDebounceSeconds = terrainRefreshDebounceSeconds;
        this.seaLevelMeters = seaLevelMeters;
        this.ensureGeneratedRoot = ensureGeneratedRoot;
        this.createLakeObject = createLakeObject;
        this.createRiverObject = createRiverObject;
        this.logDebug = logDebug;
    }

    public void ClearPendingTerrainRefresh()
    {
        terrainRefreshQueue.Clear();
    }

    public void HandleTerrainGeometryChanged(VoxelTerrainGeometryChangedEventArgs changeArgs)
    {
        int changedChunkCount = changeArgs.AffectedChunkCoordinates != null ? changeArgs.AffectedChunkCoordinates.Count : 0;
        LogLakeDebug($"GeometryChanged event received. Bounds center={changeArgs.ChangedWorldBounds.center}, size={changeArgs.ChangedWorldBounds.size}, chunks={changedChunkCount}.");
        EnqueueTerrainRefresh(changeArgs.ChangedWorldBounds, changeArgs.AffectedChunkCoordinates);
    }

    public void EnqueueTerrainRefresh(Bounds changedBounds, IReadOnlyCollection<Vector3Int> changedChunkCoordinates)
    {
        terrainRefreshQueue.Enqueue(changedBounds, changedChunkCoordinates, terrainRefreshDebounceSeconds);
    }

    public bool TryConsumePendingRefresh(out Bounds changedBounds, out List<Vector3Int> changedChunks)
    {
        return terrainRefreshQueue.TryConsumeReady(out changedBounds, out changedChunks);
    }

    public void RefreshWaterForChangedBounds(
        ProceduralVoxelTerrain terrain,
        Bounds changedBounds,
        IReadOnlyCollection<Vector3Int> changedChunkCoordinates)
    {
        if (terrain == null)
        {
            LogLakeDebug("RefreshWaterForChangedBounds aborted because voxel terrain is unavailable.");
            return;
        }

        if (terrainRefreshQueue.IsDuplicateRefresh(changedBounds))
        {
            LogLakeDebug($"RefreshWaterForChangedBounds skipped duplicate bounds at frame {Time.frameCount}. Center={changedBounds.center}, size={changedBounds.size}.");
            return;
        }

        terrainRefreshQueue.MarkProcessed(changedBounds);

        Bounds refreshBounds = changedBounds;
        if (changedChunkCoordinates == null || changedChunkCoordinates.Count == 0)
        {
            refreshBounds.Expand(Mathf.Max(terrain.ChunkWorldSizeMeters, waterUpdatePaddingMeters * 2f));
        }
        else
        {
            refreshBounds.Expand(Mathf.Max(terrain.VoxelSizeMeters, waterUpdatePaddingMeters * 2f));
        }

        LogLakeDebug($"RefreshWaterForChangedBounds processing bounds center={refreshBounds.center}, size={refreshBounds.size}, chunks={(changedChunkCoordinates != null ? changedChunkCoordinates.Count : 0)}.");
        if (generatedRivers.Count == 0)
        {
            LogLakeDebug("RefreshWaterForChangedBounds skipped lake deformation updates because active lakes now stay static until drained.");
            return;
        }

        Transform generatedRoot = ensureGeneratedRoot != null ? ensureGeneratedRoot() : null;
        bool riversUpdated = RefreshAffectedRivers(terrain, generatedRoot, refreshBounds);
        LogLakeDebug($"RefreshWaterForChangedBounds finished. Lakes updated=False, rivers updated={riversUpdated}.");
        if (riversUpdated)
        {
            FreshwaterGenerator.RebuildRiverSegmentCache(generatedRivers, generatedRiverSegments);
        }
    }

    public void UpdateLakeInfluenceBounds(ProceduralVoxelTerrain terrain, GeneratedLake lake)
    {
        if (terrain == null || lake == null)
        {
            return;
        }

        float horizontalExtent = lake.captureRadius;
        Vector3 boundsCenter = new Vector3(lake.center.x, terrain.WorldBounds.center.y, lake.center.z);
        if (WaterSpatialQueryUtility.HasLakeSurfaceGeometry(lake))
        {
            horizontalExtent = Mathf.Max(
                horizontalExtent,
                Mathf.Max(lake.surfaceBounds.extents.x, lake.surfaceBounds.extents.z) + Mathf.Max(terrain.VoxelSizeMeters, 0.25f));
            boundsCenter.x = lake.surfaceBounds.center.x;
            boundsCenter.z = lake.surfaceBounds.center.z;
        }

        lake.influenceBounds = new Bounds(
            boundsCenter,
            new Vector3(
                Mathf.Max(terrain.VoxelSizeMeters, (horizontalExtent + waterUpdatePaddingMeters) * 2f),
                terrain.WorldSize.y,
                Mathf.Max(terrain.VoxelSizeMeters, (horizontalExtent + waterUpdatePaddingMeters) * 2f)));
    }

    public void UpdateRiverInfluenceBounds(ProceduralVoxelTerrain terrain, GeneratedRiver river)
    {
        if (terrain == null || river == null || river.waterPath.Count == 0)
        {
            return;
        }

        float horizontalPadding = waterUpdatePaddingMeters;
        for (int i = 0; i < river.widthProfile.Count; i++)
        {
            horizontalPadding = Mathf.Max(horizontalPadding, river.widthProfile[i] * 0.5f);
        }

        river.influenceBounds = BuildHorizontalInfluenceBounds(terrain, river.waterPath, horizontalPadding + waterUpdatePaddingMeters);
    }

    private bool RefreshAffectedLakes(
        ProceduralVoxelTerrain terrain,
        Transform generatedRoot,
        Bounds refreshBounds,
        Bounds changedBounds,
        out Bounds hydrologyBounds,
        out bool hasHydrologyBounds)
    {
        hydrologyBounds = default;
        hasHydrologyBounds = false;
        bool updated = false;
        int originalLakeCount = generatedLakes.Count;
        for (int i = 0; i < originalLakeCount; i++)
        {
            GeneratedLake lake = generatedLakes[i];
            bool active = WaterSpatialQueryUtility.IsLakeActive(lake, minRenderableVolumeCubicMeters);
            bool shouldRefresh = active && ShouldRefreshLakeForChangedBounds(terrain, lake, refreshBounds);
            if (!active || !shouldRefresh)
            {
                if (active)
                {
                    LogLakeDebug($"Skipped {WaterDebugUtility.DescribeLake(lake)} for bounds center={refreshBounds.center}, size={refreshBounds.size}.");
                }

                continue;
            }

            Bounds preRefreshBounds = BuildLakeBasinRefreshBounds(terrain, lake);
            LogLakeDebug($"Refreshing {WaterDebugUtility.DescribeLake(lake)} for bounds center={changedBounds.center}, size={changedBounds.size}. Stored volume={lake.storedVolumeCubicMeters:F3} m^3.");
            if (!TryRefreshLakeForTerrainChange(terrain, lake, changedBounds))
            {
                LogLakeDebug($"Refresh failed for {WaterDebugUtility.DescribeLake(lake)}.");
                continue;
            }

            createLakeObject?.Invoke(lake, generatedRoot);
            EncapsulateHydrologyBounds(ref hydrologyBounds, ref hasHydrologyBounds, preRefreshBounds);
            EncapsulateHydrologyBounds(ref hydrologyBounds, ref hasHydrologyBounds, BuildLakeBasinRefreshBounds(terrain, lake));
            LogLakeDebug($"Refresh succeeded for {WaterDebugUtility.DescribeLake(lake)}. Stored volume={lake.storedVolumeCubicMeters:F3} m^3.");
            updated = true;
        }

        return updated;
    }

    private static void EncapsulateHydrologyBounds(ref Bounds hydrologyBounds, ref bool hasHydrologyBounds, Bounds candidateBounds)
    {
        if (candidateBounds.size.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        if (!hasHydrologyBounds)
        {
            hydrologyBounds = candidateBounds;
            hasHydrologyBounds = true;
            return;
        }

        hydrologyBounds.Encapsulate(candidateBounds.min);
        hydrologyBounds.Encapsulate(candidateBounds.max);
    }

    private bool ShouldRefreshLakeForChangedBounds(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        Bounds changedBounds)
    {
        if (terrain == null || lake == null)
        {
            return false;
        }

        Bounds refreshBounds = BuildLakeBasinRefreshBounds(terrain, lake);
        if (refreshBounds.size.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        return refreshBounds.Intersects(changedBounds);
    }

    private Bounds BuildLakeBasinRefreshBounds(ProceduralVoxelTerrain terrain, GeneratedLake lake)
    {
        if (terrain == null || lake == null)
        {
            return default;
        }

        float horizontalPadding = Mathf.Max(Mathf.Max(waterUpdatePaddingMeters, lakeDynamicExpansionMeters), terrain.VoxelSizeMeters * 2f);
        float horizontalExtent = Mathf.Max(lake.radius, Mathf.Min(lake.captureRadius, lake.radius + horizontalPadding));
        Vector3 boundsCenter = lake.center;
        if (WaterSpatialQueryUtility.HasLakeSurfaceGeometry(lake))
        {
            horizontalExtent = Mathf.Max(
                horizontalExtent,
                Mathf.Max(lake.surfaceBounds.extents.x, lake.surfaceBounds.extents.z) + horizontalPadding);
            boundsCenter.x = lake.surfaceBounds.center.x;
            boundsCenter.z = lake.surfaceBounds.center.z;
        }

        float verticalPadding = Mathf.Max(terrain.VoxelSizeMeters * 2f, waterUpdatePaddingMeters);
        float minY = lake.surfaceY - Mathf.Max(terrain.ChunkWorldSizeMeters, lakeDepthMeters * 2.5f);
        float maxY = lake.surfaceY + Mathf.Max(terrain.ChunkWorldSizeMeters, lakeDepthMeters * 2f);
        if (lake.terrainPatch != null && lake.terrainPatch.hasBounds)
        {
            minY = Mathf.Min(minY, lake.terrainPatch.bounds.min.y - verticalPadding);
            maxY = Mathf.Max(maxY, lake.terrainPatch.bounds.max.y + verticalPadding);
        }

        Bounds terrainBounds = terrain.WorldBounds;
        minY = Mathf.Clamp(minY, terrainBounds.min.y, terrainBounds.max.y);
        maxY = Mathf.Clamp(maxY, terrainBounds.min.y, terrainBounds.max.y);
        if (maxY <= minY + terrain.VoxelSizeMeters)
        {
            maxY = Mathf.Min(terrainBounds.max.y, minY + Mathf.Max(terrain.VoxelSizeMeters, terrain.ChunkWorldSizeMeters));
        }

        return new Bounds(
            new Vector3(boundsCenter.x, (minY + maxY) * 0.5f, boundsCenter.z),
            new Vector3(
                Mathf.Max(terrain.VoxelSizeMeters, horizontalExtent * 2f),
                Mathf.Max(terrain.VoxelSizeMeters, maxY - minY),
                Mathf.Max(terrain.VoxelSizeMeters, horizontalExtent * 2f)));
    }

    private bool RefreshAffectedRivers(ProceduralVoxelTerrain terrain, Transform generatedRoot, Bounds changedBounds)
    {
        bool updated = false;
        for (int i = 0; i < generatedRivers.Count; i++)
        {
            GeneratedRiver river = generatedRivers[i];
            if (river == null || !river.influenceBounds.Intersects(changedBounds))
            {
                continue;
            }

            if (!TryRefreshRiverForTerrainChange(terrain, river))
            {
                continue;
            }

            createRiverObject?.Invoke(river, generatedRoot, i);
            updated = true;
        }

        return updated;
    }

    private bool TryRefreshLakeForTerrainChange(ProceduralVoxelTerrain terrain, GeneratedLake lake, Bounds changedBounds)
    {
        if (terrain == null || lake == null || hydrologySolver == null)
        {
            return false;
        }

        float originalCaptureRadius = lake.captureRadius;
        float previousSurfaceY = lake.surfaceY;
        float targetStoredVolumeCubicMeters = lake.storedVolumeCubicMeters;
        ExpandLakeCaptureRadiusForChange(lake, changedBounds);
        if (lake.captureRadius > originalCaptureRadius + 0.001f)
        {
            LogLakeDebug($"Expanded capture radius for {WaterDebugUtility.DescribeLake(lake)} from {originalCaptureRadius:F2}m to {lake.captureRadius:F2}m.");
        }

        float localRefreshCaptureRadius = ComputeLocalLakeRefreshCaptureRadius(terrain, lake, changedBounds);
        if (hydrologySolver.TryBuildLakeTerrainPatch(terrain, lake, out LakeTerrainPatch prebuiltTerrainPatch, changedBounds, localRefreshCaptureRadius))
        {
            if (localRefreshCaptureRadius < lake.captureRadius - 0.001f)
            {
                LogLakeDebug(
                    $"Built local refresh patch for {WaterDebugUtility.DescribeLake(lake)} using radius {localRefreshCaptureRadius:F2}m " +
                    $"of capture {lake.captureRadius:F2}m. Patch chunks={prebuiltTerrainPatch.chunkCoordinates.Count}, triangles={prebuiltTerrainPatch.triangles.Count}.");
            }

            if (hydrologySolver.TrySolveLakeSurfaceForTargetVolume(terrain, prebuiltTerrainPatch, lake, targetStoredVolumeCubicMeters, out LakeSolveResult prebuiltSolveResult) &&
                !prebuiltSolveResult.touchesOpenBoundary)
            {
                lake.terrainPatch = prebuiltTerrainPatch;
                hydrologySolver.ApplyLakeSolveResult(lake, prebuiltSolveResult);
                lake.storedVolumeCubicMeters = LakeHydrologySolver.ClampLakeStoredVolume(targetStoredVolumeCubicMeters, prebuiltSolveResult);
                LogLakeDebug($"Solved {WaterDebugUtility.DescribeLake(lake)} from prebuilt patch. SurfaceY {previousSurfaceY:F3}->{lake.surfaceY:F3}, target volume={targetStoredVolumeCubicMeters:F3} m^3, solved volume={prebuiltSolveResult.volumeCubicMeters:F3} m^3, stored volume={lake.storedVolumeCubicMeters:F3} m^3, triangles={lake.surfaceTriangles.Length / 3}.");
                UpdateLakeInfluenceBounds(terrain, lake);
                return true;
            }

            LogLakeDebug($"Local refresh patch for {WaterDebugUtility.DescribeLake(lake)} touched an open boundary; falling back to overflow-expansion solve.");

            GeneratedLake localExpansionLake = CreateTemporaryLakeRefreshSeed(lake, localRefreshCaptureRadius);
            if (localExpansionLake != null &&
                hydrologySolver.TrySolveLakeForTargetVolumeWithOverflowExpansion(
                    terrain,
                    localExpansionLake,
                    targetStoredVolumeCubicMeters,
                    changedBounds,
                    out LakeTerrainPatch localExpansionPatch,
                    out LakeSolveResult localExpansionSolveResult) &&
                !localExpansionSolveResult.touchesOpenBoundary)
            {
                lake.terrainPatch = localExpansionPatch;
                hydrologySolver.ApplyLakeSolveResult(lake, localExpansionSolveResult);
                lake.storedVolumeCubicMeters = LakeHydrologySolver.ClampLakeStoredVolume(targetStoredVolumeCubicMeters, localExpansionSolveResult);
                LogLakeDebug(
                    $"Solved {WaterDebugUtility.DescribeLake(lake)} from local overflow expansion. Start radius={localRefreshCaptureRadius:F2}m, " +
                    $"expanded radius={localExpansionLake.captureRadius:F2}m, surfaceY {previousSurfaceY:F3}->{lake.surfaceY:F3}, " +
                    $"target volume={targetStoredVolumeCubicMeters:F3} m^3, solved volume={localExpansionSolveResult.volumeCubicMeters:F3} m^3, " +
                    $"stored volume={lake.storedVolumeCubicMeters:F3} m^3, triangles={lake.surfaceTriangles.Length / 3}.");
                UpdateLakeInfluenceBounds(terrain, lake);
                return true;
            }
        }

        if (!hydrologySolver.TrySolveLakeForTargetVolumeWithOverflowExpansion(
                terrain,
                lake,
                targetStoredVolumeCubicMeters,
                changedBounds,
                out LakeTerrainPatch terrainPatch,
                out LakeSolveResult solveResult))
        {
            LogLakeDebug($"Failed to solve target volume for {WaterDebugUtility.DescribeLake(lake)} using stored volume {lake.storedVolumeCubicMeters:F3} m^3.");
            return false;
        }

        lake.terrainPatch = terrainPatch;
        hydrologySolver.ApplyLakeSolveResult(lake, solveResult);
        lake.storedVolumeCubicMeters = LakeHydrologySolver.ClampLakeStoredVolume(targetStoredVolumeCubicMeters, solveResult);
        LogLakeDebug($"Solved {WaterDebugUtility.DescribeLake(lake)}. SurfaceY {previousSurfaceY:F3}->{lake.surfaceY:F3}, target volume={targetStoredVolumeCubicMeters:F3} m^3, solved volume={solveResult.volumeCubicMeters:F3} m^3, stored volume={lake.storedVolumeCubicMeters:F3} m^3, triangles={lake.surfaceTriangles.Length / 3}.");
        UpdateLakeInfluenceBounds(terrain, lake);
        return true;
    }

    private bool TryRefreshRiverForTerrainChange(ProceduralVoxelTerrain terrain, GeneratedRiver river)
    {
        if (terrain == null || river == null || river.waterPath.Count < 2)
        {
            return false;
        }

        List<Vector3> updatedWaterPath = FreshwaterGenerator.BuildRiverWaterPath(terrain, river.waterPath, riverDepthMeters, seaLevelMeters);
        if (updatedWaterPath.Count < 2)
        {
            return false;
        }

        List<float> widthProfile = FreshwaterGenerator.BuildRiverWidthProfile(updatedWaterPath.Count, river.baseWidth);
        river.waterPath.Clear();
        river.waterPath.AddRange(updatedWaterPath);
        river.widthProfile.Clear();
        river.widthProfile.AddRange(widthProfile);
        FreshwaterGenerator.PopulateRenderableRiverPath(river);
        UpdateRiverInfluenceBounds(terrain, river);
        return true;
    }

    private void ExpandLakeCaptureRadiusForChange(GeneratedLake lake, Bounds changedBounds)
    {
        if (lake == null)
        {
            return;
        }

        float requiredRadius = 0f;
        Vector2 lakeCenterXZ = new Vector2(lake.center.x, lake.center.z);
        Vector2[] boundsCorners =
        {
            new Vector2(changedBounds.min.x, changedBounds.min.z),
            new Vector2(changedBounds.max.x, changedBounds.min.z),
            new Vector2(changedBounds.min.x, changedBounds.max.z),
            new Vector2(changedBounds.max.x, changedBounds.max.z)
        };

        for (int i = 0; i < boundsCorners.Length; i++)
        {
            requiredRadius = Mathf.Max(requiredRadius, Vector2.Distance(lakeCenterXZ, boundsCorners[i]));
        }

        float expansionThreshold = lake.captureRadius - Mathf.Max(0.5f, waterUpdatePaddingMeters + 0.5f);
        if (requiredRadius < expansionThreshold)
        {
            return;
        }

        lake.captureRadius = Mathf.Max(lake.captureRadius, requiredRadius + Mathf.Max(lakeDynamicExpansionMeters, 2f));
    }

    private float ComputeLocalLakeRefreshCaptureRadius(ProceduralVoxelTerrain terrain, GeneratedLake lake, Bounds changedBounds)
    {
        if (terrain == null || lake == null)
        {
            return 0f;
        }

        float padding = Mathf.Max(
            terrain.ChunkWorldSizeMeters * 0.75f,
            Mathf.Max(
                lakeDynamicExpansionMeters * 2f,
                Mathf.Max(waterUpdatePaddingMeters * 2f, terrain.VoxelSizeMeters * 6f)));
        float localRadius = Mathf.Max(lake.radius + padding, ComputeBoundsCornerRadiusXZ(lake.center, changedBounds) + padding);
        if (WaterSpatialQueryUtility.HasLakeSurfaceGeometry(lake))
        {
            localRadius = Mathf.Max(localRadius, ComputeBoundsCornerRadiusXZ(lake.center, lake.surfaceBounds) + padding);
        }

        return Mathf.Clamp(localRadius, Mathf.Max(terrain.VoxelSizeMeters, lake.radius), Mathf.Max(lake.captureRadius, lake.radius));
    }

    private static GeneratedLake CreateTemporaryLakeRefreshSeed(GeneratedLake sourceLake, float captureRadius)
    {
        if (sourceLake == null)
        {
            return null;
        }

        return new GeneratedLake
        {
            center = sourceLake.center,
            radius = sourceLake.radius,
            surfaceY = sourceLake.surfaceY,
            storedVolumeCubicMeters = sourceLake.storedVolumeCubicMeters,
            captureRadius = Mathf.Max(sourceLake.radius, captureRadius),
            terrainPatch = sourceLake.terrainPatch,
            surfaceBounds = sourceLake.surfaceBounds,
            floodedCellCount = sourceLake.floodedCellCount
        };
    }

    private Bounds BuildHorizontalInfluenceBounds(ProceduralVoxelTerrain terrain, IReadOnlyList<Vector3> points, float horizontalPadding)
    {
        Bounds bounds = new Bounds(
            new Vector3(points[0].x, terrain.WorldBounds.center.y, points[0].z),
            new Vector3(0.01f, terrain.WorldSize.y, 0.01f));
        for (int i = 1; i < points.Count; i++)
        {
            bounds.Encapsulate(new Vector3(points[i].x, terrain.WorldBounds.center.y, points[i].z));
        }

        bounds.Expand(new Vector3(horizontalPadding * 2f, 0f, horizontalPadding * 2f));
        return bounds;
    }

    private void LogLakeDebug(string message)
    {
        logDebug?.Invoke(message);
    }
}
