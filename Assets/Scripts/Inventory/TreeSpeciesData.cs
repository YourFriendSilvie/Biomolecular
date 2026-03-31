using System;
using UnityEngine;
using UnityEngine.Serialization;

public enum TreeCrownShape
{
    Conical,
    Columnar,
    Rounded,
    OpenRounded
}

public enum TreeFoliageRenderStyle
{
    Automatic,
    BroadleafSimple,
    BigleafMaple,
    RedAlder,
    DouglasFir,
    WesternRedCedar
}

[Serializable]
public class TreeGenerationProfile
{
    [Header("Overall Dimensions")]
    public Vector2 heightRangeMeters = new Vector2(14f, 24f);
    public Vector2 trunkRadiusRangeMeters = new Vector2(0.14f, 0.32f);
    public Vector2 crownRadiusRangeMeters = new Vector2(2.5f, 5f);
    [Range(0.05f, 0.95f)] public float crownBaseHeightFraction = 0.35f;
    [Range(0.1f, 0.95f)] public float crownHeightFraction = 0.55f;
    public TreeCrownShape crownShape = TreeCrownShape.Rounded;

    [Header("Branching")]
    public Vector2Int branchCountRange = new Vector2Int(8, 16);
    public Vector2 branchLengthScaleRange = new Vector2(0.75f, 1.05f);
    public Vector2 branchRadiusScaleRange = new Vector2(0.12f, 0.22f);
    public Vector2 branchPitchDegreesRange = new Vector2(-10f, 25f);
    [Range(0.05f, 0.5f)] public float trunkTopRadiusFraction = 0.16f;
    [Range(0.05f, 0.5f)] public float branchTipRadiusFraction = 0.18f;
    [Range(0f, 0.2f)] public float trunkBendStrength = 0.05f;
    [Range(0f, 0.4f)] public float branchBendStrength = 0.16f;

    [Header("Foliage Distribution")]
    public Vector2 foliageUnitsPerCubicMeterRange = new Vector2(100f, 220f);
    public Vector2Int foliageClusterCountRange = new Vector2Int(16, 28);
    public Vector2 foliageClusterRadiusRangeMeters = new Vector2(0.45f, 1.2f);

    [Header("Mesh Resolution")]
    [Min(3)] public int radialSegments = 8;
    [Min(3)] public int trunkSegments = 10;
    [Min(2)] public int branchSegments = 5;
    [Min(4)] public int foliageLongitudeSegments = 10;
    [Min(3)] public int foliageLatitudeSegments = 6;
}

[CreateAssetMenu(fileName = "New Tree Species", menuName = "Biomolecular/Tree Species")]
public class TreeSpeciesData : ScriptableObject
{
    [Header("Species Identity")]
    public string commonName = "";
    public string scientificName = "";
    public bool evergreen = true;
    public string foliageUnitName = "leaf";
    public TreeFoliageRenderStyle foliageRenderStyle = TreeFoliageRenderStyle.Automatic;

    [Header("Foliage Rendering")]
    public Material foliageCardMaterial;
    [Min(0.1f)] public float foliageElementLengthScale = 1f;
    [Min(0.1f)] public float foliageElementWidthScale = 1f;
    [Range(1, 4)] public int foliageCardPlaneCount = 1;

    [Header("Wood and Bark Densities")]
    [Min(0f)] public float trunkGreenDensityKgPerCubicMeter = 850f;
    [Min(0f)] public float branchGreenDensityKgPerCubicMeter = 800f;
    [Min(0f)] public float barkGreenDensityKgPerCubicMeter = 600f;
    [Range(0f, 1f)] public float barkVolumeFraction = 0.12f;

    [Header("Dry Mass Fractions")]
    [Range(0f, 1f)] public float trunkDryMassFraction = 0.45f;
    [Range(0f, 1f)] public float branchDryMassFraction = 0.45f;
    [Range(0f, 1f)] public float barkDryMassFraction = 0.5f;
    [Range(0f, 1f)] public float foliageDryMassFraction = 0.4f;

    [Header("Foliage Estimation")]
    [Min(0f)] public float foliageWetMassPerUnitGrams = 1f;
    [FormerlySerializedAs("foliageWetDensityKgPerCubicMeter")]
    [Min(0f)] public float foliageWetBulkDensityKgPerCubicMeter = 0f;
    [Min(0f)] public float fallbackBranchVolumeRatio = 0.25f;

    [Header("Composition Assets")]
    public CompositionInfo woodyComposition;
    public CompositionInfo foliageComposition;

    [Header("Seed / Cone Yield")]
    [Tooltip("CompositionInfo for seeds or cones this tree produces. Leave null for no seed yield.")]
    public CompositionInfo seedComposition;
    [Tooltip("Wet mass of seeds/cones yielded when harvesting one tree, in grams. 0 = no seeds.")]
    [Min(0f)] public float seedWetMassGramsPerTree = 0f;

    [Header("Generation Profile")]
    public TreeGenerationProfile generationProfile = new TreeGenerationProfile();

    public string DisplayName => string.IsNullOrWhiteSpace(commonName) ? name : commonName;
    public TreeGenerationProfile GenerationProfile => generationProfile ??= new TreeGenerationProfile();

    public TreeFoliageRenderStyle ResolveFoliageRenderStyle()
    {
        if (foliageRenderStyle != TreeFoliageRenderStyle.Automatic)
        {
            return foliageRenderStyle;
        }

        string identifier = $"{commonName} {scientificName} {name}".ToLowerInvariant();
        if (identifier.Contains("douglas") || identifier.Contains("pseudotsuga"))
        {
            return TreeFoliageRenderStyle.DouglasFir;
        }

        if (identifier.Contains("cedar") || identifier.Contains("thuja"))
        {
            return TreeFoliageRenderStyle.WesternRedCedar;
        }

        if (identifier.Contains("maple") || identifier.Contains("acer"))
        {
            return TreeFoliageRenderStyle.BigleafMaple;
        }

        if (identifier.Contains("alder") || identifier.Contains("alnus"))
        {
            return TreeFoliageRenderStyle.RedAlder;
        }

        return evergreen ? TreeFoliageRenderStyle.DouglasFir : TreeFoliageRenderStyle.BroadleafSimple;
    }

    private void OnValidate()
    {
        generationProfile ??= new TreeGenerationProfile();
        foliageElementLengthScale = Mathf.Max(0.1f, foliageElementLengthScale);
        foliageElementWidthScale = Mathf.Max(0.1f, foliageElementWidthScale);
        foliageCardPlaneCount = Mathf.Clamp(foliageCardPlaneCount, 1, 4);
        foliageWetMassPerUnitGrams = Mathf.Max(0f, foliageWetMassPerUnitGrams);
        foliageWetBulkDensityKgPerCubicMeter = Mathf.Max(0f, foliageWetBulkDensityKgPerCubicMeter);
        fallbackBranchVolumeRatio = Mathf.Max(0f, fallbackBranchVolumeRatio);
    }
}
