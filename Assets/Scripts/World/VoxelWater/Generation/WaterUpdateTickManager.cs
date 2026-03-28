using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the deferred water-refresh queue for the static-basin runtime model.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Accumulates dirty world-space bounds and chunk coordinates from terrain-geometry change events.</item>
///   <item>Applies a configurable debounce delay before marking the refresh as ready to consume.</item>
///   <item>Guards against processing identical bounds twice in the same frame (duplicate-event protection).</item>
/// </list>
///
/// Usage (inside ProceduralVoxelTerrainWaterSystem):
/// <code>
///   // Enqueue a change
///   tickManager.Enqueue(changedBounds, changedChunks, terrainRefreshDebounceSeconds);
///
///   // In LateUpdate – consume when ready
///   if (tickManager.TryConsumeReady(out Bounds bounds, out List&lt;Vector3Int&gt; chunks))
///       RefreshWaterForChangedBounds(bounds, chunks);
/// </code>
/// </summary>
internal sealed class WaterUpdateTickManager
{
    // Pending-refresh state
    private readonly HashSet<Vector3Int> pendingChunks = new HashSet<Vector3Int>();
    private bool hasPending;
    private Bounds pendingBounds;
    private float processTime;

    // Duplicate-detection state (per-frame)
    private int    lastProcessedFrame  = -1;
    private Vector3 lastProcessedCenter = new Vector3(float.NaN, float.NaN, float.NaN);
    private Vector3 lastProcessedSize   = new Vector3(float.NaN, float.NaN, float.NaN);

    /// <summary>Whether a refresh is pending (debounce may not yet have elapsed).</summary>
    public bool HasPending => hasPending;

    // -------------------------------------------------------------------------
    // Enqueueing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a changed-bounds region to the pending refresh queue and resets the debounce timer.
    /// Multiple calls accumulate into a single expanding AABB.
    /// </summary>
    public void Enqueue(Bounds changedBounds, IReadOnlyCollection<Vector3Int> changedChunks, float debounceSeconds)
    {
        if (!hasPending)
        {
            pendingBounds = changedBounds;
            hasPending    = true;
        }
        else
        {
            pendingBounds.Encapsulate(changedBounds.min);
            pendingBounds.Encapsulate(changedBounds.max);
        }

        if (changedChunks != null)
        {
            foreach (Vector3Int chunk in changedChunks)
            {
                pendingChunks.Add(chunk);
            }
        }

        processTime = Time.unscaledTime + Mathf.Max(0f, debounceSeconds);
    }

    // -------------------------------------------------------------------------
    // Consuming
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to consume the pending refresh.
    /// Returns <c>true</c> (and populates <paramref name="outBounds"/> / <paramref name="outChunks"/>)
    /// only when a refresh is pending AND the debounce window has elapsed.
    /// Clears internal state on success so subsequent calls return <c>false</c> until the next
    /// <see cref="Enqueue"/>.
    /// </summary>
    public bool TryConsumeReady(out Bounds outBounds, out List<Vector3Int> outChunks)
    {
        outBounds = default;
        outChunks = null;

        if (!hasPending || Time.unscaledTime + 0.0001f < processTime)
        {
            return false;
        }

        outBounds = pendingBounds;
        outChunks = pendingChunks.Count > 0 ? new List<Vector3Int>(pendingChunks) : null;

        hasPending = false;
        pendingChunks.Clear();
        return true;
    }

    // -------------------------------------------------------------------------
    // Duplicate detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> if <paramref name="bounds"/> is identical (within floating-point
    /// tolerance) to the last bounds passed to <see cref="MarkProcessed"/> in the current frame.
    /// Call this before executing a refresh to skip redundant work.
    /// </summary>
    public bool IsDuplicateRefresh(Bounds bounds)
    {
        return Time.frameCount == lastProcessedFrame
            && Vector3.SqrMagnitude(bounds.center - lastProcessedCenter) <= 0.0001f
            && Vector3.SqrMagnitude(bounds.size   - lastProcessedSize)   <= 0.0001f;
    }

    /// <summary>
    /// Records <paramref name="bounds"/> as the last processed bounds for the current frame.
    /// Call this immediately after executing a refresh so <see cref="IsDuplicateRefresh"/>
    /// can suppress redundant same-frame calls.
    /// </summary>
    public void MarkProcessed(Bounds bounds)
    {
        lastProcessedFrame  = Time.frameCount;
        lastProcessedCenter = bounds.center;
        lastProcessedSize   = bounds.size;
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>Discards any pending refresh without processing it.</summary>
    public void Clear()
    {
        hasPending = false;
        pendingChunks.Clear();
    }
}
