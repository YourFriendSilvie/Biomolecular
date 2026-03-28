using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class Inventory : MonoBehaviour
{
    [Header("Inventory Settings")]
    [SerializeField] private int maxSlots = 20;
    [SerializeField] private bool autoStack = true;
    
    [Header("Current Inventory")]
    [SerializeField] private List<InventoryItem> items = new List<InventoryItem>();

    // Events for UI updates
    public delegate void InventoryChanged();
    public event InventoryChanged OnInventoryChanged;

    private void Awake()
    {
        // Initialize empty slots
        for (int i = 0; i < maxSlots; i++)
        {
            items.Add(null);
        }
    }

    /// <summary>
    /// Add an item to inventory with optional mass
    /// </summary>
    public bool AddItem(CompositionInfo compositionInfo, int quantity = 1, float mass = 0f, List<Composition> effectiveComposition = null)
    {
        if (compositionInfo == null)
        {
            Debug.LogWarning("Cannot add null CompositionInfo to inventory");
            return false;
        }

        return AddItems(new[]
        {
            new InventoryItem(compositionInfo, quantity, mass, effectiveComposition)
        });
    }

    public bool AddItems(IEnumerable<InventoryItem> incomingItems)
    {
        if (incomingItems == null)
        {
            Debug.LogWarning("Cannot add null item collection to inventory");
            return false;
        }

        List<InventoryItem> pendingItems = new List<InventoryItem>();
        foreach (var item in incomingItems)
        {
            if (item == null || item.compositionInfo == null || item.quantity <= 0)
            {
                continue;
            }

            pendingItems.Add(CloneItem(item));
        }

        if (pendingItems.Count == 0)
        {
            Debug.LogWarning("No valid items were provided to add to inventory");
            return false;
        }

        List<InventoryItem> simulatedItems = CloneSlots(items);
        foreach (var pendingItem in pendingItems)
        {
            if (!TryPlaceItem(simulatedItems, pendingItem))
            {
                Debug.LogWarning("Inventory is full!");
                return false;
            }
        }

        items.Clear();
        items.AddRange(simulatedItems);
        OnInventoryChanged?.Invoke();
        return true;
    }

    private bool TryPlaceItem(List<InventoryItem> targetSlots, InventoryItem incomingItem)
    {
        if (incomingItem == null || incomingItem.compositionInfo == null)
        {
            return false;
        }

        if (autoStack)
        {
            for (int i = 0; i < targetSlots.Count; i++)
            {
                if (targetSlots[i] != null && targetSlots[i].CanStackWith(incomingItem))
                {
                    targetSlots[i].AddToStack(incomingItem.quantity, incomingItem.totalMass, incomingItem.GetComposition());
                    return true;
                }
            }
        }

        for (int i = 0; i < targetSlots.Count; i++)
        {
            if (targetSlots[i] == null)
            {
                targetSlots[i] = incomingItem;
                return true;
            }
        }

        return false;
    }

    private static List<InventoryItem> CloneSlots(IEnumerable<InventoryItem> sourceSlots)
    {
        List<InventoryItem> clonedSlots = new List<InventoryItem>();
        if (sourceSlots == null)
        {
            return clonedSlots;
        }

        foreach (var sourceItem in sourceSlots)
        {
            clonedSlots.Add(CloneItem(sourceItem));
        }

        return clonedSlots;
    }

    private static InventoryItem CloneItem(InventoryItem sourceItem)
    {
        if (sourceItem == null || sourceItem.compositionInfo == null)
        {
            return null;
        }

        return new InventoryItem(
            sourceItem.compositionInfo,
            sourceItem.quantity,
            sourceItem.totalMass,
            sourceItem.GetComposition());
    }

    /// <summary>
    /// Remove a specific quantity of an item
    /// </summary>
    public bool RemoveItem(CompositionInfo compositionInfo, int quantity = 1)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null && items[i].compositionInfo == compositionInfo)
            {
                items[i].RemoveQuantity(quantity);
                
                if (items[i].quantity <= 0 || items[i].totalMass <= 0f)
                {
                    items[i] = null;
                }
                
                OnInventoryChanged?.Invoke();
                return true;
            }
        }
        
        Debug.LogWarning($"Could not find {compositionInfo.itemName} in inventory");
        return false;
    }

    /// <summary>
    /// Remove item from a specific slot
    /// </summary>
    public void RemoveItemAt(int slotIndex, int quantity = 1)
    {
        if (slotIndex < 0 || slotIndex >= items.Count)
            return;

        if (items[slotIndex] != null)
        {
            items[slotIndex].RemoveQuantity(quantity);
            
            if (items[slotIndex].quantity <= 0 || items[slotIndex].totalMass <= 0f)
            {
                items[slotIndex] = null;
            }
            
            OnInventoryChanged?.Invoke();
        }
    }

    /// <summary>
    /// Get item at specific slot
    /// </summary>
    public InventoryItem GetItemAt(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= items.Count)
            return null;
            
        return items[slotIndex];
    }

    /// <summary>
    /// Check if inventory has a specific item
    /// </summary>
    public bool HasItem(CompositionInfo compositionInfo, int requiredQuantity = 1)
    {
        int totalFound = 0;
        
        foreach (var item in items)
        {
            if (item != null && item.compositionInfo == compositionInfo)
            {
                totalFound += item.quantity;
                if (totalFound >= requiredQuantity)
                    return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Get total quantity of a specific item across all slots
    /// </summary>
    public int GetItemCount(CompositionInfo compositionInfo)
    {
        int total = 0;
        
        foreach (var item in items)
        {
            if (item != null && item.compositionInfo == compositionInfo)
            {
                total += item.quantity;
            }
        }
        
        return total;
    }

    /// <summary>
    /// Get total amount of a specific molecular resource across entire inventory
    /// </summary>
    public float GetTotalResourceAmount(string resourceName)
    {
        float total = 0f;
        
        foreach (var item in items)
        {
            if (item != null)
            {
                total += item.GetResourceAmount(resourceName);
            }
        }
        
        return total;
    }

    /// <summary>
    /// Get all unique resources and their total amounts in inventory
    /// </summary>
    public Dictionary<string, float> GetAllResourceTotals()
    {
        Dictionary<string, float> resourceTotals = new Dictionary<string, float>();
        
        foreach (var item in items)
        {
            if (item != null && item.compositionInfo != null)
            {
                foreach (var comp in item.GetComposition())
                {
                    float amount = (comp.percentage / 100f) * item.totalMass;
                    
                    if (resourceTotals.ContainsKey(comp.resource))
                        resourceTotals[comp.resource] += amount;
                    else
                        resourceTotals[comp.resource] = amount;
                }
            }
        }
        
        return resourceTotals;
    }

    /// <summary>
    /// Swap items between two slots
    /// </summary>
    public void SwapItems(int slotA, int slotB)
    {
        if (slotA < 0 || slotA >= items.Count || slotB < 0 || slotB >= items.Count)
            return;

        InventoryItem temp = items[slotA];
        items[slotA] = items[slotB];
        items[slotB] = temp;
        
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Clear the entire inventory
    /// </summary>
    public void ClearInventory()
    {
        for (int i = 0; i < items.Count; i++)
        {
            items[i] = null;
        }
        
        OnInventoryChanged?.Invoke();
    }

    public int MaxSlots => maxSlots;
    public List<InventoryItem> Items => items;
}
