using System;
using System.Collections.Generic;
using UnityEngine;

public class ProceduralTreeScatterVolume : MonoBehaviour
{
    [Header("Template")]
    [SerializeField] private ProceduralTreeGenerator treeTemplate;

    [Header("Scatter Volume")]
    [SerializeField] private Vector3 volumeSizeMeters = new Vector3(50f, 30f, 50f);
    [SerializeField, Min(1)] private int treeCount = 25;
    [SerializeField, Min(1)] private int maxPlacementAttemptsPerTree = 12;
    [SerializeField, Min(0f)] private float minimumSpacingMeters = 3f;

    [Header("Placement")]
    [SerializeField] private bool projectToGround = true;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField, Min(0.1f)] private float groundProbeDistance = 200f;
    [SerializeField] private bool alignToSurfaceNormal = false;
    [SerializeField] private bool randomizeYaw = true;
    [SerializeField] private Vector2 uniformScaleRange = Vector2.one;

    [Header("Generation")]
    [SerializeField] private bool clearExistingBeforeGenerate = true;
    [SerializeField] private string generatedRootName = "Generated Trees";
    [SerializeField] private int randomSeed = 24680;
    [SerializeField] private bool randomizeSeed = true;

    [Header("Editor Gizmo")]
    [SerializeField] private Color gizmoColor = new Color(0.16f, 0.8f, 0.32f, 0.2f);

    public Vector3 VolumeSize
    {
        get => volumeSizeMeters;
        set => volumeSizeMeters = SanitizeVolumeSize(value);
    }

    public bool ClearExistingBeforeGenerate => clearExistingBeforeGenerate;

    private void OnValidate()
    {
        volumeSizeMeters = SanitizeVolumeSize(volumeSizeMeters);
        treeCount = Mathf.Max(1, treeCount);
        maxPlacementAttemptsPerTree = Mathf.Max(1, maxPlacementAttemptsPerTree);
        minimumSpacingMeters = Mathf.Max(0f, minimumSpacingMeters);
        groundProbeDistance = Mathf.Max(0.1f, groundProbeDistance);
        uniformScaleRange = SanitizeScaleRange(uniformScaleRange);
        generatedRootName = string.IsNullOrWhiteSpace(generatedRootName) ? "Generated Trees" : generatedRootName.Trim();
    }

    [ContextMenu("Generate Trees")]
    public void GenerateTreesFromContextMenu()
    {
        GenerateTrees(clearExistingBeforeGenerate);
    }

    [ContextMenu("Clear Generated Trees")]
    public void ClearGeneratedTreesFromContextMenu()
    {
        ClearGeneratedTrees();
    }

    public List<GameObject> GenerateTrees(bool clearExisting)
    {
        List<GameObject> createdTrees = new List<GameObject>();

        if (!IsTemplateValid())
        {
            return createdTrees;
        }

        if (clearExisting)
        {
            ClearGeneratedTrees();
        }

        Transform generatedRoot = EnsureGeneratedRoot();
        System.Random random = new System.Random(randomizeSeed ? Guid.NewGuid().GetHashCode() : randomSeed);
        List<Vector3> placedPositions = new List<Vector3>(treeCount);
        int maxAttempts = treeCount * Mathf.Max(1, maxPlacementAttemptsPerTree);
        int placedCount = 0;
        int attempts = 0;

        while (placedCount < treeCount && attempts < maxAttempts)
        {
            attempts++;
            if (!TryGetPlacement(random, out Vector3 position, out Vector3 surfaceNormal))
            {
                continue;
            }

            if (!IsFarEnoughFromExisting(position, placedPositions))
            {
                continue;
            }

            GameObject createdTree = Instantiate(treeTemplate.gameObject, position, Quaternion.identity, generatedRoot);
            createdTree.name = $"{treeTemplate.gameObject.name}_{placedCount + 1:000}";
            createdTree.transform.rotation = BuildTreeRotation(random, surfaceNormal) * treeTemplate.transform.rotation;

            float scaleMultiplier = NextFloat(random, uniformScaleRange.x, uniformScaleRange.y);
            createdTree.transform.localScale = treeTemplate.transform.localScale * scaleMultiplier;

            ProceduralTreeGenerator createdGenerator = createdTree.GetComponent<ProceduralTreeGenerator>();
            if (createdGenerator != null)
            {
                createdGenerator.GenerateTree();
            }

            createdTrees.Add(createdTree);
            placedPositions.Add(position);
            placedCount++;
        }

        if (createdTrees.Count < treeCount)
        {
            Debug.LogWarning(
                $"{gameObject.name} only placed {createdTrees.Count} of {treeCount} requested trees. " +
                "Try increasing the volume, reducing spacing, or increasing placement attempts.");
        }

        return createdTrees;
    }

    public bool ClearGeneratedTrees()
    {
        Transform generatedRoot = GetGeneratedRoot();
        if (generatedRoot == null)
        {
            return false;
        }

        if (Application.isPlaying)
        {
            Destroy(generatedRoot.gameObject);
        }
        else
        {
            DestroyImmediate(generatedRoot.gameObject);
        }

        return true;
    }

    public Transform GetGeneratedRoot()
    {
        return transform.Find(string.IsNullOrWhiteSpace(generatedRootName) ? "Generated Trees" : generatedRootName);
    }

    private bool IsTemplateValid()
    {
        if (treeTemplate == null)
        {
            Debug.LogWarning($"{gameObject.name} cannot scatter trees because no tree template is assigned.");
            return false;
        }

        if (treeTemplate.gameObject == gameObject)
        {
            Debug.LogWarning($"{gameObject.name} cannot use itself as the tree template.");
            return false;
        }

        if (treeTemplate.GetComponent<ProceduralTreeScatterVolume>() != null)
        {
            Debug.LogWarning($"{treeTemplate.gameObject.name} cannot be used as a tree template because it also has a ProceduralTreeScatterVolume.");
            return false;
        }

        return true;
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

    private bool TryGetPlacement(System.Random random, out Vector3 position, out Vector3 surfaceNormal)
    {
        Vector3 extents = volumeSizeMeters * 0.5f;
        Vector3 localSample = new Vector3(
            NextFloat(random, -extents.x, extents.x),
            projectToGround ? extents.y : NextFloat(random, -extents.y, extents.y),
            NextFloat(random, -extents.z, extents.z));

        if (projectToGround)
        {
            Vector3 rayOrigin = transform.TransformPoint(localSample);
            Vector3 rayDirection = -transform.up;

            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, groundProbeDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                position = hit.point;
                surfaceNormal = hit.normal.sqrMagnitude < 0.0001f ? Vector3.up : hit.normal.normalized;
                return true;
            }

            position = Vector3.zero;
            surfaceNormal = Vector3.up;
            return false;
        }

        position = transform.TransformPoint(localSample);
        surfaceNormal = Vector3.up;
        return true;
    }

    private bool IsFarEnoughFromExisting(Vector3 position, IReadOnlyList<Vector3> existingPositions)
    {
        if (minimumSpacingMeters <= 0f || existingPositions == null || existingPositions.Count == 0)
        {
            return true;
        }

        float minimumSpacingSquared = minimumSpacingMeters * minimumSpacingMeters;
        for (int i = 0; i < existingPositions.Count; i++)
        {
            Vector3 planarDelta = Vector3.ProjectOnPlane(position - existingPositions[i], Vector3.up);
            if (planarDelta.sqrMagnitude < minimumSpacingSquared)
            {
                return false;
            }
        }

        return true;
    }

    private Quaternion BuildTreeRotation(System.Random random, Vector3 surfaceNormal)
    {
        Vector3 upAxis = alignToSurfaceNormal ? surfaceNormal : Vector3.up;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, upAxis);
        if (randomizeYaw)
        {
            rotation = Quaternion.AngleAxis(NextFloat(random, 0f, 360f), upAxis) * rotation;
        }

        return rotation;
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(Vector3.zero, volumeSizeMeters);
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        Gizmos.DrawWireCube(Vector3.zero, volumeSizeMeters);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private static Vector3 SanitizeVolumeSize(Vector3 size)
    {
        return new Vector3(
            Mathf.Max(0.5f, Mathf.Abs(size.x)),
            Mathf.Max(0.5f, Mathf.Abs(size.y)),
            Mathf.Max(0.5f, Mathf.Abs(size.z)));
    }

    private static Vector2 SanitizeScaleRange(Vector2 range)
    {
        float min = Mathf.Max(0.01f, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return new Vector2(min, max);
    }

    private static float NextFloat(System.Random random, float minInclusive, float maxInclusive)
    {
        if (Mathf.Approximately(minInclusive, maxInclusive))
        {
            return minInclusive;
        }

        float min = Mathf.Min(minInclusive, maxInclusive);
        float max = Mathf.Max(minInclusive, maxInclusive);
        return (float)(min + (random.NextDouble() * (max - min)));
    }
}
