using UnityEngine;

/// <summary>
/// ScriptableObject asset that holds tuning parameters for the voxel water simulation.
/// Assign an instance to <see cref="ProceduralVoxelTerrainWaterSystem.SimulationConfig"/> to
/// drive configuration from a shared asset rather than per-scene inline values.
/// When no asset is assigned the MonoBehaviour falls back to its own inline serialized fields.
/// </summary>
[CreateAssetMenu(fileName = "WaterSimulationConfig", menuName = "Biomolecular/Water Simulation Config")]
public sealed class WaterSimulationConfig : ScriptableObject
{
    private const float DefaultHarvestedWaterMassGrams = 5_000_000f;

    // -------------------------------------------------------------------------
    // Ocean
    // -------------------------------------------------------------------------
    [Header("Ocean")]
    [SerializeField, Min(0f)]   private float seaLevelMeters              = 4.8f;
    [SerializeField, Min(1f)]   private float oceanPaddingMeters          = 220f;
    [SerializeField, Min(0.1f)] private float oceanDepthEquivalentMeters  = 12f;

    // -------------------------------------------------------------------------
    // Freshwater bodies
    // -------------------------------------------------------------------------
    [Header("Freshwater")]
    [SerializeField] private bool generateFreshwater = true;

    [Header("Lakes")]
    [SerializeField]                    private bool    generateLakes              = true;
    [SerializeField, Range(0, 4)]       private int     lakeCount                  = 2;
    [SerializeField]                    private Vector2 lakeRadiusRangeMeters      = new Vector2(8f, 14f);
    [SerializeField, Min(0.25f)]        private float   lakeDepthMeters            = 1.5f;

    [Header("Ponds")]
    [SerializeField]                    private bool    generatePonds              = true;
    [SerializeField, Range(0, 6)]       private int     pondCount                  = 3;
    [SerializeField]                    private Vector2 pondRadiusRangeMeters      = new Vector2(3.5f, 6.5f);
    [SerializeField, Min(0.25f)]        private float   pondDepthMeters            = 0.75f;

    [Header("Rivers")]
    [SerializeField]                    private bool    generateRivers             = false;
    [SerializeField, Range(0, 3)]       private int     riverCount                 = 0;
    [SerializeField]                    private Vector2 riverWidthRangeMeters      = new Vector2(4f, 8f);
    [SerializeField, Min(0.25f)]        private float   riverDepthMeters           = 1.2f;
    [SerializeField, Range(8, 64)]      private int     riverSampleCount           = 24;
    [SerializeField]                    private Vector2 riverSourceHeightRangeNormalized = new Vector2(0.38f, 0.85f);

    [Header("Harvesting")]
    [SerializeField, Min(100f)] private float freshwaterHarvestMassGrams = DefaultHarvestedWaterMassGrams;

    // -------------------------------------------------------------------------
    // Voxel carving
    // -------------------------------------------------------------------------
    [Header("Voxel Carving")]
    [SerializeField, Min(0.25f)] private float riverCarveStepMeters = 3f;

    // -------------------------------------------------------------------------
    // Rendering
    // -------------------------------------------------------------------------
    [Header("Rendering")]
    [SerializeField, Min(0.05f)] private float waterSurfaceThicknessMeters = 0.2f;
    [SerializeField] private Color freshWaterColor = new Color(0.23f, 0.5f, 0.64f, 0.82f);
    [SerializeField] private Color saltWaterColor  = new Color(0.16f, 0.34f, 0.58f, 0.84f);

    // -------------------------------------------------------------------------
    // Dynamic update tuning
    // -------------------------------------------------------------------------
    [Header("Dynamic Updates")]
    [SerializeField, Range(12, 72)] private int   lakeOutlineSampleCount          = 40;
    [SerializeField, Min(0.25f)]    private float lakeVolumeSampleSpacingMeters   = 1f;
    [SerializeField, Min(0f)]       private float lakeDynamicExpansionMeters      = 8f;
    [SerializeField, Min(0f)]       private float waterUpdatePaddingMeters        = 2f;
    [SerializeField, Min(0f)]       private float terrainRefreshDebounceSeconds   = 0.03f;

    // -------------------------------------------------------------------------
    // Public read-only accessors
    // -------------------------------------------------------------------------
    public float   SeaLevelMeters                       => seaLevelMeters;
    public float   OceanPaddingMeters                   => oceanPaddingMeters;
    public float   OceanDepthEquivalentMeters           => oceanDepthEquivalentMeters;

    public bool    GenerateFreshwater                   => generateFreshwater;

    public bool    GenerateLakes                        => generateLakes;
    public int     LakeCount                            => lakeCount;
    public Vector2 LakeRadiusRangeMeters                => lakeRadiusRangeMeters;
    public float   LakeDepthMeters                      => lakeDepthMeters;

    public bool    GeneratePonds                        => generatePonds;
    public int     PondCount                            => pondCount;
    public Vector2 PondRadiusRangeMeters                => pondRadiusRangeMeters;
    public float   PondDepthMeters                      => pondDepthMeters;

