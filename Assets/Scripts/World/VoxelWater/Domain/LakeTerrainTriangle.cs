using System.Collections.Generic;
using UnityEngine;

internal sealed class LakeTerrainTriangle
{
    public Vector3 a;
    public Vector3 b;
    public Vector3 c;
    public readonly List<LakeTriangleNeighbor> neighbors = new List<LakeTriangleNeighbor>(3);
    public readonly List<LakeTriangleEdge> boundaryEdges = new List<LakeTriangleEdge>(3);
}
