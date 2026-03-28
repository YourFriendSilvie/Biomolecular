using System;
using System.Collections.Generic;
using UnityEngine;

public class HarvestableObject : MonoBehaviour, IHarvestable
{
    [Header("World Object Composition")]
    [SerializeField] private CompositionInfo worldComposition;
    
    [Header("Harvest Settings")]
    [SerializeField] private float totalMass = 100f; // Total mass of the object
    [SerializeField] private float harvestEfficiency = 0.8f; // 80% of resources are successfully harvested
    [SerializeField] private bool destroyOnHarvest = true;
    [SerializeField] private int harvestsRequired = 1; // Number of times object needs to be harvested
    [SerializeField] private bool randomizeOnStart = true; // Generate random composition on start
    
    [Header("Visual Feedback")]
    [SerializeField] private GameObject harvestEffect;
    [SerializeField] private AudioClip harvestSound;
    
    private int currentHarvests = 0;
    private AudioSource audioSource;
    private List<Composition> generatedComposition; // Stores the actual generated composition
    private bool isBeingHarvested = false; // Prevent multiple simultaneous harvests

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && harvestSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    public void Configure(
        CompositionInfo composition,
        float objectMass,
        float efficiency = 0.8f,
        bool shouldDestroyOnHarvest = true,
        int requiredHarvests = 1,
        bool shouldRandomizeOnStart = true)
    {
        worldComposition = composition;
        totalMass = Mathf.Max(0f, objectMass);
        harvestEfficiency = Mathf.Clamp01(efficiency);
        destroyOnHarvest = shouldDestroyOnHarvest;
        harvestsRequired = Mathf.Max(1, requiredHarvests);
        randomizeOnStart = shouldRandomizeOnStart;
        currentHarvests = 0;
        generatedComposition = null;
        isBeingHarvested = false;
    }
    
    /// <summary>
    /// Get the actual composition (generated or template)
    /// </summary>
    private List<Composition> GetActiveComposition()
    {
        if (generatedComposition != null && generatedComposition.Count > 0)
        {
            return generatedComposition;
        }

        if (randomizeOnStart && worldComposition != null)
        {
            generatedComposition = worldComposition.GenerateRandomComposition();
            if (generatedComposition != null && generatedComposition.Count > 0)
            {
                return generatedComposition;
            }
        }

        return worldComposition != null ? worldComposition.composition : null;
    }

    /// <summary>
    /// Harvest this object and add resources to player inventory
    /// </summary>
    public bool Harvest(Inventory playerInventory)
    {
        // Prevent multiple harvests at once
        if (isBeingHarvested)
        {
            return false;
        }

        if (worldComposition == null)
        {
            Debug.LogWarning($"{gameObject.name} has no WorldComposition assigned!");
            return false;
        }

        if (playerInventory == null)
        {
            Debug.LogWarning("Player inventory is null!");
            return false;
        }

        // Get the active composition (generated or template)
        List<Composition> activeComposition = GetActiveComposition();

        if (activeComposition == null || activeComposition.Count == 0)
        {
            Debug.LogWarning($"{gameObject.name} has no valid composition!");
            return false;
        }

        float harvestedMass = totalMass * harvestEfficiency;
        if (harvestedMass <= 0f)
        {
            Debug.LogWarning($"{gameObject.name} could not be harvested because the resulting mass is zero.");
            return false;
        }

        isBeingHarvested = true;
        currentHarvests++;

        if (!TryBuildHarvestItems(activeComposition, harvestedMass, out List<InventoryItem> harvestedItems))
        {
            isBeingHarvested = false;
            return false;
        }

        bool added = playerInventory.AddItems(harvestedItems);
        if (!added)
        {
            isBeingHarvested = false;
            return false;
        }

        Debug.Log($"Harvested {worldComposition.itemName} ({harvestedMass:F1} mass) from {gameObject.name}");

        // Play feedback
        PlayHarvestFeedback();

        // Check if object should be destroyed
        if (currentHarvests >= harvestsRequired && destroyOnHarvest)
        {
            Destroy(gameObject, 0.5f); // Small delay for effects
        }
        else
        {
            isBeingHarvested = false;
        }

        return true;
    }

    private bool TryBuildHarvestItems(
        List<Composition> activeComposition,
        float harvestedMass,
        out List<InventoryItem> harvestedItems)
    {
        harvestedItems = new List<InventoryItem>();
        List<string> unresolvedResourceNames = new List<string>();

        foreach (var component in activeComposition)
        {
            if (component == null || string.IsNullOrWhiteSpace(component.resource) || component.percentage <= 0f)
            {
                continue;
            }

            float componentMass = harvestedMass * (component.percentage / 100f);
            if (componentMass <= 0f)
            {
                continue;
            }

            if (TryResolveResourceComposition(component.resource, out CompositionInfo resolvedComposition))
            {
                harvestedItems.Add(new InventoryItem(
                    resolvedComposition,
                    1,
                    componentMass,
                    resolvedComposition.GenerateRandomComposition()));
            }
            else
            {
                unresolvedResourceNames.Add(component.resource);
            }
        }

        if (harvestedItems.Count == 0)
        {
            harvestedItems.Add(new InventoryItem(worldComposition, 1, harvestedMass, activeComposition));
            return true;
        }

        if (unresolvedResourceNames.Count > 0)
        {
            Debug.LogWarning(
                $"{gameObject.name} could not resolve component harvest outputs for {worldComposition.itemName}: {string.Join(", ", unresolvedResourceNames)}");
            harvestedItems.Clear();
            return false;
        }

        return true;
    }

    private static bool TryResolveResourceComposition(string resourceName, out CompositionInfo compositionInfo)
    {
        return CompositionInfoRegistry.TryGetByItemName(resourceName, out compositionInfo);
    }

    /// <summary>
    /// Play visual and audio feedback
    /// </summary>
    private void PlayHarvestFeedback()
    {
        // Spawn particle effect
        if (harvestEffect != null)
        {
            Instantiate(harvestEffect, transform.position, Quaternion.identity);
        }

        // Play sound
        if (audioSource != null && harvestSound != null)
        {
            audioSource.PlayOneShot(harvestSound);
        }
    }

    /// <summary>
    /// Get the world composition info
    /// </summary>
    public CompositionInfo GetWorldComposition() => worldComposition;

    public string GetHarvestDisplayName()
    {
        return worldComposition != null ? worldComposition.itemName : gameObject.name;
    }

    /// <summary>
    /// Preview what would be harvested without actually harvesting
    /// </summary>
    public string GetHarvestPreview()
    {
        if (worldComposition == null)
            return "Nothing to harvest";

        List<Composition> activeComposition = GetActiveComposition();
        if (activeComposition == null)
            return "Nothing to harvest";

        float harvestedMass = Mathf.Max(0f, totalMass * harvestEfficiency);
        string preview = $"{GetHarvestDisplayName()}\nYield: {harvestedMass:F1} g\n";
        
        foreach (var comp in activeComposition)
        {
            float resourceMass = (comp.percentage / 100f) * harvestedMass;
            preview += $"- {comp.resource}: {resourceMass:F1} g ({comp.percentage:F1}%)\n";
        }
        
        return preview.TrimEnd();
    }

    // Gizmo for debugging in editor
    private void OnDrawGizmosSelected()
    {
        if (worldComposition != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 1f);
        }
    }
}
