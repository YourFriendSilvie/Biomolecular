using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages player-initiated building placement.
///
/// Controls:
///   B            — toggle build mode (top-down camera, cursor unlocked)
///   WASD         — pan the top-down camera
///   Scroll       — zoom the top-down camera
///   LMB          — place selected building (when ghost is green)
///   RMB          — cancel current selection (stay in build mode)
///   R            — rotate ghost 90°
///   Escape / B   — exit build mode
///
/// Setup:
///   1. Add BuildingSystem + BuildModeController to a Manager GameObject.
///   2. Assign mainCamera, buildModeController, catalog, playerTransform,
///      characterLocomotion, and placementLayerMask in the Inspector.
///   3. Populate BuildingCatalog with PlaceableBuilding prefabs.
///   4. Call SelectBuilding(name) from UI buttons to start placing.
/// </summary>
public class BuildingSystem : MonoBehaviour
{
    public static BuildingSystem Instance { get; private set; }

    [Header("References")]
    [SerializeField] private BuildModeController buildModeController;
    [SerializeField] private BuildingCatalog     catalog;
    [SerializeField] private Camera              mainCamera;
    [SerializeField] private Transform           playerTransform;
    [SerializeField] private FirstPersonController characterController;
    [SerializeField] private Inventory           playerInventory;

    [Header("Placement")]
    [SerializeField] private LayerMask placementLayerMask = ~0;
    [SerializeField, Min(0.25f)]
    [Tooltip("World-unit grid cell size for snapping.")]
    private float gridSnapSize = 1f;

    [Header("Controls")]
    [SerializeField] private Key toggleBuildModeKey = Key.B;
    [SerializeField] private Key rotateKey          = Key.R;
    [SerializeField] private Key cancelKey          = Key.Escape;

    // ── State ─────────────────────────────────────────────────────────────────
    public bool InBuildMode { get; private set; }
    public bool IsPlacing   => _ghost != null;

    BuildingGhost     _ghost;
    PlaceableBuilding _selectedPrefab;
    float             _ghostRotationY;

    readonly List<GameObject> _placedBuildings = new();

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<bool>                              BuildModeChanged;
    public event Action<PlaceableBuilding, Vector3>        BuildingPlaced;
    /// <summary>Fired when placement is blocked (e.g. not enough materials). Passes reason string.</summary>
    public event Action<string>                            PlacementFailed;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (Keyboard.current[toggleBuildModeKey].wasPressedThisFrame)
        {
            if (InBuildMode) ExitBuildMode();
            else             EnterBuildMode();
            return;
        }

        if (!InBuildMode) return;

