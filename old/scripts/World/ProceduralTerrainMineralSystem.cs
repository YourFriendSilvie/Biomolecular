using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TerrainMineralPrototype
{
    public string displayName = "Mineral Deposit";
    public string compositionItemName = "Stone (Silicate-Rich)";
    public CompositionInfo compositionOverride;
    public Color gizmoColor = new Color(0.55f, 0.48f, 0.4f, 1f);
    [Min(1)] public int depositCount = 12;
    [Min(1)] public int maxPlacementAttemptsPerDeposit = 16;
    public Vector2 radiusRangeMeters = new Vector2(2.5f, 5.5f);
    public Vector2 totalMassRangeGrams = new Vector2(4000f, 18000f);
    public Vector2 harvestMassRangeGrams = new Vector2(250f, 650f);
    public Vector2 normalizedHeightRange = new Vector2(0.05f, 0.95f);
    public Vector2 slopeDegreesRange = new Vector2(0f, 40f);
    [Min(0.5f)] public float densityNoiseScale = 48f;
    [Range(0f, 1f)] public float densityThreshold = 0.55f;
    [Min(0f)] public float minimumSpacingMeters = 12f;
    public bool requireDryGround = true;
    [Min(0f)] public float waterExclusionPaddingMeters = 1f;

    public void Sanitize()
    {
        displayName = string.IsNullOrWhiteSpace(displayName) ? "Mineral Deposit" : displayName.Trim();
        depositCount = Mathf.Max(1, depositCount);
        maxPlacementAttemptsPerDeposit = Mathf.Max(1, maxPlacementAttemptsPerDeposit);
        radiusRangeMeters = new Vector2(
            Mathf.Max(0.25f, Mathf.Min(radiusRangeMeters.x, radiusRangeMeters.y)),
            Mathf.Max(0.25f, Mathf.Max(radiusRangeMeters.x, radiusRangeMeters.y)));
        totalMassRangeGrams = new Vector2(
            Mathf.Max(0.1f, Mathf.Min(totalMassRangeGrams.x, totalMassRangeGrams.y)),
            Mathf.Max(0.1f, Mathf.Max(totalMassRangeGrams.x, totalMassRangeGrams.y)));
        harvestMassRangeGrams = new Vector2(
            Mathf.Max(0.1f, Mathf.Min(harvestMassRangeGrams.x, harvestMassRangeGrams.y)),
            Mathf.Max(0.1f, Mathf.Max(harvestMassRangeGrams.x, harvestMassRangeGrams.y)));
        normalizedHeightRange = new Vector2(
            Mathf.Clamp01(Mathf.Min(normalizedHeightRange.x, normalizedHeightRange.y)),
            Mathf.Clamp01(Mathf.Max(normalizedHeightRange.x, normalizedHeightRange.y)));
        slopeDegreesRange = new Vector2(
            Mathf.Clamp(Mathf.Min(slopeDegreesRange.x, slopeDegreesRange.y), 0f, 90f),
            Mathf.Clamp(Mathf.Max(slopeDegreesRange.x, slopeDegreesRange.y), 0f, 90f));
        densityNoiseScale = Mathf.Max(0.5f, densityNoiseScale);
        densityThreshold = Mathf.Clamp01(densityThreshold);
        minimumSpacingMeters = Mathf.Max(0f, minimumSpacingMeters);
        waterExclusionPaddingMeters = Mathf.Max(0f, waterExclusionPaddingMeters);
    }

    public string ResolveDisplayName()
    {
        return string.IsNullOrWhiteSpace(displayName) ? "Mineral Deposit" : displayName.Trim();
    }

    public CompositionInfo ResolveComposition()
    {
        if (compositionOverride != null)
        {
            return compositionOverride;
        }

        if (!string.IsNullOrWhiteSpace(compositionItemName) &&
            CompositionInfoRegistry.TryGetByItemName(compositionItemName, out CompositionInfo resolvedComposition))
        {
            return resolvedComposition;
        }

        return null;
    }
}

[Serializable]
public class GeneratedTerrainMineralDeposit
{
    public string displayName;
    public CompositionInfo composition;
    public Color gizmoColor = Color.white;
    public Vector3 worldCenter;
    public float radiusMeters;
    public float remainingMassGrams;
    public float initialMassGrams;
    public float minimumHarvestMassGrams;
    public float maximumHarvestMassGrams;

