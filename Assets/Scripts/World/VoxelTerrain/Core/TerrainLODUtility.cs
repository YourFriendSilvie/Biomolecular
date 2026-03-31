using System;
using UnityEngine;

/// <summary>
/// Utility helpers for computing chunk LOD decisions. Provides both a simple
/// distance-based LOD computation (for batch precompute) and a hysteresis-aware
/// variant (for runtime streaming) to avoid frequent LOD thrashing.
/// All numeric operations avoid UnityEngine.Mathf so these helpers are safe to call
/// from background threads / threadpool workers used by the parallel generation path.
/// </summary>
public static class TerrainLODUtility
{
    /// <summary>
    /// Compute chunk LOD purely from distance (no hysteresis).
    /// </summary>
    public static int ComputeChunkLod(Vector3 chunkWorldPosition, Vector3 anchorPosition, float chunkWorldSizeMeters, int maxLodLevels, float lodDistanceFactor)
    {
        // Phase9 expectation: LOD step = chunkWorldSizeMeters * lodDistanceFactor
        // Compute the world-space center-to-anchor distance and divide by the step.
        // Using the exact formula keeps the LOD bands aligned to chunk boundaries as specified in the plan.
        float distance = Vector3.Distance(anchorPosition, chunkWorldPosition);

        float step = chunkWorldSizeMeters * lodDistanceFactor;
        // Guard against pathological zero/negative settings in the Inspector.
        if (step <= 0f) step = 0.0001f;

        int lod = (int)(distance / step);
        // Clamp to configured range [0, maxLodLevels]
        lod = Math.Max(0, Math.Min(lod, maxLodLevels));
        return lod;
    }

    /// <summary>
    /// Compute chunk LOD with hysteresis. The hysteresisFraction is measured in
    /// LOD units (0..1). A non-zero fraction reduces sensitivity to small
    /// distance changes by requiring the normalized distance to move by that
    /// fraction of a LOD step before accepting a change.
    /// </summary>
    public static int ComputeChunkLodWithHysteresis(Vector3 chunkWorldPosition, Vector3 anchorPosition, float chunkWorldSizeMeters, int maxLodLevels, float lodDistanceFactor, float hysteresisFraction, int previousLod)
    {
        float distance = Vector3.Distance(anchorPosition, chunkWorldPosition);
        float step = (float)(Math.Max(0.001, lodDistanceFactor) * Math.Max(0.0001, chunkWorldSizeMeters));
        float norm = distance / step;
        int baseLod = (int)norm;
        baseLod = Math.Max(0, Math.Min(baseLod, maxLodLevels));

        if (previousLod == baseLod) return baseLod;

        // If moving towards coarser LOD (larger number) require exceeding previousLod + hysteresisFraction
        if (baseLod > previousLod)
        {
            if (norm > previousLod + hysteresisFraction) return baseLod;
            return previousLod;
        }

        // If moving towards finer LOD (smaller number) require dropping below previousLod - hysteresisFraction
        if (baseLod < previousLod)
        {
            if (norm < previousLod - hysteresisFraction) return baseLod;
            return previousLod;
        }

        return baseLod;
    }
}
