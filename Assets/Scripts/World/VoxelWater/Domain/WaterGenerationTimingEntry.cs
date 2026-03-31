using System;

public readonly struct WaterGenerationTimingEntry
{
    public WaterGenerationTimingEntry(string label, long milliseconds, string details = "")
    {
        Label = string.IsNullOrWhiteSpace(label) ? "Step" : label;
        Milliseconds = Math.Max(0L, milliseconds);
        Details = details ?? string.Empty;
    }

    public string Label { get; }
    public long Milliseconds { get; }
    public string Details { get; }
}
