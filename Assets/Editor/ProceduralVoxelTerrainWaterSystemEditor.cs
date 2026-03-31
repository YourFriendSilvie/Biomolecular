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

        DrawWaterGenerationMonitoring(waterSystem);

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Coastal Rainforest Water Preset"))
        {
            waterSystem.ApplyCoastalRainforestWaterPreset();
            MarkDirty(waterSystem);
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(waterSystem.IsWaterGenerationInProgress))
            {
                if (GUILayout.Button("Generate Voxel Water"))
                {
                    waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
                    MarkDirty(waterSystem);
                }
            }

            using (new EditorGUI.DisabledScope(waterSystem.IsWaterGenerationInProgress))
            {
                if (GUILayout.Button("Clear Voxel Water"))
                {
                    waterSystem.ClearGeneratedWater();
                    MarkDirty(waterSystem);
                }
            }
        }

        ProceduralVoxelTerrain terrain = waterSystem.GetComponent<ProceduralVoxelTerrain>();
        ProceduralVoxelTerrainScatterer scatterer = waterSystem.GetComponent<ProceduralVoxelTerrainScatterer>();
        ProceduralVoxelStartAreaSystem startAreaSystem = waterSystem.GetComponent<ProceduralVoxelStartAreaSystem>();
        if ((terrain != null || scatterer != null || startAreaSystem != null) &&
            !(terrain != null && terrain.IsTerrainGenerationInProgress) &&
            !waterSystem.IsWaterGenerationInProgress &&
            GUILayout.Button("Generate Voxel Terrain + Water + Scatter + Start Area"))
        {
            VoxelTerrainEditorGenerationUtility.GenerateTerrainWaterScatterStartArea(
                terrain,
                waterSystem,
                scatterer,
                startAreaSystem);
        }

        if (waterSystem.IsWaterGenerationInProgress)
        {
            Repaint();
        }
    }

    private static void DrawWaterGenerationMonitoring(ProceduralVoxelTerrainWaterSystem waterSystem)
    {
        if (waterSystem == null)
        {
            return;
        }

        if (waterSystem.IsWaterGenerationInProgress)
        {
            EditorGUILayout.HelpBox(
                $"{waterSystem.WaterGenerationStatus} ({Mathf.RoundToInt(waterSystem.WaterGenerationProgress01 * 100f)}%)",
                MessageType.Info);
        }

        var timings = waterSystem.LastWaterGenerationTimings;
        if (timings == null || timings.Count == 0)
        {
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Last Water Generation Timings", EditorStyles.boldLabel);
        for (int i = 0; i < timings.Count; i++)
        {
            WaterGenerationTimingEntry timing = timings[i];
            EditorGUILayout.LabelField($"{timing.Label}: {timing.Milliseconds} ms", EditorStyles.miniBoldLabel);
            if (!string.IsNullOrWhiteSpace(timing.Details))
            {
                EditorGUILayout.LabelField(timing.Details, EditorStyles.wordWrappedMiniLabel);
            }
        }

        if (waterSystem.LastWaterGenerationTotalMilliseconds > 0L)
        {
            EditorGUILayout.LabelField(
                $"Total: {waterSystem.LastWaterGenerationTotalMilliseconds} ms",
                EditorStyles.miniBoldLabel);
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
