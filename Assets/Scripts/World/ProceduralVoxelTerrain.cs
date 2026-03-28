using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
[Serializable]
public class VoxelTerrainMaterialDefinition
{
    public string displayName = "Silicate Stone";
    public string compositionItemName = "Stone (Silicate-Rich)";
    public CompositionInfo compositionOverride;
    public Material material;
    public Color colorTint = new Color(0.55f, 0.57f, 0.6f, 1f);
    public bool isFallbackMaterial = false;
    public Vector2 depthRangeMeters = new Vector2(0f, 64f);
    public Vector2 normalizedHeightRange = new Vector2(0f, 1f);
    [Min(0.5f)] public float distributionNoiseScaleMeters = 18f;
    [Range(0f, 1f)] public float distributionNoiseThreshold = 0.5f;

    public void Sanitize()
    {
        displayName = string.IsNullOrWhiteSpace(displayName) ? "Terrain Material" : displayName.Trim();
        depthRangeMeters = new Vector2(
            Mathf.Max(0f, Mathf.Min(depthRangeMeters.x, depthRangeMeters.y)),
            Mathf.Max(0f, Mathf.Max(depthRangeMeters.x, depthRangeMeters.y)));
        normalizedHeightRange = new Vector2(
            Mathf.Clamp01(Mathf.Min(normalizedHeightRange.x, normalizedHeightRange.y)),
            Mathf.Clamp01(Mathf.Max(normalizedHeightRange.x, normalizedHeightRange.y)));
        distributionNoiseScaleMeters = Mathf.Max(0.5f, distributionNoiseScaleMeters);
        distributionNoiseThreshold = Mathf.Clamp01(distributionNoiseThreshold);
    }

    public string ResolveDisplayName()
    {
        return string.IsNullOrWhiteSpace(displayName) ? "Terrain Material" : displayName.Trim();
    }

    public CompositionInfo ResolveComposition()
    {
        if (compositionOverride != null)
        {
            return compositionOverride;
        }

        if (!string.IsNullOrWhiteSpace(compositionItemName) &&
            CompositionInfoRegistry.TryGetByItemName(compositionItemName, out CompositionInfo composition))
        {
            return composition;
        }

        return null;
    }
}

public readonly struct VoxelTerrainGeometryChangedEventArgs
{
    public VoxelTerrainGeometryChangedEventArgs(Bounds changedWorldBounds, IReadOnlyList<Vector3Int> affectedChunkCoordinates)
    {
        ChangedWorldBounds = changedWorldBounds;
        AffectedChunkCoordinates = affectedChunkCoordinates;
    }

    public Bounds ChangedWorldBounds { get; }
    public IReadOnlyList<Vector3Int> AffectedChunkCoordinates { get; }
}

