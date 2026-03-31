using UnityEngine;

/// <summary>
/// Implemented by systems (e.g. <see cref="ProceduralVoxelTerrain"/>) that can resolve a
/// <see cref="RaycastHit"/> into an <see cref="IHarvestable"/> target. Implement and register
/// the provider with <c>PlayerInteraction</c> so raycast-based harvesting routes hits correctly.
/// </summary>
public interface IRaycastHarvestableProvider
{
    /// <summary>
    /// Attempts to resolve a physics raycast hit into a harvestable target owned by this provider.
    /// </summary>
    /// <param name="hit">The raycast hit returned by <c>Physics.Raycast</c>.</param>
    /// <param name="harvestable">
    /// When this method returns <c>true</c>, the <see cref="IHarvestable"/> at the hit location;
    /// otherwise <c>null</c>.
    /// </param>
    /// <returns><c>true</c> if a valid harvestable target was found for this hit.</returns>
    bool TryGetHarvestable(RaycastHit hit, out IHarvestable harvestable);
}
