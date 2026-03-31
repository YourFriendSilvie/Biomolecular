using UnityEngine;

/// <summary>
/// Add this to any machine prefab root to make it interactable via the E key.
/// Requires a Collider on the root or a child so PlayerInteraction can raycast it.
///
/// On interaction: opens MachineInteractionUI (finds the singleton instance).
/// Press E again (or close button) to close.
///
/// Setup:
///   1. Add MachineInteractionTrigger to a machine prefab.
///   2. Ensure a BoxCollider exists on the root or a child.
///   3. Place ONE MachineInteractionUI in the scene — this script finds it automatically.
/// </summary>
[RequireComponent(typeof(ProcessingMachine))]
public class MachineInteractionTrigger : MonoBehaviour, IHarvestable
{
    private ProcessingMachine _machine;

    void Awake() => _machine = GetComponent<ProcessingMachine>();

    // ── IHarvestable ──────────────────────────────────────────────────────────

    public string GetHarvestDisplayName() => gameObject.name;

    public string GetHarvestPreview()
    {
        if (_machine == null) return "";
        return MachineInteractionUI.Instance != null && MachineInteractionUI.Instance.IsOpen(_machine)
            ? "[E] Close"
            : "[E] Open machine panel";
    }

    public bool Harvest(Inventory playerInventory)
    {
        var ui = MachineInteractionUI.Instance;
        if (ui == null)
        {
            Debug.LogWarning("[MachineInteractionTrigger] No MachineInteractionUI found in scene. " +
                             "Add one and assign your UI panel references.");
            return false;
        }

        if (ui.IsOpen(_machine))
            ui.Close();
        else
            ui.Open(_machine, playerInventory);

        return true;
    }
}
