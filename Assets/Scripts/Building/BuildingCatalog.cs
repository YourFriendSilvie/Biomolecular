using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject listing every player-buildable structure.
/// Assign to BuildingSystem in the Inspector.
/// Populate entries via Biomolecular → Setup → Create Machine Prefabs or by hand.
/// </summary>
[CreateAssetMenu(fileName = "BuildingCatalog", menuName = "Biomolecular/BuildingCatalog")]
public class BuildingCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public PlaceableBuilding prefab;
        [Tooltip("Optional icon displayed in the build menu UI.")]
        public Sprite icon;
    }

    public List<Entry> buildings = new();

    public Entry FindByName(string name)
    {
        foreach (var e in buildings)
            if (e.prefab != null && e.prefab.buildingName == name)
                return e;
        return null;
    }
}
