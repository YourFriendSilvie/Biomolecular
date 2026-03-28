using UnityEngine;

internal readonly struct LakePendingEdge
{
    public readonly int triangleIndex;
    public readonly Vector3 edgeA;
    public readonly Vector3 edgeB;

    public LakePendingEdge(int triangleIndex, Vector3 edgeA, Vector3 edgeB)
    {
        this.triangleIndex = triangleIndex;
        this.edgeA = edgeA;
        this.edgeB = edgeB;
    }
}
