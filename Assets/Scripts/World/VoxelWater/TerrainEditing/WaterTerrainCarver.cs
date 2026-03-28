using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles all voxel terrain editing (carving and material painting) on behalf of the water system.
/// All methods are stateless and accept the terrain and brush parameters directly.
/// </summary>
public static class WaterTerrainCarver
{
    public static void CarveLake(
        ProceduralVoxelTerrain terrain,
        Vector3 center,
        float radiusMeters,
        float surfaceY,
        float depthMeters,
        bool isPond,
        bool paintBasinMaterials = true)
    {
        if (terrain == null)
        {
            return;
        }

        terrain.BeginBulkEdit();
        try
        {
            ApplyLakeCarveDensity(terrain, center, radiusMeters, surfaceY, depthMeters, isPond, -1f);

            if (paintBasinMaterials)
            {
                PaintFreshwaterBasinMaterials(terrain, center, radiusMeters, surfaceY, depthMeters, isPond);
            }
        }
        finally
        {
            terrain.EndBulkEdit();
        }
    }

    public static void RestoreCarvedLake(
        ProceduralVoxelTerrain terrain,
        Vector3 center,
        float radiusMeters,
        float surfaceY,
        float depthMeters,
        bool isPond)
    {
        if (terrain == null)
        {
            return;
        }

        terrain.BeginBulkEdit();
        try
        {
            ApplyLakeCarveDensity(terrain, center, radiusMeters, surfaceY, depthMeters, isPond, 1f);
        }
        finally
        {
            terrain.EndBulkEdit();
        }
    }

    public static void ApplyLakeCarveDensity(
        ProceduralVoxelTerrain terrain,
        Vector3 center,
        float radiusMeters,
        float surfaceY,
        float depthMeters,
        bool isPond,
        float densitySign)
    {
        if (terrain == null || Mathf.Abs(densitySign) <= 0.0001f)
        {
            return;
        }

        Vector3 basinCenter = new Vector3(center.x, surfaceY - (depthMeters * 0.58f), center.z);
        float centerRadiusMultiplier = isPond ? 0.7f : 0.62f;
        float centerDepthMultiplier = isPond ? 1.3f : 1.15f;
        terrain.ApplyDensityBrushWorld(
            basinCenter,
            radiusMeters * centerRadiusMultiplier,
            densitySign * depthMeters * centerDepthMultiplier,
            false);

        for (int i = 0; i < 8; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / 8f;
            Vector3 innerOffset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * (radiusMeters * (isPond ? 0.28f : 0.34f));
            terrain.ApplyDensityBrushWorld(
                new Vector3(center.x + innerOffset.x, surfaceY - (depthMeters * (isPond ? 0.5f : 0.42f)), center.z + innerOffset.z),
                radiusMeters * (isPond ? 0.28f : 0.34f),
                densitySign * depthMeters * (isPond ? 0.82f : 0.7f),
                false);
        }

        for (int i = 0; i < 12; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / 12f;
            Vector3 outerOffset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * (radiusMeters * (isPond ? 0.68f : 0.72f));
            terrain.ApplyDensityBrushWorld(
                new Vector3(center.x + outerOffset.x, surfaceY - (depthMeters * (isPond ? 0.18f : 0.12f)), center.z + outerOffset.z),
                radiusMeters * (isPond ? 0.16f : 0.18f),
                densitySign * depthMeters * (isPond ? 0.32f : 0.26f),
                false);
        }

        for (int i = 0; i < 16; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / 16f;
            Vector3 shorelineOffset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * (radiusMeters * 0.86f);
            terrain.ApplyDensityBrushWorld(
                new Vector3(center.x + shorelineOffset.x, surfaceY - 0.08f, center.z + shorelineOffset.z),
                radiusMeters * 0.12f,
                densitySign * depthMeters * 0.18f,
                false);
        }
    }

    public static void BuildMergeTestRidge(
        ProceduralVoxelTerrain terrain,
        Vector3 firstCenter,
        Vector3 secondCenter,
        float surfaceY,
        float shorelineGapMeters,
        float ridgeHeightMeters)
    {
        if (terrain == null)
        {
            return;
        }

        float ridgeWidthMeters = Mathf.Max(terrain.VoxelSizeMeters * 1.2f, shorelineGapMeters * 0.75f);
        int ridgeSampleCount = Mathf.Max(3, Mathf.CeilToInt(Vector3.Distance(firstCenter, secondCenter) / Mathf.Max(terrain.VoxelSizeMeters, ridgeWidthMeters * 0.8f)));
        terrain.BeginBulkEdit();
        try
        {
            for (int i = 0; i < ridgeSampleCount; i++)
            {
                float t = ridgeSampleCount <= 1 ? 0.5f : i / (float)(ridgeSampleCount - 1);
                if (t < 0.2f || t > 0.8f)
                {
                    continue;
                }

                Vector3 ridgePoint = Vector3.Lerp(firstCenter, secondCenter, t);
                ridgePoint.y = surfaceY + (ridgeHeightMeters * 0.22f);
                terrain.ApplyDensityBrushWorld(ridgePoint, ridgeWidthMeters, ridgeHeightMeters * 1.2f, false);
            }
        }
        finally
        {
            terrain.EndBulkEdit();
        }
    }

