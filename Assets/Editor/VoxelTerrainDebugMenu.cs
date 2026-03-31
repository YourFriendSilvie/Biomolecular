using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor menu helpers to toggle the terrain debug shader modes without typing into the console.
/// Adds: Biomolecular -> Terrain Debug -> (Show Albedo / Show Normals / Show NdotL / Off / CellDebug On / CellDebug Off)
/// </summary>
public static class VoxelTerrainDebugMenu
{
    [MenuItem("Biomolecular/Terrain Debug/Show Albedo")]
    private static void ShowAlbedo() => SetDebugMode(1f);

    [MenuItem("Biomolecular/Terrain Debug/Show Normals")]
    private static void ShowNormals() => SetDebugMode(2f);

    [MenuItem("Biomolecular/Terrain Debug/Show NdotL")]
    private static void ShowNdotL() => SetDebugMode(3f);

    [MenuItem("Biomolecular/Terrain Debug/Off")]
    private static void DebugOff() => SetDebugMode(0f);

    [MenuItem("Biomolecular/Terrain Debug/CellDebug On")]
    private static void CellDebugOn() => SetCellDebug(1f);

    [MenuItem("Biomolecular/Terrain Debug/CellDebug Off")]
    private static void CellDebugOff() => SetCellDebug(0f);

    private static void SetDebugMode(float mode)
    {
        Shader.SetGlobalFloat("_DebugViewMode", mode);
        foreach (var t in Object.FindObjectsByType<ProceduralVoxelTerrain>())
        {
            // sharedTerrainMaterial is non-public; use reflection to access it safely from the editor assembly.
            var fi = t.GetType().GetField("sharedTerrainMaterial", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null)
            {
                var mat = fi.GetValue(t) as Material;
                if (mat != null)
                    mat.SetFloat("_DebugViewMode", mode);
            }

            // Try to call the ApplyPendingCellMaterialDebugTextureIfNeeded method via reflection
            var mi = typeof(ProceduralVoxelTerrain).GetMethod("ApplyPendingCellMaterialDebugTextureIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (mi != null)
            {
                try { mi.Invoke(t, null); } catch { /* ignore reflection failures */ }
            }

            EditorUtility.SetDirty(t);
        }

        Debug.Log($"VoxelTerrain: Set _DebugViewMode = {mode}");
    }

    private static void SetCellDebug(float v)
    {
        Shader.SetGlobalFloat("_DebugCellMaterial", v);
        foreach (var t in Object.FindObjectsByType<ProceduralVoxelTerrain>())
        {
            var fi = t.GetType().GetField("sharedTerrainMaterial", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null)
            {
                var mat = fi.GetValue(t) as Material;
                if (mat != null)
                    mat.SetFloat("_DebugCellMaterial", v);
            }

            // If enabling, attempt to force upload of any pending debug texture.
            if (v > 0.5f)
            {
                var mi = typeof(ProceduralVoxelTerrain).GetMethod("ApplyPendingCellMaterialDebugTextureIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (mi != null)
                {
                    try { mi.Invoke(t, null); } catch { /* ignore */ }
                }
            }

            EditorUtility.SetDirty(t);
        }

        Debug.Log($"VoxelTerrain: Set _DebugCellMaterial = {v}");
    }
}
