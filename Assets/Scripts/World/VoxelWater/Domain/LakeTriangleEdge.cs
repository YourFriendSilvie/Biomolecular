using UnityEngine;

internal readonly struct LakeTriangleEdge
{
    public readonly Vector3 edgeA;
    public readonly Vector3 edgeB;

    public LakeTriangleEdge(Vector3 edgeA, Vector3 edgeB)
    {
        this.edgeA = edgeA;
        this.edgeB = edgeB;
    }
}
