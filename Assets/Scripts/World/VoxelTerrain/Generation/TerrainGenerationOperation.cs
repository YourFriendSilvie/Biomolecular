using System;
using System.Collections.Generic;
using UnityEngine;

public partial class ProceduralVoxelTerrain
{
    private enum TerrainGenerationPhase
    {
        None,
        SurfacePrepass,
        ColumnPrepass,
        DensityField,
        CellMaterials,
        ChunkObjects,
        ChunkMeshBuildData,
        ChunkMeshCommit,
        Complete
    }

    private sealed class ChunkMeshBuildResult
    {
        public readonly Vector3Int chunkCoordinate;
        public readonly ChunkMeshBuilder.MeshBuildData buildData;

        public ChunkMeshBuildResult(Vector3Int chunkCoordinate, ChunkMeshBuilder.MeshBuildData buildData)
        {
            this.chunkCoordinate = chunkCoordinate;
            this.buildData = buildData;
        }
    }

    private sealed class TerrainGenerationOperation
    {
        private readonly ProceduralVoxelTerrain owner;
        private readonly bool clearExisting;
        private readonly Queue<ChunkMeshBuildResult> pendingChunkCommits = new Queue<ChunkMeshBuildResult>();
        private readonly List<Vector3Int> chunkCoordinates = new List<Vector3Int>();
        private readonly bool collectTimings;
        private readonly System.Diagnostics.Stopwatch totalStopwatch;
        private readonly System.Diagnostics.Stopwatch phaseStopwatch;
        private readonly float totalWorkUnits;

        private GenerationContext context;
        private int surfacePrepassZ;
        private int columnProfileZ;
        private int densityZ;
        private int materialZ;
        private int builtChunkCount;
        private int committedChunkCount;
        private bool chunkObjectsCreated;
        private bool initialized;
        private int runtimeStreamingStartupChunkTarget;
        private bool runtimeStreamingStartupChunksNotified;

        public TerrainGenerationOperation(ProceduralVoxelTerrain owner, bool clearExisting)
        {
            this.owner = owner;
            this.clearExisting = clearExisting;
            collectTimings = owner.logGenerationTimings;
            totalStopwatch = collectTimings ? System.Diagnostics.Stopwatch.StartNew() : null;
            phaseStopwatch = collectTimings ? System.Diagnostics.Stopwatch.StartNew() : null;
            totalWorkUnits = Mathf.Max(
                1f,
                owner.TotalSamplesZ
                + owner.TotalCellsZ
                + owner.TotalSamplesZ
                + owner.TotalCellsZ
                + 1f
                + (owner.TotalChunkCount * 2f));

            Phase = TerrainGenerationPhase.None;
            Status = "Preparing voxel terrain generation";
        }

        public TerrainGenerationPhase Phase { get; private set; }
        public string Status { get; private set; }
        public float Progress01 { get; private set; }
        public bool IsDone { get; private set; }
        public bool Success { get; private set; }
        public long PrepassMilliseconds { get; private set; }
        public long DensityMilliseconds { get; private set; }
        public long MaterialMilliseconds { get; private set; }
        public long ChunkObjectMilliseconds { get; private set; }
        public long MeshMilliseconds { get; private set; }
        public long TotalMilliseconds => totalStopwatch?.ElapsedMilliseconds ?? 0L;

        public void Step()
        {
            if (IsDone)
            {
                return;
            }

            if (!initialized)
            {
                Initialize();
            }

            switch (Phase)
            {
                case TerrainGenerationPhase.SurfacePrepass:
                    StepSurfacePrepass();
                    break;
                case TerrainGenerationPhase.ColumnPrepass:
                    StepColumnProfiles();
                    break;
                case TerrainGenerationPhase.DensityField:
                    StepDensityField();
                    break;
                case TerrainGenerationPhase.CellMaterials:
                    StepCellMaterials();
                    break;
                case TerrainGenerationPhase.ChunkObjects:
                    StepChunkObjects();
                    break;
                case TerrainGenerationPhase.ChunkMeshBuildData:
                    StepChunkMeshBuildData();
                    break;
                case TerrainGenerationPhase.ChunkMeshCommit:
                    StepChunkMeshCommit();
                    break;
                case TerrainGenerationPhase.Complete:
                    Complete();
                    break;
            }

            UpdateProgress();
        }

        private void Initialize()
        {
            if (owner.randomizeSeed)
            {
                owner.seed = Environment.TickCount;
            }

            if (clearExisting)
            {
                owner.ClearGeneratedTerrainForRegeneration();
            }

            if (owner.materialDefinitions == null || owner.materialDefinitions.Count == 0)
            {
                owner.ApplyOlympicRainforestPreset();
            }
            else
            {
                owner.EnsureDefaultMaterialDefinitionsPresent();
            }

            owner.generationMaterialIndices = owner.BuildGenerationMaterialIndices();
            owner.sharedChunkMaterials = owner.BuildSharedMaterials();
            owner.densitySamples = new float[owner.TotalSamplesX * owner.TotalSamplesY * owner.TotalSamplesZ];
            owner.cellMaterialIndices = new byte[owner.TotalCellsX * owner.TotalCellsY * owner.TotalCellsZ];
            owner.surfaceHeightPrepassReady = false;
            owner.surfaceHeightPrepass = new float[owner.TotalSamplesX * owner.TotalSamplesZ];
            owner.columnProfilePrepass = new ColumnMaterialProfile[owner.TotalCellsX * owner.TotalCellsZ];
            context = VoxelTerrainGenerator.BuildGenerationContext(owner.seed);

            Phase = TerrainGenerationPhase.SurfacePrepass;
            initialized = true;
            UpdateProgress();
        }

