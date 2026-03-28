using System.Collections.Generic;
using UnityEngine;

internal sealed class LakeTerrainPatch
{
    public readonly List<LakeTerrainTriangle> triangles = new List<LakeTerrainTriangle>();
    public readonly HashSet<Vector3Int> chunkCoordinates = new HashSet<Vector3Int>();
    public float minHeight = float.PositiveInfinity;
    public float maxHeight = float.NegativeInfinity;
    public Bounds bounds;
    public bool hasBounds;
}
