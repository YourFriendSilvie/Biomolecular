using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class ProceduralTreeHarvestable : MonoBehaviour, IHarvestable
{
    [Header("Species")]
    [SerializeField] private TreeSpeciesData speciesData;

    [Header("Generated Tree Geometry")]
    [SerializeField] private MeshFilter trunkMeshFilter;
    [SerializeField] private MeshFilter[] branchMeshFilters = Array.Empty<MeshFilter>();
    [SerializeField] private bool autoCollectBranchMeshesFromChildren = true;
    [SerializeField] private float manualBarkVolumeCubicMeters = -1f;
    [SerializeField] private TreeCrownShape crownShape = TreeCrownShape.Rounded;
    [SerializeField] private float crownRadiusMeters = 0f;
    [SerializeField] private float crownHeightMeters = 0f;
    [SerializeField] private float visualFoliageUnitsPerCubicMeter = 0f;

    [Header("Harvest Settings")]
    [SerializeField] private float harvestEfficiency = 0.8f;
    [SerializeField] private bool destroyOnHarvest = true;
    [SerializeField] private int harvestsRequired = 1;
    [SerializeField] private bool randomizeCompositionOnStart = true;

    [Header("Visual Feedback")]
    [SerializeField] private GameObject harvestEffect;
    [SerializeField] private AudioClip harvestSound;

    private AudioSource audioSource;
    private int currentHarvests;
    private bool isBeingHarvested;
    private List<Composition> generatedWoodyComposition;
    private List<Composition> generatedFoliageComposition;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && harvestSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
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

        if (speciesData == null)
        {
            Debug.LogWarning($"{gameObject.name} has no TreeSpeciesData assigned!");
            return false;
        }

        List<TreeHarvestYield> harvestYields = GetHarvestYields();
        if (harvestYields.Count == 0)
        {
            Debug.LogWarning($"{gameObject.name} produced no harvestable yields.");
            return false;
        }

        isBeingHarvested = true;
        currentHarvests++;

        List<InventoryItem> harvestedItems = new List<InventoryItem>(harvestYields.Count);
        foreach (var yield in harvestYields)
        {
            List<Composition> effectiveComposition = GetGeneratedCompositionFor(yield.compositionInfo);
            harvestedItems.Add(new InventoryItem(yield.compositionInfo, 1, yield.wetMassGrams, effectiveComposition));
        }

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
        return speciesData != null ? speciesData.DisplayName : gameObject.name;
    }

    public string GetHarvestPreview()
    {
        TreeCompartmentMetrics metrics = BuildCompartmentMetrics();
        TreeBiomassSnapshot biomass = TreeBiomassCalculator.CalculateBiomass(metrics, speciesData);
        List<TreeHarvestYield> harvestYields = GetHarvestYields();

        StringBuilder preview = new StringBuilder();
        preview.AppendLine(GetHarvestDisplayName());
        preview.AppendLine($"Trunk volume: {metrics.trunkVolumeCubicMeters:F3} m^3");
        preview.AppendLine($"Branch volume: {metrics.branchVolumeCubicMeters:F3} m^3");
        preview.AppendLine($"Bark volume: {metrics.barkVolumeCubicMeters:F3} m^3");
        preview.AppendLine($"{GetFoliageUnitLabel()}: {metrics.foliageUnitCount}");
        preview.AppendLine($"Woody wet mass: {biomass.TotalWoodyWetMassGrams:F1} g");
        preview.AppendLine($"Foliage wet mass: {biomass.foliageWetMassGrams:F1} g");

        foreach (var yield in harvestYields)
        {
            preview.AppendLine($"{yield.label}: {yield.wetMassGrams:F1} g");
        }

        return preview.ToString().TrimEnd();
    }

    public TreeCompartmentMetrics BuildCompartmentMetrics()
    {
        float trunkVolume = VolumeHelper.GetMeshFilterVolume(trunkMeshFilter);
        float branchVolume = CalculateBranchVolume(trunkVolume);
        float canopyVolume = TreeBiomassCalculator.CalculateCrownVolume(crownShape, crownRadiusMeters, crownHeightMeters);
        int foliageUnitCount = Mathf.Max(0, Mathf.RoundToInt(canopyVolume * Mathf.Max(0f, visualFoliageUnitsPerCubicMeter)));

        float totalWoodyVolume = trunkVolume + branchVolume;
        float barkVolume = manualBarkVolumeCubicMeters >= 0f
            ? manualBarkVolumeCubicMeters
            : totalWoodyVolume * GetBarkVolumeFraction();

        return new TreeCompartmentMetrics
        {
            trunkVolumeCubicMeters = trunkVolume,
            branchVolumeCubicMeters = branchVolume,
            barkVolumeCubicMeters = barkVolume,
            canopyVolumeCubicMeters = canopyVolume,
            foliageUnitCount = foliageUnitCount
        };
    }

    public TreeBiomassSnapshot BuildBiomassSnapshot()
    {
        return TreeBiomassCalculator.CalculateBiomass(BuildCompartmentMetrics(), speciesData);
    }

    public void ConfigureGeneratedTree(
        TreeSpeciesData generatedSpeciesData,
        MeshFilter generatedTrunkMeshFilter,
        MeshFilter[] generatedBranchMeshFilters,
        TreeCrownShape generatedCrownShape,
        float generatedCrownRadiusMeters,
        float generatedCrownHeightMeters,
        float generatedVisualFoliageUnitsPerCubicMeter)
    {
        speciesData = generatedSpeciesData;
        trunkMeshFilter = generatedTrunkMeshFilter;
        branchMeshFilters = generatedBranchMeshFilters ?? Array.Empty<MeshFilter>();
        autoCollectBranchMeshesFromChildren = false;
        crownShape = generatedCrownShape;
        crownRadiusMeters = Mathf.Max(0f, generatedCrownRadiusMeters);
        crownHeightMeters = Mathf.Max(0f, generatedCrownHeightMeters);
        visualFoliageUnitsPerCubicMeter = Mathf.Max(0f, generatedVisualFoliageUnitsPerCubicMeter);
        generatedWoodyComposition = null;
        generatedFoliageComposition = null;
    }

    private List<TreeHarvestYield> GetHarvestYields()
    {
        return TreeBiomassCalculator.BuildHarvestYields(BuildBiomassSnapshot(), speciesData, harvestEfficiency);
    }

    private float CalculateBranchVolume(float trunkVolume)
    {
        float explicitBranchVolume = 0f;
        HashSet<MeshFilter> uniqueBranchFilters = new HashSet<MeshFilter>();

        if (branchMeshFilters != null)
        {
            foreach (var branchMeshFilter in branchMeshFilters)
            {
                if (branchMeshFilter != null && uniqueBranchFilters.Add(branchMeshFilter))
                {
                    explicitBranchVolume += VolumeHelper.GetMeshFilterVolume(branchMeshFilter);
                }
            }
        }

        if (autoCollectBranchMeshesFromChildren)
        {
            foreach (var branchMeshFilter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (branchMeshFilter != null &&
                    branchMeshFilter != trunkMeshFilter &&
                    uniqueBranchFilters.Add(branchMeshFilter))
                {
                    explicitBranchVolume += VolumeHelper.GetMeshFilterVolume(branchMeshFilter);
                }
            }
        }

        if (explicitBranchVolume > 0f)
        {
            return explicitBranchVolume;
        }

        float fallbackRatio = speciesData != null ? Mathf.Max(0f, speciesData.fallbackBranchVolumeRatio) : 0f;
        return trunkVolume * fallbackRatio;
    }

    private float GetBarkVolumeFraction()
    {
        return speciesData != null ? Mathf.Clamp01(speciesData.barkVolumeFraction) : 0f;
    }

    private List<Composition> GetGeneratedCompositionFor(CompositionInfo compositionInfo)
    {
        if (speciesData == null || compositionInfo == null)
        {
            return null;
        }

        if (compositionInfo == speciesData.woodyComposition)
        {
            return GetOrCreateGeneratedComposition(ref generatedWoodyComposition, compositionInfo);
        }

        if (compositionInfo == speciesData.foliageComposition)
        {
            return GetOrCreateGeneratedComposition(ref generatedFoliageComposition, compositionInfo);
        }

        return compositionInfo.composition;
    }

    private List<Composition> GetOrCreateGeneratedComposition(ref List<Composition> cachedComposition, CompositionInfo compositionInfo)
    {
        if (!randomizeCompositionOnStart || compositionInfo == null)
        {
            return compositionInfo != null ? compositionInfo.composition : null;
        }

        if (cachedComposition == null || cachedComposition.Count == 0)
        {
            cachedComposition = GenerateCompositionSnapshot(compositionInfo);
        }

        return cachedComposition != null && cachedComposition.Count > 0
            ? cachedComposition
            : compositionInfo.composition;
    }

    private static List<Composition> GenerateCompositionSnapshot(CompositionInfo compositionInfo)
    {
        if (compositionInfo == null)
        {
            return null;
        }

        return compositionInfo.GenerateRandomComposition();
    }

    private string GetFoliageUnitLabel()
    {
        if (speciesData == null || string.IsNullOrWhiteSpace(speciesData.foliageUnitName))
        {
            return "Foliage units";
        }

        return speciesData.foliageUnitName;
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
}