        private void StepSurfacePrepass()
        {
            float localZ = surfacePrepassZ * owner.voxelSizeMeters;
            for (int x = 0; x < owner.TotalSamplesX; x++)
            {
                float localX = x * owner.voxelSizeMeters;
                owner.surfaceHeightPrepass[owner.GetSurfacePrepassIndex(x, surfacePrepassZ)] = owner.EvaluateSurfaceHeight(localX, localZ, context);
            }

            surfacePrepassZ++;
            if (surfacePrepassZ >= owner.TotalSamplesZ)
            {
                owner.surfaceHeightPrepassReady = true;
                Phase = TerrainGenerationPhase.ColumnPrepass;
            }
        }

        private void StepColumnProfiles()
        {
            float localZ = (columnProfileZ + 0.5f) * owner.voxelSizeMeters;
            for (int x = 0; x < owner.TotalCellsX; x++)
            {
                float localX = (x + 0.5f) * owner.voxelSizeMeters;
                float surfaceHeight = owner.SampleSurfaceHeightPrepass(localX, localZ);
                owner.columnProfilePrepass[owner.GetColumnPrepassIndex(x, columnProfileZ)] =
                    owner.BuildColumnMaterialProfile(localX, localZ, surfaceHeight, context);
            }

            columnProfileZ++;
            if (columnProfileZ >= owner.TotalCellsZ)
            {
                if (collectTimings && phaseStopwatch != null)
                {
                    PrepassMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                    phaseStopwatch.Restart();
                }

                Phase = TerrainGenerationPhase.DensityField;
            }
        }

        private void StepDensityField()
        {
            float localZ = densityZ * owner.voxelSizeMeters;
            for (int x = 0; x < owner.TotalSamplesX; x++)
            {
                float localX = x * owner.voxelSizeMeters;
                float surfaceHeight = owner.GetSurfaceHeightFromPrepass(x, densityZ);
                for (int y = 0; y < owner.TotalSamplesY; y++)
                {
                    float localY = y * owner.voxelSizeMeters;
                    owner.densitySamples[owner.GetSampleIndex(x, y, densityZ)] =
                        owner.EvaluateDensity(localX, localY, localZ, surfaceHeight, context);
                }
            }

            densityZ++;
            if (densityZ >= owner.TotalSamplesZ)
            {
                if (collectTimings && phaseStopwatch != null)
                {
                    DensityMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                    phaseStopwatch.Restart();
                }

                Phase = TerrainGenerationPhase.CellMaterials;
            }
        }

        private void StepCellMaterials()
        {
            float localZ = (materialZ + 0.5f) * owner.voxelSizeMeters;
            for (int x = 0; x < owner.TotalCellsX; x++)
            {
                float localX = (x + 0.5f) * owner.voxelSizeMeters;
                ColumnMaterialProfile columnProfile = owner.columnProfilePrepass[owner.GetColumnPrepassIndex(x, materialZ)];
                for (int y = 0; y < owner.TotalCellsY; y++)
                {
                    float localY = (y + 0.5f) * owner.voxelSizeMeters;
                    owner.cellMaterialIndices[owner.GetCellIndex(x, y, materialZ)] =
                        owner.DetermineCellMaterialIndex(localX, localY, localZ, context, columnProfile);
                }
            }

            materialZ++;
            if (materialZ >= owner.TotalCellsZ)
            {
                if (collectTimings && phaseStopwatch != null)
                {
                    MaterialMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                    phaseStopwatch.Restart();
                }

                Phase = TerrainGenerationPhase.ChunkObjects;
            }
        }

        private void StepChunkObjects()
        {
            owner.EnsureChunkObjects();
            chunkCoordinates.Clear();
            chunkCoordinates.AddRange(owner.BuildChunkCoordinatesInGenerationOrder());
            runtimeStreamingStartupChunkTarget = owner.CountRuntimeStreamingStartupChunks(chunkCoordinates);
            if (runtimeStreamingStartupChunkTarget == 0)
            {
                runtimeStreamingStartupChunksNotified = owner.MarkRuntimeStreamingStartupChunksReady();
            }
            chunkObjectsCreated = true;

            if (collectTimings && phaseStopwatch != null)
            {
                ChunkObjectMilliseconds = phaseStopwatch.ElapsedMilliseconds;
                phaseStopwatch.Restart();
            }

            Phase = chunkCoordinates.Count > 0
                ? TerrainGenerationPhase.ChunkMeshBuildData
                : TerrainGenerationPhase.Complete;

            if (chunkCoordinates.Count == 0)
            {
                Complete();
            }
        }

