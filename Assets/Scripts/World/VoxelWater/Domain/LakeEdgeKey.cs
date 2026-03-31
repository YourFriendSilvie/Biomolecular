using System;

internal readonly struct LakeEdgeKey : IEquatable<LakeEdgeKey>
{
    public readonly LakeVertexKey a;
    public readonly LakeVertexKey b;

    public LakeEdgeKey(LakeVertexKey first, LakeVertexKey second)
    {
        if (LakeVertexKey.Compare(first, second) <= 0)
        {
            a = first;
            b = second;
        }
        else
        {
            a = second;
            b = first;
        }
    }

    public bool Equals(LakeEdgeKey other)
    {
        return a.Equals(other.a) && b.Equals(other.b);
    }

    public override bool Equals(object obj)
    {
        return obj is LakeEdgeKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (a.GetHashCode() * 397) ^ b.GetHashCode();
        }
    }
}
