using System;
using System.Collections.Generic;
using UnityEngine;

// ── Machine type enum ─────────────────────────────────────────────────────────

/// <summary>
/// Identifies which machine class can run a given ProcessingRecipe.
/// </summary>
public enum MachineType
{
    Any,                   // Recipe works in any machine (manual or automatic)
    Manual,                // Player-crafted at a CraftingTable; no power needed
    Crusher,               // Mechanical size reduction of rock/ore
    MagneticSeparator,     // Separates Fe-bearing minerals by magnetism
    Smelter,               // High-temperature reduction/fusion (Fe, Cu, etc.)
    Kiln,                  // Drying or calcination at moderate temperature
    BiomassFractionator,   // Organosolv/hydrothermal: separates cellulose/hemi/lignin
    FermentationVessel,    // Biological fermentation: cellulose/glucose → ethanol
    AcidHydrolysisReactor, // Acid-catalyzed conversion: hemicellulose → furfural
    Pyrolyzer,             // Fast pyrolysis 400-600°C: biomass → bio-oil + char
    HDOReactor,            // Hydrodeoxygenation: bio-oil + H₂ → biodiesel
    Gasifier,              // Thermochemical gasification: dry biomass → syngas
    TransesterificationUnit, // Lipids + methanol → biodiesel (FAME) + glycerol
    // ExtractionMachine (OilPress, SolventExtractor) does not use ProcessingRecipe —
    // it is configured directly in the Inspector via ExtractionTarget list.
}

// ── Recipe output ─────────────────────────────────────────────────────────────

/// <summary>
/// One output produced by a completed ProcessingRecipe cycle.
/// Recipes can have multiple outputs (e.g. pyrolysis → bio-oil + char + syngas).
/// </summary>
[Serializable]
public class RecipeOutput
{
    [Tooltip("CompositionInfo template for this output item.")]
    public CompositionInfo composition;

    [Tooltip("Mass of this output in grams per completed cycle.")]
    [Min(0.001f)]
    public float massGrams = 10f;

    [Tooltip("Fraction of theoretical yield (1.0 = 100%). Accounts for real-world conversion losses.")]
    [Range(0.01f, 1f)]
    public float yieldFraction = 1f;
}

// ── Recipe ingredient ─────────────────────────────────────────────────────────

/// <summary>
/// One ingredient in a ProcessingRecipe — defined by the molecule/resource name
/// and the mass required to start and consumed per cycle.
/// </summary>
[Serializable]
public class RecipeInput
{
    [Tooltip("Resource/molecule name (must match CompositionInfo resource strings exactly).")]
    public string resourceName;

    [Tooltip("Minimum grams of this resource that must be present in input storage to begin.")]
    [Min(0.001f)]
    public float requiredMassGrams = 10f;

    [Tooltip("Grams consumed from input storage per completed cycle.")]
    [Min(0.001f)]
    public float consumedMassGrams = 10f;
}

// ── Processing recipe asset ───────────────────────────────────────────────────

/// <summary>
/// Describes one transformation step: a set of composition-aware inputs →
/// one output item.  Assigned to a ProcessingMachine via its availableRecipes list.
///
/// Recipes are composition-aware: they check for specific molecules (e.g., "Iron Oxide",
/// "Cellulose") regardless of what item they came from.  This means a Crusher recipe
/// can accept any ore chunk as long as it contains the required mineral.
///
/// Real-world basis for energy/time values:
///   Crushing:            ~5–15 kW industrial; game 500 W, 10–30 s per batch
///   Magnetic separation: ~2–5 kW; game 200 W, 20 s
///   Iron smelting:       ~1100–1600°C; game 1000 W, 120 s per 100 g batch
///   Copper smelting:     ~1150–1250°C; game 1000 W, 90 s per 50 g batch
///   Kiln drying:         ~3–6 MWh/m³ lumber; game 500 W, 60 s per 100 g batch
/// </summary>
[CreateAssetMenu(fileName = "New Recipe", menuName = "Biomolecular/ProcessingRecipe")]
public class ProcessingRecipe : ScriptableObject
{
    [Header("Identity")]
    public string    recipeName;
    public MachineType requiredMachine = MachineType.Any;

    [Header("Inputs")]
    public List<RecipeInput> inputs = new();

    [Header("Outputs")]
    [Tooltip("Items produced per completed cycle. Add multiple entries for multi-product reactions " +
             "(e.g. pyrolysis → bio-oil + biochar + syngas).")]
    public List<RecipeOutput> outputs = new();

    [Header("Processing")]
    [Tooltip("Time in seconds to complete one cycle at full power.")]
    [Min(0.1f)]
    public float processingTimeSeconds = 10f;

    [Header("Power")]
    [Tooltip("Watts required from the PowerGrid while processing. 0 = no power needed.")]
    [Min(0f)]
    public float requiredWatts = 500f;

    // ── Helpers used by ProcessingMachine ─────────────────────────────────────

    /// <summary>
    /// Returns true when the storage contains at least the required mass of every input.
    /// </summary>
    public bool CanProcess(MachineItemStorage storage)
    {
        if (storage == null || inputs == null) return false;
        foreach (var input in inputs)
            if (storage.GetAvailableMassOf(input.resourceName) < input.requiredMassGrams)
                return false;
        return true;
    }

    /// <summary>
    /// Consumes the specified amounts from storage.  Returns false and logs a warning
    /// if any ingredient cannot be fully consumed (storage was modified by another path).
    /// </summary>
    public bool ConsumeInputs(MachineItemStorage storage)
    {
        if (storage == null) return false;
        foreach (var input in inputs)
        {
            float consumed = storage.ConsumeMass(input.resourceName, input.consumedMassGrams);
            if (consumed < input.consumedMassGrams * 0.99f)
            {
                Debug.LogWarning($"[ProcessingRecipe] '{recipeName}': " +
                    $"could not fully consume '{input.resourceName}'. " +
                    $"Got {consumed:F2} g, needed {input.consumedMassGrams:F2} g.");
                return false;
            }
        }
        return true;
    }
}
