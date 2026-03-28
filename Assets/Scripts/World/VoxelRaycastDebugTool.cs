using System;
using UnityEngine;

[DisallowMultipleComponent]
public class VoxelRaycastDebugTool : MonoBehaviour
{
    private const int PaletteWindowId = 731042;

    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private ProceduralVoxelTerrain voxelTerrain;
    [SerializeField] private ProceduralVoxelTerrainWaterSystem voxelWaterSystem;

    [Header("Palette")]
    [SerializeField] private bool toolEnabled = true;
    [SerializeField] private bool showPalette = true;
    [SerializeField] private bool showOverlay = true;
    [SerializeField] private bool paletteCollapsed;
    [SerializeField, Min(1f)] private float raycastRange = 12f;
    [SerializeField] private LayerMask raycastLayers = -1;

    [Header("Terrain Add")]
    [SerializeField, Min(0.25f)] private float terrainBrushRadiusMeters = 1.6f;
    [SerializeField, Min(0.05f)] private float terrainDensityDeltaMeters = 1.6f;
    [SerializeField, Min(0f)] private float terrainBrushSurfaceOffsetMeters = 0.35f;
    [SerializeField] private string terrainMaterialName = "Topsoil";
    [SerializeField, Min(0.05f)] private float terrainMaterialVerticalHalfExtentMeters = 1.25f;

    [Header("Water Add")]
    [SerializeField, Min(100f)] private float waterAddMassGrams = 5000000f;
    [SerializeField, Min(0f)] private float waterTargetPaddingMeters = 1.25f;

    [Header("Single Lake")]
    [SerializeField, Min(1f)] private float singleLakeRadiusMeters = 8f;

    [Header("Merge Test Pair")]
    [SerializeField, Min(1f)] private float mergePairLakeRadiusMeters = 8f;
    [SerializeField, Min(0.25f)] private float mergePairGapMeters = 3f;
    [SerializeField, Min(0.1f)] private float mergePairRidgeHeightMeters = 1.8f;

    [Header("Debug")]
    [SerializeField] private bool logActions = true;
    [SerializeField] private bool drawDebugRay = true;

    private string cachedTerrainMaterialName = string.Empty;
    private int cachedTerrainMaterialIndex = -1;
    private string overlaySummary = string.Empty;
    private string lastActionMessage = "Ready.";
    private Vector2 overlayScroll;
    private Rect paletteRect;
    private GUIStyle wrappedLabelStyle;

    private void Awake()
    {
        paletteRect = new Rect(12f, 12f, 430f, 310f);
    }

    private void Start()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (!toolEnabled)
        {
            return;
        }

