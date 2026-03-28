using System;
using UnityEngine;

/// <summary>
/// Creates and maintains the Unity GameObjects that represent water bodies.
/// Owns <see cref="GeneratedOceanObject"/> and <see cref="GeneratedOceanHarvestable"/>.
/// </summary>
internal sealed class WaterRenderFactory
{
    private readonly int layer;
    private readonly float waterSurfaceThicknessMeters;
    private readonly float seaLevelMeters;
    private readonly float oceanPaddingMeters;
    private readonly float oceanDepthEquivalentMeters;
    private readonly bool useTriggerColliders;
    private readonly float voxelSizeMeters;
    private readonly Func<Material> getFreshWaterMaterial;
    private readonly Func<Material> getSaltWaterMaterial;
    private readonly Func<CompositionInfo> getFreshWaterComposition;
    private readonly Func<CompositionInfo> getSeaWaterComposition;
    private readonly Action<string> logDebug;

    public GameObject GeneratedOceanObject { get; private set; }
    public FinitePlaneWaterHarvestable GeneratedOceanHarvestable { get; private set; }

    public WaterRenderFactory(
        int layer,
        float waterSurfaceThicknessMeters,
        float seaLevelMeters,
        float oceanPaddingMeters,
        float oceanDepthEquivalentMeters,
        bool useTriggerColliders,
        float voxelSizeMeters,
        Func<Material> getFreshWaterMaterial,
        Func<Material> getSaltWaterMaterial,
        Func<CompositionInfo> getFreshWaterComposition,
        Func<CompositionInfo> getSeaWaterComposition,
        Action<string> logDebug)
    {
        this.layer = layer;
        this.waterSurfaceThicknessMeters = waterSurfaceThicknessMeters;
        this.seaLevelMeters = seaLevelMeters;
        this.oceanPaddingMeters = oceanPaddingMeters;
        this.oceanDepthEquivalentMeters = oceanDepthEquivalentMeters;
        this.useTriggerColliders = useTriggerColliders;
        this.voxelSizeMeters = voxelSizeMeters;
        this.getFreshWaterMaterial = getFreshWaterMaterial;
        this.getSaltWaterMaterial = getSaltWaterMaterial;
        this.getFreshWaterComposition = getFreshWaterComposition;
        this.getSeaWaterComposition = getSeaWaterComposition;
        this.logDebug = logDebug;
    }

    public void ClearOceanState()
    {
        GeneratedOceanObject = null;
        GeneratedOceanHarvestable = null;
    }

    public float GetCurrentOceanSurfaceY()
    {
        return GeneratedOceanHarvestable != null ? GeneratedOceanHarvestable.CurrentSurfaceY : seaLevelMeters;
    }

    public bool TryGetOceanBounds(out Bounds oceanBounds)
    {
        oceanBounds = default;
        if (GeneratedOceanHarvestable == null || !GeneratedOceanHarvestable.HasWater || GeneratedOceanObject == null)
        {
            return false;
        }

        Collider oceanCollider = GeneratedOceanObject.GetComponent<Collider>();
        if (oceanCollider == null || !oceanCollider.enabled)
        {
            return false;
        }

        oceanBounds = oceanCollider.bounds;
        return true;
    }

    public void CreateOceanObject(Bounds terrainWorldBounds, Transform generatedRoot)
    {
        CompositionInfo seaWaterComposition = getSeaWaterComposition();
        GameObject ocean = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ocean.name = "Voxel Ocean";
        ocean.layer = layer;
        ocean.transform.SetParent(generatedRoot, true);
        ocean.transform.position = new Vector3(
            terrainWorldBounds.center.x,
            seaLevelMeters - (waterSurfaceThicknessMeters * 0.5f),
            terrainWorldBounds.center.z);
        ocean.transform.rotation = Quaternion.identity;
        ocean.transform.localScale = new Vector3(
            terrainWorldBounds.size.x + (oceanPaddingMeters * 2f),
            waterSurfaceThicknessMeters,
            terrainWorldBounds.size.z + (oceanPaddingMeters * 2f));

        ConfigureWaterObject(ocean, getSaltWaterMaterial(), seaWaterComposition);
        ConfigureInteractionCollider(ocean.GetComponent<Collider>());
        float surfaceAreaSquareMeters = ocean.transform.localScale.x * ocean.transform.localScale.z;
        GeneratedOceanObject = ocean;
        GeneratedOceanHarvestable = EnsureFinitePlaneWaterHarvestable(
            ocean,
            seaWaterComposition,
            5000000f,
            surfaceAreaSquareMeters * oceanDepthEquivalentMeters,
            seaLevelMeters,
            surfaceAreaSquareMeters,
            waterSurfaceThicknessMeters);
    }

