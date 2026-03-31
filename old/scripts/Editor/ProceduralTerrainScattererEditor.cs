using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ProceduralTerrainScatterer))]
public class ProceduralTerrainScattererEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProceduralTerrainScatterer scatterer = (ProceduralTerrainScatterer)target;
        if (scatterer == null)
        {
            return;
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Olympic Rainforest Scatter Preset"))
        {
            scatterer.ApplyOlympicRainforestPreset();
            MarkDirty(scatterer);
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Scatter"))
            {
                scatterer.GenerateScatter(scatterer.ClearExistingBeforeGenerate);
                MarkDirty(scatterer);
            }

            if (GUILayout.Button("Clear Generated"))
            {
                scatterer.ClearGeneratedScatter();
                MarkDirty(scatterer);
            }
        }

        ProceduralTerrainGenerator terrainGenerator = scatterer.GetComponent<ProceduralTerrainGenerator>();
        ProceduralTerrainWaterSystem waterSystem = scatterer.GetComponent<ProceduralTerrainWaterSystem>();
        ProceduralTerrainMineralSystem mineralSystem = scatterer.GetComponent<ProceduralTerrainMineralSystem>();
        if ((terrainGenerator != null || waterSystem != null || mineralSystem != null) && GUILayout.Button("Generate Terrain + Water + Minerals + Scatter"))
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

            if (mineralSystem != null)
            {
                mineralSystem.GenerateMinerals(mineralSystem.ClearExistingBeforeGenerate);
                MarkDirty(mineralSystem);
            }

            scatterer.GenerateScatter(scatterer.ClearExistingBeforeGenerate);
            MarkDirty(scatterer);
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
