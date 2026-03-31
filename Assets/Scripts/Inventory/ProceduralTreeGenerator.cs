using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static ProceduralPlantUtility;

[Serializable]
public class ProceduralTreeLodSettings
{
    public bool enableLodGroup = true;
    [Range(0.01f, 0.95f)] public float highDetailTransitionHeight = 0.24f;
    [Range(0.001f, 0.2f)] public float proxyMinimumScreenHeight = 0.02f;
    [Range(1, 4)] public int proxyCardPlaneCount = 2;

    public void Sanitize()
    {
        proxyCardPlaneCount = Mathf.Clamp(proxyCardPlaneCount, 1, 4);
        proxyMinimumScreenHeight = Mathf.Clamp(proxyMinimumScreenHeight, 0.001f, 0.2f);
        highDetailTransitionHeight = Mathf.Clamp(highDetailTransitionHeight, proxyMinimumScreenHeight + 0.02f, 0.95f);
    }
}

public class ProceduralTreeGenerator : MonoBehaviour
{
    private const string GeneratedTreeRootName = "Generated Tree";
    private const string HighDetailRootName = "High Detail";
    private const string ProxyDetailRootName = "Proxy Detail";

    [Header("Species Selection")]
    [SerializeField] private TreeSpeciesData[] availableSpecies = Array.Empty<TreeSpeciesData>();
    [SerializeField] private TreeSpeciesData fixedSpecies;
    [SerializeField] private bool chooseRandomSpecies = true;

    [Header("Generation Controls")]
    [SerializeField] private bool generateOnAwake = true;
    [SerializeField] private int randomSeed = 12345;
    [SerializeField] private bool randomizeSeed = true;

    [Header("Optional Materials")]
    [SerializeField] private Material barkMaterial;
    [SerializeField] private Material foliageMaterial;

    [Header("Rendering Optimization")]
    [SerializeField] private ProceduralTreeLodSettings lodSettings = new ProceduralTreeLodSettings();
    [SerializeField] private bool castFoliageShadows = false;
    [SerializeField] private bool receiveFoliageShadows = false;

    [Header("References")]
    [SerializeField] private ProceduralTreeHarvestable harvestable;

    private static Material fallbackBarkMaterial;
    private static Material fallbackFoliageMaterial;
    private Transform generatedRoot;

    private void Awake()
    {
        if (generateOnAwake)
        {
            GenerateTree();
        }
    }

    private void OnValidate()
    {
        lodSettings ??= new ProceduralTreeLodSettings();
        lodSettings.Sanitize();
    }

    [ContextMenu("Generate Tree")]
    public void GenerateTree()
    {
        System.Random random = new System.Random(randomizeSeed ? Guid.NewGuid().GetHashCode() : randomSeed);
        TreeSpeciesData selectedSpecies = SelectSpecies(random);
        if (selectedSpecies == null)
        {
            Debug.LogWarning($"{gameObject.name} could not generate a tree because no species was assigned.");
            return;
        }

        EnsureHarvestable();
        ClearGeneratedTree();
        lodSettings ??= new ProceduralTreeLodSettings();
        lodSettings.Sanitize();

        TreeGenerationProfile profile = selectedSpecies.GenerationProfile;
        Transform treeRoot = CreateGeneratedChildRoot(transform, GeneratedTreeRootName);
        Transform highDetailRoot = CreateGeneratedChildRoot(treeRoot, HighDetailRootName);
        generatedRoot = highDetailRoot;

        float trunkHeight = NextFloat(random, profile.heightRangeMeters.x, profile.heightRangeMeters.y);
        float trunkRadius = NextFloat(random, profile.trunkRadiusRangeMeters.x, profile.trunkRadiusRangeMeters.y);
        float crownRadius = NextFloat(random, profile.crownRadiusRangeMeters.x, profile.crownRadiusRangeMeters.y);
        float crownBaseHeight = Mathf.Clamp(
            trunkHeight * profile.crownBaseHeightFraction,
            trunkHeight * 0.08f,
            trunkHeight * 0.95f);
        float desiredCrownHeight = Mathf.Clamp(trunkHeight * profile.crownHeightFraction, trunkHeight * 0.15f, trunkHeight);
        float crownHeight = Mathf.Max(0.5f, Mathf.Min(desiredCrownHeight, trunkHeight - crownBaseHeight + (trunkHeight * 0.05f)));

        List<Vector3> trunkPath = BuildTrunkPath(random, trunkHeight, profile);
        List<float> trunkRadii = BuildRadiusProfile(
            trunkRadius,
            Mathf.Max(0.02f, trunkRadius * profile.trunkTopRadiusFraction),
            trunkPath.Count);
        MeshFilter trunk = CreateWoodPart("Trunk", trunkPath, trunkRadii, profile.radialSegments);

        List<PlantFoliageAnchor> foliageAnchors = new List<PlantFoliageAnchor>();
        List<Mesh> branchMeshes = new List<Mesh>();
        BuildBranches(
            random,
            profile,
            trunkPath,
            trunkRadius,
            crownBaseHeight,
            crownHeight,
            crownRadius,
            selectedSpecies.evergreen,
            foliageAnchors,
            branchMeshes);

        MeshFilter generatedBranchMeshFilter = CreateCombinedMeshPart("Branches", branchMeshes, null, ResolveBarkMaterial());
        DisposeTemporaryMeshes(branchMeshes);

        float foliageDensity = NextFloat(
            random,
            profile.foliageUnitsPerCubicMeterRange.x,
            profile.foliageUnitsPerCubicMeterRange.y);

        MeshFilter foliage = CreateFoliageClusters(random, selectedSpecies, profile, foliageAnchors, crownRadius, crownBaseHeight, crownHeight);
        EnsureInteractionCollider(trunkHeight, trunkRadius);
        ConfigureLodRendering(treeRoot, selectedSpecies, trunk, generatedBranchMeshFilter, foliage, crownBaseHeight, crownHeight, crownRadius);

        harvestable.ConfigureGeneratedTree(
            selectedSpecies,
            trunk,
            generatedBranchMeshFilter != null ? new[] { generatedBranchMeshFilter } : Array.Empty<MeshFilter>(),
            profile.crownShape,
            crownRadius,
            crownHeight,
            foliageDensity);

        generatedRoot = treeRoot;
    }

    private void EnsureHarvestable()
    {
        if (harvestable == null)
        {
            harvestable = GetComponent<ProceduralTreeHarvestable>();
        }

        if (harvestable == null)
        {
            harvestable = gameObject.AddComponent<ProceduralTreeHarvestable>();
        }
    }

