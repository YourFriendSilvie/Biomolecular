using UnityEngine;

/// <summary>
/// Player-activated crafting surface — no grid power required.
/// The player loads items into inputStorage, selects a recipe via UI, and
/// triggers crafting.  Progress ticks automatically once started.
/// Output items sit in outputStorage until the player collects them (E key → Harvest).
///
/// Unlike automated ProcessingMachines, the ManualCraftingTable:
///   - Does NOT auto-start recipes (autoProcess = false)
///   - Requires no power (requiredWatts = 0)
///   - Exposes TryCraft(recipe) for UI/interaction code to call
///   - Implements IHarvestable so the player can collect output via PlayerInteraction
/// </summary>
public class ManualCraftingTable : ProcessingMachine, IHarvestable
{
    void Reset()
    {
        machineType   = MachineType.Manual;
        requiredWatts = 0f;
        powerPriority = 10;
        autoProcess   = false;
    }

    // ── Player interaction ────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to start crafting with the given recipe.
    /// Returns false if already crafting or inputs are insufficient.
    /// </summary>
    public bool TryCraft(ProcessingRecipe recipe)
    {
        if (recipe == null)                            return false;
        if (_activeRecipe != null)                     return false;
        if (!recipe.CanProcess(inputStorage))          return false;

        StartRecipe(recipe);
        return true;
    }

    /// <summary>Cancels the active recipe without consuming inputs or producing output.</summary>
    public void CancelCraft()
    {
        _activeRecipe = null;
        _progress     = 0f;
    }

    // ── IHarvestable — collect output via E key ───────────────────────────────

    public bool Harvest(Inventory playerInventory)
    {
        if (outputStorage == null) return false;
        return outputStorage.TransferAllTo(playerInventory) > 0;
    }

    public string GetHarvestDisplayName() => "Crafting Table";

    public string GetHarvestPreview()
    {
        if (_activeRecipe != null)
            return $"Crafting: {_activeRecipe.recipeName} ({_progress * 100f:F0}%)";
        return outputStorage != null && HasOutput() ? "Collect output" : "Empty";
    }

    private bool HasOutput()
    {
        for (int i = 0; i < outputStorage.SlotCount; i++)
            if (outputStorage.GetSlot(i) != null) return true;
        return false;
    }
}
