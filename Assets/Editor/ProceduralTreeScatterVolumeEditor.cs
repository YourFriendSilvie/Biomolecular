using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ProceduralTreeScatterVolume))]
public class ProceduralTreeScatterVolumeEditor : Editor
{
    private readonly BoxBoundsHandle boxBoundsHandle = new BoxBoundsHandle();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProceduralTreeScatterVolume scatterVolume = (ProceduralTreeScatterVolume)target;

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Trees"))
            {
                GenerateTrees(scatterVolume);
            }

            if (GUILayout.Button("Clear Generated"))
            {
                ClearGenerated(scatterVolume);
            }
        }
    }

    private void OnSceneGUI()
    {
        ProceduralTreeScatterVolume scatterVolume = (ProceduralTreeScatterVolume)target;
        if (scatterVolume == null)
        {
            return;
        }

        using (new Handles.DrawingScope(scatterVolume.transform.localToWorldMatrix))
        {
            boxBoundsHandle.center = Vector3.zero;
            boxBoundsHandle.size = scatterVolume.VolumeSize;

            EditorGUI.BeginChangeCheck();
            boxBoundsHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(scatterVolume, "Resize Tree Scatter Volume");
                scatterVolume.VolumeSize = boxBoundsHandle.size;
                EditorUtility.SetDirty(scatterVolume);
            }
        }
    }

    private static void GenerateTrees(ProceduralTreeScatterVolume scatterVolume)
    {
        if (scatterVolume == null)
        {
            return;
        }

        Transform existingRoot = scatterVolume.GetGeneratedRoot();
        if (scatterVolume.ClearExistingBeforeGenerate && existingRoot != null)
        {
            Undo.DestroyObjectImmediate(existingRoot.gameObject);
        }

        bool hadRootBeforeGenerate = existingRoot != null && !scatterVolume.ClearExistingBeforeGenerate;
        List<GameObject> createdTrees = scatterVolume.GenerateTrees(false);
        Transform generatedRoot = scatterVolume.GetGeneratedRoot();

        if (generatedRoot != null && !hadRootBeforeGenerate)
        {
            Undo.RegisterCreatedObjectUndo(generatedRoot.gameObject, "Generate Trees");
        }
        else
        {
            foreach (GameObject createdTree in createdTrees)
            {
                if (createdTree != null)
                {
                    Undo.RegisterCreatedObjectUndo(createdTree, "Generate Trees");
                }
            }
        }

        EditorUtility.SetDirty(scatterVolume);
        EditorSceneManager.MarkSceneDirty(scatterVolume.gameObject.scene);
    }

    private static void ClearGenerated(ProceduralTreeScatterVolume scatterVolume)
    {
        if (scatterVolume == null)
        {
            return;
        }

        Transform generatedRoot = scatterVolume.GetGeneratedRoot();
        if (generatedRoot == null)
        {
            return;
        }

        Undo.DestroyObjectImmediate(generatedRoot.gameObject);
        EditorUtility.SetDirty(scatterVolume);
        EditorSceneManager.MarkSceneDirty(scatterVolume.gameObject.scene);
    }
}
