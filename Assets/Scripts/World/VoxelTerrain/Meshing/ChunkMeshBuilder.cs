using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Biomolecular.VoxelTerrain.Meshing;

internal static class ChunkMeshBuilder
{
    internal const float IsoLevel = 0f;

    // 1. PERSISTENT LOOKUP TABLES
    private static NativeArray<int> s_EdgeTableNative;
    private static NativeArray<int> s_TriTableNative;
    private static NativeArray<byte> s_TransitionCellClassNative;
    private static NativeArray<byte> s_TransitionCornerDataNative;
    private static NativeArray<int> s_TransitionTriTableNative;
    private static bool s_LutsInitialized = false;
    private static readonly object s_LutInitLock = new object();

    private static void EnsureLutsAllocated()
    {
        if (s_LutsInitialized && s_EdgeTableNative.IsCreated) return;

        lock (s_LutInitLock)
        {
            if (s_LutsInitialized) return;
            s_EdgeTableNative = new NativeArray<int>(TransvoxelTables.EdgeTable, Allocator.Persistent);
            s_TriTableNative = new NativeArray<int>(TransvoxelTables.TriTableFlat, Allocator.Persistent);
            s_TransitionCellClassNative = new NativeArray<byte>(TransvoxelTables.TransitionCellClass, Allocator.Persistent);
            s_TransitionCornerDataNative = new NativeArray<byte>(TransvoxelTables.TransitionCornerData, Allocator.Persistent);
            s_TransitionTriTableNative = new NativeArray<int>(TransvoxelTables.TransitionCellData, Allocator.Persistent);
            s_LutsInitialized = true;
        }
    }

    internal sealed class MeshBuildData
    {
        public readonly List<Vector3> vertices;
        public readonly List<int> triangles;
        public readonly List<Vector3> normals;
        public readonly List<Vector4> uvs; // x: PrimaryID, y: SecondaryID, z: BlendWeight, w: slopeWeight

        internal MeshBuildData(int estimatedVertices = 0)
        {
            vertices = new List<Vector3>(estimatedVertices);
            triangles = new List<int>(estimatedVertices * 6);
            normals = new List<Vector3>(estimatedVertices);
            uvs = new List<Vector4>(estimatedVertices);
        }
    }
    // DISPOSAL LOGIC: Clean up persistent NativeArrays to prevent memory leaks
    public static void DisposeLuts()
    {
        lock (s_LutInitLock)
        {
            if (s_EdgeTableNative.IsCreated)
            {
                s_EdgeTableNative.Dispose();
            }
            if (s_TriTableNative.IsCreated)
            {
                s_TriTableNative.Dispose();
            }
            if (s_TransitionCellClassNative.IsCreated)
            {
                s_TransitionCellClassNative.Dispose();
            }
            if (s_TransitionCornerDataNative.IsCreated)
            {
                s_TransitionCornerDataNative.Dispose();
            }
            if (s_TransitionTriTableNative.IsCreated)
            {
                s_TransitionTriTableNative.Dispose();
            }
            s_LutsInitialized = false;
        }
    }
    // 2. THE BURST JOB
    [BurstCompile]
    internal struct TransvoxelMesherJob : IJob
    {
        [ReadOnly] public NativeArray<float> densitySamples;
        [ReadOnly] public NativeArray<byte> cellMaterialIndices;
        [ReadOnly] public NativeArray<int> edgeTable;
        [ReadOnly] public NativeArray<int> triTable;
        [ReadOnly] public NativeArray<byte> transitionCellClass;
        [ReadOnly] public NativeArray<int> transitionTriTable;
        [ReadOnly] public NativeArray<byte> transitionCornerData;

        public NativeList<float3> vertices;
        public NativeList<float3> normals;
        public NativeList<float4> packedMaterialUV;
        public NativeList<int> triangles;
        public NativeList<int> vertexContribCounts;
        public NativeParallelHashMap<long, int> vertMap;
        public NativeArray<int> transitionVertexIndices;
        public int currentTransitionFaceIndex;

        public int3 totalSamples;
        public float voxelSize;
        public int transitionMask;

