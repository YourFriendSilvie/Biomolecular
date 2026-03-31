using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hides a list of GameObjects when build mode is entered, shows them when exited.
/// Attach to any Manager object in the scene.
///
/// Use this to hide: the metabolism HUD bars, the interaction prompt, the inventory HUD, etc.
/// </summary>
public class HideOnBuildMode : MonoBehaviour
{
    [SerializeField] private List<GameObject> objectsToHide = new();

    void Start()
    {
        if (BuildingSystem.Instance != null)
            BuildingSystem.Instance.BuildModeChanged += OnBuildModeChanged;
    }

    void OnDestroy()
    {
        if (BuildingSystem.Instance != null)
            BuildingSystem.Instance.BuildModeChanged -= OnBuildModeChanged;
    }

    void OnBuildModeChanged(bool entering)
    {
        foreach (var go in objectsToHide)
            if (go != null) go.SetActive(!entering);
    }
}
