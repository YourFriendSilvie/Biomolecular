using System;

internal readonly struct LakeVertexKey : IEquatable<LakeVertexKey>
{
    public readonly int x;
    public readonly int y;
    public readonly int z;

    public LakeVertexKey(int x, int y, int z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public bool Equals(LakeVertexKey other)
    {
        return x == other.x && y == other.y && z == other.z;
    }

    public override bool Equals(object obj)
    {
        return obj is LakeVertexKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + x;
            hash = (hash * 31) + y;
            hash = (hash * 31) + z;
            return hash;
        }
    }

    public static int Compare(LakeVertexKey first, LakeVertexKey second)
    {
        if (first.x != second.x)
        {
            return first.x.CompareTo(second.x);
        }

        if (first.y != second.y)
        {
            return first.y.CompareTo(second.y);
        }

        return first.z.CompareTo(second.z);
    }
}
