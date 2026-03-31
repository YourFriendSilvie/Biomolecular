using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TreeCompartmentMetrics
{
    public float trunkVolumeCubicMeters;
    public float branchVolumeCubicMeters;
    public float barkVolumeCubicMeters;
    public float canopyVolumeCubicMeters;
    public int foliageUnitCount;

    public float TotalWoodyVolumeCubicMeters => trunkVolumeCubicMeters + branchVolumeCubicMeters;
}

[Serializable]
public class TreeBiomassSnapshot
{
    public float trunkWoodWetMassGrams;
    public float branchWoodWetMassGrams;
    public float barkWetMassGrams;
    public float foliageWetMassGrams;

    public float trunkWoodDryMassGrams;
    public float branchWoodDryMassGrams;
    public float barkDryMassGrams;
    public float foliageDryMassGrams;

    public float TotalWoodyWetMassGrams => trunkWoodWetMassGrams + branchWoodWetMassGrams + barkWetMassGrams;
    public float TotalWetMassGrams => TotalWoodyWetMassGrams + foliageWetMassGrams;
}

[Serializable]
public class TreeHarvestYield
{
    public string label;
    public CompositionInfo compositionInfo;
    public float wetMassGrams;
}

public static class TreeBiomassCalculator
{
    public static float CalculateCrownVolume(TreeCrownShape crownShape, float crownRadius, float crownHeight)
    {
        float radius = Mathf.Max(0f, crownRadius);
        float height = Mathf.Max(0f, crownHeight);

        switch (crownShape)
        {
            case TreeCrownShape.Conical:
                return Mathf.PI * radius * radius * height / 3f;

            case TreeCrownShape.Columnar:
                return Mathf.PI * radius * radius * height * 0.72f;

            case TreeCrownShape.OpenRounded:
                return Mathf.PI * radius * radius * height * 0.58f;

            default:
                return (4f / 3f) * Mathf.PI * radius * radius * (height * 0.5f);
        }
    }

    public static TreeBiomassSnapshot CalculateBiomass(TreeCompartmentMetrics metrics, TreeSpeciesData speciesData)
    {
        TreeBiomassSnapshot biomass = new TreeBiomassSnapshot();
        if (metrics == null || speciesData == null)
        {
            return biomass;
        }

        float trunkVolume = Mathf.Max(0f, metrics.trunkVolumeCubicMeters);
        float branchVolume = Mathf.Max(0f, metrics.branchVolumeCubicMeters);
        float totalWoodyVolume = trunkVolume + branchVolume;
        float barkVolume = Mathf.Clamp(metrics.barkVolumeCubicMeters, 0f, totalWoodyVolume);
        float barkShare = totalWoodyVolume > 0f ? barkVolume / totalWoodyVolume : 0f;

        float trunkBarkVolume = trunkVolume * barkShare;
        float branchBarkVolume = branchVolume * barkShare;
        float trunkWoodVolume = Mathf.Max(0f, trunkVolume - trunkBarkVolume);
        float branchWoodVolume = Mathf.Max(0f, branchVolume - branchBarkVolume);

        biomass.trunkWoodWetMassGrams = ConvertCubicMetersToGrams(trunkWoodVolume, speciesData.trunkGreenDensityKgPerCubicMeter);
        biomass.branchWoodWetMassGrams = ConvertCubicMetersToGrams(branchWoodVolume, speciesData.branchGreenDensityKgPerCubicMeter);
        biomass.barkWetMassGrams = ConvertCubicMetersToGrams(barkVolume, speciesData.barkGreenDensityKgPerCubicMeter);
        biomass.foliageWetMassGrams = CalculateFoliageWetMass(metrics, speciesData);

        biomass.trunkWoodDryMassGrams = biomass.trunkWoodWetMassGrams * Mathf.Clamp01(speciesData.trunkDryMassFraction);
        biomass.branchWoodDryMassGrams = biomass.branchWoodWetMassGrams * Mathf.Clamp01(speciesData.branchDryMassFraction);
        biomass.barkDryMassGrams = biomass.barkWetMassGrams * Mathf.Clamp01(speciesData.barkDryMassFraction);
        biomass.foliageDryMassGrams = biomass.foliageWetMassGrams * Mathf.Clamp01(speciesData.foliageDryMassFraction);

        return biomass;
    }

    public static List<TreeHarvestYield> BuildHarvestYields(
        TreeBiomassSnapshot biomass,
        TreeSpeciesData speciesData,
        float harvestEfficiency)
    {
        List<TreeHarvestYield> yields = new List<TreeHarvestYield>();
        if (biomass == null || speciesData == null)
        {
            return yields;
        }

        float efficiency = Mathf.Clamp01(harvestEfficiency);
        float woodyWetMass = biomass.TotalWoodyWetMassGrams * efficiency;
        float foliageWetMass = biomass.foliageWetMassGrams * efficiency;

        if (speciesData.woodyComposition != null && woodyWetMass > 0f)
        {
            yields.Add(new TreeHarvestYield
            {
                label = "Woody Matter",
                compositionInfo = speciesData.woodyComposition,
                wetMassGrams = woodyWetMass
            });
        }

        if (speciesData.foliageComposition != null && foliageWetMass > 0f)
        {
            yields.Add(new TreeHarvestYield
            {
                label = "Foliage",
                compositionInfo = speciesData.foliageComposition,
                wetMassGrams = foliageWetMass
            });
        }

        float seedMass = speciesData.seedWetMassGramsPerTree * efficiency;
        if (speciesData.seedComposition != null && seedMass > 0f)
        {
            yields.Add(new TreeHarvestYield
            {
                label = "Seeds",
                compositionInfo = speciesData.seedComposition,
                wetMassGrams = seedMass
            });
        }

        return yields;
    }

    private static float ConvertCubicMetersToGrams(float volumeCubicMeters, float densityKgPerCubicMeter)
    {
        return Mathf.Max(0f, volumeCubicMeters) * Mathf.Max(0f, densityKgPerCubicMeter) * 1000f;
    }

    private static float CalculateFoliageWetMass(TreeCompartmentMetrics metrics, TreeSpeciesData speciesData)
    {
        if (metrics.canopyVolumeCubicMeters > 0f && speciesData.foliageWetBulkDensityKgPerCubicMeter > 0f)
        {
            return ConvertCubicMetersToGrams(metrics.canopyVolumeCubicMeters, speciesData.foliageWetBulkDensityKgPerCubicMeter);
        }

        float perUnitMass = Mathf.Max(0f, speciesData.foliageWetMassPerUnitGrams);
        float foliageWetMass = Mathf.Max(0, metrics.foliageUnitCount) * perUnitMass;
        return foliageWetMass;
    }
}
