using System;
using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

[DefaultExecutionOrder(1000)]
public class ProceduralVoxelStartAreaSystem : MonoBehaviour
{
    private const string DefaultGeneratedRootName = "Generated Voxel Start Area";
    [Header("References")]
    [SerializeField] private ProceduralVoxelTerrain voxelTerrain;
    [SerializeField] private ProceduralVoxelTerrainWaterSystem waterSystem;
    [SerializeField] private ProceduralVoxelTerrainScatterer scatterer;
    [SerializeField] private Transform playerRoot;

    [Header("Generation")]
    [SerializeField] private int seed = 48621;
    [SerializeField] private bool randomizeSeed = false;
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool repositionPlayerOnStart = true;
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private string generatedRootName = DefaultGeneratedRootName;
    [SerializeField] private bool generateTerrainBeforeStartArea = true;
    [SerializeField] private bool generateWaterBeforeStartArea = true;
    [SerializeField] private bool generateScatterBeforeStartArea = true;

    [Header("Candidate Search")]
    [SerializeField, Min(16)] private int candidateSamples = 120;
    [SerializeField, Min(4f)] private float edgePaddingMeters = 18f;
    [SerializeField, Range(0f, 25f)] private float maxSlopeDegrees = 10f;
    [SerializeField] private Vector2 freshwaterDistanceRangeMeters = new Vector2(8f, 36f);
    [SerializeField, Min(1f)] private float preferredFreshwaterDistanceMeters = 18f;
    [SerializeField, Min(1f)] private float localPatchCheckRadiusMeters = 6f;
    [SerializeField, Min(0.1f)] private float maxPatchHeightVariationMeters = 1.6f;

    [Header("Clearing")]
    [SerializeField, Min(3f)] private float clearingRadiusMeters = 9f;
    [SerializeField, Min(0f)] private float clearingRemovalPaddingMeters = 2.5f;

    [Header("Landmarks")]
    [SerializeField] private bool createNaturalLandmarks = true;
    [SerializeField, Range(1, 4)] private int landmarkCount = 3;
    [SerializeField, Min(4f)] private float landmarkRingRadiusMeters = 11f;
    [SerializeField] private Color boulderColor = new Color(0.46f, 0.5f, 0.45f, 1f);
    [SerializeField] private Color logColor = new Color(0.38f, 0.27f, 0.16f, 1f);
    [SerializeField] private Color snagColor = new Color(0.29f, 0.21f, 0.14f, 1f);

    [Header("Crashed Ship")]
    [Tooltip("Placeholder ship prefab spawned at the start area. " +
             "Assign Assets/Prefabs/CrashedShip.prefab here.")]
    [SerializeField] private GameObject shipPrefab;
    [SerializeField] private bool spawnShip = true;
    [Tooltip("Offset from the clearing center at which the ship is placed. " +
             "Keeps the ship out of the player's immediate spawn point.")]
    [SerializeField] private Vector3 shipCenterOffset = new Vector3(14f, 0f, 6f);

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color clearingGizmoColor = new Color(0.52f, 0.86f, 0.58f, 0.85f);
    [SerializeField] private Color spawnGizmoColor = new Color(0.95f, 0.88f, 0.42f, 0.95f);

    [NonSerialized] private bool hasGeneratedStartArea;
    [NonSerialized] private Vector3 lastClearingCenter;
    [NonSerialized] private Vector3 lastSuggestedLookTarget;
    [NonSerialized] private Vector3 lastSpawnPosition;

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;

    private void Reset()
    {
        ApplyCoastalRainforestStartPreset();
    }

