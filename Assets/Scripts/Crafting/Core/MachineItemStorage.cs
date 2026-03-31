using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Item buffer attached to a processing machine.  Manages one set of slots
/// (use two components: one for input, one for output).
///
/// Supports composition-aware consumption so recipes can pull specific molecules
/// from mixed input items (e.g., pull 50 g of Cellulose from a wood chunk).
/// </summary>
public class MachineItemStorage : MonoBehaviour
{
    [SerializeField, Min(1)] private int slotCount = 6;

    private InventoryItem[] _slots;

    public event Action OnStorageChanged;

    public int SlotCount => _slots?.Length ?? slotCount;

    void Awake() => _slots = new InventoryItem[slotCount];

    // ── Queries ───────────────────────────────────────────────────────────────

    public InventoryItem GetSlot(int index)
        => (index >= 0 && index < _slots.Length) ? _slots[index] : null;

    /// <summary>Returns total grams of the given resource across all slots.</summary>
    public float GetAvailableMassOf(string resourceName)
    {
        float total = 0f;
        foreach (var item in _slots)
            if (item != null) total += item.GetResourceAmount(resourceName);
        return total;
    }

    /// <summary>Enumerate all non-null items (for fuel scanning, display, etc.)</summary>
    public IEnumerable<InventoryItem> GetItems()
    {
        foreach (var slot in _slots)
            if (slot != null) yield return slot;
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Tries to insert an item.  Stacks with a compatible slot first, then uses
    /// an empty slot.  Returns false if storage is full.
    /// </summary>
    public bool TryInsert(InventoryItem item)
    {
        if (item == null || item.compositionInfo == null) return false;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null && _slots[i].CanStackWith(item))
            {
                _slots[i].AddToStack(item.quantity, item.totalMass, item.GetComposition());
                OnStorageChanged?.Invoke();
                return true;
            }
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null)
            {
                _slots[i] = item;
                OnStorageChanged?.Invoke();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Consumes up to massGrams of the specified resource, drawing from slots in order.
    /// Returns actual grams consumed (may be less if supply is short).
    /// </summary>
    public float ConsumeMass(string resourceName, float massGrams)
    {
        float remaining = massGrams;

        for (int i = 0; i < _slots.Length && remaining > 0f; i++)
        {
            if (_slots[i] == null) continue;
            float available = _slots[i].GetResourceAmount(resourceName);
            if (available <= 0f) continue;

            float toConsume = Mathf.Min(available, remaining);
            _slots[i].TryExtractResource(resourceName, toConsume);
            remaining -= toConsume;

            if (_slots[i].totalMass <= 0f || _slots[i].quantity <= 0)
                _slots[i] = null;
        }

        float consumed = massGrams - remaining;
        if (consumed > 0f) OnStorageChanged?.Invoke();
        return consumed;
    }

    /// <summary>
    /// Burns up to massGrams from the first non-empty slot regardless of composition.
    /// Returns the composition fractions of the burned material (for energy calculation)
    /// and the actual mass consumed.  Used by SteamGenerator.
    /// </summary>
    public bool TryBurnMass(float massGrams, out float massConsumed, out List<Composition> burnedComposition)
    {
        burnedComposition = new List<Composition>();
        massConsumed      = 0f;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null || _slots[i].totalMass <= 0f) continue;

            massConsumed      = Mathf.Min(_slots[i].totalMass, massGrams);
            burnedComposition = CompositionInfo.CopyComposition(_slots[i].GetComposition());

            _slots[i].totalMass -= massConsumed;
            if (_slots[i].totalMass <= 0f || _slots[i].quantity <= 0)
                _slots[i] = null;

            OnStorageChanged?.Invoke();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Moves all items into a player Inventory.  Returns number of items transferred.
    /// </summary>
    public int TransferAllTo(Inventory inventory)
    {
        int count = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null) continue;
            if (inventory.AddItems(new[] { _slots[i] }))
            {
                _slots[i] = null;
                count++;
            }
        }
        if (count > 0) OnStorageChanged?.Invoke();
        return count;
    }

    /// <summary>
    /// Pulls item at slotIndex from a player inventory into this storage.
    /// </summary>
    public bool TryInsertFromInventory(Inventory inventory, int slotIndex)
    {
        var item = inventory.GetItemAt(slotIndex);
        if (item == null) return false;
        if (!TryInsert(item)) return false;
        inventory.RemoveItemAt(slotIndex, item.quantity);
        return true;
    }
}
