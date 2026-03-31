/// <summary>
/// Dries biomass (wood, plant matter) by driving off water content,
/// increasing effective fuel energy.  Also used for calcination of limestone
/// (CaCO₃ → CaO + CO₂) and other moderate-heat processes.
///
/// Real-world basis:
///   Conventional kiln: 60–80°C, 1–4 weeks; target 8–12% moisture content.
///   HFVD (advanced): dielectric heating + vacuum, 2–8 h, 5–10% MC.
///   Game scale: 500 W, 60 s per 100 g batch → outputs kiln-dry material.
///
/// Kiln output composition: same molecules as input but with Water resource
/// substantially reduced (recipe specifies the new CompositionInfo with lower water %).
///
/// Inspector setup:
///   - machineType   = Kiln
///   - requiredWatts = 500
///   - Recipe input:  "Water" in the item (min mass = the water to evaporate)
///   - Recipe output: Dried variant of the same material type
/// </summary>
public class Kiln : ProcessingMachine
{
    void Reset()
    {
        machineType   = MachineType.Kiln;
        requiredWatts = 500f;
        powerPriority = 5;
        autoProcess   = true;
    }
}