    public bool    GenerateRivers                       => generateRivers;
    public int     RiverCount                           => riverCount;
    public Vector2 RiverWidthRangeMeters                => riverWidthRangeMeters;
    public float   RiverDepthMeters                     => riverDepthMeters;
    public int     RiverSampleCount                     => riverSampleCount;
    public Vector2 RiverSourceHeightRangeNormalized     => riverSourceHeightRangeNormalized;
    public float   FreshwaterHarvestMassGrams           => freshwaterHarvestMassGrams;

    public float   RiverCarveStepMeters                 => riverCarveStepMeters;

    public float   WaterSurfaceThicknessMeters          => waterSurfaceThicknessMeters;
    public Color   FreshWaterColor                      => freshWaterColor;
    public Color   SaltWaterColor                       => saltWaterColor;

    public int     LakeOutlineSampleCount               => lakeOutlineSampleCount;
    public float   LakeVolumeSampleSpacingMeters        => lakeVolumeSampleSpacingMeters;
    public float   LakeDynamicExpansionMeters           => lakeDynamicExpansionMeters;
    public float   WaterUpdatePaddingMeters             => waterUpdatePaddingMeters;
    public float   TerrainRefreshDebounceSeconds        => terrainRefreshDebounceSeconds;

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------
    private void OnValidate()
    {
        seaLevelMeters             = Mathf.Max(0f,    seaLevelMeters);
        oceanPaddingMeters         = Mathf.Max(1f,    oceanPaddingMeters);
        oceanDepthEquivalentMeters = Mathf.Max(0.1f,  oceanDepthEquivalentMeters);

        lakeCount  = Mathf.Clamp(lakeCount,  0, 4);
        pondCount  = Mathf.Clamp(pondCount,  0, 6);
        riverCount = Mathf.Clamp(riverCount, 0, 3);

        lakeRadiusRangeMeters = ClampRange(lakeRadiusRangeMeters, 1f);
        pondRadiusRangeMeters = ClampRange(pondRadiusRangeMeters, 1f);
        riverWidthRangeMeters = ClampRange(riverWidthRangeMeters, 1f);

        lakeDepthMeters  = Mathf.Max(0.25f, lakeDepthMeters);
        pondDepthMeters  = Mathf.Max(0.25f, pondDepthMeters);
        riverDepthMeters = Mathf.Max(0.25f, riverDepthMeters);

        riverSampleCount = Mathf.Clamp(riverSampleCount, 8, 64);
        riverSourceHeightRangeNormalized = new Vector2(
            Mathf.Clamp01(Mathf.Min(riverSourceHeightRangeNormalized.x, riverSourceHeightRangeNormalized.y)),
            Mathf.Clamp01(Mathf.Max(riverSourceHeightRangeNormalized.x, riverSourceHeightRangeNormalized.y)));

        freshwaterHarvestMassGrams      = Mathf.Max(100f,  freshwaterHarvestMassGrams);
        riverCarveStepMeters            = Mathf.Max(0.25f, riverCarveStepMeters);
        waterSurfaceThicknessMeters     = Mathf.Max(0.05f, waterSurfaceThicknessMeters);
        lakeOutlineSampleCount          = Mathf.Clamp(lakeOutlineSampleCount, 12, 72);
        lakeVolumeSampleSpacingMeters   = Mathf.Max(0.25f, lakeVolumeSampleSpacingMeters);
        lakeDynamicExpansionMeters      = Mathf.Max(0f, lakeDynamicExpansionMeters);
        waterUpdatePaddingMeters        = Mathf.Max(0f, waterUpdatePaddingMeters);
        terrainRefreshDebounceSeconds   = Mathf.Max(0f, terrainRefreshDebounceSeconds);
    }

    // -------------------------------------------------------------------------
    // Preset helpers
    // -------------------------------------------------------------------------

    /// <summary>Resets all values to the Coastal Rainforest defaults.</summary>
    [ContextMenu("Apply Coastal Rainforest Preset")]
    public void ApplyCoastalRainforestPreset()
    {
        seaLevelMeters             = 4.8f;
        oceanPaddingMeters         = 220f;
        oceanDepthEquivalentMeters = 12f;

        generateFreshwater = true;
        generateLakes      = true;
        generatePonds      = true;
        generateRivers     = false;

        lakeCount              = 2;
        lakeRadiusRangeMeters  = new Vector2(8f, 14f);
        lakeDepthMeters        = 1.5f;
        pondCount              = 3;
        pondRadiusRangeMeters  = new Vector2(3.5f, 6.5f);
        pondDepthMeters        = 0.75f;
        riverCount             = 0;
        riverWidthRangeMeters  = new Vector2(4f, 8f);
        riverDepthMeters       = 1.2f;
        riverSampleCount       = 24;
        riverSourceHeightRangeNormalized = new Vector2(0.38f, 0.85f);
        freshwaterHarvestMassGrams = DefaultHarvestedWaterMassGrams;

        riverCarveStepMeters        = 3f;
        waterSurfaceThicknessMeters = 0.2f;
    }

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------
    private static Vector2 ClampRange(Vector2 range, float minValue)
    {
        float lo = Mathf.Max(minValue, Mathf.Min(range.x, range.y));
        float hi = Mathf.Max(minValue, Mathf.Max(range.x, range.y));
        return new Vector2(lo, hi);
    }
}
