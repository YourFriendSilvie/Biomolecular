using System;
using System.Collections.Generic;
using UnityEngine;

public class ProceduralVoxelTerrainWaterSystem : MonoBehaviour, IRaycastHarvestableProvider
{
    private const string DefaultGeneratedRootName = "Generated Voxel Water";
    private const float DefaultHarvestedWaterMassGrams = 5000000f;
    private const float WaterDensityGramsPerCubicMeter = 1000000f;
    private const float MinimumRenderableLakeVolumeCubicMeters = 0.001f;
    private const int MaxLakeHydrologyResolutionPasses = 8;
    private const int MaxLakeOverflowExpansionPasses = 6;
    private const string FreshwaterBedMaterialName = "Alluvial Clay";
    private const string FreshwaterBedFallbackCompositionName = "Clay Deposit";
    private const int RiverRenderSubdivisionsPerSegment = 3;
    private const string InteractionRootObjectName = "Interaction";
    private static Material cachedFreshWaterMaterial;
    private static Material cachedSaltWaterMaterial;

    private sealed class GeneratedLake
    {
        public Vector3 center;
        public float radius;
        public float surfaceY;
        public float storedVolumeCubicMeters;
        public float captureRadius;
        public float[] shorelineRadii = Array.Empty<float>();
        public float gridCellSize;
        public int gridCountPerAxis;
        public Vector2 gridOriginXZ;
        public float[] cellHeights = Array.Empty<float>();
        public bool[] floodedCells = Array.Empty<bool>();
        public Vector3[] surfaceVertices = Array.Empty<Vector3>();
        public int[] surfaceTriangles = Array.Empty<int>();
        public LakeTerrainPatch terrainPatch;
        public Bounds surfaceBounds;
        public int floodedCellCount;
        public Bounds influenceBounds;
        public GameObject waterObject;
    }

    [Serializable]
    private struct GeneratedRiverSegment
    {
        public Vector3 start;
        public Vector3 end;
        public float width;
        public float surfaceY;
    }

    private sealed class GeneratedRiver
    {
        public readonly List<Vector3> points = new List<Vector3>();
        public readonly List<float> widths = new List<float>();
        public readonly List<Vector3> waterPath = new List<Vector3>();
        public readonly List<float> widthProfile = new List<float>();
        public float baseWidth;
        public Bounds influenceBounds;
        public GameObject waterObject;
    }

    private sealed class LakeSolveResult
    {
        public float surfaceY;
        public float volumeCubicMeters;
        public bool touchesOpenBoundary;
        public float cellSize;
        public int cellCountPerAxis;
        public Vector2 originXZ;
        public float[] cellHeights = Array.Empty<float>();
        public bool[] floodedCells = Array.Empty<bool>();
        public Vector3[] surfaceVertices = Array.Empty<Vector3>();
        public int[] surfaceTriangles = Array.Empty<int>();
        public Bounds surfaceBounds;
        public int floodedCellCount;
    }

    private sealed class LakeSurfaceComponent
    {
        public Vector3 representativePoint;
        public LakeSolveResult solveResult;
    }

    private sealed class LakeTerrainPatch
    {
        public readonly List<LakeTerrainTriangle> triangles = new List<LakeTerrainTriangle>();
        public readonly HashSet<Vector3Int> chunkCoordinates = new HashSet<Vector3Int>();
        public float minHeight = float.PositiveInfinity;
        public float maxHeight = float.NegativeInfinity;
        public Bounds bounds;
        public bool hasBounds;
    }

