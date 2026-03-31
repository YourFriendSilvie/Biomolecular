#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wires all machine prefabs automatically:
///   - Assigns InputStorage / OutputStorage child components to the machine's
///     inputStorage / outputStorage serialized fields.
///   - Populates each machine's availableRecipes array with the correct
///     ProcessingRecipe assets created by DefaultAssetsCreator.
///
/// Run via: Biomolecular → Setup → Wire Machine Prefabs
///
/// Prerequisites:
///   1. Run "Create Machine Prefabs" first.
///   2. Run "Create Default Recipe Assets" first.
/// </summary>
public static class MachinePrefabWiring
{
    const string MachineRoot   = "Assets/Prefabs/Machines";
    const string RecipeIron    = "Assets/Objects/Recipes/Iron";
    const string RecipeBio     = "Assets/Objects/Recipes/Biofuel";
    const string RecipeRoot    = "Assets/Objects/Recipes";

    // Maps machine prefab name → array of recipe asset paths to assign.
    static readonly Dictionary<string, string[]> RecipeMap = new()
    {
        ["OreCrusher"]              = new[] { $"{RecipeIron}/Gravity Separation Copper.asset" },
        ["MagneticSeparator"]       = new[] { $"{RecipeIron}/Magnetic Separation Iron.asset" },
        ["Smelter"]                 = new[]
        {
            $"{RecipeIron}/Smelt Iron.asset",
            $"{RecipeIron}/Smelt Copper.asset",
            $"{RecipeIron}/Forge Iron Plate.asset",
            $"{RecipeIron}/Forge Iron Component.asset",
            $"{RecipeBio}/Hydrothermal Liquefaction.asset",
        },
        ["Kiln"]                    = new[]
        {
            $"{RecipeBio}/Kiln Dry Wood.asset",
            $"{RecipeRoot}/Kiln/Char Biomass.asset",
        },
        ["BiomassFractionator"]     = Array.Empty<string>(), // Custom logic, no ProcessingRecipe
        ["FermentationVessel"]      = new[] { $"{RecipeBio}/Ferment Cellulose.asset" },
        ["AcidHydrolysisReactor"]   = new[] { $"{RecipeBio}/Acid Hydrolyze Hemicellulose.asset" },
        ["Pyrolyzer"]               = new[]
        {
            $"{RecipeBio}/Pyrolyze Lignin.asset",
            $"{RecipeBio}/Pyrolyze Dry Biomass.asset",
        },
        ["HDOReactor"]              = new[] { $"{RecipeBio}/HDO Bio-oil.asset" },
        ["Gasifier"]                = new[] { $"{RecipeBio}/Gasify Biomass.asset" },
        ["TransesterificationUnit"] = new[] { $"{RecipeBio}/Transesterify Plant Oil.asset" },
        // OilPress and SolventExtractor use ExtractionMachine — no ProcessingRecipe assets.
        // Storage refs are wired at prefab creation time by MachinePrefabCreator.
        ["OilPress"]                = Array.Empty<string>(),
        ["SolventExtractor"]        = Array.Empty<string>(),
        // Power machines — no ProcessingRecipe slots needed.
        ["SteamGenerator"]          = Array.Empty<string>(),
        ["LiquidFuelGenerator"]     = Array.Empty<string>(),
        ["Battery"]                 = Array.Empty<string>(),
        ["ManualCraftingTable"]     = Array.Empty<string>(),
    };

    [MenuItem("Biomolecular/Setup/Wire Machine Prefabs")]
    public static void WireAll()
    {
        int wired = 0, skipped = 0;

        foreach (var (machineName, recipePaths) in RecipeMap)
        {
            string prefabPath = $"{MachineRoot}/{machineName}.prefab";
            var prefabAsset   = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefabAsset == null)
            {
                Debug.LogWarning($"[MachinePrefabWiring] Prefab not found: {prefabPath}  — run 'Create Machine Prefabs' first.");
                skipped++;
                continue;
            }

            // Open prefab contents for editing (isolated, no scene involvement).
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);

