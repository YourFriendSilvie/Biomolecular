using System.Collections.Generic;
using UnityEngine;

public readonly struct VoxelTerrainGeometryChangedEventArgs
{
    public VoxelTerrainGeometryChangedEventArgs(Bounds changedWorldBounds, IReadOnlyList<Vector3Int> affectedChunkCoordinates)
    {
        ChangedWorldBounds = changedWorldBounds;
        AffectedChunkCoordinates = affectedChunkCoordinates;
    }

    public Bounds ChangedWorldBounds { get; }
    public IReadOnlyList<Vector3Int> AffectedChunkCoordinates { get; }
}
