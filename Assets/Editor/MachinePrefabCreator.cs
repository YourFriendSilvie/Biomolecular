#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Creates all machine prefabs, the CrashedShip prefab, the TopDownCamera prefab,
/// and a BuildingSystem prefab — all using primitive shapes as placeholders.
///
/// Run via: Biomolecular → Setup → Create Machine Prefabs
///
/// After running:
///   1. Assign inputStorage / outputStorage in each machine prefab's Inspector.
///   2. Assign ProcessingRecipe assets to each machine's availableRecipes list.
///   3. Add TopDownCamera and BuildingSystem prefabs to your scene.
///   4. Wire mainFollowCamera + topDownCamera on BuildModeController.
///   5. Assign CrashedShip prefab to ProceduralVoxelStartAreaSystem.shipPrefab.
/// </summary>
public static class MachinePrefabCreator
{
    const string MachineRoot   = "Assets/Prefabs/Machines";
    const string MaterialRoot  = "Assets/Prefabs/Machines/Materials";

    [MenuItem("Biomolecular/Setup/Create Machine Prefabs")]
    public static void CreateAll()
    {
        EnsureFolder("Assets/Prefabs", "Machines");
        EnsureFolder(MachineRoot,      "Materials");

        // ── Machine prefabs ───────────────────────────────────────────────────
        // Build costs are in grams of molecule (Iron, Copper, etc.)
        // These represent ~2–4kg structural metal for a 2–3m industrial machine.
        CreateMachine("OreCrusher",              PrimitiveType.Cube,     new Color(0.40f, 0.28f, 0.18f),  3f, 2f, 3f, typeof(OreCrusher),              500f,  2,
            ("Iron", 2000f), ("Copper", 200f));
        CreateMachine("MagneticSeparator",       PrimitiveType.Cylinder, new Color(0.55f, 0.55f, 0.68f),  2f, 2f, 2f, typeof(MagneticSeparator),       200f,  1,
            ("Iron", 1000f), ("Copper", 500f));
        CreateMachine("Smelter",                 PrimitiveType.Cylinder, new Color(0.78f, 0.28f, 0.10f),  2f, 3f, 2f, typeof(Smelter),                1000f,  2,
            ("Iron", 3000f), ("Copper", 300f));
        CreateMachine("Kiln",                    PrimitiveType.Cylinder, new Color(0.70f, 0.50f, 0.22f),  2f, 2f, 2f, typeof(Kiln),                    500f,  1,
            ("Iron", 1500f));
        CreateMachine("BiomassFractionator",     PrimitiveType.Cube,     new Color(0.28f, 0.48f, 0.28f),  3f, 3f, 3f, typeof(BiomassFractionator),     800f,  2,
            ("Iron", 2000f), ("Copper", 400f));
        CreateMachine("FermentationVessel",      PrimitiveType.Sphere,   new Color(0.48f, 0.70f, 0.38f),  2f, 2f, 2f, typeof(FermentationVessel),      100f,  1,
            ("Iron", 800f), ("Copper", 100f));
        CreateMachine("AcidHydrolysisReactor",   PrimitiveType.Cylinder, new Color(0.78f, 0.78f, 0.18f),  2f, 3f, 2f, typeof(AcidHydrolysisReactor),   600f,  2,
            ("Iron", 2000f), ("Copper", 600f));
        CreateMachine("Pyrolyzer",               PrimitiveType.Cylinder, new Color(0.48f, 0.10f, 0.10f),  2f, 3f, 2f, typeof(Pyrolyzer),               800f,  2,
            ("Iron", 2500f), ("Copper", 200f));
        CreateMachine("HDOReactor",              PrimitiveType.Cylinder, new Color(0.18f, 0.38f, 0.60f),  2f, 3f, 2f, typeof(HDOReactor),             1200f,  2,
            ("Iron", 3000f), ("Copper", 800f));
        CreateMachine("Gasifier",                PrimitiveType.Cylinder, new Color(0.38f, 0.38f, 0.38f),  2f, 3f, 2f, typeof(Gasifier),                600f,  2,
            ("Iron", 2000f), ("Copper", 300f));
        CreateMachine("TransesterificationUnit", PrimitiveType.Cube,     new Color(0.58f, 0.48f, 0.70f),  3f, 2f, 3f, typeof(TransesterificationUnit), 300f,  2,
            ("Iron", 1200f), ("Copper", 400f));
        CreateExtractionMachinePrefab("OilPress");
        CreateExtractionMachinePrefab("SolventExtractor");
        CreateMachine("SteamGenerator",          PrimitiveType.Cube,     new Color(0.48f, 0.48f, 0.50f),  4f, 3f, 3f, typeof(SteamGenerator),         2000f,  2,
            ("Iron", 4000f), ("Copper", 600f));
        CreateMachine("LiquidFuelGenerator",     PrimitiveType.Cube,     new Color(0.60f, 0.38f, 0.10f),  3f, 2f, 3f, typeof(LiquidFuelGenerator),    5000f,  2,
            ("Iron", 3000f), ("Copper", 1000f));
        CreateMachine("Battery",                 PrimitiveType.Cube,     new Color(0.18f, 0.18f, 0.28f),  2f, 2f, 2f, typeof(Battery),                   0f,  1,
            ("Iron", 500f), ("Copper", 1200f));
        CreateMachine("ManualCraftingTable",     PrimitiveType.Cube,     new Color(0.55f, 0.40f, 0.22f),  2f, 1f, 2f, typeof(ManualCraftingTable),       0f,  2,
            ("Iron", 400f));

        CreateCrashedShipPrefab();
        CreateTopDownCameraPrefab();
        CreateBuildingSystemPrefab();
        CreateBuildingCatalog();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[MachinePrefabCreator] All prefabs created in Assets/Prefabs/Machines/.\n" +
                  "Next steps:\n" +
                  "  1. Assign inputStorage / outputStorage on each machine prefab.\n" +
                  "  2. Assign ProcessingRecipe assets to each machine's availableRecipes.\n" +
                  "  3. Add BuildingSystem prefab to your scene and wire its Inspector fields.\n" +
                  "  4. Assign CrashedShip to ProceduralVoxelStartAreaSystem.shipPrefab.");
    }

    // ── Machine prefab builder ────────────────────────────────────────────────

    static void CreateMachine(string name, PrimitiveType shape, Color color,
                               float sx, float sy, float sz,
                               System.Type machineType,
                               float watts, int footprint,
                               params (string mol, float g)[] costs)
    {
        string path = $"{MachineRoot}/{name}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        var root = new GameObject(name);

        // PlaceableBuilding metadata
        var placeable              = root.AddComponent<PlaceableBuilding>();
        placeable.buildingName     = name;
        placeable.category         = machineType == typeof(SteamGenerator)
                                     || machineType == typeof(LiquidFuelGenerator)
                                     || machineType == typeof(Battery)
                                       ? "Power"
                                       : "Machines";
        placeable.requiredWatts    = watts;
        placeable.footprintTiles   = new Vector2Int(footprint, footprint);

        // Build costs via SerializedObject so they persist into the prefab asset
        if (costs != null && costs.Length > 0)
        {
            var so       = new UnityEditor.SerializedObject(placeable);
            var costProp = so.FindProperty("buildCost");
            costProp.arraySize = costs.Length;
            for (int i = 0; i < costs.Length; i++)
            {
                var el = costProp.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("molecule").stringValue = costs[i].mol;
                el.FindPropertyRelative("massGrams").floatValue = costs[i].g;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Visual body
        var body = GameObject.CreatePrimitive(shape);
        body.name = "Body";
        body.transform.SetParent(root.transform);
        body.transform.localScale    = new Vector3(sx, sy, sz);
        body.transform.localPosition = new Vector3(0f, sy * 0.5f, 0f);

        var mat = MakeMaterial(name, color);
        body.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // Input storage child
        var inputGo = new GameObject("InputStorage");
        inputGo.transform.SetParent(root.transform);
        inputGo.AddComponent<MachineItemStorage>();

        // Output storage child
        var outputGo = new GameObject("OutputStorage");
        outputGo.transform.SetParent(root.transform);
        outputGo.AddComponent<MachineItemStorage>();

        // Add the machine MonoBehaviour (storage refs need manual wiring in Inspector)
        if (machineType != null)
            root.AddComponent(machineType);

        // Power grid (only for generators/batteries — others register through ProcessingMachine)
        // PowerGrid should already be in the scene; no extra component needed on machines.

        string prefabPath = $"{MachineRoot}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
    }

    // ── Crashed ship ──────────────────────────────────────────────────────────

    static void CreateCrashedShipPrefab()
    {
        const string path = "Assets/Prefabs/CrashedShip.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        var root = new GameObject("CrashedShip");

        // Main hull — flattened box, tilted to look crashed
        AddPart(root, "Hull",       PrimitiveType.Cube,
                new Vector3(20f, 4f, 12f),
                new Vector3(0f, 2f, 0f),
                Quaternion.Euler(0f, 15f, -8f),
                new Color(0.52f, 0.52f, 0.58f), "CrashedShip_Hull");

        // Engine nacelle (side)
        AddPart(root, "Engine",     PrimitiveType.Cylinder,
                new Vector3(3f, 4f, 3f),
                new Vector3(8f, 2f, 2f),
                Quaternion.Euler(0f, 0f, 30f),
                new Color(0.28f, 0.28f, 0.36f), "CrashedShip_Engine");

        // Secondary engine (buried in ground on the other side)
        AddPart(root, "Engine2",    PrimitiveType.Cylinder,
                new Vector3(2.5f, 3f, 2.5f),
                new Vector3(-7f, -0.5f, -2f),
                Quaternion.Euler(15f, 0f, -20f),
                new Color(0.28f, 0.28f, 0.36f), "CrashedShip_Engine2");

        // Viewport dome
        AddPart(root, "Viewport",   PrimitiveType.Sphere,
                new Vector3(4f, 2f, 4f),
                new Vector3(-2f, 5.5f, 0f),
                Quaternion.identity,
                new Color(0.35f, 0.55f, 0.75f), "CrashedShip_Viewport");

        // Debris chunk (chunk of hull broken off)
        AddPart(root, "DebrisA",    PrimitiveType.Cube,
                new Vector3(4f, 1f, 3f),
                new Vector3(11f, 0.5f, -4f),
                Quaternion.Euler(0f, 40f, 10f),
                new Color(0.45f, 0.45f, 0.50f), "CrashedShip_Debris");

        // ── Interactable workstations ─────────────────────────────────────────
        // Three salvage stations built from repurposed ship components.
        // Each has ShipManualMachine + MachineItemStorage children + BoxCollider.
        // Recipes are wired by MachinePrefabWiring after DefaultAssetsCreator runs.

        AddShipWorkstation(root, "SalvageCrusher",
            "Salvage Crusher",
            "The ship's main thruster, repurposed as a crude rock crusher. It won't last long.",
            position: new Vector3(8f, 2f, 2f),   // near Engine nacelle
            color: new Color(0.36f, 0.24f, 0.14f));

        AddShipWorkstation(root, "SalvageSeparator",
            "Salvage Separator",
            "The tractor-beam emitter, still generating a magnetic field strong enough to separate iron.",
            position: new Vector3(0f, 3f, -5f),  // side of hull
            color: new Color(0.24f, 0.34f, 0.46f));

        AddShipWorkstation(root, "SalvageFurnace",
            "Salvage Furnace",
            "The broken secondary thruster still burns hot enough to smelt iron.",
            position: new Vector3(-7f, 1f, 3f),  // near Engine2
            color: new Color(0.58f, 0.22f, 0.10f));

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    // ── Ship workstation helper ───────────────────────────────────────────────

    static void AddShipWorkstation(GameObject shipRoot, string goName, string stationName,
                                   string flavour, Vector3 position, Color color)
    {
        var ws = new GameObject(goName);
        ws.transform.SetParent(shipRoot.transform, false);
        ws.transform.localPosition = position;

        // Visual indicator — small glowing-ish cube
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(ws.transform, false);
        body.transform.localScale    = new Vector3(1.5f, 1.5f, 1.5f);
        body.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        body.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(goName, color);
        // Remove auto-added collider — we add our own trigger below
        Object.DestroyImmediate(body.GetComponent<BoxCollider>());

        // Trigger collider for PlayerInteraction raycast
        var col = ws.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.75f, 0f);
        col.size   = new Vector3(2f, 2f, 2f);

        // Storage children
        var inputGo  = new GameObject("InputStorage");
        inputGo.transform.SetParent(ws.transform, false);
        inputGo.AddComponent<MachineItemStorage>();

        var outputGo = new GameObject("OutputStorage");
        outputGo.transform.SetParent(ws.transform, false);
        outputGo.AddComponent<MachineItemStorage>();

        // ShipManualMachine component — wire via SerializedObject
        var machine = ws.AddComponent<ShipManualMachine>();
        var so = new SerializedObject(machine);
        so.FindProperty("stationName").stringValue   = stationName;
        so.FindProperty("flavourText").stringValue   = flavour;
        so.FindProperty("maxUses").intValue          = 5;
        so.FindProperty("inputStorage").objectReferenceValue  = inputGo.GetComponent<MachineItemStorage>();
        so.FindProperty("outputStorage").objectReferenceValue = outputGo.GetComponent<MachineItemStorage>();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── Top-down camera prefab ────────────────────────────────────────────────

    static void CreateTopDownCameraPrefab()
    {
        const string path = "Assets/Prefabs/TopDownCamera.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        var go  = new GameObject("TopDownCamera");
        var cam = go.AddComponent<Camera>();
        cam.orthographic     = true;
        cam.orthographicSize = 20f;
        cam.nearClipPlane    = 1f;
        cam.farClipPlane     = 500f;
        cam.enabled          = false;  // Disabled until BuildModeController enables it

        // Ensure audio listener doesn't conflict with main camera
        go.AddComponent<AudioListener>().enabled = false;

        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    // ── BuildingSystem prefab ─────────────────────────────────────────────────

    static void CreateBuildingSystemPrefab()
    {
        const string path = "Assets/Prefabs/BuildingSystem.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

        var go = new GameObject("BuildingSystem");
        go.AddComponent<BuildingSystem>();
        go.AddComponent<BuildModeController>();

        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    // ── BuildingCatalog asset ─────────────────────────────────────────────────

    static void CreateBuildingCatalog()
    {
        const string path = "Assets/Objects/BuildingCatalog.asset";
        if (AssetDatabase.LoadAssetAtPath<BuildingCatalog>(path) != null) return;

        var cat = ScriptableObject.CreateInstance<BuildingCatalog>();

        // Populate catalog with every machine prefab we just created
        string[] machineNames =
        {
            "OreCrusher", "MagneticSeparator", "Smelter", "Kiln",
            "BiomassFractionator", "FermentationVessel", "AcidHydrolysisReactor",
            "Pyrolyzer", "HDOReactor", "Gasifier", "TransesterificationUnit",
            "OilPress", "SolventExtractor",
            "SteamGenerator", "LiquidFuelGenerator", "Battery", "ManualCraftingTable"
        };

        foreach (var n in machineNames)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MachineRoot}/{n}.prefab");
            if (prefab == null) continue;
            var pb = prefab.GetComponent<PlaceableBuilding>();
            if (pb == null) continue;
            cat.buildings.Add(new BuildingCatalog.Entry { prefab = pb });
        }

        AssetDatabase.CreateAsset(cat, path);
    }

    // ── ExtractionMachine prefab builder ─────────────────────────────────────
    // Builds OilPress and SolventExtractor prefabs with ExtractionMachine configured
    // directly via SerializedObject so no ProcessingRecipe assets are needed.

    static void CreateExtractionMachinePrefab(string machineName)
    {
        string prefabPath = $"{MachineRoot}/{machineName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) return;

        bool isOilPress = machineName == "OilPress";

        var color     = isOilPress ? new Color(0.68f, 0.52f, 0.22f) : new Color(0.30f, 0.58f, 0.52f);
        var shape     = isOilPress ? PrimitiveType.Cube : PrimitiveType.Cylinder;
        float sx = 2f, sy = isOilPress ? 2f : 3f, sz = 2f;
        float watts   = isOilPress ? 200f : 400f;
        int footprint = isOilPress ? 1 : 2;

        var root = new GameObject(machineName);

        var placeable              = root.AddComponent<PlaceableBuilding>();
        placeable.buildingName     = machineName;
        placeable.category         = "Machines";
        placeable.requiredWatts    = watts;
        placeable.footprintTiles   = new Vector2Int(footprint, footprint);

        var body = GameObject.CreatePrimitive(shape);
        body.name = "Body";
        body.transform.SetParent(root.transform);
        body.transform.localScale    = new Vector3(sx, sy, sz);
        body.transform.localPosition = new Vector3(0f, sy * 0.5f, 0f);
        body.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(machineName, color);

        var inputGo  = new GameObject("InputStorage");
        inputGo.transform.SetParent(root.transform);
        inputGo.AddComponent<MachineItemStorage>();

        var outputGo = new GameObject("OutputStorage");
        outputGo.transform.SetParent(root.transform);
        outputGo.AddComponent<MachineItemStorage>();

        var em = root.AddComponent<ExtractionMachine>();
        var so = new SerializedObject(em);

        // Wire storage references
        so.FindProperty("inputStorage").objectReferenceValue  = inputGo.GetComponent<MachineItemStorage>();
        so.FindProperty("outputStorage").objectReferenceValue = outputGo.GetComponent<MachineItemStorage>();

        // Load Plant Oil composition (created by DefaultAssetsCreator)
        const string compBio  = "Assets/Objects/Resources/Biofuel";
        var plantOil = AssetDatabase.LoadAssetAtPath<CompositionInfo>($"{compBio}/Plant Oil.asset");
        if (plantOil == null)
            Debug.LogWarning($"[MachinePrefabCreator] Plant Oil.asset not found — run 'Create Default Recipe Assets' first, then re-run this menu item.");

        // ── OilPress targets ──────────────────────────────────────────────────
        // Mechanical cold-press of oil-bearing seeds (triglycerides only).
        // Sources the "Lipid" molecule produced by tree seed harvest.
        //   100g Lipid → 90g Plant Oil (90% efficiency)
        if (isOilPress)
        {
            var targets = so.FindProperty("targets");
            targets.arraySize = 1;
            var t0 = targets.GetArrayElementAtIndex(0);
            t0.FindPropertyRelative("sourceMolecule").stringValue          = "Lipid";
            t0.FindPropertyRelative("outputComposition").objectReferenceValue = plantOil;
            t0.FindPropertyRelative("extractionEfficiency").floatValue     = 0.90f;

            so.FindProperty("minGramsToStart").floatValue          = 10f;
            so.FindProperty("batchMaxGrams").floatValue            = 100f;
            so.FindProperty("processingTimeSeconds").floatValue    = 25f;
            so.FindProperty("requiredWatts").floatValue            = 200f;
        }
        else
        {
            // ── SolventExtractor targets ──────────────────────────────────────
            // Hot-ethanol extraction of solid waxes from wood/leaf matter.
            // Uses ethanol reagent (0.47g ethanol per gram of wax extracted).
            // Processes Wood Waxes, then Cuticular Waxes, then Suberin.
            var targets = so.FindProperty("targets");
            targets.arraySize = 3;

            var t0 = targets.GetArrayElementAtIndex(0);
            t0.FindPropertyRelative("sourceMolecule").stringValue             = "Wood Waxes";
            t0.FindPropertyRelative("outputComposition").objectReferenceValue = plantOil;
            t0.FindPropertyRelative("extractionEfficiency").floatValue        = 0.87f;

            var t1 = targets.GetArrayElementAtIndex(1);
            t1.FindPropertyRelative("sourceMolecule").stringValue             = "Cuticular Waxes";
            t1.FindPropertyRelative("outputComposition").objectReferenceValue = plantOil;
            t1.FindPropertyRelative("extractionEfficiency").floatValue        = 0.83f;

            var t2 = targets.GetArrayElementAtIndex(2);
            t2.FindPropertyRelative("sourceMolecule").stringValue             = "Suberin";
            t2.FindPropertyRelative("outputComposition").objectReferenceValue = plantOil;
            t2.FindPropertyRelative("extractionEfficiency").floatValue        = 0.60f;

            so.FindProperty("reagentMolecule").stringValue                  = "Ethanol";
            so.FindProperty("reagentGramsPerGramExtracted").floatValue      = 0.47f;
            so.FindProperty("minGramsToStart").floatValue                   = 10f;
            so.FindProperty("batchMaxGrams").floatValue                     = 60f;
            so.FindProperty("processingTimeSeconds").floatValue             = 40f;
            so.FindProperty("requiredWatts").floatValue                     = 400f;
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
    }

    // ── Building catalog ──────────────────────────────────────────────────────

    // ── Helpers ───────────────────────────────────────────────────────────────

    static Material MakeMaterial(string name, Color color)
    {
        string matPath = $"{MaterialRoot}/{name}_Mat.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null) return existing;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        {
            color = color
        };
        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    static void AddPart(GameObject parent, string partName, PrimitiveType shape,
                        Vector3 scale, Vector3 localPos, Quaternion localRot,
                        Color color, string matName)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = partName;
        go.transform.SetParent(parent.transform);
        go.transform.localScale    = scale;
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(matName, color);
    }

    static void EnsureFolder(string parent, string child)
    {
        string full = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(full))
            AssetDatabase.CreateFolder(parent, child);
    }
}
#endif