            try
            {
                bool changed = false;

                // ── Wire storage children ──────────────────────────────────────
                var inputGo  = contents.transform.Find("InputStorage");
                var outputGo = contents.transform.Find("OutputStorage");

                if (inputGo == null || outputGo == null)
                {
                    Debug.LogWarning($"[MachinePrefabWiring] {machineName}: missing InputStorage or OutputStorage child.");
                    skipped++;
                    continue;
                }

                var inputStorage  = inputGo.GetComponent<MachineItemStorage>();
                var outputStorage = outputGo.GetComponent<MachineItemStorage>();

                if (inputStorage == null || outputStorage == null)
                {
                    Debug.LogWarning($"[MachinePrefabWiring] {machineName}: MachineItemStorage component missing on storage child.");
                    skipped++;
                    continue;
                }

                // Find the ProcessingMachine component (any subclass).
                var machine = contents.GetComponent<ProcessingMachine>();
                var extractionMachine = contents.GetComponent<ExtractionMachine>();

                if (machine != null)
                {
                    var so = new SerializedObject(machine);

                    var inputProp  = so.FindProperty("inputStorage");
                    var outputProp = so.FindProperty("outputStorage");

                    if (inputProp != null && inputProp.objectReferenceValue != inputStorage)
                    {
                        inputProp.objectReferenceValue = inputStorage;
                        changed = true;
                    }
                    if (outputProp != null && outputProp.objectReferenceValue != outputStorage)
                    {
                        outputProp.objectReferenceValue = outputStorage;
                        changed = true;
                    }

                    // ── Assign recipes ─────────────────────────────────────────
                    var recipesProp = so.FindProperty("availableRecipes");
                    if (recipesProp != null && recipePaths.Length > 0)
                    {
                        var loaded = new List<ProcessingRecipe>();
                        foreach (var rp in recipePaths)
                        {
                            var recipe = AssetDatabase.LoadAssetAtPath<ProcessingRecipe>(rp);
                            if (recipe != null)
                                loaded.Add(recipe);
                            else
                                Debug.LogWarning($"[MachinePrefabWiring] Recipe asset not found: {rp}  — run 'Create Default Recipe Assets' first.");
                        }

                        // Only overwrite if not already matching.
                        bool needsUpdate = recipesProp.arraySize != loaded.Count;
                        if (!needsUpdate)
                        {
                            for (int i = 0; i < loaded.Count; i++)
                            {
                                if (recipesProp.GetArrayElementAtIndex(i).objectReferenceValue != loaded[i])
                                {
                                    needsUpdate = true;
                                    break;
                                }
                            }
                        }

                        if (needsUpdate)
                        {
                            recipesProp.arraySize = loaded.Count;
                            for (int i = 0; i < loaded.Count; i++)
                                recipesProp.GetArrayElementAtIndex(i).objectReferenceValue = loaded[i];
                            changed = true;
                        }
                    }

                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
                else if (extractionMachine != null)
                {
                    // ExtractionMachine: wire storage references (targets are set at prefab creation time).
                    var so = new SerializedObject(extractionMachine);

                    var inputProp  = so.FindProperty("inputStorage");
                    var outputProp = so.FindProperty("outputStorage");

                    if (inputProp != null && inputProp.objectReferenceValue != inputStorage)
                    { inputProp.objectReferenceValue = inputStorage;  changed = true; }
                    if (outputProp != null && outputProp.objectReferenceValue != outputStorage)
                    { outputProp.objectReferenceValue = outputStorage; changed = true; }

                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"[MachinePrefabWiring] {machineName}: ExtractionMachine storage wired (targets configured at creation time).");
                }
                else
                {
                    // Non-ProcessingMachine machines (Battery, LiquidFuelGenerator, etc.)
                    // don't need storage wiring.
                    Debug.Log($"[MachinePrefabWiring] {machineName}: no ProcessingMachine or ExtractionMachine component — skipping storage/recipe wiring.");
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                    wired++;
                }
                else
                {
                    skipped++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        AssetDatabase.SaveAssets();
        WireShipWorkstations();
        Debug.Log($"[MachinePrefabWiring] Done. Wired: {wired}  Already up-to-date / skipped: {skipped}");
    }

    // ── Ship workstation recipe wiring ────────────────────────────────────────

    // Maps ship child workstation name → recipe paths.
    static readonly Dictionary<string, string[]> ShipRecipeMap = new()
    {
        ["SalvageCrusher"]   = new[] { $"{RecipeIron}/Gravity Separation Copper.asset" },
        ["SalvageSeparator"] = new[] { $"{RecipeIron}/Magnetic Separation Iron.asset" },
        ["SalvageFurnace"]   = new[]
        {
            $"{RecipeIron}/Smelt Iron.asset",
            $"{RecipeIron}/Forge Iron Plate.asset",
            $"{RecipeIron}/Forge Iron Component.asset",
        },
    };

    static void WireShipWorkstations()
    {
        const string shipPath = "Assets/Prefabs/CrashedShip.prefab";
        var shipAsset = AssetDatabase.LoadAssetAtPath<GameObject>(shipPath);
        if (shipAsset == null)
        {
            Debug.LogWarning("[MachinePrefabWiring] CrashedShip.prefab not found — run 'Create Machine Prefabs' first.");
            return;
        }

        var contents = PrefabUtility.LoadPrefabContents(shipPath);
        bool changed = false;

        try
        {
            foreach (var (childName, recipePaths) in ShipRecipeMap)
            {
                var child = contents.transform.Find(childName);
                if (child == null)
                {
                    Debug.LogWarning($"[MachinePrefabWiring] CrashedShip: child '{childName}' not found.");
                    continue;
                }

                var machine = child.GetComponent<ShipManualMachine>();
                if (machine == null)
                {
                    Debug.LogWarning($"[MachinePrefabWiring] CrashedShip/{childName}: no ShipManualMachine component.");
                    continue;
                }

                var loaded = new List<ProcessingRecipe>();
                foreach (var rp in recipePaths)
                {
                    var recipe = AssetDatabase.LoadAssetAtPath<ProcessingRecipe>(rp);
                    if (recipe != null)
                        loaded.Add(recipe);
                    else
                        Debug.LogWarning($"[MachinePrefabWiring] Recipe not found: {rp} — run 'Create Default Recipe Assets' first.");
                }

                var so = new SerializedObject(machine);
                var recipesProp = so.FindProperty("recipes");
                bool needsUpdate = recipesProp.arraySize != loaded.Count;
                if (!needsUpdate)
                {
                    for (int i = 0; i < loaded.Count; i++)
                        if (recipesProp.GetArrayElementAtIndex(i).objectReferenceValue != loaded[i])
                        { needsUpdate = true; break; }
                }

                if (needsUpdate)
                {
                    recipesProp.arraySize = loaded.Count;
                    for (int i = 0; i < loaded.Count; i++)
                        recipesProp.GetArrayElementAtIndex(i).objectReferenceValue = loaded[i];
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                    Debug.Log($"[MachinePrefabWiring] CrashedShip/{childName}: wired {loaded.Count} recipes.");
                }
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(contents, shipPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
#endif
