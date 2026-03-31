using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Tracks timing entries, progress fraction, and status strings for a single water
/// generation run. Owns the editor progress-bar lifecycle for non-play-mode generation.
/// </summary>
internal sealed class WaterGenerationProfiler
{
    private readonly List<WaterGenerationTimingEntry> timings = new List<WaterGenerationTimingEntry>();
    private int completedSteps;
    private int totalSteps;
#if UNITY_EDITOR
    private double lastProgressBarTime;
#endif

    public bool IsGenerating { get; private set; }
    public IReadOnlyList<WaterGenerationTimingEntry> Timings => timings;
    public string Status { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public float Progress01 { get; private set; }
    public long TotalMilliseconds { get; private set; }

    public void Reset(int stepCount, string initialStatus, string ownerName)
    {
        timings.Clear();
        TotalMilliseconds = 0L;
        Summary = string.Empty;
        totalSteps = Mathf.Max(1, stepCount);
        completedSteps = 0;
        Progress01 = 0f;
        Status = string.IsNullOrWhiteSpace(initialStatus) ? "Preparing voxel water" : initialStatus;
        IsGenerating = true;
#if UNITY_EDITOR
        lastProgressBarTime = 0d;
#endif
        RefreshEditorProgress(true, ownerName);
    }

    public void UpdateProgress(string status, float currentStepProgress01, bool force, string ownerName)
    {
        Status = string.IsNullOrWhiteSpace(status) ? "Generating voxel water" : status;
        Progress01 = Mathf.Clamp01(
            (completedSteps + Mathf.Clamp01(currentStepProgress01)) /
            Mathf.Max(1f, totalSteps));
        RefreshEditorProgress(force, ownerName);
    }

    public void RecordStep(string label, long milliseconds, string details = null)
    {
        timings.Add(new WaterGenerationTimingEntry(label, milliseconds, details));
    }

    public void CompleteStep(string ownerName)
    {
        completedSteps = Mathf.Min(completedSteps + 1, totalSteps);
        Progress01 = Mathf.Clamp01(completedSteps / Mathf.Max(1f, totalSteps));
        RefreshEditorProgress(true, ownerName);
    }

    public void Finalize(long totalMs, bool success, string finalStatus, string ownerName)
    {
        TotalMilliseconds = Math.Max(0L, totalMs);
        Summary = BuildSummary(TotalMilliseconds);
        if (!success && !string.IsNullOrWhiteSpace(finalStatus))
        {
            Summary = string.IsNullOrWhiteSpace(Summary)
                ? finalStatus
                : $"{finalStatus}. {Summary}";
        }

        if (success)
        {
            completedSteps = totalSteps;
            Progress01 = 1f;
        }

        if (!string.IsNullOrWhiteSpace(finalStatus))
        {
            Status = finalStatus;
        }

        RefreshEditorProgress(true, ownerName);
        IsGenerating = false;
    }

    public string BuildSummary(long totalMs)
    {
        if (timings.Count == 0)
        {
            return totalMs > 0L ? $"total={totalMs}ms" : string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < timings.Count; i++)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            WaterGenerationTimingEntry entry = timings[i];
            builder
                .Append(entry.Label)
                .Append('=')
                .Append(entry.Milliseconds)
                .Append("ms");
        }

        if (builder.Length > 0)
        {
            builder.Append(", ");
        }

        builder
            .Append("total=")
            .Append(totalMs)
            .Append("ms");
        return builder.ToString();
    }

    public void ClearEditorProgress()
    {
#if UNITY_EDITOR
        lastProgressBarTime = 0d;
        EditorUtility.ClearProgressBar();
#endif
    }

    private void RefreshEditorProgress(bool force, string ownerName)
    {
#if UNITY_EDITOR
        if (Application.isPlaying || !IsGenerating)
        {
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        if (!force && now < lastProgressBarTime + 0.05d)
        {
            return;
        }

        lastProgressBarTime = now;
        EditorUtility.DisplayProgressBar($"Generating {ownerName}", Status, Progress01);
#endif
    }
}
