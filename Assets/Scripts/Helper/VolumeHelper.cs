using UnityEngine;

public static class VolumeHelper
{
    public static float GetMeshVolume(Mesh mesh)
    {
        return GetScaledMeshVolume(mesh, Vector3.one);
    }

    public static float GetScaledMeshVolume(Mesh mesh, Vector3 lossyScale)
    {
        if (mesh == null)
        {
            Debug.LogError("Mesh is null. Cannot calculate volume.");
            return 0f;
        }

        if (mesh.triangles == null || mesh.triangles.Length % 3 != 0)
        {
            Debug.LogError("Mesh triangles are not valid.");
            return 0f;
        }

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        float signedVolume = 0f;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 p1 = Vector3.Scale(vertices[triangles[i]], lossyScale);
            Vector3 p2 = Vector3.Scale(vertices[triangles[i + 1]], lossyScale);
            Vector3 p3 = Vector3.Scale(vertices[triangles[i + 2]], lossyScale);
            signedVolume += Vector3.Dot(p1, Vector3.Cross(p2, p3)) / 6f;
        }

        return Mathf.Abs(signedVolume);
    }

    public static float GetMeshFilterVolume(MeshFilter meshFilter)
    {
        if (meshFilter == null)
        {
            return 0f;
        }

        return GetScaledMeshVolume(meshFilter.sharedMesh, meshFilter.transform.lossyScale);
    }

    public static float GetTetrahedronVolume(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        return Mathf.Abs(Vector3.Dot(p1, Vector3.Cross(p2, p3))) / 6f;
    }

    public static float getMeshVolume(Mesh mesh)
    {
        return GetMeshVolume(mesh);
    }

    public static float getVolumeOfTetrahedron(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        return GetTetrahedronVolume(p1, p2, p3);
    }
}
