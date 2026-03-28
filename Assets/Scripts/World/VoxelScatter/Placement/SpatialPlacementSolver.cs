using System.Collections.Generic;
using UnityEngine;

internal static class SpatialPlacementSolver
{
    internal static Vector3 NormalizeSurfaceNormal(Vector3 surfaceNormal)
    {
        return surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;
    }

    internal static bool IsWithinPrototypeSurfaceConstraints(
        Vector3 surfacePoint,
        Vector3 surfaceNormal,
        TerrainScatterPrototype prototype,
        Bounds terrainBounds)
    {
        float normalizedHeight = terrainBounds.size.y <= 0.0001f
            ? 0f
            : Mathf.Clamp01((surfacePoint.y - terrainBounds.min.y) / terrainBounds.size.y);
        if (normalizedHeight < prototype.normalizedHeightRange.x || normalizedHeight > prototype.normalizedHeightRange.y)
        {
            return false;
        }

        float slope = Vector3.Angle(NormalizeSurfaceNormal(surfaceNormal), Vector3.up);
        return slope >= prototype.slopeDegreesRange.x && slope <= prototype.slopeDegreesRange.y;
    }

    internal static bool IsFarEnoughFromExisting(Vector3 position, IReadOnlyList<Vector3> existingPositions, float minimumSpacingMeters)
    {
        if (minimumSpacingMeters <= 0f || existingPositions == null || existingPositions.Count == 0)
        {
            return true;
        }

        float minimumSpacingSquared = minimumSpacingMeters * minimumSpacingMeters;
        for (int i = 0; i < existingPositions.Count; i++)
        {
            Vector3 planarDelta = Vector3.ProjectOnPlane(position - existingPositions[i], Vector3.up);
            if (planarDelta.sqrMagnitude < minimumSpacingSquared)
            {
                return false;
            }
        }

        return true;
    }

    internal static float GetPrimitiveHalfHeight(PrimitiveType primitiveType, Vector3 scale)
    {
        switch (primitiveType)
        {
            case PrimitiveType.Cylinder:
            case PrimitiveType.Capsule:
                return scale.y;

            default:
                return scale.y * 0.5f;
        }
    }

    internal static int CalculateAdaptiveMaxAttempts(TerrainScatterPrototype prototype, Bounds bounds)
    {
        if (prototype == null)
        {
            return 0;
        }

        int requestedCount = Mathf.Max(1, prototype.spawnCount);
        int baseAttemptBudget = requestedCount * Mathf.Max(1, prototype.maxPlacementAttemptsPerInstance);
        float heightAcceptance = Mathf.Max(0.1f, prototype.normalizedHeightRange.y - prototype.normalizedHeightRange.x);
        float slopeAcceptance = Mathf.Max(0.1f, (prototype.slopeDegreesRange.y - prototype.slopeDegreesRange.x) / 90f);
        float densityAcceptance = Mathf.Max(0.08f, 1f - prototype.densityThreshold);
        float horizontalArea = Mathf.Max(1f, bounds.size.x * bounds.size.z);
        float spacingFootprint = prototype.minimumSpacingMeters <= 0.001f
            ? 1f
            : prototype.minimumSpacingMeters * prototype.minimumSpacingMeters * requestedCount * 0.75f;
        float spacingAcceptance = prototype.minimumSpacingMeters <= 0.001f
            ? 1f
            : Mathf.Clamp(horizontalArea / Mathf.Max(1f, spacingFootprint), 0.35f, 1f);
        float estimatedAcceptance = Mathf.Clamp(heightAcceptance * slopeAcceptance * densityAcceptance * spacingAcceptance, 0.02f, 1f);
        int adaptiveAttemptBudget = Mathf.CeilToInt(requestedCount / estimatedAcceptance);
        int hardCap = Mathf.Max(baseAttemptBudget, requestedCount * Mathf.Max(24, prototype.maxPlacementAttemptsPerInstance * 6));
        return Mathf.Clamp(Mathf.Max(baseAttemptBudget, adaptiveAttemptBudget), baseAttemptBudget, hardCap);
    }

    internal static float NextScatterSample01(int sampleIndex, int sequenceBase, float offset)
    {
        return Mathf.Repeat(CalculateHaltonValue(Mathf.Max(1, sampleIndex), Mathf.Max(2, sequenceBase)) + offset, 1f);
    }

    internal static float NextFloat(System.Random random, float minInclusive, float maxInclusive)
    {
        if (Mathf.Approximately(minInclusive, maxInclusive))
        {
            return minInclusive;
        }

        float min = Mathf.Min(minInclusive, maxInclusive);
        float max = Mathf.Max(minInclusive, maxInclusive);
        return (float)(min + (random.NextDouble() * (max - min)));
    }

    internal static Vector3 NextVector3(System.Random random, Vector3 min, Vector3 max)
    {
        return new Vector3(
            NextFloat(random, min.x, max.x),
            NextFloat(random, min.y, max.y),
            NextFloat(random, min.z, max.z));
    }

    private static float CalculateHaltonValue(int index, int sequenceBase)
    {
        float result = 0f;
        float fraction = 1f / sequenceBase;
        int currentIndex = Mathf.Max(1, index);
        int currentBase = Mathf.Max(2, sequenceBase);

        while (currentIndex > 0)
        {
            result += (currentIndex % currentBase) * fraction;
            currentIndex /= currentBase;
            fraction /= currentBase;
        }

        return result;
    }
}
