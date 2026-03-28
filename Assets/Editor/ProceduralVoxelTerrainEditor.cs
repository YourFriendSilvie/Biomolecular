using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ProceduralVoxelTerrain))]
public class ProceduralVoxelTerrainEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProceduralVoxelTerrain voxelTerrain = (ProceduralVoxelTerrain)target;
        if (voxelTerrain == null)
        {
            return;
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Olympic Rainforest Voxel Preset"))
        {
            voxelTerrain.ApplyOlympicRainforestPreset();
            MarkDirty(voxelTerrain);
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Voxel Terrain"))
            {
                voxelTerrain.GenerateTerrain(voxelTerrain.ClearExistingBeforeGenerate);
                MarkDirty(voxelTerrain);
            }

            if (GUILayout.Button("Clear Voxel Terrain"))
            {
                voxelTerrain.ClearGeneratedTerrain();
                MarkDirty(voxelTerrain);
            }
        }

        ProceduralVoxelTerrainWaterSystem waterSystem = voxelTerrain.GetComponent<ProceduralVoxelTerrainWaterSystem>();
        ProceduralVoxelTerrainScatterer scatterer = voxelTerrain.GetComponent<ProceduralVoxelTerrainScatterer>();
        ProceduralVoxelStartAreaSystem startAreaSystem = voxelTerrain.GetComponent<ProceduralVoxelStartAreaSystem>();
        if ((waterSystem != null || scatterer != null || startAreaSystem != null) && GUILayout.Button("Generate Voxel Terrain + Water + Scatter + Start Area"))
        {
            voxelTerrain.GenerateTerrain(voxelTerrain.ClearExistingBeforeGenerate);
            MarkDirty(voxelTerrain);

            if (waterSystem != null)
            {
                waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
                MarkDirty(waterSystem);
            }

            if (scatterer != null)
            {
                scatterer.GenerateScatter(scatterer.ClearExistingBeforeGenerate);
                MarkDirty(scatterer);
            }

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
