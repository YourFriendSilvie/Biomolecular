using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// MonoBehaviour that manages the player's item storage. Items are held in a fixed number of
/// slots and will auto-stack when two <see cref="InventoryItem"/> entries share the same name
/// and molecular composition. Compositions blend by mass-weighted average when stacking.
/// Raises <see cref="OnInventoryChanged"/> whenever the contents change so UI can refresh.
/// </summary>
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
    /// <param name="compositionInfo">The composition asset that defines the item type.</param>
    /// <param name="quantity">Number of units to add.</param>
    /// <param name="mass">Total mass in grams. Pass <c>0</c> to use the default from <paramref name="compositionInfo"/>.</param>
    /// <param name="effectiveComposition">Override composition list, or <c>null</c> to use the asset defaults.</param>
    /// <returns><c>true</c> if all items were placed; <c>false</c> if the inventory was full.</returns>
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

    /// <summary>
    /// Adds a batch of items to the inventory in a single atomic operation.
    /// All items are first simulated against a clone of the current slots; only if every item
    /// fits is the real inventory updated, preventing partial additions.
    /// </summary>
    /// <param name="incomingItems">Items to add. Null entries and zero-quantity items are skipped.</param>
    /// <returns><c>true</c> if all valid items were placed; <c>false</c> if any item could not fit.</returns>
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
    /// Remove a specific quantity of an item by its composition type.
    /// The first matching slot is decremented; if its quantity reaches zero the slot is cleared.
    /// </summary>
    /// <param name="compositionInfo">The composition asset identifying which item to remove.</param>
    /// <param name="quantity">Number of units to remove.</param>
    /// <returns><c>true</c> if the item was found and removed; <c>false</c> if no matching slot existed.</returns>
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
    /// Removes a quantity of the item in the given slot. Clears the slot when quantity reaches zero.
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index. Out-of-range indices are silently ignored.</param>
    /// <param name="quantity">Number of units to remove.</param>
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
    /// Returns the <see cref="InventoryItem"/> in the given slot, or <c>null</c> if the slot is empty or out of range.
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    public InventoryItem GetItemAt(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= items.Count)
            return null;
            
        return items[slotIndex];
    }

    /// <summary>
    /// Returns <c>true</c> if the inventory contains at least <paramref name="requiredQuantity"/> units
    /// of the specified composition type across all slots.
    /// </summary>
    /// <param name="compositionInfo">The composition asset identifying which item to look for.</param>
    /// <param name="requiredQuantity">Minimum total quantity required.</param>
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
    /// Returns the sum of all stacked quantities of the given composition type across every slot.
    /// </summary>
    /// <param name="compositionInfo">The composition asset identifying which item to count.</param>
    /// <returns>Total unit count, or <c>0</c> if the item is not in inventory.</returns>
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
    /// Returns the total grams of a named molecular resource (e.g. <c>"Cellulose"</c>) held across
    /// all inventory slots, computed from each item's composition percentages and mass.
    /// </summary>
    /// <param name="resourceName">The molecule/resource name to sum, case-sensitive.</param>
    /// <returns>Total mass in grams, or <c>0</c> if the resource is not present.</returns>
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
    /// Aggregates every molecular resource across all slots into a single dictionary of
    /// resource name → total grams. Useful for crafting checks and UI summary panels.
    /// </summary>
    /// <returns>
    /// A dictionary mapping resource names to their combined mass in grams.
    /// Only resources actually present in the inventory are included.
    /// </returns>
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
    /// Swaps the items between two inventory slots. Out-of-range indices are silently ignored.
    /// </summary>
    /// <param name="slotA">Zero-based index of the first slot.</param>
    /// <param name="slotB">Zero-based index of the second slot.</param>
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
    /// Sets every slot to <c>null</c> and fires <see cref="OnInventoryChanged"/>.
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
