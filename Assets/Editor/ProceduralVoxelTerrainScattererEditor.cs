using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ProceduralVoxelTerrainScatterer))]
public class ProceduralVoxelTerrainScattererEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProceduralVoxelTerrainScatterer scatterer = (ProceduralVoxelTerrainScatterer)target;
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
            if (GUILayout.Button("Generate Voxel Scatter"))
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

        ProceduralVoxelTerrain terrain = scatterer.GetComponent<ProceduralVoxelTerrain>();
        ProceduralVoxelTerrainWaterSystem waterSystem = scatterer.GetComponent<ProceduralVoxelTerrainWaterSystem>();
        ProceduralVoxelStartAreaSystem startAreaSystem = scatterer.GetComponent<ProceduralVoxelStartAreaSystem>();
        if ((terrain != null || waterSystem != null || startAreaSystem != null) && GUILayout.Button("Generate Voxel Terrain + Water + Scatter + Start Area"))
        {
            if (terrain != null)
            {
                terrain.GenerateTerrain(terrain.ClearExistingBeforeGenerate);
                MarkDirty(terrain);
            }

            if (waterSystem != null)
            {
                waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
                MarkDirty(waterSystem);
            }

            scatterer.GenerateScatter(scatterer.ClearExistingBeforeGenerate);
            MarkDirty(scatterer);

            if (startAreaSystem != null)
            {
                startAreaSystem.GenerateStartArea(false);
                MarkDirty(startAreaSystem);
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