    public void CreateOrUpdateLakeObject(GeneratedLake lake, Transform generatedRoot, float minRenderableVolume)
    {
        if (lake == null)
        {
            return;
        }

        CompositionInfo freshWaterComposition = getFreshWaterComposition();
        lake.waterObject = EnsureFreshWaterObject(
            lake.waterObject,
            lake.isPond ? "Voxel Freshwater Pond" : "Voxel Freshwater Lake",
            generatedRoot,
            freshWaterComposition);
        RemoveStaticWaterHarvestable(lake.waterObject);
        DisableVisualWaterCollider(lake.waterObject);
        Mesh lakeMesh = WaterMeshGenerator.BuildLakeMesh(lake, generatedRoot, voxelSizeMeters);
        if (!TryAssignWaterMesh(lake.waterObject, lakeMesh, lake.storedVolumeCubicMeters > minRenderableVolume))
        {
            logDebug?.Invoke($"CreateOrUpdateLakeObject disabled {WaterDebugUtility.DescribeLake(lake)} because no renderable mesh was built.");
            return;
        }

        ConfigureLakeInteractionCollider(lake);
        logDebug?.Invoke($"CreateOrUpdateLakeObject updated {WaterDebugUtility.DescribeLake(lake)}. Mesh vertices={lakeMesh.vertexCount}, triangles={lake.surfaceTriangles.Length / 3}, active={lake.waterObject.activeSelf}.");
    }

    public void CreateOrUpdateRiverObject(GeneratedRiver river, Transform generatedRoot, int index)
    {
        if (river == null || river.points.Count < 2 || river.points.Count != river.widths.Count)
        {
            return;
        }

        CompositionInfo freshWaterComposition = getFreshWaterComposition();
        river.waterObject = EnsureFreshWaterObject(
            river.waterObject,
            $"Voxel River {index + 1:000}",
            generatedRoot,
            freshWaterComposition);
        RemoveStaticWaterHarvestable(river.waterObject);
        DisableVisualWaterCollider(river.waterObject);

        Mesh riverMesh = WaterMeshGenerator.BuildRiverMesh(river, generatedRoot, waterSurfaceThicknessMeters);
        if (!TryAssignWaterMesh(river.waterObject, riverMesh, true))
        {
            return;
        }

        ConfigureRiverInteractionCollider(river);
    }

    private void ConfigureLakeInteractionCollider(GeneratedLake lake)
    {
        if (lake == null || lake.waterObject == null)
        {
            return;
        }

        float colliderHeight = Mathf.Max(0.35f, waterSurfaceThicknessMeters + 0.45f);
        Bounds bounds = lake.surfaceBounds;
        if (bounds.size.x <= 0.001f || bounds.size.z <= 0.001f)
        {
            float maxRadius = GetMaxLakeRadius(lake);
            bounds = new Bounds(
                new Vector3(lake.center.x, lake.surfaceY, lake.center.z),
                new Vector3(maxRadius * 2f, waterSurfaceThicknessMeters, maxRadius * 2f));
        }

        float horizontalPadding = Mathf.Max(voxelSizeMeters, 0.5f);
        bounds.Expand(new Vector3(horizontalPadding, 0f, horizontalPadding));
        bounds.center = new Vector3(bounds.center.x, lake.surfaceY - (colliderHeight * 0.5f), bounds.center.z);
        bounds.size = new Vector3(
            Mathf.Max(0.5f, bounds.size.x),
            colliderHeight,
            Mathf.Max(0.5f, bounds.size.z));

        Transform interactionRoot = EnsureInteractionRoot(lake.waterObject);
        interactionRoot.localPosition = lake.waterObject.transform.InverseTransformPoint(bounds.center);

        BoxCollider collider = interactionRoot.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = interactionRoot.gameObject.AddComponent<BoxCollider>();
        }

