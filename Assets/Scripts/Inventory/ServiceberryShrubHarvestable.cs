using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class ServiceberryShrubHarvestable : MonoBehaviour, IHarvestable
{
    private enum ServiceberryPhenologyStage
    {
        Dormant,
        LeafedOut,
        Blooming,
        Fruiting
    }

    private readonly struct RipenessDistribution
    {
        public readonly float underripeWeight;
        public readonly float ripeWeight;
        public readonly float overripeWeight;

        public RipenessDistribution(float underripeWeight, float ripeWeight, float overripeWeight)
        {
            float clampedUnderripe = Mathf.Max(0f, underripeWeight);
            float clampedRipe = Mathf.Max(0f, ripeWeight);
            float clampedOverripe = Mathf.Max(0f, overripeWeight);
            float total = clampedUnderripe + clampedRipe + clampedOverripe;

            if (total <= 0f)
            {
                this.underripeWeight = 0f;
                this.ripeWeight = 0f;
                this.overripeWeight = 0f;
                return;
            }

            this.underripeWeight = clampedUnderripe / total;
            this.ripeWeight = clampedRipe / total;
            this.overripeWeight = clampedOverripe / total;
        }
    }

    private readonly struct SeasonalHarvestState
    {
        public readonly ServiceberryPhenologyStage stage;
        public readonly float availableFoliageMassGrams;
        public readonly float availableFruitMassGrams;
        public readonly float blossomFractionOfFoliage;
        public readonly RipenessDistribution ripenessDistribution;

        public SeasonalHarvestState(
            ServiceberryPhenologyStage stage,
            float availableFoliageMassGrams,
            float availableFruitMassGrams,
            float blossomFractionOfFoliage,
            RipenessDistribution ripenessDistribution)
        {
            this.stage = stage;
            this.availableFoliageMassGrams = Mathf.Max(0f, availableFoliageMassGrams);
            this.availableFruitMassGrams = Mathf.Max(0f, availableFruitMassGrams);
            this.blossomFractionOfFoliage = Mathf.Clamp01(blossomFractionOfFoliage);
            this.ripenessDistribution = ripenessDistribution;
        }
    }

    private const string DefaultDisplayName = "Serviceberry Shrub";
    private const string DefaultWoodyCompositionName = "Woody Matter (Hardwood)";
    private const string DefaultFoliageCompositionName = "Serviceberry Foliage";
    private const string DefaultBlossomCompositionName = "Serviceberry Blossoms";
    private const string DefaultUnderripeFruitCompositionName = "Serviceberry Fruit (Underripe)";
    private const string DefaultRipeFruitCompositionName = "Serviceberry Fruit (Ripe)";
    private const string DefaultOverripeFruitCompositionName = "Serviceberry Fruit (Overripe)";

    [Header("Display")]
    [SerializeField] private string shrubDisplayName = DefaultDisplayName;

    [Header("Composition Assets")]
    [SerializeField] private CompositionInfo woodyComposition;
    [SerializeField] private CompositionInfo foliageComposition;
    [SerializeField] private CompositionInfo blossomComposition;
    [SerializeField] private CompositionInfo underripeFruitComposition;
    [SerializeField] private CompositionInfo ripeFruitComposition;
    [SerializeField] private CompositionInfo overripeFruitComposition;

    [Header("Mass Model")]
    [SerializeField] private float woodyMassGrams = 1600f;
    [SerializeField] private float foliageMassGrams = 260f;
    [SerializeField] private float peakFruitMassGrams = 180f;
    [SerializeField, Range(0f, 1f)] private float peakBlossomFractionOfFoliage = 0.18f;

    [Header("Generated Geometry Mass Model")]
    [SerializeField] private bool deriveMassFromGeneratedGeometry = false;
    [SerializeField] private MeshFilter[] generatedWoodyMeshFilters = Array.Empty<MeshFilter>();
    [SerializeField] private bool autoCollectGeneratedWoodyMeshesFromChildren = false;
    [SerializeField] private TreeCrownShape generatedCrownShape = TreeCrownShape.OpenRounded;
    [SerializeField] private float generatedCrownRadiusMeters = 0f;
    [SerializeField] private float generatedCrownHeightMeters = 0f;
    [SerializeField] private float woodyGreenDensityKgPerCubicMeter = 720f;
    [SerializeField] private float foliageWetMassPerCubicMeterOfCrownGrams = 180f;
    [SerializeField] private float peakFruitWetMassPerCubicMeterOfCrownGrams = 120f;

    [Header("Seasonal Windows")]
    [SerializeField] private CalendarRange bloomWindow = new CalendarRange(
        new MonthDay(CalendarMonth.April, 20),
        new MonthDay(CalendarMonth.May, 31));
    [SerializeField] private CalendarRange fruitWindow = new CalendarRange(
        new MonthDay(CalendarMonth.June, 10),
        new MonthDay(CalendarMonth.August, 10));

    [Header("Harvest Settings")]
    [SerializeField, Range(0f, 1f)] private float harvestEfficiency = 0.82f;
    [SerializeField] private bool destroyOnHarvest = true;
    [SerializeField] private int harvestsRequired = 1;

    [Header("Visual Feedback")]
    [SerializeField] private GameObject harvestEffect;
    [SerializeField] private AudioClip harvestSound;

    private AudioSource audioSource;
    private int currentHarvests;
    private bool isBeingHarvested;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && harvestSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        AutoAssignDefaultCompositions();
    }

    private void OnValidate()
    {
        shrubDisplayName = string.IsNullOrWhiteSpace(shrubDisplayName) ? DefaultDisplayName : shrubDisplayName.Trim();
        woodyMassGrams = Mathf.Max(0f, woodyMassGrams);
        foliageMassGrams = Mathf.Max(0f, foliageMassGrams);
        peakFruitMassGrams = Mathf.Max(0f, peakFruitMassGrams);
        generatedCrownRadiusMeters = Mathf.Max(0f, generatedCrownRadiusMeters);
        generatedCrownHeightMeters = Mathf.Max(0f, generatedCrownHeightMeters);
        woodyGreenDensityKgPerCubicMeter = Mathf.Max(0f, woodyGreenDensityKgPerCubicMeter);
        foliageWetMassPerCubicMeterOfCrownGrams = Mathf.Max(0f, foliageWetMassPerCubicMeterOfCrownGrams);
        peakFruitWetMassPerCubicMeterOfCrownGrams = Mathf.Max(0f, peakFruitWetMassPerCubicMeterOfCrownGrams);
        harvestEfficiency = Mathf.Clamp01(harvestEfficiency);
        harvestsRequired = Mathf.Max(1, harvestsRequired);
        AutoAssignDefaultCompositions();
    }

    [ContextMenu("Auto Assign Serviceberry Assets")]
    private void AutoAssignDefaultCompositions()
    {
        TryAssignCompositionIfMissing(ref woodyComposition, DefaultWoodyCompositionName);
        TryAssignCompositionIfMissing(ref foliageComposition, DefaultFoliageCompositionName);
        TryAssignCompositionIfMissing(ref blossomComposition, DefaultBlossomCompositionName);
        TryAssignCompositionIfMissing(ref underripeFruitComposition, DefaultUnderripeFruitCompositionName);
        TryAssignCompositionIfMissing(ref ripeFruitComposition, DefaultRipeFruitCompositionName);
        TryAssignCompositionIfMissing(ref overripeFruitComposition, DefaultOverripeFruitCompositionName);
    }

    public bool Harvest(Inventory playerInventory)
    {
        if (isBeingHarvested)
        {
            return false;
        }

        if (playerInventory == null)
        {
            Debug.LogWarning("Player inventory is null!");
            return false;
        }

        if (!SeasonCalendar.TryGetCurrentDate(out CalendarDate currentDate))
        {
            Debug.LogWarning($"{gameObject.name} could not be harvested because no active SeasonCalendar was found.");
            return false;
        }

        if (!HasRequiredCompositions(out string missingCompositions))
        {
            Debug.LogWarning($"{gameObject.name} is missing required Serviceberry composition assets: {missingCompositions}");
            return false;
        }

        if (!TryBuildHarvestItems(currentDate, out List<InventoryItem> harvestedItems))
        {
            return false;
        }

        isBeingHarvested = true;
        currentHarvests++;

        if (!playerInventory.AddItems(harvestedItems))
        {
            isBeingHarvested = false;
            return false;
        }

        PlayHarvestFeedback();

        if (currentHarvests >= harvestsRequired && destroyOnHarvest)
        {
            Destroy(gameObject, 0.5f);
        }
        else
        {
            isBeingHarvested = false;
        }

        return true;
    }

    public string GetHarvestDisplayName()
    {
        if (!SeasonCalendar.TryGetCurrentDate(out CalendarDate currentDate))
        {
            return shrubDisplayName;
        }

        SeasonalHarvestState seasonalState = EvaluateSeasonalHarvestState(currentDate);
        switch (seasonalState.stage)
        {
            case ServiceberryPhenologyStage.Blooming:
                return $"{shrubDisplayName} (Blooming)";

            case ServiceberryPhenologyStage.Fruiting:
                return $"{shrubDisplayName} (Fruiting)";

            case ServiceberryPhenologyStage.Dormant:
                return $"{shrubDisplayName} (Dormant)";

            default:
                return shrubDisplayName;
        }
    }

    public string GetHarvestPreview()
    {
        StringBuilder preview = new StringBuilder();
        preview.AppendLine(shrubDisplayName);

        if (!SeasonCalendar.TryGetCurrentDate(out CalendarDate currentDate))
        {
            preview.Append("No active SeasonCalendar found.");
            return preview.ToString();
        }

        SeasonalHarvestState seasonalState = EvaluateSeasonalHarvestState(currentDate);
        float currentWoodyMassGrams = GetCurrentWoodyMassGrams();
        preview.AppendLine($"Date: {currentDate}");
        preview.AppendLine($"Season: {currentDate.Season}");
        preview.AppendLine($"Stage: {GetStageLabel(seasonalState.stage)}");
        preview.AppendLine($"{woodyComposition?.itemName ?? DefaultWoodyCompositionName}: {(currentWoodyMassGrams * harvestEfficiency):F1} g");

        if (deriveMassFromGeneratedGeometry)
        {
            preview.AppendLine($"Woody volume: {CalculateGeneratedWoodyVolumeCubicMeters():F4} m^3");
            preview.AppendLine($"Crown volume: {CalculateGeneratedCrownVolumeCubicMeters():F3} m^3");
        }

        float harvestedFoliageMass = seasonalState.availableFoliageMassGrams * harvestEfficiency;
        if (harvestedFoliageMass > 0f)
        {
            preview.AppendLine($"{foliageComposition?.itemName ?? DefaultFoliageCompositionName}: {harvestedFoliageMass:F1} g");
            if (seasonalState.blossomFractionOfFoliage > 0f)
            {
                preview.AppendLine($"  includes blossom chemistry: {(seasonalState.blossomFractionOfFoliage * 100f):F1}% of foliage mass");
            }
        }

        float harvestedFruitMass = seasonalState.availableFruitMassGrams * harvestEfficiency;
        if (harvestedFruitMass > 0f)
        {
            preview.AppendLine($"{underripeFruitComposition?.itemName ?? DefaultUnderripeFruitCompositionName}: {(harvestedFruitMass * seasonalState.ripenessDistribution.underripeWeight):F1} g");
            preview.AppendLine($"{ripeFruitComposition?.itemName ?? DefaultRipeFruitCompositionName}: {(harvestedFruitMass * seasonalState.ripenessDistribution.ripeWeight):F1} g");
            preview.AppendLine($"{overripeFruitComposition?.itemName ?? DefaultOverripeFruitCompositionName}: {(harvestedFruitMass * seasonalState.ripenessDistribution.overripeWeight):F1} g");
        }

        return preview.ToString().TrimEnd();
    }

    private bool TryBuildHarvestItems(CalendarDate currentDate, out List<InventoryItem> harvestedItems)
    {
        harvestedItems = new List<InventoryItem>();
        SeasonalHarvestState seasonalState = EvaluateSeasonalHarvestState(currentDate);

        float harvestedWoodyMass = GetCurrentWoodyMassGrams() * harvestEfficiency;
        if (harvestedWoodyMass > 0f)
        {
            harvestedItems.Add(CreateInventoryItem(woodyComposition, harvestedWoodyMass));
        }

        float harvestedFoliageMass = seasonalState.availableFoliageMassGrams * harvestEfficiency;
        if (harvestedFoliageMass > 0f)
        {
            harvestedItems.Add(new InventoryItem(
                foliageComposition,
                1,
                harvestedFoliageMass,
                BuildFoliageComposition(harvestedFoliageMass, seasonalState.blossomFractionOfFoliage)));
        }

        float harvestedFruitMass = seasonalState.availableFruitMassGrams * harvestEfficiency;
        AddFruitItemIfPresent(
            harvestedItems,
            underripeFruitComposition,
            harvestedFruitMass * seasonalState.ripenessDistribution.underripeWeight);
        AddFruitItemIfPresent(
            harvestedItems,
            ripeFruitComposition,
            harvestedFruitMass * seasonalState.ripenessDistribution.ripeWeight);
        AddFruitItemIfPresent(
            harvestedItems,
            overripeFruitComposition,
            harvestedFruitMass * seasonalState.ripenessDistribution.overripeWeight);

        if (harvestedItems.Count == 0)
        {
            Debug.LogWarning($"{gameObject.name} produced no harvestable outputs.");
            return false;
        }

        return true;
    }

    private SeasonalHarvestState EvaluateSeasonalHarvestState(CalendarDate currentDate)
    {
        float resolvedFoliageMassGrams = GetCurrentFoliageMassGrams();
        float resolvedPeakFruitMassGrams = GetCurrentPeakFruitMassGrams();
        ServiceberryPhenologyStage stage = currentDate.Season == WorldSeason.Winter
            ? ServiceberryPhenologyStage.Dormant
            : ServiceberryPhenologyStage.LeafedOut;

        float availableFoliageMass = currentDate.Season == WorldSeason.Winter ? 0f : resolvedFoliageMassGrams;
        float availableFruitMass = 0f;
        float blossomFraction = 0f;
        RipenessDistribution ripenessDistribution = new RipenessDistribution(0f, 0f, 0f);

        float bloomProgress = bloomWindow.GetProgress(currentDate);
        if (bloomProgress >= 0f)
        {
            stage = ServiceberryPhenologyStage.Blooming;
            blossomFraction = peakBlossomFractionOfFoliage * EvaluateSeasonalPeakWeight(bloomProgress);
        }

        float fruitProgress = fruitWindow.GetProgress(currentDate);
        if (fruitProgress >= 0f)
        {
            stage = ServiceberryPhenologyStage.Fruiting;
            availableFruitMass = resolvedPeakFruitMassGrams * (0.45f + (0.55f * EvaluateSeasonalPeakWeight(fruitProgress)));
            ripenessDistribution = EvaluateRipenessDistribution(fruitProgress);
        }

        return new SeasonalHarvestState(
            stage,
            availableFoliageMass,
            availableFruitMass,
            blossomFraction,
            ripenessDistribution);
    }

    private List<Composition> BuildFoliageComposition(float totalFoliageMass, float blossomFraction)
    {
        List<Composition> baseFoliageComposition = foliageComposition.GenerateRandomComposition();
        float clampedBlossomFraction = Mathf.Clamp01(blossomFraction);
        float blossomMass = totalFoliageMass * clampedBlossomFraction;
        float leafMass = Mathf.Max(0f, totalFoliageMass - blossomMass);

        if (blossomMass <= 0f || blossomComposition == null)
        {
            return baseFoliageComposition;
        }

        List<Composition> blossomChemistry = blossomComposition.GenerateRandomComposition();
        if (leafMass <= 0f)
        {
            return blossomChemistry;
        }

        return CompositionInfo.BlendCompositionByMass(
            baseFoliageComposition,
            leafMass,
            blossomChemistry,
            blossomMass);
    }

    public void ConfigureGeneratedShrub(
        MeshFilter[] woodyMeshFilters,
        TreeCrownShape crownShape,
        float crownRadiusMeters,
        float crownHeightMeters)
    {
        generatedWoodyMeshFilters = woodyMeshFilters ?? Array.Empty<MeshFilter>();
        autoCollectGeneratedWoodyMeshesFromChildren = false;
        generatedCrownShape = crownShape;
        generatedCrownRadiusMeters = Mathf.Max(0f, crownRadiusMeters);
        generatedCrownHeightMeters = Mathf.Max(0f, crownHeightMeters);
        deriveMassFromGeneratedGeometry = generatedWoodyMeshFilters.Length > 0
            || generatedCrownRadiusMeters > 0f
            || generatedCrownHeightMeters > 0f;
    }

    private float GetCurrentWoodyMassGrams()
    {
        if (!deriveMassFromGeneratedGeometry)
        {
            return woodyMassGrams;
        }

        float generatedWoodyVolume = CalculateGeneratedWoodyVolumeCubicMeters();
        if (generatedWoodyVolume <= 0f)
        {
            return woodyMassGrams;
        }

        return generatedWoodyVolume * woodyGreenDensityKgPerCubicMeter * 1000f;
    }

    private float GetCurrentFoliageMassGrams()
    {
        if (!deriveMassFromGeneratedGeometry)
        {
            return foliageMassGrams;
        }

        float crownVolume = CalculateGeneratedCrownVolumeCubicMeters();
        if (crownVolume <= 0f)
        {
            return foliageMassGrams;
        }

        return crownVolume * foliageWetMassPerCubicMeterOfCrownGrams;
    }

    private float GetCurrentPeakFruitMassGrams()
    {
        if (!deriveMassFromGeneratedGeometry)
        {
            return peakFruitMassGrams;
        }

        float crownVolume = CalculateGeneratedCrownVolumeCubicMeters();
        if (crownVolume <= 0f)
        {
            return peakFruitMassGrams;
        }

        return crownVolume * peakFruitWetMassPerCubicMeterOfCrownGrams;
    }

    private float CalculateGeneratedCrownVolumeCubicMeters()
    {
        return TreeBiomassCalculator.CalculateCrownVolume(
            generatedCrownShape,
            generatedCrownRadiusMeters,
            generatedCrownHeightMeters);
    }

    private float CalculateGeneratedWoodyVolumeCubicMeters()
    {
        HashSet<MeshFilter> uniqueFilters = new HashSet<MeshFilter>();
        float woodyVolume = 0f;

        if (generatedWoodyMeshFilters != null)
        {
            foreach (MeshFilter generatedWoodyMeshFilter in generatedWoodyMeshFilters)
            {
                if (generatedWoodyMeshFilter != null && uniqueFilters.Add(generatedWoodyMeshFilter))
                {
                    woodyVolume += VolumeHelper.GetMeshFilterVolume(generatedWoodyMeshFilter);
                }
            }
        }

        if (autoCollectGeneratedWoodyMeshesFromChildren)
        {
            foreach (MeshFilter generatedWoodyMeshFilter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (generatedWoodyMeshFilter != null && uniqueFilters.Add(generatedWoodyMeshFilter))
                {
                    woodyVolume += VolumeHelper.GetMeshFilterVolume(generatedWoodyMeshFilter);
                }
            }
        }

        return woodyVolume;
    }

    private static float EvaluateSeasonalPeakWeight(float normalizedProgress)
    {
        float clampedProgress = Mathf.Clamp01(normalizedProgress);
        return 0.35f + (0.65f * Mathf.Sin(clampedProgress * Mathf.PI));
    }

    private static RipenessDistribution EvaluateRipenessDistribution(float normalizedProgress)
    {
        float clampedProgress = Mathf.Clamp01(normalizedProgress);
        float underripeWeight = Mathf.Lerp(0.85f, 0.08f, clampedProgress);
        float ripeWeight = 0.15f + Mathf.Clamp01(1f - (Mathf.Abs(clampedProgress - 0.5f) / 0.32f));
        float overripeWeight = Mathf.Lerp(0.04f, 0.9f, clampedProgress);
        return new RipenessDistribution(underripeWeight, ripeWeight, overripeWeight);
    }

    private InventoryItem CreateInventoryItem(CompositionInfo compositionInfo, float totalMass)
    {
        return new InventoryItem(
            compositionInfo,
            1,
            totalMass,
            compositionInfo.GenerateRandomComposition());
    }

    private void AddFruitItemIfPresent(List<InventoryItem> harvestedItems, CompositionInfo compositionInfo, float totalMass)
    {
        if (harvestedItems == null || compositionInfo == null || totalMass <= 0.01f)
        {
            return;
        }

        harvestedItems.Add(CreateInventoryItem(compositionInfo, totalMass));
    }

    private bool HasRequiredCompositions(out string missingCompositions)
    {
        List<string> missing = new List<string>();

        if (woodyComposition == null)
        {
            missing.Add(DefaultWoodyCompositionName);
        }

        if (foliageComposition == null)
        {
            missing.Add(DefaultFoliageCompositionName);
        }

        if (blossomComposition == null)
        {
            missing.Add(DefaultBlossomCompositionName);
        }

        if (underripeFruitComposition == null)
        {
            missing.Add(DefaultUnderripeFruitCompositionName);
        }

        if (ripeFruitComposition == null)
        {
            missing.Add(DefaultRipeFruitCompositionName);
        }

        if (overripeFruitComposition == null)
        {
            missing.Add(DefaultOverripeFruitCompositionName);
        }

        missingCompositions = string.Join(", ", missing);
        return missing.Count == 0;
    }

    private void TryAssignCompositionIfMissing(ref CompositionInfo targetComposition, string itemName)
    {
        if (targetComposition != null)
        {
            return;
        }

        if (CompositionInfoRegistry.TryGetByItemName(itemName, out CompositionInfo resolvedComposition))
        {
            targetComposition = resolvedComposition;
        }
    }

    private void PlayHarvestFeedback()
    {
        if (harvestEffect != null)
        {
            Instantiate(harvestEffect, transform.position, Quaternion.identity);
        }

        if (audioSource != null && harvestSound != null)
        {
            audioSource.PlayOneShot(harvestSound);
        }
    }

    private static string GetStageLabel(ServiceberryPhenologyStage stage)
    {
        switch (stage)
        {
            case ServiceberryPhenologyStage.Dormant:
                return "Dormant";

            case ServiceberryPhenologyStage.LeafedOut:
                return "Leafed out";

            case ServiceberryPhenologyStage.Blooming:
                return "Blooming";

            case ServiceberryPhenologyStage.Fruiting:
                return "Fruiting";

            default:
                return stage.ToString();
        }
    }
}
