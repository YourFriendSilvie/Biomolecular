#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Creates all default CompositionInfo and ProcessingRecipe ScriptableObject assets
/// for the iron production chain and biofuel production chain.
///
/// Run via: Biomolecular → Setup → Create Default Recipe Assets
///
/// All CompositionInfo assets are placed in Assets/Objects/Resources/ subfolders
/// so CompositionInfoRegistry (Resources.LoadAll) can discover them automatically.
/// ProcessingRecipe assets are placed in Assets/Objects/Recipes/ and assigned to
/// machines manually via their Inspector availableRecipes field.
/// </summary>
public static class DefaultAssetsCreator
{
    // ── Root paths ────────────────────────────────────────────────────────────
    const string CompRoot    = "Assets/Objects/Resources";
    const string RecipeRoot  = "Assets/Objects/Recipes";

    // ── Entry point ───────────────────────────────────────────────────────────
    [MenuItem("Biomolecular/Setup/Create Default Recipe Assets")]
    public static void CreateAll()
    {
        EnsureFolders();
        var comps = BuildAllCompositions();
        BuildIronRecipes(comps);
        BuildBiofuelRecipes(comps);
        BuildKilnRecipes(comps);
        // Note: OilPress and SolventExtractor use ExtractionMachine — no recipe assets needed.
        // Their targets are configured directly in the Inspector on each prefab.
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DefaultAssetsCreator] Done. Assign recipe assets to machine 'Available Recipes' fields in the Inspector.");
    }

    // ── Folder setup ──────────────────────────────────────────────────────────
    static void EnsureFolders()
    {
        EnsureFolder("Assets/Objects",          "Resources");
        EnsureFolder(CompRoot,                  "Iron");
        EnsureFolder(CompRoot,                  "Biofuel");
        EnsureFolder(CompRoot,                  "Seeds");
        EnsureFolder("Assets/Objects",          "Recipes");
        EnsureFolder(RecipeRoot,                "Iron");
        EnsureFolder(RecipeRoot,                "Biofuel");
        EnsureFolder(RecipeRoot,                "Kiln");
    }

    static void EnsureFolder(string parent, string child)
    {
        string full = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(full))
            AssetDatabase.CreateFolder(parent, child);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // COMPOSITIONS
    // ═════════════════════════════════════════════════════════════════════════

    static Dictionary<string, CompositionInfo> BuildAllCompositions()
    {
        var d = new Dictionary<string, CompositionInfo>();

        // ── Iron chain ────────────────────────────────────────────────────────
        d["Charcoal"]          = Comp($"{CompRoot}/Iron/Charcoal.asset",
            "Charcoal",
            ("Carbon",           90f),
            ("Ash",               8f),
            ("Water",             2f));

        d["Iron Concentrate"]  = Comp($"{CompRoot}/Iron/Iron Concentrate.asset",
            "Iron Concentrate",
            ("Iron Oxide",       93f),
            ("Silicon Dioxide",   5f),
            ("Aluminum Oxide",    2f));

        d["Pig Iron"]          = Comp($"{CompRoot}/Iron/Pig Iron.asset",
            "Pig Iron",
            ("Iron",             96f),
            ("Carbon",            3f),
            ("Slag",              1f));

        d["Iron Plate"]        = Comp($"{CompRoot}/Iron/Iron Plate.asset",
            "Iron Plate",
            ("Iron",             99f),
            ("Carbon",            1f));

        d["Iron Component"]    = Comp($"{CompRoot}/Iron/Iron Component.asset",
            "Iron Component",
            ("Iron",             98f),
            ("Carbon",            2f));

        d["Copper Concentrate"] = Comp($"{CompRoot}/Iron/Copper Concentrate.asset",
            "Copper Concentrate",
            ("Copper Oxide",     90f),
            ("Silicon Dioxide",   8f),
            ("Aluminum Oxide",    2f));

        d["Copper Ingot"]      = Comp($"{CompRoot}/Iron/Copper Ingot.asset",
            "Copper Ingot",
            ("Copper",           99f),
            ("Slag",              1f));

        // ── Terrain ore harvesting (voxel harvest yields these) ───────────────
        // Iron-Rich Stone: magnetite/limonite bands in Olympic greywacke
        // 28-38% Iron Oxide (Fe2O3/Fe3O4), rest silicate matrix
        d["Iron-Rich Stone"]    = Comp($"{CompRoot}/Iron/Iron-Rich Stone.asset",
            "Iron-Rich Stone",
            ("Iron Oxide",       33f),
            ("Silicon Dioxide",  47f),
            ("Aluminium Oxide",  11f),
            ("Manganese Oxide",   2f));

        // Copper-Rich Stone: chalcopyrite in Crescent Formation basalt (Olympic Peninsula)
        // 2-5% Copper Oxide (CuO) — high-grade visible vein; rest basalt matrix
        d["Copper-Rich Stone"]  = Comp($"{CompRoot}/Iron/Copper-Rich Stone.asset",
            "Copper-Rich Stone",
            ("Copper Oxide",      3.5f),
            ("Silicon Dioxide",  53f),
            ("Iron Oxide",       11f),
            ("Aluminium Oxide",  10f),
            ("Calcium Oxide",     6f));

        // ── Biofuel fractions ─────────────────────────────────────────────────
        d["Cellulose Pulp"]    = Comp($"{CompRoot}/Biofuel/Cellulose Pulp.asset",
            "Cellulose Pulp",
            ("Cellulose",        97f),
            ("Water",             3f));

        d["Hemicellulose Fraction"] = Comp($"{CompRoot}/Biofuel/Hemicellulose Fraction.asset",
            "Hemicellulose Fraction",
            ("Hemicellulose",    95f),
            ("Water",             5f));

        d["Lignin Fraction"]   = Comp($"{CompRoot}/Biofuel/Lignin Fraction.asset",
            "Lignin Fraction",
            ("Lignin",           97f),
            ("Water",             3f));

        // ── Liquid fuels ──────────────────────────────────────────────────────
        // Ethanol 96% / water 4% azeotrope (as produced by distillation)
        d["Ethanol"]           = Comp($"{CompRoot}/Biofuel/Ethanol.asset",
            "Ethanol",
            ("Ethanol",          96f),
            ("Water",             4f));

        // Furfural distilled from acid hydrolysis
        d["Furfural"]          = Comp($"{CompRoot}/Biofuel/Furfural.asset",
            "Furfural",
            ("Furfural",         98f),
            ("Water",             2f));

        // Pyrolysis bio-oil (high water + oxygenated phenolics)
        d["Bio-oil"]           = Comp($"{CompRoot}/Biofuel/Bio-oil.asset",
            "Bio-oil",
            ("Bio-oil",          78f),
            ("Water",            20f),
            ("Biochar",           2f));

        // Biochar from pyrolysis
        d["Biochar"]           = Comp($"{CompRoot}/Biofuel/Biochar.asset",
            "Biochar",
            ("Biochar",          85f),
            ("Ash",              15f));

        // Syngas (game abstraction: compressed gas container)
        d["Syngas"]            = Comp($"{CompRoot}/Biofuel/Syngas.asset",
            "Syngas",
            ("Syngas",          100f));

        // HDO biodiesel (highest quality, fossil-diesel grade)
        d["Biodiesel (HDO)"]   = Comp($"{CompRoot}/Biofuel/Biodiesel (HDO).asset",
            "Biodiesel (HDO)",
            ("Biodiesel (HDO)",  99f),
            ("Water",             1f));

        // FAME biodiesel from transesterification
        d["Biodiesel"]         = Comp($"{CompRoot}/Biofuel/Biodiesel.asset",
            "Biodiesel",
            ("Biodiesel",        98f),
            ("Glycerol",          2f));

        // Glycerol byproduct
        d["Glycerol"]          = Comp($"{CompRoot}/Biofuel/Glycerol.asset",
            "Glycerol",
            ("Glycerol",        100f));

        // Methanol (reagent for transesterification; also a fuel)
        d["Methanol"]          = Comp($"{CompRoot}/Biofuel/Methanol.asset",
            "Methanol",
            ("Methanol",        100f));

        // HTL biocrude (moderate quality from wet biomass)
        d["Biocrude"]          = Comp($"{CompRoot}/Biofuel/Biocrude.asset",
            "Biocrude",
            ("Biocrude",         88f),
            ("Water",            12f));

        // Plant oil / lipids (from nut/seed harvests)
        d["Plant Oil"]         = Comp($"{CompRoot}/Biofuel/Plant Oil.asset",
            "Plant Oil",
            ("Lipid",           100f));

        // Humin (acid hydrolysis insoluble residue — low value solid)
        d["Humin"]             = Comp($"{CompRoot}/Biofuel/Humin.asset",
            "Humin",
            ("Humin",           100f));

        // Press Cake (oil-press residue — mostly fibre, still useful as SteamGenerator fuel)
        d["Press Cake"]        = Comp($"{CompRoot}/Biofuel/Press Cake.asset",
            "Press Cake",
            ("Cellulose",       60f),
            ("Hemicellulose",   20f),
            ("Lignin",          15f),
            ("Water",            5f));

        // Wood Spirit (crude methanol from destructive distillation; ~78% methanol)
        d["Wood Spirit"]       = Comp($"{CompRoot}/Biofuel/Wood Spirit.asset",
            "Wood Spirit",
            ("Methanol",        78f),
            ("Water",           20f),
            ("Acetic Acid",      2f));

        // ── Tree seed compositions ────────────────────────────────────────────
        // Douglas-fir seeds: ~18% lipid, 15% protein, 35% starch, 12% cellulose
        // Source: Pacific Northwest conifer seed nutritional data
        d["Douglas-fir Seeds"] = Comp($"{CompRoot}/Seeds/Douglas-fir Seeds.asset",
            "Douglas-fir Seeds",
            ("Lipid",           18f),
            ("Protiens",        15f),
            ("Starch",          35f),
            ("Cellulose",       12f),
            ("Hemicellulose",    8f),
            ("Water",           12f));

        // Bigleaf maple samaras: ~8% lipid — lower oil content but abundantly produced
        d["Maple Seeds"]       = Comp($"{CompRoot}/Seeds/Maple Seeds.asset",
            "Maple Seeds",
            ("Lipid",            8f),
            ("Protiens",        12f),
            ("Starch",          50f),
            ("Cellulose",       15f),
            ("Hemicellulose",    8f),
            ("Water",            7f));

        // Generic conifer seeds (cedar, hemlock, fir): moderate oil content
        d["Conifer Seeds"]     = Comp($"{CompRoot}/Seeds/Conifer Seeds.asset",
            "Conifer Seeds",
            ("Lipid",           12f),
            ("Protiens",        12f),
            ("Starch",          40f),
            ("Cellulose",       15f),
            ("Hemicellulose",    8f),
            ("Water",           13f));

        return d;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // IRON CHAIN RECIPES
    // ═════════════════════════════════════════════════════════════════════════

    static void BuildIronRecipes(Dictionary<string, CompositionInfo> c)
    {
        // ── 1. Magnetic Separation ────────────────────────────────────────────
        // Iron-Rich Stone (Iron Oxide 34%) → Iron Concentrate
        // Consumes Iron Oxide from stone; leftover silicon/aluminium = tailings in storage
        Recipe($"{RecipeRoot}/Iron/Magnetic Separation Iron.asset",
            "Magnetic Separation (Iron)", MachineType.MagneticSeparator,
            watts: 200f, seconds: 20f,
            inputs: new[] { In("Iron Oxide", reqMin: 20f, consume: 20f) },
            outputs: new[] { Out(c["Iron Concentrate"], mass: 22f, yield: 0.95f) });

        // ── 2. Magnetic Separation (Copper) ───────────────────────────────────
        // Copper-Rich Stone: Copper Oxide does NOT respond to magnetic separation.
        // Instead, Copper Ore → Gravity Separation → Copper Concentrate.
        // We model this as a "Crusher" (physical separation) for simplicity.
        Recipe($"{RecipeRoot}/Iron/Gravity Separation Copper.asset",
            "Gravity Separation (Copper)", MachineType.Crusher,
            watts: 400f, seconds: 25f,
            inputs: new[] { In("Copper Oxide", reqMin: 20f, consume: 20f) },
            outputs: new[] { Out(c["Copper Concentrate"], mass: 22f, yield: 0.90f) });

        // ── 3. Smelt Iron ─────────────────────────────────────────────────────
        // Fe2O3 + 3C → 2Fe + 3CO2
        // 160g Fe2O3 + 36g C → 112g Fe + 132g CO2 (stoichiometric)
        // Game: 20g Iron Oxide + 8g Carbon → 12g Pig Iron (70% mass yield as Fe)
        Recipe($"{RecipeRoot}/Iron/Smelt Iron.asset",
            "Smelt Iron", MachineType.Smelter,
            watts: 1000f, seconds: 120f,
            inputs: new[]
            {
                In("Iron Oxide", reqMin: 20f, consume: 20f),
                In("Carbon",     reqMin:  8f, consume:  8f)
            },
            outputs: new[] { Out(c["Pig Iron"], mass: 12f, yield: 1.0f) });

        // ── 4. Smelt Copper ───────────────────────────────────────────────────
        // CuO + C → Cu + CO2  (at ~1000-1100°C)
        // Game: 20g Copper Oxide + 6g Carbon → 13g Copper Ingot
        Recipe($"{RecipeRoot}/Iron/Smelt Copper.asset",
            "Smelt Copper", MachineType.Smelter,
            watts: 1000f, seconds: 90f,
            inputs: new[]
            {
                In("Copper Oxide", reqMin: 20f, consume: 20f),
                In("Carbon",       reqMin:  6f, consume:  6f)
            },
            outputs: new[] { Out(c["Copper Ingot"], mass: 13f, yield: 1.0f) });

        // ── 5. Forge Iron Plate ───────────────────────────────────────────────
        // Pig Iron → plastic deformation at ~1100°C → Iron Plate
        // Game: 10g Iron (from pig iron) → 10g Iron Plate
        Recipe($"{RecipeRoot}/Iron/Forge Iron Plate.asset",
            "Forge Iron Plate", MachineType.Smelter,
            watts: 800f, seconds: 60f,
            inputs: new[] { In("Iron", reqMin: 10f, consume: 10f) },
            outputs: new[] { Out(c["Iron Plate"], mass: 10f, yield: 0.98f) });

        // ── 6. Forge Iron Component ───────────────────────────────────────────
        Recipe($"{RecipeRoot}/Iron/Forge Iron Component.asset",
            "Forge Iron Component", MachineType.Smelter,
            watts: 800f, seconds: 75f,
            inputs: new[] { In("Iron", reqMin: 15f, consume: 15f) },
            outputs: new[] { Out(c["Iron Component"], mass: 14f, yield: 0.95f) });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BIOFUEL CHAIN RECIPES
    // ═════════════════════════════════════════════════════════════════════════

    static void BuildBiofuelRecipes(Dictionary<string, CompositionInfo> c)
    {
        // ── 1. Kiln-dry biomass (water removal) ───────────────────────────────
        // Removes water from wet wood for use in gasifier/pyrolyzer.
        // No solid output — water is evaporated; remaining molecules stay in input storage.
        Recipe($"{RecipeRoot}/Biofuel/Kiln Dry Wood.asset",
            "Kiln Dry Wood", MachineType.Kiln,
            watts: 500f, seconds: 60f,
            inputs: new[] { In("Water", reqMin: 20f, consume: 40f) },
            outputs: null);

        // ── 2. Ferment Cellulose → Ethanol ────────────────────────────────────
        // C6H10O5 + H2O → 2 C2H5OH + 2 CO2
        // Game: 100g Cellulose + 50g Water → 25g Ethanol (simplified, distillation included)
        // Real yield: ~25g per 100g cellulose (50% theoretical × 88% fermentation efficiency × distillation loss)
        Recipe($"{RecipeRoot}/Biofuel/Ferment Cellulose.asset",
            "Ferment Cellulose → Ethanol", MachineType.FermentationVessel,
            watts: 100f, seconds: 180f,
            inputs: new[]
            {
                In("Cellulose", reqMin: 100f, consume: 100f),
                In("Water",     reqMin:  50f, consume:  50f)
            },
            outputs: new[] { Out(c["Ethanol"], mass: 25f, yield: 1.0f) });

        // ── 3. Acid Hydrolyze Hemicellulose → Furfural ────────────────────────
        // C5H10O5 → C5H4O2 + 3H2O   (170-200°C, dilute H2SO4)
        // Game: 100g Hemicellulose → 58g Furfural + 12g Humin (solid residue)
        Recipe($"{RecipeRoot}/Biofuel/Acid Hydrolyze Hemicellulose.asset",
            "Acid Hydrolyze Hemicellulose → Furfural", MachineType.AcidHydrolysisReactor,
            watts: 600f, seconds: 60f,
            inputs: new[] { In("Hemicellulose", reqMin: 100f, consume: 100f) },
            outputs: new[]
            {
                Out(c["Furfural"], mass: 58f, yield: 1.0f),
                Out(c["Humin"],    mass: 12f, yield: 1.0f)
            });

        // ── 4. Pyrolyze Lignin → Bio-oil + Biochar ───────────────────────────
        // Fast pyrolysis 400-600°C, no oxygen
        // Game: 100g Lignin → 50g Bio-oil + 25g Biochar (+ ~20g syngas lost, ~5g water vapor)
        Recipe($"{RecipeRoot}/Biofuel/Pyrolyze Lignin.asset",
            "Pyrolyze Lignin → Bio-oil + Biochar", MachineType.Pyrolyzer,
            watts: 800f, seconds: 45f,
            inputs: new[] { In("Lignin", reqMin: 100f, consume: 100f) },
            outputs: new[]
            {
                Out(c["Bio-oil"], mass: 50f, yield: 1.0f),
                Out(c["Biochar"], mass: 25f, yield: 1.0f)
            });

        // ── 5. Pyrolyze Any Dry Biomass (Cellulose-led) ───────────────────────
        // Whole dry biomass into bio-oil + char + syngas
        // Game: 70g Cellulose + 30g Lignin → 45g Bio-oil + 20g Biochar + 20g Syngas
        Recipe($"{RecipeRoot}/Biofuel/Pyrolyze Dry Biomass.asset",
            "Pyrolyze Dry Biomass", MachineType.Pyrolyzer,
            watts: 800f, seconds: 50f,
            inputs: new[]
            {
                In("Cellulose", reqMin: 70f, consume: 70f),
                In("Lignin",    reqMin: 30f, consume: 30f)
            },
            outputs: new[]
            {
                Out(c["Bio-oil"], mass: 45f, yield: 1.0f),
                Out(c["Biochar"], mass: 20f, yield: 1.0f),
                Out(c["Syngas"],  mass: 20f, yield: 1.0f)
            });

        // ── 6. HDO Bio-oil → Biodiesel (HDO grade) ───────────────────────────
        // Bio-oil + H2 → deoxygenated hydrocarbons + H2O (300-400°C, 50-150 bar)
        // Water as in-situ H2 proxy (game simplification for early chain)
        // Game: 100g Bio-oil + 30g Water → 75g Biodiesel (HDO) + water condensate removed
        Recipe($"{RecipeRoot}/Biofuel/HDO Bio-oil.asset",
            "Hydrodeoxygenate Bio-oil → Biodiesel", MachineType.HDOReactor,
            watts: 1200f, seconds: 90f,
            inputs: new[]
            {
                In("Bio-oil", reqMin: 100f, consume: 100f),
                In("Water",   reqMin:  30f, consume:  30f)
            },
            outputs: new[] { Out(c["Biodiesel (HDO)"], mass: 75f, yield: 1.0f) });

        // ── 7. Gasify Dry Biomass → Syngas + Char ────────────────────────────
        // Partial oxidation/steam at 700-1000°C
        // Game: 70g Cellulose + 30g Lignin → 75g Syngas + 20g Biochar
        Recipe($"{RecipeRoot}/Biofuel/Gasify Biomass.asset",
            "Gasify Dry Biomass → Syngas", MachineType.Gasifier,
            watts: 600f, seconds: 30f,
            inputs: new[]
            {
                In("Cellulose", reqMin: 70f, consume: 70f),
                In("Lignin",    reqMin: 30f, consume: 30f)
            },
            outputs: new[]
            {
                Out(c["Syngas"],  mass: 75f, yield: 1.0f),
                Out(c["Biochar"], mass: 20f, yield: 1.0f)
            });

        // ── 8. Transesterify Plant Oil → FAME Biodiesel ───────────────────────
        // Triglyceride + 3 CH3OH → 3 FAME + Glycerol   (55°C, NaOH catalyst)
        // Game: 100g Lipid + 11g Methanol → 98g Biodiesel + 10g Glycerol
        Recipe($"{RecipeRoot}/Biofuel/Transesterify Plant Oil.asset",
            "Transesterify Plant Oil → Biodiesel", MachineType.TransesterificationUnit,
            watts: 300f, seconds: 60f,
            inputs: new[]
            {
                In("Lipid",    reqMin: 100f, consume: 100f),
                In("Methanol", reqMin:  11f, consume:  11f)
            },
            outputs: new[]
            {
                Out(c["Biodiesel"], mass: 98f, yield: 1.0f),
                Out(c["Glycerol"],  mass: 10f, yield: 1.0f)
            });

        // ── 9. Hydrothermal Liquefaction (HTL) ───────────────────────────────
        // Wet biomass (no drying needed!) 300-350°C, 10-20 MPa → biocrude
        // Key advantage: works on WET feedstock — no Kiln step needed
        // Game: 100g Cellulose + 100g Hemicellulose (wet biomass molecules) + 200g Water → 30g Biocrude
        Recipe($"{RecipeRoot}/Biofuel/Hydrothermal Liquefaction.asset",
            "Hydrothermal Liquefaction (HTL) → Biocrude", MachineType.Smelter,
            watts: 1500f, seconds: 120f,
            inputs: new[]
            {
                In("Cellulose",     reqMin:  60f, consume:  60f),
                In("Hemicellulose", reqMin:  40f, consume:  40f),
                In("Water",         reqMin: 150f, consume: 150f)
            },
            outputs: new[] { Out(c["Biocrude"], mass: 30f, yield: 1.0f) });
        // HTL uses Smelter type (high pressure/temp) until dedicated HTL machine is built
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ═════════════════════════════════════════════════════════════════════════
    // KILN RECIPES (Charcoal + Methanol production)
    // ═════════════════════════════════════════════════════════════════════════

    static void BuildKilnRecipes(Dictionary<string, CompositionInfo> c)
    {
        // Kiln Dry Wood already exists in Biofuel subfolder — do not recreate here.

        // ── 1. Char Biomass → Charcoal + Wood Spirit ─────────────────────────
        // Destructive distillation at 300-400°C with limited oxygen.
        // Historically this was how charcoal was made AND where "wood alcohol" (methanol)
        // was first discovered — condensed from the smoke into wood vinegar.
        //
        // Real yield per 100g dry wood:
        //   ~25-30g charcoal (Carbon 85%, Ash 15%)
        //   ~2-3g methanol (as dilute "wood spirit")
        //   ~5-10g pyroligneous acid (water + acetic acid + methanol), rest is CO2 + CO gas
        //
        // Game (simplified): 70g Cellulose + 30g Lignin → 35g Charcoal + 4g Wood Spirit
        Recipe($"{RecipeRoot}/Kiln/Char Biomass.asset",
            "Char Biomass → Charcoal + Wood Spirit", MachineType.Kiln,
            watts: 500f, seconds: 90f,
            inputs: new[]
            {
                In("Cellulose", reqMin: 50f, consume: 70f),
                In("Lignin",    reqMin: 15f, consume: 30f),
            },
            outputs: new[]
            {
                Out(c["Charcoal"],     mass: 35f, yield: 1.0f),
                Out(c["Wood Spirit"],  mass:  4f, yield: 1.0f),
            });
    }

    static CompositionInfo Comp(string path, string itemName,
        params (string resource, float pct)[] entries)
    {
        var existing = AssetDatabase.LoadAssetAtPath<CompositionInfo>(path);
        if (existing != null) return existing;

        var asset = ScriptableObject.CreateInstance<CompositionInfo>();
        asset.itemName = itemName;
        foreach (var (res, pct) in entries)
            asset.composition.Add(new Composition { resource = res, percentage = pct });

        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    static RecipeInput In(string resource, float reqMin, float consume)
        => new RecipeInput { resourceName = resource, requiredMassGrams = reqMin, consumedMassGrams = consume };

    static RecipeOutput Out(CompositionInfo comp, float mass, float yield)
        => new RecipeOutput { composition = comp, massGrams = mass, yieldFraction = yield };

    static void Recipe(string path, string name, MachineType machine,
        float watts, float seconds,
        RecipeInput[]  inputs,
        RecipeOutput[] outputs)
    {
        if (AssetDatabase.LoadAssetAtPath<ProcessingRecipe>(path) != null) return;

        var r = ScriptableObject.CreateInstance<ProcessingRecipe>();
        r.recipeName       = name;
        r.requiredMachine  = machine;
        r.requiredWatts    = watts;
        r.processingTimeSeconds = seconds;

        if (inputs != null)
            foreach (var i in inputs)
                r.inputs.Add(i);

        if (outputs != null)
            foreach (var o in outputs)
                r.outputs.Add(o);

        AssetDatabase.CreateAsset(r, path);
    }
}
#endif