        HandlePlacementInput();
        UpdateGhostPose();
    }

    // ── Build mode ────────────────────────────────────────────────────────────

    public void EnterBuildMode()
    {
        if (InBuildMode) return;
        InBuildMode = true;

        if (characterController != null)
            characterController.enabled = false;

        Vector3 focus = playerTransform != null ? playerTransform.position : Vector3.zero;
        buildModeController?.Enter(focus);

        BuildModeChanged?.Invoke(true);
    }

    public void ExitBuildMode()
    {
        if (!InBuildMode) return;
        CancelPlacement();
        InBuildMode = false;

        buildModeController?.Exit();

        if (characterController != null)
            characterController.enabled = true;

        BuildModeChanged?.Invoke(false);
    }

    // ── Building selection ────────────────────────────────────────────────────

    /// <summary>Start placing a specific prefab by direct reference.</summary>
    public void SelectBuilding(PlaceableBuilding prefab)
    {
        if (prefab == null) return;
        if (!InBuildMode) EnterBuildMode();
        CancelPlacement();

        _selectedPrefab = prefab;
        _ghostRotationY = 0f;

        // Instantiate a ghost clone
        var ghostGo = Instantiate(prefab.gameObject);
        ghostGo.name = $"Ghost_{prefab.buildingName}";

        // Disable all functional MonoBehaviours on the ghost except BuildingGhost
        foreach (var mb in ghostGo.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            if (mb is not BuildingGhost)
                mb.enabled = false;

        // Disable colliders so ghost doesn't interfere with placement raycasts
        foreach (var col in ghostGo.GetComponentsInChildren<Collider>(includeInactive: true))
            col.enabled = false;

        _ghost = ghostGo.GetComponent<BuildingGhost>();
        if (_ghost == null) _ghost = ghostGo.AddComponent<BuildingGhost>();
    }

    /// <summary>Start placing a building by catalog name.</summary>
    public void SelectBuilding(string buildingName)
    {
        var entry = catalog?.FindByName(buildingName);
        if (entry?.prefab != null) SelectBuilding(entry.prefab);
    }

    // ── Placement input ───────────────────────────────────────────────────────

    void HandlePlacementInput()
    {
        if (_ghost == null) return;

        if (Keyboard.current[rotateKey].wasPressedThisFrame)
            _ghostRotationY = (_ghostRotationY + 90f) % 360f;

        if (Keyboard.current[cancelKey].wasPressedThisFrame)
        {
            ExitBuildMode();
            return;
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelPlacement();
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame && _ghost.IsValid)
            ConfirmPlacement();
    }

    // ── Ghost positioning ─────────────────────────────────────────────────────

    void UpdateGhostPose()
    {
        if (_ghost == null) return;

        // Use the top-down camera during build mode, fall back to main camera
        Camera cam = buildModeController?.TopDownCamera ?? mainCamera;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, placementLayerMask))
        {
            _ghost.SetValid(false);
            return;
        }

        Vector3 snapped = SnapToGrid(hit.point);
        bool overlapping = CheckOverlap(snapped);
        _ghost.SetValid(!overlapping);
        _ghost.UpdatePose(snapped, Quaternion.Euler(0f, _ghostRotationY, 0f));
    }

    // ── Confirm / cancel ──────────────────────────────────────────────────────

    public void ConfirmPlacement()
    {
        if (_ghost == null || !_ghost.IsValid || _selectedPrefab == null) return;

        // ── Build cost check ──────────────────────────────────────────────────
        if (playerInventory != null && _selectedPrefab.buildCost != null)
        {
            string missing = GetMissingCostDescription(_selectedPrefab);
            if (missing != null)
            {
                PlacementFailed?.Invoke($"Not enough materials: {missing}");
                Debug.Log($"[BuildingSystem] Cannot place {_selectedPrefab.buildingName}: {missing}");
                return;
            }
            ConsumeBuildCost(_selectedPrefab);
        }

        Vector3    pos = _ghost.transform.position;
        Quaternion rot = _ghost.transform.rotation;

        GameObject placed = Instantiate(_selectedPrefab.gameObject, pos, rot);
        placed.name = _selectedPrefab.buildingName;
        _placedBuildings.Add(placed);

        BuildingPlaced?.Invoke(_selectedPrefab, pos);

        // Re-select the same type so the player can keep placing
        SelectBuilding(_selectedPrefab);
    }

    public void CancelPlacement()
    {
        if (_ghost != null)
        {
            Destroy(_ghost.gameObject);
            _ghost = null;
        }
        _selectedPrefab = null;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    Vector3 SnapToGrid(Vector3 worldPos)
        => new(
            Mathf.Round(worldPos.x / gridSnapSize) * gridSnapSize,
            worldPos.y,
            Mathf.Round(worldPos.z / gridSnapSize) * gridSnapSize);

    bool CheckOverlap(Vector3 pos)
    {
        if (_selectedPrefab == null) return false;

        float halfW = _selectedPrefab.footprintTiles.x * gridSnapSize * 0.5f - 0.05f;
        float halfD = _selectedPrefab.footprintTiles.y * gridSnapSize * 0.5f - 0.05f;

        Collider[] hits = Physics.OverlapBox(
            pos + Vector3.up,
            new Vector3(halfW, 1f, halfD),
            Quaternion.Euler(0f, _ghostRotationY, 0f),
            ~0,
            QueryTriggerInteraction.Ignore);

        foreach (var h in hits)
        {
            if (_ghost != null && h.transform.IsChildOf(_ghost.transform)) continue;
            // Check root and all parents — PlaceableBuilding lives on the prefab root,
            // but the hit collider may be on a child (e.g. the Body primitive).
            if (h.GetComponentInParent<PlaceableBuilding>() != null) return true;
        }
        return false;
    }

    // ── Build cost helpers ────────────────────────────────────────────────────

    /// <summary>Returns a human-readable description of what's missing, or null if all costs met.</summary>
    string GetMissingCostDescription(PlaceableBuilding prefab)
    {
        if (playerInventory == null || prefab.buildCost == null) return null;
        foreach (var cost in prefab.buildCost)
        {
            if (cost.massGrams <= 0f) continue;
            float have = playerInventory.GetTotalResourceAmount(cost.molecule);
            if (have < cost.massGrams)
                return $"{cost.molecule} ({have:F0}g / {cost.massGrams:F0}g)";
        }
        return null;
    }

    /// <summary>Returns formatted cost string for UI display.</summary>
    public string GetCostDescription(PlaceableBuilding prefab)
    {
        if (prefab?.buildCost == null || prefab.buildCost.Count == 0) return "Free";
        var parts = new System.Text.StringBuilder();
        foreach (var cost in prefab.buildCost)
        {
            if (cost.massGrams <= 0f) continue;
            float have  = playerInventory != null ? playerInventory.GetTotalResourceAmount(cost.molecule) : 0f;
            bool  canAff = have >= cost.massGrams;
            parts.Append(canAff ? "✓ " : "✗ ");
            parts.Append($"{cost.molecule} {cost.massGrams:F0}g\n");
        }
        return parts.ToString().TrimEnd();
    }

    void ConsumeBuildCost(PlaceableBuilding prefab)
    {
        if (playerInventory == null || prefab.buildCost == null) return;
        foreach (var cost in prefab.buildCost)
        {
            float remaining = cost.massGrams;
            foreach (var item in playerInventory.Items)
            {
                if (remaining <= 0f) break;
                float available = item.GetResourceAmount(cost.molecule) * item.totalMass;
                if (available <= 0f) continue;
                float toConsume = Mathf.Min(available, remaining);
                item.TryExtractResource(cost.molecule, toConsume);
                remaining -= toConsume;
            }
        }
    }
}
