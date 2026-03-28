using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static helpers for producing human-readable lake debug summaries.
/// </summary>
internal static class WaterDebugUtility
{
    private const string SplitSupportSummary = "Split support: surface partition + downstream overflow seeding enabled.";

    public static string DescribeLake(GeneratedLake lake)
    {
        if (lake == null)
        {
            return "Lake<null>";
        }

        string bodyType = lake.isPond ? "Pond" : "Lake";
        return $"{bodyType}(center=({lake.center.x:F1}, {lake.center.z:F1}), surfaceY={lake.surfaceY:F2}, captureRadius={lake.captureRadius:F2})";
    }

    public static string FormatLakeDebugSummary(GeneratedLake lake, int lakeIndex, Vector3? probePoint)
    {
        if (lake == null)
        {
            return lakeIndex >= 0 ? $"[{lakeIndex}] Lake<null>" : "Lake<null>";
        }

        string prefix = lakeIndex >= 0 ? $"[{lakeIndex}] " : string.Empty;
        string probeSummary = string.Empty;
        if (probePoint.HasValue &&
            WaterSpatialQueryUtility.TryGetClosestPointOnLakeSurface(lake, probePoint.Value, out _, out float planarDistanceMeters))
        {
            probeSummary = $" probeDistanceXZ={planarDistanceMeters:F2}m";
        }

        return $"{prefix}{DescribeLake(lake)}, volume={lake.storedVolumeCubicMeters:F3} m^3, flooded={lake.floodedCellCount}, tris={lake.surfaceTriangles.Length / 3}, bounds=({lake.surfaceBounds.size.x:F1} x {lake.surfaceBounds.size.z:F1}){probeSummary}";
    }

    public static int CountActiveLakes(IReadOnlyList<GeneratedLake> lakes, float minRenderableVolume)
    {
        if (lakes == null)
        {
            return 0;
        }

        int activeLakeCount = 0;
        for (int i = 0; i < lakes.Count; i++)
        {
            if (WaterSpatialQueryUtility.IsLakeActive(lakes[i], minRenderableVolume))
            {
                activeLakeCount++;
            }
        }

        return activeLakeCount;
    }

    public static string GetLakeDebugSummaryAtPoint(
        IReadOnlyList<GeneratedLake> lakes,
        Vector3 worldPoint,
        float pointPaddingMeters,
        float minRenderableVolume)
    {
        if (!WaterSpatialQueryUtility.TryResolveLakeNearPoint(lakes, worldPoint, pointPaddingMeters, minRenderableVolume, out GeneratedLake lake))
        {
            return $"No active lake near ({worldPoint.x:F1}, {worldPoint.y:F1}, {worldPoint.z:F1}). Active lakes: {CountActiveLakes(lakes, minRenderableVolume)}. {SplitSupportSummary}";
        }

        return $"{FormatLakeDebugSummary(lake, IndexOfLake(lakes, lake), worldPoint)}\n{SplitSupportSummary}";
    }

    public static string GetAllLakeDebugSummary(IReadOnlyList<GeneratedLake> lakes, float minRenderableVolume)
    {
        List<string> lines = new List<string>();
        int activeLakeCount = CountActiveLakes(lakes, minRenderableVolume);
        lines.Add($"Active lakes: {activeLakeCount}");
        lines.Add(SplitSupportSummary);
        if (activeLakeCount <= 0 || lakes == null)
        {
            return string.Join("\n", lines);
        }

        for (int i = 0; i < lakes.Count; i++)
        {
            GeneratedLake lake = lakes[i];
            if (!WaterSpatialQueryUtility.IsLakeActive(lake, minRenderableVolume))
            {
                continue;
            }

            lines.Add(FormatLakeDebugSummary(lake, i, null));
        }

        return string.Join("\n", lines);
    }

    private static int IndexOfLake(IReadOnlyList<GeneratedLake> lakes, GeneratedLake lake)
    {
        if (lakes == null || lake == null)
        {
            return -1;
        }

        for (int i = 0; i < lakes.Count; i++)
        {
            if (ReferenceEquals(lakes[i], lake))
            {
                return i;
            }
        }

        return -1;
    }
}
