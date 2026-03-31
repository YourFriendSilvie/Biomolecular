using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ProceduralVoxelTerrain
{
    private void EnsureChunkObjects(HashSet<Vector3Int> surfaceFilter = null)
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
                    // When a surface filter is provided, only create objects for surface chunks.
                    if (surfaceFilter != null && !surfaceFilter.Contains(chunkCoordinate))
                    {
                        continue;
                    }

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
                    chunk.Initialize(chunkCoordinate, new Material[] { sharedTerrainMaterial });
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

    private List<Vector3Int> BuildChunkCoordinatesInGenerationOrder(HashSet<Vector3Int> surfaceFilter = null)
    {
        if (IsRuntimeStreamingModeActive)
        {
            return BuildRuntimeStreamingChunkCoordinatesInGenerationOrder(surfaceFilter);
        }

        List<Vector3Int> chunkCoordinates = new List<Vector3Int>(surfaceFilter?.Count ?? TotalChunkCount);
        for (int z = 0; z < Mathf.Max(1, chunkCounts.z); z++)
        {
            for (int y = 0; y < Mathf.Max(1, chunkCounts.y); y++)
            {
                for (int x = 0; x < Mathf.Max(1, chunkCounts.x); x++)
                {
                    Vector3Int chunkCoordinate = new Vector3Int(x, y, z);
                    if (generatedChunks.ContainsKey(chunkCoordinate) &&
                        (surfaceFilter == null || surfaceFilter.Contains(chunkCoordinate)))
                    {
                        chunkCoordinates.Add(chunkCoordinate);
                    }
                }
            }
        }

        return chunkCoordinates;
    }

    private List<Vector3Int> BuildRuntimeStreamingChunkCoordinatesInGenerationOrder(HashSet<Vector3Int> surfaceFilter = null)
    {
        List<Vector3Int> coords = streamingManager.BuildRuntimeStreamingChunkOrder(
            GetRuntimeStreamingGenerationAnchorChunk(),
            chunkCounts,
            generatedChunks);
        if (surfaceFilter != null)
        {
            coords.RemoveAll(c => !surfaceFilter.Contains(c));
        }

        return coords;
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

    private void CommitChunkMesh(Vector3Int chunkCoordinate, ChunkMeshBuilder.MeshBuildData buildData,
        Queue<(Mesh mesh, MeshCollider collider, Task bakeTask)> asyncBakeQueue = null)
    {
        if (!generatedChunks.TryGetValue(chunkCoordinate, out ProceduralVoxelTerrainChunk chunk) || chunk == null)
        {
            return;
        }   
        chunk.currentLod = GetChunkLod(chunkCoordinate);
        if (buildData == null || buildData.vertices.Count < 3 || buildData.triangles.Count < 3)
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
        mesh.subMeshCount = 1;
        mesh.SetTriangles(buildData.triangles, 0, false);
        mesh.SetNormals(buildData.normals);

        // Feed the packed Material IDs directly into the mesh UVs!
        if (buildData.uvs != null && buildData.uvs.Count > 0)
            mesh.SetUVs(0, buildData.uvs);

        mesh.RecalculateBounds();

        if (asyncBakeQueue != null)
        {
            MeshCollider collider = chunk.ApplyMeshVisualOnly(mesh, new Material[] { sharedTerrainMaterial });

            // Unity 6+ uses GetEntityId() for thread-safe background physics baking!
            var entityId = mesh.GetEntityId();
            Task bakeTask = Task.Run(() => Physics.BakeMesh(entityId, false));
            asyncBakeQueue.Enqueue((mesh, collider, bakeTask));
        }
        else
        {
            chunk.ApplyMesh(mesh, new Material[] { sharedTerrainMaterial });
        }
    }

    private ChunkMeshBuilder.MeshBuildData BuildChunkMesh(Vector3Int chunkCoordinate)
    {
        // Compute transition mask based on neighbor LODs so mesher can stitch to lower-detail neighbors
        int transitionMask = ComputeChunkTransitionMask(chunkCoordinate);

        // We removed the old ComputeVertexColors step entirely! The Burst Job does everything now.
        return BuildChunkMesh(chunkCoordinate, transitionMask);
    }

    // Overload: allows caller to provide precomputed transitionMask so this method is safe to call from background threads
    private ChunkMeshBuilder.MeshBuildData BuildChunkMesh(Vector3Int chunkCoordinate, int transitionMask)
    {
        // If running on the main thread, compare the provided precomputed transitionMask to the deterministic runtime mask
        // and log disagreements to help debug offline precompute vs runtime decisions. Skip this check on background threads.
        try
        {
            if (!Thread.CurrentThread.IsBackground && Application.isPlaying)
            {
                int runtimeMask = ComputeChunkTransitionMask(chunkCoordinate);
                if (runtimeMask != transitionMask)
                {
                    Debug.LogWarning($"Transition mask disagreement for chunk {chunkCoordinate}: precomputed={transitionMask} runtime={runtimeMask}");
                }
            }
        }
        catch (Exception)
        {
            // Be conservative — never throw from mesh build helper due to logging.
        }

        return ChunkMeshBuilder.BuildChunkMesh(
            densitySamples,
            chunkCoordinate,
            cellsPerChunkAxis,
            voxelSizeMeters,
            TotalSamplesX,
            TotalSamplesY,
            TotalSamplesZ,
            cellMaterialIndices,
            normalSampleCells,
            transitionMask);
    }

    // Simple LOD estimator used to decide where transition stitching is required.
    // LOD 0 = highest detail; larger numbers indicate progressively lower detail.
    private int GetChunkLod(Vector3Int chunkCoordinate)
    {
        Vector3 chunkSize = Vector3.one * (cellsPerChunkAxis * voxelSizeMeters);
        Vector3 localCenter = Vector3.Scale(chunkCoordinate, chunkSize) + (chunkSize * 0.5f);
        Vector3 worldCenter = transform.TransformPoint(localCenter);

        Vector3 anchorPos;
        if (runtimeStreamingAnchor != null)
            anchorPos = runtimeStreamingAnchor.position;
        else if (Camera.main != null)
            anchorPos = Camera.main.transform.position;
        else
            anchorPos = transform.position;

        int previousLod = 0;
        ProceduralVoxelTerrainChunk chunk = null;
        if (generatedChunks.TryGetValue(chunkCoordinate, out chunk) && chunk != null)
        {
            previousLod = chunk.currentLod;
        }

        int lod = TerrainLODUtility.ComputeChunkLodWithHysteresis(
            worldCenter,
            anchorPos,
            ChunkWorldSizeMeters,
            maxLodLevels,
            lodDistanceFactor,
            lodHysteresisFraction,
            previousLod);

        return lod;
    }

    private int ComputeChunkTransitionMask(Vector3Int chunkCoordinate)
    {
        // Use deterministic, non-hysteresis LODs for neighbor comparisons so both sides agree on stitching.
        if (!Application.isPlaying) return 0;
        int mask = 0;
        // Compute chunk center and anchor position (same logic as GetChunkLod but without hysteresis)
        Vector3 chunkSize = Vector3.one * (cellsPerChunkAxis * voxelSizeMeters);
        Vector3 localCenter = Vector3.Scale(chunkCoordinate, chunkSize) + (chunkSize * 0.5f);
        Vector3 worldCenter = transform.TransformPoint(localCenter);

        Vector3 anchorPos;
        if (runtimeStreamingAnchor != null)
            anchorPos = runtimeStreamingAnchor.position;
        else if (Camera.main != null)
            anchorPos = Camera.main.transform.position;
        else
            anchorPos = transform.position;

        int baseLod = TerrainLODUtility.ComputeChunkLod(worldCenter, anchorPos, ChunkWorldSizeMeters, maxLodLevels, lodDistanceFactor);

        Vector3Int[] dirs = new Vector3Int[] {
            new Vector3Int(-1, 0, 0), // -X
            new Vector3Int(1, 0, 0),  // +X
            new Vector3Int(0, -1, 0), // -Y
            new Vector3Int(0, 1, 0),  // +Y
            new Vector3Int(0, 0, -1), // -Z
            new Vector3Int(0, 0, 1)   // +Z
        };

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3Int n = chunkCoordinate + dirs[i];
            if (n.x < 0 || n.x >= chunkCounts.x || n.y < 0 || n.y >= chunkCounts.y || n.z < 0 || n.z >= chunkCounts.z)
            {
                continue;
            }

            // Compute neighbor center and deterministic base LOD (no hysteresis)
            Vector3 nLocalCenter = Vector3.Scale(n, chunkSize) + (chunkSize * 0.5f);
            Vector3 nWorldCenter = transform.TransformPoint(nLocalCenter);
            int neighborBaseLod = TerrainLODUtility.ComputeChunkLod(nWorldCenter, anchorPos, ChunkWorldSizeMeters, maxLodLevels, lodDistanceFactor);

            // Stitch when neighbor is coarser (>=1 LOD). This avoids holes when >1 LOD differences appear around rings.
            if (neighborBaseLod > baseLod)
            {
                mask |= (1 << i);
            }
        }

        return mask;
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

    private void OnDestroy()
    {
        // Clean up any persistent LUT natives allocated by the mesher.
        try { ChunkMeshBuilder.DisposeLuts(); } catch { }
    }

    private int GetSampleIndex(int x, int y, int z)
        => VoxelDataStore.GetSampleIndex(x, y, z, TotalSamplesX, TotalSamplesY);

    private int GetCellIndex(int x, int y, int z)
        => VoxelDataStore.GetCellIndex(x, y, z, TotalCellsX, TotalCellsY);

    private void GetCellCoordinates(int cellIndex, out int x, out int y, out int z)
        => VoxelDataStore.GetCellCoordinates(cellIndex, TotalCellsX, TotalCellsY, out x, out y, out z);
}
