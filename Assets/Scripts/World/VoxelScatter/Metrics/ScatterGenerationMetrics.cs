using System.Collections.Generic;
using System.Text;
using UnityEngine;

internal sealed class ScatterGenerationMetrics
{
    public long totalTicks;
    public long resolveTerrainTicks;
    public long resolveWaterTicks;
    public long clearTicks;
    public long boundsTicks;
    public long rootTicks;
    public readonly List<PrototypeGenerationMetrics> prototypeMetrics = new List<PrototypeGenerationMetrics>();
}

internal sealed class PrototypeGenerationMetrics
{
    public string prototypeName = string.Empty;
    public int requestedCount;
    public int acceptedCount;
    public int attempts;
    public int baseAttemptBudget;
    public int maxAttempts;
    public int cachedScreeningSamples;
    public int liveScreeningFallbackSamples;
    public int rejectedScreeningSurfaceMiss;
    public int rejectedScreeningConstraints;
    public int rejectedDensity;
    public int rejectedSpacing;
    public int rejectedLiveSurfaceMiss;
    public int rejectedLiveConstraints;
    public int rejectedWater;
    public long totalTicks;
    public long screeningSurfaceTicks;
    public long surfaceConstraintTicks;
    public long densityTicks;
    public long spacingTicks;
    public long liveSurfaceTicks;
    public long waterTicks;
    public long instantiationTicks;
}

internal static class ScatterTimingUtility
{
    internal static long BeginTiming(bool collectTimings)
    {
        return collectTimings ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
    }

    internal static void EndTiming(ref long accumulatorTicks, long startTimestamp)
    {
        if (startTimestamp == 0L) return;
        accumulatorTicks += System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp;
    }

    internal static long GetElapsedTicks(long startTimestamp)
    {
        return startTimestamp == 0L ? 0L : System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp;
    }

    internal static string FormatTimingMilliseconds(long ticks)
    {
        if (ticks <= 0L) return "0.0ms";
        double milliseconds = (ticks * 1000d) / System.Diagnostics.Stopwatch.Frequency;
        return $"{milliseconds:0.0}ms";
    }

    internal static string BuildPrototypeTimingSummary(PrototypeGenerationMetrics m)
    {
        StringBuilder builder = new StringBuilder(512);
        builder.Append('[')
            .Append(nameof(ProceduralVoxelTerrainScatterer))
            .Append("] prototype ")
            .Append(m.prototypeName)
            .Append(": total=")
            .Append(FormatTimingMilliseconds(m.totalTicks))
            .Append(", placed=")
            .Append(m.acceptedCount)
            .Append('/')
            .Append(m.requestedCount)
            .Append(", attempts=")
            .Append(m.attempts)
            .Append('/')
            .Append(m.maxAttempts)
            .Append(" (base ")
            .Append(m.baseAttemptBudget)
            .Append("), timings { screening=")
            .Append(FormatTimingMilliseconds(m.screeningSurfaceTicks))
            .Append(", constraints=")
            .Append(FormatTimingMilliseconds(m.surfaceConstraintTicks))
            .Append(", density=")
            .Append(FormatTimingMilliseconds(m.densityTicks))
            .Append(", spacing=")
            .Append(FormatTimingMilliseconds(m.spacingTicks))
            .Append(", liveTerrain=")
            .Append(FormatTimingMilliseconds(m.liveSurfaceTicks))
            .Append(", water=")
            .Append(FormatTimingMilliseconds(m.waterTicks))
            .Append(", instantiate=")
            .Append(FormatTimingMilliseconds(m.instantiationTicks))
            .Append(" }, rejections { screenMiss=")
            .Append(m.rejectedScreeningSurfaceMiss)
            .Append(", screenConstraint=")
            .Append(m.rejectedScreeningConstraints)
            .Append(", density=")
            .Append(m.rejectedDensity)
            .Append(", spacing=")
            .Append(m.rejectedSpacing)
            .Append(", liveMiss=")
            .Append(m.rejectedLiveSurfaceMiss)
            .Append(", liveConstraint=")
            .Append(m.rejectedLiveConstraints)
            .Append(", water=")
            .Append(m.rejectedWater)
            .Append(" }, sources { cached=")
            .Append(m.cachedScreeningSamples)
            .Append(", liveFallback=")
            .Append(m.liveScreeningFallbackSamples)
            .Append(" }.");
        return builder.ToString();
    }
}