    private void OnValidate()
    {
        generatedRootName = string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName.Trim();
        candidateSamples = Mathf.Max(16, candidateSamples);
        edgePaddingMeters = Mathf.Max(4f, edgePaddingMeters);
        maxSlopeDegrees = Mathf.Clamp(maxSlopeDegrees, 0f, 25f);
        freshwaterDistanceRangeMeters = new Vector2(
            Mathf.Max(0f, Mathf.Min(freshwaterDistanceRangeMeters.x, freshwaterDistanceRangeMeters.y)),
            Mathf.Max(0f, Mathf.Max(freshwaterDistanceRangeMeters.x, freshwaterDistanceRangeMeters.y)));
        preferredFreshwaterDistanceMeters = Mathf.Max(1f, preferredFreshwaterDistanceMeters);
        localPatchCheckRadiusMeters = Mathf.Max(1f, localPatchCheckRadiusMeters);
        maxPatchHeightVariationMeters = Mathf.Max(0.1f, maxPatchHeightVariationMeters);
        clearingRadiusMeters = Mathf.Max(3f, clearingRadiusMeters);
        clearingRemovalPaddingMeters = Mathf.Max(0f, clearingRemovalPaddingMeters);
        landmarkCount = Mathf.Clamp(landmarkCount, 1, 4);
        landmarkRingRadiusMeters = Mathf.Max(clearingRadiusMeters + 1f, landmarkRingRadiusMeters);
    }

    private IEnumerator Start()
    {
        if (!Application.isPlaying || !generateOnStart)
        {
            yield break;
        }

        voxelTerrain ??= GetComponent<ProceduralVoxelTerrain>();
        yield return null;
        while (voxelTerrain != null)
        {
            if (voxelTerrain.HasReadyGameplayTerrain)
            {
                break;
            }

            if (!voxelTerrain.IsTerrainGenerationInProgress)
            {
                if (generateTerrainBeforeStartArea && voxelTerrain.IsRuntimeStreamingModeActive)
                {
                    voxelTerrain.GenerateTerrainWithConfiguredMode(voxelTerrain.ClearExistingBeforeGenerate);
                    yield return null;
                    continue;
                }

                break;
            }

            yield return null;
        }

        GenerateStartArea(repositionPlayerOnStart);
    }

    [ContextMenu("Apply Coastal Rainforest Start Preset")]
    public void ApplyCoastalRainforestStartPreset()
    {
        candidateSamples = 120;
        edgePaddingMeters = 18f;
        maxSlopeDegrees = 10f;
        freshwaterDistanceRangeMeters = new Vector2(8f, 36f);
        preferredFreshwaterDistanceMeters = 18f;
        localPatchCheckRadiusMeters = 6f;
        maxPatchHeightVariationMeters = 1.6f;
        clearingRadiusMeters = 9f;
        clearingRemovalPaddingMeters = 2.5f;
        landmarkCount = 3;
        landmarkRingRadiusMeters = 11f;
    }

    [ContextMenu("Generate Start Area")]
    public void GenerateStartAreaFromContextMenu()
    {
        GenerateStartArea(false);
    }

    [ContextMenu("Clear Generated Start Area")]
    public void ClearGeneratedStartAreaFromContextMenu()
    {
        ClearGeneratedStartArea();
    }

