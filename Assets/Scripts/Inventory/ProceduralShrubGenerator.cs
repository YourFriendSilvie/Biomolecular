using System;
using System.Collections.Generic;
using UnityEngine;
using static ProceduralPlantUtility;

public class ProceduralShrubGenerator : MonoBehaviour
{
    [Header("Generation Controls")]
    [SerializeField] private bool generateOnAwake = true;
    [SerializeField] private int randomSeed = 271828;
    [SerializeField] private bool randomizeSeed = true;

    [Header("Shrub Dimensions (Meters)")]
    [SerializeField] private Vector2 heightRangeMeters = new Vector2(1.6f, 3.2f);
    [SerializeField] private Vector2 crownRadiusRangeMeters = new Vector2(0.8f, 1.7f);
    [SerializeField, Range(0.04f, 0.3f)] private float crownBaseHeightFraction = 0.08f;
    [SerializeField] private TreeCrownShape crownShape = TreeCrownShape.OpenRounded;

    [Header("Stem Structure")]
    [SerializeField] private Vector2Int stemCountRange = new Vector2Int(5, 9);
    [SerializeField] private Vector2 stemBaseRadiusRangeMeters = new Vector2(0.008f, 0.024f);
    [SerializeField] private Vector2 stemLeanDegreesRange = new Vector2(8f, 24f);
    [SerializeField] private Vector2 stemArcStrengthRange = new Vector2(0.12f, 0.26f);
    [SerializeField] private Vector2Int branchCountPerStemRange = new Vector2Int(4, 8);
    [SerializeField] private Vector2 branchLengthRangeMeters = new Vector2(0.25f, 0.9f);
    [SerializeField] private Vector2 branchRadiusScaleRange = new Vector2(0.24f, 0.46f);
    [SerializeField] private Vector2 branchPitchDegreesRange = new Vector2(12f, 48f);
    [SerializeField, Range(0.05f, 0.35f)] private float branchBendStrength = 0.14f;
    [SerializeField, Range(0.05f, 0.35f)] private float stemTipRadiusFraction = 0.24f;
    [SerializeField, Range(0.05f, 0.4f)] private float branchTipRadiusFraction = 0.28f;
    [SerializeField, Min(3)] private int radialSegments = 6;
    [SerializeField, Min(3)] private int stemSegments = 5;
    [SerializeField, Min(2)] private int branchSegments = 3;

    [Header("Foliage")]
    [SerializeField] private TreeFoliageRenderStyle foliageRenderStyle = TreeFoliageRenderStyle.RedAlder;
    [SerializeField] private Vector2Int foliageClusterCountRange = new Vector2Int(20, 34);
    [SerializeField] private Vector2 foliageClusterRadiusRangeMeters = new Vector2(0.18f, 0.42f);
    [SerializeField] private Vector2Int leavesPerClusterRange = new Vector2Int(12, 22);
    [SerializeField] private Vector2 leafLengthRangeMeters = new Vector2(0.08f, 0.16f);
    [SerializeField] private Vector2 leafWidthToLengthRange = new Vector2(0.52f, 0.78f);
    [SerializeField] private Material foliageCardMaterial;
    [SerializeField, Range(1, 4)] private int foliageCardPlaneCount = 1;

    [Header("Materials")]
    [SerializeField] private Material barkMaterial;
    [SerializeField] private Material foliageMaterial;

    [Header("References")]
    [SerializeField] private ServiceberryShrubHarvestable harvestable;

    private static Material fallbackBarkMaterial;
    private static Material fallbackFoliageMaterial;
    private Transform generatedRoot;

    private void Awake()
    {
        if (generateOnAwake)
        {
            GenerateShrub();
        }
    }

