using System;
using System.Collections.Generic;
using UnityEngine;

public partial class ProceduralVoxelTerrain
{
    public sealed class TerrainRegionSnapshot
    {
        internal TerrainRegionSnapshot(
            Vector3Int minSample,
            Vector3Int maxSample,
            Vector3Int minCell,
            Vector3Int maxCell,
            float[] densityValues,
            byte[] materialValues,
            Bounds changedWorldBounds,
            List<Vector3Int> affectedChunkCoordinates)
        {
            MinSample = minSample;
            MaxSample = maxSample;
            MinCell = minCell;
            MaxCell = maxCell;
            DensityValues = densityValues ?? Array.Empty<float>();
            MaterialValues = materialValues ?? Array.Empty<byte>();
            ChangedWorldBounds = changedWorldBounds;
            AffectedChunkCoordinates = affectedChunkCoordinates ?? new List<Vector3Int>();
        }

        internal Vector3Int MinSample { get; }
        internal Vector3Int MaxSample { get; }
        internal Vector3Int MinCell { get; }
        internal Vector3Int MaxCell { get; }
        internal float[] DensityValues { get; }
        internal byte[] MaterialValues { get; }
        internal List<Vector3Int> AffectedChunkCoordinates { get; }
        public Bounds ChangedWorldBounds { get; }
    }
}