    public bool GenerateStartArea(bool repositionPlayer)
    {
        if (!ResolveDependencies())
        {
            return false;
        }

        if (randomizeSeed)
        {
            seed = Environment.TickCount;
        }

        if (clearExistingBeforeGenerate)
        {
            ClearGeneratedStartAreaContents();
        }

        List<Vector3> scatterPositions = StartAreaGenerator.GatherScatterPositions(scatterer);
        System.Random random = new System.Random(seed);
        if (!StartAreaGenerator.TryFindBestCandidate(
            random, scatterPositions,
            voxelTerrain, waterSystem,
            candidateSamples, edgePaddingMeters, maxSlopeDegrees,
            freshwaterDistanceRangeMeters, preferredFreshwaterDistanceMeters,
            localPatchCheckRadiusMeters, maxPatchHeightVariationMeters,
            clearingRadiusMeters,
            out StartAreaCandidate candidate))
        {
            Debug.LogWarning($"{gameObject.name} could not find a suitable voxel starting area candidate.");
            return false;
        }

        StartAreaGenerator.RemoveScatterWithinRadius(scatterer, candidate.center, clearingRadiusMeters + clearingRemovalPaddingMeters);

        Transform generatedRoot = EnsureGeneratedRoot();
        if (createNaturalLandmarks)
        {
            StartAreaGenerator.CreateLandmarks(
                random, candidate, generatedRoot,
                voxelTerrain, waterSystem,
                landmarkCount, landmarkRingRadiusMeters,
                gameObject.layer,
                boulderColor, logColor, snagColor);
        }

        lastClearingCenter = candidate.center;
        lastSuggestedLookTarget = candidate.nearestFreshwaterPoint;
        lastSpawnPosition = candidate.center;
        hasGeneratedStartArea = true;

        if (spawnShip && shipPrefab != null)
            SpawnShip(candidate.center, generatedRoot);

        if (repositionPlayer)
        {
            RepositionPlayer(candidate);
        }

        return true;
    }

