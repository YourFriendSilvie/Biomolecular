public interface IHarvestable
{
    bool Harvest(Inventory playerInventory);
    string GetHarvestDisplayName();
    string GetHarvestPreview();
}