    public static void CarveRiver(
        ProceduralVoxelTerrain terrain,
        IReadOnlyList<Vector3> samples,
        float widthMeters,
        float depthMeters,
        float riverCarveStepMeters)
    {
        if (terrain == null || samples == null || samples.Count == 0)
        {
            return;
        }

        terrain.BeginBulkEdit();
        try
        {
            for (int i = 0; i < samples.Count; i++)
            {
                float t = samples.Count <= 1 ? 0f : i / (float)(samples.Count - 1);
                float width = Mathf.Lerp(widthMeters * 0.86f, widthMeters * 1.08f, t);
                float centerDepth = Mathf.Lerp(depthMeters * 0.82f, depthMeters * 1.04f, Mathf.SmoothStep(0f, 1f, t));
                float brushRadius = Mathf.Max(width * 0.56f, riverCarveStepMeters * 0.5f);
                terrain.ApplyDensityBrushWorld(samples[i] - new Vector3(0f, centerDepth * 0.42f, 0f), brushRadius, -centerDepth * 1.18f, false);

                Vector3 tangent;
                if (i == 0)
                {
                    tangent = Vector3.ProjectOnPlane(samples[Mathf.Min(i + 1, samples.Count - 1)] - samples[i], Vector3.up);
                }
                else if (i == samples.Count - 1)
                {
                    tangent = Vector3.ProjectOnPlane(samples[i] - samples[i - 1], Vector3.up);
                }
                else
                {
                    tangent = Vector3.ProjectOnPlane(samples[i + 1] - samples[i - 1], Vector3.up);
                }

                if (tangent.sqrMagnitude > 0.0001f)
                {
                    Vector3 bankDirection = Vector3.Cross(Vector3.up, tangent.normalized);
                    terrain.ApplyDensityBrushWorld(samples[i] + (bankDirection * width * 0.32f) - new Vector3(0f, depthMeters * 0.15f, 0f), width * 0.22f, -depthMeters * 0.28f, false);
                    terrain.ApplyDensityBrushWorld(samples[i] - (bankDirection * width * 0.32f) - new Vector3(0f, depthMeters * 0.15f, 0f), width * 0.22f, -depthMeters * 0.28f, false);
                }
            }

            PaintRiverBedMaterials(terrain, samples, widthMeters, depthMeters);
        }
        finally
        {
            terrain.EndBulkEdit();
        }
    }

