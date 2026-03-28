using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ProceduralVoxelTerrain
{
    private void EnsureChunkObjects()
    {
        Transform generatedRoot = EnsureGeneratedRoot();
        generatedChunks.Clear();
        Vector3 chunkSize = Vector3.one * (cellsPerChunkAxis * voxelSizeMeters);
        HashSet<string> expectedChunkNames = new HashSet<string>(StringComparer.Ordinal);

        for (int z = 0; z < chunkCounts.z; z++)
        {
            for (int y = 0; y < chunkCounts.y; y++)
            {
                for (int x = 0; x < chunkCounts.x; x++)
                {
                    Vector3Int chunkCoordinate = new Vector3Int(x, y, z);
                    string chunkName = $"Chunk_{x:00}_{y:00}_{z:00}";
                    expectedChunkNames.Add(chunkName);
                    Transform chunkTransform = generatedRoot.Find(chunkName);
                    ProceduralVoxelTerrainChunk chunk = null;
                    if (chunkTransform != null)
                    {
                        chunk = chunkTransform.GetComponent<ProceduralVoxelTerrainChunk>();
                    }

                    if (chunk == null)
                    {
                        GameObject chunkObject = new GameObject(chunkName);
                        chunkObject.layer = gameObject.layer;
                        chunkObject.transform.SetParent(generatedRoot, false);
                        chunk = chunkObject.AddComponent<ProceduralVoxelTerrainChunk>();
                    }

                    chunk.transform.localPosition = Vector3.Scale(chunkCoordinate, chunkSize);
                    chunk.transform.localRotation = Quaternion.identity;
                    chunk.transform.localScale = Vector3.one;
                    chunk.Initialize(chunkCoordinate, sharedChunkMaterials);
                    generatedChunks[chunkCoordinate] = chunk;
                }
            }
        }

        List<Transform> staleChildren = new List<Transform>();
        foreach (Transform child in generatedRoot)
        {
            if (!expectedChunkNames.Contains(child.name))
            {
                staleChildren.Add(child);
            }
        }

        for (int i = 0; i < staleChildren.Count; i++)
        {
            if (Application.isPlaying)
            {
                Destroy(staleChildren[i].gameObject);
            }
            else
            {
                DestroyImmediate(staleChildren[i].gameObject);
            }
        }
    }

    private List<Vector3Int> BuildChunkCoordinatesInGenerationOrder()
    {
        if (IsRuntimeStreamingModeActive)
        {
            return BuildRuntimeStreamingChunkCoordinatesInGenerationOrder();
        }

        List<Vector3Int> chunkCoordinates = new List<Vector3Int>(TotalChunkCount);
        for (int z = 0; z < Mathf.Max(1, chunkCounts.z); z++)
        {
            for (int y = 0; y < Mathf.Max(1, chunkCounts.y); y++)
            {
                for (int x = 0; x < Mathf.Max(1, chunkCounts.x); x++)
                {
                    Vector3Int chunkCoordinate = new Vector3Int(x, y, z);
                    if (generatedChunks.ContainsKey(chunkCoordinate))
                    {
                        chunkCoordinates.Add(chunkCoordinate);
                    }
                }
            }
        }

        return chunkCoordinates;
    }

    private List<Vector3Int> BuildRuntimeStreamingChunkCoordinatesInGenerationOrder()
    {
        return streamingManager.BuildRuntimeStreamingChunkOrder(
            GetRuntimeStreamingGenerationAnchorChunk(),
            chunkCounts,
            generatedChunks);
    }

    private void RebuildAllChunks()
    {
        List<Vector3Int> chunkCoordinates = BuildChunkCoordinatesInGenerationOrder();
        for (int i = 0; i < chunkCoordinates.Count; i++)
        {
            RebuildChunk(chunkCoordinates[i]);
        }
    }

    private void RebuildChunk(Vector3Int chunkCoordinate)
    {
        CommitChunkMesh(chunkCoordinate, BuildChunkMesh(chunkCoordinate));
    }

    private void CommitChunkMesh(Vector3Int chunkCoordinate, ChunkMeshBuilder.MeshBuildData buildData)
    {
        if (!generatedChunks.TryGetValue(chunkCoordinate, out ProceduralVoxelTerrainChunk chunk) || chunk == null)
        {
            return;
        }

        if (buildData == null || buildData.vertices.Count == 0)
        {
            chunk.ClearMesh();
            return;
        }

        Mesh mesh = new Mesh
        {
            name = $"Voxel Chunk {chunkCoordinate.x}-{chunkCoordinate.y}-{chunkCoordinate.z}",
            indexFormat = IndexFormat.UInt32
        };
        mesh.SetVertices(buildData.vertices);
        mesh.subMeshCount = buildData.submeshIndices.Length;
        for (int i = 0; i < buildData.submeshIndices.Length; i++)
        {
            mesh.SetTriangles(buildData.submeshIndices[i], i, false);
        }

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        chunk.ApplyMesh(mesh, sharedChunkMaterials);
    }

    private ChunkMeshBuilder.MeshBuildData BuildChunkMesh(Vector3Int chunkCoordinate)
    {
        return ChunkMeshBuilder.BuildChunkMesh(
            densitySamples,
            cellMaterialIndices,
            chunkCoordinate,
            cellsPerChunkAxis,
            voxelSizeMeters,
            Mathf.Max(1, materialDefinitions.Count),
            TotalSamplesX,
            TotalSamplesY);
    }

    private Transform GetGeneratedRoot()
    {
        return transform.Find(GeneratedRootName);
    }

    private Transform EnsureGeneratedRoot()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot != null)
        {
            return generatedRoot;
        }

        GameObject rootObject = new GameObject(GeneratedRootName);
        rootObject.layer = gameObject.layer;
        rootObject.transform.SetParent(transform, false);
        rootObject.transform.localPosition = Vector3.zero;
        rootObject.transform.localRotation = Quaternion.identity;
        rootObject.transform.localScale = Vector3.one;
        return rootObject.transform;
    }

    private bool IsWithinWorld(Vector3 localPoint)
    {
        Vector3 worldSize = TotalWorldSize;
        return localPoint.x >= 0f && localPoint.x <= worldSize.x
            && localPoint.y >= 0f && localPoint.y <= worldSize.y
            && localPoint.z >= 0f && localPoint.z <= worldSize.z;
    }

    private Vector3Int GetChunkCoordinateForCell(int cellX, int cellY, int cellZ)
    {
        return new Vector3Int(
            Mathf.Clamp(cellX / cellsPerChunkAxis, 0, chunkCounts.x - 1),
            Mathf.Clamp(cellY / cellsPerChunkAxis, 0, chunkCounts.y - 1),
            Mathf.Clamp(cellZ / cellsPerChunkAxis, 0, chunkCounts.z - 1));
    }

    private int GetSampleIndex(int x, int y, int z)
        => VoxelDataStore.GetSampleIndex(x, y, z, TotalSamplesX, TotalSamplesY);

    private int GetCellIndex(int x, int y, int z)
        => VoxelDataStore.GetCellIndex(x, y, z, TotalCellsX, TotalCellsY);

    private void GetCellCoordinates(int cellIndex, out int x, out int y, out int z)
        => VoxelDataStore.GetCellCoordinates(cellIndex, TotalCellsX, TotalCellsY, out x, out y, out z);
}
