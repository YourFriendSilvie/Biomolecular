using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Burst-compiled parallel job that filters terrain triangles for lake surface solving.
///
/// Each triangle is tested against two independent criteria:
///   1. Normal direction: the face must be approximately upward-facing
///      (normal.y / |normal| >= 0.12) to exclude vertical walls.
///   2. XZ proximity: the closest point on the triangle in the XZ plane must
///      be within <see cref="InclusionRadius"/> of <see cref="LakeCenterXZ"/>.
///
/// Input vertices are world-space, laid out flat: for triangle i the three
/// vertices occupy indices i*3, i*3+1, i*3+2 of <see cref="WorldVertices"/>.
/// </summary>
[BurstCompile]
internal struct LakeTriangleFilterJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> WorldVertices;
    public float2 LakeCenterXZ;
    public float InclusionRadius;

    [WriteOnly] public NativeArray<bool> Accepted;

    public void Execute(int triangleIndex)
    {
        float3 a = WorldVertices[triangleIndex * 3];
        float3 b = WorldVertices[triangleIndex * 3 + 1];
        float3 c = WorldVertices[triangleIndex * 3 + 2];

        float3 normal = math.cross(b - a, c - a);
        float normalMag = math.length(normal);
        if (normalMag <= 0.0001f || (normal.y / normalMag) < 0.12f)
        {
            Accepted[triangleIndex] = false;
            return;
        }

        float dist = DistToTriangleXZ(LakeCenterXZ, new float2(a.x, a.z), new float2(b.x, b.z), new float2(c.x, c.z));
        Accepted[triangleIndex] = dist <= InclusionRadius;
    }

    // -------------------------------------------------------------------------
    // Inlined 2-D geometry helpers (matches WaterMathUtility logic exactly).
    // Static methods with value-type parameters are Burst-friendly.
    // -------------------------------------------------------------------------

    private static float DistToTriangleXZ(float2 point, float2 a, float2 b, float2 c)
    {
        return math.distance(point, ClosestOnTriangle(point, a, b, c));
    }

    private static float2 ClosestOnTriangle(float2 point, float2 a, float2 b, float2 c)
    {
        if (IsPointInTriangle(point, a, b, c))
        {
            return point;
        }

        float2 pAB = ClosestOnSegment(point, a, b);
        float2 pBC = ClosestOnSegment(point, b, c);
        float2 pCA = ClosestOnSegment(point, c, a);
        float dAB = math.distancesq(pAB, point);
        float dBC = math.distancesq(pBC, point);
        float dCA = math.distancesq(pCA, point);

        if (dAB <= dBC && dAB <= dCA)
        {
            return pAB;
        }

        return dBC <= dCA ? pBC : pCA;
    }

    private static bool IsPointInTriangle(float2 p, float2 a, float2 b, float2 c)
    {
        float s1 = TriSign(p, a, b);
        float s2 = TriSign(p, b, c);
        float s3 = TriSign(p, c, a);
        bool hasNeg = s1 < 0f || s2 < 0f || s3 < 0f;
        bool hasPos = s1 > 0f || s2 > 0f || s3 > 0f;
        return !(hasNeg && hasPos);
    }

    private static float TriSign(float2 p, float2 a, float2 b)
    {
        return ((p.x - b.x) * (a.y - b.y)) - ((a.x - b.x) * (p.y - b.y));
    }

    private static float2 ClosestOnSegment(float2 point, float2 start, float2 end)
    {
        float2 seg = end - start;
        float lenSq = math.dot(seg, seg);
        if (lenSq <= 0.0001f)
        {
            return start;
        }

        float t = math.clamp(math.dot(point - start, seg) / lenSq, 0f, 1f);
        return start + seg * t;
    }
}
