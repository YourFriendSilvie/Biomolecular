using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ProceduralTerrainMineralSystem))]
public class ProceduralTerrainMineralSystemEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProceduralTerrainMineralSystem mineralSystem = (ProceduralTerrainMineralSystem)target;
        if (mineralSystem == null)
        {
            return;
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Olympic Rainforest Mineral Preset"))
        {
            mineralSystem.ApplyOlympicRainforestPreset();
            MarkDirty(mineralSystem);
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Minerals"))
            {
                mineralSystem.GenerateMinerals(mineralSystem.ClearExistingBeforeGenerate);
                MarkDirty(mineralSystem);
            }

            if (GUILayout.Button("Clear Minerals"))
            {
                mineralSystem.ClearGeneratedMinerals();
                MarkDirty(mineralSystem);
            }
        }

        ProceduralTerrainGenerator terrainGenerator = mineralSystem.GetComponent<ProceduralTerrainGenerator>();
        ProceduralTerrainWaterSystem waterSystem = mineralSystem.GetComponent<ProceduralTerrainWaterSystem>();
        ProceduralTerrainScatterer scatterer = mineralSystem.GetComponent<ProceduralTerrainScatterer>();
        if ((terrainGenerator != null || waterSystem != null || scatterer != null) &&
            GUILayout.Button("Generate Terrain + Water + Minerals + Scatter"))
        {
            if (terrainGenerator != null)
            {
                terrainGenerator.GenerateTerrain();
                MarkDirty(terrainGenerator);
            }

            if (waterSystem != null)
            {
                waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
                MarkDirty(waterSystem);
            }

            mineralSystem.GenerateMinerals(mineralSystem.ClearExistingBeforeGenerate);
            MarkDirty(mineralSystem);

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