    private void ClearGeneratedTree()
    {
        Transform existingRoot = transform.Find(GeneratedTreeRootName);
        if (existingRoot != null)
        {
            DestroyGeneratedObject(existingRoot.gameObject);
        }

        ResetLodGroup();
        generatedRoot = null;
    }

    private TreeSpeciesData SelectSpecies(System.Random random)
    {
        if (!chooseRandomSpecies && fixedSpecies != null)
        {
            return fixedSpecies;
        }

        List<TreeSpeciesData> validSpecies = new List<TreeSpeciesData>();
        foreach (var species in availableSpecies)
        {
            if (species != null)
            {
                validSpecies.Add(species);
            }
        }

        if (validSpecies.Count == 0)
        {
            return fixedSpecies;
        }

        return validSpecies[random.Next(0, validSpecies.Count)];
    }

    private MeshFilter CreateWoodPart(string objectName, IReadOnlyList<Vector3> pathPoints, IReadOnlyList<float> radii, int radialSegments)
    {
        Mesh mesh = ProceduralTreeMeshBuilder.CreateTubeMesh(pathPoints, radii, radialSegments);
        return CreateMeshPart(objectName, mesh, Vector3.zero, ResolveBarkMaterial());
    }

    private MeshFilter CreateFoliageClusters(
        System.Random random,
        TreeSpeciesData speciesData,
        TreeGenerationProfile profile,
        IReadOnlyList<PlantFoliageAnchor> foliageAnchors,
        float crownRadius,
        float crownBaseHeight,
        float crownHeight)
    {
        int requestedClusterCount = NextIntInclusive(random, profile.foliageClusterCountRange);
        if (requestedClusterCount <= 0)
        {
            return null;
        }

        List<PlantFoliageAnchor> anchors = new List<PlantFoliageAnchor>(foliageAnchors ?? Array.Empty<PlantFoliageAnchor>());
        if (anchors.Count == 0)
        {
            anchors.Add(new PlantFoliageAnchor(new Vector3(0f, crownBaseHeight + (crownHeight * 0.5f), 0f), Vector3.up));
        }

        TreeFoliageRenderStyle foliageRenderStyle = speciesData != null
            ? speciesData.ResolveFoliageRenderStyle()
            : TreeFoliageRenderStyle.BroadleafSimple;
        Material assignedFoliageCardMaterial = ResolveAssignedFoliageCardMaterial(speciesData);
        bool useFoliageCards = assignedFoliageCardMaterial != null;
        float foliageLengthScale = speciesData != null ? Mathf.Max(0.1f, speciesData.foliageElementLengthScale) : 1f;
        float foliageWidthScale = speciesData != null ? Mathf.Max(0.1f, speciesData.foliageElementWidthScale) : 1f;
        bool needleLikeFoliage = IsNeedleLikeFoliage(foliageRenderStyle);
        int minimumClusterCount = needleLikeFoliage
            ? anchors.Count
            : Mathf.CeilToInt(anchors.Count * 1.35f);
        int clusterCount = Mathf.Max(requestedClusterCount, minimumClusterCount);
        int estimatedElementsPerCluster = GetEstimatedElementsPerCluster(foliageRenderStyle);
        List<Mesh> foliageMeshes = new List<Mesh>(clusterCount * estimatedElementsPerCluster);
        List<Matrix4x4> foliageTransforms = new List<Matrix4x4>(clusterCount * estimatedElementsPerCluster);
        List<Mesh> foliagePrototypeMeshes = new List<Mesh>(1);
        Mesh foliageElementMesh = CreateFoliageElementMesh(speciesData, foliageRenderStyle, useFoliageCards);
        foliagePrototypeMeshes.Add(foliageElementMesh);

        for (int i = 0; i < clusterCount; i++)
        {
            PlantFoliageAnchor anchor = i < anchors.Count
                ? anchors[i]
                : anchors[random.Next(0, anchors.Count)];
            float radiusBase = NextFloat(
                random,
                profile.foliageClusterRadiusRangeMeters.x,
                profile.foliageClusterRadiusRangeMeters.y);
            if (i >= anchors.Count)
            {
                radiusBase *= needleLikeFoliage ? 0.85f : 0.92f;
            }

            AppendFoliageClusterInstances(
                random,
                foliageRenderStyle,
                anchor,
                radiusBase,
                foliageElementMesh,
                foliageMeshes,
                foliageTransforms,
                foliageLengthScale,
                foliageWidthScale);
        }

        if (foliageMeshes.Count > 0)
        {
            Material foliageRenderMaterial = useFoliageCards
                ? assignedFoliageCardMaterial
                : ResolveFoliageMaterial();
            MeshFilter foliageMeshFilter = CreateCombinedMeshPart(
                "Foliage",
                foliageMeshes,
                foliageTransforms,
                foliageRenderMaterial,
                castFoliageShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveFoliageShadows);
            DisposeTemporaryMeshes(foliagePrototypeMeshes);
            return foliageMeshFilter;
        }

        DisposeTemporaryMeshes(foliagePrototypeMeshes);
        return null;
    }

    private static Mesh CreateFoliageElementMesh(TreeSpeciesData speciesData, TreeFoliageRenderStyle foliageRenderStyle, bool useFoliageCards)
    {
        if (useFoliageCards)
        {
            int planeCount = speciesData != null ? speciesData.foliageCardPlaneCount : 1;
            return ProceduralTreeMeshBuilder.CreateMultiPlaneFoliageCardMesh(1f, 1f, planeCount);
        }

        switch (foliageRenderStyle)
        {
            case TreeFoliageRenderStyle.BigleafMaple:
                return ProceduralTreeMeshBuilder.CreateMapleLeafMesh(1f, 1f);

            case TreeFoliageRenderStyle.RedAlder:
                return ProceduralTreeMeshBuilder.CreateAlderLeafMesh(1f, 1f);

            case TreeFoliageRenderStyle.DouglasFir:
                return ProceduralTreeMeshBuilder.CreateDouglasFirNeedleMesh(1f, 1f);

            case TreeFoliageRenderStyle.WesternRedCedar:
                return ProceduralTreeMeshBuilder.CreateCedarSprayMesh(1f, 1f);

            default:
                return ProceduralTreeMeshBuilder.CreateLeafBladeMesh(1f, 1f);
        }
    }