    [ContextMenu("Generate Shrub")]
    public void GenerateShrub()
    {
        System.Random random = new System.Random(randomizeSeed ? Guid.NewGuid().GetHashCode() : randomSeed);
        EnsureHarvestable();
        ClearGeneratedShrub();

        generatedRoot = new GameObject("Generated Shrub").transform;
        generatedRoot.SetParent(transform, false);
        generatedRoot.localPosition = Vector3.zero;
        generatedRoot.localRotation = Quaternion.identity;
        generatedRoot.localScale = Vector3.one;

        float shrubHeight = NextFloat(random, heightRangeMeters.x, heightRangeMeters.y);
        float crownRadius = NextFloat(random, crownRadiusRangeMeters.x, crownRadiusRangeMeters.y);
        float crownBaseHeight = Mathf.Clamp(shrubHeight * crownBaseHeightFraction, shrubHeight * 0.04f, shrubHeight * 0.45f);
        float crownHeight = Mathf.Max(0.45f, shrubHeight - crownBaseHeight);
        int stemCount = Mathf.Max(1, NextIntInclusive(random, stemCountRange));

        List<Mesh> woodyMeshes = new List<Mesh>(stemCount * 4);
        List<PlantFoliageAnchor> foliageAnchors = new List<PlantFoliageAnchor>(stemCount * 8);

        float baseYawOffset = NextFloat(random, 0f, 360f);
        for (int stemIndex = 0; stemIndex < stemCount; stemIndex++)
        {
            float yaw = baseYawOffset + ((360f / stemCount) * stemIndex) + NextFloat(random, -18f, 18f);
            Vector3 outward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float baseOffsetRadius = crownRadius * NextFloat(random, 0f, 0.18f);
            Vector3 stemBase = outward * baseOffsetRadius;
            float stemHeight = shrubHeight * NextFloat(random, 0.72f, 1f);
            float stemLeanRadians = NextFloat(random, stemLeanDegreesRange.x, stemLeanDegreesRange.y) * Mathf.Deg2Rad;
            float lateralReach = Mathf.Tan(stemLeanRadians) * stemHeight * 0.42f;
            float stemArcStrength = NextFloat(random, stemArcStrengthRange.x, stemArcStrengthRange.y);

            List<Vector3> stemPath = BuildStemPath(random, stemBase, outward, stemHeight, lateralReach, stemArcStrength);
            float stemBaseRadius = NextFloat(random, stemBaseRadiusRangeMeters.x, stemBaseRadiusRangeMeters.y);
            List<float> stemRadii = BuildRadiusProfile(
                stemBaseRadius,
                Mathf.Max(0.0025f, stemBaseRadius * stemTipRadiusFraction),
                stemPath.Count);
            woodyMeshes.Add(ProceduralTreeMeshBuilder.CreateTubeMesh(stemPath, stemRadii, radialSegments));

            AddFoliageAnchors(foliageAnchors, stemPath, 0.42f, 0.66f, 0.84f, 1f);

            int branchCount = Mathf.Max(1, NextIntInclusive(random, branchCountPerStemRange));
            for (int branchIndex = 0; branchIndex < branchCount; branchIndex++)
            {
                float alongStem = Mathf.Clamp01(0.24f + (branchIndex / (float)Mathf.Max(1, branchCount - 1)) * 0.62f + NextFloat(random, -0.05f, 0.05f));
                Vector3 branchStart = SamplePath(stemPath, alongStem);
                Vector3 stemTangent = GetPathTangent(stemPath, alongStem);
                Vector3 radialOut = Vector3.ProjectOnPlane(branchStart - stemBase, Vector3.up);
                if (radialOut.sqrMagnitude < 0.0001f)
                {
                    radialOut = Vector3.ProjectOnPlane(stemTangent, Vector3.up);
                }

                radialOut = radialOut.sqrMagnitude < 0.0001f ? outward : radialOut.normalized;
                Vector3 branchOut = (Quaternion.AngleAxis(NextFloat(random, -48f, 48f), Vector3.up) * radialOut).normalized;
                float branchPitch = NextFloat(random, branchPitchDegreesRange.x, branchPitchDegreesRange.y);
                Vector3 branchDirection = ((Quaternion.Euler(branchPitch, 0f, 0f) * branchOut) + (Vector3.up * 0.38f) + (stemTangent * 0.18f)).normalized;
                float branchLength = Mathf.Lerp(branchLengthRangeMeters.x, branchLengthRangeMeters.y, Mathf.Pow(1f - alongStem, 0.55f))
                    * NextFloat(random, 0.82f, 1.14f);
                float branchRadius = Mathf.Max(
                    0.0016f,
                    stemBaseRadius * NextFloat(random, branchRadiusScaleRange.x, branchRadiusScaleRange.y) * Mathf.Lerp(1f, 0.52f, alongStem));

                List<Vector3> branchPath = BuildBranchPath(
                    random,
                    branchStart,
                    branchDirection,
                    branchLength,
                    branchSegments,
                    branchBendStrength,
                    false,
                    alongStem);
                List<float> branchRadii = BuildRadiusProfile(
                    branchRadius,
                    Mathf.Max(0.0011f, branchRadius * branchTipRadiusFraction),
                    branchPath.Count);
                woodyMeshes.Add(ProceduralTreeMeshBuilder.CreateTubeMesh(branchPath, branchRadii, radialSegments));
                AddFoliageAnchors(foliageAnchors, branchPath, 0.36f, 0.62f, 0.88f, 1f);
            }
        }

        MeshFilter woodyMeshFilter = CreateCombinedMeshPart("Wood", woodyMeshes, null, ResolveBarkMaterial());
        DisposeTemporaryMeshes(woodyMeshes);

        CreateFoliage(random, foliageAnchors, crownRadius, crownBaseHeight, crownHeight);
        EnsureInteractionCollider(shrubHeight, crownRadius);

        if (harvestable != null)
        {
            harvestable.ConfigureGeneratedShrub(
                woodyMeshFilter != null ? new[] { woodyMeshFilter } : Array.Empty<MeshFilter>(),
                crownShape,
                crownRadius,
                crownHeight);
        }
    }

