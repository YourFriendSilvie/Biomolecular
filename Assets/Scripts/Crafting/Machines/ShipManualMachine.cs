using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// A damaged ship workstation that can be interacted with by the player (E key).
///
/// Works without any power connection, but has a limited number of uses — the
/// machine breaks down permanently once uses are exhausted, leaving the player
/// with just enough processed metal to build their first automated machines.
///
/// Interaction flow (two-press cycle):
///   First E  → if any recipe is satisfiable from INPUT storage: process it instantly.
///   Follow-up E (or same press) → if OUTPUT storage has items: transfer them to player inventory.
///
/// The machine processes from its own MachineItemStorage children, NOT directly
/// from the player's inventory.  Drop raw materials into InputStorage before use.
///
/// Place on a child GameObject of the CrashedShip prefab.
/// Assign a BoxCollider so PlayerInteraction can raycast to it.
/// </summary>
public class ShipManualMachine : MonoBehaviour, IHarvestable
{
    [Header("Station Identity")]
    [SerializeField] private string stationName   = "Salvage Workstation";
    [SerializeField] private string flavourText   = "A damaged ship component repurposed as a crude tool.";

    [Header("Durability")]
    [SerializeField, Min(1)] private int maxUses = 5;

    [Header("Recipes")]
    [Tooltip("Recipes this station can execute. First matching recipe runs automatically.")]
    [SerializeField] private List<ProcessingRecipe> recipes = new();

    [Header("Storage")]
    [SerializeField] private MachineItemStorage inputStorage;
    [SerializeField] private MachineItemStorage outputStorage;

    // ── State ─────────────────────────────────────────────────────────────────
    private int  _usesRemaining;
    private bool _broken;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        _usesRemaining = maxUses;
    }

    // ── IHarvestable ──────────────────────────────────────────────────────────

    public string GetHarvestDisplayName() => stationName;

    public string GetHarvestPreview()
    {
        if (_broken)
            return $"{stationName} is broken beyond further use.";

        var sb = new StringBuilder();
        sb.AppendLine($"{flavourText}");
        sb.AppendLine($"Uses remaining: {_usesRemaining}/{maxUses}");

        // Show output ready for pickup
        if (outputStorage != null)
        {
            bool hasOutput = false;
            foreach (var item in outputStorage.GetItems())
            {
                if (item == null) continue;
                if (!hasOutput) { sb.AppendLine("Output ready:"); hasOutput = true; }
                sb.AppendLine($"  {item.ItemName}  {item.totalMass:F1}g");
            }
        }

        // Show first valid recipe
        var recipe = FindMatchingRecipe();
        if (recipe != null)
            sb.AppendLine($"Can craft: {recipe.recipeName}");
        else if (inputStorage != null)
            sb.AppendLine("Add materials to the input bin to craft.");

        return sb.ToString().TrimEnd();
    }

    public bool Harvest(Inventory playerInventory)
    {
        if (_broken)
        {
            Debug.Log($"[ShipManualMachine] {stationName} is broken.");
            return false;
        }

        // First priority: collect any output that's ready.
        if (outputStorage != null)
        {
            int transferred = outputStorage.TransferAllTo(playerInventory);
            if (transferred > 0) return true;
        }

        // Second: try to process a recipe.
        var recipe = FindMatchingRecipe();
        if (recipe == null)
        {
            Debug.Log($"[ShipManualMachine] {stationName}: no matching recipe (add materials to input bin).");
            return false;
        }

        ExecuteRecipe(recipe);

        _usesRemaining--;
        if (_usesRemaining <= 0)
        {
            _broken = true;
            Debug.Log($"[ShipManualMachine] {stationName} has broken down — it gave its last bit of usefulness.");
        }

        return true;
    }

    // ── Recipe logic ──────────────────────────────────────────────────────────

    ProcessingRecipe FindMatchingRecipe()
    {
        if (inputStorage == null || recipes == null) return null;

        foreach (var recipe in recipes)
        {
            if (recipe == null) continue;
            if (RecipeSatisfied(recipe)) return recipe;
        }
        return null;
    }

    bool RecipeSatisfied(ProcessingRecipe recipe)
    {
        foreach (var input in recipe.inputs)
        {
            if (inputStorage.GetAvailableMassOf(input.resourceName) < input.requiredMassGrams)
                return false;
        }
        return true;
    }

    void ExecuteRecipe(ProcessingRecipe recipe)
    {
        // Consume inputs
        foreach (var input in recipe.inputs)
            inputStorage.ConsumeMass(input.resourceName, input.consumedMassGrams);

        // Produce outputs
        if (outputStorage != null)
        {
            foreach (var output in recipe.outputs)
            {
                if (output.composition == null) continue;
                float mass = output.massGrams * output.yieldFraction;
                if (mass < 0.001f) continue;
                var item = new InventoryItem(output.composition, 1, mass);
                if (!outputStorage.TryInsert(item))
                    Debug.LogWarning($"[ShipManualMachine] {stationName}: output storage full — lost {mass:F1}g of {output.composition.itemName}.");
            }
        }

        Debug.Log($"[ShipManualMachine] {stationName} processed: {recipe.recipeName}. {_usesRemaining - 1} uses left.");
    }
}
