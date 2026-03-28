using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ProceduralVoxelTerrainWaterSystem))]
public class ProceduralVoxelTerrainWaterSystemEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProceduralVoxelTerrainWaterSystem waterSystem = (ProceduralVoxelTerrainWaterSystem)target;
        if (waterSystem == null)
        {
            return;
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Coastal Rainforest Water Preset"))
        {
            waterSystem.ApplyCoastalRainforestWaterPreset();
            MarkDirty(waterSystem);
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Voxel Water"))
            {
                waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
                MarkDirty(waterSystem);
            }

            if (GUILayout.Button("Clear Voxel Water"))
            {
                waterSystem.ClearGeneratedWater();
                MarkDirty(waterSystem);
            }
        }

        ProceduralVoxelTerrain terrain = waterSystem.GetComponent<ProceduralVoxelTerrain>();
        ProceduralVoxelTerrainScatterer scatterer = waterSystem.GetComponent<ProceduralVoxelTerrainScatterer>();
        ProceduralVoxelStartAreaSystem startAreaSystem = waterSystem.GetComponent<ProceduralVoxelStartAreaSystem>();
        if ((terrain != null || scatterer != null || startAreaSystem != null) && GUILayout.Button("Generate Voxel Terrain + Water + Scatter + Start Area"))
        {
            if (terrain != null)
            {
                terrain.GenerateTerrain(terrain.ClearExistingBeforeGenerate);
                MarkDirty(terrain);
            }

            waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
            MarkDirty(waterSystem);

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
