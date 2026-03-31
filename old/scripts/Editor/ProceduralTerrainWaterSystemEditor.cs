using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ProceduralTerrainWaterSystem))]
public class ProceduralTerrainWaterSystemEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProceduralTerrainWaterSystem waterSystem = (ProceduralTerrainWaterSystem)target;
        if (waterSystem == null)
        {
            return;
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Coastal Water Preset"))
        {
            waterSystem.ApplyCoastalRainforestWaterPreset();
            MarkDirty(waterSystem);
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Water"))
            {
                waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
                MarkDirty(waterSystem);
            }

            if (GUILayout.Button("Clear Water"))
            {
                waterSystem.ClearGeneratedWater();
                MarkDirty(waterSystem);
            }
        }

        ProceduralTerrainGenerator terrainGenerator = waterSystem.GetComponent<ProceduralTerrainGenerator>();
        ProceduralTerrainMineralSystem mineralSystem = waterSystem.GetComponent<ProceduralTerrainMineralSystem>();
        ProceduralTerrainScatterer scatterer = waterSystem.GetComponent<ProceduralTerrainScatterer>();
        if ((terrainGenerator != null || mineralSystem != null || scatterer != null) && GUILayout.Button("Generate Terrain + Water + Minerals + Scatter"))
        {
            if (terrainGenerator != null)
            {
                terrainGenerator.GenerateTerrain();
                MarkDirty(terrainGenerator);
            }

            waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
            MarkDirty(waterSystem);

            if (mineralSystem != null)
            {
                mineralSystem.GenerateMinerals(mineralSystem.ClearExistingBeforeGenerate);
                MarkDirty(mineralSystem);
            }

            if (scatterer != null)
            {
                scatterer.GenerateScatter(scatterer.ClearExistingBeforeGenerate);
                MarkDirty(scatterer);
            }
        }
    }

    private static void MarkDirty(Component component)
    {
        if (component == null)
        {
            return;
        }

        EditorUtility.SetDirty(component);
        if (component.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
