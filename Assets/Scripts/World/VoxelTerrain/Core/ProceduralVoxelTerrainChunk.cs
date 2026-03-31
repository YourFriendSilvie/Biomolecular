using System;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class ProceduralVoxelTerrainChunk : MonoBehaviour
{
    [SerializeField, HideInInspector] private Vector3Int chunkCoordinate;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh generatedMesh;

    public Vector3Int ChunkCoordinate => chunkCoordinate;
    [Header("LOD Data")]
    public int transitionMask;
    [NonSerialized] public int currentLod;
    public void Initialize(Vector3Int coordinate, Material[] sharedMaterials)
    {
        chunkCoordinate = coordinate;
        meshFilter = GetOrAddComponent<MeshFilter>();
        meshRenderer = GetOrAddComponent<MeshRenderer>();
        meshCollider = GetOrAddComponent<MeshCollider>();
        meshRenderer.sharedMaterials = sharedMaterials ?? Array.Empty<Material>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.On;
        meshRenderer.receiveShadows = true;
    }

    public void ApplyMesh(Mesh mesh, Material[] sharedMaterials)
    {
        meshFilter = GetOrAddComponent<MeshFilter>();
        meshRenderer = GetOrAddComponent<MeshRenderer>();
        meshCollider = GetOrAddComponent<MeshCollider>();

        DestroyManagedMesh();
        generatedMesh = mesh;

        meshFilter.sharedMesh = generatedMesh;
        meshCollider.sharedMesh = generatedMesh;
        meshRenderer.sharedMaterials = sharedMaterials ?? Array.Empty<Material>();
    }

    /// <summary>
    /// Applies the mesh to the visual components (MeshFilter, MeshRenderer) only.
    /// The MeshCollider is NOT updated — call AssignColliderMesh once Physics.BakeMesh completes.
    /// Returns the MeshCollider component so the caller can assign it after the bake.
    /// </summary>
    public MeshCollider ApplyMeshVisualOnly(Mesh mesh, Material[] sharedMaterials)
    {
        meshFilter = GetOrAddComponent<MeshFilter>();
        meshRenderer = GetOrAddComponent<MeshRenderer>();
        meshCollider = GetOrAddComponent<MeshCollider>();

        DestroyManagedMesh();
        generatedMesh = mesh;

        meshFilter.sharedMesh = generatedMesh;
        meshRenderer.sharedMaterials = sharedMaterials ?? Array.Empty<Material>();
        return meshCollider;
    }

    /// <summary>
    /// Assigns a pre-baked mesh to the MeshCollider. Call this on the main thread after
    /// Physics.BakeMesh has completed on a background thread.
    /// </summary>
    public void AssignColliderMesh(Mesh mesh)
    {
        meshCollider = GetOrAddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
    }

    public void ClearMesh()
    {
        meshFilter = GetOrAddComponent<MeshFilter>();
        meshCollider = GetOrAddComponent<MeshCollider>();
        meshFilter.sharedMesh = null;
        meshCollider.sharedMesh = null;
        DestroyManagedMesh();
    }

    private T GetOrAddComponent<T>() where T : Component
    {
        T component = GetComponent<T>();
        if (component == null)
        {
            component = gameObject.AddComponent<T>();
        }

        return component;
    }

    private void DestroyManagedMesh()
    {
        if (generatedMesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(generatedMesh);
        }
        else
        {
            DestroyImmediate(generatedMesh);
        }

        generatedMesh = null;
    }
}