    public static void PaintFreshwaterBasinMaterials(
        ProceduralVoxelTerrain terrain,
        Vector3 center,
        float radiusMeters,
        float surfaceY,
        float depthMeters,
        bool isPond)
    {
        if (terrain == null)
        {
            return;
        }

        terrain.BeginBulkEdit();
        try
        {
            int gravelIndex = ResolveTerrainMaterialIndex(terrain, "Basin Gravel");
            int sandIndex = ResolveTerrainMaterialIndex(terrain, "Basin Sand");
            int mudIndex = ResolveTerrainMaterialIndex(terrain, "Lake Mud", "Organic Layer");
            int clayIndex = ResolveTerrainMaterialIndex(terrain, "Clay Deposit");
            int organicIndex = ResolveTerrainMaterialIndex(terrain, "Organic Layer");

            if (sandIndex >= 0)
            {
                terrain.ApplyMaterialBrushWorld(
                    new Vector3(center.x, surfaceY - (depthMeters * 0.52f), center.z),
                    radiusMeters * (isPond ? 0.82f : 0.88f),
                    Mathf.Max(depthMeters * 0.42f, terrain.VoxelSizeMeters * 0.8f),
                    sandIndex,
                    false);

                int beachRingSamples = Mathf.Max(12, Mathf.CeilToInt(radiusMeters * 1.8f));
                float beachRingDistance = radiusMeters * (isPond ? 0.94f : 0.98f);
                for (int i = 0; i < beachRingSamples; i++)
                {
                    float angleRadians = (Mathf.PI * 2f * i) / beachRingSamples;
                    Vector3 beachOffset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * beachRingDistance;
                    terrain.ApplyMaterialBrushWorld(
                        new Vector3(center.x + beachOffset.x, surfaceY + (terrain.VoxelSizeMeters * 0.18f), center.z + beachOffset.z),
                        radiusMeters * (isPond ? 0.05f : 0.06f),
                        terrain.VoxelSizeMeters * 0.45f,
                        sandIndex,
                        false);
                }
            }

            if (gravelIndex >= 0)
            {
                int ringSamples = Mathf.Max(10, Mathf.CeilToInt(radiusMeters * 1.6f));
                float ringDistance = radiusMeters * (isPond ? 0.72f : 0.84f);
                for (int i = 0; i < ringSamples; i++)
                {
                    float angleRadians = (Mathf.PI * 2f * i) / ringSamples;
                    Vector3 ringOffset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * ringDistance;
                    terrain.ApplyMaterialBrushWorld(
                        new Vector3(center.x + ringOffset.x, surfaceY - (depthMeters * 0.38f), center.z + ringOffset.z),
                        radiusMeters * (isPond ? 0.09f : 0.12f),
                        Mathf.Max(depthMeters * 0.22f, terrain.VoxelSizeMeters * 0.75f),
                        gravelIndex,
                        false);
                }
            }

            if (mudIndex >= 0)
            {
                terrain.ApplyMaterialBrushWorld(
                    new Vector3(center.x, surfaceY - (depthMeters * 0.7f), center.z),
                    radiusMeters * (isPond ? 0.72f : 0.56f),
                    Mathf.Max(depthMeters * 0.28f, terrain.VoxelSizeMeters * 0.8f),
                    mudIndex,
                    false);
            }

            if (clayIndex >= 0)
            {
                terrain.ApplyMaterialBrushWorld(
                    new Vector3(center.x, surfaceY - (depthMeters * 0.86f), center.z),
                    radiusMeters * (isPond ? 0.28f : 0.22f),
                    Mathf.Max(depthMeters * 0.2f, terrain.VoxelSizeMeters * 0.75f),
                    clayIndex,
                    false);
            }

            if (isPond && organicIndex >= 0)
            {
                terrain.ApplyMaterialBrushWorld(
                    new Vector3(center.x, surfaceY - (depthMeters * 0.26f), center.z),
                    radiusMeters * 0.32f,
                    Mathf.Max(depthMeters * 0.12f, terrain.VoxelSizeMeters * 0.6f),
                    organicIndex,
                    false);
            }
        }
        finally
        {
            terrain.EndBulkEdit();
        }
    }

    public static void PaintRiverBedMaterials(
        ProceduralVoxelTerrain terrain,
        IReadOnlyList<Vector3> samples,
        float widthMeters,
        float depthMeters)
    {
        if (terrain == null || samples == null || samples.Count == 0)
        {
            return;
        }

        terrain.BeginBulkEdit();
        try
        {
            int gravelIndex = ResolveTerrainMaterialIndex(terrain, "Basin Gravel");
            int sandIndex = ResolveTerrainMaterialIndex(terrain, "Basin Sand");
            int clayIndex = ResolveTerrainMaterialIndex(terrain, "Clay Deposit");

            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 samplePoint = samples[i];
                if (gravelIndex >= 0)
                {
                    terrain.ApplyMaterialBrushWorld(
                        samplePoint - new Vector3(0f, depthMeters * 0.2f, 0f),
                        widthMeters * 0.72f,
                        depthMeters + 0.9f,
                        gravelIndex,
                        false);
                }

                if (sandIndex >= 0)
                {
                    terrain.ApplyMaterialBrushWorld(
                        samplePoint - new Vector3(0f, depthMeters * 0.14f, 0f),
                        widthMeters * 0.48f,
                        depthMeters * 0.8f,
                        sandIndex,
                        false);
                }

                if (clayIndex >= 0 && (i % 4) == 0)
                {
                    terrain.ApplyMaterialBrushWorld(
                        samplePoint - new Vector3(0f, depthMeters * 0.24f, 0f),
                        widthMeters * 0.24f,
                        Mathf.Max(depthMeters * 0.4f, terrain.VoxelSizeMeters),
                        clayIndex,
                        false);
                }
            }
        }
        finally
        {
            terrain.EndBulkEdit();
        }
    }

    public static int ResolveTerrainMaterialIndex(ProceduralVoxelTerrain terrain, params string[] aliases)
    {
        if (terrain == null || aliases == null)
        {
            return -1;
        }

        for (int i = 0; i < aliases.Length; i++)
        {
            int index = terrain.FindMaterialIndex(aliases[i]);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }
}
