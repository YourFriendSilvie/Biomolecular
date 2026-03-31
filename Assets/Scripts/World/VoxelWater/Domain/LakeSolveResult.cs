using System;
using UnityEngine;

internal sealed class LakeSolveResult
{
    public float surfaceY;
    public float volumeCubicMeters;
    public bool touchesOpenBoundary;
    public float cellSize;
    public int cellCountPerAxis;
    public Vector2 originXZ;
    public float[] cellHeights = Array.Empty<float>();
    public bool[] floodedCells = Array.Empty<bool>();
    public Vector3[] surfaceVertices = Array.Empty<Vector3>();
    public int[] surfaceTriangles = Array.Empty<int>();
    public Bounds surfaceBounds;
    public int floodedCellCount;
}
