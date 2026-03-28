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

        if (scatterer.IsScatterGenerationInProgress)
        {
            EditorGUILayout.HelpBox(
                $"{scatterer.ScatterGenerationStatus} ({Mathf.RoundToInt(scatterer.ScatterGenerationProgress01 * 100f)}%)",
                MessageType.Info);
            Repaint();
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(scatterer.IsScatterGenerationInProgress))
        {
            if (GUILayout.Button("Apply Olympic Rainforest Scatter Preset"))
            {
                scatterer.ApplyOlympicRainforestPreset();
                MarkDirty(scatterer);
            }
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(scatterer.IsScatterGenerationInProgress))
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
        }

        ProceduralVoxelTerrain terrain = scatterer.GetComponent<ProceduralVoxelTerrain>();
        ProceduralVoxelTerrainWaterSystem waterSystem = scatterer.GetComponent<ProceduralVoxelTerrainWaterSystem>();
        ProceduralVoxelStartAreaSystem startAreaSystem = scatterer.GetComponent<ProceduralVoxelStartAreaSystem>();
        if ((terrain != null || waterSystem != null || startAreaSystem != null) &&
            !(terrain != null && terrain.IsTerrainGenerationInProgress) &&
            !scatterer.IsScatterGenerationInProgress &&
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