        ResolveReferences();
        UpdateOverlaySummary(showPalette || showOverlay);
    }

    private void ResolveReferences()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        if (voxelTerrain == null)
        {
            voxelTerrain = FindAnyObjectByType<ProceduralVoxelTerrain>();
        }

        if (voxelWaterSystem == null)
        {
            voxelWaterSystem = FindAnyObjectByType<ProceduralVoxelTerrainWaterSystem>();
        }
    }

    private void TryAddTerrain()
    {
        if (voxelTerrain == null || !voxelTerrain.HasGeneratedTerrain)
        {
            ReportAction("Add terrain skipped because voxel terrain is unavailable.");
            return;
        }

        if (!TryGetSortedHits(out RaycastHit[] hits) || !TryResolveTerrainHit(hits, out RaycastHit terrainHit))
        {
            ReportAction("Add terrain skipped because no terrain surface was found under the raycast.");
            return;
        }

        Vector3 surfaceNormal = terrainHit.normal.sqrMagnitude > 0.0001f ? terrainHit.normal.normalized : Vector3.up;
        Vector3 brushPoint = terrainHit.point + (surfaceNormal * terrainBrushSurfaceOffsetMeters);
        bool densityApplied = voxelTerrain.ApplyDensityBrushWorld(brushPoint, terrainBrushRadiusMeters, terrainDensityDeltaMeters);
        bool materialApplied = false;
        int materialIndex = ResolveTerrainMaterialIndex();
        if (densityApplied && materialIndex >= 0)
        {
            materialApplied = voxelTerrain.ApplyMaterialBrushWorld(
                brushPoint,
                terrainBrushRadiusMeters,
                terrainMaterialVerticalHalfExtentMeters,
                materialIndex,
                false);
        }

        ReportAction(
            densityApplied
                ? $"Added terrain at {brushPoint}. Material applied={materialApplied}."
                : $"Add terrain made no changes at {brushPoint}.");
    }

    private void TryAddWater()
    {
        if (voxelWaterSystem == null)
        {
            ReportAction("Add water skipped because voxel water is unavailable.");
            return;
        }

        if (!TryGetSortedHits(out RaycastHit[] hits))
        {
            ReportAction("Add water skipped because the raycast did not hit anything.");
            return;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            if (!voxelWaterSystem.TryAddWaterFromRaycast(hits[i], waterAddMassGrams, waterTargetPaddingMeters))
            {
                continue;
            }

            ReportAction($"Added {waterAddMassGrams:F0} g of water from raycast point {hits[i].point}.");
            return;
        }

        ReportAction("Add water skipped because the raycast was not near an active lake.");
    }

    private void TryCreateSingleLake()
    {
        if (voxelWaterSystem == null)
        {
            ReportAction("Single lake spawn skipped because voxel water is unavailable.");
            return;
        }

        if (!TryGetInteractionPoint(out Vector3 worldPoint))
        {
            ReportAction("Single lake spawn skipped because the raycast did not hit anything.");
            return;
        }

        if (!voxelWaterSystem.TryCreateDebugLakeAtPoint(worldPoint, singleLakeRadiusMeters))
        {
            ReportAction("Single lake spawn failed.");
            return;
        }

        ReportAction($"Created debug lake at {worldPoint}.");
    }

    private void TryCreateMergePair()
    {
        if (voxelWaterSystem == null)
        {
            ReportAction("Merge-test lake pair skipped because voxel water is unavailable.");
            return;
        }

        if (!TryGetInteractionPoint(out Vector3 worldPoint))
        {
            ReportAction("Merge-test lake pair skipped because the raycast did not hit anything.");
            return;
        }

        Vector3 lateralDirection = playerCamera != null
            ? Vector3.ProjectOnPlane(playerCamera.transform.right, Vector3.up)
            : Vector3.right;
        if (!voxelWaterSystem.TryCreateMergeTestLakePairAtPoint(
                worldPoint,
                lateralDirection,
                mergePairLakeRadiusMeters,
                mergePairGapMeters,
                mergePairRidgeHeightMeters))
        {
            ReportAction("Merge-test lake pair creation failed.");
            return;
        }

        ReportAction("Created merge-test lake pair.");
    }

    private void LogTargetLakeSummary()
    {
        if (voxelWaterSystem == null)
        {
            ReportAction("Lake summary unavailable because voxel water is unavailable.");
            return;
        }

        if (!TryGetPrimaryRaycastPoint(out Vector3 worldPoint))
        {
            ReportAction("Lake summary unavailable because the raycast did not hit anything.");
            return;
        }

        ReportAction(voxelWaterSystem.GetLakeDebugSummaryAtPoint(worldPoint, waterTargetPaddingMeters));
    }

    private void LogAllLakesSummary()
    {
        if (voxelWaterSystem == null)
        {
            ReportAction("All-lakes summary unavailable because voxel water is unavailable.");
            return;
        }

        ReportAction(voxelWaterSystem.GetAllLakeDebugSummary());
    }

    private void UpdateOverlaySummary(bool overlayEnabled)
    {
        if (!overlayEnabled)
        {
            overlaySummary = string.Empty;
            return;
        }

        if (!TryGetPrimaryRaycastPoint(out Vector3 worldPoint))
        {
            overlaySummary = "Center-screen raycast: no hit.";
            return;
        }

        string raycastSummary = $"Raycast point: {FormatVector(worldPoint)}";
        if (voxelWaterSystem == null)
        {
            overlaySummary = raycastSummary + "\nVoxel water unavailable.";
            return;
        }

        overlaySummary = raycastSummary + "\n" + voxelWaterSystem.GetLakeDebugSummaryAtPoint(worldPoint, waterTargetPaddingMeters);
    }

    private bool TryGetSortedHits(out RaycastHit[] hits)
    {
        hits = Array.Empty<RaycastHit>();
        if (playerCamera == null)
        {
            return false;
        }

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        hits = Physics.RaycastAll(ray, raycastRange, raycastLayers, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
        {
            hits = Array.Empty<RaycastHit>();
            return false;
        }

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        return true;
    }

    private bool TryGetPrimaryRaycastPoint(out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        if (!TryGetSortedHits(out RaycastHit[] hits) || hits.Length == 0)
        {
            return false;
        }

        worldPoint = hits[0].point;
        return true;
    }

    private bool TryGetInteractionPoint(out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        if (!TryGetSortedHits(out RaycastHit[] hits) || hits.Length == 0)
        {
            return false;
        }

        if (TryResolveTerrainHit(hits, out RaycastHit terrainHit))
        {
            worldPoint = terrainHit.point;
            return true;
        }

        worldPoint = hits[0].point;
        return true;
    }

    private bool TryResolveTerrainHit(RaycastHit[] hits, out RaycastHit terrainHit)
    {
        terrainHit = default;
        if (hits == null || hits.Length == 0 || voxelTerrain == null)
        {
            return false;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            if (IsVoxelTerrainHit(hits[i]))
            {
                terrainHit = hits[i];
                return true;
            }
        }

        for (int i = 0; i < hits.Length; i++)
        {
            if (voxelTerrain.TrySampleSurfaceWorld(hits[i].point.x, hits[i].point.z, out terrainHit))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsVoxelTerrainHit(RaycastHit hit)
    {
        return voxelTerrain != null &&
               hit.collider != null &&
               hit.collider.GetComponentInParent<ProceduralVoxelTerrain>() == voxelTerrain;
    }

    private int ResolveTerrainMaterialIndex()
    {
        if (voxelTerrain == null || string.IsNullOrWhiteSpace(terrainMaterialName))
        {
            return -1;
        }

        if (string.Equals(cachedTerrainMaterialName, terrainMaterialName, StringComparison.Ordinal))
        {
            return cachedTerrainMaterialIndex;
        }

        cachedTerrainMaterialName = terrainMaterialName;
        cachedTerrainMaterialIndex = voxelTerrain.FindMaterialIndex(terrainMaterialName);
        return cachedTerrainMaterialIndex;
    }

    private void ReportAction(string message)
    {
        lastActionMessage = message;
        if (!logActions)
        {
            return;
        }

        Debug.Log($"[{nameof(VoxelRaycastDebugTool)}:{name}] {message}", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugRay)
        {
            return;
        }

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        if (playerCamera == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(playerCamera.transform.position, playerCamera.transform.forward * raycastRange);
    }

    private void OnGUI()
    {
        if (!toolEnabled || !showPalette)
        {
            return;
        }

        EnsureStyles();
        paletteRect.width = 430f;
        paletteRect.height = paletteCollapsed ? 56f : 310f;
        paletteRect = GUI.Window(PaletteWindowId, paletteRect, DrawPaletteWindow, "Voxel Debug Palette");
    }

    private void DrawPaletteWindow(int windowId)
    {
        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(paletteCollapsed ? "Expand" : "Collapse", GUILayout.Width(80f)))
        {
            paletteCollapsed = !paletteCollapsed;
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label("Uses the center-screen raycast.", wrappedLabelStyle, GUILayout.MaxWidth(220f));
        GUILayout.EndHorizontal();

        if (!paletteCollapsed)
        {
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Add Terrain ({terrainBrushRadiusMeters:F1}m)"))
            {
                TryAddTerrain();
            }

            if (GUILayout.Button($"Add Water ({waterAddMassGrams:F0}g)"))
            {
                TryAddWater();
            }

            if (GUILayout.Button($"Spawn Lake ({singleLakeRadiusMeters:F1}m)"))
            {
                TryCreateSingleLake();
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Spawn Pair ({mergePairLakeRadiusMeters:F1}m)"))
            {
                TryCreateMergePair();
            }

            if (GUILayout.Button("Target Lake"))
            {
                LogTargetLakeSummary();
            }

            if (GUILayout.Button("All Lakes"))
            {
                LogAllLakesSummary();
            }

            GUILayout.EndHorizontal();

            if (showOverlay)
            {
                GUILayout.Space(8f);
                GUILayout.Label("Current Target", wrappedLabelStyle);
                GUILayout.BeginVertical(GUI.skin.box);
                overlayScroll = GUILayout.BeginScrollView(overlayScroll, GUILayout.Height(120f));
                GUILayout.Label(string.IsNullOrWhiteSpace(overlaySummary) ? "No target summary." : overlaySummary, wrappedLabelStyle);
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }

            GUILayout.Space(6f);
            GUILayout.Label("Last Action", wrappedLabelStyle);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(string.IsNullOrWhiteSpace(lastActionMessage) ? "Ready." : lastActionMessage, wrappedLabelStyle);
            GUILayout.EndVertical();
        }

        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }

    private void EnsureStyles()
    {
        if (wrappedLabelStyle != null)
        {
            return;
        }

        wrappedLabelStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
    }
}