        public void Execute()
        {
            int sx = totalSamples.x;
            int sy = totalSamples.y;
            int sz = totalSamples.z;

            // 1. REGULAR CELL PASS
            // We iterate through the full INTERNAL chunk volume (size N)
            for (int z = 1; z <= sz - 3; z++)
            {
                for (int y = 1; y <= sy - 3; y++)
                {
                    for (int x = 1; x <= sx - 3; x++)
                    {
                        // Only skip if this cell is exactly on an active transition face.
                        if (IsCellOnTransitionFace(x, y, z)) continue;

                        BuildRegularCell(x, y, z);
                    }
                }
            }

            // 2. TRANSITION FACE PASS
            // Initialize transitionVertexIndices array to -1 (no owner yet)
            int sx2 = totalSamples.x;
            int sy2 = totalSamples.y;
            int sz2 = totalSamples.z;
            int faceSize = sx2 * sy2 * sz2;
            if (!transitionVertexIndices.IsCreated || transitionVertexIndices.Length != faceSize * 6)
            {
                // Should not happen because allocated by caller, but guard defensively.
            }

            for (int i = 0; i < 6; i++)
            {
                if ((transitionMask & (1 << i)) != 0)
                {
                    // Allocate a temporary triangle buffer for this face so we can later append atomically.
                    NativeList<int> localTris = new NativeList<int>(Allocator.Temp);
                    BuildTransitionFace(i, ref localTris);

                    // Append localTris into the global triangles list
                    for (int ti = 0; ti < localTris.Length; ti++) triangles.Add(localTris[ti]);

                    // Dispose local buffer
                    localTris.Dispose();
                }
            }
        }
        private long GetUniversalEdgeKey(int3 pA, int3 pB)
        {
            // Always order them so min is first. 
            // This ensures (A to B) and (B to A) generate the exact same key!
            int3 minP = math.min(pA, pB);
            int3 maxP = math.max(pA, pB);

            // Pack the two 3D coordinates into a single 64-bit long
            // Each coordinate gets 10 bits (0-1023), which is plenty for chunks
            return ((long)minP.x & 0x3FFL) |
                   (((long)minP.y & 0x3FFL) << 10) |
                   (((long)minP.z & 0x3FFL) << 20) |
                   (((long)maxP.x & 0x3FFL) << 30) |
                   (((long)maxP.y & 0x3FFL) << 40) |
                   (((long)maxP.z & 0x3FFL) << 50);
        }
        private void GetTransitionEdgeEndpoints(int3 origin, int3 uDir, int3 vDir, int u, int v, int edgeIdx, out int3 ep0, out int3 ep1)
        {
            int3 local0 = 0, local1 = 0;
            switch (edgeIdx)
            {
                case 0: local0 = new int3(u, v, 0); local1 = new int3(u + 1, v, 0); break;
                case 1: local0 = new int3(u + 1, v, 0); local1 = new int3(u + 2, v, 0); break;
                case 2: local0 = new int3(u, v + 1, 0); local1 = new int3(u + 1, v + 1, 0); break;
                case 3: local0 = new int3(u + 1, v + 1, 0); local1 = new int3(u + 2, v + 1, 0); break;
                case 4: local0 = new int3(u, v + 2, 0); local1 = new int3(u + 1, v + 2, 0); break;
                case 5: local0 = new int3(u + 1, v + 2, 0); local1 = new int3(u + 2, v + 2, 0); break;
                case 6: local0 = new int3(u, v, 0); local1 = new int3(u, v + 1, 0); break;
                case 7: local0 = new int3(u, v + 1, 0); local1 = new int3(u, v + 2, 0); break;
                case 8: local0 = new int3(u + 1, v, 0); local1 = new int3(u + 1, v + 1, 0); break;
                case 9: local0 = new int3(u + 1, v + 1, 0); local1 = new int3(u + 1, v + 2, 0); break;
                case 10: local0 = new int3(u + 2, v, 0); local1 = new int3(u + 2, v + 1, 0); break;
                case 11: local0 = new int3(u + 2, v + 1, 0); local1 = new int3(u + 2, v + 2, 0); break;
            }
            ep0 = origin + (uDir * local0.x) + (vDir * local0.y);
            ep1 = origin + (uDir * local1.x) + (vDir * local1.y);
        }
        private bool IsCellOnTransitionFace(int x, int y, int z)
        {
            // A cell is only 'on a transition face' if the mask for that 
            // specific direction is actually ACTIVE.
            int limitX = totalSamples.x - 3;
            int limitY = totalSamples.y - 3;
            int limitZ = totalSamples.z - 3;

            // We check BOTH the coordinate AND the mask bit.
            // If the mask bit is 0, the Regular Pass MUST draw this cell.
            if (x == 1 && (transitionMask & 1) != 0) return true;  // -X
            if (x == limitX && (transitionMask & 2) != 0) return true;  // +X
            if (y == 1 && (transitionMask & 4) != 0) return true;  // -Y
            if (y == limitY && (transitionMask & 8) != 0) return true;  // +Y
            if (z == 1 && (transitionMask & 16) != 0) return true; // -Z
            if (z == limitZ && (transitionMask & 32) != 0) return true; // +Z

            return false;
        }
        private void BuildRegularCell(int x, int y, int z)
        {
            int sx = totalSamples.x; int sy = totalSamples.y;
            int i0 = x + y * sx + z * sx * sy;
            int i1 = (x + 1) + y * sx + z * sx * sy;
            int i2 = (x + 1) + y * sx + (z + 1) * sx * sy;
            int i3 = x + y * sx + (z + 1) * sx * sy;
            int i4 = x + (y + 1) * sx + z * sx * sy;
            int i5 = (x + 1) + (y + 1) * sx + z * sx * sy;
            int i6 = (x + 1) + (y + 1) * sx + (z + 1) * sx * sy;
            int i7 = x + (y + 1) * sx + (z + 1) * sx * sy;

            float c0 = densitySamples[i0]; float c1 = densitySamples[i1];
            float c2 = densitySamples[i2]; float c3 = densitySamples[i3];
            float c4 = densitySamples[i4]; float c5 = densitySamples[i5];
            float c6 = densitySamples[i6]; float c7 = densitySamples[i7];

            int cubeIndex = 0;
            if (c0 < 0) cubeIndex |= 1; if (c1 < 0) cubeIndex |= 2;
            if (c2 < 0) cubeIndex |= 4; if (c3 < 0) cubeIndex |= 8;
            if (c4 < 0) cubeIndex |= 16; if (c5 < 0) cubeIndex |= 32;
            if (c6 < 0) cubeIndex |= 64; if (c7 < 0) cubeIndex |= 128;

            if (edgeTable[cubeIndex] == 0) return;

            // Material logic (simplified here for brevity, use your full frequent-ID logic!)
            byte m1 = cellMaterialIndices[x + y * (sx - 1) + z * (sx - 1) * (sy - 1)];

            int triBase = cubeIndex * 16;
            for (int i = 0; triTable[triBase + i] != -1; i += 3)
            {
                int vA = GetOrCreateVertex(x, y, z, triTable[triBase + i], m1, m1, 0, c0, c1, c2, c3, c4, c5, c6, c7);
                int vB = GetOrCreateVertex(x, y, z, triTable[triBase + i + 1], m1, m1, 0, c0, c1, c2, c3, c4, c5, c6, c7);
                int vC = GetOrCreateVertex(x, y, z, triTable[triBase + i + 2], m1, m1, 0, c0, c1, c2, c3, c4, c5, c6, c7);

                triangles.Add(vA); triangles.Add(vC); triangles.Add(vB); // Unity order
                float3 pA = vertices[vA]; float3 pB = vertices[vB]; float3 pC = vertices[vC];
                float3 faceNorm = math.normalize(math.cross(pC - pA, pB - pA));
                normals[vA] += faceNorm; normals[vB] += faceNorm; normals[vC] += faceNorm;
                vertexContribCounts[vA]++; vertexContribCounts[vB]++; vertexContribCounts[vC]++;
            }
        }

