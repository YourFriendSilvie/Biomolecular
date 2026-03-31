/// <summary>
/// Contract for any in-world object that the player can harvest.
/// Implement this interface on a MonoBehaviour to make an object mineable, cuttable, or
/// otherwise collectible. Register a provider via <see cref="IRaycastHarvestableProvider"/>
/// to integrate with the raycast-based interaction system in <c>PlayerInteraction</c>.
/// </summary>
public interface IHarvestable
{
    /// <summary>
    /// Performs the harvest action, optionally yielding items into the player's inventory.
    /// </summary>
    /// <param name="playerInventory">The inventory that receives any harvested items.</param>
    /// <returns>
    /// <c>true</c> if the harvest succeeded and the object was consumed or damaged;
    /// <c>false</c> if the harvest was blocked (e.g. inventory full, already depleted).
    /// </returns>
    bool Harvest(Inventory playerInventory);

    /// <summary>
    /// Returns the display name of this harvestable shown in the player's interaction UI.
    /// </summary>
    /// <returns>A human-readable name, e.g. <c>"Organic Soil"</c> or <c>"Serviceberry Bush"</c>.</returns>
    string GetHarvestDisplayName();

    /// <summary>
    /// Returns a short preview string describing what the player will receive when harvesting.
    /// Used for the interaction tooltip before the player commits to the action.
    /// </summary>
    /// <returns>A preview string suitable for the interaction HUD, e.g. <c>"~850g Organic Soil"</c>.</returns>
    string GetHarvestPreview();
}