    private static Material ResolveAssignedFoliageCardMaterial(TreeSpeciesData speciesData)
    {
        if (speciesData != null && speciesData.foliageCardMaterial != null)
        {
            return speciesData.foliageCardMaterial;
        }

        return null;
    }

    private static int GetEstimatedElementsPerCluster(TreeFoliageRenderStyle foliageRenderStyle)
    {
        switch (foliageRenderStyle)
        {
            case TreeFoliageRenderStyle.BigleafMaple:
                return 14;

            case TreeFoliageRenderStyle.RedAlder:
                return 22;

            case TreeFoliageRenderStyle.DouglasFir:
                return 30;

            case TreeFoliageRenderStyle.WesternRedCedar:
                return 16;

            default:
                return 20;
        }
    }

    private static bool IsNeedleLikeFoliage(TreeFoliageRenderStyle foliageRenderStyle)
    {
        return foliageRenderStyle == TreeFoliageRenderStyle.DouglasFir
            || foliageRenderStyle == TreeFoliageRenderStyle.WesternRedCedar;
    }

    private static void AppendFoliageClusterInstances(
        System.Random random,
        TreeFoliageRenderStyle foliageRenderStyle,
        PlantFoliageAnchor anchor,
        float clusterRadius,
        Mesh foliageElementMesh,
        ICollection<Mesh> foliageMeshes,
        ICollection<Matrix4x4> foliageTransforms,
        float foliageLengthScale,
        float foliageWidthScale)
    {
        switch (foliageRenderStyle)
        {
            case TreeFoliageRenderStyle.BigleafMaple:
                AppendMapleLeafClusterInstances(random, anchor, clusterRadius, foliageElementMesh, foliageMeshes, foliageTransforms, foliageLengthScale, foliageWidthScale);
                break;

            case TreeFoliageRenderStyle.RedAlder:
                AppendAlderLeafClusterInstances(random, anchor, clusterRadius, foliageElementMesh, foliageMeshes, foliageTransforms, foliageLengthScale, foliageWidthScale);
                break;

            case TreeFoliageRenderStyle.DouglasFir:
                AppendNeedleClusterInstances(random, anchor, clusterRadius, foliageElementMesh, foliageMeshes, foliageTransforms, foliageLengthScale, foliageWidthScale);
                break;

            case TreeFoliageRenderStyle.WesternRedCedar:
                AppendCedarSprayClusterInstances(random, anchor, clusterRadius, foliageElementMesh, foliageMeshes, foliageTransforms, foliageLengthScale, foliageWidthScale);
                break;

            default:
                AppendBroadleafClusterInstances(random, anchor, clusterRadius, foliageElementMesh, foliageMeshes, foliageTransforms, foliageLengthScale, foliageWidthScale);
                break;
        }
    }

    private void BuildBranches(
        System.Random random,
        TreeGenerationProfile profile,
        IReadOnlyList<Vector3> trunkPath,
        float trunkBaseRadius,
        float crownBaseHeight,
        float crownHeight,
        float crownRadius,
        bool evergreen,
        List<PlantFoliageAnchor> foliageAnchors,
        List<Mesh> branchMeshes)
    {
        float trunkHeight = trunkPath[trunkPath.Count - 1].y;
        int totalBranchCount = Mathf.Max(1, NextIntInclusive(random, profile.branchCountRange));

        if (evergreen && (profile.crownShape == TreeCrownShape.Conical || profile.crownShape == TreeCrownShape.Columnar))
        {
            BuildConiferBranches(
                random,
                profile,
                trunkPath,
                trunkBaseRadius,
                trunkHeight,
                crownBaseHeight,
                crownHeight,
                crownRadius,
                totalBranchCount,
                foliageAnchors,
                branchMeshes);
            return;
        }

        BuildBroadleafBranches(
            random,
            profile,
            trunkPath,
            trunkBaseRadius,
            trunkHeight,
            crownBaseHeight,
            crownHeight,
            crownRadius,
            totalBranchCount,
            foliageAnchors,
            branchMeshes);
    }

    private void BuildConiferBranches(
        System.Random random,
        TreeGenerationProfile profile,
        IReadOnlyList<Vector3> trunkPath,
        float trunkBaseRadius,
        float trunkHeight,
        float crownBaseHeight,
        float crownHeight,
        float crownRadius,
        int totalBranchCount,
        List<PlantFoliageAnchor> foliageAnchors,
        List<Mesh> branchMeshes)
    {
        float trunkTopRadius = Mathf.Max(0.02f, trunkBaseRadius * profile.trunkTopRadiusFraction);
        int levelCount = Mathf.Clamp(Mathf.RoundToInt(totalBranchCount / 1.7f), 7, 18);
        float levelYawOffset = NextFloat(random, 0f, 360f);
        float levelSpacingNormalized = levelCount <= 1 ? 1f : 1f / (levelCount - 1f);

        for (int levelIndex = 0; levelIndex < levelCount; levelIndex++)
        {
            float baseNormalizedInCrown = levelCount == 1 ? 0.5f : levelIndex / (float)(levelCount - 1);
            int branchesThisLevel = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Lerp(8f, 4f, baseNormalizedInCrown) + NextFloat(random, -0.4f, 0.9f)),
                4,
                9);

