using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ProceduralTerrainGenerator))]
public class ProceduralTerrainGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProceduralTerrainGenerator terrainGenerator = (ProceduralTerrainGenerator)target;
        if (terrainGenerator == null)
        {
            return;
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Olympic Rainforest Preset"))
        {
            terrainGenerator.ApplyOlympicRainforestPreset();
            MarkDirty(terrainGenerator);
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Terrain"))
            {
                terrainGenerator.GenerateTerrain();
                MarkDirty(terrainGenerator);
            }

            if (GUILayout.Button("Flatten Terrain"))
            {
                terrainGenerator.FlattenTerrain();
                MarkDirty(terrainGenerator);
            }
        }

        ProceduralTerrainWaterSystem waterSystem = terrainGenerator.GetComponent<ProceduralTerrainWaterSystem>();
        ProceduralTerrainMineralSystem mineralSystem = terrainGenerator.GetComponent<ProceduralTerrainMineralSystem>();
        ProceduralTerrainScatterer scatterer = terrainGenerator.GetComponent<ProceduralTerrainScatterer>();
        if (waterSystem != null && GUILayout.Button("Generate Terrain + Water"))
        {
            terrainGenerator.GenerateTerrain();
            waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
            MarkDirty(terrainGenerator);
            MarkDirty(waterSystem);
        }

        if ((scatterer != null || waterSystem != null || mineralSystem != null) && GUILayout.Button("Generate Terrain + Water + Minerals + Scatter"))
        {
            terrainGenerator.GenerateTerrain();
            if (waterSystem != null)
            {
                waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
                MarkDirty(waterSystem);
            }

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

            MarkDirty(terrainGenerator);
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
