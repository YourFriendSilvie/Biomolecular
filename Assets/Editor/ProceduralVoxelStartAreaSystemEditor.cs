using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ProceduralVoxelStartAreaSystem))]
public class ProceduralVoxelStartAreaSystemEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProceduralVoxelStartAreaSystem startAreaSystem = (ProceduralVoxelStartAreaSystem)target;
        if (startAreaSystem == null)
        {
            return;
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Coastal Rainforest Start Preset"))
        {
            startAreaSystem.ApplyCoastalRainforestStartPreset();
            MarkDirty(startAreaSystem);
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Start Area"))
            {
                startAreaSystem.GenerateStartArea(false);
                MarkDirty(startAreaSystem);
            }

            if (GUILayout.Button("Clear Start Area"))
            {
                startAreaSystem.ClearGeneratedStartArea();
                MarkDirty(startAreaSystem);
            }
        }

        ProceduralVoxelTerrain terrain = startAreaSystem.GetComponent<ProceduralVoxelTerrain>();
        ProceduralVoxelTerrainWaterSystem waterSystem = startAreaSystem.GetComponent<ProceduralVoxelTerrainWaterSystem>();
        ProceduralVoxelTerrainScatterer scatterer = startAreaSystem.GetComponent<ProceduralVoxelTerrainScatterer>();
        if ((terrain != null || waterSystem != null || scatterer != null) &&
            !(terrain != null && terrain.IsTerrainGenerationInProgress) &&
            GUILayout.Button("Generate Voxel Terrain + Water + Scatter + Start Area"))
        {
            VoxelTerrainEditorGenerationUtility.GenerateTerrainWaterScatterStartArea(
                terrain,
                waterSystem,
                scatterer,
                startAreaSystem);
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