        collider.center = Vector3.zero;
        collider.size = bounds.size;
        ConfigureInteractionCollider(collider);
    }

    private void ConfigureRiverInteractionCollider(GeneratedRiver river)
    {
        if (river == null || river.waterObject == null || river.waterPath.Count == 0)
        {
            return;
        }

        float colliderHeight = Mathf.Max(0.35f, waterSurfaceThicknessMeters + 0.45f);
        float averageY = 0f;
        for (int i = 0; i < river.waterPath.Count; i++)
        {
            averageY += river.waterPath[i].y;
        }

        averageY /= river.waterPath.Count;
        Bounds bounds = river.influenceBounds;
        bounds.center = new Vector3(bounds.center.x, averageY - (colliderHeight * 0.5f), bounds.center.z);
        bounds.size = new Vector3(
            Mathf.Max(0.5f, bounds.size.x),
            colliderHeight,
            Mathf.Max(0.5f, bounds.size.z));

        Transform interactionRoot = EnsureInteractionRoot(river.waterObject);
        interactionRoot.localPosition = river.waterObject.transform.InverseTransformPoint(bounds.center);

        BoxCollider collider = interactionRoot.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = interactionRoot.gameObject.AddComponent<BoxCollider>();
        }

        collider.center = Vector3.zero;
        collider.size = bounds.size;
        ConfigureInteractionCollider(collider);
    }

    private Transform EnsureInteractionRoot(GameObject waterObject)
    {
        const string InteractionRootObjectName = "Interaction";
        Transform interactionRoot = waterObject.transform.Find(InteractionRootObjectName);
        if (interactionRoot != null)
        {
            interactionRoot.localRotation = Quaternion.identity;
            interactionRoot.localScale = Vector3.one;
            interactionRoot.gameObject.layer = layer;
            return interactionRoot;
        }

        GameObject interactionObject = new GameObject(InteractionRootObjectName);
        interactionObject.layer = layer;
        interactionObject.transform.SetParent(waterObject.transform, false);
        interactionObject.transform.localPosition = Vector3.zero;
        interactionObject.transform.localRotation = Quaternion.identity;
        interactionObject.transform.localScale = Vector3.one;
        return interactionObject.transform;
    }

    private void ConfigureInteractionCollider(Collider collider)
    {
        if (collider == null)
        {
            return;
        }

        collider.enabled = true;
        collider.isTrigger = useTriggerColliders;
    }

    private void DisableVisualWaterCollider(GameObject waterObject)
    {
        if (waterObject == null)
        {
            return;
        }

        Collider collider = waterObject.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyComponent(collider);
        }
    }

    private FinitePlaneWaterHarvestable EnsureFinitePlaneWaterHarvestable(
        GameObject waterObject,
        CompositionInfo composition,
        float harvestMassGrams,
        float totalVolumeCubicMeters,
        float surfaceY,
        float surfaceAreaSquareMeters,
        float visibleThicknessMeters)
    {
        if (waterObject == null || composition == null)
        {
            return null;
        }

        HarvestableObject staticHarvestable = waterObject.GetComponent<HarvestableObject>();
        if (staticHarvestable != null)
        {
            DestroyComponent(staticHarvestable);
        }

        FinitePlaneWaterHarvestable harvestable = waterObject.GetComponent<FinitePlaneWaterHarvestable>();
        if (harvestable == null)
        {
            harvestable = waterObject.AddComponent<FinitePlaneWaterHarvestable>();
        }

        harvestable.Configure(
            composition,
            harvestMassGrams,
            totalVolumeCubicMeters,
            surfaceY,
            surfaceAreaSquareMeters,
            visibleThicknessMeters);
        return harvestable;
    }

    private static void RemoveStaticWaterHarvestable(GameObject waterObject)
    {
        if (waterObject == null)
        {
            return;
        }

        HarvestableObject harvestable = waterObject.GetComponent<HarvestableObject>();
        if (harvestable != null)
        {
            DestroyComponent(harvestable);
        }
    }

    private void ConfigureWaterObject(GameObject waterObject, Material material, CompositionInfo composition)
    {
        if (waterObject == null)
        {
            return;
        }

        MeshRenderer renderer = waterObject.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private GameObject EnsureFreshWaterObject(
        GameObject waterObject,
        string objectName,
        Transform generatedRoot,
        CompositionInfo freshWaterComposition)
    {
        if (waterObject != null)
        {
            return waterObject;
        }

        waterObject = new GameObject(objectName);
        waterObject.layer = layer;
        waterObject.transform.SetParent(generatedRoot, false);
        waterObject.transform.localPosition = Vector3.zero;
        waterObject.transform.localRotation = Quaternion.identity;
        waterObject.transform.localScale = Vector3.one;
        waterObject.AddComponent<MeshFilter>();
        waterObject.AddComponent<MeshRenderer>();
        ConfigureWaterObject(waterObject, getFreshWaterMaterial(), freshWaterComposition);
        return waterObject;
    }

    private static bool TryAssignWaterMesh(GameObject waterObject, Mesh mesh, bool activeState)
    {
        if (waterObject == null)
        {
            return false;
        }

        if (mesh == null)
        {
            waterObject.SetActive(false);
            return false;
        }

        ReplaceSharedMesh(waterObject.GetComponent<MeshFilter>(), mesh);
        waterObject.SetActive(activeState);
        return true;
    }

    private static void ReplaceSharedMesh(MeshFilter meshFilter, Mesh mesh)
    {
        if (meshFilter == null)
        {
            return;
        }

        Mesh previousMesh = meshFilter.sharedMesh;
        meshFilter.sharedMesh = mesh;
        if (previousMesh == null || previousMesh == mesh)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(previousMesh);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(previousMesh);
        }
    }

    private static float GetMaxLakeRadius(GeneratedLake lake)
    {
        if (lake == null)
        {
            return 0f;
        }

        float maxRadius = Mathf.Max(0f, lake.radius);
        if (WaterSpatialQueryUtility.HasLakeSurfaceGeometry(lake))
        {
            maxRadius = Mathf.Max(maxRadius, Mathf.Max(lake.surfaceBounds.extents.x, lake.surfaceBounds.extents.z));
        }

        return Mathf.Max(maxRadius, lake.captureRadius * 0.5f);
    }

    private static void DestroyComponent(Component component)
    {
        if (component == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(component);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(component);
        }
    }
}
