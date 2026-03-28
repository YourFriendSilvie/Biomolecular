using System.Collections.Generic;
using UnityEngine;

// -------------------------------------------------------------------------
// ChunkMeshBuilder – pure static helpers for building chunk meshes from
// voxel density and material data using the marching-tetrahedra algorithm.
//
// No Unity scene state is touched here; the caller (ProceduralVoxelTerrain)
// supplies all raw data as parameters and then commits the resulting
// MeshBuildData to its chunk objects.
// -------------------------------------------------------------------------
internal static class ChunkMeshBuilder
{
    internal const float IsoLevel = 0f;

    internal static readonly Vector3[] CubeCornerOffsets =
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(1f, 0f, 0f),
        new Vector3(1f, 0f, 1f),
        new Vector3(0f, 0f, 1f),
        new Vector3(0f, 1f, 0f),
        new Vector3(1f, 1f, 0f),
        new Vector3(1f, 1f, 1f),
        new Vector3(0f, 1f, 1f)
    };

    internal static readonly int[,] CubeTetrahedra =
    {
        { 0, 5, 1, 6 },
        { 0, 1, 2, 6 },
        { 0, 2, 3, 6 },
        { 0, 3, 7, 6 },
        { 0, 7, 4, 6 },
        { 0, 4, 5, 6 }
    };

    // -------------------------------------------------------------------------
    // MeshBuildData – accumulates vertices and per-submesh triangle indices
    // while the marching-tetrahedra algorithm processes a single chunk.
    // -------------------------------------------------------------------------
    internal sealed class MeshBuildData
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int>[] submeshIndices;

        public MeshBuildData(int submeshCount)
        {
            submeshIndices = new List<int>[Mathf.Max(1, submeshCount)];
            for (int i = 0; i < submeshIndices.Length; i++)
            {
                submeshIndices[i] = new List<int>();
            }
        }
    }

    // -------------------------------------------------------------------------
    // BuildChunkMesh – main entry point.
    // Iterates the cells in the given chunk, runs marching-tetrahedra
    // polygonisation, and returns the intermediate MeshBuildData ready for
    // Mesh assembly.  All inputs are plain value / array types.
    // -------------------------------------------------------------------------
    public static MeshBuildData BuildChunkMesh(
        float[] densitySamples,
        byte[] cellMaterialIndices,
        Vector3Int chunkCoordinate,
        int cellsPerChunkAxis,
        float voxelSizeMeters,
        int submeshCount,
        int totalSamplesX,
        int totalSamplesY)
    {
        int totalCellsX = totalSamplesX - 1;
        int totalCellsY = totalSamplesY - 1;

        MeshBuildData buildData = new MeshBuildData(submeshCount);
        Vector3Int chunkStartCell = new Vector3Int(
            chunkCoordinate.x * cellsPerChunkAxis,
            chunkCoordinate.y * cellsPerChunkAxis,
            chunkCoordinate.z * cellsPerChunkAxis);
        Vector3 chunkOrigin = new Vector3(
            chunkStartCell.x * voxelSizeMeters,
            chunkStartCell.y * voxelSizeMeters,
            chunkStartCell.z * voxelSizeMeters);

        float[] cubeDensities = new float[8];
        Vector3[] cubePositions = new Vector3[8];

        for (int z = 0; z < cellsPerChunkAxis; z++)
        {
            int globalZ = chunkStartCell.z + z;
            for (int y = 0; y < cellsPerChunkAxis; y++)
            {
                int globalY = chunkStartCell.y + y;
                for (int x = 0; x < cellsPerChunkAxis; x++)
                {
                    int globalX = chunkStartCell.x + x;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        int sampleX = globalX + (int)CubeCornerOffsets[corner].x;
                        int sampleY = globalY + (int)CubeCornerOffsets[corner].y;
                        int sampleZ = globalZ + (int)CubeCornerOffsets[corner].z;
                        cubeDensities[corner] = densitySamples[VoxelDataStore.GetSampleIndex(sampleX, sampleY, sampleZ, totalSamplesX, totalSamplesY)];
                        cubePositions[corner] = new Vector3(
                            sampleX * voxelSizeMeters,
                            sampleY * voxelSizeMeters,
                            sampleZ * voxelSizeMeters) - chunkOrigin;
                    }

                    if (!VoxelTerrainGenerator.CubeIntersectsSurface(cubeDensities, IsoLevel))
                    {
                        continue;
                    }

                    int materialIndex = cellMaterialIndices[VoxelDataStore.GetCellIndex(globalX, globalY, globalZ, totalCellsX, totalCellsY)];
                    for (int tetraIndex = 0; tetraIndex < CubeTetrahedra.GetLength(0); tetraIndex++)
                    {
                        PolygoniseTetrahedron(cubePositions, cubeDensities, tetraIndex, materialIndex, buildData);
                    }
                }
            }
        }

        return buildData;
    }

    // -------------------------------------------------------------------------
    // Marching-tetrahedra geometry helpers
    // -------------------------------------------------------------------------

    private static void PolygoniseTetrahedron(Vector3[] cubePositions, float[] cubeDensities, int tetrahedronIndex, int materialIndex, MeshBuildData buildData)
    {
        int a = CubeTetrahedra[tetrahedronIndex, 0];
        int b = CubeTetrahedra[tetrahedronIndex, 1];
        int c = CubeTetrahedra[tetrahedronIndex, 2];
        int d = CubeTetrahedra[tetrahedronIndex, 3];

        int[] tetraCorners = { a, b, c, d };
        List<int> inside = new List<int>(4);
        List<int> outside = new List<int>(4);
        for (int i = 0; i < tetraCorners.Length; i++)
        {
            if (cubeDensities[tetraCorners[i]] > IsoLevel)
            {
                inside.Add(tetraCorners[i]);
            }
            else
            {
                outside.Add(tetraCorners[i]);
            }
        }

        if (inside.Count == 0 || inside.Count == 4)
        {
            return;
        }

        if (inside.Count == 1)
        {
            Vector3 v0 = VoxelTerrainGenerator.InterpolateEdge(cubePositions[inside[0]], cubePositions[outside[0]], cubeDensities[inside[0]], cubeDensities[outside[0]], IsoLevel);
            Vector3 v1 = VoxelTerrainGenerator.InterpolateEdge(cubePositions[inside[0]], cubePositions[outside[1]], cubeDensities[inside[0]], cubeDensities[outside[1]], IsoLevel);
            Vector3 v2 = VoxelTerrainGenerator.InterpolateEdge(cubePositions[inside[0]], cubePositions[outside[2]], cubeDensities[inside[0]], cubeDensities[outside[2]], IsoLevel);
            AddTriangle(v0, v1, v2, inside, outside, cubePositions, materialIndex, buildData);
            return;
        }

        if (inside.Count == 3)
        {
            Vector3 v0 = VoxelTerrainGenerator.InterpolateEdge(cubePositions[outside[0]], cubePositions[inside[0]], cubeDensities[outside[0]], cubeDensities[inside[0]], IsoLevel);
            Vector3 v1 = VoxelTerrainGenerator.InterpolateEdge(cubePositions[outside[0]], cubePositions[inside[1]], cubeDensities[outside[0]], cubeDensities[inside[1]], IsoLevel);
            Vector3 v2 = VoxelTerrainGenerator.InterpolateEdge(cubePositions[outside[0]], cubePositions[inside[2]], cubeDensities[outside[0]], cubeDensities[inside[2]], IsoLevel);
            AddTriangle(v0, v2, v1, inside, outside, cubePositions, materialIndex, buildData);
            return;
        }

        if (inside.Count == 2)
        {
            Vector3 v0 = VoxelTerrainGenerator.InterpolateEdge(cubePositions[inside[0]], cubePositions[outside[0]], cubeDensities[inside[0]], cubeDensities[outside[0]], IsoLevel);
            Vector3 v1 = VoxelTerrainGenerator.InterpolateEdge(cubePositions[inside[0]], cubePositions[outside[1]], cubeDensities[inside[0]], cubeDensities[outside[1]], IsoLevel);
            Vector3 v2 = VoxelTerrainGenerator.InterpolateEdge(cubePositions[inside[1]], cubePositions[outside[0]], cubeDensities[inside[1]], cubeDensities[outside[0]], IsoLevel);
            Vector3 v3 = VoxelTerrainGenerator.InterpolateEdge(cubePositions[inside[1]], cubePositions[outside[1]], cubeDensities[inside[1]], cubeDensities[outside[1]], IsoLevel);
            AddTriangle(v0, v1, v2, inside, outside, cubePositions, materialIndex, buildData);
            AddTriangle(v2, v1, v3, inside, outside, cubePositions, materialIndex, buildData);
        }
    }

    private static void AddTriangle(
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        List<int> insideIndices,
        List<int> outsideIndices,
        Vector3[] cubePositions,
        int materialIndex,
        MeshBuildData buildData)
    {
        Vector3 insideCentroid = Vector3.zero;
        for (int i = 0; i < insideIndices.Count; i++)
        {
            insideCentroid += cubePositions[insideIndices[i]];
        }
        insideCentroid /= Mathf.Max(1, insideIndices.Count);

        Vector3 outsideCentroid = Vector3.zero;
        for (int i = 0; i < outsideIndices.Count; i++)
        {
            outsideCentroid += cubePositions[outsideIndices[i]];
        }
        outsideCentroid /= Mathf.Max(1, outsideIndices.Count);

        Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
        Vector3 desiredDirection = outsideCentroid - insideCentroid;
        if (Vector3.Dot(normal, desiredDirection) < 0f)
        {
            (v1, v2) = (v2, v1);
        }

        int clampedMaterialIndex = Mathf.Clamp(materialIndex, 0, buildData.submeshIndices.Length - 1);
        List<int> indices = buildData.submeshIndices[clampedMaterialIndex];
        int vertexStart = buildData.vertices.Count;
        buildData.vertices.Add(v0);
        buildData.vertices.Add(v1);
        buildData.vertices.Add(v2);
        indices.Add(vertexStart);
        indices.Add(vertexStart + 1);
        indices.Add(vertexStart + 2);
    }
}