    private sealed class LakeTerrainTriangle
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 c;
        public readonly List<LakeTriangleNeighbor> neighbors = new List<LakeTriangleNeighbor>(3);
        public readonly List<LakeTriangleEdge> boundaryEdges = new List<LakeTriangleEdge>(3);
    }

    private readonly struct LakeTriangleNeighbor
    {
        public readonly int triangleIndex;
        public readonly Vector3 edgeA;
        public readonly Vector3 edgeB;

        public LakeTriangleNeighbor(int triangleIndex, Vector3 edgeA, Vector3 edgeB)
        {
            this.triangleIndex = triangleIndex;
            this.edgeA = edgeA;
            this.edgeB = edgeB;
        }
    }

    private readonly struct LakeTriangleEdge
    {
        public readonly Vector3 edgeA;
        public readonly Vector3 edgeB;

        public LakeTriangleEdge(Vector3 edgeA, Vector3 edgeB)
        {
            this.edgeA = edgeA;
            this.edgeB = edgeB;
        }
    }

    private readonly struct LakePendingEdge
    {
        public readonly int triangleIndex;
        public readonly Vector3 edgeA;
        public readonly Vector3 edgeB;

        public LakePendingEdge(int triangleIndex, Vector3 edgeA, Vector3 edgeB)
        {
            this.triangleIndex = triangleIndex;
            this.edgeA = edgeA;
            this.edgeB = edgeB;
        }
    }

    private readonly struct LakeVertexKey : IEquatable<LakeVertexKey>
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

    private readonly struct LakeEdgeKey : IEquatable<LakeEdgeKey>
    {
        public readonly LakeVertexKey a;
        public readonly LakeVertexKey b;

        public LakeEdgeKey(LakeVertexKey first, LakeVertexKey second)
        {
            if (LakeVertexKey.Compare(first, second) <= 0)
            {
                a = first;
                b = second;
            }
            else
            {
                a = second;
                b = first;
            }
        }

        public bool Equals(LakeEdgeKey other)
        {
            return a.Equals(other.a) && b.Equals(other.b);
        }

        public override bool Equals(object obj)
        {
            return obj is LakeEdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (a.GetHashCode() * 397) ^ b.GetHashCode();
            }
        }
    }

    private sealed class FreshWaterHarvestTarget : IHarvestable
    {
        private readonly ProceduralVoxelTerrainWaterSystem owner;
        private readonly GeneratedLake lake;
        private readonly GeneratedRiver river;

        public FreshWaterHarvestTarget(ProceduralVoxelTerrainWaterSystem owner, GeneratedLake lake)
        {
            this.owner = owner;
            this.lake = lake;
        }

        public FreshWaterHarvestTarget(ProceduralVoxelTerrainWaterSystem owner, GeneratedRiver river)
        {
            this.owner = owner;
            this.river = river;
        }

        public bool Harvest(Inventory playerInventory)
        {
            if (owner == null)
            {
                return false;
            }

            if (lake != null)
            {
                return owner.HarvestLake(lake, playerInventory);
            }

            if (river != null)
            {
                return owner.HarvestRiver(river, playerInventory);
            }

            return false;
        }

        public string GetHarvestDisplayName()
        {
            return owner != null ? owner.GetFreshWaterDisplayName() : "Fresh Water";
        }

        public string GetHarvestPreview()
        {
            if (owner == null)
            {
                return "Nothing to harvest";
            }

            if (lake != null)
            {
                return owner.GetLakeHarvestPreview(lake);
            }

            if (river != null)
            {
                return owner.GetFreshWaterHarvestPreview(owner.freshwaterHarvestMassGrams, 0f);
            }

            return "Nothing to harvest";
        }
    }

    [Header("Voxel Terrain")]
    [SerializeField] private ProceduralVoxelTerrain voxelTerrain;
    [SerializeField] private bool generateTerrainBeforeWater = true;

    [Header("Generation")]
    [SerializeField] private int seed = 35791;
    [SerializeField] private bool randomizeSeed = false;
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private string generatedRootName = DefaultGeneratedRootName;

    [Header("Ocean")]
    [SerializeField] private bool generateOcean = true;
    [SerializeField, Min(0f)] private float seaLevelMeters = 4.8f;
    [SerializeField, Min(1f)] private float oceanPaddingMeters = 220f;

    [Header("Freshwater")]
    [SerializeField] private bool generateFreshwater = true;
    [SerializeField] private bool generateLakes = true;
    [SerializeField] private bool generateRivers = false;
    [SerializeField, Range(0, 4)] private int lakeCount = 2;
    [SerializeField] private Vector2 lakeRadiusRangeMeters = new Vector2(10f, 18f);
    [SerializeField, Min(0.25f)] private float lakeDepthMeters = 1.8f;
    [SerializeField, Range(0, 3)] private int riverCount = 0;
    [SerializeField] private Vector2 riverWidthRangeMeters = new Vector2(4f, 8f);
    [SerializeField, Min(0.25f)] private float riverDepthMeters = 1.2f;
    [SerializeField, Range(8, 64)] private int riverSampleCount = 24;
    [SerializeField] private Vector2 riverSourceHeightRangeNormalized = new Vector2(0.38f, 0.85f);
    [SerializeField, Min(100f)] private float freshwaterHarvestMassGrams = DefaultHarvestedWaterMassGrams;

    [Header("Voxel Carving")]
    [SerializeField] private bool carveTerrainForWater = true;
    [SerializeField, Min(0.25f)] private float riverCarveStepMeters = 3f;

    [Header("Rendering")]
    [SerializeField, Min(0.05f)] private float waterSurfaceThicknessMeters = 0.2f;
    [SerializeField] private Material freshWaterMaterial;
    [SerializeField] private Material saltWaterMaterial;
    [SerializeField] private Color freshWaterColor = new Color(0.23f, 0.5f, 0.64f, 0.82f);
    [SerializeField] private Color saltWaterColor = new Color(0.16f, 0.34f, 0.58f, 0.84f);
    [SerializeField] private bool useTriggerColliders = true;

    [Header("Dynamic Updates")]
    [SerializeField] private bool updateWaterOnTerrainDeform = true;
    [SerializeField, Range(12, 72)] private int lakeOutlineSampleCount = 40;
    [SerializeField, Min(0.25f)] private float lakeVolumeSampleSpacingMeters = 1f;
    [SerializeField, Min(0f)] private float lakeDynamicExpansionMeters = 8f;
    [SerializeField, Min(0f)] private float waterUpdatePaddingMeters = 2f;
    [SerializeField, Min(0f)] private float terrainRefreshDebounceSeconds = 0.03f;

    [Header("Debug")]
    [SerializeField] private bool logLakeDebug = false;

    private readonly List<GeneratedLake> generatedLakes = new List<GeneratedLake>();
    private readonly List<GeneratedRiverSegment> generatedRiverSegments = new List<GeneratedRiverSegment>();
    private readonly List<GeneratedRiver> generatedRivers = new List<GeneratedRiver>();
    private bool isGeneratingWater;
    private int lastProcessedTerrainRefreshFrame = -1;
    private Vector3 lastProcessedTerrainRefreshCenter = new Vector3(float.NaN, float.NaN, float.NaN);
    private Vector3 lastProcessedTerrainRefreshSize = new Vector3(float.NaN, float.NaN, float.NaN);
    private readonly HashSet<Vector3Int> pendingTerrainRefreshChunks = new HashSet<Vector3Int>();
    private bool hasPendingTerrainRefresh;
    private Bounds pendingTerrainRefreshBounds;
    private float pendingTerrainRefreshProcessTime;

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;
    public float SeaLevelMeters => seaLevelMeters;

    private void Reset()
    {
        ApplyCoastalRainforestWaterPreset();
    }

    private void OnEnable()
    {
        SubscribeToTerrainChanges();
    }

    private void OnDisable()
    {
        UnsubscribeFromTerrainChanges();
        ClearPendingTerrainRefresh();
    }

    private void LateUpdate()
    {
        ProcessPendingTerrainRefresh();
    }

    private void Start()
    {
        if (Application.isPlaying && generateOnStart)
        {
            GenerateWater(clearExistingBeforeGenerate);
        }
    }

    private void OnValidate()
    {
        generatedRootName = string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName.Trim();
        seaLevelMeters = Mathf.Max(0f, seaLevelMeters);
        oceanPaddingMeters = Mathf.Max(1f, oceanPaddingMeters);
        lakeCount = Mathf.Clamp(lakeCount, 0, 4);
        lakeRadiusRangeMeters = new Vector2(
            Mathf.Max(1f, Mathf.Min(lakeRadiusRangeMeters.x, lakeRadiusRangeMeters.y)),
            Mathf.Max(1f, Mathf.Max(lakeRadiusRangeMeters.x, lakeRadiusRangeMeters.y)));
        lakeDepthMeters = Mathf.Max(0.25f, lakeDepthMeters);
        riverCount = Mathf.Clamp(riverCount, 0, 3);
        riverWidthRangeMeters = new Vector2(
            Mathf.Max(1f, Mathf.Min(riverWidthRangeMeters.x, riverWidthRangeMeters.y)),
            Mathf.Max(1f, Mathf.Max(riverWidthRangeMeters.x, riverWidthRangeMeters.y)));
        riverDepthMeters = Mathf.Max(0.25f, riverDepthMeters);
        riverSampleCount = Mathf.Clamp(riverSampleCount, 8, 64);
        riverSourceHeightRangeNormalized = new Vector2(
            Mathf.Clamp01(Mathf.Min(riverSourceHeightRangeNormalized.x, riverSourceHeightRangeNormalized.y)),
            Mathf.Clamp01(Mathf.Max(riverSourceHeightRangeNormalized.x, riverSourceHeightRangeNormalized.y)));
        freshwaterHarvestMassGrams = Mathf.Max(100f, freshwaterHarvestMassGrams);
        riverCarveStepMeters = Mathf.Max(0.25f, riverCarveStepMeters);
        waterSurfaceThicknessMeters = Mathf.Max(0.05f, waterSurfaceThicknessMeters);
        lakeOutlineSampleCount = Mathf.Clamp(lakeOutlineSampleCount, 12, 72);
        lakeVolumeSampleSpacingMeters = Mathf.Max(0.25f, lakeVolumeSampleSpacingMeters);
        lakeDynamicExpansionMeters = Mathf.Max(0f, lakeDynamicExpansionMeters);
        waterUpdatePaddingMeters = Mathf.Max(0f, waterUpdatePaddingMeters);
        terrainRefreshDebounceSeconds = Mathf.Max(0f, terrainRefreshDebounceSeconds);
        SubscribeToTerrainChanges();
    }

    [ContextMenu("Apply Coastal Rainforest Water Preset")]
    public void ApplyCoastalRainforestWaterPreset()
    {
        seaLevelMeters = 4.8f;
        oceanPaddingMeters = 220f;
        generateFreshwater = true;
        generateLakes = true;
        generateRivers = false;
        lakeCount = 2;
        lakeRadiusRangeMeters = new Vector2(10f, 18f);
        lakeDepthMeters = 1.8f;
        riverCount = 0;
        riverWidthRangeMeters = new Vector2(4f, 8f);
        riverDepthMeters = 1.2f;
        riverSampleCount = 24;
        riverSourceHeightRangeNormalized = new Vector2(0.38f, 0.85f);
        freshwaterHarvestMassGrams = DefaultHarvestedWaterMassGrams;
        riverCarveStepMeters = 3f;
        waterSurfaceThicknessMeters = 0.2f;
    }

    [ContextMenu("Generate Voxel Water")]
    public void GenerateWaterFromContextMenu()
    {
        GenerateWater(clearExistingBeforeGenerate);
    }

    [ContextMenu("Clear Voxel Water")]
    public void ClearGeneratedWaterFromContextMenu()
    {
        ClearGeneratedWater();
    }

    public bool GenerateWater(bool clearExisting)
    {
        if (isGeneratingWater)
        {
            LogLakeDebug("GenerateWater ignored because generation is already in progress.");
            return false;
        }

        isGeneratingWater = true;
        try
        {
            ProceduralVoxelTerrain resolvedTerrain = ResolveVoxelTerrainAndMaybeGenerate();
            if (resolvedTerrain == null || !resolvedTerrain.HasGeneratedTerrain)
            {
                Debug.LogWarning($"{gameObject.name} could not generate voxel water because no voxel terrain is available.");
                return false;
            }

            if (randomizeSeed)
            {
                seed = Environment.TickCount;
            }

            if (clearExisting)
            {
                ClearGeneratedWaterContents();
            }

            generatedLakes.Clear();
            generatedRiverSegments.Clear();
            generatedRivers.Clear();

            System.Random random = new System.Random(seed);
            Bounds bounds = resolvedTerrain.WorldBounds;

            if (ShouldGenerateLakes())
            {
                GenerateLakes(random, resolvedTerrain, bounds);
            }

            if (ShouldGenerateRivers())
            {
                GenerateRivers(random, resolvedTerrain, bounds);
            }

            Transform generatedRoot = EnsureGeneratedRoot();
            if (generateOcean)
            {
                CreateOceanObject(bounds, generatedRoot);
            }

            for (int i = 0; i < generatedLakes.Count; i++)
            {
                CreateLakeObject(generatedLakes[i], generatedRoot);
            }


            for (int i = 0; i < generatedRivers.Count; i++)
            {
                CreateRiverObject(generatedRivers[i], generatedRoot, i);
            }

            SubscribeToTerrainChanges();
            return true;
        }
        finally
        {
            isGeneratingWater = false;
        }
    }

    public bool ClearGeneratedWater()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
            generatedLakes.Clear();
            generatedRiverSegments.Clear();
            generatedRivers.Clear();
            SubscribeToTerrainChanges();
            return false;
        }

        ClearGeneratedWaterContents();

        if (Application.isPlaying)
        {
            Destroy(generatedRoot.gameObject);
        }
        else
        {
            DestroyImmediate(generatedRoot.gameObject);
        }

        generatedLakes.Clear();
        generatedRiverSegments.Clear();
        generatedRivers.Clear();
        SubscribeToTerrainChanges();
        return true;
    }

    private void ClearGeneratedWaterContents()
    {
        generatedLakes.Clear();
        generatedRiverSegments.Clear();
        generatedRivers.Clear();

        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
            return;
        }

        List<GameObject> children = new List<GameObject>();
        foreach (Transform child in generatedRoot)
        {
            if (child != null)
            {
                children.Add(child.gameObject);
            }
        }

        for (int i = 0; i < children.Count; i++)
        {
            GameObject childObject = children[i];
            if (childObject == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                childObject.SetActive(false);
                Destroy(childObject);
            }
            else
            {
                DestroyImmediate(childObject);
            }
        }
    }

    public bool IsPointUnderWater(Vector3 worldPoint, float paddingMeters = 0f)
    {
        ProceduralVoxelTerrain resolvedTerrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (resolvedTerrain == null || !resolvedTerrain.HasGeneratedTerrain)
        {
            return false;
        }

        Bounds bounds = resolvedTerrain.WorldBounds;
        if (generateOcean &&
            worldPoint.x >= bounds.min.x &&
            worldPoint.x <= bounds.max.x &&
            worldPoint.z >= bounds.min.z &&
            worldPoint.z <= bounds.max.z &&
            worldPoint.y <= seaLevelMeters + paddingMeters)
        {
            return true;
        }

        for (int i = 0; i < generatedLakes.Count; i++)
        {
            GeneratedLake lake = generatedLakes[i];
            if (!IsLakeActive(lake))
            {
                continue;
            }

            if (worldPoint.y <= lake.surfaceY + paddingMeters &&
                ContainsPointOnLakeSurface(lake, worldPoint.x, worldPoint.z, paddingMeters))
            {
                return true;
            }
        }

        for (int i = 0; i < generatedRiverSegments.Count; i++)
        {
            if (DistanceToSegmentXZ(worldPoint, generatedRiverSegments[i].start, generatedRiverSegments[i].end, out float projectionT) <= (generatedRiverSegments[i].width * 0.5f) + paddingMeters &&
                worldPoint.y <= Mathf.Lerp(generatedRiverSegments[i].start.y, generatedRiverSegments[i].end.y, projectionT) + paddingMeters)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetTerrainExcavationBlockReason(Vector3 worldPoint, float excavationRadiusMeters, out string blockReason)
    {
        blockReason = string.Empty;

        ProceduralVoxelTerrain terrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (terrain == null || !terrain.HasGeneratedTerrain)
        {
            return false;
        }

        float excavationRadius = Mathf.Max(0f, excavationRadiusMeters);
        float verticalPadding = Mathf.Max(terrain.VoxelSizeMeters * 0.5f, 0.25f);
        float excavationMinY = worldPoint.y - Mathf.Max(excavationRadius, verticalPadding);
        float shorelinePadding = excavationRadius + verticalPadding;
        for (int i = 0; i < generatedLakes.Count; i++)
        {
            GeneratedLake lake = generatedLakes[i];
            if (!IsLakeActive(lake) || excavationMinY > lake.surfaceY + verticalPadding)
            {
                continue;
            }

            if (!ContainsPointOnLakeSurface(lake, worldPoint.x, worldPoint.z, shorelinePadding))
            {
                continue;
            }

            blockReason = "Shoreline mining is disabled while the nearby lake still contains water. Drain the lake first.";
            return true;
        }

        return false;
    }

    public bool TryGetNearestFreshwaterPoint(Vector3 worldPoint, out Vector3 nearestPoint, out float distanceMeters)
    {
        nearestPoint = Vector3.zero;
        distanceMeters = float.PositiveInfinity;
        bool found = false;

        Vector2 pointXZ = new Vector2(worldPoint.x, worldPoint.z);
        for (int i = 0; i < generatedLakes.Count; i++)
        {
            GeneratedLake lake = generatedLakes[i];
            if (!IsLakeActive(lake))
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

        for (int i = 0; i < generatedRiverSegments.Count; i++)
        {
            Vector2 projectedXZ = ClosestPointOnSegmentXZ(
                pointXZ,
                new Vector2(generatedRiverSegments[i].start.x, generatedRiverSegments[i].start.z),
                new Vector2(generatedRiverSegments[i].end.x, generatedRiverSegments[i].end.z),
                out float projectionT);
            float centerlineDistance = Vector2.Distance(pointXZ, projectedXZ);
            float candidateDistance = Mathf.Abs(centerlineDistance - (generatedRiverSegments[i].width * 0.5f));
            if (candidateDistance >= distanceMeters)
            {
                continue;
            }

            distanceMeters = candidateDistance;
            nearestPoint = new Vector3(
                projectedXZ.x,
                Mathf.Lerp(generatedRiverSegments[i].start.y, generatedRiverSegments[i].end.y, projectionT),
                projectedXZ.y);
            found = true;
        }

        return found;
    }

    public string GetLakeDebugSummaryAtPoint(Vector3 worldPoint, float pointPaddingMeters = 0.75f)
    {
        if (!TryResolveLakeNearPoint(worldPoint, pointPaddingMeters, out GeneratedLake lake))
        {
            return $"No active lake near ({worldPoint.x:F1}, {worldPoint.y:F1}, {worldPoint.z:F1}). Active lakes: {CountActiveLakes()}. Split support: surface partition + downstream overflow seeding enabled.";
        }

        int lakeIndex = generatedLakes.IndexOf(lake);
        return $"{FormatLakeDebugSummary(lake, lakeIndex, worldPoint)}\nSplit support: surface partition + downstream overflow seeding enabled.";
    }

    public string GetAllLakeDebugSummary()
    {
        List<string> lines = new List<string>();
        int activeLakeCount = CountActiveLakes();
        lines.Add($"Active lakes: {activeLakeCount}");
        lines.Add("Split support: surface partition + downstream overflow seeding enabled.");
        if (activeLakeCount <= 0)
        {
            return string.Join("\n", lines);
        }

        for (int i = 0; i < generatedLakes.Count; i++)
        {
            GeneratedLake lake = generatedLakes[i];
            if (!IsLakeActive(lake))
            {
                continue;
            }

            lines.Add(FormatLakeDebugSummary(lake, i, null));
        }

        return string.Join("\n", lines);
    }

    public bool TryAddWaterFromRaycast(RaycastHit hit, float waterMassGrams, float pointPaddingMeters = 0.75f)
    {
        if (waterMassGrams <= 0.001f || hit.collider == null)
        {
            return false;
        }

        if (TryResolveLakeFromHit(hit, out GeneratedLake lake))
        {
            return TryAddWaterToLake(lake, waterMassGrams, $"raycast hit at {hit.point}");
        }

        return TryAddWaterAtPoint(hit.point, waterMassGrams, pointPaddingMeters);
    }

    public bool TryAddWaterAtPoint(Vector3 worldPoint, float waterMassGrams, float pointPaddingMeters = 0.75f)
    {
        if (waterMassGrams <= 0.001f)
        {
            return false;
        }

        if (!TryResolveLakeNearPoint(worldPoint, pointPaddingMeters, out GeneratedLake lake))
        {
            LogLakeDebug($"Add water skipped at {worldPoint} because no active lake was found within {Mathf.Max(0f, pointPaddingMeters):F2}m.");
            return false;
        }

        return TryAddWaterToLake(lake, waterMassGrams, $"point {worldPoint}");
    }

    public bool TryCreateDebugLakeAtPoint(Vector3 worldPoint, float radiusMeters)
    {
        ProceduralVoxelTerrain terrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (terrain == null || !terrain.HasGeneratedTerrain)
        {
            return false;
        }

        radiusMeters = Mathf.Max(terrain.VoxelSizeMeters * 4f, radiusMeters);
        if (!terrain.TrySampleSurfaceWorld(worldPoint.x, worldPoint.z, out RaycastHit anchorHit))
        {
            LogLakeDebug($"Debug lake skipped because terrain could not be sampled at {worldPoint}.");
            return false;
        }

        if (!TrySampleLakeShorelineStats(terrain, anchorHit.point, radiusMeters, out float minimumShoreHeight, out float averageShoreHeight))
        {
            LogLakeDebug($"Debug lake skipped because shoreline sampling failed near {anchorHit.point}.");
            return false;
        }

        float surfaceY = Mathf.Max(
            seaLevelMeters + 0.55f,
            Mathf.Min(minimumShoreHeight - 0.08f, averageShoreHeight - 0.05f));
        if (carveTerrainForWater)
        {
            CarveLake(terrain, anchorHit.point, radiusMeters, surfaceY, lakeDepthMeters);
        }

        if (!TryRefineLakeSurfaceY(terrain, anchorHit.point, radiusMeters, surfaceY, out surfaceY))
        {
            LogLakeDebug($"Debug lake skipped because surface refinement failed near {anchorHit.point}.");
            return false;
        }

        if (!TryCreateLakeAtSurface(terrain, anchorHit.point, radiusMeters, surfaceY, out GeneratedLake createdLake))
        {
            LogLakeDebug($"Debug lake failed to initialize near {anchorHit.point}.");
            return false;
        }

        generatedLakes.Add(createdLake);
        Transform generatedRoot = EnsureGeneratedRoot();
        CreateLakeObject(createdLake, generatedRoot);
        LogLakeDebug($"Created debug lake near {anchorHit.point}. Radius={radiusMeters:F2}m, surfaceY={surfaceY:F2}.");
        return true;
    }

    public bool TryCreateMergeTestLakePairAtPoint(
        Vector3 worldPoint,
        Vector3 lateralDirection,
        float radiusMeters,
        float shorelineGapMeters,
        float ridgeHeightMeters)
    {
        ProceduralVoxelTerrain terrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (terrain == null || !terrain.HasGeneratedTerrain)
        {
            return false;
        }

        radiusMeters = Mathf.Max(terrain.VoxelSizeMeters * 4f, radiusMeters);
        shorelineGapMeters = Mathf.Max(terrain.VoxelSizeMeters * 2f, shorelineGapMeters);
        ridgeHeightMeters = Mathf.Max(terrain.VoxelSizeMeters, ridgeHeightMeters);

        Vector3 lateralAxis = Vector3.ProjectOnPlane(lateralDirection, Vector3.up);
        if (lateralAxis.sqrMagnitude <= 0.0001f)
        {
            lateralAxis = Vector3.right;
        }

        lateralAxis.Normalize();
        if (!terrain.TrySampleSurfaceWorld(worldPoint.x, worldPoint.z, out RaycastHit anchorHit))
        {
            LogLakeDebug($"Merge-test lake pair skipped because terrain could not be sampled at {worldPoint}.");
            return false;
        }

        float effectiveRadius = radiusMeters * 0.96f;
        float centerDistance = (effectiveRadius * 2f) + shorelineGapMeters;
        Vector3 firstProbe = anchorHit.point - (lateralAxis * (centerDistance * 0.5f));
        Vector3 secondProbe = anchorHit.point + (lateralAxis * (centerDistance * 0.5f));
        if (!terrain.TrySampleSurfaceWorld(firstProbe.x, firstProbe.z, out RaycastHit firstHit) ||
            !terrain.TrySampleSurfaceWorld(secondProbe.x, secondProbe.z, out RaycastHit secondHit))
        {
            LogLakeDebug($"Merge-test lake pair skipped because one or both lake centers could not be sampled near {anchorHit.point}.");
            return false;
        }

        if (!TrySampleLakeShorelineStats(terrain, firstHit.point, radiusMeters, out float firstMinimumShoreHeight, out float firstAverageShoreHeight) ||
            !TrySampleLakeShorelineStats(terrain, secondHit.point, radiusMeters, out float secondMinimumShoreHeight, out float secondAverageShoreHeight))
        {
            LogLakeDebug($"Merge-test lake pair skipped because shoreline sampling failed near {anchorHit.point}.");
            return false;
        }

        float sharedSurfaceY = Mathf.Max(
            seaLevelMeters + 0.55f,
            Mathf.Min(firstMinimumShoreHeight, secondMinimumShoreHeight) - 0.08f);
        sharedSurfaceY = Mathf.Min(sharedSurfaceY, Mathf.Min(firstAverageShoreHeight, secondAverageShoreHeight) - 0.05f);
        sharedSurfaceY = Mathf.Max(seaLevelMeters + 0.55f, sharedSurfaceY);

        if (carveTerrainForWater)
        {
            CarveLake(terrain, firstHit.point, radiusMeters, sharedSurfaceY, lakeDepthMeters);
            CarveLake(terrain, secondHit.point, radiusMeters, sharedSurfaceY, lakeDepthMeters);
            BuildMergeTestRidge(terrain, firstHit.point, secondHit.point, sharedSurfaceY, shorelineGapMeters, ridgeHeightMeters);
        }

        if (!TryCreateLakeAtSurface(terrain, firstHit.point, radiusMeters, sharedSurfaceY, out GeneratedLake firstLake) ||
            !TryCreateLakeAtSurface(terrain, secondHit.point, radiusMeters, sharedSurfaceY, out GeneratedLake secondLake))
        {
            LogLakeDebug($"Merge-test lake pair failed to initialize near {anchorHit.point}.");
            return false;
        }

        generatedLakes.Add(firstLake);
        generatedLakes.Add(secondLake);

        Transform generatedRoot = EnsureGeneratedRoot();
        CreateLakeObject(firstLake, generatedRoot);
        CreateLakeObject(secondLake, generatedRoot);
        LogLakeDebug(
            $"Created lake pair near {anchorHit.point}. Radius={radiusMeters:F2}m, gap={shorelineGapMeters:F2}m, ridge={ridgeHeightMeters:F2}m. Automatic merge resolution is disabled in the simplified lake runtime.");
        return true;
    }

    public Transform GetGeneratedRoot()
    {
        return transform.Find(string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName);
    }

    private bool ShouldGenerateLakes()
    {
        return generateFreshwater && lakeCount > 0 && (generateLakes || !generateRivers);
    }

    private bool ShouldGenerateRivers()
    {
        return generateFreshwater && generateRivers && riverCount > 0;
    }

    private ProceduralVoxelTerrain ResolveVoxelTerrainAndMaybeGenerate(bool regenerateWhenRequested = true)
    {
        if (voxelTerrain == null)
        {
            voxelTerrain = GetComponent<ProceduralVoxelTerrain>();
        }

        if (voxelTerrain != null && generateTerrainBeforeWater && regenerateWhenRequested)
        {
            voxelTerrain.GenerateTerrain(voxelTerrain.ClearExistingBeforeGenerate);
        }

        return voxelTerrain;
    }

    private Transform EnsureGeneratedRoot()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot != null)
        {
            return generatedRoot;
        }

        GameObject rootObject = new GameObject(generatedRootName);
        rootObject.layer = gameObject.layer;
        rootObject.transform.SetParent(transform, false);
        rootObject.transform.localPosition = Vector3.zero;
        rootObject.transform.localRotation = Quaternion.identity;
        rootObject.transform.localScale = Vector3.one;
        return rootObject.transform;
    }

    private void SubscribeToTerrainChanges()
    {
        if (voxelTerrain == null)
        {
            voxelTerrain = GetComponent<ProceduralVoxelTerrain>();
        }

        if (voxelTerrain == null)
        {
            return;
        }

        voxelTerrain.GeometryChanged -= HandleTerrainGeometryChanged;
        if (ShouldHandleTerrainGeometryChanges())
        {
            voxelTerrain.GeometryChanged += HandleTerrainGeometryChanged;
            return;
        }

        ClearPendingTerrainRefresh();
    }

    private void UnsubscribeFromTerrainChanges()
    {
        if (voxelTerrain != null)
        {
            voxelTerrain.GeometryChanged -= HandleTerrainGeometryChanged;
        }
    }

    private bool ShouldHandleTerrainGeometryChanges()
    {
        return updateWaterOnTerrainDeform &&
               generateFreshwater &&
               generatedRivers.Count > 0;
    }

    private void ClearPendingTerrainRefresh()
    {
        hasPendingTerrainRefresh = false;
        pendingTerrainRefreshChunks.Clear();
    }

    private void HandleTerrainGeometryChanged(VoxelTerrainGeometryChangedEventArgs changeArgs)
    {
        if (!ShouldHandleTerrainGeometryChanges())
        {
            ClearPendingTerrainRefresh();
            return;
        }

        int changedChunkCount = changeArgs.AffectedChunkCoordinates != null ? changeArgs.AffectedChunkCoordinates.Count : 0;
        LogLakeDebug($"GeometryChanged event received. Bounds center={changeArgs.ChangedWorldBounds.center}, size={changeArgs.ChangedWorldBounds.size}, chunks={changedChunkCount}.");
        EnqueueTerrainRefresh(changeArgs.ChangedWorldBounds, changeArgs.AffectedChunkCoordinates);
    }

    private void EnqueueTerrainRefresh(Bounds changedBounds, IReadOnlyCollection<Vector3Int> changedChunkCoordinates)
    {
        if (!hasPendingTerrainRefresh)
        {
            pendingTerrainRefreshBounds = changedBounds;
            hasPendingTerrainRefresh = true;
        }
        else
        {
            pendingTerrainRefreshBounds.Encapsulate(changedBounds.min);
            pendingTerrainRefreshBounds.Encapsulate(changedBounds.max);
        }

        if (changedChunkCoordinates != null)
        {
            foreach (Vector3Int chunkCoordinate in changedChunkCoordinates)
            {
                pendingTerrainRefreshChunks.Add(chunkCoordinate);
            }
        }

        pendingTerrainRefreshProcessTime = Time.unscaledTime + Mathf.Max(0f, terrainRefreshDebounceSeconds);
    }

    private void ProcessPendingTerrainRefresh()
    {
        if (!ShouldHandleTerrainGeometryChanges())
        {
            ClearPendingTerrainRefresh();
            return;
        }

        if (!hasPendingTerrainRefresh || Time.unscaledTime + 0.0001f < pendingTerrainRefreshProcessTime)
        {
            return;
        }

        Bounds changedBounds = pendingTerrainRefreshBounds;
        List<Vector3Int> changedChunks = pendingTerrainRefreshChunks.Count > 0
            ? new List<Vector3Int>(pendingTerrainRefreshChunks)
            : null;

        hasPendingTerrainRefresh = false;
        pendingTerrainRefreshChunks.Clear();
        RefreshWaterForChangedBounds(changedBounds, changedChunks);
    }

    public void RefreshWaterForChangedBounds(Bounds changedBounds)
    {
        RefreshWaterForChangedBounds(changedBounds, null);
    }

    public void RefreshWaterForChangedBounds(Bounds changedBounds, IReadOnlyCollection<Vector3Int> changedChunkCoordinates)
    {
        if (isGeneratingWater)
        {
            LogLakeDebug("RefreshWaterForChangedBounds ignored because water generation is in progress.");
            return;
        }

        if (!updateWaterOnTerrainDeform || !generateFreshwater || generatedLakes.Count == 0 && generatedRivers.Count == 0)
        {
            LogLakeDebug($"RefreshWaterForChangedBounds ignored. updateWaterOnTerrainDeform={updateWaterOnTerrainDeform}, generateFreshwater={generateFreshwater}, lakes={generatedLakes.Count}, rivers={generatedRivers.Count}.");
            return;
        }

        if (Time.frameCount == lastProcessedTerrainRefreshFrame &&
            Vector3.SqrMagnitude(changedBounds.center - lastProcessedTerrainRefreshCenter) <= 0.0001f &&
            Vector3.SqrMagnitude(changedBounds.size - lastProcessedTerrainRefreshSize) <= 0.0001f)
        {
            LogLakeDebug($"RefreshWaterForChangedBounds skipped duplicate bounds at frame {Time.frameCount}. Center={changedBounds.center}, size={changedBounds.size}.");
            return;
        }

        lastProcessedTerrainRefreshFrame = Time.frameCount;
        lastProcessedTerrainRefreshCenter = changedBounds.center;
        lastProcessedTerrainRefreshSize = changedBounds.size;

        ProceduralVoxelTerrain terrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (terrain == null || !terrain.HasGeneratedTerrain)
        {
            LogLakeDebug("RefreshWaterForChangedBounds aborted because voxel terrain is unavailable.");
            return;
        }

        Bounds refreshBounds = changedBounds;
        if (changedChunkCoordinates == null || changedChunkCoordinates.Count == 0)
        {
            refreshBounds.Expand(Mathf.Max(terrain.ChunkWorldSizeMeters, waterUpdatePaddingMeters * 2f));
        }
        else
        {
            refreshBounds.Expand(Mathf.Max(terrain.VoxelSizeMeters, waterUpdatePaddingMeters * 2f));
        }

        LogLakeDebug($"RefreshWaterForChangedBounds processing bounds center={refreshBounds.center}, size={refreshBounds.size}, chunks={(changedChunkCoordinates != null ? changedChunkCoordinates.Count : 0)}.");
        if (generatedRivers.Count == 0)
        {
            LogLakeDebug("RefreshWaterForChangedBounds skipped lake deformation updates because active lakes now stay static until drained.");
            return;
        }

        Transform generatedRoot = EnsureGeneratedRoot();
        bool riversUpdated = RefreshAffectedRivers(terrain, generatedRoot, refreshBounds);
        LogLakeDebug($"RefreshWaterForChangedBounds finished. Lakes updated=False, rivers updated={riversUpdated}.");
        if (riversUpdated)
        {
            RebuildRiverSegmentCache();
        }
    }

    private bool RefreshAffectedLakes(
        ProceduralVoxelTerrain terrain,
        Transform generatedRoot,
        Bounds refreshBounds,
        Bounds changedBounds,
        out Bounds hydrologyBounds,
        out bool hasHydrologyBounds)
    {
        hydrologyBounds = default;
        hasHydrologyBounds = false;
        bool updated = false;
        int originalLakeCount = generatedLakes.Count;
        for (int i = 0; i < originalLakeCount; i++)
        {
            GeneratedLake lake = generatedLakes[i];
            bool active = IsLakeActive(lake);
            bool shouldRefresh = active && ShouldRefreshLakeForChangedBounds(terrain, lake, refreshBounds);
            if (!active || !shouldRefresh)
            {
                if (active)
                {
                    LogLakeDebug($"Skipped {DescribeLake(lake)} for bounds center={refreshBounds.center}, size={refreshBounds.size}.");
                }
                continue;
            }

            Bounds preRefreshBounds = BuildLakeBasinRefreshBounds(terrain, lake);
            LogLakeDebug($"Refreshing {DescribeLake(lake)} for bounds center={changedBounds.center}, size={changedBounds.size}. Stored volume={lake.storedVolumeCubicMeters:F3} m^3.");
            if (!TryRefreshLakeForTerrainChange(terrain, lake, changedBounds, generatedRoot))
            {
                LogLakeDebug($"Refresh failed for {DescribeLake(lake)}.");
                continue;
            }

            CreateLakeObject(lake, generatedRoot);
            EncapsulateHydrologyBounds(ref hydrologyBounds, ref hasHydrologyBounds, preRefreshBounds);
            EncapsulateHydrologyBounds(ref hydrologyBounds, ref hasHydrologyBounds, BuildLakeBasinRefreshBounds(terrain, lake));
            LogLakeDebug($"Refresh succeeded for {DescribeLake(lake)}. Stored volume={lake.storedVolumeCubicMeters:F3} m^3.");
            updated = true;
        }

        return updated;
    }

    private static void EncapsulateHydrologyBounds(ref Bounds hydrologyBounds, ref bool hasHydrologyBounds, Bounds candidateBounds)
    {
        if (candidateBounds.size.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        if (!hasHydrologyBounds)
        {
            hydrologyBounds = candidateBounds;
            hasHydrologyBounds = true;
            return;
        }

        hydrologyBounds.Encapsulate(candidateBounds.min);
        hydrologyBounds.Encapsulate(candidateBounds.max);
    }

    private bool ShouldRefreshLakeForChangedBounds(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        Bounds changedBounds)
    {
        if (terrain == null || lake == null)
        {
            return false;
        }

        Bounds refreshBounds = BuildLakeBasinRefreshBounds(terrain, lake);
        if (refreshBounds.size.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        return refreshBounds.Intersects(changedBounds);
    }

    private Bounds BuildLakeBasinRefreshBounds(ProceduralVoxelTerrain terrain, GeneratedLake lake)
    {
        if (terrain == null || lake == null)
        {
            return default;
        }

        float horizontalPadding = Mathf.Max(Mathf.Max(waterUpdatePaddingMeters, lakeDynamicExpansionMeters), terrain.VoxelSizeMeters * 2f);
        float horizontalExtent = Mathf.Max(lake.radius, Mathf.Min(lake.captureRadius, lake.radius + horizontalPadding));
        Vector3 boundsCenter = lake.center;
        if (HasLakeSurfaceGeometry(lake))
        {
            horizontalExtent = Mathf.Max(
                horizontalExtent,
                Mathf.Max(lake.surfaceBounds.extents.x, lake.surfaceBounds.extents.z) + horizontalPadding);
            boundsCenter.x = lake.surfaceBounds.center.x;
            boundsCenter.z = lake.surfaceBounds.center.z;
        }

        float verticalPadding = Mathf.Max(terrain.VoxelSizeMeters * 2f, waterUpdatePaddingMeters);
        float minY = lake.surfaceY - Mathf.Max(terrain.ChunkWorldSizeMeters, lakeDepthMeters * 2.5f);
        float maxY = lake.surfaceY + Mathf.Max(terrain.ChunkWorldSizeMeters, lakeDepthMeters * 2f);
        if (lake.terrainPatch != null && lake.terrainPatch.hasBounds)
        {
            minY = Mathf.Min(minY, lake.terrainPatch.bounds.min.y - verticalPadding);
            maxY = Mathf.Max(maxY, lake.terrainPatch.bounds.max.y + verticalPadding);
        }

        Bounds terrainBounds = terrain.WorldBounds;
        minY = Mathf.Clamp(minY, terrainBounds.min.y, terrainBounds.max.y);
        maxY = Mathf.Clamp(maxY, terrainBounds.min.y, terrainBounds.max.y);
        if (maxY <= minY + terrain.VoxelSizeMeters)
        {
            maxY = Mathf.Min(terrainBounds.max.y, minY + Mathf.Max(terrain.VoxelSizeMeters, terrain.ChunkWorldSizeMeters));
        }

        return new Bounds(
            new Vector3(boundsCenter.x, (minY + maxY) * 0.5f, boundsCenter.z),
            new Vector3(
                Mathf.Max(terrain.VoxelSizeMeters, horizontalExtent * 2f),
                Mathf.Max(terrain.VoxelSizeMeters, maxY - minY),
                Mathf.Max(terrain.VoxelSizeMeters, horizontalExtent * 2f)));
    }

    private bool RefreshAffectedRivers(ProceduralVoxelTerrain terrain, Transform generatedRoot, Bounds changedBounds)
    {
        bool updated = false;
        for (int i = 0; i < generatedRivers.Count; i++)
        {
            GeneratedRiver river = generatedRivers[i];
            if (river == null || !river.influenceBounds.Intersects(changedBounds))
            {
                continue;
            }

            if (!TryRefreshRiverForTerrainChange(terrain, river))
            {
                continue;
            }

            CreateRiverObject(river, generatedRoot, i);
            updated = true;
        }

        return updated;
    }

    private bool TryInitializeLakeState(ProceduralVoxelTerrain terrain, GeneratedLake lake)
    {
        if (terrain == null || lake == null)
        {
            return false;
        }

        if (!TryEvaluateLakeAtFixedSurfaceWithOverflowExpansion(terrain, lake, lake.surfaceY, null, out LakeTerrainPatch terrainPatch, out LakeSolveResult solveResult) ||
            solveResult.volumeCubicMeters <= MinimumRenderableLakeVolumeCubicMeters)
        {
            return false;
        }

        lake.terrainPatch = terrainPatch;
        ApplyLakeSolveResult(lake, solveResult);
        UpdateLakeInfluenceBounds(terrain, lake);
        return IsLakeActive(lake);
    }

    private bool TryCreateLakeAtSurface(ProceduralVoxelTerrain terrain, Vector3 center, float radiusMeters, float surfaceY, out GeneratedLake lake)
    {
        lake = null;
        if (terrain == null)
        {
            return false;
        }

        float effectiveRadius = radiusMeters * 0.96f;
        GeneratedLake createdLake = new GeneratedLake
        {
            center = center,
            radius = effectiveRadius,
            surfaceY = surfaceY,
            captureRadius = effectiveRadius + lakeDynamicExpansionMeters
        };

        if (!TryInitializeLakeState(terrain, createdLake))
        {
            return false;
        }

        lake = createdLake;
        return true;
    }

    private bool TryRefreshLakeForTerrainChange(ProceduralVoxelTerrain terrain, GeneratedLake lake, Bounds changedBounds, Transform generatedRoot)
    {
        if (terrain == null || lake == null)
        {
            return false;
        }

        float originalCaptureRadius = lake.captureRadius;
        float previousSurfaceY = lake.surfaceY;
        float targetStoredVolumeCubicMeters = lake.storedVolumeCubicMeters;
        ExpandLakeCaptureRadiusForChange(lake, changedBounds);
        if (lake.captureRadius > originalCaptureRadius + 0.001f)
        {
            LogLakeDebug($"Expanded capture radius for {DescribeLake(lake)} from {originalCaptureRadius:F2}m to {lake.captureRadius:F2}m.");
        }

        float localRefreshCaptureRadius = ComputeLocalLakeRefreshCaptureRadius(terrain, lake, changedBounds);
        if (TryBuildLakeTerrainPatch(terrain, lake, out LakeTerrainPatch prebuiltTerrainPatch, changedBounds, localRefreshCaptureRadius))
        {
            if (localRefreshCaptureRadius < lake.captureRadius - 0.001f)
            {
                LogLakeDebug(
                    $"Built local refresh patch for {DescribeLake(lake)} using radius {localRefreshCaptureRadius:F2}m " +
                    $"of capture {lake.captureRadius:F2}m. Patch chunks={prebuiltTerrainPatch.chunkCoordinates.Count}, triangles={prebuiltTerrainPatch.triangles.Count}.");
            }

            if (TrySolveLakeSurfaceForTargetVolume(prebuiltTerrainPatch, lake, targetStoredVolumeCubicMeters, out LakeSolveResult prebuiltSolveResult) &&
                !prebuiltSolveResult.touchesOpenBoundary)
            {
                lake.terrainPatch = prebuiltTerrainPatch;
                ApplyLakeSolveResult(lake, prebuiltSolveResult);
                lake.storedVolumeCubicMeters = ClampLakeStoredVolume(targetStoredVolumeCubicMeters, prebuiltSolveResult);
                LogLakeDebug($"Solved {DescribeLake(lake)} from prebuilt patch. SurfaceY {previousSurfaceY:F3}->{lake.surfaceY:F3}, target volume={targetStoredVolumeCubicMeters:F3} m^3, solved volume={prebuiltSolveResult.volumeCubicMeters:F3} m^3, stored volume={lake.storedVolumeCubicMeters:F3} m^3, triangles={lake.surfaceTriangles.Length / 3}.");
                UpdateLakeInfluenceBounds(terrain, lake);
                return true;
            }

            LogLakeDebug($"Local refresh patch for {DescribeLake(lake)} touched an open boundary; falling back to overflow-expansion solve.");

            GeneratedLake localExpansionLake = CreateTemporaryLakeRefreshSeed(lake, localRefreshCaptureRadius);
            if (localExpansionLake != null &&
                TrySolveLakeForTargetVolumeWithOverflowExpansion(
                    terrain,
                    localExpansionLake,
                    targetStoredVolumeCubicMeters,
                    changedBounds,
                    out LakeTerrainPatch localExpansionPatch,
                    out LakeSolveResult localExpansionSolveResult) &&
                !localExpansionSolveResult.touchesOpenBoundary)
            {
                lake.terrainPatch = localExpansionPatch;
                ApplyLakeSolveResult(lake, localExpansionSolveResult);
                lake.storedVolumeCubicMeters = ClampLakeStoredVolume(targetStoredVolumeCubicMeters, localExpansionSolveResult);
                LogLakeDebug(
                    $"Solved {DescribeLake(lake)} from local overflow expansion. Start radius={localRefreshCaptureRadius:F2}m, " +
                    $"expanded radius={localExpansionLake.captureRadius:F2}m, surfaceY {previousSurfaceY:F3}->{lake.surfaceY:F3}, " +
                    $"target volume={targetStoredVolumeCubicMeters:F3} m^3, solved volume={localExpansionSolveResult.volumeCubicMeters:F3} m^3, " +
                    $"stored volume={lake.storedVolumeCubicMeters:F3} m^3, triangles={lake.surfaceTriangles.Length / 3}.");
                UpdateLakeInfluenceBounds(terrain, lake);
                return true;
            }
        }

        if (!TrySolveLakeForTargetVolumeWithOverflowExpansion(
                terrain,
                lake,
                targetStoredVolumeCubicMeters,
                changedBounds,
                out LakeTerrainPatch terrainPatch,
                out LakeSolveResult solveResult))
        {
            LogLakeDebug($"Failed to solve target volume for {DescribeLake(lake)} using stored volume {lake.storedVolumeCubicMeters:F3} m^3.");
            return false;
        }

        lake.terrainPatch = terrainPatch;
        ApplyLakeSolveResult(lake, solveResult);
        lake.storedVolumeCubicMeters = ClampLakeStoredVolume(targetStoredVolumeCubicMeters, solveResult);
        LogLakeDebug($"Solved {DescribeLake(lake)}. SurfaceY {previousSurfaceY:F3}->{lake.surfaceY:F3}, target volume={targetStoredVolumeCubicMeters:F3} m^3, solved volume={solveResult.volumeCubicMeters:F3} m^3, stored volume={lake.storedVolumeCubicMeters:F3} m^3, triangles={lake.surfaceTriangles.Length / 3}.");
        UpdateLakeInfluenceBounds(terrain, lake);
        return true;
    }

    private bool TryRefreshRiverForTerrainChange(ProceduralVoxelTerrain terrain, GeneratedRiver river)
    {
        if (terrain == null || river == null || river.waterPath.Count < 2)
        {
            return false;
        }

        List<Vector3> updatedWaterPath = BuildRiverWaterPath(terrain, river.waterPath, riverDepthMeters);
        if (updatedWaterPath.Count < 2)
        {
            return false;
        }

        List<float> widthProfile = BuildRiverWidthProfile(updatedWaterPath.Count, river.baseWidth);
        river.waterPath.Clear();
        river.waterPath.AddRange(updatedWaterPath);
        river.widthProfile.Clear();
        river.widthProfile.AddRange(widthProfile);
        PopulateRenderableRiverPath(river);
        UpdateRiverInfluenceBounds(terrain, river);
        return true;
    }

    private bool TryEvaluateLakeAtFixedSurfaceWithOverflowExpansion(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        float surfaceY,
        Bounds? expansionBounds,
        out LakeTerrainPatch terrainPatch,
        out LakeSolveResult solveResult)
    {
        terrainPatch = null;
        solveResult = null;
        if (terrain == null || lake == null)
        {
            return false;
        }

        float maxCaptureRadius = GetMaxPossibleLakeCaptureRadius(terrain, lake.center);
        for (int expansionPass = 0; expansionPass < MaxLakeOverflowExpansionPasses; expansionPass++)
        {
            if (!TryBuildLakeTerrainPatch(terrain, lake, out terrainPatch, expansionBounds) ||
                !TryEvaluateLakeAtSurface(terrainPatch, lake.center, surfaceY, out solveResult))
            {
                return false;
            }

            if (!solveResult.touchesOpenBoundary)
            {
                return true;
            }

            float expandedCaptureRadius = ComputeOverflowExpandedCaptureRadius(terrain, lake, terrainPatch, solveResult);
            if (expandedCaptureRadius <= lake.captureRadius + Mathf.Max(terrain.VoxelSizeMeters * 0.5f, 0.25f))
            {
                return true;
            }

            float previousCaptureRadius = lake.captureRadius;
            lake.captureRadius = Mathf.Min(maxCaptureRadius, expandedCaptureRadius);
            if (lake.captureRadius <= previousCaptureRadius + 0.001f)
            {
                return true;
            }

            LogLakeDebug($"Expanded overflow capture for {DescribeLake(lake)} from {previousCaptureRadius:F2}m to {lake.captureRadius:F2}m at fixed surface {surfaceY:F3}.");
        }

        return terrainPatch != null && solveResult != null;
    }

    private bool TrySolveLakeForTargetVolumeWithOverflowExpansion(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        float targetVolumeCubicMeters,
        Bounds? expansionBounds,
        out LakeTerrainPatch terrainPatch,
        out LakeSolveResult solveResult)
    {
        terrainPatch = null;
        solveResult = null;
        if (terrain == null || lake == null)
        {
            return false;
        }

        float maxCaptureRadius = GetMaxPossibleLakeCaptureRadius(terrain, lake.center);
        for (int expansionPass = 0; expansionPass < MaxLakeOverflowExpansionPasses; expansionPass++)
        {
            if (!TryBuildLakeTerrainPatch(terrain, lake, out terrainPatch, expansionBounds) ||
                !TrySolveLakeSurfaceForTargetVolume(terrainPatch, lake, targetVolumeCubicMeters, out solveResult))
            {
                return false;
            }

            if (!solveResult.touchesOpenBoundary)
            {
                return true;
            }

            float expandedCaptureRadius = ComputeOverflowExpandedCaptureRadius(terrain, lake, terrainPatch, solveResult);
            if (expandedCaptureRadius <= lake.captureRadius + Mathf.Max(terrain.VoxelSizeMeters * 0.5f, 0.25f))
            {
                return true;
            }

            float previousCaptureRadius = lake.captureRadius;
            lake.captureRadius = Mathf.Min(maxCaptureRadius, expandedCaptureRadius);
            if (lake.captureRadius <= previousCaptureRadius + 0.001f)
            {
                return true;
            }

            LogLakeDebug($"Expanded overflow capture for {DescribeLake(lake)} from {previousCaptureRadius:F2}m to {lake.captureRadius:F2}m while solving target volume {targetVolumeCubicMeters:F3} m^3.");
        }

        return terrainPatch != null && solveResult != null;
    }

    private bool TrySolveLakeSurfaceForTargetVolume(
        LakeTerrainPatch terrainPatch,
        GeneratedLake lake,
        float targetVolume,
        out LakeSolveResult resolvedResult)
    {
        resolvedResult = null;
        if (terrainPatch == null || lake == null || terrainPatch.triangles.Count == 0)
        {
            return false;
        }

        float sampleStep = voxelTerrain != null ? Mathf.Max(voxelTerrain.VoxelSizeMeters, 0.25f) : 0.25f;
        targetVolume = Mathf.Max(0f, targetVolume);
        if (targetVolume <= MinimumRenderableLakeVolumeCubicMeters)
        {
            resolvedResult = CreateEmptyLakeSolveResult(terrainPatch, lake.center, terrainPatch.minHeight);
            return true;
        }

        float quickAcceptVolumeTolerance = Mathf.Max(0.02f, sampleStep * sampleStep * 0.05f);
        if (TryEvaluateLakeAtSurface(terrainPatch, lake.center, lake.surfaceY, out LakeSolveResult currentSurfaceResult) &&
            !currentSurfaceResult.touchesOpenBoundary &&
            Mathf.Abs(currentSurfaceResult.volumeCubicMeters - targetVolume) <= quickAcceptVolumeTolerance)
        {
            resolvedResult = currentSurfaceResult;
            return true;
        }

        float lowerBound = terrainPatch.minHeight - sampleStep;
        float upperBound = Mathf.Max(
            lake.surfaceY + sampleStep,
            terrainPatch.maxHeight + Mathf.Max(lakeDepthMeters, sampleStep * 2f));
        float absoluteUpperBound = voxelTerrain != null
            ? voxelTerrain.WorldBounds.max.y - Mathf.Max(voxelTerrain.VoxelSizeMeters * 0.5f, 0.25f)
            : upperBound;
        upperBound = Mathf.Min(upperBound, absoluteUpperBound);
        if (upperBound <= lowerBound + 0.05f)
        {
            upperBound = Mathf.Min(absoluteUpperBound, lowerBound + sampleStep);
        }

        if (!TryEvaluateLakeAtSurface(terrainPatch, lake.center, lowerBound, out LakeSolveResult lowResult) ||
            !TryEvaluateLakeAtSurface(terrainPatch, lake.center, upperBound, out LakeSolveResult highResult))
        {
            return false;
        }

        float volumeTolerance = Mathf.Max(0.001f, sampleStep * sampleStep * 0.005f);
        int expansionCount = 0;
        while (highResult.volumeCubicMeters < targetVolume - volumeTolerance &&
               upperBound < absoluteUpperBound - 0.01f &&
               expansionCount < 8)
        {
            upperBound = Mathf.Min(absoluteUpperBound, upperBound + Mathf.Max(lakeDepthMeters, sampleStep * 2f));
            if (!TryEvaluateLakeAtSurface(terrainPatch, lake.center, upperBound, out highResult))
            {
                return false;
            }

            expansionCount++;
        }

        if (highResult.volumeCubicMeters <= targetVolume + volumeTolerance)
        {
            resolvedResult = highResult;
            return true;
        }

        if (lowResult.volumeCubicMeters >= targetVolume - volumeTolerance)
        {
            resolvedResult = lowResult;
            return true;
        }

        float low = lowerBound;
        float high = upperBound;
        for (int iteration = 0; iteration < 14; iteration++)
        {
            float mid = Mathf.Lerp(low, high, 0.5f);
            if (!TryEvaluateLakeAtSurface(terrainPatch, lake.center, mid, out LakeSolveResult midResult))
            {
                return false;
            }

            if (midResult.volumeCubicMeters >= targetVolume)
            {
                high = mid;
                highResult = midResult;
            }
            else
            {
                low = mid;
                lowResult = midResult;
            }
        }

        if (highResult.volumeCubicMeters <= MinimumRenderableLakeVolumeCubicMeters)
        {
            resolvedResult = lowResult;
        }
        else if (lowResult.volumeCubicMeters <= MinimumRenderableLakeVolumeCubicMeters)
        {
            resolvedResult = highResult;
        }
        else
        {
            float lowDelta = Mathf.Abs(lowResult.volumeCubicMeters - targetVolume);
            float highDelta = Mathf.Abs(highResult.volumeCubicMeters - targetVolume);
            resolvedResult = lowDelta <= highDelta ? lowResult : highResult;
        }

        return true;
    }

    private bool TryEvaluateLakeAtSurface(
        LakeTerrainPatch terrainPatch,
        Vector3 lakeCenter,
        float surfaceY,
        out LakeSolveResult result)
    {
        result = null;
        if (terrainPatch == null || terrainPatch.triangles.Count == 0)
        {
            return false;
        }

        result = CreateEmptyLakeSolveResult(terrainPatch, lakeCenter, surfaceY);
        int seedIndex = FindLakeSeedTriangleIndex(terrainPatch, lakeCenter, surfaceY);
        if (seedIndex < 0)
        {
            return true;
        }

        bool[] visitedTriangles = new bool[terrainPatch.triangles.Count];
        if (!TryEvaluateLakeSurfaceComponentFromSeed(terrainPatch, surfaceY, seedIndex, visitedTriangles, out LakeSurfaceComponent component))
        {
            return true;
        }

        result = component.solveResult;
        return true;
    }

    private bool TryEvaluateLakeComponentsAtSurface(
        LakeTerrainPatch terrainPatch,
        float surfaceY,
        out List<LakeSurfaceComponent> components)
    {
        components = null;
        if (terrainPatch == null || terrainPatch.triangles.Count == 0)
        {
            return false;
        }

        components = new List<LakeSurfaceComponent>(4);
        bool[] visitedTriangles = new bool[terrainPatch.triangles.Count];
        List<Vector3> clippedPolygon = new List<Vector3>(4);
        for (int triangleIndex = 0; triangleIndex < terrainPatch.triangles.Count; triangleIndex++)
        {
            if (visitedTriangles[triangleIndex])
            {
                continue;
            }

            LakeTerrainTriangle triangle = terrainPatch.triangles[triangleIndex];
            if (!TryClipTriangleBelowSurface(triangle.a, triangle.b, triangle.c, surfaceY, clippedPolygon))
            {
                visitedTriangles[triangleIndex] = true;
                continue;
            }

            if (TryEvaluateLakeSurfaceComponentFromSeed(terrainPatch, surfaceY, triangleIndex, visitedTriangles, out LakeSurfaceComponent component))
            {
                components.Add(component);
            }
        }

        return true;
    }

    private bool TryEvaluateLakeSurfaceComponentFromSeed(
        LakeTerrainPatch terrainPatch,
        float surfaceY,
        int seedIndex,
        bool[] visitedTriangles,
        out LakeSurfaceComponent component)
    {
        component = null;
        if (terrainPatch == null ||
            visitedTriangles == null ||
            seedIndex < 0 ||
            seedIndex >= terrainPatch.triangles.Count)
        {
            return false;
        }

        Queue<int> queue = new Queue<int>(Mathf.Max(16, terrainPatch.triangles.Count / 3));
        List<Vector3> clippedPolygon = new List<Vector3>(4);
        List<Vector3> surfaceVertices = new List<Vector3>(terrainPatch.triangles.Count * 3);
        List<int> surfaceTriangles = new List<int>(terrainPatch.triangles.Count * 3);
        float volumeCubicMeters = 0f;
        int floodedTriangleCount = 0;
        bool touchesOpenBoundary = false;
        bool hasBounds = false;
        bool hasRepresentativePoint = false;
        Bounds surfaceBounds = default;
        Vector3 representativePoint = default;

        visitedTriangles[seedIndex] = true;
        queue.Enqueue(seedIndex);
        while (queue.Count > 0)
        {
            int triangleIndex = queue.Dequeue();
            LakeTerrainTriangle triangle = terrainPatch.triangles[triangleIndex];
            if (!TryClipTriangleBelowSurface(triangle.a, triangle.b, triangle.c, surfaceY, clippedPolygon))
            {
                continue;
            }

            if (!hasRepresentativePoint && clippedPolygon.Count > 0)
            {
                representativePoint = new Vector3(clippedPolygon[0].x, surfaceY, clippedPolygon[0].z);
                hasRepresentativePoint = true;
            }

            if (!TryAppendClippedLakePolygon(surfaceY, clippedPolygon, surfaceVertices, surfaceTriangles, ref volumeCubicMeters, ref surfaceBounds, ref hasBounds))
            {
                continue;
            }

            floodedTriangleCount++;
            for (int boundaryEdgeIndex = 0; boundaryEdgeIndex < triangle.boundaryEdges.Count; boundaryEdgeIndex++)
            {
                LakeTriangleEdge boundaryEdge = triangle.boundaryEdges[boundaryEdgeIndex];
                if (Mathf.Min(boundaryEdge.edgeA.y, boundaryEdge.edgeB.y) < surfaceY - 0.01f)
                {
                    touchesOpenBoundary = true;
                    break;
                }
            }

            for (int neighborIndex = 0; neighborIndex < triangle.neighbors.Count; neighborIndex++)
            {
                LakeTriangleNeighbor neighbor = triangle.neighbors[neighborIndex];
                if (neighbor.triangleIndex < 0 ||
                    neighbor.triangleIndex >= visitedTriangles.Length ||
                    visitedTriangles[neighbor.triangleIndex] ||
                    Mathf.Min(neighbor.edgeA.y, neighbor.edgeB.y) >= surfaceY - 0.01f)
                {
                    continue;
                }

                visitedTriangles[neighbor.triangleIndex] = true;
                queue.Enqueue(neighbor.triangleIndex);
            }
        }

        if (!hasRepresentativePoint ||
            surfaceTriangles.Count == 0 ||
            volumeCubicMeters <= MinimumRenderableLakeVolumeCubicMeters)
        {
            return false;
        }

        LakeSolveResult solveResult = CreateEmptyLakeSolveResult(terrainPatch, representativePoint, surfaceY);
        solveResult.volumeCubicMeters = volumeCubicMeters;
        solveResult.surfaceVertices = surfaceVertices.ToArray();
        solveResult.surfaceTriangles = surfaceTriangles.ToArray();
        solveResult.surfaceBounds = hasBounds
            ? surfaceBounds
            : new Bounds(representativePoint, new Vector3(0f, Mathf.Max(waterSurfaceThicknessMeters, 0.05f), 0f));
        solveResult.floodedCellCount = floodedTriangleCount;
        solveResult.touchesOpenBoundary = touchesOpenBoundary;

        component = new LakeSurfaceComponent
        {
            representativePoint = representativePoint,
            solveResult = solveResult
        };
        return true;
    }

    private LakeSolveResult CreateEmptyLakeSolveResult(LakeTerrainPatch terrainPatch, Vector3 center, float surfaceY)
    {
        float cellSize = voxelTerrain != null ? Mathf.Max(voxelTerrain.VoxelSizeMeters, 0.25f) : 0.25f;
        return new LakeSolveResult
        {
            surfaceY = surfaceY,
            volumeCubicMeters = 0f,
            cellSize = cellSize,
            cellCountPerAxis = 0,
            originXZ = Vector2.zero,
            cellHeights = Array.Empty<float>(),
            floodedCells = Array.Empty<bool>(),
            surfaceVertices = Array.Empty<Vector3>(),
            surfaceTriangles = Array.Empty<int>(),
            surfaceBounds = new Bounds(
                new Vector3(center.x, surfaceY, center.z),
                new Vector3(0f, Mathf.Max(0.05f, waterSurfaceThicknessMeters), 0f)),
            floodedCellCount = 0,
            touchesOpenBoundary = false
        };
    }

    private void ApplyLakeSolveResult(GeneratedLake lake, LakeSolveResult solveResult)
    {
        if (lake == null || solveResult == null)
        {
            return;
        }

        lake.surfaceY = solveResult.surfaceY;
        lake.storedVolumeCubicMeters = Mathf.Max(0f, solveResult.volumeCubicMeters);
        lake.gridCellSize = 0f;
        lake.gridCountPerAxis = 0;
        lake.gridOriginXZ = Vector2.zero;
        lake.cellHeights = Array.Empty<float>();
        lake.floodedCells = Array.Empty<bool>();
        lake.surfaceVertices = solveResult.surfaceVertices ?? Array.Empty<Vector3>();
        lake.surfaceTriangles = solveResult.surfaceTriangles ?? Array.Empty<int>();
        lake.surfaceBounds = solveResult.surfaceBounds;
        lake.floodedCellCount = Mathf.Max(0, solveResult.floodedCellCount);
        lake.shorelineRadii = Array.Empty<float>();
    }

    private static float ClampLakeStoredVolume(float targetVolumeCubicMeters, LakeSolveResult solveResult)
    {
        if (solveResult == null || solveResult.volumeCubicMeters <= MinimumRenderableLakeVolumeCubicMeters)
        {
            return 0f;
        }

        return Mathf.Min(
            Mathf.Max(0f, targetVolumeCubicMeters),
            Mathf.Max(0f, solveResult.volumeCubicMeters));
    }

    private float ComputeOverflowExpandedCaptureRadius(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        LakeTerrainPatch terrainPatch,
        LakeSolveResult solveResult)
    {
        float expansionStep = Mathf.Max(
            lakeDynamicExpansionMeters,
            terrain != null ? terrain.ChunkWorldSizeMeters * 0.5f : 4f,
            terrain != null ? terrain.VoxelSizeMeters * 4f : 1f);
        float expandedRadius = lake != null ? lake.captureRadius + expansionStep : expansionStep;
        if (lake != null && terrainPatch != null && terrainPatch.hasBounds)
        {
            expandedRadius = Mathf.Max(expandedRadius, ComputeBoundsCornerRadiusXZ(lake.center, terrainPatch.bounds) + expansionStep);
        }

        if (lake != null && solveResult != null)
        {
            expandedRadius = Mathf.Max(expandedRadius, ComputeBoundsCornerRadiusXZ(lake.center, solveResult.surfaceBounds) + expansionStep);
        }

        return expandedRadius;
    }

    private float GetMaxPossibleLakeCaptureRadius(ProceduralVoxelTerrain terrain, Vector3 center)
    {
        if (terrain == null)
        {
            return float.PositiveInfinity;
        }

        Bounds worldBounds = terrain.WorldBounds;
        Vector2 centerXZ = new Vector2(center.x, center.z);
        Vector2[] corners =
        {
            new Vector2(worldBounds.min.x, worldBounds.min.z),
            new Vector2(worldBounds.min.x, worldBounds.max.z),
            new Vector2(worldBounds.max.x, worldBounds.min.z),
            new Vector2(worldBounds.max.x, worldBounds.max.z)
        };

        float maxRadius = 0f;
        for (int i = 0; i < corners.Length; i++)
        {
            maxRadius = Mathf.Max(maxRadius, Vector2.Distance(centerXZ, corners[i]));
        }

        return maxRadius + Mathf.Max(terrain.VoxelSizeMeters, waterUpdatePaddingMeters);
    }

    private void ExpandLakeCaptureRadiusForChange(GeneratedLake lake, Bounds changedBounds)
    {
        if (lake == null)
        {
            return;
        }

        float requiredRadius = 0f;
        Vector2 lakeCenterXZ = new Vector2(lake.center.x, lake.center.z);
        Vector2[] boundsCorners =
        {
            new Vector2(changedBounds.min.x, changedBounds.min.z),
            new Vector2(changedBounds.max.x, changedBounds.min.z),
            new Vector2(changedBounds.min.x, changedBounds.max.z),
            new Vector2(changedBounds.max.x, changedBounds.max.z)
        };

        for (int i = 0; i < boundsCorners.Length; i++)
        {
            requiredRadius = Mathf.Max(requiredRadius, Vector2.Distance(lakeCenterXZ, boundsCorners[i]));
        }

        float expansionThreshold = lake.captureRadius - Mathf.Max(0.5f, waterUpdatePaddingMeters + 0.5f);
        if (requiredRadius < expansionThreshold)
        {
            return;
        }

        lake.captureRadius = Mathf.Max(lake.captureRadius, requiredRadius + Mathf.Max(lakeDynamicExpansionMeters, 2f));
    }

    private float ComputeLocalLakeRefreshCaptureRadius(ProceduralVoxelTerrain terrain, GeneratedLake lake, Bounds changedBounds)
    {
        if (terrain == null || lake == null)
        {
            return 0f;
        }

        float padding = Mathf.Max(
            terrain.ChunkWorldSizeMeters * 0.75f,
            Mathf.Max(
                lakeDynamicExpansionMeters * 2f,
                Mathf.Max(waterUpdatePaddingMeters * 2f, terrain.VoxelSizeMeters * 6f)));
        float localRadius = Mathf.Max(lake.radius + padding, ComputeBoundsCornerRadiusXZ(lake.center, changedBounds) + padding);
        if (HasLakeSurfaceGeometry(lake))
        {
            localRadius = Mathf.Max(localRadius, ComputeBoundsCornerRadiusXZ(lake.center, lake.surfaceBounds) + padding);
        }

        return Mathf.Clamp(localRadius, Mathf.Max(terrain.VoxelSizeMeters, lake.radius), Mathf.Max(lake.captureRadius, lake.radius));
    }

    private GeneratedLake CreateTemporaryLakeRefreshSeed(GeneratedLake sourceLake, float captureRadius)
    {
        if (sourceLake == null)
        {
            return null;
        }

        return new GeneratedLake
        {
            center = sourceLake.center,
            radius = sourceLake.radius,
            surfaceY = sourceLake.surfaceY,
            storedVolumeCubicMeters = sourceLake.storedVolumeCubicMeters,
            captureRadius = Mathf.Max(sourceLake.radius, captureRadius),
            terrainPatch = sourceLake.terrainPatch,
            surfaceBounds = sourceLake.surfaceBounds,
            floodedCellCount = sourceLake.floodedCellCount
        };
    }

    private bool TryBuildLakeTerrainPatch(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        out LakeTerrainPatch terrainPatch,
        Bounds? expansionBounds = null,
        float? captureRadiusOverride = null)
    {
        terrainPatch = null;
        float effectiveCaptureRadius = captureRadiusOverride.HasValue
            ? captureRadiusOverride.Value
            : (lake != null ? lake.captureRadius : 0f);
        if (terrain == null || lake == null || effectiveCaptureRadius <= 0.1f)
        {
            return false;
        }

        LakeTerrainPatch patch = new LakeTerrainPatch();
        Dictionary<LakeEdgeKey, List<LakePendingEdge>> edgeMap = new Dictionary<LakeEdgeKey, List<LakePendingEdge>>();
        Bounds captureBounds = BuildLakeCaptureBounds(terrain, lake, expansionBounds, effectiveCaptureRadius);
        Vector2 lakeCenterXZ = new Vector2(lake.center.x, lake.center.z);
        float inclusionRadius = effectiveCaptureRadius + Mathf.Max(terrain.VoxelSizeMeters * 1.5f, 1f);
        terrain.GetChunkCoordinateRange(captureBounds, out Vector3Int minChunkCoordinate, out Vector3Int maxChunkCoordinate);
        List<Vector3> vertexBuffer = new List<Vector3>(512);
        List<int> indexBuffer = new List<int>(768);
        for (int z = minChunkCoordinate.z; z <= maxChunkCoordinate.z; z++)
        {
            for (int y = minChunkCoordinate.y; y <= maxChunkCoordinate.y; y++)
            {
                for (int x = minChunkCoordinate.x; x <= maxChunkCoordinate.x; x++)
                {
                    Vector3Int chunkCoordinate = new Vector3Int(x, y, z);
                    if (!terrain.TryGetGeneratedChunk(chunkCoordinate, out ProceduralVoxelTerrainChunk chunk) ||
                        !terrain.GetChunkWorldBounds(chunkCoordinate).Intersects(captureBounds))
                    {
                        continue;
                    }

                    patch.chunkCoordinates.Add(chunkCoordinate);

                    MeshFilter meshFilter = chunk.GetComponent<MeshFilter>();
                    Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                    if (mesh == null || mesh.vertexCount == 0)
                    {
                        continue;
                    }

                    vertexBuffer.Clear();
                    mesh.GetVertices(vertexBuffer);
                    if (vertexBuffer.Count == 0)
                    {
                        continue;
                    }

                    Matrix4x4 localToWorld = chunk.transform.localToWorldMatrix;
                    for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                    {
                        indexBuffer.Clear();
                        mesh.GetTriangles(indexBuffer, subMeshIndex);
                        for (int i = 0; i + 2 < indexBuffer.Count; i += 3)
                        {
                            Vector3 worldA = localToWorld.MultiplyPoint3x4(vertexBuffer[indexBuffer[i]]);
                            Vector3 worldB = localToWorld.MultiplyPoint3x4(vertexBuffer[indexBuffer[i + 1]]);
                            Vector3 worldC = localToWorld.MultiplyPoint3x4(vertexBuffer[indexBuffer[i + 2]]);
                            Vector3 normal = Vector3.Cross(worldB - worldA, worldC - worldA);
                            float normalMagnitude = normal.magnitude;
                            if (normalMagnitude <= 0.0001f || (normal.y / normalMagnitude) < 0.12f)
                            {
                                continue;
                            }

                            if (DistancePointToTriangleXZ(lakeCenterXZ, worldA, worldB, worldC) > inclusionRadius)
                            {
                                continue;
                            }

                            int triangleIndex = patch.triangles.Count;
                            patch.triangles.Add(new LakeTerrainTriangle
                            {
                                a = worldA,
                                b = worldB,
                                c = worldC
                            });

                            patch.minHeight = Mathf.Min(patch.minHeight, Mathf.Min(worldA.y, Mathf.Min(worldB.y, worldC.y)));
                            patch.maxHeight = Mathf.Max(patch.maxHeight, Mathf.Max(worldA.y, Mathf.Max(worldB.y, worldC.y)));
                            Bounds triangleBounds = new Bounds(worldA, Vector3.zero);
                            triangleBounds.Encapsulate(worldB);
                            triangleBounds.Encapsulate(worldC);
                            if (!patch.hasBounds)
                            {
                                patch.bounds = triangleBounds;
                                patch.hasBounds = true;
                            }
                            else
                            {
                                patch.bounds.Encapsulate(triangleBounds.min);
                                patch.bounds.Encapsulate(triangleBounds.max);
                            }

                            AddLakeTerrainTriangleEdge(edgeMap, triangleIndex, worldA, worldB);
                            AddLakeTerrainTriangleEdge(edgeMap, triangleIndex, worldB, worldC);
                            AddLakeTerrainTriangleEdge(edgeMap, triangleIndex, worldC, worldA);
                        }
                    }
                }
            }
        }

        if (patch.triangles.Count == 0 || float.IsPositiveInfinity(patch.minHeight) || float.IsNegativeInfinity(patch.maxHeight))
        {
            return false;
        }

        foreach (KeyValuePair<LakeEdgeKey, List<LakePendingEdge>> edgeEntry in edgeMap)
        {
            List<LakePendingEdge> sharedEdges = edgeEntry.Value;
            if (sharedEdges == null || sharedEdges.Count == 0)
            {
                continue;
            }

            if (sharedEdges.Count == 1)
            {
                LakePendingEdge boundaryEdge = sharedEdges[0];
                patch.triangles[boundaryEdge.triangleIndex].boundaryEdges.Add(new LakeTriangleEdge(boundaryEdge.edgeA, boundaryEdge.edgeB));
                continue;
            }

            for (int i = 0; i < sharedEdges.Count; i++)
            {
                for (int j = i + 1; j < sharedEdges.Count; j++)
                {
                    LakePendingEdge first = sharedEdges[i];
                    LakePendingEdge second = sharedEdges[j];
                    patch.triangles[first.triangleIndex].neighbors.Add(new LakeTriangleNeighbor(second.triangleIndex, first.edgeA, first.edgeB));
                    patch.triangles[second.triangleIndex].neighbors.Add(new LakeTriangleNeighbor(first.triangleIndex, first.edgeA, first.edgeB));
                }
            }
        }

        terrainPatch = patch;
        return true;
    }

    private Bounds BuildLakeCaptureBounds(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        Bounds? expansionBounds = null,
        float? captureRadiusOverride = null)
    {
        float verticalPadding = Mathf.Max(terrain.VoxelSizeMeters * 2f, waterUpdatePaddingMeters);
        float captureRadius = captureRadiusOverride.HasValue ? captureRadiusOverride.Value : lake.captureRadius;
        float minY = lake.surfaceY - Mathf.Max(terrain.ChunkWorldSizeMeters, lakeDepthMeters * 2.5f);
        float maxY = lake.surfaceY + Mathf.Max(terrain.ChunkWorldSizeMeters, lakeDepthMeters * 2f);
        if (lake.terrainPatch != null && lake.terrainPatch.hasBounds)
        {
            minY = Mathf.Min(minY, lake.terrainPatch.bounds.min.y - verticalPadding);
            maxY = Mathf.Max(maxY, lake.terrainPatch.bounds.max.y + verticalPadding);
        }

        if (expansionBounds.HasValue)
        {
            Bounds changedBounds = expansionBounds.Value;
            minY = Mathf.Min(minY, changedBounds.min.y - verticalPadding);
            maxY = Mathf.Max(maxY, changedBounds.max.y + verticalPadding);
        }

        Bounds terrainBounds = terrain.WorldBounds;
        minY = Mathf.Clamp(minY, terrainBounds.min.y, terrainBounds.max.y);
        maxY = Mathf.Clamp(maxY, terrainBounds.min.y, terrainBounds.max.y);
        if (maxY <= minY + terrain.VoxelSizeMeters)
        {
            maxY = Mathf.Min(terrainBounds.max.y, minY + Mathf.Max(terrain.VoxelSizeMeters, terrain.ChunkWorldSizeMeters));
        }

        return new Bounds(
            new Vector3(lake.center.x, (minY + maxY) * 0.5f, lake.center.z),
            new Vector3(
                captureRadius * 2f,
                Mathf.Max(terrain.VoxelSizeMeters, maxY - minY),
                captureRadius * 2f));
    }

    private Bounds BuildLakeHorizontalBounds(ProceduralVoxelTerrain terrain, Vector3 center, float radius, float horizontalPadding)
    {
        return new Bounds(
            new Vector3(center.x, terrain.WorldBounds.center.y, center.z),
            new Vector3(
                Mathf.Max(terrain.VoxelSizeMeters, (radius + horizontalPadding) * 2f),
                terrain.WorldSize.y,
                Mathf.Max(terrain.VoxelSizeMeters, (radius + horizontalPadding) * 2f)));
    }

    private static float ComputeBoundsCornerRadiusXZ(Vector3 center, Bounds bounds)
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

    private bool TryBuildLakeSurfaceHorizontalBounds(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        float horizontalPadding,
        out Bounds bounds)
    {
        bounds = default;
        if (terrain == null || !HasLakeSurfaceGeometry(lake))
        {
            return false;
        }

        bounds = new Bounds(
            new Vector3(lake.surfaceBounds.center.x, terrain.WorldBounds.center.y, lake.surfaceBounds.center.z),
            new Vector3(
                Mathf.Max(terrain.VoxelSizeMeters, lake.surfaceBounds.size.x + (horizontalPadding * 2f)),
                terrain.WorldSize.y,
                Mathf.Max(terrain.VoxelSizeMeters, lake.surfaceBounds.size.z + (horizontalPadding * 2f))));
        return true;
    }

    private void AddLakeTerrainTriangleEdge(
        Dictionary<LakeEdgeKey, List<LakePendingEdge>> edgeMap,
        int triangleIndex,
        Vector3 edgeA,
        Vector3 edgeB)
    {
        if (edgeMap == null)
        {
            return;
        }

        LakeEdgeKey edgeKey = GetLakeEdgeKey(edgeA, edgeB);
        if (!edgeMap.TryGetValue(edgeKey, out List<LakePendingEdge> sharedEdges))
        {
            sharedEdges = new List<LakePendingEdge>(2);
            edgeMap[edgeKey] = sharedEdges;
        }

        sharedEdges.Add(new LakePendingEdge(triangleIndex, edgeA, edgeB));
    }

    private static LakeEdgeKey GetLakeEdgeKey(Vector3 a, Vector3 b)
    {
        return new LakeEdgeKey(GetLakeVertexKey(a), GetLakeVertexKey(b));
    }

    private static LakeVertexKey GetLakeVertexKey(Vector3 point)
    {
        return new LakeVertexKey(
            Mathf.RoundToInt(point.x * 1000f),
            Mathf.RoundToInt(point.y * 1000f),
            Mathf.RoundToInt(point.z * 1000f));
    }

    private static bool TryClipTriangleBelowSurface(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        float surfaceY,
        List<Vector3> clippedPolygon)
    {
        if (clippedPolygon == null)
        {
            return false;
        }

        clippedPolygon.Clear();
        ClipLakeTriangleEdge(c, a, surfaceY, clippedPolygon);
        ClipLakeTriangleEdge(a, b, surfaceY, clippedPolygon);
        ClipLakeTriangleEdge(b, c, surfaceY, clippedPolygon);
        return clippedPolygon.Count >= 3;
    }

    private static void ClipLakeTriangleEdge(Vector3 start, Vector3 end, float surfaceY, List<Vector3> clippedPolygon)
    {
        bool startInside = start.y < surfaceY - 0.0001f;
        bool endInside = end.y < surfaceY - 0.0001f;
        if (startInside && endInside)
        {
            clippedPolygon.Add(end);
            return;
        }

        if (startInside && !endInside)
        {
            clippedPolygon.Add(IntersectLakeEdgeAtSurface(start, end, surfaceY));
            return;
        }

        if (!startInside && endInside)
        {
            clippedPolygon.Add(IntersectLakeEdgeAtSurface(start, end, surfaceY));
            clippedPolygon.Add(end);
        }
    }

    private static Vector3 IntersectLakeEdgeAtSurface(Vector3 start, Vector3 end, float surfaceY)
    {
        float heightDelta = end.y - start.y;
        if (Mathf.Abs(heightDelta) <= 0.0001f)
        {
            return new Vector3(end.x, surfaceY, end.z);
        }

        float t = Mathf.Clamp01((surfaceY - start.y) / heightDelta);
        Vector3 point = Vector3.Lerp(start, end, t);
        point.y = surfaceY;
        return point;
    }

    private bool TryAppendClippedLakePolygon(
        float surfaceY,
        List<Vector3> clippedPolygon,
        List<Vector3> surfaceVertices,
        List<int> surfaceTriangles,
        ref float volumeCubicMeters,
        ref Bounds surfaceBounds,
        ref bool hasBounds)
    {
        if (clippedPolygon == null || clippedPolygon.Count < 3)
        {
            return false;
        }

        bool appended = false;
        for (int i = 1; i < clippedPolygon.Count - 1; i++)
        {
            Vector3 terrainA = clippedPolygon[0];
            Vector3 terrainB = clippedPolygon[i];
            Vector3 terrainC = clippedPolygon[i + 1];
            float projectedArea = TriangleAreaXZ(terrainA, terrainB, terrainC);
            if (projectedArea <= 0.0001f)
            {
                continue;
            }

            float depthA = Mathf.Max(0f, surfaceY - terrainA.y);
            float depthB = Mathf.Max(0f, surfaceY - terrainB.y);
            float depthC = Mathf.Max(0f, surfaceY - terrainC.y);
            volumeCubicMeters += projectedArea * ((depthA + depthB + depthC) / 3f);

            Vector3 topA = new Vector3(terrainA.x, surfaceY, terrainA.z);
            Vector3 topB = new Vector3(terrainB.x, surfaceY, terrainB.z);
            Vector3 topC = new Vector3(terrainC.x, surfaceY, terrainC.z);
            int vertexStart = surfaceVertices.Count;
            surfaceVertices.Add(topA);
            surfaceVertices.Add(topB);
            surfaceVertices.Add(topC);
            surfaceTriangles.Add(vertexStart);
            surfaceTriangles.Add(vertexStart + 1);
            surfaceTriangles.Add(vertexStart + 2);

            if (!hasBounds)
            {
                surfaceBounds = new Bounds(topA, Vector3.zero);
                hasBounds = true;
            }

            surfaceBounds.Encapsulate(topA);
            surfaceBounds.Encapsulate(topB);
            surfaceBounds.Encapsulate(topC);
            appended = true;
        }

        return appended;
    }

    private int FindLakeSeedTriangleIndex(LakeTerrainPatch terrainPatch, Vector3 lakeCenter, float surfaceY)
    {
        if (terrainPatch == null || terrainPatch.triangles.Count == 0)
        {
            return -1;
        }

        Vector2 lakeCenterXZ = new Vector2(lakeCenter.x, lakeCenter.z);
        List<Vector3> clippedPolygon = new List<Vector3>(4);
        float bestDistanceSquared = float.PositiveInfinity;
        int bestTriangleIndex = -1;
        for (int triangleIndex = 0; triangleIndex < terrainPatch.triangles.Count; triangleIndex++)
        {
            LakeTerrainTriangle triangle = terrainPatch.triangles[triangleIndex];
            if (!TryClipTriangleBelowSurface(triangle.a, triangle.b, triangle.c, surfaceY, clippedPolygon))
            {
                continue;
            }

            for (int i = 1; i < clippedPolygon.Count - 1; i++)
            {
                Vector2 closestPoint = ClosestPointOnTriangleXZ(lakeCenterXZ, clippedPolygon[0], clippedPolygon[i], clippedPolygon[i + 1]);
                float distanceSquared = (closestPoint - lakeCenterXZ).sqrMagnitude;
                if (distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                bestTriangleIndex = triangleIndex;
                if (distanceSquared <= 0.0001f)
                {
                    return triangleIndex;
                }
            }
        }

        return bestTriangleIndex;
    }

    private static float DistancePointToTriangleXZ(Vector2 point, Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector2.Distance(point, ClosestPointOnTriangleXZ(point, a, b, c));
    }

    private static Vector2 ClosestPointOnTriangleXZ(Vector2 point, Vector3 aWorld, Vector3 bWorld, Vector3 cWorld)
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

    private static float TriangleAreaXZ(Vector3 a, Vector3 b, Vector3 c)
    {
        return Mathf.Abs(((b.x - a.x) * (c.z - a.z)) - ((b.z - a.z) * (c.x - a.x))) * 0.5f;
    }

    private bool ContainsPointOnLakeSurface(GeneratedLake lake, float worldX, float worldZ, float paddingMeters)
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

    private bool TryResolveLakeNearPoint(Vector3 worldPoint, float pointPaddingMeters, out GeneratedLake lake)
    {
        lake = null;
        float bestScore = float.PositiveInfinity;
        float searchPaddingMeters = Mathf.Max(0f, pointPaddingMeters);

        for (int i = 0; i < generatedLakes.Count; i++)
        {
            GeneratedLake candidate = generatedLakes[i];
            if (!IsLakeActive(candidate) ||
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

    private bool TryGetClosestPointOnLakeSurface(GeneratedLake lake, Vector3 worldPoint, out Vector3 closestPoint, out float planarDistanceMeters)
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

    private void UpdateLakeInfluenceBounds(ProceduralVoxelTerrain terrain, GeneratedLake lake)
    {
        if (terrain == null || lake == null)
        {
            return;
        }

        float horizontalExtent = lake.captureRadius;
        Vector3 boundsCenter = new Vector3(lake.center.x, terrain.WorldBounds.center.y, lake.center.z);
        if (HasLakeSurfaceGeometry(lake))
        {
            horizontalExtent = Mathf.Max(
                horizontalExtent,
                Mathf.Max(lake.surfaceBounds.extents.x, lake.surfaceBounds.extents.z) + Mathf.Max(terrain.VoxelSizeMeters, 0.25f));
            boundsCenter.x = lake.surfaceBounds.center.x;
            boundsCenter.z = lake.surfaceBounds.center.z;
        }

        lake.influenceBounds = new Bounds(
            boundsCenter,
            new Vector3(
                Mathf.Max(terrain.VoxelSizeMeters, (horizontalExtent + waterUpdatePaddingMeters) * 2f),
                terrain.WorldSize.y,
                Mathf.Max(terrain.VoxelSizeMeters, (horizontalExtent + waterUpdatePaddingMeters) * 2f)));
    }

    private void UpdateRiverInfluenceBounds(ProceduralVoxelTerrain terrain, GeneratedRiver river)
    {
        if (terrain == null || river == null || river.waterPath.Count == 0)
        {
            return;
        }

        float horizontalPadding = waterUpdatePaddingMeters;
        for (int i = 0; i < river.widthProfile.Count; i++)
        {
            horizontalPadding = Mathf.Max(horizontalPadding, river.widthProfile[i] * 0.5f);
        }

        river.influenceBounds = BuildHorizontalInfluenceBounds(terrain, river.waterPath, horizontalPadding + waterUpdatePaddingMeters);
    }

    private Bounds BuildHorizontalInfluenceBounds(ProceduralVoxelTerrain terrain, IReadOnlyList<Vector3> points, float horizontalPadding)
    {
        Bounds bounds = new Bounds(
            new Vector3(points[0].x, terrain.WorldBounds.center.y, points[0].z),
            new Vector3(0.01f, terrain.WorldSize.y, 0.01f));
        for (int i = 1; i < points.Count; i++)
        {
            bounds.Encapsulate(new Vector3(points[i].x, terrain.WorldBounds.center.y, points[i].z));
        }

        bounds.Expand(new Vector3(horizontalPadding * 2f, 0f, horizontalPadding * 2f));
        return bounds;
    }

    private void GenerateLakes(System.Random random, ProceduralVoxelTerrain terrain, Bounds bounds)
    {
        if (lakeCount <= 0)
        {
            return;
        }

        int generatedCount = 0;
        int attempts = 0;
        int maxAttempts = lakeCount * 48;
        float minimumCandidateSurfaceY = seaLevelMeters + Mathf.Max(0.7f, lakeDepthMeters * 0.6f);
        float maximumCandidateSurfaceY = bounds.max.y - Mathf.Max(1.25f, lakeDepthMeters);
        TryResolveLakeCandidateHeightRange(terrain, bounds, out minimumCandidateSurfaceY, out maximumCandidateSurfaceY);

        int rejectedMissingSurface = 0;
        int rejectedElevation = 0;
        int rejectedSlope = 0;
        int rejectedSpacing = 0;
        int rejectedShoreline = 0;
        int rejectedRefine = 0;
        int rejectedProfile = 0;

        while (generatedCount < lakeCount && attempts < maxAttempts)
        {
            attempts++;
            float attemptProgress = maxAttempts <= 1 ? 1f : (attempts - 1f) / (maxAttempts - 1f);
            float candidateSurfaceFloor = Mathf.Lerp(
                minimumCandidateSurfaceY,
                seaLevelMeters + Mathf.Max(0.45f, lakeDepthMeters * 0.3f),
                attemptProgress * 0.35f);
            float candidateSurfaceCeiling = Mathf.Lerp(
                maximumCandidateSurfaceY,
                bounds.max.y - Mathf.Max(0.75f, lakeDepthMeters * 0.5f),
                attemptProgress);
            float maxAllowedSlopeDegrees = Mathf.Lerp(14f, 20f, attemptProgress);
            float normalizedX = NextFloat(random, 0.14f, 0.86f);
            float normalizedZ = NextFloat(random, 0.14f, 0.86f);
            if (!terrain.TrySampleSurface(normalizedX, normalizedZ, out RaycastHit hit))
            {
                rejectedMissingSurface++;
                continue;
            }

            if (hit.point.y <= candidateSurfaceFloor || hit.point.y >= candidateSurfaceCeiling)
            {
                rejectedElevation++;
                continue;
            }

            float slope = Vector3.Angle(hit.normal, Vector3.up);
            if (slope > maxAllowedSlopeDegrees)
            {
                rejectedSlope++;
                continue;
            }

            float radius = NextFloat(random, lakeRadiusRangeMeters.x, lakeRadiusRangeMeters.y);
            Vector3 center = hit.point;
            if (IsTooCloseToExistingWater(center, radius * 1.8f))
            {
                rejectedSpacing++;
                continue;
            }

            if (!TrySampleLakeShorelineStats(terrain, center, radius, out float minimumShoreHeight, out float averageShoreHeight))
            {
                rejectedShoreline++;
                continue;
            }

            float surfaceY = Mathf.Max(
                seaLevelMeters + 0.55f,
                Mathf.Min(minimumShoreHeight - 0.08f, averageShoreHeight - 0.05f));
            if (carveTerrainForWater)
            {
                CarveLake(terrain, center, radius, surfaceY, lakeDepthMeters);
            }

            if (!TryRefineLakeSurfaceY(terrain, center, radius, surfaceY, out surfaceY))
            {
                rejectedRefine++;
                continue;
            }

            if (!TryCreateLakeAtSurface(terrain, center, radius, surfaceY, out GeneratedLake generatedLake))
            {
                rejectedProfile++;
                continue;
            }

            generatedLakes.Add(generatedLake);
            generatedCount++;
        }

        if (generatedCount <= 0)
        {
            Debug.LogWarning(
                $"{gameObject.name} could not place any lakes. Candidate elevation window was {minimumCandidateSurfaceY:F1}m to {maximumCandidateSurfaceY:F1}m with {attempts} attempts. " +
                $"Rejections: missing-surface={rejectedMissingSurface}, elevation={rejectedElevation}, slope={rejectedSlope}, overlap={rejectedSpacing}, shoreline={rejectedShoreline}, refine={rejectedRefine}, profile={rejectedProfile}.");
        }
    }

    private bool TryResolveLakeCandidateHeightRange(ProceduralVoxelTerrain terrain, Bounds bounds, out float minimumSurfaceY, out float maximumSurfaceY)
    {
        minimumSurfaceY = seaLevelMeters + Mathf.Max(0.7f, lakeDepthMeters * 0.6f);
        maximumSurfaceY = bounds.max.y - Mathf.Max(1.25f, lakeDepthMeters);
        if (terrain == null)
        {
            return false;
        }

        const int sampleGridSize = 8;
        List<float> sampledHeights = new List<float>(sampleGridSize * sampleGridSize);
        for (int z = 0; z < sampleGridSize; z++)
        {
            float normalizedZ = Mathf.Lerp(0.14f, 0.86f, (z + 0.5f) / sampleGridSize);
            for (int x = 0; x < sampleGridSize; x++)
            {
                float normalizedX = Mathf.Lerp(0.14f, 0.86f, (x + 0.5f) / sampleGridSize);
                if (!terrain.TrySampleSurface(normalizedX, normalizedZ, out RaycastHit hit))
                {
                    continue;
                }

                sampledHeights.Add(hit.point.y);
            }
        }

        if (sampledHeights.Count == 0)
        {
            return false;
        }

        sampledHeights.Sort();
        float lowerPercentileHeight = GetSortedSampleValue(sampledHeights, 0.18f);
        float upperPercentileHeight = GetSortedSampleValue(sampledHeights, 0.78f);
        minimumSurfaceY = Mathf.Max(
            seaLevelMeters + Mathf.Max(0.7f, lakeDepthMeters * 0.6f),
            lowerPercentileHeight - terrain.VoxelSizeMeters);
        maximumSurfaceY = Mathf.Min(
            bounds.max.y - Mathf.Max(1.25f, lakeDepthMeters),
            upperPercentileHeight + (terrain.VoxelSizeMeters * 2f));

        float minimumRange = Mathf.Max(3f, lakeDepthMeters * 2f);
        if (maximumSurfaceY <= minimumSurfaceY + minimumRange)
        {
            maximumSurfaceY = Mathf.Min(
                bounds.max.y - Mathf.Max(1.25f, lakeDepthMeters),
                minimumSurfaceY + minimumRange);
        }

        return maximumSurfaceY > minimumSurfaceY;
    }

    private static float GetSortedSampleValue(List<float> sortedValues, float percentile)
    {
        if (sortedValues == null || sortedValues.Count == 0)
        {
            return 0f;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        float scaledIndex = Mathf.Clamp01(percentile) * (sortedValues.Count - 1);
        int lowerIndex = Mathf.FloorToInt(scaledIndex);
        int upperIndex = Mathf.Min(sortedValues.Count - 1, lowerIndex + 1);
        float t = scaledIndex - lowerIndex;
        return Mathf.Lerp(sortedValues[lowerIndex], sortedValues[upperIndex], t);
    }

    private void GenerateRivers(System.Random random, ProceduralVoxelTerrain terrain, Bounds bounds)
    {
        if (riverCount <= 0)
        {
            return;
        }

        int generatedCount = 0;
        int attempts = 0;
        int maxAttempts = riverCount * 20;
        while (generatedCount < riverCount && attempts < maxAttempts)
        {
            attempts++;
            if (!TryFindRiverSource(random, terrain, bounds, out Vector3 source))
            {
                continue;
            }

            Vector3 mouth = CreateRiverMouth(random, bounds);
            float width = NextFloat(random, riverWidthRangeMeters.x, riverWidthRangeMeters.y);
            List<Vector3> samples = BuildRiverPath(random, terrain, bounds, source, mouth, width);
            if (samples.Count < 4)
            {
                continue;
            }

            if (carveTerrainForWater)
            {
                CarveRiver(terrain, samples, width, riverDepthMeters);
            }

            List<Vector3> waterPath = BuildRiverWaterPath(terrain, samples, riverDepthMeters);
            if (waterPath.Count < 2)
            {
                continue;
            }

            List<float> widthProfile = BuildRiverWidthProfile(waterPath.Count, width);
            GeneratedRiver generatedRiver = BuildRenderableRiver(waterPath, widthProfile, width, terrain);
            if (generatedRiver == null)
            {
                continue;
            }

            generatedRivers.Add(generatedRiver);
            generatedCount++;
        }

        RebuildRiverSegmentCache();
    }

    private bool TryFindRiverSource(System.Random random, ProceduralVoxelTerrain terrain, Bounds bounds, out Vector3 source)
    {
        source = Vector3.zero;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            float normalizedX = NextFloat(random, 0.18f, 0.82f);
            float normalizedZ = NextFloat(random, 0.22f, 0.82f);
            if (!terrain.TrySampleSurface(normalizedX, normalizedZ, out RaycastHit hit))
            {
                continue;
            }

            float normalizedHeight = bounds.size.y <= 0.0001f ? 0f : Mathf.Clamp01((hit.point.y - bounds.min.y) / bounds.size.y);
            float slope = Vector3.Angle(hit.normal, Vector3.up);
            if (normalizedHeight < riverSourceHeightRangeNormalized.x || normalizedHeight > riverSourceHeightRangeNormalized.y)
            {
                continue;
            }

            if (slope < 2f || slope > 24f)
            {
                continue;
            }

            source = hit.point;
            return true;
        }

        return false;
    }

    private Vector3 CreateRiverMouth(System.Random random, Bounds bounds)
    {
        float edgeSelector = NextFloat(random, 0f, 4f);
        float x;
        float z;

        if (edgeSelector < 1f)
        {
            x = bounds.min.x;
            z = Mathf.Lerp(bounds.min.z, bounds.max.z, NextFloat(random, 0.12f, 0.88f));
        }
        else if (edgeSelector < 2f)
        {
            x = bounds.max.x;
            z = Mathf.Lerp(bounds.min.z, bounds.max.z, NextFloat(random, 0.12f, 0.88f));
        }
        else if (edgeSelector < 3f)
        {
            x = Mathf.Lerp(bounds.min.x, bounds.max.x, NextFloat(random, 0.12f, 0.88f));
            z = bounds.min.z;
        }
        else
        {
            x = Mathf.Lerp(bounds.min.x, bounds.max.x, NextFloat(random, 0.12f, 0.88f));
            z = bounds.max.z;
        }

        return new Vector3(x, seaLevelMeters, z);
    }

    private List<Vector3> BuildRiverPath(System.Random random, ProceduralVoxelTerrain terrain, Bounds bounds, Vector3 source, Vector3 mouth, float width)
    {
        List<Vector3> samples = new List<Vector3>(Mathf.Max(8, riverSampleCount))
        {
            source
        };

        float stepLength = Mathf.Max(riverCarveStepMeters, width * 0.82f);
        Vector3 current = source;
        Vector3 currentDirection = Vector3.ProjectOnPlane(mouth - source, Vector3.up).normalized;
        if (currentDirection.sqrMagnitude < 0.0001f)
        {
            currentDirection = Vector3.forward;
        }

        int maxSteps = Mathf.Max(8, riverSampleCount);
        for (int step = 1; step < maxSteps; step++)
        {
            float remainingDistance = Vector3.Distance(
                new Vector3(current.x, 0f, current.z),
                new Vector3(mouth.x, 0f, mouth.z));
            if (remainingDistance <= stepLength * 1.35f || current.y <= seaLevelMeters + 0.4f)
            {
                break;
            }

            if (!TryAdvanceRiverPoint(terrain, bounds, samples, current, mouth, currentDirection, stepLength, out Vector3 nextPoint))
            {
                break;
            }

            currentDirection = Vector3.ProjectOnPlane(nextPoint - current, Vector3.up).normalized;
            if (currentDirection.sqrMagnitude < 0.0001f)
            {
                currentDirection = Vector3.ProjectOnPlane(mouth - nextPoint, Vector3.up).normalized;
            }

            current = nextPoint;
            samples.Add(current);
        }

        float mouthX = Mathf.Clamp(mouth.x, bounds.min.x + 1f, bounds.max.x - 1f);
        float mouthZ = Mathf.Clamp(mouth.z, bounds.min.z + 1f, bounds.max.z - 1f);
        if (terrain.TrySampleSurfaceWorld(mouthX, mouthZ, out RaycastHit mouthHit) &&
            Vector3.Distance(samples[samples.Count - 1], mouthHit.point) > stepLength * 0.45f)
        {
            samples.Add(mouthHit.point);
        }

        return samples;
    }

    private void CarveLake(ProceduralVoxelTerrain terrain, Vector3 center, float radiusMeters, float surfaceY, float depthMeters)
    {
        Vector3 basinCenter = new Vector3(center.x, surfaceY - (depthMeters * 0.58f), center.z);
        terrain.ApplyDensityBrushWorld(basinCenter, radiusMeters * 0.62f, -depthMeters * 1.15f, false);
        for (int i = 0; i < 8; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / 8f;
            Vector3 innerOffset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * (radiusMeters * 0.34f);
            terrain.ApplyDensityBrushWorld(
                new Vector3(center.x + innerOffset.x, surfaceY - (depthMeters * 0.42f), center.z + innerOffset.z),
                radiusMeters * 0.34f,
                -depthMeters * 0.7f,
                false);
        }

        for (int i = 0; i < 12; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / 12f;
            Vector3 outerOffset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * (radiusMeters * 0.72f);
            terrain.ApplyDensityBrushWorld(
                new Vector3(center.x + outerOffset.x, surfaceY - (depthMeters * 0.12f), center.z + outerOffset.z),
                radiusMeters * 0.18f,
                -depthMeters * 0.26f,
                false);
        }

        for (int i = 0; i < 16; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / 16f;
            Vector3 shorelineOffset = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * (radiusMeters * 0.86f);
            terrain.ApplyDensityBrushWorld(
                new Vector3(center.x + shorelineOffset.x, surfaceY - 0.08f, center.z + shorelineOffset.z),
                radiusMeters * 0.12f,
                -depthMeters * 0.18f,
                false);
        }

        int sedimentMaterialIndex = ResolveFreshwaterBedMaterialIndex(terrain);
        if (sedimentMaterialIndex >= 0)
        {
            terrain.ApplyMaterialBrushWorld(
                new Vector3(center.x, surfaceY - (depthMeters * 0.35f), center.z),
                radiusMeters * 0.94f,
                depthMeters + 1.2f,
                sedimentMaterialIndex,
                false);
        }
    }

    private void BuildMergeTestRidge(
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

    private void CarveRiver(ProceduralVoxelTerrain terrain, IReadOnlyList<Vector3> samples, float widthMeters, float depthMeters)
    {
        if (samples == null || samples.Count == 0)
        {
            return;
        }

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

            int sedimentMaterialIndex = ResolveFreshwaterBedMaterialIndex(terrain);
            if (sedimentMaterialIndex >= 0)
            {
                terrain.ApplyMaterialBrushWorld(
                    samples[i] - new Vector3(0f, depthMeters * 0.28f, 0f),
                    width * 0.72f,
                    depthMeters + 1.1f,
                    sedimentMaterialIndex,
                    false);
            }
        }
    }

    private void CreateOceanObject(Bounds bounds, Transform generatedRoot)
    {
        CompositionInfo seaWaterComposition = ResolveComposition("Sea Water");
        GameObject ocean = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ocean.name = "Voxel Ocean";
        ocean.layer = gameObject.layer;
        ocean.transform.SetParent(generatedRoot, true);
        ocean.transform.position = new Vector3(
            bounds.center.x,
            seaLevelMeters - (waterSurfaceThicknessMeters * 0.5f),
            bounds.center.z);
        ocean.transform.rotation = Quaternion.identity;
        ocean.transform.localScale = new Vector3(
            bounds.size.x + (oceanPaddingMeters * 2f),
            waterSurfaceThicknessMeters,
            bounds.size.z + (oceanPaddingMeters * 2f));

        ConfigureWaterObject(ocean, ResolveSaltWaterMaterial(), seaWaterComposition);
        ConfigureInteractionCollider(ocean.GetComponent<Collider>());
        EnsureStaticWaterHarvestable(ocean, seaWaterComposition);
    }

    private void CreateLakeObject(GeneratedLake lake, Transform generatedRoot)
    {
        if (lake == null)
        {
            return;
        }

        lake.waterObject = EnsureFreshWaterObject(
            lake.waterObject,
            "Voxel Freshwater Lake",
            generatedRoot,
            ResolveFreshWaterComposition());
        RemoveStaticWaterHarvestable(lake.waterObject);
        DisableVisualWaterCollider(lake.waterObject);
        Mesh lakeMesh = BuildLakeMesh(lake, generatedRoot);
        if (!TryAssignWaterMesh(lake.waterObject, lakeMesh, lake.storedVolumeCubicMeters > MinimumRenderableLakeVolumeCubicMeters))
        {
            LogLakeDebug($"CreateLakeObject disabled {DescribeLake(lake)} because no renderable mesh was built.");
            return;
        }

        ConfigureLakeInteractionCollider(lake);
        LogLakeDebug($"CreateLakeObject updated {DescribeLake(lake)}. Mesh vertices={lakeMesh.vertexCount}, triangles={lake.surfaceTriangles.Length / 3}, active={lake.waterObject.activeSelf}.");
    }

    private void CreateRiverObject(GeneratedRiver river, Transform generatedRoot, int index)
    {
        if (river == null || river.points.Count < 2 || river.points.Count != river.widths.Count)
        {
            return;
        }

        river.waterObject = EnsureFreshWaterObject(
            river.waterObject,
            $"Voxel River {index + 1:000}",
            generatedRoot,
            ResolveFreshWaterComposition());
        RemoveStaticWaterHarvestable(river.waterObject);
        DisableVisualWaterCollider(river.waterObject);

        Mesh riverMesh = BuildRiverMesh(river, generatedRoot);
        if (!TryAssignWaterMesh(river.waterObject, riverMesh, true))
        {
            return;
        }

        ConfigureRiverInteractionCollider(river);
    }

    private void ConfigureLakeInteractionCollider(GeneratedLake lake)
    {
        if (lake == null || lake.waterObject == null)
        {
            return;
        }

        float colliderHeight = Mathf.Max(0.35f, waterSurfaceThicknessMeters + 0.45f);
        Bounds bounds = lake.surfaceBounds;
        if (bounds.size.x <= 0.001f || bounds.size.z <= 0.001f)
        {
            float maxRadius = GetMaxLakeRadius(lake);
            bounds = new Bounds(
                new Vector3(lake.center.x, lake.surfaceY, lake.center.z),
                new Vector3(maxRadius * 2f, waterSurfaceThicknessMeters, maxRadius * 2f));
        }

        float horizontalPadding = Mathf.Max(voxelTerrain != null ? voxelTerrain.VoxelSizeMeters : 0.5f, 0.5f);
        bounds.Expand(new Vector3(horizontalPadding, 0f, horizontalPadding));
        bounds.center = new Vector3(bounds.center.x, lake.surfaceY - (colliderHeight * 0.5f), bounds.center.z);
        bounds.size = new Vector3(
            Mathf.Max(0.5f, bounds.size.x),
            colliderHeight,
            Mathf.Max(0.5f, bounds.size.z));

        Transform interactionRoot = EnsureInteractionRoot(lake.waterObject);
        interactionRoot.localPosition = lake.waterObject.transform.InverseTransformPoint(bounds.center);

        BoxCollider collider = interactionRoot.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = interactionRoot.gameObject.AddComponent<BoxCollider>();
        }

        collider.center = Vector3.zero;
        collider.size = bounds.size;
        ConfigureInteractionCollider(collider);
    }

    private void ConfigureRiverInteractionCollider(GeneratedRiver river)
    {
        if (river == null || river.waterObject == null || river.waterPath.Count == 0)
        {
            return;
        }

        float colliderHeight = Mathf.Max(0.35f, waterSurfaceThicknessMeters + 0.45f);
        float averageY = 0f;
        for (int i = 0; i < river.waterPath.Count; i++)
        {
            averageY += river.waterPath[i].y;
        }

        averageY /= river.waterPath.Count;
        Bounds bounds = river.influenceBounds;
        bounds.center = new Vector3(bounds.center.x, averageY - (colliderHeight * 0.5f), bounds.center.z);
        bounds.size = new Vector3(
            Mathf.Max(0.5f, bounds.size.x),
            colliderHeight,
            Mathf.Max(0.5f, bounds.size.z));

        Transform interactionRoot = EnsureInteractionRoot(river.waterObject);
        interactionRoot.localPosition = river.waterObject.transform.InverseTransformPoint(bounds.center);

        BoxCollider collider = interactionRoot.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = interactionRoot.gameObject.AddComponent<BoxCollider>();
        }

        collider.center = Vector3.zero;
        collider.size = bounds.size;
        ConfigureInteractionCollider(collider);
    }

    private Transform EnsureInteractionRoot(GameObject waterObject)
    {
        Transform interactionRoot = waterObject.transform.Find(InteractionRootObjectName);
        if (interactionRoot != null)
        {
            interactionRoot.localRotation = Quaternion.identity;
            interactionRoot.localScale = Vector3.one;
            interactionRoot.gameObject.layer = gameObject.layer;
            return interactionRoot;
        }

        GameObject interactionObject = new GameObject(InteractionRootObjectName);
        interactionObject.layer = gameObject.layer;
        interactionObject.transform.SetParent(waterObject.transform, false);
        interactionObject.transform.localPosition = Vector3.zero;
        interactionObject.transform.localRotation = Quaternion.identity;
        interactionObject.transform.localScale = Vector3.one;
        return interactionObject.transform;
    }

    private void ConfigureInteractionCollider(Collider collider)
    {
        if (collider == null)
        {
            return;
        }

        collider.enabled = true;
        collider.isTrigger = useTriggerColliders;
    }

    private void DisableVisualWaterCollider(GameObject waterObject)
    {
        if (waterObject == null)
        {
            return;
        }

        Collider collider = waterObject.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyComponent(collider);
        }
    }

    private void EnsureStaticWaterHarvestable(GameObject waterObject, CompositionInfo composition)
    {
        if (waterObject == null || composition == null)
        {
            return;
        }

        HarvestableObject harvestable = waterObject.GetComponent<HarvestableObject>();
        if (harvestable == null)
        {
            harvestable = waterObject.AddComponent<HarvestableObject>();
        }

        harvestable.Configure(
            composition,
            DefaultHarvestedWaterMassGrams,
            1f,
            false,
            1,
            false);
    }

    private void RemoveStaticWaterHarvestable(GameObject waterObject)
    {
        if (waterObject == null)
        {
            return;
        }

        HarvestableObject harvestable = waterObject.GetComponent<HarvestableObject>();
        if (harvestable != null)
        {
            DestroyComponent(harvestable);
        }
    }

    private Mesh BuildLakeMesh(GeneratedLake lake, Transform generatedRoot)
    {
        if (lake == null ||
            generatedRoot == null ||
            lake.surfaceVertices == null ||
            lake.surfaceTriangles == null ||
            lake.surfaceTriangles.Length < 3)
        {
            return null;
        }

        List<Vector3> vertices = new List<Vector3>(lake.surfaceVertices.Length);
        List<int> triangles = new List<int>(lake.surfaceTriangles.Length);
        List<Vector2> uvs = new List<Vector2>(lake.surfaceVertices.Length);
        List<Vector3> normals = new List<Vector3>(lake.surfaceVertices.Length);
        float uvScale = Mathf.Max(lake.captureRadius * 2f, voxelTerrain != null ? voxelTerrain.VoxelSizeMeters : 1f);
        for (int i = 0; i < lake.surfaceVertices.Length; i++)
        {
            Vector3 vertex = lake.surfaceVertices[i];
            vertices.Add(generatedRoot.InverseTransformPoint(vertex));
            uvs.Add(new Vector2(vertex.x / uvScale, vertex.z / uvScale));
            normals.Add(Vector3.up);
        }

        if (vertices.Count == 0)
        {
            return null;
        }

        triangles.AddRange(lake.surfaceTriangles);

        Mesh mesh = new Mesh
        {
            name = "Generated Lake Mesh",
            indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh BuildRiverMesh(GeneratedRiver river, Transform generatedRoot)
    {
        if (river == null || generatedRoot == null || river.points.Count < 2 || river.points.Count != river.widths.Count)
        {
            return null;
        }

        int pointCount = river.points.Count;
        Vector3[] topLeft = new Vector3[pointCount];
        Vector3[] topRight = new Vector3[pointCount];
        Vector3[] bottomLeft = new Vector3[pointCount];
        Vector3[] bottomRight = new Vector3[pointCount];
        float[] pathDistances = new float[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 currentPoint = river.points[i];
            Vector3 previousPoint = river.points[Mathf.Max(0, i - 1)];
            Vector3 nextPoint = river.points[Mathf.Min(pointCount - 1, i + 1)];
            Vector3 tangent = Vector3.ProjectOnPlane(nextPoint - previousPoint, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = i > 0
                    ? Vector3.ProjectOnPlane(currentPoint - previousPoint, Vector3.up)
                    : Vector3.ProjectOnPlane(nextPoint - currentPoint, Vector3.up);
            }

            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.forward;
            }

            Vector3 right = Vector3.Cross(Vector3.up, tangent.normalized);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.right;
            }

            float halfWidth = Mathf.Max(0.2f, river.widths[i] * 0.5f);
            Vector3 leftWorld = currentPoint - (right * halfWidth);
            Vector3 rightWorld = currentPoint + (right * halfWidth);
            Vector3 down = Vector3.up * waterSurfaceThicknessMeters;

            topLeft[i] = generatedRoot.InverseTransformPoint(leftWorld);
            topRight[i] = generatedRoot.InverseTransformPoint(rightWorld);
            bottomLeft[i] = generatedRoot.InverseTransformPoint(leftWorld - down);
            bottomRight[i] = generatedRoot.InverseTransformPoint(rightWorld - down);

            if (i > 0)
            {
                pathDistances[i] = pathDistances[i - 1] + Vector3.Distance(
                    new Vector3(currentPoint.x, 0f, currentPoint.z),
                    new Vector3(river.points[i - 1].x, 0f, river.points[i - 1].z));
            }
        }

        List<Vector3> vertices = new List<Vector3>((pointCount - 1) * 16);
        List<int> triangles = new List<int>((pointCount - 1) * 24);
        List<Vector2> uvs = new List<Vector2>((pointCount - 1) * 16);
        for (int i = 0; i < pointCount - 1; i++)
        {
            float startDistance = pathDistances[i];
            float endDistance = pathDistances[i + 1];

            AddQuad(vertices, triangles, uvs,
                topLeft[i], topRight[i], topRight[i + 1], topLeft[i + 1],
                new Vector2(0f, startDistance), new Vector2(1f, startDistance), new Vector2(1f, endDistance), new Vector2(0f, endDistance),
                true);

            AddQuad(vertices, triangles, uvs,
                bottomRight[i], bottomLeft[i], bottomLeft[i + 1], bottomRight[i + 1],
                new Vector2(0f, startDistance), new Vector2(1f, startDistance), new Vector2(1f, endDistance), new Vector2(0f, endDistance),
                false);

            AddQuad(vertices, triangles, uvs,
                topLeft[i], topLeft[i + 1], bottomLeft[i + 1], bottomLeft[i],
                new Vector2(0f, startDistance), new Vector2(1f, startDistance), new Vector2(1f, endDistance), new Vector2(0f, endDistance),
                false);

            AddQuad(vertices, triangles, uvs,
                topRight[i + 1], topRight[i], bottomRight[i], bottomRight[i + 1],
                new Vector2(0f, startDistance), new Vector2(1f, startDistance), new Vector2(1f, endDistance), new Vector2(0f, endDistance),
                false);
        }

        AddQuad(vertices, triangles, uvs,
            topRight[0], topLeft[0], bottomLeft[0], bottomRight[0],
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            false);

        int lastIndex = pointCount - 1;
        AddQuad(vertices, triangles, uvs,
            topLeft[lastIndex], topRight[lastIndex], bottomRight[lastIndex], bottomLeft[lastIndex],
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            false);

        if (vertices.Count == 0)
        {
            return null;
        }

        Mesh mesh = new Mesh
        {
            name = "Generated River Mesh"
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD,
        bool doubleSided)
    {
        int vertexStart = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        uvs.Add(uvD);

        triangles.Add(vertexStart);
        triangles.Add(vertexStart + 1);
        triangles.Add(vertexStart + 2);
        triangles.Add(vertexStart);
        triangles.Add(vertexStart + 2);
        triangles.Add(vertexStart + 3);

        if (!doubleSided)
        {
            return;
        }

        triangles.Add(vertexStart + 2);
        triangles.Add(vertexStart + 1);
        triangles.Add(vertexStart);
        triangles.Add(vertexStart + 3);
        triangles.Add(vertexStart + 2);
        triangles.Add(vertexStart);
    }

    private void ConfigureWaterObject(GameObject waterObject, Material material, CompositionInfo composition)
    {
        if (waterObject == null)
        {
            return;
        }

        MeshRenderer renderer = waterObject.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private CompositionInfo ResolveFreshWaterComposition()
    {
        return ResolveComposition("Fresh Water");
    }

    private GameObject EnsureFreshWaterObject(
        GameObject waterObject,
        string objectName,
        Transform generatedRoot,
        CompositionInfo freshWaterComposition)
    {
        if (waterObject != null)
        {
            return waterObject;
        }

        waterObject = new GameObject(objectName);
        waterObject.layer = gameObject.layer;
        waterObject.transform.SetParent(generatedRoot, false);
        waterObject.transform.localPosition = Vector3.zero;
        waterObject.transform.localRotation = Quaternion.identity;
        waterObject.transform.localScale = Vector3.one;
        waterObject.AddComponent<MeshFilter>();
        waterObject.AddComponent<MeshRenderer>();
        ConfigureWaterObject(waterObject, ResolveFreshWaterMaterial(), freshWaterComposition);
        return waterObject;
    }

    private bool TryAssignWaterMesh(GameObject waterObject, Mesh mesh, bool activeState)
    {
        if (waterObject == null)
        {
            return false;
        }

        if (mesh == null)
        {
            waterObject.SetActive(false);
            return false;
        }

        ReplaceSharedMesh(waterObject.GetComponent<MeshFilter>(), mesh);
        waterObject.SetActive(activeState);
        return true;
    }

    private bool HarvestLake(GeneratedLake lake, Inventory playerInventory)
    {
        if (lake == null || playerInventory == null)
        {
            return false;
        }

        CompositionInfo freshWaterComposition = ResolveFreshWaterComposition();
        if (freshWaterComposition == null)
        {
            return false;
        }

        ProceduralVoxelTerrain terrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (terrain == null || !terrain.HasGeneratedTerrain)
        {
            return false;
        }

        float availableMass = ConvertVolumeToMassGrams(lake.storedVolumeCubicMeters);
        float harvestMass = Mathf.Min(freshwaterHarvestMassGrams, availableMass);
        if (harvestMass <= 0.001f)
        {
            LogLakeDebug($"Harvest skipped for {DescribeLake(lake)} because available harvest mass was {harvestMass:F3} g.");
            return false;
        }

        if (!playerInventory.AddItem(
                freshWaterComposition,
                1,
                harvestMass,
                CompositionInfo.CopyComposition(freshWaterComposition.composition)))
        {
            LogLakeDebug($"Harvest failed for {DescribeLake(lake)} because inventory rejected {harvestMass:F0} g.");
            return false;
        }

        float previousSurfaceY = lake.surfaceY;
        float previousStoredVolume = lake.storedVolumeCubicMeters;
        float targetVolume = Mathf.Max(0f, lake.storedVolumeCubicMeters - ConvertMassToVolumeCubicMeters(harvestMass));
        LogLakeDebug($"Harvesting {DescribeLake(lake)}. Starting surfaceY={previousSurfaceY:F3}, stored volume={previousStoredVolume:F3} m^3, harvest mass={harvestMass:F0} g, target volume={targetVolume:F3} m^3.");
        if (!TrySolveExistingLakeForStoredVolumeFast(terrain, lake, targetVolume, out LakeTerrainPatch terrainPatch, out LakeSolveResult solveResult))
        {
            LogLakeDebug($"Harvest solve failed for {DescribeLake(lake)} at target volume {targetVolume:F3} m^3.");
            return false;
        }

        lake.terrainPatch = terrainPatch;
        ApplyLakeSolveResult(lake, solveResult);
        lake.storedVolumeCubicMeters = targetVolume <= MinimumRenderableLakeVolumeCubicMeters
            ? 0f
            : ClampLakeStoredVolume(targetVolume, solveResult);
        if (lake.storedVolumeCubicMeters <= MinimumRenderableLakeVolumeCubicMeters)
        {
            if (lake.waterObject != null)
            {
                lake.waterObject.SetActive(false);
            }

            LogLakeDebug($"Harvest emptied {DescribeLake(lake)}. Previous surfaceY={previousSurfaceY:F3}, previous volume={previousStoredVolume:F3} m^3.");
            return true;
        }

        LogLakeDebug($"Harvest solve succeeded for {DescribeLake(lake)}. SurfaceY {previousSurfaceY:F3}->{lake.surfaceY:F3}, volume {previousStoredVolume:F3}->{lake.storedVolumeCubicMeters:F3} m^3, solved volume={solveResult.volumeCubicMeters:F3} m^3, triangles={lake.surfaceTriangles.Length / 3}.");
        UpdateLakeInfluenceBounds(terrain, lake);
        CreateLakeObject(lake, EnsureGeneratedRoot());
        return true;
    }

    private bool TryAddWaterToLake(GeneratedLake lake, float waterMassGrams, string sourceDescription)
    {
        if (lake == null || waterMassGrams <= 0.001f)
        {
            return false;
        }

        ProceduralVoxelTerrain terrain = ResolveVoxelTerrainAndMaybeGenerate(false);
        if (terrain == null || !terrain.HasGeneratedTerrain)
        {
            return false;
        }

        float previousSurfaceY = lake.surfaceY;
        float previousStoredVolume = lake.storedVolumeCubicMeters;
        float targetVolume = lake.storedVolumeCubicMeters + ConvertMassToVolumeCubicMeters(waterMassGrams);
        if (!TrySolveExistingLakeForStoredVolumeFast(terrain, lake, targetVolume, out LakeTerrainPatch terrainPatch, out LakeSolveResult solveResult))
        {
            LogLakeDebug($"Add water failed for {DescribeLake(lake)} from {sourceDescription} at target volume {targetVolume:F3} m^3.");
            return false;
        }

        float overflowTolerance = Mathf.Max(0.01f, terrain.VoxelSizeMeters * terrain.VoxelSizeMeters * 0.01f);
        if (targetVolume > solveResult.volumeCubicMeters + overflowTolerance)
        {
            LogLakeDebug(
                $"Add water to {DescribeLake(lake)} from {sourceDescription} exceeded the static basin capacity. " +
                $"Target volume={targetVolume:F3} m^3, retained single-basin volume={solveResult.volumeCubicMeters:F3} m^3.");
        }

        Transform generatedRoot = EnsureGeneratedRoot();
        lake.terrainPatch = terrainPatch;
        ApplyLakeSolveResult(lake, solveResult);
        lake.storedVolumeCubicMeters = ClampLakeStoredVolume(targetVolume, solveResult);
        LogLakeDebug($"Added {waterMassGrams:F0} g of water to {DescribeLake(lake)} from {sourceDescription}. SurfaceY {previousSurfaceY:F3}->{lake.surfaceY:F3}, volume {previousStoredVolume:F3}->{lake.storedVolumeCubicMeters:F3} m^3, solved volume={solveResult.volumeCubicMeters:F3} m^3, triangles={lake.surfaceTriangles.Length / 3}.");
        UpdateLakeInfluenceBounds(terrain, lake);
        CreateLakeObject(lake, generatedRoot);
        return true;
    }

    private float GetLakeSolveSampleStep()
    {
        return voxelTerrain != null ? Mathf.Max(voxelTerrain.VoxelSizeMeters, 0.25f) : 0.25f;
    }

    private bool TrySolveExistingLakeForStoredVolumeFast(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        float targetVolume,
        out LakeTerrainPatch terrainPatch,
        out LakeSolveResult solveResult)
    {
        terrainPatch = lake != null ? lake.terrainPatch : null;
        solveResult = null;
        if (terrain == null || lake == null)
        {
            return false;
        }

        if (terrainPatch == null && !TryBuildLakeTerrainPatch(terrain, lake, out terrainPatch))
        {
            return false;
        }

        if (terrainPatch == null)
        {
            return false;
        }

        if (targetVolume <= MinimumRenderableLakeVolumeCubicMeters)
        {
            solveResult = CreateEmptyLakeSolveResult(terrainPatch, lake.center, terrainPatch.minHeight);
            return true;
        }

        if (TrySolveLakeSurfaceForTargetVolumeFromSurfaceEstimate(terrainPatch, lake, targetVolume, out solveResult))
        {
            return true;
        }

        return TrySolveLakeSurfaceForTargetVolume(terrainPatch, lake, targetVolume, out solveResult);
    }

    private bool TrySolveLakeSurfaceForTargetVolumeFromSurfaceEstimate(
        LakeTerrainPatch terrainPatch,
        GeneratedLake lake,
        float targetVolume,
        out LakeSolveResult solveResult)
    {
        solveResult = null;
        if (terrainPatch == null ||
            lake == null ||
            terrainPatch.triangles.Count == 0 ||
            !HasLakeSurfaceGeometry(lake))
        {
            return false;
        }

        float currentSurfaceArea = ComputeLakeSurfaceAreaXZ(lake.surfaceVertices, lake.surfaceTriangles);
        if (currentSurfaceArea <= 0.0001f)
        {
            return false;
        }

        float sampleStep = GetLakeSolveSampleStep();
        float currentVolume = Mathf.Max(lake.storedVolumeCubicMeters, 0f);
        float volumeTolerance = Mathf.Max(0.01f, sampleStep * sampleStep * 0.01f);
        float minimumSurfaceY = terrainPatch.minHeight - sampleStep;
        float maximumSurfaceY = Mathf.Max(
            lake.surfaceY + sampleStep,
            terrainPatch.maxHeight + Mathf.Max(lakeDepthMeters, sampleStep * 2f));
        if (voxelTerrain != null)
        {
            maximumSurfaceY = Mathf.Min(
                maximumSurfaceY,
                voxelTerrain.WorldBounds.max.y - Mathf.Max(voxelTerrain.VoxelSizeMeters * 0.5f, 0.25f));
        }

        if (maximumSurfaceY <= minimumSurfaceY + 0.05f)
        {
            maximumSurfaceY = minimumSurfaceY + sampleStep;
        }

        float estimatedSurfaceY = Mathf.Clamp(
            lake.surfaceY + ((targetVolume - currentVolume) / currentSurfaceArea),
            minimumSurfaceY,
            maximumSurfaceY);
        if (!TryEvaluateLakeAtSurface(terrainPatch, lake.center, estimatedSurfaceY, out LakeSolveResult estimatedResult))
        {
            return false;
        }

        if (Mathf.Abs(estimatedResult.volumeCubicMeters - targetVolume) <= volumeTolerance)
        {
            solveResult = estimatedResult;
            return true;
        }

        float previousSurfaceY = lake.surfaceY;
        float previousVolume = currentVolume;
        float currentSurfaceY = estimatedSurfaceY;
        LakeSolveResult currentResult = estimatedResult;
        for (int iteration = 0; iteration < 3; iteration++)
        {
            float denominator = currentResult.volumeCubicMeters - previousVolume;
            if (Mathf.Abs(denominator) <= 0.0001f)
            {
                break;
            }

            float nextSurfaceY = currentSurfaceY + ((targetVolume - currentResult.volumeCubicMeters) * (currentSurfaceY - previousSurfaceY) / denominator);
            nextSurfaceY = Mathf.Clamp(nextSurfaceY, minimumSurfaceY, maximumSurfaceY);
            if (Mathf.Abs(nextSurfaceY - currentSurfaceY) <= 0.0001f)
            {
                break;
            }

            if (!TryEvaluateLakeAtSurface(terrainPatch, lake.center, nextSurfaceY, out LakeSolveResult nextResult))
            {
                return false;
            }

            if (Mathf.Abs(nextResult.volumeCubicMeters - targetVolume) <= volumeTolerance)
            {
                solveResult = nextResult;
                return true;
            }

            previousSurfaceY = currentSurfaceY;
            previousVolume = currentResult.volumeCubicMeters;
            currentSurfaceY = nextSurfaceY;
            currentResult = nextResult;
        }

        return false;
    }

    private static float ComputeLakeSurfaceAreaXZ(Vector3[] surfaceVertices, int[] surfaceTriangles)
    {
        if (surfaceVertices == null || surfaceTriangles == null || surfaceTriangles.Length < 3)
        {
            return 0f;
        }

        float area = 0f;
        for (int triangleOffset = 0; triangleOffset + 2 < surfaceTriangles.Length; triangleOffset += 3)
        {
            area += TriangleAreaXZ(
                surfaceVertices[surfaceTriangles[triangleOffset]],
                surfaceVertices[surfaceTriangles[triangleOffset + 1]],
                surfaceVertices[surfaceTriangles[triangleOffset + 2]]);
        }

        return area;
    }

    private bool TrySolveLakeForStoredVolume(
        ProceduralVoxelTerrain terrain,
        GeneratedLake lake,
        float targetVolume,
        out LakeTerrainPatch terrainPatch,
        out LakeSolveResult solveResult)
    {
        terrainPatch = lake != null ? lake.terrainPatch : null;
        solveResult = null;
        if (terrain == null || lake == null)
        {
            return false;
        }

        if (terrainPatch == null && !TryBuildLakeTerrainPatch(terrain, lake, out terrainPatch))
        {
            return false;
        }

        if (terrainPatch == null)
        {
            return false;
        }

        if (targetVolume <= MinimumRenderableLakeVolumeCubicMeters)
        {
            solveResult = CreateEmptyLakeSolveResult(terrainPatch, lake.center, terrainPatch.minHeight);
            return true;
        }

        return TrySolveLakeForTargetVolumeWithOverflowExpansion(
            terrain,
            lake,
            targetVolume,
            null,
            out terrainPatch,
            out solveResult);
    }

    private bool HarvestRiver(GeneratedRiver river, Inventory playerInventory)
    {
        if (river == null || playerInventory == null)
        {
            return false;
        }

        CompositionInfo freshWaterComposition = ResolveFreshWaterComposition();
        if (freshWaterComposition == null)
        {
            return false;
        }

        float harvestMass = freshwaterHarvestMassGrams;
        return playerInventory.AddItem(
            freshWaterComposition,
            1,
            harvestMass,
            CompositionInfo.CopyComposition(freshWaterComposition.composition));
    }

    private string GetFreshWaterDisplayName()
    {
        CompositionInfo freshWaterComposition = ResolveFreshWaterComposition();
        return freshWaterComposition != null ? freshWaterComposition.itemName : "Fresh Water";
    }

    private string GetLakeHarvestPreview(GeneratedLake lake)
    {
        if (lake == null)
        {
            return "Nothing to harvest";
        }

        float volumeDisplay = lake.storedVolumeCubicMeters > 0f ? lake.storedVolumeCubicMeters : 0f;
        float harvestMass = Mathf.Min(freshwaterHarvestMassGrams, ConvertVolumeToMassGrams(volumeDisplay));
        return GetFreshWaterHarvestPreview(harvestMass, volumeDisplay);
    }

    private string GetFreshWaterHarvestPreview(float harvestMassGrams, float volumeCubicMeters)
    {
        CompositionInfo freshWaterComposition = ResolveFreshWaterComposition();
        string name = freshWaterComposition != null ? freshWaterComposition.itemName : "Fresh Water";
        if (volumeCubicMeters > 0f)
        {
            return $"{name}\nYield: {harvestMassGrams:F0} g\nVolume: {volumeCubicMeters:F1} m³";
        }

        return $"{name}\nYield: {harvestMassGrams:F0} g";
    }

    private static float ConvertMassToVolumeCubicMeters(float massGrams)
    {
        return Mathf.Max(0f, massGrams) / WaterDensityGramsPerCubicMeter;
    }

    private static float ConvertVolumeToMassGrams(float volumeCubicMeters)
    {
        return Mathf.Max(0f, volumeCubicMeters) * WaterDensityGramsPerCubicMeter;
    }

    private static bool HasLakeSurfaceGeometry(GeneratedLake lake)
    {
        return lake != null &&
               lake.surfaceVertices != null &&
               lake.surfaceTriangles != null &&
               lake.surfaceTriangles.Length >= 3;
    }

    private float GetMaxLakeRadius(GeneratedLake lake)
    {
        if (lake == null)
        {
            return 0f;
        }

        float maxRadius = Mathf.Max(0f, lake.radius);
        if (HasLakeSurfaceGeometry(lake))
        {
            maxRadius = Mathf.Max(maxRadius, Mathf.Max(lake.surfaceBounds.extents.x, lake.surfaceBounds.extents.z));
        }

        return Mathf.Max(maxRadius, lake.captureRadius * 0.5f);
    }

    private bool IsLakeActive(GeneratedLake lake)
    {
        return lake != null &&
               lake.storedVolumeCubicMeters > MinimumRenderableLakeVolumeCubicMeters &&
               HasLakeSurfaceGeometry(lake) &&
               lake.floodedCellCount > 0;
    }

    private int CountActiveLakes()
    {
        int activeLakeCount = 0;
        for (int i = 0; i < generatedLakes.Count; i++)
        {
            if (IsLakeActive(generatedLakes[i]))
            {
                activeLakeCount++;
            }
        }

        return activeLakeCount;
    }

    private string FormatLakeDebugSummary(GeneratedLake lake, int lakeIndex, Vector3? probePoint)
    {
        if (lake == null)
        {
            return lakeIndex >= 0 ? $"[{lakeIndex}] Lake<null>" : "Lake<null>";
        }

        string prefix = lakeIndex >= 0 ? $"[{lakeIndex}] " : string.Empty;
        string probeSummary = string.Empty;
        if (probePoint.HasValue && TryGetClosestPointOnLakeSurface(lake, probePoint.Value, out _, out float planarDistanceMeters))
        {
            probeSummary = $" probeDistanceXZ={planarDistanceMeters:F2}m";
        }

        return $"{prefix}{DescribeLake(lake)}, volume={lake.storedVolumeCubicMeters:F3} m^3, flooded={lake.floodedCellCount}, tris={lake.surfaceTriangles.Length / 3}, bounds=({lake.surfaceBounds.size.x:F1} x {lake.surfaceBounds.size.z:F1}){probeSummary}";
    }

    private string DescribeLake(GeneratedLake lake)
    {
        if (lake == null)
        {
            return "Lake<null>";
        }

        return $"Lake(center=({lake.center.x:F1}, {lake.center.z:F1}), surfaceY={lake.surfaceY:F2}, captureRadius={lake.captureRadius:F2})";
    }

    private void LogLakeDebug(string message)
    {
        if (!logLakeDebug)
        {
            return;
        }

        Debug.Log($"[{nameof(ProceduralVoxelTerrainWaterSystem)}:{name}] {message}", this);
    }

    private void DestroyComponent(Component component)
    {
        if (component == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(component);
        }
        else
        {
            DestroyImmediate(component);
        }
    }

    private void DestroyLakeObject(GeneratedLake lake)
    {
        if (lake == null || lake.waterObject == null)
        {
            return;
        }

        GameObject waterObject = lake.waterObject;
        lake.waterObject = null;
        if (Application.isPlaying)
        {
            Destroy(waterObject);
        }
        else
        {
            DestroyImmediate(waterObject);
        }
    }

    private void ReplaceSharedMesh(MeshFilter meshFilter, Mesh mesh)
    {
        if (meshFilter == null)
        {
            return;
        }

        Mesh previousMesh = meshFilter.sharedMesh;
        meshFilter.sharedMesh = mesh;
        if (previousMesh == null || previousMesh == mesh)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(previousMesh);
        }
        else
        {
            DestroyImmediate(previousMesh);
        }
    }

    private CompositionInfo ResolveComposition(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        CompositionInfoRegistry.TryGetByItemName(itemName, out CompositionInfo compositionInfo);
        return compositionInfo;
    }

    private Material ResolveFreshWaterMaterial()
    {
        return ResolveWaterMaterial(freshWaterMaterial, ref cachedFreshWaterMaterial, freshWaterColor, "Voxel Fresh Water Auto Material");
    }

    private Material ResolveSaltWaterMaterial()
    {
        return ResolveWaterMaterial(saltWaterMaterial, ref cachedSaltWaterMaterial, saltWaterColor, "Voxel Salt Water Auto Material");
    }

    private bool IsTooCloseToExistingWater(Vector3 point, float minimumDistance)
    {
        for (int i = 0; i < generatedLakes.Count; i++)
        {
            if (Vector3.Distance(point, generatedLakes[i].center) <= generatedLakes[i].radius + minimumDistance)
            {
                return true;
            }
        }

        for (int i = 0; i < generatedRiverSegments.Count; i++)
        {
            if (DistanceToSegmentXZ(point, generatedRiverSegments[i].start, generatedRiverSegments[i].end) <= (generatedRiverSegments[i].width * 0.5f) + minimumDistance)
            {
                return true;
            }
        }

        return false;
    }

    private static float DistanceToSegmentXZ(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector2 pointXZ = new Vector2(point.x, point.z);
        Vector2 startXZ = new Vector2(start.x, start.z);
        Vector2 endXZ = new Vector2(end.x, end.z);
        Vector2 projection = ClosestPointOnSegmentXZ(pointXZ, startXZ, endXZ, out _);
        return Vector2.Distance(pointXZ, projection);
    }

    private static float DistanceToSegmentXZ(Vector3 point, Vector3 start, Vector3 end, out float t)
    {
        Vector2 pointXZ = new Vector2(point.x, point.z);
        Vector2 startXZ = new Vector2(start.x, start.z);
        Vector2 endXZ = new Vector2(end.x, end.z);
        Vector2 projection = ClosestPointOnSegmentXZ(pointXZ, startXZ, endXZ, out t);
        return Vector2.Distance(pointXZ, projection);
    }

    private static Vector2 ClosestPointOnSegmentXZ(Vector2 pointXZ, Vector2 startXZ, Vector2 endXZ, out float t)
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

    private bool TrySampleLakeShorelineStats(ProceduralVoxelTerrain terrain, Vector3 center, float radiusMeters, out float minimumHeight, out float averageHeight)
    {
        minimumHeight = float.PositiveInfinity;
        averageHeight = 0f;
        int sampleCount = 12;
        int validSamples = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            float angleRadians = (Mathf.PI * 2f * i) / sampleCount;
            Vector3 shorelinePoint = center + new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians)) * (radiusMeters * 0.92f);
            if (!terrain.TrySampleSurfaceWorld(shorelinePoint.x, shorelinePoint.z, out RaycastHit hit))
            {
                return false;
            }

            minimumHeight = Mathf.Min(minimumHeight, hit.point.y);
            averageHeight += hit.point.y;
            validSamples++;
        }

        if (validSamples <= 0)
        {
            return false;
        }

        averageHeight /= validSamples;
        return true;
    }

    private bool TryRefineLakeSurfaceY(ProceduralVoxelTerrain terrain, Vector3 center, float radiusMeters, float proposedSurfaceY, out float refinedSurfaceY)
    {
        refinedSurfaceY = proposedSurfaceY;
        if (!TrySampleLakeShorelineStats(terrain, center, radiusMeters, out float minimumShoreHeight, out _) ||
            !terrain.TrySampleSurfaceWorld(center.x, center.z, out RaycastHit centerHit))
        {
            return false;
        }

        float upperBound = minimumShoreHeight - 0.06f;
        float lowerBound = Mathf.Max(
            seaLevelMeters + 0.55f,
            centerHit.point.y + Mathf.Max(0.24f, lakeDepthMeters * 0.52f));
        if (upperBound <= lowerBound)
        {
            return false;
        }

        refinedSurfaceY = Mathf.Clamp(proposedSurfaceY, lowerBound, upperBound);
        return true;
    }

    private bool TryAdvanceRiverPoint(
        ProceduralVoxelTerrain terrain,
        Bounds bounds,
        IReadOnlyList<Vector3> currentPath,
        Vector3 current,
        Vector3 mouth,
        Vector3 currentDirection,
        float stepLength,
        out Vector3 nextPoint)
    {
        nextPoint = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        float[] candidateAngles = { 0f, -25f, 25f, -50f, 50f, -80f, 80f };
        Vector3 toMouth = Vector3.ProjectOnPlane(mouth - current, Vector3.up).normalized;
        if (toMouth.sqrMagnitude < 0.0001f)
        {
            toMouth = currentDirection.sqrMagnitude > 0.0001f ? currentDirection : Vector3.forward;
        }

        for (int i = 0; i < candidateAngles.Length; i++)
        {
            Vector3 baseDirection = currentDirection.sqrMagnitude > 0.0001f ? currentDirection : toMouth;
            Vector3 candidateDirection = Quaternion.AngleAxis(candidateAngles[i], Vector3.up) * baseDirection;
            candidateDirection = Vector3.ProjectOnPlane((candidateDirection * 0.65f) + (toMouth * 0.35f), Vector3.up).normalized;
            if (candidateDirection.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Vector3 candidatePoint = current + (candidateDirection * stepLength);
            if (candidatePoint.x <= bounds.min.x + 0.5f ||
                candidatePoint.x >= bounds.max.x - 0.5f ||
                candidatePoint.z <= bounds.min.z + 0.5f ||
                candidatePoint.z >= bounds.max.z - 0.5f)
            {
                continue;
            }

            if (!terrain.TrySampleSurfaceWorld(candidatePoint.x, candidatePoint.z, out RaycastHit hit))
            {
                continue;
            }

            if (IsTooCloseToRiverPath(hit.point, currentPath, stepLength * 0.55f))
            {
                continue;
            }

            float remainingDistance = Vector3.Distance(
                new Vector3(current.x, 0f, current.z),
                new Vector3(mouth.x, 0f, mouth.z));
            float candidateRemainingDistance = Vector3.Distance(
                new Vector3(hit.point.x, 0f, hit.point.z),
                new Vector3(mouth.x, 0f, mouth.z));
            float progress = remainingDistance - candidateRemainingDistance;
            float heightDrop = current.y - hit.point.y;
            float uphillPenalty = Mathf.Max(0f, hit.point.y - current.y);
            float slopePenalty = Mathf.Max(0f, Vector3.Angle(hit.normal, Vector3.up) - 35f) * 0.03f;
            float directionality = Vector3.Dot(candidateDirection, toMouth);
            float score = (heightDrop * 2.8f) + (progress * 0.55f) + (directionality * 1.1f) - (uphillPenalty * 4.5f) - slopePenalty;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            nextPoint = hit.point;
        }

        return bestScore > float.NegativeInfinity;
    }

    private static bool IsTooCloseToRiverPath(Vector3 point, IReadOnlyList<Vector3> currentPath, float minimumSpacingMeters)
    {
        if (currentPath == null || currentPath.Count <= 1 || minimumSpacingMeters <= 0f)
        {
            return false;
        }

        float minimumSpacingSquared = minimumSpacingMeters * minimumSpacingMeters;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Vector3 planarDelta = Vector3.ProjectOnPlane(point - currentPath[i], Vector3.up);
            if (planarDelta.sqrMagnitude < minimumSpacingSquared)
            {
                return true;
            }
        }

        return false;
    }

    private List<Vector3> BuildRiverWaterPath(ProceduralVoxelTerrain terrain, IReadOnlyList<Vector3> carvedPath, float depthMeters)
    {
        List<Vector3> waterPath = new List<Vector3>(carvedPath.Count);
        if (carvedPath == null || carvedPath.Count == 0)
        {
            return waterPath;
        }

        for (int i = 0; i < carvedPath.Count; i++)
        {
            if (!terrain.TrySampleSurfaceWorld(carvedPath[i].x, carvedPath[i].z, out RaycastHit hit))
            {
                continue;
            }

            float waterY = hit.point.y + Mathf.Max(0.16f, depthMeters * 0.72f);
            if (waterPath.Count > 0)
            {
                Vector3 previousPoint = waterPath[waterPath.Count - 1];
                float stepDistance = Vector3.Distance(
                    new Vector3(previousPoint.x, 0f, previousPoint.z),
                    new Vector3(hit.point.x, 0f, hit.point.z));
                float maxAllowedY = previousPoint.y - Mathf.Max(0.012f, stepDistance * 0.006f);
                waterY = Mathf.Min(waterY, maxAllowedY);
            }

            waterY = Mathf.Max(seaLevelMeters + 0.04f, waterY);
            waterPath.Add(new Vector3(hit.point.x, waterY, hit.point.z));
        }

        return waterPath;
    }

    private static List<float> BuildRiverWidthProfile(int sampleCount, float baseWidth)
    {
        List<float> widths = new List<float>(Mathf.Max(0, sampleCount));
        if (sampleCount <= 0)
        {
            return widths;
        }

        for (int i = 0; i < sampleCount; i++)
        {
            float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
            widths.Add(Mathf.Lerp(baseWidth * 0.88f, baseWidth * 1.08f, t));
        }

        return widths;
    }

    private GeneratedRiver BuildRenderableRiver(IReadOnlyList<Vector3> waterPath, IReadOnlyList<float> widths, float baseWidth, ProceduralVoxelTerrain terrain)
    {
        if (waterPath == null || widths == null || waterPath.Count < 2 || waterPath.Count != widths.Count)
        {
            return null;
        }

        GeneratedRiver river = new GeneratedRiver
        {
            baseWidth = baseWidth
        };
        river.waterPath.AddRange(waterPath);
        river.widthProfile.AddRange(widths);
        PopulateRenderableRiverPath(river);
        UpdateRiverInfluenceBounds(terrain, river);
        return river.points.Count >= 2 ? river : null;
    }

    private static void PopulateRenderableRiverPath(GeneratedRiver river)
    {
        if (river == null)
        {
            return;
        }

        river.points.Clear();
        river.widths.Clear();
        if (river.waterPath.Count < 2 || river.widthProfile.Count != river.waterPath.Count)
        {
            return;
        }

        for (int i = 0; i < river.waterPath.Count - 1; i++)
        {
            for (int subdivision = 0; subdivision < RiverRenderSubdivisionsPerSegment; subdivision++)
            {
                float t = subdivision / (float)RiverRenderSubdivisionsPerSegment;
                river.points.Add(Vector3.Lerp(river.waterPath[i], river.waterPath[i + 1], t));
                river.widths.Add(Mathf.Lerp(river.widthProfile[i], river.widthProfile[i + 1], t));
            }
        }

        river.points.Add(river.waterPath[river.waterPath.Count - 1]);
        river.widths.Add(river.widthProfile[river.widthProfile.Count - 1]);
    }

    private void RebuildRiverSegmentCache()
    {
        generatedRiverSegments.Clear();
        for (int riverIndex = 0; riverIndex < generatedRivers.Count; riverIndex++)
        {
            GeneratedRiver river = generatedRivers[riverIndex];
            if (river == null || river.waterPath.Count < 2 || river.widthProfile.Count != river.waterPath.Count)
            {
                continue;
            }

            for (int i = 1; i < river.waterPath.Count; i++)
            {
                generatedRiverSegments.Add(new GeneratedRiverSegment
                {
                    start = river.waterPath[i - 1],
                    end = river.waterPath[i],
                    width = Mathf.Lerp(river.widthProfile[i - 1], river.widthProfile[i], 0.5f),
                    surfaceY = Mathf.Lerp(river.waterPath[i - 1].y, river.waterPath[i].y, 0.5f)
                });
            }
        }
    }

    private static int ResolveFreshwaterBedMaterialIndex(ProceduralVoxelTerrain terrain)
    {
        if (terrain == null)
        {
            return -1;
        }

        int index = terrain.FindMaterialIndex(FreshwaterBedMaterialName);
        if (index >= 0)
        {
            return index;
        }

        return terrain.FindMaterialIndex(FreshwaterBedFallbackCompositionName);
    }

    private static Vector3 EvaluateCubicBezier(Vector3 start, Vector3 controlOne, Vector3 controlTwo, Vector3 end, float t)
    {
        float oneMinusT = 1f - t;
        return (oneMinusT * oneMinusT * oneMinusT * start)
            + (3f * oneMinusT * oneMinusT * t * controlOne)
            + (3f * oneMinusT * t * t * controlTwo)
            + (t * t * t * end);
    }

    private static Material ResolveWaterMaterial(Material assignedMaterial, ref Material cachedMaterial, Color color, string materialName)
    {
        if (ProceduralRenderMaterialUtility.CanUseAssignedMaterial(assignedMaterial))
        {
            if (cachedMaterial == null || cachedMaterial.shader != assignedMaterial.shader)
            {
                cachedMaterial = new Material(assignedMaterial)
                {
                    name = materialName,
                    hideFlags = Application.isPlaying ? HideFlags.HideAndDontSave : HideFlags.None
                };
            }

            ApplyWaterMaterialAppearance(cachedMaterial, color);
            return cachedMaterial;
        }

        if (cachedMaterial == null)
        {
            cachedMaterial = ProceduralRenderMaterialUtility.CreateTransparentMaterial(
                materialName,
                color,
                0.9f,
                0f);
        }
        else
        {
            ApplyWaterMaterialAppearance(cachedMaterial, color);
        }

        return cachedMaterial;
    }

    private static void ApplyWaterMaterialAppearance(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.9f);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0f);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        }

        if (material.HasProperty("_CullMode"))
        {
            material.SetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Off);
        }

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    public bool TryGetHarvestable(RaycastHit hit, out IHarvestable harvestable)
    {
        harvestable = null;
        if (hit.collider == null)
        {
            return false;
        }

        if (TryResolveLakeFromHit(hit, out GeneratedLake lake))
        {
            harvestable = new FreshWaterHarvestTarget(this, lake);
            return true;
        }

        if (TryResolveRiverFromHit(hit, out GeneratedRiver river))
        {
            harvestable = new FreshWaterHarvestTarget(this, river);
            return true;
        }

        return false;
    }

    private bool TryResolveLakeFromHit(RaycastHit hit, out GeneratedLake lake)
    {
        lake = null;
        if (hit.collider == null)
        {
            return false;
        }

        Transform hitTransform = hit.collider.transform;
        for (int i = 0; i < generatedLakes.Count; i++)
        {
            GeneratedLake candidate = generatedLakes[i];
            if (!IsLakeActive(candidate) || candidate.waterObject == null || !hitTransform.IsChildOf(candidate.waterObject.transform))
            {
                continue;
            }

            if (!ContainsPointOnLakeSurface(candidate, hit.point.x, hit.point.z, 0.05f))
            {
                continue;
            }

            lake = candidate;
            return true;
        }

        return false;
    }

    private bool TryResolveRiverFromHit(RaycastHit hit, out GeneratedRiver river)
    {
        river = null;
        if (hit.collider == null)
        {
            return false;
        }

        Transform hitTransform = hit.collider.transform;
        for (int i = 0; i < generatedRivers.Count; i++)
        {
            GeneratedRiver candidate = generatedRivers[i];
            if (candidate?.waterObject == null || !hitTransform.IsChildOf(candidate.waterObject.transform))
            {
                continue;
            }

            river = candidate;
            return true;
        }

        return false;
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
}
