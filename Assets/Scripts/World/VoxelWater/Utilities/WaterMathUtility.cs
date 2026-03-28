using UnityEngine;

internal static class WaterMathUtility
{
    public static float ComputeBoundsCornerRadiusXZ(Vector3 center, Bounds bounds)
    {
        Vector2 centerXZ = new Vector2(center.x, center.z);
        Vector2[] corners =
        {
            new Vector2(bounds.min.x, bounds.min.z),
            new Vector2(bounds.min.x, bounds.max.z),
            new Vector2(bounds.max.x, bounds.min.z),
            new Vector2(bounds.max.x, bounds.max.z)
        };

        float maxRadius = 0f;
        for (int i = 0; i < corners.Length; i++)
        {
            maxRadius = Mathf.Max(maxRadius, Vector2.Distance(centerXZ, corners[i]));
        }

        return maxRadius;
    }

    public static float DistancePointToTriangleXZ(Vector2 point, Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector2.Distance(point, ClosestPointOnTriangleXZ(point, a, b, c));
    }

    public static Vector2 ClosestPointOnTriangleXZ(Vector2 point, Vector3 aWorld, Vector3 bWorld, Vector3 cWorld)
    {
        Vector2 a = new Vector2(aWorld.x, aWorld.z);
        Vector2 b = new Vector2(bWorld.x, bWorld.z);
        Vector2 c = new Vector2(cWorld.x, cWorld.z);
        if (IsPointInTriangleXZ(point, a, b, c))
        {
            return point;
        }

        Vector2 pointOnAB = ClosestPointOnSegmentXZ(point, a, b, out _);
        Vector2 pointOnBC = ClosestPointOnSegmentXZ(point, b, c, out _);
        Vector2 pointOnCA = ClosestPointOnSegmentXZ(point, c, a, out _);
        float distanceAB = (pointOnAB - point).sqrMagnitude;
        float distanceBC = (pointOnBC - point).sqrMagnitude;
        float distanceCA = (pointOnCA - point).sqrMagnitude;
        if (distanceAB <= distanceBC && distanceAB <= distanceCA)
        {
            return pointOnAB;
        }

        return distanceBC <= distanceCA ? pointOnBC : pointOnCA;
    }

    public static float TriangleAreaXZ(Vector3 a, Vector3 b, Vector3 c)
    {
        return Mathf.Abs(((b.x - a.x) * (c.z - a.z)) - ((b.z - a.z) * (c.x - a.x))) * 0.5f;
    }

    public static float DistanceToSegmentXZ(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector2 pointXZ = new Vector2(point.x, point.z);
        Vector2 startXZ = new Vector2(start.x, start.z);
        Vector2 endXZ = new Vector2(end.x, end.z);
        Vector2 projection = ClosestPointOnSegmentXZ(pointXZ, startXZ, endXZ, out _);
        return Vector2.Distance(pointXZ, projection);
    }

    public static float DistanceToSegmentXZ(Vector3 point, Vector3 start, Vector3 end, out float t)
    {
        Vector2 pointXZ = new Vector2(point.x, point.z);
        Vector2 startXZ = new Vector2(start.x, start.z);
        Vector2 endXZ = new Vector2(end.x, end.z);
        Vector2 projection = ClosestPointOnSegmentXZ(pointXZ, startXZ, endXZ, out t);
        return Vector2.Distance(pointXZ, projection);
    }

    public static Vector2 ClosestPointOnSegmentXZ(Vector2 pointXZ, Vector2 startXZ, Vector2 endXZ, out float t)
    {
        Vector2 segment = endXZ - startXZ;
        float segmentLengthSquared = segment.sqrMagnitude;
        if (segmentLengthSquared <= 0.0001f)
        {
            t = 0f;
            return startXZ;
        }

        t = Mathf.Clamp01(Vector2.Dot(pointXZ - startXZ, segment) / segmentLengthSquared);
        return startXZ + (segment * t);
    }

    public static Vector3 EvaluateCubicBezier(Vector3 start, Vector3 controlOne, Vector3 controlTwo, Vector3 end, float t)
    {
        float oneMinusT = 1f - t;
        return (oneMinusT * oneMinusT * oneMinusT * start)
            + (3f * oneMinusT * oneMinusT * t * controlOne)
            + (3f * oneMinusT * t * t * controlTwo)
            + (t * t * t * end);
    }

    public static float NextFloat(System.Random random, float minInclusive, float maxInclusive)
    {
        if (Mathf.Approximately(minInclusive, maxInclusive))
        {
            return minInclusive;
        }

        float min = Mathf.Min(minInclusive, maxInclusive);
        float max = Mathf.Max(minInclusive, maxInclusive);
        return (float)(min + (random.NextDouble() * (max - min)));
    }

    private static bool IsPointInTriangleXZ(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float signAB = SignTriangle(point, a, b);
        float signBC = SignTriangle(point, b, c);
        float signCA = SignTriangle(point, c, a);
        bool hasNegative = signAB < 0f || signBC < 0f || signCA < 0f;
        bool hasPositive = signAB > 0f || signBC > 0f || signCA > 0f;
        return !(hasNegative && hasPositive);
    }

    private static float SignTriangle(Vector2 point, Vector2 a, Vector2 b)
    {
        return ((point.x - b.x) * (a.y - b.y)) - ((a.x - b.x) * (point.y - b.y));
    }
}
