using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure static geometry/math helpers shared across start-area generation.
/// No MonoBehaviour dependencies — safe to call from editor or runtime code.
/// </summary>
public static class VoxelBrushUtility
{
    /// <summary>
    /// Returns the normalised surface normal, falling back to Vector3.up when the input is degenerate.
    /// </summary>
    public static Vector3 NormalizeSurfaceNormal(Vector3 surfaceNormal)
    {
        return surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;
    }

    /// <summary>
    /// Draws a uniform float in [minInclusive, maxInclusive] from a seeded System.Random.
    /// </summary>
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

    /// <summary>
    /// Returns true when <paramref name="position"/> is at least <paramref name="minimumSpacingMeters"/>
    /// away (planar) from every position in <paramref name="existingPositions"/>.
    /// </summary>
    public static bool IsFarEnoughFromPlaced(Vector3 position, IReadOnlyList<Vector3> existingPositions, float minimumSpacingMeters)
    {
        if (existingPositions == null || existingPositions.Count == 0)
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

    /// <summary>
    /// Scores [0, 1] how far <paramref name="position"/> is from the nearest edge of <paramref name="bounds"/>.
    /// Interior positions score higher.
    /// </summary>
    public static float EvaluateEdgeScore(Vector3 position, Bounds bounds)
    {
        float minDistance = Mathf.Min(
            position.x - bounds.min.x,
            bounds.max.x - position.x,
            position.z - bounds.min.z,
            bounds.max.z - position.z);
        return Mathf.Clamp01(minDistance / Mathf.Max(1f, Mathf.Min(bounds.size.x, bounds.size.z) * 0.25f));
    }

    /// <summary>
    /// Returns true when <paramref name="root"/> is non-null and has at least one child Transform.
    /// </summary>
    public static bool HasGeneratedChildren(Transform root)
    {
        return root != null && root.childCount > 0;
    }
}