        private void BuildTransitionFace(int faceIndex, ref NativeList<int> localTriangles)
        {
            // 1. DIRECTIONAL MAPPING.
            // We map 2D (u, v) coordinates to 3D (x, y, z) based on which face we are 'sewing'.
            // Face Indices: 0:-X, 1:+X, 2:-Y, 3:+Y, 4:-Z, 5:+Z
            int3 uDir = 0, vDir = 0;
            int3 origin = 0;
            int size = totalSamples.x - 3; // interior width of the chunk

            switch (faceIndex)
            {
                // Each case starts exactly at the boundary after the ghost layer.
                case 0: origin = new int3(1, 1, 1); uDir = new int3(0, 0, 1); vDir = new int3(0, 1, 0); break; // -X
                case 1: origin = new int3(size + 1, 1, 1); uDir = new int3(0, 0, 1); vDir = new int3(0, 1, 0); break; // +X
                case 2: origin = new int3(1, 1, 1); uDir = new int3(1, 0, 0); vDir = new int3(0, 0, 1); break; // -Y
                case 3: origin = new int3(1, size + 1, 1); uDir = new int3(1, 0, 0); vDir = new int3(0, 0, 1); break; // +Y
                case 4: origin = new int3(1, 1, 1); uDir = new int3(1, 0, 0); vDir = new int3(0, 1, 0); break; // -Z
                case 5: origin = new int3(1, 1, size + 1); uDir = new int3(1, 0, 0); vDir = new int3(0, 1, 0); break; // +Z
            }
            // Mark which face index is currently being processed so transition helper routines can deterministically own vertices.
            currentTransitionFaceIndex = faceIndex;

            // 2. THE 2D TRANSITION LOOP
            // We iterate across the face in 2-voxel steps to match the LOD 1 resolution.
            for (int v = 0; v < size; v += 2)
            {
                for (int u = 0; u < size; u += 2)
                {
                    // Sample the 9 transition corners (Low-res corners and High-res midpoints)
                    float c0 = GetTransitionDensity(origin, uDir, vDir, u, v, 0);
                    float c1 = GetTransitionDensity(origin, uDir, vDir, u, v, 1);
                    float c2 = GetTransitionDensity(origin, uDir, vDir, u, v, 2);
                    float c3 = GetTransitionDensity(origin, uDir, vDir, u, v, 3);
                    float c4 = GetTransitionDensity(origin, uDir, vDir, u, v, 4);
                    float c5 = GetTransitionDensity(origin, uDir, vDir, u, v, 5);
                    float c6 = GetTransitionDensity(origin, uDir, vDir, u, v, 6);
                    float c7 = GetTransitionDensity(origin, uDir, vDir, u, v, 7);
                    float c8 = GetTransitionDensity(origin, uDir, vDir, u, v, 8);

                    // Build the 9-bit case index
                    int transitionCase = 0;
                    if (c0 < 0) transitionCase |= 1; if (c1 < 0) transitionCase |= 2;
                    if (c2 < 0) transitionCase |= 4; if (c3 < 0) transitionCase |= 8;
                    if (c4 < 0) transitionCase |= 16; if (c5 < 0) transitionCase |= 32;
                    if (c6 < 0) transitionCase |= 64; if (c7 < 0) transitionCase |= 128;
                    if (c8 < 0) transitionCase |= 256;

                    if (transitionCase == 0 || transitionCase == 511) continue;

                    // Map to one of the 56 equivalence classes
                    byte classIndex = transitionCellClass[transitionCase];
                    bool inverted = (classIndex & 0x80) != 0;
                    int actualClass = classIndex & 0x7F;

                    // 3. TRIANGULATION
                    int triBase = actualClass * 20;
                    for (int i = 0; i < 20; i += 3)
                    {
                        int e0 = transitionTriTable[triBase + i];
                        if (e0 == -1) break; // End of triangles for this case
                        int e1 = transitionTriTable[triBase + i + 1];
                        int e2 = transitionTriTable[triBase + i + 2];

                        // 3. CREATE TRANSITION VERTICES
                        // We use the 9 sampled densities (c0-c8) to find the edge crossings
                        int vA = GetOrCreateTransitionVertex(origin, uDir, vDir, u, v, e0, c0, c1, c2, c3, c4, c5, c6, c7, c8);
                        int vB = GetOrCreateTransitionVertex(origin, uDir, vDir, u, v, e1, c0, c1, c2, c3, c4, c5, c6, c7, c8);
                        int vC = GetOrCreateTransitionVertex(origin, uDir, vDir, u, v, e2, c0, c1, c2, c3, c4, c5, c6, c7, c8);

                        // 4. WINDING & INVERSION
                        // If the 'inverted' bit was set in the table, we flip the triangle
                        // Write triangle indices into either per-face temp buffer (if present) or the global triangle list.
                        if (localTriangles.IsCreated)
                        {
                            if (inverted)
                            {
                                localTriangles.Add(vA); localTriangles.Add(vC); localTriangles.Add(vB);
                            }
                            else
                            {
                                localTriangles.Add(vA); localTriangles.Add(vB); localTriangles.Add(vC);
                            }
                        }
                        else
                        {
                            if (inverted)
                            {
                                triangles.Add(vA); triangles.Add(vC); triangles.Add(vB);
                            }
                            else
                            {
                                triangles.Add(vA); triangles.Add(vB); triangles.Add(vC);
                            }
                        }

                        // Standard Normal accumulation (same as Regular Cell)
                        float3 faceNorm = math.normalize(math.cross(vertices[vC] - vertices[vA], vertices[vB] - vertices[vA]));
                        normals[vA] += faceNorm; normals[vB] += faceNorm; normals[vC] += faceNorm;
                        vertexContribCounts[vA]++; vertexContribCounts[vB]++; vertexContribCounts[vC]++;
                    }
                }
            }
        }
        private int GetOrCreateTransitionVertex(int3 origin, int3 uDir, int3 vDir, int u, int v, int edgeIdx, float c0, float c1, float c2, float c3, float c4, float c5, float c6, float c7, float c8)
        {
            // 1. Get the 3D endpoints for this transition edge
            int3 ep0, ep1;
            GetTransitionEdgeEndpoints(origin, uDir, vDir, u, v, edgeIdx, out ep0, out ep1);
            
            // 2. Generate the exact same Universal Key as the regular cells!
            long key = GetUniversalEdgeKey(ep0, ep1);

            if (vertMap.TryGetValue(key, out int existingIndex)) return existingIndex;

            // 2b. Check the per-face transitionVertexIndices buffer for a pre-owned vertex.
            int sx = totalSamples.x;
            int sy = totalSamples.y;
            int sz = totalSamples.z;
            int faceSize = sx * sy * sz;
            // Compute an index keyed by face-local ep0 coordinate to ensure deterministic ownership within the transition pass.
            int localIndex = (ep0.x) + (ep0.y) * sx + (ep0.z) * sx * sy; // 0..faceSize-1
            // We don't have faceIndex in params, but origin/uDir/vDir mapping corresponds to current face; derive faceIndex by matching uDir/vDir axes:
            int faceIndex = currentTransitionFaceIndex;
            if (uDir.x == 0 && uDir.z == 1 && vDir.y == 1) faceIndex = (origin.x == 1) ? 0 : 1; // -X or +X
            else if (uDir.x == 1 && vDir.z == 1) faceIndex = (origin.y == 1) ? 2 : 3; // -Y or +Y
            else if (uDir.x == 1 && vDir.y == 1) faceIndex = (origin.z == 1) ? 4 : 5; // -Z or +Z

            
            int globalIdx = faceIndex * faceSize + localIndex;
            if (globalIdx >= 0 && globalIdx < transitionVertexIndices.Length)
            {
                int stored = transitionVertexIndices[globalIdx];
                if (stored >= 0) return stored;
            }

            // Transition Edge Mapping (map edgeIdx (0-11) to interpolations between c0-c8 samples)
            float3 pos = CalculateTransitionEdgePos(origin, uDir, vDir, u, v, edgeIdx, c0, c1, c2, c3, c4, c5, c6, c7, c8);

            int newIdx = vertices.Length;
            vertices.Add(pos * voxelSize - (new float3(1, 1, 1) * voxelSize));
            normals.Add(new float3(0, 0, 0));
            vertexContribCounts.Add(0);

            
            // Sample a reasonable material id for transition vertices by sampling the central cell of the 3x3 transition patch.
            int3 sampleCell = origin + (uDir * (u + 1)) + (vDir * (v + 1));
            // Clamp to valid cell indices: cell array dimensions are (totalSamples - 1) per axis
            int maxCellX = totalSamples.x - 2;
            int maxCellY = totalSamples.y - 2;
            int maxCellZ = totalSamples.z - 2;
            int sxm1 = totalSamples.x - 1;
            int sym1 = totalSamples.y - 1;

            int cx = math.clamp(sampleCell.x, 0, maxCellX);
            int cy = math.clamp(sampleCell.y, 0, maxCellY);
            int cz = math.clamp(sampleCell.z, 0, maxCellZ);

            int matIdx = cx + cy * sxm1 + cz * sxm1 * sym1;
            matIdx = math.clamp(matIdx, 0, cellMaterialIndices.Length - 1);
            byte sampledMat = cellMaterialIndices[matIdx];

            // Pack sampled material into both primary and secondary slots with zero blend for now.
            packedMaterialUV.Add(new float4((float)sampledMat, (float)sampledMat, 0f, 0f));

            // Register in global vertMap and local transitionVertexIndices buffer
            vertMap.TryAdd(key, newIdx);
            if (globalIdx >= 0 && globalIdx < transitionVertexIndices.Length)
            {
                transitionVertexIndices[globalIdx] = newIdx;
            }
            return newIdx;
        }
        private float3 CalculateTransitionEdgePos(int3 origin, int3 uDir, int3 vDir, int u, int v, int edgeIdx, float c0, float c1, float c2, float c3, float c4, float c5, float c6, float c7, float c8)
        {
            float3 p0, p1;
            float v0, v1;

            switch (edgeIdx)
            {
                case 0: p0 = new float3(u, v, 0); p1 = new float3(u + 1, v, 0); v0 = c0; v1 = c1; break;
                case 1: p0 = new float3(u + 1, v, 0); p1 = new float3(u + 2, v, 0); v0 = c1; v1 = c2; break;
                case 2: p0 = new float3(u, v + 1, 0); p1 = new float3(u + 1, v + 1, 0); v0 = c3; v1 = c4; break;
                case 3: p0 = new float3(u + 1, v + 1, 0); p1 = new float3(u + 2, v + 1, 0); v0 = c4; v1 = c5; break;
                case 4: p0 = new float3(u, v + 2, 0); p1 = new float3(u + 1, v + 2, 0); v0 = c6; v1 = c7; break;
                case 5: p0 = new float3(u + 1, v + 2, 0); p1 = new float3(u + 2, v + 2, 0); v0 = c7; v1 = c8; break;
                case 6: p0 = new float3(u, v, 0); p1 = new float3(u, v + 1, 0); v0 = c0; v1 = c3; break;
                case 7: p0 = new float3(u, v + 1, 0); p1 = new float3(u, v + 2, 0); v0 = c3; v1 = c6; break;
                case 8: p0 = new float3(u + 1, v, 0); p1 = new float3(u + 1, v + 1, 0); v0 = c1; v1 = c4; break;
                case 9: p0 = new float3(u + 1, v + 1, 0); p1 = new float3(u + 1, v + 2, 0); v0 = c4; v1 = c7; break;
                // Fixed case 10 typo:
                case 10: p0 = new float3(u + 2, v, 0); p1 = new float3(u + 2, v + 1, 0); v0 = c2; v1 = c5; break;
                case 11: p0 = new float3(u + 2, v + 1, 0); p1 = new float3(u + 2, v + 2, 0); v0 = c5; v1 = c8; break;
                default: p0 = 0; p1 = 0; v0 = 0; v1 = 0; break;
            }

            // Calculate local interpolation on the 2D face grid
            float delta = v1 - v0;
            float t = (math.abs(delta) < 0.00001f) ? 0.5f : (IsoLevel - v0) / delta;
            t = math.clamp(t, 0f, 1f);

            // Small deterministic transition offset to favour the coarse-grid endpoint.
            // If one endpoint lies on the coarse grid (even indices in the 2D face), nudge t toward it.
            bool p0IsCoarse = ((int)p0.x % 2 == 0) && ((int)p0.y % 2 == 0);
            bool p1IsCoarse = ((int)p1.x % 2 == 0) && ((int)p1.y % 2 == 0);
            if (p0IsCoarse && !p1IsCoarse)
            {
                // move 20% toward p0
                t = t * 0.8f;
            }
            else if (p1IsCoarse && !p0IsCoarse)
            {
                // move 20% toward p1
                t = 1f - (1f - t) * 0.8f;
            }

            // Convert everything to float3 EXPLICITLY before doing the math
            float3 fOrigin = new float3(origin.x, origin.y, origin.z);
            float3 fUDir = new float3(uDir.x, uDir.y, uDir.z);
            float3 fVDir = new float3(vDir.x, vDir.y, vDir.z);

            // Convert the selected edge interpolation to a local point on the 2D face grid
            float3 localPoint = math.lerp(p0, p1, t);

            // This ensures no 'NaN' or 'Infinity' values are created
            return fOrigin + (fUDir * localPoint.x) + (fVDir * localPoint.y);
        }
        private float GetTransitionDensity(int3 origin, int3 uDir, int3 vDir, int u, int v, int cornerIndex)
        {
            // Maps the 9 transition corners (0-8) to 3D grid samples
            int3 offset = 0;
            switch (cornerIndex)
            {
                case 0: offset = uDir * u + vDir * v; break;
                case 1: offset = uDir * (u + 1) + vDir * v; break;
                case 2: offset = uDir * (u + 2) + vDir * v; break;
                case 3: offset = uDir * u + vDir * (v + 1); break;
                case 4: offset = uDir * (u + 1) + vDir * (v + 1); break;
                case 5: offset = uDir * (u + 2) + vDir * (v + 1); break;
                case 6: offset = uDir * u + vDir * (v + 2); break;
                case 7: offset = uDir * (u + 1) + vDir * (v + 2); break;
                case 8: offset = uDir * (u + 2) + vDir * (v + 2); break;
            }
            int3 p = origin + offset;
            return densitySamples[p.x + p.y * totalSamples.x + p.z * totalSamples.x * totalSamples.y];
        }