    public bool IsDepleted => remainingMassGrams <= 0.01f || composition == null;
}

public class ProceduralTerrainMineralSystem : MonoBehaviour, IRaycastHarvestableProvider
{
    private sealed class TerrainMineralHarvestTarget : IHarvestable
    {
        private readonly ProceduralTerrainMineralSystem owner;
        private readonly int depositIndex;
        private readonly Vector3 hitPoint;

        public TerrainMineralHarvestTarget(ProceduralTerrainMineralSystem owner, int depositIndex, Vector3 hitPoint)
        {
            this.owner = owner;
            this.depositIndex = depositIndex;
            this.hitPoint = hitPoint;
        }

        public bool Harvest(Inventory playerInventory)
        {
            return owner != null && owner.HarvestDeposit(depositIndex, hitPoint, playerInventory);
        }

        public string GetHarvestDisplayName()
        {
            return owner != null ? owner.GetDepositDisplayName(depositIndex) : "Mineral Deposit";
        }

        public string GetHarvestPreview()
        {
            return owner != null ? owner.GetDepositPreview(depositIndex) : "Nothing to harvest";
        }
    }

    [Header("Terrain")]
    [SerializeField] private ProceduralTerrainGenerator terrainGenerator;
    [SerializeField] private Terrain targetTerrain;
    [SerializeField] private bool generateTerrainBeforeMinerals = true;

    [Header("Water Exclusion")]
    [SerializeField] private ProceduralTerrainWaterSystem waterSystem;
    [SerializeField] private bool generateWaterBeforeMinerals = true;

    [Header("Generation")]
    [SerializeField] private int seed = 97531;
    [SerializeField] private bool randomizeSeed = false;
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private bool generateOnStart = false;

    [Header("Mineral Deposits")]
    [SerializeField] private List<TerrainMineralPrototype> prototypes = new List<TerrainMineralPrototype>();

    [Header("Debug")]
    [SerializeField] private bool drawDepositGizmos = true;
    [SerializeField, Min(0f)] private float gizmoVerticalOffsetMeters = 0.2f;

    [SerializeField, HideInInspector] private List<GeneratedTerrainMineralDeposit> generatedDeposits = new List<GeneratedTerrainMineralDeposit>();

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;

    private void Reset()
    {
        ApplyOlympicRainforestPreset();
    }

    private void Start()
    {
        if (Application.isPlaying && generateOnStart && (generatedDeposits == null || generatedDeposits.Count == 0))
        {
            GenerateMinerals(clearExistingBeforeGenerate);
        }
    }

    private void OnValidate()
    {
        prototypes ??= new List<TerrainMineralPrototype>();
        generatedDeposits ??= new List<GeneratedTerrainMineralDeposit>();

        if (prototypes.Count == 0)
        {
            ApplyOlympicRainforestPreset();
            return;
        }

        foreach (TerrainMineralPrototype prototype in prototypes)
        {
            prototype?.Sanitize();
        }

        gizmoVerticalOffsetMeters = Mathf.Max(0f, gizmoVerticalOffsetMeters);
    }

    [ContextMenu("Apply Olympic Rainforest Mineral Preset")]
    public void ApplyOlympicRainforestPreset()
    {
        prototypes = BuildOlympicRainforestPrototypes();
    }

    [ContextMenu("Generate Minerals")]
    public void GenerateMineralsFromContextMenu()
    {
        GenerateMinerals(clearExistingBeforeGenerate);
    }

    [ContextMenu("Clear Minerals")]
    public void ClearGeneratedMineralsFromContextMenu()
    {
        ClearGeneratedMinerals();
    }

    public bool GenerateMinerals(bool clearExisting)
    {
        Terrain terrain = ResolveTerrain(true);
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning($"{gameObject.name} could not generate terrain minerals because no terrain is available.");
            return false;
        }

        if (randomizeSeed)
        {
            seed = Environment.TickCount;
        }

        ProceduralTerrainWaterSystem resolvedWaterSystem = ResolveWaterSystemAndMaybeGenerate();
        if (clearExisting)
        {
            ClearGeneratedMinerals();
        }

        generatedDeposits ??= new List<GeneratedTerrainMineralDeposit>();
        System.Random random = new System.Random(seed);
        TerrainData terrainData = terrain.terrainData;