    private void EnsureHarvestable()
    {
        if (harvestable == null)
        {
            harvestable = GetComponent<ServiceberryShrubHarvestable>();
        }

        if (harvestable == null)
        {
            harvestable = gameObject.AddComponent<ServiceberryShrubHarvestable>();
        }
    }

    private void ClearGeneratedShrub()
    {
        Transform existingRoot = transform.Find("Generated Shrub");
        if (existingRoot != null)
        {
            if (Application.isPlaying)
            {
                Destroy(existingRoot.gameObject);
            }
            else
            {
                DestroyImmediate(existingRoot.gameObject);
            }
        }

        generatedRoot = null;
    }

    private List<Vector3> BuildStemPath(System.Random random, Vector3 stemBase, Vector3 outward, float stemHeight, float lateralReach, float arcStrength)
    {
        List<Vector3> pathPoints = new List<Vector3>(Mathf.Max(3, stemSegments) + 1);
        Vector3 controlPointOne = stemBase
            + (Vector3.up * stemHeight * 0.28f)
            + (outward * lateralReach * 0.45f)
            + (Vector3.right * NextFloat(random, -1f, 1f) * stemHeight * arcStrength * 0.08f);
        Vector3 controlPointTwo = stemBase
            + (Vector3.up * stemHeight * 0.68f)
            + (outward * lateralReach)
            + (Vector3.forward * NextFloat(random, -1f, 1f) * stemHeight * arcStrength * 0.08f);
        Vector3 tipPoint = stemBase
            + (Vector3.up * stemHeight)
            + (outward * lateralReach * 1.08f);

        int steps = Mathf.Max(3, stemSegments);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            pathPoints.Add(EvaluateCubicBezier(stemBase, controlPointOne, controlPointTwo, tipPoint, t));
        }