        private int GetOrCreateVertex(int x, int y, int z, int edgeIdx, byte m1, byte m2, float blend, float c0, float c1, float c2, float c3, float c4, float c5, float c6, float c7)
        {
            // Find exactly where the surface crosses the edge first
            float3 p0, p1;
            float v0, v1;
            GetEdgeEndpoints(edgeIdx, x, y, z, c0, c1, c2, c3, c4, c5, c6, c7, out p0, out p1, out v0, out v1);

            // 1. Generate the Universal Key using the endpoints!
            long key = GetUniversalEdgeKey(new int3((int)p0.x, (int)p0.y, (int)p0.z), new int3((int)p1.x, (int)p1.y, (int)p1.z));

            if (vertMap.TryGetValue(key, out int existingIndex))
            {
                return existingIndex;
            }

            float t = (IsoLevel - v0) / (v1 - v0);
            float3 pos = math.lerp(p0, p1, t) * voxelSize;

            // ARCHITECTURE: HALO COORDINATE OFFSET
            // Because our meshing loop started at (1, 1, 1) to account for the halo padding,
            // we must subtract 1 voxel from the local position to align the mesh with the world.
            pos -= new float3(1, 1, 1) * voxelSize;

            // 3. Register the new vertex
            int newIdx = vertices.Length;
            vertices.Add(pos);

            // Normals are initialized to zero and accumulated/averaged in the main loop
            normals.Add(new float3(0, 0, 0));
            vertexContribCounts.Add(0);

            // ARCHITECTURE: MATERIAL ID PACKING
            // We pack the Primary ID, Secondary ID, and Blend Weight into the UV channel.
            // This is the "Single Source of Truth" that the Triplanar shader uses to paint textures.
            packedMaterialUV.Add(new float4((float)m1, (float)m2, blend, 0f));

            vertMap.TryAdd(key, newIdx);
            return newIdx;
        }

