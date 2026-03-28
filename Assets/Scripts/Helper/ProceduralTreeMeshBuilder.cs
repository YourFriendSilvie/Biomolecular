using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class ProceduralTreeMeshBuilder
{
    public static Mesh CreateTubeMesh(IReadOnlyList<Vector3> pathPoints, IReadOnlyList<float> radii, int radialSegments)
    {
        Mesh mesh = new Mesh();
        if (pathPoints == null || radii == null || pathPoints.Count < 2 || pathPoints.Count != radii.Count)
        {
            return mesh;
        }

        radialSegments = Mathf.Max(3, radialSegments);
        int ringCount = pathPoints.Count;
        List<Vector3> vertices = new List<Vector3>(ringCount * radialSegments + 2);
        List<Vector2> uvs = new List<Vector2>(ringCount * radialSegments + 2);
        List<int> triangles = new List<int>((ringCount - 1) * radialSegments * 6 + radialSegments * 6);

        Vector3[] normals = new Vector3[ringCount];
        Vector3[] binormals = new Vector3[ringCount];
        BuildFrames(pathPoints, normals, binormals);

        for (int ringIndex = 0; ringIndex < ringCount; ringIndex++)
        {
            float radius = Mathf.Max(0.001f, radii[ringIndex]);
            float v = ringCount == 1 ? 0f : ringIndex / (float)(ringCount - 1);

            for (int radialIndex = 0; radialIndex < radialSegments; radialIndex++)
            {
                float angle = (radialIndex / (float)radialSegments) * Mathf.PI * 2f;
                Vector3 radialDirection = (normals[ringIndex] * Mathf.Cos(angle)) + (binormals[ringIndex] * Mathf.Sin(angle));
                vertices.Add(pathPoints[ringIndex] + radialDirection * radius);
                uvs.Add(new Vector2(radialIndex / (float)radialSegments, v));
            }
        }

        for (int ringIndex = 0; ringIndex < ringCount - 1; ringIndex++)
        {
            int currentRingStart = ringIndex * radialSegments;
            int nextRingStart = (ringIndex + 1) * radialSegments;

            for (int radialIndex = 0; radialIndex < radialSegments; radialIndex++)
            {
                int current = currentRingStart + radialIndex;
                int currentNext = currentRingStart + ((radialIndex + 1) % radialSegments);
                int next = nextRingStart + radialIndex;
                int nextNext = nextRingStart + ((radialIndex + 1) % radialSegments);

                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(currentNext);

                triangles.Add(currentNext);
                triangles.Add(next);
                triangles.Add(nextNext);
            }
        }

        int startCapCenterIndex = vertices.Count;
        vertices.Add(pathPoints[0]);
        uvs.Add(new Vector2(0.5f, 0f));

        for (int radialIndex = 0; radialIndex < radialSegments; radialIndex++)
        {
            int current = radialIndex;
            int next = (radialIndex + 1) % radialSegments;
            triangles.Add(startCapCenterIndex);
            triangles.Add(next);
            triangles.Add(current);
        }

        int endCapCenterIndex = vertices.Count;
        vertices.Add(pathPoints[ringCount - 1]);
        uvs.Add(new Vector2(0.5f, 1f));

        int finalRingStart = (ringCount - 1) * radialSegments;
        for (int radialIndex = 0; radialIndex < radialSegments; radialIndex++)
        {
            int current = finalRingStart + radialIndex;
            int next = finalRingStart + ((radialIndex + 1) % radialSegments);
            triangles.Add(endCapCenterIndex);
            triangles.Add(current);
            triangles.Add(next);
        }

        ReverseTriangleWinding(triangles);
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh CreateEllipsoidMesh(Vector3 radii, int longitudeSegments, int latitudeSegments)
    {
        Mesh mesh = new Mesh();
        longitudeSegments = Mathf.Max(4, longitudeSegments);
        latitudeSegments = Mathf.Max(3, latitudeSegments);

        List<Vector3> vertices = new List<Vector3>((longitudeSegments + 1) * (latitudeSegments + 1));
        List<Vector2> uvs = new List<Vector2>((longitudeSegments + 1) * (latitudeSegments + 1));
        List<int> triangles = new List<int>(longitudeSegments * latitudeSegments * 6);

        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            float v = lat / (float)latitudeSegments;
            float polar = Mathf.PI * v;
            float y = Mathf.Cos(polar);
            float horizontal = Mathf.Sin(polar);

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float u = lon / (float)longitudeSegments;
                float azimuth = u * Mathf.PI * 2f;
                float x = Mathf.Cos(azimuth) * horizontal;
                float z = Mathf.Sin(azimuth) * horizontal;

                vertices.Add(new Vector3(x * radii.x, y * radii.y, z * radii.z));
                uvs.Add(new Vector2(u, v));
            }
        }

        int ringSize = longitudeSegments + 1;
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            int currentRingStart = lat * ringSize;
            int nextRingStart = (lat + 1) * ringSize;

            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int current = currentRingStart + lon;
                int currentNext = current + 1;
                int next = nextRingStart + lon;
                int nextNext = next + 1;

                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(currentNext);

                triangles.Add(currentNext);
                triangles.Add(next);
                triangles.Add(nextNext);
            }
        }

        ReverseTriangleWinding(triangles);
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh CreateFoliageCardMesh(float length, float width)
    {
        float cardLength = Mathf.Max(0.01f, length);
        float halfWidth = Mathf.Max(0.01f, width) * 0.5f;

        Vector3[] frontVertices =
        {
            new Vector3(-halfWidth, 0f, 0f),
            new Vector3(-halfWidth, cardLength, 0f),
            new Vector3(halfWidth, cardLength, 0f),
            new Vector3(halfWidth, 0f, 0f)
        };

        int[] frontTriangles =
        {
            0, 1, 2,
            0, 2, 3
        };

        Vector2[] uvs =
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f)
        };

        return CreateDoubleSidedMesh(frontVertices, frontTriangles, uvs);
    }

    public static Mesh CreateMultiPlaneFoliageCardMesh(float length, float width, int planeCount)
    {
        int clampedPlaneCount = Mathf.Max(1, planeCount);
        float cardLength = Mathf.Max(0.01f, length);
        float halfWidth = Mathf.Max(0.01f, width) * 0.5f;
        float angleStep = 180f / clampedPlaneCount;

        List<Vector3> frontVertices = new List<Vector3>(clampedPlaneCount * 4);
        List<Vector2> frontUvs = new List<Vector2>(clampedPlaneCount * 4);
        List<int> frontTriangles = new List<int>(clampedPlaneCount * 6);

        for (int planeIndex = 0; planeIndex < clampedPlaneCount; planeIndex++)
        {
            Quaternion rotation = Quaternion.Euler(0f, angleStep * planeIndex, 0f);
            int vertexStart = frontVertices.Count;

            frontVertices.Add(rotation * new Vector3(-halfWidth, 0f, 0f));
            frontVertices.Add(rotation * new Vector3(-halfWidth, cardLength, 0f));
            frontVertices.Add(rotation * new Vector3(halfWidth, cardLength, 0f));
            frontVertices.Add(rotation * new Vector3(halfWidth, 0f, 0f));

            frontUvs.Add(new Vector2(0f, 0f));
            frontUvs.Add(new Vector2(0f, 1f));
            frontUvs.Add(new Vector2(1f, 1f));
            frontUvs.Add(new Vector2(1f, 0f));

            frontTriangles.Add(vertexStart);
            frontTriangles.Add(vertexStart + 1);
            frontTriangles.Add(vertexStart + 2);

            frontTriangles.Add(vertexStart);
            frontTriangles.Add(vertexStart + 2);
            frontTriangles.Add(vertexStart + 3);
        }

        return CreateDoubleSidedMesh(frontVertices, frontTriangles, frontUvs);
    }

    public static Mesh CreateLeafBladeMesh(float length, float width)
    {
        float bladeLength = Mathf.Max(0.01f, length);
        float halfWidth = Mathf.Max(0.01f, width) * 0.5f;

        Vector3[] frontVertices =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(-halfWidth * 0.24f, bladeLength * 0.18f, -bladeLength * 0.016f),
            new Vector3(-halfWidth, bladeLength * 0.56f, bladeLength * 0.038f),
            new Vector3(0f, bladeLength, 0f),
            new Vector3(halfWidth, bladeLength * 0.56f, bladeLength * 0.038f),
            new Vector3(halfWidth * 0.24f, bladeLength * 0.18f, -bladeLength * 0.016f),
            new Vector3(0f, bladeLength * 0.5f, bladeLength * 0.06f)
        };

        int[] frontTriangles =
        {
            0, 1, 6,
            1, 2, 6,
            6, 2, 3,
            6, 3, 4,
            6, 4, 5,
            0, 6, 5
        };

        Vector2[] uvs =
        {
            new Vector2(0.5f, 0f),
            new Vector2(0.38f, 0.18f),
            new Vector2(0f, 0.56f),
            new Vector2(0.5f, 1f),
            new Vector2(1f, 0.56f),
            new Vector2(0.62f, 0.18f),
            new Vector2(0.5f, 0.5f)
        };

        return CreateDoubleSidedMesh(frontVertices, frontTriangles, uvs);
    }

    public static Mesh CreateAlderLeafMesh(float length, float width)
    {
        float bladeLength = Mathf.Max(0.01f, length);
        float halfWidth = Mathf.Max(0.01f, width) * 0.5f;

        Vector3[] perimeterVertices =
        {
            new Vector3(-halfWidth * 0.12f, bladeLength * 0.05f, -bladeLength * 0.012f),
            new Vector3(-halfWidth * 0.34f, bladeLength * 0.18f, bladeLength * 0.006f),
            new Vector3(-halfWidth * 0.56f, bladeLength * 0.38f, bladeLength * 0.03f),
            new Vector3(-halfWidth * 0.48f, bladeLength * 0.6f, bladeLength * 0.04f),
            new Vector3(-halfWidth * 0.22f, bladeLength * 0.86f, bladeLength * 0.022f),
            new Vector3(0f, bladeLength, 0f),
            new Vector3(halfWidth * 0.22f, bladeLength * 0.86f, bladeLength * 0.022f),
            new Vector3(halfWidth * 0.48f, bladeLength * 0.6f, bladeLength * 0.04f),
            new Vector3(halfWidth * 0.56f, bladeLength * 0.38f, bladeLength * 0.03f),
            new Vector3(halfWidth * 0.34f, bladeLength * 0.18f, bladeLength * 0.006f),
            new Vector3(halfWidth * 0.12f, bladeLength * 0.05f, -bladeLength * 0.012f)
        };

        Vector2[] perimeterUvs =
        {
            new Vector2(0.44f, 0.05f),
            new Vector2(0.33f, 0.18f),
            new Vector2(0.22f, 0.38f),
            new Vector2(0.26f, 0.6f),
            new Vector2(0.39f, 0.86f),
            new Vector2(0.5f, 1f),
            new Vector2(0.61f, 0.86f),
            new Vector2(0.74f, 0.6f),
            new Vector2(0.78f, 0.38f),
            new Vector2(0.67f, 0.18f),
            new Vector2(0.56f, 0.05f)
        };

        return CreateCenteredFanMesh(
            perimeterVertices,
            perimeterUvs,
            new Vector3(0f, bladeLength * 0.44f, bladeLength * 0.045f),
            new Vector2(0.5f, 0.44f));
    }

    public static Mesh CreateServiceberryLeafMesh(float length, float width)
    {
        float bladeLength = Mathf.Max(0.01f, length);
        float halfWidth = Mathf.Max(0.01f, width) * 0.5f;

        Vector3[] perimeterVertices =
        {
            new Vector3(-halfWidth * 0.1f, bladeLength * 0.04f, -bladeLength * 0.012f),
            new Vector3(-halfWidth * 0.28f, bladeLength * 0.16f, bladeLength * 0.004f),
            new Vector3(-halfWidth * 0.46f, bladeLength * 0.34f, bladeLength * 0.024f),
            new Vector3(-halfWidth * 0.5f, bladeLength * 0.58f, bladeLength * 0.036f),
            new Vector3(-halfWidth * 0.26f, bladeLength * 0.84f, bladeLength * 0.02f),
            new Vector3(0f, bladeLength, 0f),
            new Vector3(halfWidth * 0.26f, bladeLength * 0.84f, bladeLength * 0.02f),
            new Vector3(halfWidth * 0.5f, bladeLength * 0.58f, bladeLength * 0.036f),
            new Vector3(halfWidth * 0.46f, bladeLength * 0.34f, bladeLength * 0.024f),
            new Vector3(halfWidth * 0.28f, bladeLength * 0.16f, bladeLength * 0.004f),
            new Vector3(halfWidth * 0.1f, bladeLength * 0.04f, -bladeLength * 0.012f)
        };

        Vector2[] perimeterUvs =
        {
            new Vector2(0.45f, 0.04f),
            new Vector2(0.36f, 0.16f),
            new Vector2(0.24f, 0.34f),
            new Vector2(0.22f, 0.58f),
            new Vector2(0.39f, 0.84f),
            new Vector2(0.5f, 1f),
            new Vector2(0.61f, 0.84f),
            new Vector2(0.78f, 0.58f),
            new Vector2(0.76f, 0.34f),
            new Vector2(0.64f, 0.16f),
            new Vector2(0.55f, 0.04f)
        };

        return CreateCenteredFanMesh(
            perimeterVertices,
            perimeterUvs,
            new Vector3(0f, bladeLength * 0.46f, bladeLength * 0.04f),
            new Vector2(0.5f, 0.46f));
    }

    public static Mesh CreateMapleLeafMesh(float length, float width)
    {
        float bladeLength = Mathf.Max(0.01f, length);
        float halfWidth = Mathf.Max(0.01f, width) * 0.5f;

        Vector3[] perimeterVertices =
        {
            new Vector3(-halfWidth * 0.14f, bladeLength * 0.04f, -bladeLength * 0.015f),
            new Vector3(-halfWidth * 0.34f, bladeLength * 0.14f, bladeLength * 0.004f),
            new Vector3(-halfWidth * 0.82f, bladeLength * 0.3f, bladeLength * 0.02f),
            new Vector3(-halfWidth * 0.38f, bladeLength * 0.44f, bladeLength * 0.045f),
            new Vector3(-halfWidth * 0.64f, bladeLength * 0.72f, bladeLength * 0.05f),
            new Vector3(-halfWidth * 0.18f, bladeLength * 0.68f, bladeLength * 0.046f),
            new Vector3(0f, bladeLength, 0f),
            new Vector3(halfWidth * 0.18f, bladeLength * 0.68f, bladeLength * 0.046f),
            new Vector3(halfWidth * 0.64f, bladeLength * 0.72f, bladeLength * 0.05f),
            new Vector3(halfWidth * 0.38f, bladeLength * 0.44f, bladeLength * 0.045f),
            new Vector3(halfWidth * 0.82f, bladeLength * 0.3f, bladeLength * 0.02f),
            new Vector3(halfWidth * 0.34f, bladeLength * 0.14f, bladeLength * 0.004f),
            new Vector3(halfWidth * 0.14f, bladeLength * 0.04f, -bladeLength * 0.015f)
        };

        Vector2[] perimeterUvs =
        {
            new Vector2(0.43f, 0.04f),
            new Vector2(0.33f, 0.14f),
            new Vector2(0.09f, 0.3f),
            new Vector2(0.31f, 0.44f),
            new Vector2(0.18f, 0.72f),
            new Vector2(0.41f, 0.68f),
            new Vector2(0.5f, 1f),
            new Vector2(0.59f, 0.68f),
            new Vector2(0.82f, 0.72f),
            new Vector2(0.69f, 0.44f),
            new Vector2(0.91f, 0.3f),
            new Vector2(0.67f, 0.14f),
            new Vector2(0.57f, 0.04f)
        };

        return CreateCenteredFanMesh(
            perimeterVertices,
            perimeterUvs,
            new Vector3(0f, bladeLength * 0.42f, bladeLength * 0.05f),
            new Vector2(0.5f, 0.42f));
    }

    public static Mesh CreateNeedleBladeMesh(float length, float width)
    {
        float bladeLength = Mathf.Max(0.01f, length);
        float halfWidth = Mathf.Max(0.002f, width) * 0.5f;

        Vector3[] frontVertices =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(-halfWidth * 0.18f, bladeLength * 0.12f, -bladeLength * 0.008f),
            new Vector3(-halfWidth * 0.58f, bladeLength * 0.62f, bladeLength * 0.012f),
            new Vector3(0f, bladeLength, 0f),
            new Vector3(halfWidth * 0.58f, bladeLength * 0.62f, bladeLength * 0.012f),
            new Vector3(halfWidth * 0.18f, bladeLength * 0.12f, -bladeLength * 0.008f),
            new Vector3(0f, bladeLength * 0.68f, bladeLength * 0.018f)
        };

        int[] frontTriangles =
        {
            0, 1, 6,
            1, 2, 6,
            6, 2, 3,
            6, 3, 4,
            6, 4, 5,
            0, 6, 5
        };

        Vector2[] uvs =
        {
            new Vector2(0.5f, 0f),
            new Vector2(0.42f, 0.12f),
            new Vector2(0f, 0.62f),
            new Vector2(0.5f, 1f),
            new Vector2(1f, 0.62f),
            new Vector2(0.58f, 0.12f),
            new Vector2(0.5f, 0.68f)
        };

        return CreateDoubleSidedMesh(frontVertices, frontTriangles, uvs);
    }

    public static Mesh CreateDouglasFirNeedleMesh(float length, float width)
    {
        return CreateNeedleBladeMesh(length, width * 0.85f);
    }

    public static Mesh CreateCedarSprayMesh(float length, float width)
    {
        float bladeLength = Mathf.Max(0.01f, length);
        float halfWidth = Mathf.Max(0.01f, width) * 0.5f;

        Vector3[] perimeterVertices =
        {
            new Vector3(-halfWidth * 0.08f, bladeLength * 0.02f, -bladeLength * 0.01f),
            new Vector3(-halfWidth * 0.34f, bladeLength * 0.14f, bladeLength * 0.004f),
            new Vector3(-halfWidth * 0.14f, bladeLength * 0.3f, bladeLength * 0.018f),
            new Vector3(-halfWidth * 0.48f, bladeLength * 0.48f, bladeLength * 0.022f),
            new Vector3(-halfWidth * 0.18f, bladeLength * 0.68f, bladeLength * 0.032f),
            new Vector3(-halfWidth * 0.3f, bladeLength * 0.84f, bladeLength * 0.024f),
            new Vector3(0f, bladeLength, 0f),
            new Vector3(halfWidth * 0.3f, bladeLength * 0.84f, bladeLength * 0.024f),
            new Vector3(halfWidth * 0.18f, bladeLength * 0.68f, bladeLength * 0.032f),
            new Vector3(halfWidth * 0.48f, bladeLength * 0.48f, bladeLength * 0.022f),
            new Vector3(halfWidth * 0.14f, bladeLength * 0.3f, bladeLength * 0.018f),
            new Vector3(halfWidth * 0.34f, bladeLength * 0.14f, bladeLength * 0.004f),
            new Vector3(halfWidth * 0.08f, bladeLength * 0.02f, -bladeLength * 0.01f)
        };

        Vector2[] perimeterUvs =
        {
            new Vector2(0.46f, 0.02f),
            new Vector2(0.33f, 0.14f),
            new Vector2(0.43f, 0.3f),
            new Vector2(0.26f, 0.48f),
            new Vector2(0.41f, 0.68f),
            new Vector2(0.35f, 0.84f),
            new Vector2(0.5f, 1f),
            new Vector2(0.65f, 0.84f),
            new Vector2(0.59f, 0.68f),
            new Vector2(0.74f, 0.48f),
            new Vector2(0.57f, 0.3f),
            new Vector2(0.67f, 0.14f),
            new Vector2(0.54f, 0.02f)
        };

        return CreateCenteredFanMesh(
            perimeterVertices,
            perimeterUvs,
            new Vector3(0f, bladeLength * 0.38f, bladeLength * 0.02f),
            new Vector2(0.5f, 0.38f));
    }

    public static float EstimateTubeVolume(IReadOnlyList<Vector3> pathPoints, IReadOnlyList<float> radii)
    {
        if (pathPoints == null || radii == null || pathPoints.Count < 2 || pathPoints.Count != radii.Count)
        {
            return 0f;
        }

        float totalVolume = 0f;
        for (int i = 0; i < pathPoints.Count - 1; i++)
        {
            float segmentLength = Vector3.Distance(pathPoints[i], pathPoints[i + 1]);
            float startRadius = Mathf.Max(0f, radii[i]);
            float endRadius = Mathf.Max(0f, radii[i + 1]);
            totalVolume += Mathf.PI * segmentLength * (startRadius * startRadius + (startRadius * endRadius) + (endRadius * endRadius)) / 3f;
        }

        return totalVolume;
    }

    public static Mesh CreateCombinedMesh(IReadOnlyList<Mesh> meshes, IReadOnlyList<Matrix4x4> transforms = null)
    {
        Mesh combinedMesh = new Mesh();
        if (meshes == null || meshes.Count == 0)
        {
            return combinedMesh;
        }

        List<CombineInstance> combineInstances = new List<CombineInstance>(meshes.Count);
        int totalVertexCount = 0;

        for (int i = 0; i < meshes.Count; i++)
        {
            if (meshes[i] == null)
            {
                continue;
            }

            totalVertexCount += meshes[i].vertexCount;
            combineInstances.Add(new CombineInstance
            {
                mesh = meshes[i],
                transform = transforms != null && i < transforms.Count
                    ? transforms[i]
                    : Matrix4x4.identity
            });
        }

        if (combineInstances.Count == 0)
        {
            return combinedMesh;
        }

        if (totalVertexCount > 65535)
        {
            combinedMesh.indexFormat = IndexFormat.UInt32;
        }

        combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true, false);
        combinedMesh.RecalculateBounds();
        return combinedMesh;
    }

    private static void BuildFrames(IReadOnlyList<Vector3> pathPoints, Vector3[] normals, Vector3[] binormals)
    {
        Vector3 tangent = GetTangent(pathPoints, 0);
        Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
        Vector3 normal = Vector3.Cross(reference, tangent).normalized;
        Vector3 binormal = Vector3.Cross(tangent, normal).normalized;

        normals[0] = normal;
        binormals[0] = binormal;

        for (int i = 1; i < pathPoints.Count; i++)
        {
            tangent = GetTangent(pathPoints, i);
            normal = Vector3.ProjectOnPlane(normals[i - 1], tangent);

            if (normal.sqrMagnitude < 0.0001f)
            {
                reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
                normal = Vector3.Cross(reference, tangent);
            }

            normal.Normalize();
            binormal = Vector3.Cross(tangent, normal).normalized;

            normals[i] = normal;
            binormals[i] = binormal;
        }
    }

    private static Vector3 GetTangent(IReadOnlyList<Vector3> pathPoints, int index)
    {
        if (pathPoints.Count == 1)
        {
            return Vector3.up;
        }

        if (index <= 0)
        {
            return (pathPoints[1] - pathPoints[0]).normalized;
        }

        if (index >= pathPoints.Count - 1)
        {
            return (pathPoints[index] - pathPoints[index - 1]).normalized;
        }

        return (pathPoints[index + 1] - pathPoints[index - 1]).normalized;
    }

    private static Mesh CreateCenteredFanMesh(
        IReadOnlyList<Vector3> perimeterVertices,
        IReadOnlyList<Vector2> perimeterUvs,
        Vector3 centerVertex,
        Vector2 centerUv)
    {
        Mesh mesh = new Mesh();
        if (perimeterVertices == null || perimeterVertices.Count < 3)
        {
            return mesh;
        }

        List<Vector3> frontVertices = new List<Vector3>(perimeterVertices.Count + 1)
        {
            centerVertex
        };
        List<Vector2> frontUvs = new List<Vector2>(perimeterVertices.Count + 1)
        {
            centerUv
        };

        for (int i = 0; i < perimeterVertices.Count; i++)
        {
            frontVertices.Add(perimeterVertices[i]);
            frontUvs.Add(perimeterUvs != null && i < perimeterUvs.Count ? perimeterUvs[i] : Vector2.zero);
        }

        List<int> frontTriangles = new List<int>(perimeterVertices.Count * 3);
        for (int i = 1; i <= perimeterVertices.Count; i++)
        {
            int next = i == perimeterVertices.Count ? 1 : i + 1;
            frontTriangles.Add(0);
            frontTriangles.Add(i);
            frontTriangles.Add(next);
        }

        return CreateDoubleSidedMesh(frontVertices, frontTriangles, frontUvs);
    }

    private static Mesh CreateDoubleSidedMesh(IReadOnlyList<Vector3> frontVertices, IReadOnlyList<int> frontTriangles, IReadOnlyList<Vector2> frontUvs)
    {
        Mesh mesh = new Mesh();
        if (frontVertices == null || frontTriangles == null || frontUvs == null || frontVertices.Count == 0)
        {
            return mesh;
        }

        int frontVertexCount = frontVertices.Count;
        List<Vector3> vertices = new List<Vector3>(frontVertexCount * 2);
        List<Vector2> uvs = new List<Vector2>(frontVertexCount * 2);
        List<int> triangles = new List<int>(frontTriangles.Count * 2);

        for (int i = 0; i < frontVertexCount; i++)
        {
            vertices.Add(frontVertices[i]);
            uvs.Add(i < frontUvs.Count ? frontUvs[i] : Vector2.zero);
        }

        for (int i = 0; i < frontTriangles.Count; i += 3)
        {
            triangles.Add(frontTriangles[i]);
            triangles.Add(frontTriangles[i + 1]);
            triangles.Add(frontTriangles[i + 2]);
        }

        for (int i = 0; i < frontVertexCount; i++)
        {
            vertices.Add(frontVertices[i]);
            uvs.Add(i < frontUvs.Count ? frontUvs[i] : Vector2.zero);
        }

        for (int i = 0; i < frontTriangles.Count; i += 3)
        {
            triangles.Add(frontTriangles[i] + frontVertexCount);
            triangles.Add(frontTriangles[i + 2] + frontVertexCount);
            triangles.Add(frontTriangles[i + 1] + frontVertexCount);
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void ReverseTriangleWinding(List<int> triangles)
    {
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            int temp = triangles[i + 1];
            triangles[i + 1] = triangles[i + 2];
            triangles[i + 2] = temp;
        }
    }
}