            for (int branchIndex = 0; branchIndex < branchesThisLevel; branchIndex++)
            {
                float withinLevelOffset = branchesThisLevel <= 1
                    ? 0f
                    : ((branchIndex / (float)(branchesThisLevel - 1)) - 0.5f) * levelSpacingNormalized * 0.7f;
                float branchNormalizedInCrown = Mathf.Clamp01(
                    baseNormalizedInCrown
                    + withinLevelOffset
                    + NextFloat(random, -levelSpacingNormalized * 0.14f, levelSpacingNormalized * 0.14f));
                float attachmentHeight = crownBaseHeight + (branchNormalizedInCrown * crownHeight);
                float attachmentT = trunkHeight <= 0f ? 0f : Mathf.Clamp01(attachmentHeight / trunkHeight);
                Vector3 trunkCenter = SamplePath(trunkPath, attachmentT);
                float radiusAtAttachment = Mathf.Lerp(trunkBaseRadius, trunkTopRadius, attachmentT);
                float localCrownRadius = EvaluateCrownRadiusAtHeight(profile.crownShape, branchNormalizedInCrown, crownRadius);
                float yaw = levelYawOffset
                    + ((360f / branchesThisLevel) * branchIndex)
                    + NextFloat(random, -14f, 14f);
                float pitch = Mathf.Clamp(GetBranchPitchDegrees(random, profile, true, branchNormalizedInCrown) - 10f, -55f, 4f);
                Vector3 outward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 branchStart = trunkCenter + (outward * radiusAtAttachment * 0.65f);
                float branchLength = Mathf.Max(
                    crownRadius * 0.14f,
                    localCrownRadius * NextFloat(random, profile.branchLengthScaleRange.x, profile.branchLengthScaleRange.y) * 0.85f);
                float branchRadius = Mathf.Max(
                    trunkBaseRadius * 0.02f,
                    radiusAtAttachment * NextFloat(random, profile.branchRadiusScaleRange.x, profile.branchRadiusScaleRange.y) * Mathf.Lerp(1f, 0.65f, branchNormalizedInCrown));
                Vector3 branchDirection = (Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward).normalized;

                List<Vector3> branchPath = BuildBranchPath(
                    random,
                    branchStart,
                    branchDirection,
                    branchLength,
                    profile.branchSegments,
                    profile.branchBendStrength,
                    true,
                    branchNormalizedInCrown);
                List<float> branchRadii = BuildRadiusProfile(
                    branchRadius,
                    Mathf.Max(0.008f, branchRadius * profile.branchTipRadiusFraction),
                    branchPath.Count);

                branchMeshes.Add(ProceduralTreeMeshBuilder.CreateTubeMesh(branchPath, branchRadii, profile.radialSegments));
                AddFoliageAnchors(foliageAnchors, branchPath, 0.28f, 0.52f, 0.76f, 1f);
            }
        }
    }

    private void BuildBroadleafBranches(
        System.Random random,
        TreeGenerationProfile profile,
        IReadOnlyList<Vector3> trunkPath,
        float trunkBaseRadius,
        float trunkHeight,
        float crownBaseHeight,
        float crownHeight,
        float crownRadius,
        int totalBranchCount,
        List<PlantFoliageAnchor> foliageAnchors,
        List<Mesh> branchMeshes)
    {
        float trunkTopRadius = Mathf.Max(0.02f, trunkBaseRadius * profile.trunkTopRadiusFraction);
        int primaryCount = Mathf.Clamp(Mathf.RoundToInt(totalBranchCount * 0.55f), 5, 10);
        int remainingSecondaryCount = Mathf.Max(0, totalBranchCount - primaryCount);
        int secondaryBaseCount = primaryCount > 0 ? remainingSecondaryCount / primaryCount : 0;
        int secondaryExtraCount = primaryCount > 0 ? remainingSecondaryCount % primaryCount : 0;
        float yawOffset = NextFloat(random, 0f, 360f);

        for (int primaryIndex = 0; primaryIndex < primaryCount; primaryIndex++)
        {
            float crownT = Mathf.Clamp01(
                Mathf.Lerp(0.04f, 0.86f, primaryCount == 1 ? 0.5f : primaryIndex / (float)(primaryCount - 1))
                + NextFloat(random, -0.04f, 0.04f));
            float attachmentHeight = crownBaseHeight + (crownT * crownHeight);
            float attachmentT = trunkHeight <= 0f ? 0f : Mathf.Clamp01(attachmentHeight / trunkHeight);
            Vector3 trunkCenter = SamplePath(trunkPath, attachmentT);
            float radiusAtAttachment = Mathf.Lerp(trunkBaseRadius, trunkTopRadius, attachmentT);
            float yaw = yawOffset + ((360f / primaryCount) * primaryIndex) + NextFloat(random, -28f, 28f);
            Vector3 outward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 branchStart = trunkCenter + (outward * radiusAtAttachment * 0.85f);
            float localCrownRadius = EvaluateCrownRadiusAtHeight(profile.crownShape, crownT, crownRadius);
            float primaryLength = Mathf.Max(
                crownRadius * 0.24f,
                localCrownRadius * NextFloat(random, profile.branchLengthScaleRange.x, profile.branchLengthScaleRange.y));
            float primaryRadius = Mathf.Max(
                trunkBaseRadius * 0.028f,
                radiusAtAttachment * NextFloat(random, profile.branchRadiusScaleRange.x, profile.branchRadiusScaleRange.y) * 1.1f);
            float primaryPitch = Mathf.Clamp(GetBranchPitchDegrees(random, profile, false, crownT) + 10f, 6f, 36f);
            Vector3 primaryDirection = ((Quaternion.Euler(primaryPitch, yaw, 0f) * Vector3.forward) + (Vector3.up * 0.12f)).normalized;

            List<Vector3> primaryPath = BuildBranchPath(
                random,
                branchStart,
                primaryDirection,
                primaryLength,
                profile.branchSegments + 1,
                profile.branchBendStrength * 0.85f,
                false,
                crownT);
            List<float> primaryRadii = BuildRadiusProfile(
                primaryRadius,
                Mathf.Max(0.01f, primaryRadius * profile.branchTipRadiusFraction),
                primaryPath.Count);

            branchMeshes.Add(ProceduralTreeMeshBuilder.CreateTubeMesh(primaryPath, primaryRadii, profile.radialSegments));

            int secondaryCount = secondaryBaseCount + (primaryIndex < secondaryExtraCount ? 1 : 0);
            if (secondaryCount <= 0)
            {
                AddFoliageAnchors(foliageAnchors, primaryPath, 0.6f, 0.82f, 1f);
                continue;
            }

            for (int secondaryIndex = 0; secondaryIndex < secondaryCount; secondaryIndex++)
            {
                float alongPrimary = Mathf.Clamp01(0.14f + (secondaryIndex * 0.11f) + NextFloat(random, -0.04f, 0.04f));
                Vector3 secondaryStart = SamplePath(primaryPath, alongPrimary);
                Vector3 parentTangent = GetPathTangent(primaryPath, alongPrimary);
                Vector3 radialOut = Vector3.ProjectOnPlane(secondaryStart, Vector3.up);
                if (radialOut.sqrMagnitude < 0.0001f)
                {
                    radialOut = Vector3.ProjectOnPlane(parentTangent, Vector3.up);
                }
                radialOut = radialOut.sqrMagnitude < 0.0001f ? Vector3.forward : radialOut.normalized;
                Vector3 spreadDirection = (Quaternion.AngleAxis(NextFloat(random, -55f, 55f), Vector3.up) * radialOut).normalized;
                Vector3 secondaryDirection = ((parentTangent * 0.55f) + (spreadDirection * 0.7f) + (Vector3.up * 0.26f)).normalized;
                float secondaryLength = primaryLength * NextFloat(random, 0.34f, 0.6f) * Mathf.Lerp(1f, 0.7f, alongPrimary);
                float secondaryRadius = Mathf.Max(
                    trunkBaseRadius * 0.012f,
                    primaryRadius * Mathf.Lerp(0.55f, 0.32f, alongPrimary) * NextFloat(random, 0.58f, 0.74f));

                List<Vector3> secondaryPath = BuildBranchPath(
                    random,
                    secondaryStart,
                    secondaryDirection,
                    secondaryLength,
                    Mathf.Max(2, profile.branchSegments - 1),
                    profile.branchBendStrength * 0.75f,
                    false,
                    crownT);
                List<float> secondaryRadii = BuildRadiusProfile(
                    secondaryRadius,
                    Mathf.Max(0.006f, secondaryRadius * profile.branchTipRadiusFraction),
                    secondaryPath.Count);

                branchMeshes.Add(ProceduralTreeMeshBuilder.CreateTubeMesh(secondaryPath, secondaryRadii, profile.radialSegments));
                AddFoliageAnchors(foliageAnchors, secondaryPath, 0.45f, 0.72f, 1f);
            }

            AddFoliageAnchors(foliageAnchors, primaryPath, 0.72f, 1f);
        }
    }

    private static void AppendMapleLeafClusterInstances(
        System.Random random,
        PlantFoliageAnchor anchor,
        float clusterRadius,
        Mesh leafMesh,
        ICollection<Mesh> foliageMeshes,
        ICollection<Matrix4x4> foliageTransforms,
        float foliageLengthScale,
        float foliageWidthScale)
    {
        AppendBroadleafClusterInstances(
            random,
            anchor,
            clusterRadius,
            leafMesh,
            foliageMeshes,
            foliageTransforms,
            new Vector2Int(10, 18),
            new Vector2(0.46f, 0.7f),
            new Vector2(0.82f, 1.08f),
            0.58f,
            0.9f,
            0.18f,
            0.32f,
            foliageLengthScale,
            foliageWidthScale);
    }

    private static void AppendAlderLeafClusterInstances(
        System.Random random,
        PlantFoliageAnchor anchor,
        float clusterRadius,
        Mesh leafMesh,
        ICollection<Mesh> foliageMeshes,
        ICollection<Matrix4x4> foliageTransforms,
        float foliageLengthScale,
        float foliageWidthScale)
    {
        AppendBroadleafClusterInstances(
            random,
            anchor,
            clusterRadius,
            leafMesh,
            foliageMeshes,
            foliageTransforms,
            new Vector2Int(18, 30),
            new Vector2(0.34f, 0.52f),
            new Vector2(0.46f, 0.62f),
            0.82f,
            0.76f,
            0.08f,
            0.52f,
            foliageLengthScale,
            foliageWidthScale);
    }

    private static void AppendBroadleafClusterInstances(
        System.Random random,
        PlantFoliageAnchor anchor,
        float clusterRadius,
        Mesh leafMesh,
        ICollection<Mesh> foliageMeshes,
        ICollection<Matrix4x4> foliageTransforms,
        float foliageLengthScale,
        float foliageWidthScale)
    {
        AppendBroadleafClusterInstances(
            random,
            anchor,
            clusterRadius,
            leafMesh,
            foliageMeshes,
            foliageTransforms,
            new Vector2Int(16, 28),
            new Vector2(0.38f, 0.62f),
            new Vector2(0.34f, 0.52f),
            0.75f,
            0.8f,
            0.12f,
            0.45f,
            foliageLengthScale,
            foliageWidthScale);
    }

    private static void AppendBroadleafClusterInstances(
        System.Random random,
        PlantFoliageAnchor anchor,
        float clusterRadius,
        Mesh leafMesh,
        ICollection<Mesh> foliageMeshes,
        ICollection<Matrix4x4> foliageTransforms,
        Vector2Int leafCountRange,
        Vector2 leafLengthScaleRange,
        Vector2 leafWidthToLengthRange,
        float shellVerticalScale,
        float shellDistanceExponent,
        float downwardBiasMax,
        float canopyBiasWeight,
        float foliageLengthScale,
        float foliageWidthScale)
    {
        if (leafMesh == null || foliageMeshes == null || foliageTransforms == null)
        {
            return;
        }

        float radiusT = Mathf.InverseLerp(0.35f, 1.35f, clusterRadius);
        int leafCount = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(leafCountRange.x, leafCountRange.y, radiusT) + NextFloat(random, -2f, 2.5f)),
            Mathf.Max(1, leafCountRange.x - 1),
            leafCountRange.y + 2);
        Vector3 anchorDirection = anchor.direction.sqrMagnitude < 0.0001f ? Vector3.up : anchor.direction.normalized;
        Vector3 canopyBias = ((anchorDirection * canopyBiasWeight) + Vector3.up).normalized;

        for (int leafIndex = 0; leafIndex < leafCount; leafIndex++)
        {
            Vector3 shellDirection = RandomOnUnitSphere(random);
            shellDirection = new Vector3(shellDirection.x, shellDirection.y * shellVerticalScale, shellDirection.z).normalized;
            float shellDistance = clusterRadius * Mathf.Lerp(0.1f, 1f, Mathf.Pow(NextFloat(random, 0f, 1f), shellDistanceExponent));
            Vector3 localOffset = new Vector3(
                shellDirection.x * clusterRadius,
                shellDirection.y * clusterRadius * shellVerticalScale,
                shellDirection.z * clusterRadius) * (shellDistance / Mathf.Max(0.001f, clusterRadius));
            Vector3 leafPosition = anchor.position + localOffset;
            Vector3 leafDirection = ((localOffset.normalized * 0.72f) + (canopyBias * 0.45f) + (Vector3.down * NextFloat(random, 0f, downwardBiasMax))).normalized;
            if (leafDirection.sqrMagnitude < 0.0001f)
            {
                leafDirection = canopyBias;
            }

            float length = clusterRadius * NextFloat(random, leafLengthScaleRange.x, leafLengthScaleRange.y) * foliageLengthScale;
            float width = length * NextFloat(random, leafWidthToLengthRange.x, leafWidthToLengthRange.y) * foliageWidthScale;
            Quaternion rotation = CreateBladeRotation(leafDirection, NextFloat(random, 0f, 360f));
            foliageMeshes.Add(leafMesh);
            foliageTransforms.Add(Matrix4x4.TRS(leafPosition, rotation, new Vector3(width, length, 1f)));
        }
    }

    private static void AppendNeedleClusterInstances(
        System.Random random,
        PlantFoliageAnchor anchor,
        float clusterRadius,
        Mesh needleMesh,
        ICollection<Mesh> foliageMeshes,
        ICollection<Matrix4x4> foliageTransforms,
        float foliageLengthScale,
        float foliageWidthScale)
    {
        if (needleMesh == null || foliageMeshes == null || foliageTransforms == null)
        {
            return;
        }

        float radiusT = Mathf.InverseLerp(0.35f, 1.35f, clusterRadius);
        int needleCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(18f, 34f, radiusT) + NextFloat(random, -2f, 4f)), 18, 36);
        Vector3 sprayAxis = ((anchor.direction * 0.85f) + (Vector3.up * 0.15f)).normalized;
        if (sprayAxis.sqrMagnitude < 0.0001f)
        {
            sprayAxis = Vector3.up;
        }

        Vector3 radialBasis = GetPerpendicular(sprayAxis);
        float sprayLength = clusterRadius * NextFloat(random, 0.85f, 1.25f);
        float sprayWidth = clusterRadius * NextFloat(random, 0.18f, 0.34f);

        for (int needleIndex = 0; needleIndex < needleCount; needleIndex++)
        {
            float around = (needleIndex / (float)needleCount) * 360f + NextFloat(random, -18f, 18f);
            Vector3 ringDirection = Quaternion.AngleAxis(around, sprayAxis) * radialBasis;
            float alongSpray = Mathf.Lerp(0.05f, 1f, Mathf.Pow(NextFloat(random, 0f, 1f), 0.78f));
            Vector3 needlePosition = anchor.position
                + (sprayAxis * sprayLength * alongSpray * 0.35f)
                + (ringDirection * sprayWidth * NextFloat(random, 0.08f, 0.58f));
            Vector3 needleDirection = ((sprayAxis * NextFloat(random, 0.72f, 1f))
                + (ringDirection * NextFloat(random, 0.18f, 0.42f))
                + (Vector3.down * NextFloat(random, 0.02f, 0.14f))).normalized;
            if (needleDirection.sqrMagnitude < 0.0001f)
            {
                needleDirection = sprayAxis;
            }

            float length = clusterRadius * NextFloat(random, 0.48f, 0.9f) * foliageLengthScale;
            float width = length * NextFloat(random, 0.05f, 0.12f) * foliageWidthScale;
            Quaternion rotation = CreateBladeRotation(needleDirection, NextFloat(random, 0f, 360f));
            foliageMeshes.Add(needleMesh);
            foliageTransforms.Add(Matrix4x4.TRS(needlePosition, rotation, new Vector3(width, length, 1f)));
        }
    }

    private static void AppendCedarSprayClusterInstances(
        System.Random random,
        PlantFoliageAnchor anchor,
        float clusterRadius,
        Mesh sprayMesh,
        ICollection<Mesh> foliageMeshes,
        ICollection<Matrix4x4> foliageTransforms,
        float foliageLengthScale,
        float foliageWidthScale)
    {
        if (sprayMesh == null || foliageMeshes == null || foliageTransforms == null)
        {
            return;
        }

        float radiusT = Mathf.InverseLerp(0.35f, 1.1f, clusterRadius);
        int sprayCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(12f, 22f, radiusT) + NextFloat(random, -1f, 2f)), 11, 24);
        Vector3 outward = Vector3.ProjectOnPlane(anchor.position, Vector3.up);
        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = Vector3.ProjectOnPlane(anchor.direction, Vector3.up);
        }
        outward = outward.sqrMagnitude < 0.0001f ? Vector3.forward : outward.normalized;
        Vector3 droopAxis = ((outward * 0.72f) + (anchor.direction * 0.16f) + (Vector3.down * 0.82f)).normalized;
        if (droopAxis.sqrMagnitude < 0.0001f)
        {
            droopAxis = ((outward * 0.6f) + Vector3.down).normalized;
        }

        Vector3 lateral = Vector3.Cross(Vector3.up, outward);
        lateral = lateral.sqrMagnitude < 0.0001f ? Vector3.right : lateral.normalized;
        float sprayLength = clusterRadius * NextFloat(random, 1.08f, 1.52f);
        float frondWidth = clusterRadius * NextFloat(random, 0.24f, 0.42f);
        float baseOutwardOffset = clusterRadius * NextFloat(random, 0.14f, 0.32f);

        for (int sprayIndex = 0; sprayIndex < sprayCount; sprayIndex++)
        {
            float lateralBlend = sprayCount == 1
                ? 0f
                : Mathf.Lerp(-1f, 1f, sprayIndex / (float)(sprayCount - 1));
            float lateralOffset = lateralBlend * frondWidth * 0.6f + NextFloat(random, -frondWidth * 0.08f, frondWidth * 0.08f);
            float alongSpray = Mathf.Lerp(0.08f, 1f, Mathf.Pow(NextFloat(random, 0f, 1f), 0.74f));
            Vector3 frondBase = anchor.position
                + (outward * baseOutwardOffset)
                + (lateral * lateralOffset)
                + (Vector3.down * clusterRadius * NextFloat(random, 0.02f, 0.1f));
            Vector3 sprayPosition = anchor.position
                + (frondBase - anchor.position)
                + (droopAxis * sprayLength * alongSpray * 0.44f);
            Vector3 sprayDirection = ((droopAxis * NextFloat(random, 0.82f, 1f))
                + (outward * NextFloat(random, 0.18f, 0.34f))
                + (lateral * lateralBlend * NextFloat(random, 0.08f, 0.18f))
                + (Vector3.down * NextFloat(random, 0.04f, 0.14f))).normalized;
            if (sprayDirection.sqrMagnitude < 0.0001f)
            {
                sprayDirection = droopAxis;
            }

            Vector3 sprayPlaneNormal = Vector3.Cross(sprayDirection, lateral);
            if (sprayPlaneNormal.sqrMagnitude < 0.0001f)
            {
                sprayPlaneNormal = outward;
            }

            float length = clusterRadius * NextFloat(random, 0.96f, 1.34f) * foliageLengthScale;
            float width = length * NextFloat(random, 0.38f, 0.6f) * foliageWidthScale;
            Quaternion rotation = CreateFlatFoliageRotation(sprayDirection, sprayPlaneNormal);
            foliageMeshes.Add(sprayMesh);
            foliageTransforms.Add(Matrix4x4.TRS(sprayPosition, rotation, new Vector3(width, length, 1f)));
        }
    }

    private List<Vector3> BuildTrunkPath(System.Random random, float trunkHeight, TreeGenerationProfile profile)
    {
        List<Vector3> pathPoints = new List<Vector3>(Mathf.Max(3, profile.trunkSegments) + 1);
        Vector3 basePoint = Vector3.zero;
        Vector3 controlPointOne = new Vector3(
            NextFloat(random, -1f, 1f) * trunkHeight * profile.trunkBendStrength,
            trunkHeight * 0.3f,
            NextFloat(random, -1f, 1f) * trunkHeight * profile.trunkBendStrength);
        Vector3 controlPointTwo = new Vector3(
            NextFloat(random, -1f, 1f) * trunkHeight * profile.trunkBendStrength,
            trunkHeight * 0.72f,
            NextFloat(random, -1f, 1f) * trunkHeight * profile.trunkBendStrength);
        Vector3 topPoint = new Vector3(
            (controlPointOne.x + controlPointTwo.x) * 0.35f,
            trunkHeight,
            (controlPointOne.z + controlPointTwo.z) * 0.35f);

        int segmentCount = Mathf.Max(3, profile.trunkSegments);
        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            pathPoints.Add(EvaluateCubicBezier(basePoint, controlPointOne, controlPointTwo, topPoint, t));
        }

        return pathPoints;
    }

    private void EnsureInteractionCollider(float trunkHeight, float trunkRadius)
    {
        CapsuleCollider collider = GetComponent<CapsuleCollider>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<CapsuleCollider>();
        }

        float colliderRadius = Mathf.Max(trunkRadius * 1.15f, 0.18f);
        float colliderHeight = Mathf.Max(trunkHeight, colliderRadius * 2f);
        collider.direction = 1;
        collider.radius = colliderRadius;
        collider.height = colliderHeight;
        collider.center = new Vector3(0f, collider.height * 0.5f, 0f);
    }

    private void ConfigureLodRendering(
        Transform treeRoot,
        TreeSpeciesData speciesData,
        MeshFilter trunkMeshFilter,
        MeshFilter branchMeshFilter,
        MeshFilter foliageMeshFilter,
        float crownBaseHeight,
        float crownHeight,
        float crownRadius)
    {
        List<Renderer> highDetailRenderers = new List<Renderer>(3);
        AddRendererIfValid(highDetailRenderers, trunkMeshFilter);
        AddRendererIfValid(highDetailRenderers, branchMeshFilter);
        AddRendererIfValid(highDetailRenderers, foliageMeshFilter);

        LODGroup lodGroup = GetComponent<LODGroup>();
        if (!lodSettings.enableLodGroup || highDetailRenderers.Count == 0)
        {
            if (lodGroup != null)
            {
                lodGroup.SetLODs(Array.Empty<LOD>());
                lodGroup.enabled = false;
            }

            return;
        }

        Transform proxyRoot = CreateGeneratedChildRoot(treeRoot, ProxyDetailRootName);
        MeshFilter proxyTrunk = CloneMeshPart(trunkMeshFilter, proxyRoot, "Proxy Trunk", ShadowCastingMode.On, true);
        MeshFilter proxyBranches = CloneMeshPart(branchMeshFilter, proxyRoot, "Proxy Branches", ShadowCastingMode.Off, false);
        MeshFilter proxyCrown = CreateProxyCrown(speciesData, proxyRoot, crownBaseHeight, crownHeight, crownRadius);

        List<Renderer> proxyRenderers = new List<Renderer>(3);
        AddRendererIfValid(proxyRenderers, proxyTrunk);
        AddRendererIfValid(proxyRenderers, proxyBranches);
        AddRendererIfValid(proxyRenderers, proxyCrown);

        if (proxyRenderers.Count == 0)
        {
            if (lodGroup != null)
            {
                lodGroup.SetLODs(Array.Empty<LOD>());
                lodGroup.enabled = false;
            }

            return;
        }

        if (lodGroup == null)
        {
            lodGroup = gameObject.AddComponent<LODGroup>();
        }

        lodGroup.fadeMode = LODFadeMode.None;
        lodGroup.animateCrossFading = false;
        lodGroup.localReferencePoint = new Vector3(0f, crownBaseHeight + (crownHeight * 0.5f), 0f);
        lodGroup.size = Mathf.Max(1f, crownHeight, crownRadius * 2f);
        lodGroup.SetLODs(new[]
        {
            new LOD(lodSettings.highDetailTransitionHeight, highDetailRenderers.ToArray()),
            new LOD(lodSettings.proxyMinimumScreenHeight, proxyRenderers.ToArray())
        });
        lodGroup.enabled = true;
        lodGroup.RecalculateBounds();
    }

    private MeshFilter CreateProxyCrown(TreeSpeciesData speciesData, Transform proxyRoot, float crownBaseHeight, float crownHeight, float crownRadius)
    {
        if (crownHeight <= 0f || crownRadius <= 0f)
        {
            return null;
        }

        Material assignedCardMaterial = ResolveAssignedFoliageCardMaterial(speciesData);
        bool useCardProxy = assignedCardMaterial != null;
        Material crownMaterial = useCardProxy ? assignedCardMaterial : ResolveFoliageMaterial();
        Mesh crownMesh;
        Vector3 localPosition;

        if (useCardProxy)
        {
            crownMesh = ProceduralTreeMeshBuilder.CreateMultiPlaneFoliageCardMesh(
                crownHeight,
                crownRadius * 2.1f,
                lodSettings.proxyCardPlaneCount);
            localPosition = new Vector3(0f, crownBaseHeight, 0f);
        }
        else
        {
            TreeCrownShape crownShape = speciesData != null ? speciesData.GenerationProfile.crownShape : TreeCrownShape.Rounded;
            Vector3 proxyRadii = GetProxyCrownRadii(crownShape, crownRadius, crownHeight);
            crownMesh = ProceduralTreeMeshBuilder.CreateEllipsoidMesh(proxyRadii, 6, 4);
            localPosition = new Vector3(0f, crownBaseHeight + (crownHeight * 0.5f), 0f);
        }

        Transform previousGeneratedRoot = generatedRoot;
        generatedRoot = proxyRoot;
        MeshFilter proxyCrown = CreateMeshPart(
            "Proxy Crown",
            crownMesh,
            localPosition,
            crownMaterial,
            ShadowCastingMode.Off,
            false);
        generatedRoot = previousGeneratedRoot;
        return proxyCrown;
    }

    private MeshFilter CloneMeshPart(
        MeshFilter sourceMeshFilter,
        Transform parent,
        string objectName,
        ShadowCastingMode shadowCastingMode,
        bool receiveShadows)
    {
        if (sourceMeshFilter == null || sourceMeshFilter.sharedMesh == null)
        {
            return null;
        }

        MeshRenderer sourceRenderer = sourceMeshFilter.GetComponent<MeshRenderer>();
        if (sourceRenderer == null)
        {
            return null;
        }

        Transform previousGeneratedRoot = generatedRoot;
        generatedRoot = parent;
        MeshFilter clonedMeshFilter = CreateMeshPart(
            objectName,
            sourceMeshFilter.sharedMesh,
            sourceMeshFilter.transform.localPosition,
            sourceRenderer.sharedMaterial,
            shadowCastingMode,
            receiveShadows,
            false);
        generatedRoot = previousGeneratedRoot;
        return clonedMeshFilter;
    }

    private static Vector3 GetProxyCrownRadii(TreeCrownShape crownShape, float crownRadius, float crownHeight)
    {
        float horizontalRadius = crownRadius;
        float verticalRadius = crownHeight * 0.5f;

        switch (crownShape)
        {
            case TreeCrownShape.Conical:
                horizontalRadius *= 0.78f;
                verticalRadius *= 0.6f;
                break;

            case TreeCrownShape.Columnar:
                horizontalRadius *= 0.7f;
                verticalRadius *= 0.62f;
                break;

            case TreeCrownShape.OpenRounded:
                horizontalRadius *= 1.05f;
                verticalRadius *= 0.45f;
                break;
        }

        return new Vector3(
            Mathf.Max(0.2f, horizontalRadius),
            Mathf.Max(0.2f, verticalRadius),
            Mathf.Max(0.2f, horizontalRadius));
    }

    private static void AddRendererIfValid(ICollection<Renderer> renderers, MeshFilter meshFilter)
    {
        if (renderers == null || meshFilter == null)
        {
            return;
        }

        MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderers.Add(renderer);
        }
    }

    private void ResetLodGroup()
    {
        LODGroup lodGroup = GetComponent<LODGroup>();
        if (lodGroup == null)
        {
            return;
        }

        lodGroup.SetLODs(Array.Empty<LOD>());
        lodGroup.enabled = false;
    }

    private void DestroyGeneratedObject(UnityEngine.Object targetObject)
    {
        if (targetObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(targetObject);
        }
        else
        {
            DestroyImmediate(targetObject);
        }
    }

    private Transform CreateGeneratedChildRoot(Transform parent, string objectName)
    {
        GameObject childObject = new GameObject(objectName);
        childObject.layer = gameObject.layer;
        childObject.transform.SetParent(parent, false);
        childObject.transform.localPosition = Vector3.zero;
        childObject.transform.localRotation = Quaternion.identity;
        childObject.transform.localScale = Vector3.one;
        return childObject.transform;
    }

    private MeshFilter CreateMeshPart(
        string objectName,
        Mesh mesh,
        Vector3 localPosition,
        Material material,
        ShadowCastingMode shadowCastingMode = ShadowCastingMode.On,
        bool receiveShadows = true,
        bool renameMesh = true)
    {
        GameObject meshObject = new GameObject(objectName);
        meshObject.layer = gameObject.layer;
        meshObject.transform.SetParent(generatedRoot, false);
        meshObject.transform.localPosition = localPosition;
        meshObject.transform.localRotation = Quaternion.identity;
        meshObject.transform.localScale = Vector3.one;

        MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
        if (renameMesh && mesh != null)
        {
            mesh.name = $"{objectName} Mesh";
        }

        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = shadowCastingMode;
        meshRenderer.receiveShadows = receiveShadows;
        return meshFilter;
    }

    private MeshFilter CreateCombinedMeshPart(
        string objectName,
        IReadOnlyList<Mesh> meshes,
        IReadOnlyList<Matrix4x4> transforms,
        Material material,
        ShadowCastingMode shadowCastingMode = ShadowCastingMode.On,
        bool receiveShadows = true)
    {
        if (meshes == null || meshes.Count == 0)
        {
            return null;
        }

        Mesh combinedMesh = ProceduralTreeMeshBuilder.CreateCombinedMesh(meshes, transforms);
        return CreateMeshPart(objectName, combinedMesh, Vector3.zero, material, shadowCastingMode, receiveShadows);
    }

    private void DisposeTemporaryMeshes(IReadOnlyList<Mesh> meshes)
    {
        if (meshes == null)
        {
            return;
        }

        foreach (var mesh in meshes)
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
            "Procedural Tree Bark");
    }

    private Material ResolveFoliageMaterial()
    {
        return ResolveMaterial(
            foliageMaterial,
            ref fallbackFoliageMaterial,
            new Color(0.21f, 0.48f, 0.2f),
            "Procedural Tree Foliage");
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

    private static float EvaluateCrownRadiusAtHeight(TreeCrownShape crownShape, float normalizedHeight, float maxRadius)
    {
        normalizedHeight = Mathf.Clamp01(normalizedHeight);

        switch (crownShape)
        {
            case TreeCrownShape.Conical:
                return maxRadius * Mathf.Lerp(1f, 0.08f, normalizedHeight);

            case TreeCrownShape.Columnar:
                return maxRadius * (0.68f + (0.18f * Mathf.Sin(normalizedHeight * Mathf.PI)));

            case TreeCrownShape.OpenRounded:
                {
                    float offset = (normalizedHeight - 0.58f) / 0.58f;
                    return maxRadius * 0.92f * Mathf.Sqrt(Mathf.Max(0f, 1f - (offset * offset)));
                }

            default:
                {
                    float offset = (normalizedHeight - 0.5f) / 0.5f;
                    return maxRadius * Mathf.Sqrt(Mathf.Max(0f, 1f - (offset * offset)));
                }
        }
    }

    private static float GetBranchPitchDegrees(System.Random random, TreeGenerationProfile profile, bool evergreen, float normalizedInCrown)
    {
        float pitch = NextFloat(random, profile.branchPitchDegreesRange.x, profile.branchPitchDegreesRange.y);

        if (evergreen)
        {
            pitch += Mathf.Lerp(-12f, 8f, normalizedInCrown);
        }
        else
        {
            pitch += Mathf.Lerp(-2f, 10f, normalizedInCrown);
        }

        return pitch;
    }
}