        private void GetEdgeEndpoints(int edgeIdx, int x, int y, int z, float c0, float c1, float c2, float c3, float c4, float c5, float c6, float c7, out float3 p0, out float3 p1, out float v0, out float v1)
        {
            // Maps the Transvoxel edge index to local cell coordinates and corner densities
            float3 origin = new float3(x, y, z);
            switch (edgeIdx)
            {
                case 0: p0 = origin + new float3(0, 0, 0); p1 = origin + new float3(1, 0, 0); v0 = c0; v1 = c1; break;
                case 1: p0 = origin + new float3(1, 0, 0); p1 = origin + new float3(1, 0, 1); v0 = c1; v1 = c2; break;
                case 2: p0 = origin + new float3(1, 0, 1); p1 = origin + new float3(0, 0, 1); v0 = c2; v1 = c3; break;
                case 3: p0 = origin + new float3(0, 0, 1); p1 = origin + new float3(0, 0, 0); v0 = c3; v1 = c0; break;
                case 4: p0 = origin + new float3(0, 1, 0); p1 = origin + new float3(1, 1, 0); v0 = c4; v1 = c5; break;
                case 5: p0 = origin + new float3(1, 1, 0); p1 = origin + new float3(1, 1, 1); v0 = c5; v1 = c6; break;
                case 6: p0 = origin + new float3(1, 1, 1); p1 = origin + new float3(0, 1, 1); v0 = c6; v1 = c7; break;
                case 7: p0 = origin + new float3(0, 1, 1); p1 = origin + new float3(0, 1, 0); v0 = c7; v1 = c4; break;
                case 8: p0 = origin + new float3(0, 0, 0); p1 = origin + new float3(0, 1, 0); v0 = c0; v1 = c4; break;
                case 9: p0 = origin + new float3(1, 0, 0); p1 = origin + new float3(1, 1, 0); v0 = c1; v1 = c5; break;
                case 10: p0 = origin + new float3(1, 0, 1); p1 = origin + new float3(1, 1, 1); v0 = c2; v1 = c6; break;
                case 11: p0 = origin + new float3(0, 0, 1); p1 = origin + new float3(0, 1, 1); v0 = c3; v1 = c7; break;
                default: p0 = origin; p1 = origin; v0 = 0; v1 = 0; break;
            }
        }
    }

