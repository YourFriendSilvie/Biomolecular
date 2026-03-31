using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One resource molecule + mass required to build this machine.
/// Costs are checked against the player's Inventory at placement time.
/// </summary>
[Serializable]
public class BuildCostEntry
{
    [Tooltip("Molecule name as it appears in CompositionInfo (e.g. 'Iron', 'Copper').")]
    public string molecule;

    [Tooltip("Mass in grams required (e.g. 1000 = 1 kg).")]
    [Min(0f)]
    public float massGrams;
}

/// <summary>
/// Marks a prefab as placeable by the BuildingSystem.
/// Stores metadata shown in the build catalog and ghost renderer.
/// Add this component to any GameObject prefab you want players to build.
/// </summary>
public class PlaceableBuilding : MonoBehaviour
{
    [Header("Identity")]
    public string buildingName = "Building";
    public string category     = "Machines";

    [TextArea(2, 4)]
    public string description = "";

    [Header("Footprint")]
    [Tooltip("Width (X) and depth (Z) in grid cells. Used for overlap detection.")]
    public Vector2Int footprintTiles = Vector2Int.one;

    [Header("Power")]
    [Tooltip("Watts drawn when active. Informational — drives build-menu display.")]
    public float requiredWatts = 0f;

    [Header("Build Cost")]
    [Tooltip("Molecules consumed from player inventory when this machine is placed.")]
    public List<BuildCostEntry> buildCost = new();
}