        foreach (TerrainMineralPrototype prototype in prototypes)
        {
            if (prototype == null)
            {
                continue;
            }

            prototype.Sanitize();
            CompositionInfo composition = prototype.ResolveComposition();
            if (composition == null)
            {
                Debug.LogWarning($"{gameObject.name} could not resolve a CompositionInfo for mineral prototype {prototype.ResolveDisplayName()}.");
                continue;
            }

            GeneratePrototypeDeposits(random, terrain, terrainData, resolvedWaterSystem, prototype, composition);
        }

        return generatedDeposits.Count > 0;
    }

    public bool ClearGeneratedMinerals()
    {
        if (generatedDeposits == null || generatedDeposits.Count == 0)
        {
            return false;
        }

        generatedDeposits.Clear();
        return true;
    }

    public bool TryGetHarvestable(RaycastHit hit, out IHarvestable harvestable)
    {
        harvestable = null;
        if (hit.collider == null)
        {
            return false;
        }

        Terrain terrain = ResolveTerrain(false);
        if (terrain == null || terrain.terrainData == null)
        {
            return false;
        }

        TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (terrainCollider == null || hit.collider != terrainCollider)
        {
            return false;
        }

        int depositIndex = FindBestDepositIndex(hit.point);
        if (depositIndex < 0)
        {
            return false;
        }

        harvestable = new TerrainMineralHarvestTarget(this, depositIndex, hit.point);
        return true;
    }

    private bool HarvestDeposit(int depositIndex, Vector3 hitPoint, Inventory playerInventory)
    {
        if (playerInventory == null || !TryGetDeposit(depositIndex, out GeneratedTerrainMineralDeposit deposit) || deposit.IsDepleted)
        {
            return false;
        }

        float harvestMass = CalculateHarvestMass(deposit, hitPoint);
        if (harvestMass <= 0f)
        {
            return false;
        }

        List<Composition> harvestedComposition = deposit.composition.GenerateRandomComposition();
        if (!playerInventory.AddItem(deposit.composition, 1, harvestMass, harvestedComposition))
        {
            return false;
        }

        deposit.remainingMassGrams = Mathf.Max(0f, deposit.remainingMassGrams - harvestMass);
        Debug.Log($"Harvested {harvestMass:F1} grams of {deposit.composition.itemName} from terrain deposit {deposit.displayName}.");
        return true;
    }

    private string GetDepositDisplayName(int depositIndex)
    {
        return TryGetDeposit(depositIndex, out GeneratedTerrainMineralDeposit deposit) && !string.IsNullOrWhiteSpace(deposit.displayName)
            ? deposit.displayName
            : "Mineral Deposit";
    }

    private string GetDepositPreview(int depositIndex)
    {
        if (!TryGetDeposit(depositIndex, out GeneratedTerrainMineralDeposit deposit) || deposit.composition == null)
        {
            return "Nothing to harvest";
        }

        return $"{deposit.displayName}\nMaterial: {deposit.composition.itemName}\nRemaining mass: {deposit.remainingMassGrams:F0} g";
    }

    private bool TryGetDeposit(int depositIndex, out GeneratedTerrainMineralDeposit deposit)
    {
        deposit = null;
        if (generatedDeposits == null || depositIndex < 0 || depositIndex >= generatedDeposits.Count)
        {
            return false;
        }

        deposit = generatedDeposits[depositIndex];
        return deposit != null;
    }

    private int FindBestDepositIndex(Vector3 worldPoint)
    {
        if (generatedDeposits == null || generatedDeposits.Count == 0)
        {
            return -1;
        }

        int bestIndex = -1;
        float bestScore = float.MaxValue;
        Vector2 pointXZ = new Vector2(worldPoint.x, worldPoint.z);

        for (int i = 0; i < generatedDeposits.Count; i++)
        {
            GeneratedTerrainMineralDeposit deposit = generatedDeposits[i];
            if (deposit == null || deposit.IsDepleted || deposit.radiusMeters <= 0f)
            {
                continue;
            }

            float distance = Vector2.Distance(pointXZ, new Vector2(deposit.worldCenter.x, deposit.worldCenter.z));
            if (distance > deposit.radiusMeters)
            {
                continue;
            }

            float normalizedDistance = distance / Mathf.Max(0.001f, deposit.radiusMeters);
            if (normalizedDistance < bestScore)
            {
                bestScore = normalizedDistance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private float CalculateHarvestMass(GeneratedTerrainMineralDeposit deposit, Vector3 hitPoint)
    {
        float distance = Vector2.Distance(
            new Vector2(hitPoint.x, hitPoint.z),
            new Vector2(deposit.worldCenter.x, deposit.worldCenter.z));
        float proximity = 1f - Mathf.Clamp01(distance / Mathf.Max(0.001f, deposit.radiusMeters));
        float richness = Mathf.SmoothStep(0.2f, 1f, proximity);
        float requestedMass = Mathf.Lerp(deposit.minimumHarvestMassGrams, deposit.maximumHarvestMassGrams, richness);
        return Mathf.Min(requestedMass, deposit.remainingMassGrams);
    }

    private void GeneratePrototypeDeposits(
        System.Random random,
        Terrain terrain,
        TerrainData terrainData,
        ProceduralTerrainWaterSystem resolvedWaterSystem,
        TerrainMineralPrototype prototype,
        CompositionInfo composition)
    {
        List<Vector3> placedPositions = new List<Vector3>(prototype.depositCount);
        int placedCount = 0;
        int attempts = 0;
        int maxAttempts = prototype.depositCount * prototype.maxPlacementAttemptsPerDeposit;
        float densityOffsetX = NextFloat(random, -10000f, 10000f);
        float densityOffsetZ = NextFloat(random, -10000f, 10000f);

        while (placedCount < prototype.depositCount && attempts < maxAttempts)
        {
            attempts++;

            float normalizedX = NextFloat(random, 0f, 1f);
            float normalizedZ = NextFloat(random, 0f, 1f);
            float localHeight = terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
            float normalizedHeight = terrainData.size.y <= 0.0001f
                ? 0f
                : Mathf.Clamp01(localHeight / terrainData.size.y);
            if (normalizedHeight < prototype.normalizedHeightRange.x || normalizedHeight > prototype.normalizedHeightRange.y)
            {
                continue;
            }

            float slope = terrainData.GetSteepness(normalizedX, normalizedZ);
            if (slope < prototype.slopeDegreesRange.x || slope > prototype.slopeDegreesRange.y)
            {
                continue;
            }

            Vector3 terrainPosition = terrain.transform.position;
            float worldX = terrainPosition.x + (normalizedX * terrainData.size.x);
            float worldZ = terrainPosition.z + (normalizedZ * terrainData.size.z);
            float density = Mathf.PerlinNoise(
                (worldX + densityOffsetX) / prototype.densityNoiseScale,
                (worldZ + densityOffsetZ) / prototype.densityNoiseScale);
            if (density < prototype.densityThreshold)
            {
                continue;
            }

            Vector3 surfacePoint = new Vector3(worldX, terrainPosition.y + localHeight, worldZ);
            if (prototype.requireDryGround &&
                resolvedWaterSystem != null &&
                resolvedWaterSystem.IsPointUnderWater(surfacePoint, prototype.waterExclusionPaddingMeters))
            {
                continue;
            }

            float radius = NextFloat(random, prototype.radiusRangeMeters.x, prototype.radiusRangeMeters.y);
            if (!IsFarEnoughFromExisting(surfacePoint, placedPositions, Mathf.Max(prototype.minimumSpacingMeters, radius)))
            {
                continue;
            }

            generatedDeposits.Add(new GeneratedTerrainMineralDeposit
            {
                displayName = prototype.ResolveDisplayName(),
                composition = composition,
                gizmoColor = prototype.gizmoColor,
                worldCenter = surfacePoint,
                radiusMeters = radius,
                initialMassGrams = NextFloat(random, prototype.totalMassRangeGrams.x, prototype.totalMassRangeGrams.y),
                remainingMassGrams = 0f,
                minimumHarvestMassGrams = prototype.harvestMassRangeGrams.x,
                maximumHarvestMassGrams = prototype.harvestMassRangeGrams.y
            });

            GeneratedTerrainMineralDeposit createdDeposit = generatedDeposits[generatedDeposits.Count - 1];
            createdDeposit.remainingMassGrams = createdDeposit.initialMassGrams;
            placedPositions.Add(surfacePoint);
            placedCount++;
        }

        if (placedCount < prototype.depositCount)
        {
            Debug.LogWarning($"{gameObject.name} only placed {placedCount} of {prototype.depositCount} requested terrain deposits for {prototype.ResolveDisplayName()}.");
        }
    }

    private Terrain ResolveTerrain(bool generateIfNeeded)
    {
        if (terrainGenerator == null)
        {
            terrainGenerator = GetComponent<ProceduralTerrainGenerator>();
        }

        if (terrainGenerator != null)
        {
            targetTerrain = generateIfNeeded && generateTerrainBeforeMinerals
                ? terrainGenerator.GenerateTerrain()
                : terrainGenerator.GetGeneratedTerrain();
        }

        if (targetTerrain == null)
        {
            targetTerrain = GetComponentInChildren<Terrain>();
        }

        return targetTerrain;
    }

    private ProceduralTerrainWaterSystem ResolveWaterSystemAndMaybeGenerate()
    {
        if (waterSystem == null)
        {
            waterSystem = GetComponent<ProceduralTerrainWaterSystem>();
        }

        if (waterSystem != null && generateWaterBeforeMinerals)
        {
            waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
        }

        return waterSystem;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDepositGizmos || generatedDeposits == null)
        {
            return;
        }

        for (int i = 0; i < generatedDeposits.Count; i++)
        {
            GeneratedTerrainMineralDeposit deposit = generatedDeposits[i];
            if (deposit == null || deposit.radiusMeters <= 0f)
            {
                continue;
            }

            float remainingRatio = deposit.initialMassGrams <= 0.0001f
                ? 0f
                : Mathf.Clamp01(deposit.remainingMassGrams / deposit.initialMassGrams);
            Color depositColor = Color.Lerp(deposit.gizmoColor * 0.35f, deposit.gizmoColor, remainingRatio);
            depositColor.a = 1f;
            Gizmos.color = depositColor;

            Vector3 center = deposit.worldCenter + (Vector3.up * gizmoVerticalOffsetMeters);
            Gizmos.DrawWireSphere(center, deposit.radiusMeters);
            Gizmos.DrawSphere(center, Mathf.Min(0.35f, deposit.radiusMeters * 0.08f));
        }
    }

    private static bool IsFarEnoughFromExisting(Vector3 position, IReadOnlyList<Vector3> existingPositions, float minimumSpacingMeters)
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

    private static List<TerrainMineralPrototype> BuildOlympicRainforestPrototypes()
    {
        return new List<TerrainMineralPrototype>
        {
            new TerrainMineralPrototype
            {
                displayName = "Silicate Outcrop",
                compositionItemName = "Stone (Silicate-Rich)",
                gizmoColor = new Color(0.58f, 0.6f, 0.63f),
                depositCount = 22,
                radiusRangeMeters = new Vector2(2.5f, 5.8f),
                totalMassRangeGrams = new Vector2(5000f, 22000f),
                harvestMassRangeGrams = new Vector2(260f, 720f),
                normalizedHeightRange = new Vector2(0.16f, 0.98f),
                slopeDegreesRange = new Vector2(16f, 52f),
                densityNoiseScale = 56f,
                densityThreshold = 0.52f,
                minimumSpacingMeters = 14f
            },
            new TerrainMineralPrototype
            {
                displayName = "Iron-Rich Seam",
                compositionItemName = "Iron-Rich Stone",
                gizmoColor = new Color(0.6f, 0.3f, 0.22f),
                depositCount = 8,
                radiusRangeMeters = new Vector2(1.8f, 4.2f),
                totalMassRangeGrams = new Vector2(3200f, 14000f),
                harvestMassRangeGrams = new Vector2(180f, 420f),
                normalizedHeightRange = new Vector2(0.24f, 0.96f),
                slopeDegreesRange = new Vector2(14f, 50f),
                densityNoiseScale = 74f,
                densityThreshold = 0.74f,
                minimumSpacingMeters = 22f
            },
            new TerrainMineralPrototype
            {
                displayName = "Clay-Rich Bank",
                compositionItemName = "Clay Deposit",
                gizmoColor = new Color(0.55f, 0.36f, 0.22f),
                depositCount = 12,
                radiusRangeMeters = new Vector2(3.4f, 7.6f),
                totalMassRangeGrams = new Vector2(7000f, 26000f),
                harvestMassRangeGrams = new Vector2(320f, 860f),
                normalizedHeightRange = new Vector2(0.02f, 0.22f),
                slopeDegreesRange = new Vector2(0f, 10f),
                densityNoiseScale = 36f,
                densityThreshold = 0.58f,
                minimumSpacingMeters = 18f
            }
        };
    }
}
