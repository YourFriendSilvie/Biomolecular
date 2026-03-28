using UnityEngine;

public interface IRaycastHarvestableProvider
{
    bool TryGetHarvestable(RaycastHit hit, out IHarvestable harvestable);
}