    public bool ClearGeneratedStartArea()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
            hasGeneratedStartArea = false;
            return false;
        }

        ClearGeneratedStartAreaContents();
        if (Application.isPlaying)
        {
            Destroy(generatedRoot.gameObject);
        }
        else
        {
            DestroyImmediate(generatedRoot.gameObject);
        }

        hasGeneratedStartArea = false;
        return true;
    }

    public Transform GetGeneratedRoot()
    {
        return transform.Find(string.IsNullOrWhiteSpace(generatedRootName) ? DefaultGeneratedRootName : generatedRootName);
    }

    private bool ResolveDependencies()
    {
        voxelTerrain ??= GetComponent<ProceduralVoxelTerrain>();
        waterSystem ??= GetComponent<ProceduralVoxelTerrainWaterSystem>();
        scatterer ??= GetComponent<ProceduralVoxelTerrainScatterer>();

        if (voxelTerrain == null)
        {
            Debug.LogWarning($"{gameObject.name} requires a {nameof(ProceduralVoxelTerrain)} to generate a starting area.");
            return false;
        }

        if (generateTerrainBeforeStartArea && !voxelTerrain.HasReadyGameplayTerrain)
        {
            if (!voxelTerrain.IsTerrainGenerationInProgress)
            {
                if (voxelTerrain.IsRuntimeStreamingModeActive)
                {
                    voxelTerrain.GenerateTerrainWithConfiguredMode(voxelTerrain.ClearExistingBeforeGenerate);
                }
                else
                {
                    voxelTerrain.GenerateTerrain(voxelTerrain.ClearExistingBeforeGenerate);
                }
            }
        }

        if (!voxelTerrain.HasReadyGameplayTerrain)
        {
            Debug.LogWarning($"{gameObject.name} could not generate a starting area because voxel terrain is not available.");
            return false;
        }

        if (waterSystem != null && generateWaterBeforeStartArea && !HasGeneratedChildren(waterSystem.GetGeneratedRoot()))
        {
            waterSystem.GenerateWater(waterSystem.ClearExistingBeforeGenerate);
        }

        if (scatterer != null && generateScatterBeforeStartArea && !HasGeneratedChildren(scatterer.GetGeneratedRoot()))
        {
            scatterer.GenerateScatter(scatterer.ClearExistingBeforeGenerate);
        }

        return true;
    }

    private void SpawnShip(Vector3 clearingCenter, Transform parent)
    {
        Vector3 spawnPos = clearingCenter + shipCenterOffset;

        // Try to land the ship on the terrain surface
        if (Physics.Raycast(spawnPos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f))
            spawnPos.y = hit.point.y;

        // Face the ship roughly toward the clearing center so the entrance looks at the player spawn
        Vector3 toCenter = Vector3.ProjectOnPlane(clearingCenter - spawnPos, Vector3.up);
        Quaternion rot = toCenter.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(toCenter.normalized, Vector3.up)
            : Quaternion.identity;

        Instantiate(shipPrefab, spawnPos, rot, parent);
    }

    private void RepositionPlayer(StartAreaCandidate candidate)
    {
        Transform resolvedPlayerRoot = ResolvePlayerRoot();
        if (resolvedPlayerRoot == null)
        {
            Debug.LogWarning($"{gameObject.name} could not find a player object to reposition. Assign Player Root if auto-detection is not enough.");
            return;
        }

        CharacterController controller = resolvedPlayerRoot.GetComponent<CharacterController>();
        Rigidbody body = resolvedPlayerRoot.GetComponent<Rigidbody>();

        Vector3 spawnPosition = candidate.center + Vector3.up * 0.05f;
        if (controller != null)
        {
            spawnPosition += Vector3.up * ((controller.height * 0.5f) - controller.center.y + controller.skinWidth + 0.05f);
            controller.enabled = false;
        }

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        resolvedPlayerRoot.position = spawnPosition;

        Vector3 lookDirection = Vector3.ProjectOnPlane(candidate.nearestFreshwaterPoint - candidate.center, Vector3.up);
        if (lookDirection.sqrMagnitude > 0.001f)
        {
            resolvedPlayerRoot.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }

        if (controller != null)
        {
            controller.enabled = true;
        }

        lastSpawnPosition = spawnPosition;
    }

    private Transform ResolvePlayerRoot()
    {
        if (playerRoot != null)
        {
            return playerRoot;
        }

        FirstPersonController firstPersonController = FindAnyObjectByType<FirstPersonController>();
        if (firstPersonController != null)
        {
            playerRoot = firstPersonController.transform;
            return playerRoot;
        }

        PlayerInteraction interaction = FindAnyObjectByType<PlayerInteraction>();
        if (interaction != null)
        {
            playerRoot = interaction.transform;
            return playerRoot;
        }

        CharacterController characterController = FindAnyObjectByType<CharacterController>();
        if (characterController != null)
        {
            playerRoot = characterController.transform;
            return playerRoot;
        }

        return null;
    }

    private Transform EnsureGeneratedRoot()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot != null)
        {
            return generatedRoot;
        }

        GameObject rootObject = new GameObject(generatedRootName);
        rootObject.layer = gameObject.layer;
        rootObject.transform.SetParent(transform, false);
        rootObject.transform.localPosition = Vector3.zero;
        rootObject.transform.localRotation = Quaternion.identity;
        rootObject.transform.localScale = Vector3.one;
        return rootObject.transform;
    }

    private void ClearGeneratedStartAreaContents()
    {
        hasGeneratedStartArea = false;
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
            return;
        }

        List<GameObject> children = new List<GameObject>();
        foreach (Transform child in generatedRoot)
        {
            if (child != null)
            {
                children.Add(child.gameObject);
            }
        }

        for (int i = 0; i < children.Count; i++)
        {
            if (Application.isPlaying)
            {
                children[i].SetActive(false);
                Destroy(children[i]);
            }
            else
            {
                DestroyImmediate(children[i]);
            }
        }
    }

    private static bool HasGeneratedChildren(Transform root) => VoxelBrushUtility.HasGeneratedChildren(root);

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !hasGeneratedStartArea)
        {
            return;
        }

        Gizmos.color = clearingGizmoColor;
        Gizmos.DrawWireSphere(lastClearingCenter, clearingRadiusMeters);

        Gizmos.color = spawnGizmoColor;
        Gizmos.DrawSphere(lastSpawnPosition, 0.35f);

        if ((lastSuggestedLookTarget - lastClearingCenter).sqrMagnitude > 0.01f)
        {
            Gizmos.DrawLine(lastClearingCenter + Vector3.up * 0.35f, lastSuggestedLookTarget + Vector3.up * 0.35f);
        }
    }
}