public class ProceduralVoxelTerrain : MonoBehaviour, IRaycastHarvestableProvider
{
    private const string GeneratedRootName = "Generated Voxel Terrain";
    private const float IsoLevel = 0f;
    private static readonly Dictionary<string, Material> AutoMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);
    private static readonly Vector3[] CubeCornerOffsets =
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(1f, 0f, 0f),
        new Vector3(1f, 0f, 1f),
        new Vector3(0f, 0f, 1f),
        new Vector3(0f, 1f, 0f),
        new Vector3(1f, 1f, 0f),
        new Vector3(1f, 1f, 1f),
        new Vector3(0f, 1f, 1f)
    };
    private static readonly int[,] CubeTetrahedra =
    {
        { 0, 5, 1, 6 },
        { 0, 1, 2, 6 },
        { 0, 2, 3, 6 },
        { 0, 3, 7, 6 },
        { 0, 7, 4, 6 },
        { 0, 4, 5, 6 }
    };

    private sealed class HarvestTarget : IHarvestable
    {
        private readonly ProceduralVoxelTerrain owner;
        private readonly Vector3 hitPoint;
        private readonly int sampledMaterialIndex;

        public HarvestTarget(ProceduralVoxelTerrain owner, Vector3 hitPoint, int sampledMaterialIndex)
        {
            this.owner = owner;
            this.hitPoint = hitPoint;
            this.sampledMaterialIndex = sampledMaterialIndex;
        }

        public bool Harvest(Inventory playerInventory)
        {
            return owner != null && owner.ExcavateSphere(hitPoint, playerInventory);
        }

        public string GetHarvestDisplayName()
        {
            return owner != null ? owner.GetHarvestDisplayName(sampledMaterialIndex) : "Terrain";
        }

        public string GetHarvestPreview()
        {
            return owner != null ? owner.GetHarvestPreview(sampledMaterialIndex, hitPoint) : "Nothing to harvest";
        }
    }

    private sealed class MeshBuildData
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int>[] submeshIndices;

        public MeshBuildData(int submeshCount)
        {
            submeshIndices = new List<int>[Mathf.Max(1, submeshCount)];
            for (int i = 0; i < submeshIndices.Length; i++)
            {
                submeshIndices[i] = new List<int>();
            }
        }
    }

    private struct GenerationContext
    {
        public float surfaceOffsetX;
        public float surfaceOffsetZ;
        public float ridgeOffsetX;
        public float ridgeOffsetZ;
        public float detailOffsetX;
        public float detailOffsetZ;
        public float caveOffsetX;
        public float caveOffsetY;
        public float caveOffsetZ;
        public float materialOffsetX;
        public float materialOffsetY;
        public float materialOffsetZ;
    }

    [Header("Layout")]
    [SerializeField] private Vector3Int chunkCounts = new Vector3Int(4, 2, 4);
    [SerializeField, Min(4)] private int cellsPerChunkAxis = 16;
    [SerializeField, Min(0.1f)] private float voxelSizeMeters = 1f;
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private bool generateOnStart = true;

    [Header("Terrain Shape")]
    [SerializeField] private int seed = 24681357;
    [SerializeField] private bool randomizeSeed = false;
    [SerializeField, Min(0f)] private float baseSurfaceHeightMeters = 18f;
    [SerializeField, Min(1f)] private float surfaceNoiseScaleMeters = 72f;
    [SerializeField, Min(1)] private int surfaceNoiseOctaves = 4;
    [SerializeField, Range(0f, 1f)] private float surfaceNoisePersistence = 0.5f;
    [SerializeField, Min(1f)] private float surfaceNoiseLacunarity = 2f;
    [SerializeField, Min(0f)] private float surfaceAmplitudeMeters = 12f;
    [SerializeField, Min(1f)] private float ridgeNoiseScaleMeters = 34f;
    [SerializeField, Min(0f)] private float ridgeAmplitudeMeters = 4.5f;
    [SerializeField, Min(1f)] private float detailNoiseScaleMeters = 20f;
    [SerializeField, Min(0f)] private float detailAmplitudeMeters = 1.75f;
    [SerializeField, Min(1f)] private float caveNoiseScaleMeters = 18f;
    [SerializeField, Range(0f, 1f)] private float caveNoiseThreshold = 0.74f;
    [SerializeField, Min(0f)] private float caveCarveStrengthMeters = 12f;
    [SerializeField, Min(0f)] private float caveStartDepthMeters = 7f;

    [Header("Mining")]
    [SerializeField, Min(0.1f)] private float miningRadiusMeters = 1.35f;
    [SerializeField, Min(0.05f)] private float excavationStrengthMeters = 2.1f;
    [SerializeField, Min(0.1f)] private float harvestedMassPerSolidCellGrams = 850f;

    [Header("Materials")]
    [SerializeField] private List<VoxelTerrainMaterialDefinition> materialDefinitions = new List<VoxelTerrainMaterialDefinition>();

    [Header("Debug")]
    [SerializeField] private bool drawWorldBoundsGizmos = true;
    [SerializeField] private bool drawChunkBoundsGizmos = false;
    [SerializeField] private Color boundsGizmoColor = new Color(0.36f, 0.76f, 0.92f, 0.85f);
    [SerializeField] private bool logExcavationDebug = false;

    [NonSerialized] private float[] densitySamples;
    [NonSerialized] private byte[] cellMaterialIndices;
    private readonly Dictionary<Vector3Int, ProceduralVoxelTerrainChunk> generatedChunks = new Dictionary<Vector3Int, ProceduralVoxelTerrainChunk>();
    private Material[] sharedChunkMaterials = Array.Empty<Material>();
    private ProceduralVoxelTerrainWaterSystem cachedWaterSystem;

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;
    public bool HasGeneratedTerrain => densitySamples != null && generatedChunks.Count > 0;
    public Vector3 WorldSize => TotalWorldSize;
    public Bounds WorldBounds => new Bounds(transform.position + (TotalWorldSize * 0.5f), TotalWorldSize);
    public float VoxelSizeMeters => voxelSizeMeters;
    public float ChunkWorldSizeMeters => cellsPerChunkAxis * voxelSizeMeters;
    public event Action<VoxelTerrainGeometryChangedEventArgs> GeometryChanged;

    private int TotalCellsX => Mathf.Max(1, chunkCounts.x) * Mathf.Max(4, cellsPerChunkAxis);
    private int TotalCellsY => Mathf.Max(1, chunkCounts.y) * Mathf.Max(4, cellsPerChunkAxis);
    private int TotalCellsZ => Mathf.Max(1, chunkCounts.z) * Mathf.Max(4, cellsPerChunkAxis);
    private int TotalSamplesX => TotalCellsX + 1;
    private int TotalSamplesY => TotalCellsY + 1;
    private int TotalSamplesZ => TotalCellsZ + 1;
    private Vector3 TotalWorldSize => new Vector3(TotalCellsX * voxelSizeMeters, TotalCellsY * voxelSizeMeters, TotalCellsZ * voxelSizeMeters);

    private void Reset()
    {
        ApplyOlympicRainforestPreset();
    }

    private void Start()
    {
        if (Application.isPlaying && generateOnStart)
        {
            GenerateTerrain(clearExistingBeforeGenerate);
        }
    }

    private void OnValidate()
    {
        chunkCounts = new Vector3Int(
            Mathf.Max(1, chunkCounts.x),
            Mathf.Max(1, chunkCounts.y),
            Mathf.Max(1, chunkCounts.z));
        cellsPerChunkAxis = Mathf.Max(4, cellsPerChunkAxis);
        voxelSizeMeters = Mathf.Max(0.1f, voxelSizeMeters);
        baseSurfaceHeightMeters = Mathf.Max(0f, baseSurfaceHeightMeters);
        surfaceNoiseScaleMeters = Mathf.Max(1f, surfaceNoiseScaleMeters);
        surfaceNoiseOctaves = Mathf.Max(1, surfaceNoiseOctaves);
        surfaceNoisePersistence = Mathf.Clamp01(surfaceNoisePersistence);
        surfaceNoiseLacunarity = Mathf.Max(1f, surfaceNoiseLacunarity);
        surfaceAmplitudeMeters = Mathf.Max(0f, surfaceAmplitudeMeters);
        ridgeNoiseScaleMeters = Mathf.Max(1f, ridgeNoiseScaleMeters);
        ridgeAmplitudeMeters = Mathf.Max(0f, ridgeAmplitudeMeters);
        detailNoiseScaleMeters = Mathf.Max(1f, detailNoiseScaleMeters);
        detailAmplitudeMeters = Mathf.Max(0f, detailAmplitudeMeters);
        caveNoiseScaleMeters = Mathf.Max(1f, caveNoiseScaleMeters);
        caveNoiseThreshold = Mathf.Clamp01(caveNoiseThreshold);
        caveCarveStrengthMeters = Mathf.Max(0f, caveCarveStrengthMeters);
        caveStartDepthMeters = Mathf.Max(0f, caveStartDepthMeters);
        miningRadiusMeters = Mathf.Max(0.1f, miningRadiusMeters);
        excavationStrengthMeters = Mathf.Max(0.05f, excavationStrengthMeters);
        harvestedMassPerSolidCellGrams = Mathf.Max(0.1f, harvestedMassPerSolidCellGrams);
        materialDefinitions ??= new List<VoxelTerrainMaterialDefinition>();
        if (materialDefinitions.Count == 0)
        {
            ApplyOlympicRainforestPreset();
            return;
        }

        EnsureDefaultMaterialDefinitionsPresent();
        foreach (VoxelTerrainMaterialDefinition definition in materialDefinitions)
        {
            definition?.Sanitize();
        }
    }

    [ContextMenu("Apply Olympic Rainforest Voxel Preset")]
    public void ApplyOlympicRainforestPreset()
    {
        chunkCounts = new Vector3Int(4, 2, 4);
        cellsPerChunkAxis = 16;
        voxelSizeMeters = 1f;
        baseSurfaceHeightMeters = 18f;
        surfaceNoiseScaleMeters = 72f;
        surfaceNoiseOctaves = 4;
        surfaceNoisePersistence = 0.5f;
        surfaceNoiseLacunarity = 2f;
        surfaceAmplitudeMeters = 12f;
        ridgeNoiseScaleMeters = 34f;
        ridgeAmplitudeMeters = 4.5f;
        detailNoiseScaleMeters = 20f;
        detailAmplitudeMeters = 1.75f;
        caveNoiseScaleMeters = 18f;
        caveNoiseThreshold = 0.74f;
        caveCarveStrengthMeters = 12f;
        caveStartDepthMeters = 7f;
        miningRadiusMeters = 1.35f;
        excavationStrengthMeters = 2.1f;
        harvestedMassPerSolidCellGrams = 850f;
        generateOnStart = true;
        materialDefinitions = BuildDefaultMaterialDefinitions();
    }

    [ContextMenu("Generate Voxel Terrain")]
    public void GenerateTerrainFromContextMenu()
    {
        GenerateTerrain(clearExistingBeforeGenerate);
    }

    [ContextMenu("Clear Voxel Terrain")]
    public void ClearGeneratedTerrainFromContextMenu()
    {
        ClearGeneratedTerrain();
    }

    public bool GenerateTerrain(bool clearExisting)
    {
        if (randomizeSeed)
        {
            seed = Environment.TickCount;
        }

        if (clearExisting)
        {
            ClearGeneratedTerrainForRegeneration();
        }

        if (materialDefinitions == null || materialDefinitions.Count == 0)
        {
            ApplyOlympicRainforestPreset();
        }
        else
        {
            EnsureDefaultMaterialDefinitionsPresent();
        }

        sharedChunkMaterials = BuildSharedMaterials();
        densitySamples = new float[TotalSamplesX * TotalSamplesY * TotalSamplesZ];
        cellMaterialIndices = new byte[TotalCellsX * TotalCellsY * TotalCellsZ];

        GenerationContext context = BuildGenerationContext(seed);
        PopulateDensityField(context);
        PopulateCellMaterials(context);
        EnsureChunkObjects();
        RebuildAllChunks();
        return true;
    }

    public bool ClearGeneratedTerrain()
    {
        ClearGeneratedTerrainForRegeneration();

        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot != null)
        {
            if (Application.isPlaying)
            {
                Destroy(generatedRoot.gameObject);
            }
            else
            {
                DestroyImmediate(generatedRoot.gameObject);
            }

            return true;
        }

        return false;
    }

    private void ClearGeneratedTerrainForRegeneration()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot != null)
        {
            foreach (Transform child in generatedRoot)
            {
                ProceduralVoxelTerrainChunk chunk = child.GetComponent<ProceduralVoxelTerrainChunk>();
                if (chunk != null)
                {
                    chunk.ClearMesh();
                }
            }
        }

        generatedChunks.Clear();
        sharedChunkMaterials = Array.Empty<Material>();
        densitySamples = null;
        cellMaterialIndices = null;
    }

    public bool TrySampleSurface(float normalizedX, float normalizedZ, out RaycastHit hit)
    {
        Bounds bounds = WorldBounds;
        float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(normalizedX));
        float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, Mathf.Clamp01(normalizedZ));
        return TrySampleSurfaceWorld(worldX, worldZ, out hit);
    }

    public bool TrySampleSurfaceWorld(float worldX, float worldZ, out RaycastHit hit)
    {
        hit = default;
        if (!HasGeneratedTerrain)
        {
            return false;
        }

        Bounds bounds = WorldBounds;
        Ray ray = new Ray(
            new Vector3(worldX, bounds.max.y + Mathf.Max(4f, voxelSizeMeters * 4f), worldZ),
            Vector3.down);
        float maxDistance = bounds.size.y + Mathf.Max(8f, voxelSizeMeters * 8f);
        bool foundHit = false;
        float closestDistance = float.MaxValue;

        foreach (ProceduralVoxelTerrainChunk chunk in generatedChunks.Values)
        {
            if (chunk == null)
            {
                continue;
            }

            MeshCollider collider = chunk.GetComponent<MeshCollider>();
            if (collider == null || !collider.enabled || collider.sharedMesh == null)
            {
                continue;
            }

            if (collider.Raycast(ray, out RaycastHit candidateHit, maxDistance) && candidateHit.distance < closestDistance)
            {
                closestDistance = candidateHit.distance;
                hit = candidateHit;
                foundHit = true;
            }
        }

        return foundHit;
    }

    public bool ApplyDensityBrushWorld(Vector3 worldPoint, float radiusMeters, float densityDeltaMeters, bool notifyGeometryChange = true)
    {
        return ModifyDensitySphere(worldPoint, radiusMeters, densityDeltaMeters, null, false, notifyGeometryChange);
    }

    public Bounds GetChunkWorldBounds(Vector3Int chunkCoordinate)
    {
        Vector3 chunkWorldSize = Vector3.one * ChunkWorldSizeMeters;
        Vector3 localMin = Vector3.Scale(chunkCoordinate, chunkWorldSize);
        Vector3 localCenter = localMin + (chunkWorldSize * 0.5f);
        return new Bounds(transform.TransformPoint(localCenter), chunkWorldSize);
    }

    public bool TryGetGeneratedChunk(Vector3Int chunkCoordinate, out ProceduralVoxelTerrainChunk chunk)
    {
        return generatedChunks.TryGetValue(chunkCoordinate, out chunk) && chunk != null;
    }

    public void GetChunkCoordinateRange(Bounds worldBounds, out Vector3Int minChunkCoordinate, out Vector3Int maxChunkCoordinate)
    {
        Vector3 localMin = transform.InverseTransformPoint(worldBounds.min);
        Vector3 localMax = transform.InverseTransformPoint(worldBounds.max);
        Vector3 min = Vector3.Min(localMin, localMax);
        Vector3 max = Vector3.Max(localMin, localMax);
        float chunkWorldSize = ChunkWorldSizeMeters;

        minChunkCoordinate = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(min.x / chunkWorldSize), 0, chunkCounts.x - 1),
            Mathf.Clamp(Mathf.FloorToInt(min.y / chunkWorldSize), 0, chunkCounts.y - 1),
            Mathf.Clamp(Mathf.FloorToInt(min.z / chunkWorldSize), 0, chunkCounts.z - 1));
        maxChunkCoordinate = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt(max.x / chunkWorldSize) - 1, 0, chunkCounts.x - 1),
            Mathf.Clamp(Mathf.CeilToInt(max.y / chunkWorldSize) - 1, 0, chunkCounts.y - 1),
            Mathf.Clamp(Mathf.CeilToInt(max.z / chunkWorldSize) - 1, 0, chunkCounts.z - 1));
    }

    public int FindMaterialIndex(string materialOrCompositionName)
    {
        if (string.IsNullOrWhiteSpace(materialOrCompositionName) || materialDefinitions == null)
        {
            return -1;
        }

        for (int i = 0; i < materialDefinitions.Count; i++)
        {
            VoxelTerrainMaterialDefinition definition = materialDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            if (string.Equals(definition.ResolveDisplayName(), materialOrCompositionName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(definition.compositionItemName, materialOrCompositionName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public bool ApplyMaterialBrushWorld(Vector3 worldPoint, float radiusMeters, float verticalHalfExtentMeters, int materialIndex, bool notifyGeometryChange = true)
    {
        if (cellMaterialIndices == null || materialDefinitions == null || materialIndex < 0 || materialIndex >= materialDefinitions.Count)
        {
            return false;
        }

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        Vector3 worldSize = TotalWorldSize;
        if (localPoint.x < 0f || localPoint.x > worldSize.x || localPoint.z < 0f || localPoint.z > worldSize.z)
        {
            return false;
        }

        radiusMeters = Mathf.Max(voxelSizeMeters * 0.5f, radiusMeters);
        verticalHalfExtentMeters = Mathf.Max(voxelSizeMeters * 0.5f, verticalHalfExtentMeters);

        Vector3Int minCell = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt((localPoint.x - radiusMeters) / voxelSizeMeters), 0, TotalCellsX - 1),
            Mathf.Clamp(Mathf.FloorToInt((localPoint.y - verticalHalfExtentMeters) / voxelSizeMeters), 0, TotalCellsY - 1),
            Mathf.Clamp(Mathf.FloorToInt((localPoint.z - radiusMeters) / voxelSizeMeters), 0, TotalCellsZ - 1));
        Vector3Int maxCell = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt((localPoint.x + radiusMeters) / voxelSizeMeters), 0, TotalCellsX - 1),
            Mathf.Clamp(Mathf.CeilToInt((localPoint.y + verticalHalfExtentMeters) / voxelSizeMeters), 0, TotalCellsY - 1),
            Mathf.Clamp(Mathf.CeilToInt((localPoint.z + radiusMeters) / voxelSizeMeters), 0, TotalCellsZ - 1));

        float radiusSquared = radiusMeters * radiusMeters;
        bool modified = false;
        HashSet<Vector3Int> affectedChunkCoordinates = new HashSet<Vector3Int>();
        for (int z = minCell.z; z <= maxCell.z; z++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    Vector3 cellCenter = new Vector3(
                        (x + 0.5f) * voxelSizeMeters,
                        (y + 0.5f) * voxelSizeMeters,
                        (z + 0.5f) * voxelSizeMeters);
                    if (Mathf.Abs(cellCenter.y - localPoint.y) > verticalHalfExtentMeters)
                    {
                        continue;
                    }

                    Vector3 planarDelta = Vector3.ProjectOnPlane(cellCenter - localPoint, Vector3.up);
                    if (planarDelta.sqrMagnitude > radiusSquared)
                    {
                        continue;
                    }

                    int cellIndex = GetCellIndex(x, y, z);
                    if (cellMaterialIndices[cellIndex] == materialIndex)
                    {
                        continue;
                    }

                    cellMaterialIndices[cellIndex] = (byte)materialIndex;
                    affectedChunkCoordinates.Add(GetChunkCoordinateForCell(x, y, z));
                    modified = true;
                }
            }
        }

        if (!modified)
        {
            return false;
        }

        foreach (Vector3Int chunkCoordinate in affectedChunkCoordinates)
        {
            RebuildChunk(chunkCoordinate);
        }

        if (notifyGeometryChange)
        {
            NotifyGeometryChanged(localPoint, radiusMeters, affectedChunkCoordinates);
        }

        return true;
    }

    private void NotifyGeometryChanged(Vector3 localPoint, float radiusMeters, HashSet<Vector3Int> affectedChunkCoordinates)
    {
        if (affectedChunkCoordinates == null || affectedChunkCoordinates.Count == 0)
        {
            LogExcavationDebug("NotifyGeometryChanged skipped because no chunks were affected.");
            return;
        }

        Vector3 worldCenter = transform.TransformPoint(localPoint);
        Bounds changedBounds = new Bounds(worldCenter, Vector3.one * Mathf.Max(voxelSizeMeters, radiusMeters * 2f));

        LogExcavationDebug(
            $"NotifyGeometryChanged at world {worldCenter} radius {radiusMeters:F2}. Bounds center={changedBounds.center}, size={changedBounds.size}, affected chunks={affectedChunkCoordinates.Count}.");

        GeometryChanged?.Invoke(new VoxelTerrainGeometryChangedEventArgs(
            changedBounds,
            new List<Vector3Int>(affectedChunkCoordinates)));
    }

    public bool TryGetHarvestable(RaycastHit hit, out IHarvestable harvestable)
    {
        harvestable = null;
        if (densitySamples == null || cellMaterialIndices == null || hit.collider == null)
        {
            return false;
        }

        ProceduralVoxelTerrainChunk chunk = hit.collider.GetComponentInParent<ProceduralVoxelTerrainChunk>();
        if (chunk == null)
        {
            return false;
        }

        Vector3 localPoint = transform.InverseTransformPoint(hit.point);
        if (!IsWithinWorld(localPoint))
        {
            return false;
        }

        int materialIndex = GetMaterialIndexAtLocalPoint(localPoint);
        harvestable = new HarvestTarget(this, hit.point, materialIndex);
        return true;
    }

    private void PopulateDensityField(GenerationContext context)
    {
        for (int z = 0; z < TotalSamplesZ; z++)
        {
            float localZ = z * voxelSizeMeters;
            for (int y = 0; y < TotalSamplesY; y++)
            {
                float localY = y * voxelSizeMeters;
                for (int x = 0; x < TotalSamplesX; x++)
                {
                    float localX = x * voxelSizeMeters;
                    densitySamples[GetSampleIndex(x, y, z)] = EvaluateDensity(localX, localY, localZ, context);
                }
            }
        }
    }

    private void PopulateCellMaterials(GenerationContext context)
    {
        for (int z = 0; z < TotalCellsZ; z++)
        {
            float localZ = (z + 0.5f) * voxelSizeMeters;
            for (int y = 0; y < TotalCellsY; y++)
            {
                float localY = (y + 0.5f) * voxelSizeMeters;
                for (int x = 0; x < TotalCellsX; x++)
                {
                    float localX = (x + 0.5f) * voxelSizeMeters;
                    cellMaterialIndices[GetCellIndex(x, y, z)] = DetermineCellMaterialIndex(localX, localY, localZ, context);
                }
            }
        }
    }

    private float EvaluateDensity(float localX, float localY, float localZ, GenerationContext context)
    {
        float surfaceHeight = EvaluateSurfaceHeight(localX, localZ, context);
        float density = surfaceHeight - localY;

        float depthBelowSurface = surfaceHeight - localY;
        if (depthBelowSurface > caveStartDepthMeters)
        {
            float caveNoise = EvaluatePerlin3D(
                (localX + context.caveOffsetX) / caveNoiseScaleMeters,
                (localY + context.caveOffsetY) / caveNoiseScaleMeters,
                (localZ + context.caveOffsetZ) / caveNoiseScaleMeters);
            if (caveNoise > caveNoiseThreshold)
            {
                float carveAmount = (caveNoise - caveNoiseThreshold) / Mathf.Max(0.0001f, 1f - caveNoiseThreshold);
                density -= carveAmount * caveCarveStrengthMeters;
            }
        }

        return density;
    }

    private float EvaluateSurfaceHeight(float localX, float localZ, GenerationContext context)
    {
        float fractalNoise = EvaluateFractalNoise(
            (localX + context.surfaceOffsetX) / surfaceNoiseScaleMeters,
            (localZ + context.surfaceOffsetZ) / surfaceNoiseScaleMeters,
            surfaceNoiseOctaves,
            surfaceNoisePersistence,
            surfaceNoiseLacunarity);
        float ridgeNoise = Mathf.PerlinNoise(
            (localX + context.ridgeOffsetX) / ridgeNoiseScaleMeters,
            (localZ + context.ridgeOffsetZ) / ridgeNoiseScaleMeters);
        ridgeNoise = 1f - Mathf.Abs((ridgeNoise * 2f) - 1f);
        ridgeNoise *= ridgeNoise;
        float detailNoise = Mathf.PerlinNoise(
            (localX + context.detailOffsetX) / detailNoiseScaleMeters,
            (localZ + context.detailOffsetZ) / detailNoiseScaleMeters);
        detailNoise = (detailNoise - 0.5f) * 2f;

        float worldHeight = baseSurfaceHeightMeters
            + ((fractalNoise - 0.5f) * 2f * surfaceAmplitudeMeters)
            + (ridgeNoise * ridgeAmplitudeMeters)
            + (detailNoise * detailAmplitudeMeters);
        return Mathf.Clamp(worldHeight, 0f, TotalWorldSize.y - voxelSizeMeters);
    }

    private byte DetermineCellMaterialIndex(float localX, float localY, float localZ, GenerationContext context)
    {
        float surfaceHeight = EvaluateSurfaceHeight(localX, localZ, context);
        float depthBelowSurface = surfaceHeight - localY;
        float normalizedHeight = TotalWorldSize.y <= 0.0001f ? 0f : Mathf.Clamp01(localY / TotalWorldSize.y);

        byte fallbackIndex = 0;
        int topsoilIndex = -1;
        int alluvialClayIndex = -1;
        int claySubsoilIndex = -1;
        int weatheredStoneIndex = -1;
        for (int i = 0; i < materialDefinitions.Count; i++)
        {
            VoxelTerrainMaterialDefinition definition = materialDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            if (definition.isFallbackMaterial)
            {
                fallbackIndex = (byte)i;
                continue;
            }

            string displayName = definition.ResolveDisplayName();
            if (topsoilIndex < 0 && string.Equals(displayName, "Topsoil", StringComparison.OrdinalIgnoreCase))
            {
                topsoilIndex = i;
            }
            else if (alluvialClayIndex < 0 && string.Equals(displayName, "Alluvial Clay", StringComparison.OrdinalIgnoreCase))
            {
                alluvialClayIndex = i;
            }
            else if (claySubsoilIndex < 0 && string.Equals(displayName, "Clay Subsoil", StringComparison.OrdinalIgnoreCase))
            {
                claySubsoilIndex = i;
            }
            else if (weatheredStoneIndex < 0 && string.Equals(displayName, "Weathered Surface Stone", StringComparison.OrdinalIgnoreCase))
            {
                weatheredStoneIndex = i;
            }
        }

        float soilThicknessNoise = Mathf.PerlinNoise(
            (localX + context.materialOffsetX + 421.3f) / 78f,
            (localZ + context.materialOffsetZ + 217.9f) / 78f);
        float drainageNoise = Mathf.PerlinNoise(
            (localX + context.materialOffsetX + 811.1f) / 54f,
            (localZ + context.materialOffsetZ + 1043.7f) / 54f);
        float outcropNoise = Mathf.PerlinNoise(
            (localX + context.materialOffsetX + 1337.3f) / 96f,
            (localZ + context.materialOffsetZ + 553.1f) / 96f);

        bool lowland = normalizedHeight < 0.34f;
        bool likelyAlluvial = lowland && drainageNoise > 0.47f;

        float topsoilThickness = Mathf.Lerp(0.28f, 0.82f, soilThicknessNoise);
        if (normalizedHeight > 0.72f)
        {
            topsoilThickness *= 0.55f;
        }

        if (likelyAlluvial)
        {
            topsoilThickness *= 1.12f;
        }

        if (topsoilIndex >= 0 && depthBelowSurface <= topsoilThickness)
        {
            return (byte)topsoilIndex;
        }

        if (alluvialClayIndex >= 0 &&
            likelyAlluvial &&
            depthBelowSurface <= Mathf.Max(1.3f, topsoilThickness + 0.7f))
        {
            return (byte)alluvialClayIndex;
        }

        if (weatheredStoneIndex >= 0 &&
            depthBelowSurface <= Mathf.Max(1.6f, topsoilThickness + 1.2f) &&
            (!lowland || outcropNoise > 0.7f))
        {
            return (byte)weatheredStoneIndex;
        }

        if (claySubsoilIndex >= 0 &&
            (lowland || drainageNoise > 0.56f) &&
            depthBelowSurface <= 5f)
        {
            return (byte)claySubsoilIndex;
        }

        for (int i = 0; i < materialDefinitions.Count; i++)
        {
            VoxelTerrainMaterialDefinition definition = materialDefinitions[i];
            if (definition == null || definition.isFallbackMaterial)
            {
                continue;
            }

            if (i == topsoilIndex ||
                i == alluvialClayIndex ||
                i == claySubsoilIndex ||
                i == weatheredStoneIndex)
            {
                continue;
            }

            if (depthBelowSurface < definition.depthRangeMeters.x || depthBelowSurface > definition.depthRangeMeters.y)
            {
                continue;
            }

            if (normalizedHeight < definition.normalizedHeightRange.x || normalizedHeight > definition.normalizedHeightRange.y)
            {
                continue;
            }

            float materialNoise = EvaluatePerlin3D(
                (localX + context.materialOffsetX + (i * 137.17f)) / definition.distributionNoiseScaleMeters,
                (localY + context.materialOffsetY + (i * 59.11f)) / definition.distributionNoiseScaleMeters,
                (localZ + context.materialOffsetZ + (i * 83.37f)) / definition.distributionNoiseScaleMeters);
            if (materialNoise >= definition.distributionNoiseThreshold)
            {
                return (byte)i;
            }
        }

        return fallbackIndex;
    }

    private void EnsureChunkObjects()
    {
        Transform generatedRoot = EnsureGeneratedRoot();
        generatedChunks.Clear();
        Vector3 chunkSize = Vector3.one * (cellsPerChunkAxis * voxelSizeMeters);
        HashSet<string> expectedChunkNames = new HashSet<string>(StringComparer.Ordinal);

        for (int z = 0; z < chunkCounts.z; z++)
        {
            for (int y = 0; y < chunkCounts.y; y++)
            {
                for (int x = 0; x < chunkCounts.x; x++)
                {
                    Vector3Int chunkCoordinate = new Vector3Int(x, y, z);
                    string chunkName = $"Chunk_{x:00}_{y:00}_{z:00}";
                    expectedChunkNames.Add(chunkName);
                    Transform chunkTransform = generatedRoot.Find(chunkName);
                    ProceduralVoxelTerrainChunk chunk = null;
                    if (chunkTransform != null)
                    {
                        chunk = chunkTransform.GetComponent<ProceduralVoxelTerrainChunk>();
                    }

                    if (chunk == null)
                    {
                        GameObject chunkObject = new GameObject(chunkName);
                        chunkObject.layer = gameObject.layer;
                        chunkObject.transform.SetParent(generatedRoot, false);
                        chunk = chunkObject.AddComponent<ProceduralVoxelTerrainChunk>();
                    }

                    chunk.transform.localPosition = Vector3.Scale(chunkCoordinate, chunkSize);
                    chunk.transform.localRotation = Quaternion.identity;
                    chunk.transform.localScale = Vector3.one;
                    chunk.Initialize(chunkCoordinate, sharedChunkMaterials);
                    generatedChunks[chunkCoordinate] = chunk;
                }
            }
        }

        List<Transform> staleChildren = new List<Transform>();
        foreach (Transform child in generatedRoot)
        {
            if (!expectedChunkNames.Contains(child.name))
            {
                staleChildren.Add(child);
            }
        }

        for (int i = 0; i < staleChildren.Count; i++)
        {
            if (Application.isPlaying)
            {
                Destroy(staleChildren[i].gameObject);
            }
            else
            {
                DestroyImmediate(staleChildren[i].gameObject);
            }
        }
    }

    private void RebuildAllChunks()
    {
        foreach (KeyValuePair<Vector3Int, ProceduralVoxelTerrainChunk> chunkEntry in generatedChunks)
        {
            RebuildChunk(chunkEntry.Key);
        }
    }

    private void RebuildChunk(Vector3Int chunkCoordinate)
    {
        if (!generatedChunks.TryGetValue(chunkCoordinate, out ProceduralVoxelTerrainChunk chunk) || chunk == null)
        {
            return;
        }

        MeshBuildData buildData = BuildChunkMesh(chunkCoordinate);
        if (buildData.vertices.Count == 0)
        {
            chunk.ClearMesh();
            return;
        }

        Mesh mesh = new Mesh
        {
            name = $"Voxel Chunk {chunkCoordinate.x}-{chunkCoordinate.y}-{chunkCoordinate.z}",
            indexFormat = IndexFormat.UInt32
        };
        mesh.SetVertices(buildData.vertices);
        mesh.subMeshCount = buildData.submeshIndices.Length;
        for (int i = 0; i < buildData.submeshIndices.Length; i++)
        {
            mesh.SetTriangles(buildData.submeshIndices[i], i, false);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        chunk.ApplyMesh(mesh, sharedChunkMaterials);
    }

    private MeshBuildData BuildChunkMesh(Vector3Int chunkCoordinate)
    {
        MeshBuildData buildData = new MeshBuildData(Mathf.Max(1, materialDefinitions.Count));
        Vector3Int chunkStartCell = new Vector3Int(
            chunkCoordinate.x * cellsPerChunkAxis,
            chunkCoordinate.y * cellsPerChunkAxis,
            chunkCoordinate.z * cellsPerChunkAxis);
        Vector3 chunkOrigin = new Vector3(
            chunkStartCell.x * voxelSizeMeters,
            chunkStartCell.y * voxelSizeMeters,
            chunkStartCell.z * voxelSizeMeters);

        float[] cubeDensities = new float[8];
        Vector3[] cubePositions = new Vector3[8];

        for (int z = 0; z < cellsPerChunkAxis; z++)
        {
            int globalZ = chunkStartCell.z + z;
            for (int y = 0; y < cellsPerChunkAxis; y++)
            {
                int globalY = chunkStartCell.y + y;
                for (int x = 0; x < cellsPerChunkAxis; x++)
                {
                    int globalX = chunkStartCell.x + x;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        int sampleX = globalX + (int)CubeCornerOffsets[corner].x;
                        int sampleY = globalY + (int)CubeCornerOffsets[corner].y;
                        int sampleZ = globalZ + (int)CubeCornerOffsets[corner].z;
                        cubeDensities[corner] = densitySamples[GetSampleIndex(sampleX, sampleY, sampleZ)];
                        cubePositions[corner] = new Vector3(
                            sampleX * voxelSizeMeters,
                            sampleY * voxelSizeMeters,
                            sampleZ * voxelSizeMeters) - chunkOrigin;
                    }

                    if (!CubeIntersectsSurface(cubeDensities))
                    {
                        continue;
                    }

                    int materialIndex = cellMaterialIndices[GetCellIndex(globalX, globalY, globalZ)];
                    for (int tetraIndex = 0; tetraIndex < CubeTetrahedra.GetLength(0); tetraIndex++)
                    {
                        PolygoniseTetrahedron(cubePositions, cubeDensities, tetraIndex, materialIndex, buildData);
                    }
                }
            }
        }

        return buildData;
    }

    private void PolygoniseTetrahedron(Vector3[] cubePositions, float[] cubeDensities, int tetrahedronIndex, int materialIndex, MeshBuildData buildData)
    {
        int a = CubeTetrahedra[tetrahedronIndex, 0];
        int b = CubeTetrahedra[tetrahedronIndex, 1];
        int c = CubeTetrahedra[tetrahedronIndex, 2];
        int d = CubeTetrahedra[tetrahedronIndex, 3];

        int[] tetraCorners = { a, b, c, d };
        List<int> inside = new List<int>(4);
        List<int> outside = new List<int>(4);
        for (int i = 0; i < tetraCorners.Length; i++)
        {
            if (cubeDensities[tetraCorners[i]] > IsoLevel)
            {
                inside.Add(tetraCorners[i]);
            }
            else
            {
                outside.Add(tetraCorners[i]);
            }
        }

        if (inside.Count == 0 || inside.Count == 4)
        {
            return;
        }

        if (inside.Count == 1)
        {
            Vector3 v0 = InterpolateEdge(cubePositions[inside[0]], cubePositions[outside[0]], cubeDensities[inside[0]], cubeDensities[outside[0]]);
            Vector3 v1 = InterpolateEdge(cubePositions[inside[0]], cubePositions[outside[1]], cubeDensities[inside[0]], cubeDensities[outside[1]]);
            Vector3 v2 = InterpolateEdge(cubePositions[inside[0]], cubePositions[outside[2]], cubeDensities[inside[0]], cubeDensities[outside[2]]);
            AddTriangle(v0, v1, v2, inside, outside, cubePositions, materialIndex, buildData);
            return;
        }

        if (inside.Count == 3)
        {
            Vector3 v0 = InterpolateEdge(cubePositions[outside[0]], cubePositions[inside[0]], cubeDensities[outside[0]], cubeDensities[inside[0]]);
            Vector3 v1 = InterpolateEdge(cubePositions[outside[0]], cubePositions[inside[1]], cubeDensities[outside[0]], cubeDensities[inside[1]]);
            Vector3 v2 = InterpolateEdge(cubePositions[outside[0]], cubePositions[inside[2]], cubeDensities[outside[0]], cubeDensities[inside[2]]);
            AddTriangle(v0, v2, v1, inside, outside, cubePositions, materialIndex, buildData);
            return;
        }

        if (inside.Count == 2)
        {
            Vector3 v0 = InterpolateEdge(cubePositions[inside[0]], cubePositions[outside[0]], cubeDensities[inside[0]], cubeDensities[outside[0]]);
            Vector3 v1 = InterpolateEdge(cubePositions[inside[0]], cubePositions[outside[1]], cubeDensities[inside[0]], cubeDensities[outside[1]]);
            Vector3 v2 = InterpolateEdge(cubePositions[inside[1]], cubePositions[outside[0]], cubeDensities[inside[1]], cubeDensities[outside[0]]);
            Vector3 v3 = InterpolateEdge(cubePositions[inside[1]], cubePositions[outside[1]], cubeDensities[inside[1]], cubeDensities[outside[1]]);
            AddTriangle(v0, v1, v2, inside, outside, cubePositions, materialIndex, buildData);
            AddTriangle(v2, v1, v3, inside, outside, cubePositions, materialIndex, buildData);
        }
    }

    private void AddTriangle(
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        List<int> insideIndices,
        List<int> outsideIndices,
        Vector3[] cubePositions,
        int materialIndex,
        MeshBuildData buildData)
    {
        Vector3 insideCentroid = Vector3.zero;
        for (int i = 0; i < insideIndices.Count; i++)
        {
            insideCentroid += cubePositions[insideIndices[i]];
        }
        insideCentroid /= Mathf.Max(1, insideIndices.Count);

        Vector3 outsideCentroid = Vector3.zero;
        for (int i = 0; i < outsideIndices.Count; i++)
        {
            outsideCentroid += cubePositions[outsideIndices[i]];
        }
        outsideCentroid /= Mathf.Max(1, outsideIndices.Count);

        Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
        Vector3 desiredDirection = outsideCentroid - insideCentroid;
        if (Vector3.Dot(normal, desiredDirection) < 0f)
        {
            (v1, v2) = (v2, v1);
        }

        int clampedMaterialIndex = Mathf.Clamp(materialIndex, 0, buildData.submeshIndices.Length - 1);
        List<int> indices = buildData.submeshIndices[clampedMaterialIndex];
        int vertexStart = buildData.vertices.Count;
        buildData.vertices.Add(v0);
        buildData.vertices.Add(v1);
        buildData.vertices.Add(v2);
        indices.Add(vertexStart);
        indices.Add(vertexStart + 1);
        indices.Add(vertexStart + 2);
    }

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

        foreach (Vector3Int chunkCoordinate in affectedChunkCoordinates)
        {
            RebuildChunk(chunkCoordinate);
        }

        if (notifyGeometryChange)
        {
            NotifyGeometryChanged(localPoint, radiusMeters, affectedChunkCoordinates);
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
        if (!logExcavationDebug)
        {
            return;
        }

        Debug.Log($"[{nameof(ProceduralVoxelTerrain)}:{name}] {message}", this);
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

    private bool TryGetExcavationBlockReason(Vector3 worldPoint, out string blockReason)
    {
        blockReason = string.Empty;

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

    private bool IsCellSolid(int x, int y, int z)
    {
        float densitySum = 0f;
        for (int corner = 0; corner < 8; corner++)
        {
            int sampleX = x + (int)CubeCornerOffsets[corner].x;
            int sampleY = y + (int)CubeCornerOffsets[corner].y;
            int sampleZ = z + (int)CubeCornerOffsets[corner].z;
            densitySum += densitySamples[GetSampleIndex(sampleX, sampleY, sampleZ)];
        }

        return (densitySum / 8f) > IsoLevel;
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

    private Transform GetGeneratedRoot()
    {
        return transform.Find(GeneratedRootName);
    }

    private Transform EnsureGeneratedRoot()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot != null)
        {
            return generatedRoot;
        }

        GameObject rootObject = new GameObject(GeneratedRootName);
        rootObject.layer = gameObject.layer;
        rootObject.transform.SetParent(transform, false);
        rootObject.transform.localPosition = Vector3.zero;
        rootObject.transform.localRotation = Quaternion.identity;
        rootObject.transform.localScale = Vector3.one;
        return rootObject.transform;
    }

    private Material[] BuildSharedMaterials()
    {
        Material[] materials = new Material[Mathf.Max(1, materialDefinitions.Count)];
        for (int i = 0; i < materials.Length; i++)
        {
            VoxelTerrainMaterialDefinition definition = i < materialDefinitions.Count ? materialDefinitions[i] : null;
            materials[i] = ResolveRenderMaterial(definition);
        }

        return materials;
    }

    private Material ResolveRenderMaterial(VoxelTerrainMaterialDefinition definition)
    {
        if (definition != null && ProceduralRenderMaterialUtility.CanUseAssignedMaterial(definition.material))
        {
            return definition.material;
        }

        Color colorTint = definition != null ? definition.colorTint : Color.gray;
        string displayName = definition != null ? definition.ResolveDisplayName() : "Voxel Terrain";
        string cacheKey = $"{displayName}_{ColorUtility.ToHtmlStringRGBA(colorTint)}";
        if (AutoMaterials.TryGetValue(cacheKey, out Material cachedMaterial) && cachedMaterial != null)
        {
            return cachedMaterial;
        }

        Material material = ProceduralRenderMaterialUtility.CreateOpaqueMaterial(
            $"{displayName} Auto Material",
            colorTint,
            0.18f,
            0f);
        if (material == null)
        {
            return null;
        }

        AutoMaterials[cacheKey] = material;
        return material;
    }

    private GenerationContext BuildGenerationContext(int generationSeed)
    {
        System.Random random = new System.Random(generationSeed);
        return new GenerationContext
        {
            surfaceOffsetX = NextFloat(random, -10000f, 10000f),
            surfaceOffsetZ = NextFloat(random, -10000f, 10000f),
            ridgeOffsetX = NextFloat(random, -10000f, 10000f),
            ridgeOffsetZ = NextFloat(random, -10000f, 10000f),
            detailOffsetX = NextFloat(random, -10000f, 10000f),
            detailOffsetZ = NextFloat(random, -10000f, 10000f),
            caveOffsetX = NextFloat(random, -10000f, 10000f),
            caveOffsetY = NextFloat(random, -10000f, 10000f),
            caveOffsetZ = NextFloat(random, -10000f, 10000f),
            materialOffsetX = NextFloat(random, -10000f, 10000f),
            materialOffsetY = NextFloat(random, -10000f, 10000f),
            materialOffsetZ = NextFloat(random, -10000f, 10000f)
        };
    }

    private bool IsWithinWorld(Vector3 localPoint)
    {
        Vector3 worldSize = TotalWorldSize;
        return localPoint.x >= 0f && localPoint.x <= worldSize.x
            && localPoint.y >= 0f && localPoint.y <= worldSize.y
            && localPoint.z >= 0f && localPoint.z <= worldSize.z;
    }

    private Vector3Int GetChunkCoordinateForCell(int cellX, int cellY, int cellZ)
    {
        return new Vector3Int(
            Mathf.Clamp(cellX / cellsPerChunkAxis, 0, chunkCounts.x - 1),
            Mathf.Clamp(cellY / cellsPerChunkAxis, 0, chunkCounts.y - 1),
            Mathf.Clamp(cellZ / cellsPerChunkAxis, 0, chunkCounts.z - 1));
    }

    private int GetSampleIndex(int x, int y, int z)
    {
        return x + (TotalSamplesX * (y + (TotalSamplesY * z)));
    }

    private int GetCellIndex(int x, int y, int z)
    {
        return x + (TotalCellsX * (y + (TotalCellsY * z)));
    }

    private void GetCellCoordinates(int cellIndex, out int x, out int y, out int z)
    {
        z = cellIndex / (TotalCellsX * TotalCellsY);
        int remainder = cellIndex - (z * TotalCellsX * TotalCellsY);
        y = remainder / TotalCellsX;
        x = remainder % TotalCellsX;
    }

    private static bool CubeIntersectsSurface(float[] cubeDensities)
    {
        bool hasPositive = false;
        bool hasNegative = false;
        for (int i = 0; i < 8; i++)
        {
            if (cubeDensities[i] > IsoLevel)
            {
                hasPositive = true;
            }
            else
            {
                hasNegative = true;
            }

            if (hasPositive && hasNegative)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector3 InterpolateEdge(Vector3 pointA, Vector3 pointB, float densityA, float densityB)
    {
        float denominator = densityB - densityA;
        if (Mathf.Abs(denominator) <= 0.0001f)
        {
            return Vector3.Lerp(pointA, pointB, 0.5f);
        }

        float t = Mathf.Clamp01((IsoLevel - densityA) / denominator);
        return Vector3.Lerp(pointA, pointB, t);
    }

    private static float EvaluateFractalNoise(float x, float z, int octaves, float persistence, float lacunarity)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float total = 0f;
        float normalization = 0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            total += Mathf.PerlinNoise(x * frequency, z * frequency) * amplitude;
            normalization += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return normalization <= 0.0001f ? 0f : total / normalization;
    }

    private static float EvaluatePerlin3D(float x, float y, float z)
    {
        float xy = Mathf.PerlinNoise(x, y);
        float yz = Mathf.PerlinNoise(y, z);
        float xz = Mathf.PerlinNoise(x, z);
        float yx = Mathf.PerlinNoise(y, x);
        float zy = Mathf.PerlinNoise(z, y);
        float zx = Mathf.PerlinNoise(z, x);
        return (xy + yz + xz + yx + zy + zx) / 6f;
    }

    private static float NextFloat(System.Random random, float minInclusive, float maxInclusive)
    {
        if (Mathf.Approximately(minInclusive, maxInclusive))
        {
            return minInclusive;
        }

        float min = Mathf.Min(minInclusive, maxInclusive);
        float max = Mathf.Max(minInclusive, maxInclusive);
        return (float)(min + (random.NextDouble() * (max - min)));
    }

    private static List<VoxelTerrainMaterialDefinition> BuildDefaultMaterialDefinitions()
    {
        return new List<VoxelTerrainMaterialDefinition>
        {
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Topsoil",
                compositionItemName = "Topsoil",
                colorTint = new Color(0.22f, 0.16f, 0.1f, 1f),
                depthRangeMeters = new Vector2(0f, 1.2f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 40f,
                distributionNoiseThreshold = 0f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Alluvial Clay",
                compositionItemName = "Clay Deposit",
                colorTint = new Color(0.42f, 0.31f, 0.23f, 1f),
                depthRangeMeters = new Vector2(0f, 2.4f),
                normalizedHeightRange = new Vector2(0f, 0.38f),
                distributionNoiseScaleMeters = 22f,
                distributionNoiseThreshold = 0.48f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Clay Subsoil",
                compositionItemName = "Clay Deposit",
                colorTint = new Color(0.56f, 0.39f, 0.29f, 1f),
                depthRangeMeters = new Vector2(0.8f, 5.5f),
                normalizedHeightRange = new Vector2(0f, 0.72f),
                distributionNoiseScaleMeters = 26f,
                distributionNoiseThreshold = 0.62f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Weathered Surface Stone",
                compositionItemName = "Weathered Surface Stone",
                colorTint = new Color(0.47f, 0.44f, 0.4f, 1f),
                depthRangeMeters = new Vector2(0f, 2.2f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 64f,
                distributionNoiseThreshold = 0.72f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Iron Seam",
                compositionItemName = "Iron-Rich Stone",
                colorTint = new Color(0.56f, 0.28f, 0.21f, 1f),
                depthRangeMeters = new Vector2(4f, 48f),
                normalizedHeightRange = new Vector2(0.05f, 0.95f),
                distributionNoiseScaleMeters = 16f,
                distributionNoiseThreshold = 0.76f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Silicate Bedrock",
                compositionItemName = "Stone (Silicate-Rich)",
                colorTint = new Color(0.56f, 0.58f, 0.62f, 1f),
                isFallbackMaterial = true,
                depthRangeMeters = new Vector2(0f, 128f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 24f,
                distributionNoiseThreshold = 0f
            }
        };
    }

    private void EnsureDefaultMaterialDefinitionsPresent()
    {
        if (materialDefinitions == null || materialDefinitions.Count == 0 || !UsesDefaultOlympicMaterialStack())
        {
            return;
        }

        List<VoxelTerrainMaterialDefinition> defaultDefinitions = BuildDefaultMaterialDefinitions();
        for (int i = 0; i < defaultDefinitions.Count; i++)
        {
            VoxelTerrainMaterialDefinition defaultDefinition = defaultDefinitions[i];
            int existingIndex = FindMaterialDefinitionIndex(defaultDefinition.displayName);
            if (existingIndex < 0)
            {
                materialDefinitions.Insert(Mathf.Min(i, materialDefinitions.Count), CloneMaterialDefinition(defaultDefinition));
                continue;
            }

            if (string.Equals(defaultDefinition.displayName, "Weathered Surface Stone", StringComparison.OrdinalIgnoreCase))
            {
                VoxelTerrainMaterialDefinition existingDefinition = materialDefinitions[existingIndex];
                if (string.IsNullOrWhiteSpace(existingDefinition.compositionItemName) ||
                    string.Equals(existingDefinition.compositionItemName, "Stone (Silicate-Rich)", StringComparison.OrdinalIgnoreCase))
                {
                    existingDefinition.compositionItemName = defaultDefinition.compositionItemName;
                    if (Mathf.Approximately(existingDefinition.depthRangeMeters.x, 0f) &&
                        Mathf.Approximately(existingDefinition.depthRangeMeters.y, 1.4f) &&
                        Mathf.Approximately(existingDefinition.distributionNoiseScaleMeters, 34f) &&
                        Mathf.Approximately(existingDefinition.distributionNoiseThreshold, 0.14f))
                    {
                        existingDefinition.depthRangeMeters = defaultDefinition.depthRangeMeters;
                        existingDefinition.distributionNoiseScaleMeters = defaultDefinition.distributionNoiseScaleMeters;
                        existingDefinition.distributionNoiseThreshold = defaultDefinition.distributionNoiseThreshold;
                        existingDefinition.colorTint = defaultDefinition.colorTint;
                    }
                }
            }
        }
    }

    private bool UsesDefaultOlympicMaterialStack()
    {
        return FindMaterialDefinitionIndex("Silicate Bedrock") >= 0 ||
               FindMaterialDefinitionIndex("Weathered Surface Stone") >= 0 ||
               FindMaterialDefinitionIndex("Clay Subsoil") >= 0 ||
               FindMaterialDefinitionIndex("Alluvial Clay") >= 0;
    }

    private int FindMaterialDefinitionIndex(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || materialDefinitions == null)
        {
            return -1;
        }

        for (int i = 0; i < materialDefinitions.Count; i++)
        {
            if (string.Equals(materialDefinitions[i]?.ResolveDisplayName(), displayName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static VoxelTerrainMaterialDefinition CloneMaterialDefinition(VoxelTerrainMaterialDefinition source)
    {
        if (source == null)
        {
            return null;
        }

        return new VoxelTerrainMaterialDefinition
        {
            displayName = source.displayName,
            compositionItemName = source.compositionItemName,
            compositionOverride = source.compositionOverride,
            material = source.material,
            colorTint = source.colorTint,
            isFallbackMaterial = source.isFallbackMaterial,
            depthRangeMeters = source.depthRangeMeters,
            normalizedHeightRange = source.normalizedHeightRange,
            distributionNoiseScaleMeters = source.distributionNoiseScaleMeters,
            distributionNoiseThreshold = source.distributionNoiseThreshold
        };
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawWorldBoundsGizmos && !drawChunkBoundsGizmos)
        {
            return;
        }

        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 worldSize = TotalWorldSize;
        if (drawWorldBoundsGizmos)
        {
            Gizmos.color = boundsGizmoColor;
            Gizmos.DrawWireCube(worldSize * 0.5f, worldSize);
        }

        if (drawChunkBoundsGizmos)
        {
            Gizmos.color = new Color(boundsGizmoColor.r, boundsGizmoColor.g, boundsGizmoColor.b, 0.4f);
            Vector3 chunkWorldSize = Vector3.one * (cellsPerChunkAxis * voxelSizeMeters);
            for (int z = 0; z < chunkCounts.z; z++)
            {
                for (int y = 0; y < chunkCounts.y; y++)
                {
                    for (int x = 0; x < chunkCounts.x; x++)
                    {
                        Vector3 center = Vector3.Scale(new Vector3(x, y, z), chunkWorldSize) + (chunkWorldSize * 0.5f);
                        Gizmos.DrawWireCube(center, chunkWorldSize);
                    }
                }
            }
        }
    }
}
