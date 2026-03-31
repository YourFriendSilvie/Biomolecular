using System.Collections.Generic;
using UnityEngine;
using static WaterMathUtility;

/// <summary>
/// Pure static spatial-query helpers that answer "is point inside water?" and
/// "find closest water surface" questions without owning any mutable state.
/// </summary>
internal static class WaterSpatialQueryUtility
{
    public static bool HasLakeSurfaceGeometry(GeneratedLake lake)
    {
        return lake != null &&
               lake.surfaceVertices != null &&
               lake.surfaceTriangles != null &&
               lake.surfaceTriangles.Length >= 3;
    }

    public static bool IsLakeActive(GeneratedLake lake, float minRenderableVolume)
    {
        return lake != null &&
               lake.storedVolumeCubicMeters > minRenderableVolume &&
               HasLakeSurfaceGeometry(lake) &&
               lake.floodedCellCount > 0;
    }

    public static bool ContainsPointOnLakeSurface(GeneratedLake lake, float worldX, float worldZ, float paddingMeters)
    {
        if (!HasLakeSurfaceGeometry(lake))
        {
            return false;
        }

        if (lake.surfaceBounds.size.x > 0f || lake.surfaceBounds.size.z > 0f)
        {
            Bounds paddedBounds = lake.surfaceBounds;
            paddedBounds.Expand(new Vector3(paddingMeters * 2f, 0f, paddingMeters * 2f));
            if (worldX < paddedBounds.min.x ||
                worldX > paddedBounds.max.x ||
                worldZ < paddedBounds.min.z ||
                worldZ > paddedBounds.max.z)
            {
                return false;
            }
        }

        Vector2 worldPointXZ = new Vector2(worldX, worldZ);
        float maxDistanceSquared = Mathf.Max(0f, paddingMeters) * Mathf.Max(0f, paddingMeters);
        for (int triangleOffset = 0; triangleOffset + 2 < lake.surfaceTriangles.Length; triangleOffset += 3)
        {
            Vector3 a = lake.surfaceVertices[lake.surfaceTriangles[triangleOffset]];
            Vector3 b = lake.surfaceVertices[lake.surfaceTriangles[triangleOffset + 1]];
            Vector3 c = lake.surfaceVertices[lake.surfaceTriangles[triangleOffset + 2]];
            Vector2 closestPoint = ClosestPointOnTriangleXZ(worldPointXZ, a, b, c);
            if ((closestPoint - worldPointXZ).sqrMagnitude <= maxDistanceSquared + 0.0001f)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetClosestPointOnLakeSurface(GeneratedLake lake, Vector3 worldPoint, out Vector3 closestPoint, out float planarDistanceMeters)
    {
        closestPoint = Vector3.zero;
        planarDistanceMeters = float.PositiveInfinity;
        if (!HasLakeSurfaceGeometry(lake))
        {
            return false;
        }

        Vector2 worldPointXZ = new Vector2(worldPoint.x, worldPoint.z);
        Vector2 closestPointXZ = default;
        float closestDistanceSquared = float.PositiveInfinity;
        bool found = false;
        for (int triangleOffset = 0; triangleOffset + 2 < lake.surfaceTriangles.Length; triangleOffset += 3)
        {
            Vector3 a = lake.surfaceVertices[lake.surfaceTriangles[triangleOffset]];
            Vector3 b = lake.surfaceVertices[lake.surfaceTriangles[triangleOffset + 1]];
            Vector3 c = lake.surfaceVertices[lake.surfaceTriangles[triangleOffset + 2]];
            Vector2 candidatePointXZ = ClosestPointOnTriangleXZ(worldPointXZ, a, b, c);
            float candidateDistanceSquared = (candidatePointXZ - worldPointXZ).sqrMagnitude;
            if (candidateDistanceSquared >= closestDistanceSquared)
            {
                continue;
            }

            closestDistanceSquared = candidateDistanceSquared;
            closestPointXZ = candidatePointXZ;
            found = true;
        }

        if (!found)
        {
            return false;
        }

        planarDistanceMeters = Mathf.Sqrt(Mathf.Max(0f, closestDistanceSquared));
        closestPoint = new Vector3(closestPointXZ.x, lake.surfaceY, closestPointXZ.y);
        return true;
    }

    public static bool TryResolveLakeNearPoint(
        IReadOnlyList<GeneratedLake> lakes,
        Vector3 worldPoint,
        float pointPaddingMeters,
        float minRenderableVolume,
        out GeneratedLake lake)
    {
        lake = null;
        float bestScore = float.PositiveInfinity;
        float searchPaddingMeters = Mathf.Max(0f, pointPaddingMeters);

        for (int i = 0; i < lakes.Count; i++)
        {
            GeneratedLake candidate = lakes[i];
            if (!IsLakeActive(candidate, minRenderableVolume) ||
                !TryGetClosestPointOnLakeSurface(candidate, worldPoint, out _, out float planarDistanceMeters))
            {
                continue;
            }

            bool containsPoint = ContainsPointOnLakeSurface(candidate, worldPoint.x, worldPoint.z, searchPaddingMeters);
            if (!containsPoint && planarDistanceMeters > searchPaddingMeters + 0.001f)
            {
                continue;
            }

            float score = planarDistanceMeters + (Mathf.Abs(worldPoint.y - candidate.surfaceY) * 0.1f);
            if (containsPoint)
            {
                score -= 0.5f;
            }

            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            lake = candidate;
        }

        return lake != null;
    }

    public static bool IsPointUnderWater(
        Vector3 worldPoint,
        float paddingMeters,
        IReadOnlyList<GeneratedLake> lakes,
        IReadOnlyList<GeneratedRiverSegment> riverSegments,
        float minRenderableVolume,
        bool hasOcean,
        Bounds oceanBounds,
        float oceanSurfaceY)
    {
        if (hasOcean &&
            worldPoint.x >= oceanBounds.min.x &&
            worldPoint.x <= oceanBounds.max.x &&
            worldPoint.z >= oceanBounds.min.z &&
            worldPoint.z <= oceanBounds.max.z &&
            worldPoint.y <= oceanSurfaceY + paddingMeters)
        {
            return true;
        }

        for (int i = 0; i < lakes.Count; i++)
        {
            GeneratedLake lake = lakes[i];
            if (!IsLakeActive(lake, minRenderableVolume))
            {
                continue;
            }

            if (worldPoint.y <= lake.surfaceY + paddingMeters &&
                ContainsPointOnLakeSurface(lake, worldPoint.x, worldPoint.z, paddingMeters))
            {
                return true;
            }
        }

        for (int i = 0; i < riverSegments.Count; i++)
        {
            GeneratedRiverSegment segment = riverSegments[i];
            if (DistanceToSegmentXZ(worldPoint, segment.start, segment.end, out float projectionT) <=
                    (segment.width * 0.5f) + paddingMeters &&
                worldPoint.y <= Mathf.Lerp(segment.start.y, segment.end.y, projectionT) + paddingMeters)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetNearestFreshwaterPoint(
        Vector3 worldPoint,
        IReadOnlyList<GeneratedLake> lakes,
        IReadOnlyList<GeneratedRiverSegment> riverSegments,
        float minRenderableVolume,
        out Vector3 nearestPoint,
        out float distanceMeters)
    {
        nearestPoint = Vector3.zero;
        distanceMeters = float.PositiveInfinity;
        bool found = false;

        Vector2 pointXZ = new Vector2(worldPoint.x, worldPoint.z);
        for (int i = 0; i < lakes.Count; i++)
        {
            GeneratedLake lake = lakes[i];
            if (!IsLakeActive(lake, minRenderableVolume))
            {
                continue;
            }

            for (int triangleOffset = 0; triangleOffset + 2 < lake.surfaceTriangles.Length; triangleOffset += 3)
            {
                Vector3 a = lake.surfaceVertices[lake.surfaceTriangles[triangleOffset]];
                Vector3 b = lake.surfaceVertices[lake.surfaceTriangles[triangleOffset + 1]];
                Vector3 c = lake.surfaceVertices[lake.surfaceTriangles[triangleOffset + 2]];
                Vector2 closestPointXZ = ClosestPointOnTriangleXZ(pointXZ, a, b, c);
                Vector3 candidatePoint = new Vector3(closestPointXZ.x, lake.surfaceY, closestPointXZ.y);
                float candidateDistance = Vector3.Distance(worldPoint, candidatePoint);
                if (candidateDistance >= distanceMeters)
                {
                    continue;
                }

                distanceMeters = candidateDistance;
                nearestPoint = candidatePoint;
                found = true;
            }
        }

        for (int i = 0; i < riverSegments.Count; i++)
        {
            GeneratedRiverSegment segment = riverSegments[i];
            Vector2 projectedXZ = ClosestPointOnSegmentXZ(
                pointXZ,
                new Vector2(segment.start.x, segment.start.z),
                new Vector2(segment.end.x, segment.end.z),
                out float projectionT);
            float centerlineDistance = Vector2.Distance(pointXZ, projectedXZ);
            float candidateDistance = Mathf.Abs(centerlineDistance - (segment.width * 0.5f));
            if (candidateDistance >= distanceMeters)
            {
                continue;
            }

            distanceMeters = candidateDistance;
            nearestPoint = new Vector3(
                projectedXZ.x,
                Mathf.Lerp(segment.start.y, segment.end.y, projectionT),
                projectedXZ.y);
            found = true;
        }

        return found;
    }
}
