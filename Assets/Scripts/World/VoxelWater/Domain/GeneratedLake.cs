using System;
using UnityEngine;

internal sealed class GeneratedLake
{
    public Vector3 center;
    public float radius;
    public float surfaceY;
    public float storedVolumeCubicMeters;
    public float captureRadius;
    public float[] shorelineRadii = Array.Empty<float>();
    public float gridCellSize;
    public int gridCountPerAxis;
    public Vector2 gridOriginXZ;
    public float[] cellHeights = Array.Empty<float>();
    public bool[] floodedCells = Array.Empty<bool>();
    public Vector3[] surfaceVertices = Array.Empty<Vector3>();
    public int[] surfaceTriangles = Array.Empty<int>();
    public LakeTerrainPatch terrainPatch;
    public Bounds surfaceBounds;
    public int floodedCellCount;
    public Bounds influenceBounds;
    public GameObject waterObject;
    public bool isPond;
}
