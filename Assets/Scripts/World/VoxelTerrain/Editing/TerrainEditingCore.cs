using System.Collections.Generic;
using UnityEngine;

public partial class ProceduralVoxelTerrain
{
    private bool ExcavateSphere(Vector3 worldPoint, Inventory playerInventory)
    {
        LogExcavationDebug($"ExcavateSphere requested at {worldPoint} with radius {miningRadiusMeters:F2}.");
        if (TryGetExcavationBlockReason(worldPoint, out string blockReason))
        {
            LogExcavationDebug($"ExcavateSphere blocked at {worldPoint}: {blockReason}");
            Debug.Log(blockReason, this);
            return false;
        }

        bool result = ModifyDensitySphere(worldPoint, miningRadiusMeters, -excavationStrengthMeters, playerInventory, true, true);
        LogExcavationDebug($"ExcavateSphere {(result ? "succeeded" : "failed")} at {worldPoint}.");
        return result;
    }

    private bool ModifyDensitySphere(
        Vector3 worldPoint,
        float radiusMeters,
        float densityDeltaMeters,
        Inventory playerInventory,
        bool collectHarvest,
        bool notifyGeometryChange = true)
    {
        if (densitySamples == null || cellMaterialIndices == null || Mathf.Approximately(densityDeltaMeters, 0f))
        {
            return false;
        }

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        if (!IsWithinWorld(localPoint))
        {
            return false;
        }

        Dictionary<int, float> originalSampleValues = new Dictionary<int, float>();
        HashSet<int> affectedCellIndices = new HashSet<int>();
        HashSet<Vector3Int> affectedChunkCoordinates = new HashSet<Vector3Int>();
        Dictionary<int, bool> preSolidStates = new Dictionary<int, bool>();

        GetAffectedBounds(localPoint, radiusMeters, out Vector3Int minSample, out Vector3Int maxSample, out Vector3Int minCell, out Vector3Int maxCell);

        for (int z = minCell.z; z <= maxCell.z; z++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    if (!SphereIntersectsCell(localPoint, radiusMeters, x, y, z))
                    {
                        continue;
                    }

                    int cellIndex = GetCellIndex(x, y, z);
                    affectedCellIndices.Add(cellIndex);
                    preSolidStates[cellIndex] = IsCellSolid(x, y, z);
                    affectedChunkCoordinates.Add(GetChunkCoordinateForCell(x, y, z));
                }
            }
        }

        for (int z = minSample.z; z <= maxSample.z; z++)
        {
            for (int y = minSample.y; y <= maxSample.y; y++)
            {
                for (int x = minSample.x; x <= maxSample.x; x++)
                {
                    Vector3 samplePosition = new Vector3(x * voxelSizeMeters, y * voxelSizeMeters, z * voxelSizeMeters);
                    float distance = Vector3.Distance(samplePosition, localPoint);
                    if (distance > radiusMeters)
                    {
                        continue;
                    }

                    int sampleIndex = GetSampleIndex(x, y, z);
                    if (!originalSampleValues.ContainsKey(sampleIndex))
                    {
                        originalSampleValues[sampleIndex] = densitySamples[sampleIndex];
                    }

                    float normalizedDistance = 1f - Mathf.Clamp01(distance / radiusMeters);
                    float falloff = normalizedDistance * normalizedDistance * (3f - (2f * normalizedDistance));
                    densitySamples[sampleIndex] += densityDeltaMeters * falloff;
                }
            }
        }

        if (originalSampleValues.Count == 0)
        {
            LogExcavationDebug($"ModifyDensitySphere made no sample changes at {worldPoint}.");
            return false;
        }

        Dictionary<int, float> harvestedMassByMaterial = new Dictionary<int, float>();
        foreach (int cellIndex in affectedCellIndices)
        {
            GetCellCoordinates(cellIndex, out int cellX, out int cellY, out int cellZ);
            bool wasSolid = preSolidStates.TryGetValue(cellIndex, out bool previousState) && previousState;
            bool isSolid = IsCellSolid(cellX, cellY, cellZ);
            if (wasSolid && !isSolid)
            {
                int materialIndex = cellMaterialIndices[cellIndex];
                if (!harvestedMassByMaterial.ContainsKey(materialIndex))
                {
                    harvestedMassByMaterial[materialIndex] = 0f;
                }

                harvestedMassByMaterial[materialIndex] += harvestedMassPerSolidCellGrams;
            }
        }

        if (collectHarvest && playerInventory == null)
        {
            foreach (KeyValuePair<int, float> original in originalSampleValues)
            {
                densitySamples[original.Key] = original.Value;
            }

            LogExcavationDebug("ModifyDensitySphere rolled back because player inventory was missing.");
            return false;
        }

        List<InventoryItem> harvestedItems = collectHarvest
            ? BuildHarvestedItems(harvestedMassByMaterial)
            : new List<InventoryItem>();
        if (harvestedItems.Count > 0 && !playerInventory.AddItems(harvestedItems))
        {
            foreach (KeyValuePair<int, float> original in originalSampleValues)
            {
                densitySamples[original.Key] = original.Value;
            }

            LogExcavationDebug($"ModifyDensitySphere rolled back because inventory rejected {harvestedItems.Count} harvested item(s).");
            return false;
        }

        if (IsBulkEditActive)
        {
            QueueBulkEditedChunks(affectedChunkCoordinates);
        }
        else
        {
            foreach (Vector3Int chunkCoordinate in affectedChunkCoordinates)
            {
                RebuildChunk(chunkCoordinate);
            }
        }

        if (notifyGeometryChange)
        {
            if (IsBulkEditActive)
            {
                QueueBulkGeometryChange(
                    BuildChangedWorldBounds(localPoint, radiusMeters, radiusMeters),
                    affectedChunkCoordinates);
            }
            else
            {
                NotifyGeometryChanged(localPoint, radiusMeters, affectedChunkCoordinates);
            }
        }

        int removedSolidCellCount = 0;
        foreach (KeyValuePair<int, float> harvestedMass in harvestedMassByMaterial)
        {
            if (harvestedMass.Value > 0f)
            {
                removedSolidCellCount += Mathf.Max(1, Mathf.RoundToInt(harvestedMass.Value / Mathf.Max(0.0001f, harvestedMassPerSolidCellGrams)));
            }
        }

        LogExcavationDebug(
            $"ModifyDensitySphere applied at {worldPoint}. Affected chunks={affectedChunkCoordinates.Count}, changed samples={originalSampleValues.Count}, removed solid cells~={removedSolidCellCount}, harvested items={harvestedItems.Count}.");
        return true;
    }

    private void LogExcavationDebug(string message)
    {
        if (!logExcavationDebug || (IsBulkEditActive && suppressExcavationDebugDuringBulkEdit))
        {
            return;
        }

        Debug.Log($"[{nameof(ProceduralVoxelTerrain)}:{name}] {message}", this);
    }

    private bool IsBulkEditActive => bulkEditDepth > 0;

    private Bounds BuildChangedWorldBounds(Vector3 localPoint, float horizontalRadiusMeters, float verticalHalfExtentMeters)
    {
        Vector3 worldCenter = transform.TransformPoint(localPoint);
        return new Bounds(
            worldCenter,
            new Vector3(
                Mathf.Max(voxelSizeMeters, horizontalRadiusMeters * 2f),
                Mathf.Max(voxelSizeMeters, verticalHalfExtentMeters * 2f),
                Mathf.Max(voxelSizeMeters, horizontalRadiusMeters * 2f)));
    }

    private void QueueBulkEditedChunks(HashSet<Vector3Int> affectedChunkCoordinates)
    {
        if (affectedChunkCoordinates == null)
        {
            return;
        }

        foreach (Vector3Int chunkCoordinate in affectedChunkCoordinates)
        {
            bulkEditedChunkCoordinates.Add(chunkCoordinate);
        }
    }

    private void QueueBulkGeometryChange(Bounds changedWorldBounds, HashSet<Vector3Int> affectedChunkCoordinates)
    {
        if (!hasBulkGeometryWorldBounds)
        {
            bulkGeometryWorldBounds = changedWorldBounds;
            hasBulkGeometryWorldBounds = true;
        }
        else
        {
            bulkGeometryWorldBounds.Encapsulate(changedWorldBounds.min);
            bulkGeometryWorldBounds.Encapsulate(changedWorldBounds.max);
        }

        if (affectedChunkCoordinates == null)
        {
            return;
        }

        foreach (Vector3Int chunkCoordinate in affectedChunkCoordinates)
        {
            bulkGeometryChunkCoordinates.Add(chunkCoordinate);
        }
    }

    private List<InventoryItem> BuildHarvestedItems(Dictionary<int, float> harvestedMassByMaterial)
    {
        List<InventoryItem> harvestedItems = new List<InventoryItem>();
        if (harvestedMassByMaterial == null)
        {
            return harvestedItems;
        }

        foreach (KeyValuePair<int, float> harvestedMass in harvestedMassByMaterial)
        {
            if (harvestedMass.Value <= 0f || harvestedMass.Key < 0 || harvestedMass.Key >= materialDefinitions.Count)
            {
                continue;
            }

            VoxelTerrainMaterialDefinition materialDefinition = materialDefinitions[harvestedMass.Key];
            CompositionInfo composition = materialDefinition?.ResolveComposition();
            if (composition == null)
            {
                continue;
            }

            harvestedItems.Add(new InventoryItem(
                composition,
                1,
                harvestedMass.Value,
                composition.GenerateRandomComposition()));
        }

        return harvestedItems;
    }

    private string GetHarvestDisplayName(int materialIndex)
    {
        if (materialIndex >= 0 && materialIndex < materialDefinitions.Count)
        {
            CompositionInfo composition = materialDefinitions[materialIndex]?.ResolveComposition();
            if (composition != null)
            {
                return composition.itemName;
            }

            return materialDefinitions[materialIndex]?.ResolveDisplayName() ?? "Terrain";
        }

        return "Terrain";
    }

    private string GetHarvestPreview(int materialIndex, Vector3 worldPoint)
    {
        string materialName = GetHarvestDisplayName(materialIndex);
        if (TryGetExcavationBlockReason(worldPoint, out string blockReason))
        {
            return $"{blockReason}\nLikely material: {materialName}.";
        }

        return $"Excavate roughly {miningRadiusMeters:F1}m of terrain. Likely material: {materialName}.";
    }

    // Carves (or restores) a smooth bowl-shaped cylinder defined by XZ radius.
    // Uses SDF-style direct per-voxel density modification so the result is a
    // perfectly circular pond — not the petal pattern produced by discrete spheres.
    //   densitySign = -1f  → carve (reduce density / make air)
    //   densitySign = +1f  → restore (increase density / make solid)
    public bool ApplyDensityXZCylinder(
        Vector3 worldCenter,
        float radiusXZ,
        float surfaceY,
        float carveDepth,
        float densitySign = -1f)
    {
        if (densitySamples == null) return false;

        // Work in local space (identity transform assumed for terrain).
        Vector3 localCenter = transform.InverseTransformPoint(new Vector3(worldCenter.x, surfaceY, worldCenter.z));

        int xMin = Mathf.Clamp(Mathf.FloorToInt((localCenter.x - radiusXZ) / voxelSizeMeters) - 1, 0, TotalSamplesX - 1);
        int xMax = Mathf.Clamp(Mathf.CeilToInt( (localCenter.x + radiusXZ) / voxelSizeMeters) + 1, 0, TotalSamplesX - 1);
        int yMin = Mathf.Clamp(Mathf.FloorToInt((localCenter.y - carveDepth) / voxelSizeMeters) - 1, 0, TotalSamplesY - 1);
        int yMax = Mathf.Clamp(Mathf.CeilToInt(  localCenter.y / voxelSizeMeters) + 1, 0, TotalSamplesY - 1);
        int zMin = Mathf.Clamp(Mathf.FloorToInt((localCenter.z - radiusXZ) / voxelSizeMeters) - 1, 0, TotalSamplesZ - 1);
        int zMax = Mathf.Clamp(Mathf.CeilToInt( (localCenter.z + radiusXZ) / voxelSizeMeters) + 1, 0, TotalSamplesZ - 1);

        HashSet<Vector3Int> affectedChunks = new HashSet<Vector3Int>();
        bool modified = false;

        for (int z = zMin; z <= zMax; z++)
        for (int y = yMin; y <= yMax; y++)
        for (int x = xMin; x <= xMax; x++)
        {
            float wx = x * voxelSizeMeters;
            float wy = y * voxelSizeMeters;
            float wz = z * voxelSizeMeters;

            float dxz = Mathf.Sqrt((wx - localCenter.x) * (wx - localCenter.x)
                                 + (wz - localCenter.z) * (wz - localCenter.z));
            if (dxz >= radiusXZ) continue;

            // Radial smoothstep: 1 at centre, 0 at rim.
            float rimT    = 1f - dxz / radiusXZ;
            float smoothXZ = rimT * rimT * (3f - 2f * rimT);

            // Bowl floor deepens toward centre.
            float bowlFloor = localCenter.y - carveDepth * smoothXZ;
            if (wy > localCenter.y + voxelSizeMeters) continue;
            if (wy < bowlFloor  - voxelSizeMeters)   continue;

            // Vertical smoothstep from surface to floor.
            float yRange  = Mathf.Max(0.001f, localCenter.y - bowlFloor);
            float yT      = Mathf.Clamp01((localCenter.y - wy) / yRange);
            float smoothY = yT * yT * (3f - 2f * yT);

            float delta = carveDepth * smoothXZ * smoothY;
            if (delta < 0.01f) continue;

            int idx = GetSampleIndex(x, y, z);
            densitySamples[idx] += densitySign * delta;
            modified = true;

            int cx = Mathf.Clamp(x, 0, TotalCellsX - 1);
            int cy = Mathf.Clamp(y, 0, TotalCellsY - 1);
            int cz = Mathf.Clamp(z, 0, TotalCellsZ - 1);
            affectedChunks.Add(GetChunkCoordinateForCell(cx, cy, cz));
        }

        if (!modified) return false;

        if (IsBulkEditActive)
            QueueBulkEditedChunks(affectedChunks);
        else
            foreach (Vector3Int c in affectedChunks) RebuildChunk(c);

        return true;
    }

    private bool TryGetExcavationBlockReason(Vector3 worldPoint, out string blockReason)
    {
        blockReason = string.Empty;

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        if ((localPoint.y - miningRadiusMeters) <= minimumMineableElevationMeters)
        {
            blockReason = $"Mining is blocked below elevation {minimumMineableElevationMeters:F1}m because lower bedrock is too dense.";
            return true;
        }

        ProceduralVoxelTerrainWaterSystem waterSystem = ResolveWaterSystem();
        return waterSystem != null &&
               waterSystem.TryGetTerrainExcavationBlockReason(worldPoint, miningRadiusMeters, out blockReason);
    }

    private ProceduralVoxelTerrainWaterSystem ResolveWaterSystem()
    {
        if (cachedWaterSystem == null)
        {
            cachedWaterSystem = GetComponent<ProceduralVoxelTerrainWaterSystem>();
        }

        return cachedWaterSystem;
    }

    private int GetMaterialIndexAtLocalPoint(Vector3 localPoint)
    {
        int cellX = Mathf.Clamp(Mathf.FloorToInt(localPoint.x / voxelSizeMeters), 0, TotalCellsX - 1);
        int cellY = Mathf.Clamp(Mathf.FloorToInt(localPoint.y / voxelSizeMeters), 0, TotalCellsY - 1);
        int cellZ = Mathf.Clamp(Mathf.FloorToInt(localPoint.z / voxelSizeMeters), 0, TotalCellsZ - 1);
        return cellMaterialIndices[GetCellIndex(cellX, cellY, cellZ)];
    }

    // Add these corners so the mining system knows how to check the 8 points of a voxel cell!
    private static readonly Vector3Int[] CellCornerOffsets = new Vector3Int[]
    {
        new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 1), new Vector3Int(0, 0, 1),
        new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(1, 1, 1), new Vector3Int(0, 1, 1)
    };

    private bool IsCellSolid(int x, int y, int z)
    {
        float densitySum = 0f;
        for (int corner = 0; corner < 8; corner++)
        {
            int sampleX = x + CellCornerOffsets[corner].x;
            int sampleY = y + CellCornerOffsets[corner].y;
            int sampleZ = z + CellCornerOffsets[corner].z;
            densitySum += densitySamples[GetSampleIndex(sampleX, sampleY, sampleZ)];
        }

        // 0f is our IsoLevel!
        return (densitySum / 8f) > 0f; 
    }

    private bool SphereIntersectsCell(Vector3 localPoint, float radiusMeters, int cellX, int cellY, int cellZ)
    {
        Vector3 cellMin = new Vector3(cellX * voxelSizeMeters, cellY * voxelSizeMeters, cellZ * voxelSizeMeters);
        Vector3 cellMax = cellMin + Vector3.one * voxelSizeMeters;
        float clampedX = Mathf.Clamp(localPoint.x, cellMin.x, cellMax.x);
        float clampedY = Mathf.Clamp(localPoint.y, cellMin.y, cellMax.y);
        float clampedZ = Mathf.Clamp(localPoint.z, cellMin.z, cellMax.z);
        Vector3 closestPoint = new Vector3(clampedX, clampedY, clampedZ);
        return (closestPoint - localPoint).sqrMagnitude <= radiusMeters * radiusMeters;
    }

    private Bounds BuildRegionChangedWorldBounds(Vector3Int minCell, Vector3Int maxCell)
    {
        Vector3 localMin = new Vector3(minCell.x * voxelSizeMeters, minCell.y * voxelSizeMeters, minCell.z * voxelSizeMeters);
        Vector3 localMax = new Vector3(
            Mathf.Min(TotalWorldSize.x, (maxCell.x + 1) * voxelSizeMeters),
            Mathf.Min(TotalWorldSize.y, (maxCell.y + 1) * voxelSizeMeters),
            Mathf.Min(TotalWorldSize.z, (maxCell.z + 1) * voxelSizeMeters));
        Vector3 worldMin = transform.TransformPoint(localMin);
        Vector3 worldMax = transform.TransformPoint(localMax);
        Vector3 boundsMin = Vector3.Min(worldMin, worldMax);
        Vector3 boundsMax = Vector3.Max(worldMin, worldMax);
        Bounds bounds = new Bounds((boundsMin + boundsMax) * 0.5f, boundsMax - boundsMin);
        bounds.size = Vector3.Max(bounds.size, Vector3.one * voxelSizeMeters);
        return bounds;
    }

    private void GetRegionSnapshotBounds(Bounds worldBounds, out Vector3Int minSample, out Vector3Int maxSample, out Vector3Int minCell, out Vector3Int maxCell)
    {
        Vector3 localMin = transform.InverseTransformPoint(worldBounds.min);
        Vector3 localMax = transform.InverseTransformPoint(worldBounds.max);
        Vector3 min = Vector3.Min(localMin, localMax);
        Vector3 max = Vector3.Max(localMin, localMax);
        minSample = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(min.x / voxelSizeMeters), 0, TotalSamplesX - 1),
            Mathf.Clamp(Mathf.FloorToInt(min.y / voxelSizeMeters), 0, TotalSamplesY - 1),
            Mathf.Clamp(Mathf.FloorToInt(min.z / voxelSizeMeters), 0, TotalSamplesZ - 1));
        maxSample = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt(max.x / voxelSizeMeters), 0, TotalSamplesX - 1),
            Mathf.Clamp(Mathf.CeilToInt(max.y / voxelSizeMeters), 0, TotalSamplesY - 1),
            Mathf.Clamp(Mathf.CeilToInt(max.z / voxelSizeMeters), 0, TotalSamplesZ - 1));
        minCell = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(min.x / voxelSizeMeters), 0, TotalCellsX - 1),
            Mathf.Clamp(Mathf.FloorToInt(min.y / voxelSizeMeters), 0, TotalCellsY - 1),
            Mathf.Clamp(Mathf.FloorToInt(min.z / voxelSizeMeters), 0, TotalCellsZ - 1));
        maxCell = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt(max.x / voxelSizeMeters), 0, TotalCellsX - 1),
            Mathf.Clamp(Mathf.CeilToInt(max.y / voxelSizeMeters), 0, TotalCellsY - 1),
            Mathf.Clamp(Mathf.CeilToInt(max.z / voxelSizeMeters), 0, TotalCellsZ - 1));
    }

    private void GetAffectedBounds(Vector3 localPoint, float radiusMeters, out Vector3Int minSample, out Vector3Int maxSample, out Vector3Int minCell, out Vector3Int maxCell)
    {
        minSample = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt((localPoint.x - radiusMeters) / voxelSizeMeters), 0, TotalSamplesX - 1),
            Mathf.Clamp(Mathf.FloorToInt((localPoint.y - radiusMeters) / voxelSizeMeters), 0, TotalSamplesY - 1),
            Mathf.Clamp(Mathf.FloorToInt((localPoint.z - radiusMeters) / voxelSizeMeters), 0, TotalSamplesZ - 1));
        maxSample = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt((localPoint.x + radiusMeters) / voxelSizeMeters), 0, TotalSamplesX - 1),
            Mathf.Clamp(Mathf.CeilToInt((localPoint.y + radiusMeters) / voxelSizeMeters), 0, TotalSamplesY - 1),
            Mathf.Clamp(Mathf.CeilToInt((localPoint.z + radiusMeters) / voxelSizeMeters), 0, TotalSamplesZ - 1));
        minCell = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt((localPoint.x - radiusMeters) / voxelSizeMeters), 0, TotalCellsX - 1),
            Mathf.Clamp(Mathf.FloorToInt((localPoint.y - radiusMeters) / voxelSizeMeters), 0, TotalCellsY - 1),
            Mathf.Clamp(Mathf.FloorToInt((localPoint.z - radiusMeters) / voxelSizeMeters), 0, TotalCellsZ - 1));
        maxCell = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt((localPoint.x + radiusMeters) / voxelSizeMeters), 0, TotalCellsX - 1),
            Mathf.Clamp(Mathf.CeilToInt((localPoint.y + radiusMeters) / voxelSizeMeters), 0, TotalCellsY - 1),
            Mathf.Clamp(Mathf.CeilToInt((localPoint.z + radiusMeters) / voxelSizeMeters), 0, TotalCellsZ - 1));
    }
}
