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
