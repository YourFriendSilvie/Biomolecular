using System;
#if UNITY_EDITOR
using UnityEditor;

public partial class ProceduralVoxelTerrain
{
    private sealed class EditorTerrainGenerationDriver : IDisposable
    {
        private readonly ProceduralVoxelTerrain owner;
        private readonly TerrainGenerationOperation operation;
        private bool disposed;

        public EditorTerrainGenerationDriver(ProceduralVoxelTerrain owner, TerrainGenerationOperation operation)
        {
            this.owner = owner;
            this.operation = operation;
        }

        public void Start()
        {
            EditorApplication.update += Update;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            EditorApplication.update -= Update;
            EditorUtility.ClearProgressBar();
        }

        private void Update()
        {
            if (owner == null)
            {
                Dispose();
                return;
            }

            owner.AdvanceTerrainGenerationWithinBudget(operation);

            if (owner.activeTerrainGenerationOperation != operation)
            {
                Dispose();
                return;
            }

            EditorUtility.DisplayProgressBar(
                $"Generating {owner.name}",
                owner.TerrainGenerationStatus,
                owner.TerrainGenerationProgress01);
            SceneView.RepaintAll();
        }
    }
}
#endif
