using System.Collections.Generic;
using UnityEngine;

internal static class WaterMeshGenerator
{
    public static Mesh BuildLakeMesh(GeneratedLake lake, Transform generatedRoot, float terrainVoxelSizeMeters)
    {
        if (lake == null ||
            generatedRoot == null ||
            lake.surfaceVertices == null ||
            lake.surfaceTriangles == null ||
            lake.surfaceTriangles.Length < 3)
        {
            return null;
        }

        List<Vector3> vertices = new List<Vector3>(lake.surfaceVertices.Length);
        List<int> triangles = new List<int>(lake.surfaceTriangles.Length);
        List<Vector2> uvs = new List<Vector2>(lake.surfaceVertices.Length);
        List<Vector3> normals = new List<Vector3>(lake.surfaceVertices.Length);
        float uvScale = Mathf.Max(lake.captureRadius * 2f, terrainVoxelSizeMeters);
        for (int i = 0; i < lake.surfaceVertices.Length; i++)
        {
            Vector3 vertex = lake.surfaceVertices[i];
            vertices.Add(generatedRoot.InverseTransformPoint(vertex));
            uvs.Add(new Vector2(vertex.x / uvScale, vertex.z / uvScale));
            normals.Add(Vector3.up);
        }

        if (vertices.Count == 0)
        {
            return null;
        }

        triangles.AddRange(lake.surfaceTriangles);

        Mesh mesh = new Mesh
        {
            name = "Generated Lake Mesh",
            indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh BuildRiverMesh(GeneratedRiver river, Transform generatedRoot, float waterSurfaceThicknessMeters)
    {
        if (river == null || generatedRoot == null || river.points.Count < 2 || river.points.Count != river.widths.Count)
        {
            return null;
        }

        int pointCount = river.points.Count;
        Vector3[] topLeft = new Vector3[pointCount];
        Vector3[] topRight = new Vector3[pointCount];
        Vector3[] bottomLeft = new Vector3[pointCount];
        Vector3[] bottomRight = new Vector3[pointCount];
        float[] pathDistances = new float[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 currentPoint = river.points[i];
            Vector3 previousPoint = river.points[Mathf.Max(0, i - 1)];
            Vector3 nextPoint = river.points[Mathf.Min(pointCount - 1, i + 1)];
            Vector3 tangent = Vector3.ProjectOnPlane(nextPoint - previousPoint, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = i > 0
                    ? Vector3.ProjectOnPlane(currentPoint - previousPoint, Vector3.up)
                    : Vector3.ProjectOnPlane(nextPoint - currentPoint, Vector3.up);
            }

            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.forward;
            }

            Vector3 right = Vector3.Cross(Vector3.up, tangent.normalized);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.right;
            }

            float halfWidth = Mathf.Max(0.2f, river.widths[i] * 0.5f);
            Vector3 leftWorld = currentPoint - (right * halfWidth);
            Vector3 rightWorld = currentPoint + (right * halfWidth);
            Vector3 down = Vector3.up * waterSurfaceThicknessMeters;

            topLeft[i] = generatedRoot.InverseTransformPoint(leftWorld);
            topRight[i] = generatedRoot.InverseTransformPoint(rightWorld);
            bottomLeft[i] = generatedRoot.InverseTransformPoint(leftWorld - down);
            bottomRight[i] = generatedRoot.InverseTransformPoint(rightWorld - down);

            if (i > 0)
            {
                pathDistances[i] = pathDistances[i - 1] + Vector3.Distance(
                    new Vector3(currentPoint.x, 0f, currentPoint.z),
                    new Vector3(river.points[i - 1].x, 0f, river.points[i - 1].z));
            }
        }

        List<Vector3> vertices = new List<Vector3>((pointCount - 1) * 16);
        List<int> triangles = new List<int>((pointCount - 1) * 24);
        List<Vector2> uvs = new List<Vector2>((pointCount - 1) * 16);
        for (int i = 0; i < pointCount - 1; i++)
        {
            float startDistance = pathDistances[i];
            float endDistance = pathDistances[i + 1];

            AddQuad(vertices, triangles, uvs,
                topLeft[i], topRight[i], topRight[i + 1], topLeft[i + 1],
                new Vector2(0f, startDistance), new Vector2(1f, startDistance), new Vector2(1f, endDistance), new Vector2(0f, endDistance),
                true);

            AddQuad(vertices, triangles, uvs,
                bottomRight[i], bottomLeft[i], bottomLeft[i + 1], bottomRight[i + 1],
                new Vector2(0f, startDistance), new Vector2(1f, startDistance), new Vector2(1f, endDistance), new Vector2(0f, endDistance),
                false);

            AddQuad(vertices, triangles, uvs,
                topLeft[i], topLeft[i + 1], bottomLeft[i + 1], bottomLeft[i],
                new Vector2(0f, startDistance), new Vector2(1f, startDistance), new Vector2(1f, endDistance), new Vector2(0f, endDistance),
                false);

            AddQuad(vertices, triangles, uvs,
                topRight[i + 1], topRight[i], bottomRight[i], bottomRight[i + 1],
                new Vector2(0f, startDistance), new Vector2(1f, startDistance), new Vector2(1f, endDistance), new Vector2(0f, endDistance),
                false);
        }

        AddQuad(vertices, triangles, uvs,
            topRight[0], topLeft[0], bottomLeft[0], bottomRight[0],
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            false);

        int lastIndex = pointCount - 1;
        AddQuad(vertices, triangles, uvs,
            topLeft[lastIndex], topRight[lastIndex], bottomRight[lastIndex], bottomLeft[lastIndex],
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            false);

        if (vertices.Count == 0)
        {
            return null;
        }

        Mesh mesh = new Mesh
        {
            name = "Generated River Mesh"
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD,
        bool doubleSided)
    {
        int vertexStart = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        uvs.Add(uvD);

        triangles.Add(vertexStart);
        triangles.Add(vertexStart + 1);
        triangles.Add(vertexStart + 2);
        triangles.Add(vertexStart);
        triangles.Add(vertexStart + 2);
        triangles.Add(vertexStart + 3);

        if (!doubleSided)
        {
            return;
        }

        triangles.Add(vertexStart + 2);
        triangles.Add(vertexStart + 1);
        triangles.Add(vertexStart);
        triangles.Add(vertexStart + 3);
        triangles.Add(vertexStart + 2);
        triangles.Add(vertexStart);
    }
}