        private void StepChunkMeshBuildData()
        {
            if (builtChunkCount >= chunkCoordinates.Count)
            {
                Phase = pendingChunkCommits.Count > 0
                    ? TerrainGenerationPhase.ChunkMeshCommit
                    : TerrainGenerationPhase.Complete;

                if (Phase == TerrainGenerationPhase.Complete)
                {
                    Complete();
                }

                return;
            }

            Vector3Int chunkCoordinate = chunkCoordinates[builtChunkCount];
            pendingChunkCommits.Enqueue(new ChunkMeshBuildResult(chunkCoordinate, owner.BuildChunkMesh(chunkCoordinate)));
            builtChunkCount++;

            if (pendingChunkCommits.Count >= owner.asyncChunkBuildQueueSize || builtChunkCount >= chunkCoordinates.Count)
            {
                Phase = TerrainGenerationPhase.ChunkMeshCommit;
            }
        }

        private void StepChunkMeshCommit()
        {
            if (pendingChunkCommits.Count == 0)
            {
                if (builtChunkCount >= chunkCoordinates.Count)
                {
                    Complete();
                    return;
                }

                Phase = TerrainGenerationPhase.ChunkMeshBuildData;
                return;
            }

            ChunkMeshBuildResult chunkBuildResult = pendingChunkCommits.Dequeue();
            owner.CommitChunkMesh(chunkBuildResult.chunkCoordinate, chunkBuildResult.buildData);
            committedChunkCount++;
            if (!runtimeStreamingStartupChunksNotified &&
                runtimeStreamingStartupChunkTarget > 0 &&
                committedChunkCount >= runtimeStreamingStartupChunkTarget)
            {
                runtimeStreamingStartupChunksNotified = owner.MarkRuntimeStreamingStartupChunksReady();
            }

            if (committedChunkCount >= chunkCoordinates.Count &&
                builtChunkCount >= chunkCoordinates.Count &&
                pendingChunkCommits.Count == 0)
            {
                Complete();
                return;
            }

            if (builtChunkCount < chunkCoordinates.Count && pendingChunkCommits.Count < owner.asyncChunkBuildQueueSize)
            {
                Phase = TerrainGenerationPhase.ChunkMeshBuildData;
            }
        }

        private void Complete()
        {
            if (IsDone)
            {
                return;
            }

            if (collectTimings)
            {
                MeshMilliseconds = phaseStopwatch?.ElapsedMilliseconds ?? 0L;
                totalStopwatch?.Stop();
            }

            if (!runtimeStreamingStartupChunksNotified && runtimeStreamingStartupChunkTarget > 0)
            {
                runtimeStreamingStartupChunksNotified = owner.MarkRuntimeStreamingStartupChunksReady();
            }

            Phase = TerrainGenerationPhase.Complete;
            Progress01 = 1f;
            Status = "Voxel terrain generation complete";
            Success = true;
            IsDone = true;
        }

        private void UpdateProgress()
        {
            if (IsDone)
            {
                return;
            }

            float completedUnits = surfacePrepassZ
                + columnProfileZ
                + densityZ
                + materialZ
                + (chunkObjectsCreated ? 1f : 0f)
                + builtChunkCount
                + committedChunkCount;

            Progress01 = totalWorkUnits <= 0.0001f
                ? 1f
                : Mathf.Clamp01(completedUnits / totalWorkUnits);
            Status = BuildStatus();
        }

        private string BuildStatus()
        {
            switch (Phase)
            {
                case TerrainGenerationPhase.SurfacePrepass:
                    return $"Terrain data prep: surface prepass {Mathf.Min(surfacePrepassZ, owner.TotalSamplesZ)}/{owner.TotalSamplesZ}";
                case TerrainGenerationPhase.ColumnPrepass:
                    return $"Terrain data prep: column profiles {Mathf.Min(columnProfileZ, owner.TotalCellsZ)}/{owner.TotalCellsZ}";
                case TerrainGenerationPhase.DensityField:
                    return $"Terrain data prep: density field {Mathf.Min(densityZ, owner.TotalSamplesZ)}/{owner.TotalSamplesZ}";
                case TerrainGenerationPhase.CellMaterials:
                    return $"Terrain data prep: cell materials {Mathf.Min(materialZ, owner.TotalCellsZ)}/{owner.TotalCellsZ}";
                case TerrainGenerationPhase.ChunkObjects:
                    return "Terrain data prep: chunk objects";
                case TerrainGenerationPhase.ChunkMeshBuildData:
                    return $"Chunk mesh build data {Mathf.Min(builtChunkCount, chunkCoordinates.Count)}/{chunkCoordinates.Count}";
                case TerrainGenerationPhase.ChunkMeshCommit:
                    return $"Chunk mesh commit {Mathf.Min(committedChunkCount, chunkCoordinates.Count)}/{chunkCoordinates.Count}";
                case TerrainGenerationPhase.Complete:
                    return "Voxel terrain generation complete";
                default:
                    return "Preparing voxel terrain generation";
            }
        }
    }
}