        return pathPoints;
    }

    private void CreateFoliage(
        System.Random random,
        IReadOnlyList<PlantFoliageAnchor> foliageAnchors,
        float crownRadius,
        float crownBaseHeight,
        float crownHeight)
    {
        int requestedClusterCount = Mathf.Max(1, NextIntInclusive(random, foliageClusterCountRange));
        List<PlantFoliageAnchor> anchors = new List<PlantFoliageAnchor>(foliageAnchors ?? Array.Empty<PlantFoliageAnchor>());
        if (anchors.Count == 0)
        {
            anchors.Add(new PlantFoliageAnchor(new Vector3(0f, crownBaseHeight + (crownHeight * 0.5f), 0f), Vector3.up));
        }

        int clusterCount = Mathf.Max(requestedClusterCount, Mathf.CeilToInt(anchors.Count * 1.1f));
        bool useFoliageCards = foliageCardMaterial != null;
        Mesh foliageElementMesh = CreateFoliageElementMesh(useFoliageCards);
        List<Mesh> foliageMeshes = new List<Mesh>(clusterCount * leavesPerClusterRange.y);
        List<Matrix4x4> foliageTransforms = new List<Matrix4x4>(clusterCount * leavesPerClusterRange.y);
        List<Mesh> foliagePrototypeMeshes = new List<Mesh>(1) { foliageElementMesh };

        for (int clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
        {
            PlantFoliageAnchor anchor = clusterIndex < anchors.Count
                ? anchors[clusterIndex]
                : anchors[random.Next(0, anchors.Count)];
            float clusterRadius = NextFloat(random, foliageClusterRadiusRangeMeters.x, foliageClusterRadiusRangeMeters.y);
            AppendLeafClusterInstances(random, anchor, clusterRadius, foliageElementMesh, foliageMeshes, foliageTransforms);
        }

        if (foliageMeshes.Count > 0)
        {
            CreateCombinedMeshPart(
                "Foliage",
                foliageMeshes,
                foliageTransforms,
                useFoliageCards ? foliageCardMaterial : ResolveFoliageMaterial());
        }

        DisposeTemporaryMeshes(foliagePrototypeMeshes);
    }

    private Mesh CreateFoliageElementMesh(bool useFoliageCards)
    {
        if (useFoliageCards)
        {
            return ProceduralTreeMeshBuilder.CreateMultiPlaneFoliageCardMesh(1f, 1f, foliageCardPlaneCount);
        }

        switch (foliageRenderStyle)
        {
            case TreeFoliageRenderStyle.BigleafMaple:
                return ProceduralTreeMeshBuilder.CreateMapleLeafMesh(1f, 1f);

            case TreeFoliageRenderStyle.RedAlder:
                return ProceduralTreeMeshBuilder.CreateAlderLeafMesh(1f, 1f);

            default:
                return ProceduralTreeMeshBuilder.CreateServiceberryLeafMesh(1f, 1f);
        }
    }

    private void AppendLeafClusterInstances(
        System.Random random,
        PlantFoliageAnchor anchor,
        float clusterRadius,
        Mesh leafMesh,
        ICollection<Mesh> foliageMeshes,
        ICollection<Matrix4x4> foliageTransforms)
    {
        if (leafMesh == null || foliageMeshes == null || foliageTransforms == null)
        {
            return;
        }

        int leafCount = Mathf.Max(1, NextIntInclusive(random, leavesPerClusterRange));
        Vector3 canopyBias = ((anchor.direction * 0.52f) + Vector3.up).normalized;

        for (int leafIndex = 0; leafIndex < leafCount; leafIndex++)
        {
            Vector3 shellDirection = RandomOnUnitSphere(random);
            shellDirection = new Vector3(shellDirection.x, shellDirection.y * 0.72f, shellDirection.z).normalized;
            float shellDistance = clusterRadius * Mathf.Lerp(0.12f, 1f, Mathf.Pow(NextFloat(random, 0f, 1f), 0.82f));
            Vector3 localOffset = new Vector3(
                shellDirection.x * clusterRadius,
                shellDirection.y * clusterRadius * 0.72f,
                shellDirection.z * clusterRadius) * (shellDistance / Mathf.Max(0.001f, clusterRadius));
            Vector3 leafPosition = anchor.position + localOffset;
            Vector3 leafDirection = ((localOffset.normalized * 0.7f) + (canopyBias * 0.48f) + (Vector3.down * NextFloat(random, 0f, 0.22f))).normalized;
            if (leafDirection.sqrMagnitude < 0.0001f)
            {
                leafDirection = canopyBias;
            }

            float length = NextFloat(random, leafLengthRangeMeters.x, leafLengthRangeMeters.y);
            float width = length * NextFloat(random, leafWidthToLengthRange.x, leafWidthToLengthRange.y);
            Quaternion rotation = CreateBladeRotation(leafDirection, NextFloat(random, 0f, 360f));
            foliageMeshes.Add(leafMesh);
            foliageTransforms.Add(Matrix4x4.TRS(leafPosition, rotation, new Vector3(width, length, 1f)));
        }
    }

    private void EnsureInteractionCollider(float shrubHeight, float crownRadius)
    {
        CapsuleCollider collider = GetComponent<CapsuleCollider>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<CapsuleCollider>();
        }

        float radius = Mathf.Max(crownRadius * 0.45f, 0.22f);
        float height = Mathf.Max(shrubHeight, radius * 2f);
        collider.direction = 1;
        collider.radius = radius;
        collider.height = height;
        collider.center = new Vector3(0f, height * 0.5f, 0f);
    }

    private MeshFilter CreateCombinedMeshPart(string objectName, IReadOnlyList<Mesh> meshes, IReadOnlyList<Matrix4x4> transforms, Material material)
    {
        if (meshes == null || meshes.Count == 0)
        {
            return null;
        }

        Mesh combinedMesh = ProceduralTreeMeshBuilder.CreateCombinedMesh(meshes, transforms);
        return CreateMeshPart(objectName, combinedMesh, Vector3.zero, material);
    }

    private MeshFilter CreateMeshPart(string objectName, Mesh mesh, Vector3 localPosition, Material material)
    {
        GameObject meshObject = new GameObject(objectName);
        meshObject.layer = gameObject.layer;
        meshObject.transform.SetParent(generatedRoot, false);
        meshObject.transform.localPosition = localPosition;
        meshObject.transform.localRotation = Quaternion.identity;
        meshObject.transform.localScale = Vector3.one;

        MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
        mesh.name = $"{objectName} Mesh";
        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;
        return meshFilter;
    }

    private void DisposeTemporaryMeshes(IReadOnlyList<Mesh> meshes)
    {
        if (meshes == null)
        {
            return;
        }

        foreach (Mesh mesh in meshes)
        {
            if (mesh == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }
    }

    private Material ResolveBarkMaterial()
    {
        return ResolveMaterial(
            barkMaterial,
            ref fallbackBarkMaterial,
            new Color(0.42f, 0.28f, 0.18f),
            "Procedural Shrub Bark");
    }

    private Material ResolveFoliageMaterial()
    {
        return ResolveMaterial(
            foliageMaterial,
            ref fallbackFoliageMaterial,
            new Color(0.24f, 0.5f, 0.23f),
            "Procedural Shrub Foliage");
    }

    private static Material ResolveMaterial(Material assignedMaterial, ref Material cachedMaterial, Color fallbackColor, string materialName)
    {
        if (assignedMaterial != null)
        {
            return assignedMaterial;
        }

        if (cachedMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            cachedMaterial = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave
            };

            if (cachedMaterial.HasProperty("_BaseColor"))
            {
                cachedMaterial.SetColor("_BaseColor", fallbackColor);
            }

            if (cachedMaterial.HasProperty("_Color"))
            {
                cachedMaterial.SetColor("_Color", fallbackColor);
            }
        }

        return cachedMaterial;
    }
}
