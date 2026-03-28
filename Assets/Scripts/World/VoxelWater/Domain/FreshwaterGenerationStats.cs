using System;
using UnityEngine;

internal sealed class FreshwaterGenerationStats
{
    public FreshwaterGenerationStats(string bodyLabel, int targetCount, int maxAttempts)
    {
        this.bodyLabel = bodyLabel;
        this.targetCount = Mathf.Max(0, targetCount);
        this.maxAttempts = Math.Max(0, maxAttempts);
    }

    public readonly string bodyLabel;
    public readonly int targetCount;
    public readonly int maxAttempts;
    public int generatedCount;
    public int attempts;
    public int rejectedMissingSurface;
    public int rejectedElevation;
    public int rejectedSlope;
    public int rejectedSpacing;
    public int rejectedShoreline;
    public int rejectedRefine;
    public int rejectedCachedBasin;
    public int rejectedProfile;
    public long candidateAnalysisMilliseconds;
    public long previewSolveMilliseconds;
    public long carveMilliseconds;
    public long finalizeSolveMilliseconds;
    public long rollbackMilliseconds;
    public long basinMaterialMilliseconds;

    public string BuildSummary()
    {
        return
            $"{bodyLabel} accepted={generatedCount}/{targetCount}, attempts={attempts}/{maxAttempts}, " +
            $"candidate={candidateAnalysisMilliseconds}ms, preview={previewSolveMilliseconds}ms, carve={carveMilliseconds}ms, finalize={finalizeSolveMilliseconds}ms, rollback={rollbackMilliseconds}ms, " +
            $"paint={basinMaterialMilliseconds}ms, rejected: " +
            $"missing-surface={rejectedMissingSurface}, elevation={rejectedElevation}, slope={rejectedSlope}, overlap={rejectedSpacing}, " +
            $"shoreline={rejectedShoreline}, refine={rejectedRefine}, cached-basin={rejectedCachedBasin}, profile={rejectedProfile}";
    }
}
