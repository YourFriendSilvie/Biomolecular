using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct PlantFoliageAnchor
{
    public readonly Vector3 position;
    public readonly Vector3 direction;

    public PlantFoliageAnchor(Vector3 position, Vector3 direction)
    {
        this.position = position;
        this.direction = direction.sqrMagnitude < 0.0001f ? Vector3.up : direction.normalized;
    }
}

public static class ProceduralPlantUtility
{
    public static List<Vector3> BuildBranchPath(
        System.Random random,
        Vector3 branchStart,
        Vector3 branchDirection,
        float branchLength,
        int segmentCount,
        float bendStrength,
        bool evergreen,
        float normalizedInCrown)
    {
        List<Vector3> pathPoints = new List<Vector3>(Mathf.Max(2, segmentCount) + 1);
        Vector3 side = Vector3.Cross(Vector3.up, branchDirection);
        if (side.sqrMagnitude < 0.0001f)
        {
            side = Vector3.right;
        }

        side.Normalize();

        float lateralBend = branchLength * bendStrength * NextFloat(random, -1f, 1f);
        float tipVerticalBias = evergreen
            ? Mathf.Lerp(-0.18f, 0.02f, normalizedInCrown)
            : Mathf.Lerp(0.02f, 0.18f, normalizedInCrown);
        Vector3 controlPointOne = branchStart
            + (branchDirection * branchLength * 0.28f)
            + (side * lateralBend * 0.4f)
            + (Vector3.up * branchLength * 0.05f);
        Vector3 controlPointTwo = branchStart
            + (branchDirection * branchLength * 0.68f)
            - (side * lateralBend * 0.7f)
            + (Vector3.up * branchLength * tipVerticalBias * 0.6f);
        Vector3 tipPoint = branchStart
            + (branchDirection * branchLength)
            + (Vector3.up * branchLength * tipVerticalBias);

        int steps = Mathf.Max(2, segmentCount);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            pathPoints.Add(EvaluateCubicBezier(branchStart, controlPointOne, controlPointTwo, tipPoint, t));
        }

        return pathPoints;
    }

    public static void AddFoliageAnchors(ICollection<PlantFoliageAnchor> foliageAnchors, IReadOnlyList<Vector3> branchPath, params float[] sampleTs)
    {
        if (foliageAnchors == null || branchPath == null || branchPath.Count == 0 || sampleTs == null)
        {
            return;
        }

        foreach (float sampleT in sampleTs)
        {
            foliageAnchors.Add(new PlantFoliageAnchor(SamplePath(branchPath, sampleT), GetPathTangent(branchPath, sampleT)));
        }
    }

    public static Quaternion CreateBladeRotation(Vector3 direction, float twistDegrees)
    {
        Vector3 normalizedDirection = direction.sqrMagnitude < 0.0001f ? Vector3.up : direction.normalized;
        return Quaternion.AngleAxis(twistDegrees, normalizedDirection) * Quaternion.FromToRotation(Vector3.up, normalizedDirection);
    }

    public static Quaternion CreateFlatFoliageRotation(Vector3 direction, Vector3 planeNormal)
    {
        Vector3 up = direction.sqrMagnitude < 0.0001f ? Vector3.up : direction.normalized;
        Vector3 forward = Vector3.ProjectOnPlane(planeNormal, up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = GetPerpendicular(up);
        }

        forward.Normalize();
        return Quaternion.LookRotation(forward, up);
    }

    public static Vector3 GetPerpendicular(Vector3 axis)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
        Vector3 perpendicular = Vector3.Cross(axis, reference);
        return perpendicular.sqrMagnitude < 0.0001f ? Vector3.forward : perpendicular.normalized;
    }

    public static List<float> BuildRadiusProfile(float baseRadius, float tipRadius, int sampleCount)
    {
        List<float> radii = new List<float>(sampleCount);
        int count = Mathf.Max(2, sampleCount);

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            radii.Add(Mathf.Lerp(baseRadius, tipRadius, Mathf.Pow(t, 0.85f)));
        }

        return radii;
    }

    public static Vector3 SamplePath(IReadOnlyList<Vector3> pathPoints, float t)
    {
        if (pathPoints == null || pathPoints.Count == 0)
        {
            return Vector3.zero;
        }

        if (pathPoints.Count == 1)
        {
            return pathPoints[0];
        }

        float scaledIndex = Mathf.Clamp01(t) * (pathPoints.Count - 1);
        int lowerIndex = Mathf.FloorToInt(scaledIndex);
        int upperIndex = Mathf.Min(pathPoints.Count - 1, lowerIndex + 1);
        float blend = scaledIndex - lowerIndex;
        return Vector3.Lerp(pathPoints[lowerIndex], pathPoints[upperIndex], blend);
    }

    public static Vector3 GetPathTangent(IReadOnlyList<Vector3> pathPoints, float t)
    {
        float offset = 1f / Mathf.Max(4f, pathPoints.Count - 1f);
        Vector3 before = SamplePath(pathPoints, Mathf.Clamp01(t - offset));
        Vector3 after = SamplePath(pathPoints, Mathf.Clamp01(t + offset));
        Vector3 tangent = after - before;
        return tangent.sqrMagnitude < 0.0001f ? Vector3.up : tangent.normalized;
    }

    public static Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float oneMinusT = 1f - t;
        return (oneMinusT * oneMinusT * oneMinusT * p0)
             + (3f * oneMinusT * oneMinusT * t * p1)
             + (3f * oneMinusT * t * t * p2)
             + (t * t * t * p3);
    }

    public static Vector3 RandomInsideUnitSphere(System.Random random)
    {
        Vector3 vector = new Vector3(
            NextFloat(random, -1f, 1f),
            NextFloat(random, -1f, 1f),
            NextFloat(random, -1f, 1f));
        return vector.sqrMagnitude > 0.0001f ? vector.normalized * NextFloat(random, 0f, 1f) : Vector3.up * NextFloat(random, 0f, 1f);
    }

    public static Vector3 RandomOnUnitSphere(System.Random random)
    {
        Vector3 direction = RandomInsideUnitSphere(random);
        return direction.sqrMagnitude < 0.0001f ? Vector3.up : direction.normalized;
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

    public static int NextIntInclusive(System.Random random, Vector2Int range)
    {
        int min = Mathf.Min(range.x, range.y);
        int max = Mathf.Max(range.x, range.y);
        return random.Next(min, max + 1);
    }
}
