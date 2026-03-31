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

        ProceduralVoxelTerrainWaterSystem waterSystem = voxelTerrain.GetComponent<ProceduralVoxelTerrainWaterSystem>();
        bool isWaterGenerationInProgress = waterSystem != null && waterSystem.IsWaterGenerationInProgress;
        ProceduralVoxelTerrainScatterer scatterer = voxelTerrain.GetComponent<ProceduralVoxelTerrainScatterer>();
        bool isScatterGenerationInProgress = scatterer != null && scatterer.IsScatterGenerationInProgress;

        if (voxelTerrain.IsTerrainGenerationInProgress)
        {
            EditorGUILayout.HelpBox(
                $"{voxelTerrain.TerrainGenerationStatus} ({Mathf.RoundToInt(voxelTerrain.TerrainGenerationProgress01 * 100f)}%)",
                MessageType.Info);
            Repaint();
        }

        if (isWaterGenerationInProgress)
        {
            EditorGUILayout.HelpBox(
                $"{waterSystem.WaterGenerationStatus} ({Mathf.RoundToInt(waterSystem.WaterGenerationProgress01 * 100f)}%)",
                MessageType.Info);
            Repaint();
        }

        if (isScatterGenerationInProgress)
        {
            EditorGUILayout.HelpBox(
                $"{scatterer.ScatterGenerationStatus} ({Mathf.RoundToInt(scatterer.ScatterGenerationProgress01 * 100f)}%)",
                MessageType.Info);
            Repaint();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Olympic Rainforest Voxel Preset"))
        {
            voxelTerrain.ApplyOlympicRainforestPreset();
            MarkDirty(voxelTerrain);
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(isWaterGenerationInProgress))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(voxelTerrain.IsTerrainGenerationInProgress))
                {
                    if (GUILayout.Button("Generate Voxel Terrain"))
                    {
                        VoxelTerrainEditorGenerationUtility.GenerateTerrainOnly(voxelTerrain);
                    }
                }

                if (GUILayout.Button(voxelTerrain.IsTerrainGenerationInProgress ? "Clear / Cancel Voxel Terrain" : "Clear Voxel Terrain"))
                {
                    voxelTerrain.ClearGeneratedTerrain();
                    MarkDirty(voxelTerrain);
                }
            }
        }

        ProceduralVoxelStartAreaSystem startAreaSystem = voxelTerrain.GetComponent<ProceduralVoxelStartAreaSystem>();
        if ((waterSystem != null || scatterer != null || startAreaSystem != null) &&
            !voxelTerrain.IsTerrainGenerationInProgress &&
            !isWaterGenerationInProgress &&
            !isScatterGenerationInProgress &&
            GUILayout.Button("Generate Voxel Terrain + Water + Scatter + Start Area"))
        {
            VoxelTerrainEditorGenerationUtility.GenerateTerrainWaterScatterStartArea(
                voxelTerrain,
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

internal static class VoxelTerrainEditorGenerationUtility
{
    public static void GenerateTerrainOnly(ProceduralVoxelTerrain terrain)
    {
        if (terrain == null)
        {
            return;
        }

        terrain.GenerateTerrainWithConfiguredMode(
            terrain.ClearExistingBeforeGenerate,
            _ => MarkDirty(terrain));
    }

    public static void GenerateTerrainWaterScatterStartArea(
        ProceduralVoxelTerrain terrain,
        ProceduralVoxelTerrainWaterSystem waterSystem,
        ProceduralVoxelTerrainScatterer scatterer,
        ProceduralVoxelStartAreaSystem startAreaSystem)
    {
        void ContinueGeneration(bool success)
        {
            if (!success)
            {
                return;
            }

            if (terrain != null)
            {
                MarkDirty(terrain);
            }

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

        if (terrain == null)
        {
            ContinueGeneration(true);
            return;
        }

        terrain.GenerateTerrainWithConfiguredMode(
            terrain.ClearExistingBeforeGenerate,
            ContinueGeneration);
    }

    public static void MarkDirty(Component component)
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