    // 3. THE MAIN ENTRY POINT
    public static MeshBuildData BuildChunkMesh(
            float[] worldDensity,
            Vector3Int chunkCoord,
            int N,
            float voxelSize,
            int totalX,
            int totalY,
            int totalZ,
            byte[] worldMaterials,
            int normalStride,
            int transitionMask = 0)
    {
        EnsureLutsAllocated();

        if ((N & 1) != 0)
        {
            Debug.LogWarning($"Chunk cell count N={N} is odd — transition loops expect even N. This may cause missing transition columns. Consider using even chunk sizes.");
        }

        int sxLocal = N + 3;
        int syLocal = N + 3;
        int szLocal = N + 3;

        // Use Allocator.TempJob to ensure compatibility with Parallel.For
        NativeArray<float> nDens = new NativeArray<float>(sxLocal * syLocal * szLocal, Allocator.TempJob);
        NativeArray<byte> nMats = new NativeArray<byte>((sxLocal - 1) * (syLocal - 1) * (szLocal - 1), Allocator.TempJob);

        for (int lz = 0; lz < szLocal; lz++)
        {
            for (int ly = 0; ly < syLocal; ly++)
            {
                for (int lx = 0; lx < sxLocal; lx++)
                {
                    // SAFETY CLAMPS: Prevent reading outside the global world arrays.
                    int gx = Mathf.Clamp(chunkCoord.x * N + lx - 1, 0, totalX - 1);
                    int gy = Mathf.Clamp(chunkCoord.y * N + ly - 1, 0, totalY - 1);
                    int gz = Mathf.Clamp(chunkCoord.z * N + lz - 1, 0, totalZ - 1);

                    int localIdx = lx + ly * sxLocal + lz * sxLocal * syLocal;

                    // Use a safe index calculation for the global arrays
                    int globalIdx = gx + gy * totalX + gz * totalX * totalY;

                    nDens[localIdx] = worldDensity[globalIdx];

                    if (lx < sxLocal - 1 && ly < syLocal - 1 && lz < szLocal - 1)
                    {
                        int matIdx = lx + ly * (sxLocal - 1) + lz * (sxLocal - 1) * (syLocal - 1);

                        // Ensure material mapping stays within worldMaterials bounds
                        int globalCellIdx = gx + gy * (totalX - 1) + gz * (totalX - 1) * (totalY - 1);
                        globalCellIdx = Mathf.Clamp(globalCellIdx, 0, worldMaterials.Length - 1);

                        nMats[matIdx] = worldMaterials[globalCellIdx];
                    }
                }
            }
        }

        // 2. JOB SETUP
        NativeList<float3> nVerts = new NativeList<float3>(Allocator.TempJob);
        NativeList<float3> nNorms = new NativeList<float3>(Allocator.TempJob);
        NativeList<float4> nUVs = new NativeList<float4>(Allocator.TempJob);
        NativeList<int> nTris = new NativeList<int>(Allocator.TempJob);
        NativeList<int> nCounts = new NativeList<int>(Allocator.TempJob);
        NativeParallelHashMap<long, int> vertMap = new NativeParallelHashMap<long, int>(1024, Allocator.TempJob);

        int faceArrayLen = sxLocal * syLocal * szLocal * 6;
        NativeArray<int> transitionVertexIndices = new NativeArray<int>(faceArrayLen, Allocator.TempJob);
        for (int i = 0; i < transitionVertexIndices.Length; i++) transitionVertexIndices[i] = -1;

        TransvoxelMesherJob job = new TransvoxelMesherJob
        {
            densitySamples = nDens,
            cellMaterialIndices = nMats,
            edgeTable = s_EdgeTableNative,
            triTable = s_TriTableNative,
            transitionCellClass = s_TransitionCellClassNative,
            transitionCornerData = s_TransitionCornerDataNative,
            transitionTriTable = s_TransitionTriTableNative,
            vertices = nVerts,
            normals = nNorms,
            packedMaterialUV = nUVs,
            triangles = nTris,
            vertexContribCounts = nCounts,
            vertMap = vertMap,
            transitionVertexIndices = transitionVertexIndices,
            totalSamples = new int3(sxLocal, syLocal, szLocal),
            voxelSize = voxelSize,
            transitionMask = transitionMask
        };
        MeshBuildData result;
        // 3. EXECUTION & COMMIT
        try
        {
            job.Schedule().Complete();
            result = new MeshBuildData(nVerts.Length);
            for (int i = 0; i < nVerts.Length; i++)
            {
                result.vertices.Add(nVerts[i]);

                // Average the accumulated normals for smooth lighting
                float3 avgNorm = nNorms[i];
                if (nCounts[i] > 0) avgNorm /= (float)nCounts[i];
                float3 norm = math.normalize(avgNorm);
                result.normals.Add(norm);

                // Compute slopeWeight from the vertex normal so steep faces lerp toward rock
                float upDot = norm.y; // 1=flat upward, 0=vertical
                float slopeWeight = math.clamp(math.pow(1.0f - math.abs(upDot), 2.0f), 0.0f, 1.0f);

                // Update the packed material UV's w component with the computed slopeWeight
                float4 uv4 = nUVs[i];
                uv4.w = slopeWeight;
                nUVs[i] = uv4;

                // Pack into the Mesh's UV channel (Vector4 expected by shader)
                result.uvs.Add(new Vector4(uv4.x, uv4.y, uv4.z, uv4.w));
            }

            for (int i = 0; i < nTris.Length; i++)
            {
                result.triangles.Add(nTris[i]);
            }
        }
        finally
        {
            // 4. CLEANUP
            nDens.Dispose();
            nMats.Dispose();
            nVerts.Dispose();
            nNorms.Dispose();
            nUVs.Dispose();
            nTris.Dispose();
            nCounts.Dispose();
            if (job.transitionVertexIndices.IsCreated) job.transitionVertexIndices.Dispose();
            vertMap.Dispose();
        }
        return result;
    }
}




