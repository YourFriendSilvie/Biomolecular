/// <summary>
/// Acid hydrolysis reactor: converts hemicellulose pentose sugars → furfural.
///
/// Real-world basis:
///   C5H10O5 (xylose) --H2SO4, 170-200°C, 10-20 bar--> C5H4O2 (furfural) + 3 H2O
///   Yield: ~55% furfural from hemicellulose (58g per 100g hemicellulose).
///   Byproducts: levulinic acid (~8g), humin solids (~12g), water + acid (~22g).
///   Furfural calorific value: 24.3 MJ/kg — moderate fuel and platform chemical.
///   Uses: direct fuel, solvent, diesel extender, or input to further hydrogenation.
///
/// Game scale: 600W, 60s per cycle.
///   Recipe inputs: Hemicellulose 100g
///   Recipe outputs: Furfural 58g + Humin (solid residue) 12g
///
/// Inspector setup:
///   - machineType   = AcidHydrolysisReactor
///   - requiredWatts = 600
///   - powerPriority = 5
/// </summary>
public class AcidHydrolysisReactor : ProcessingMachine
{
    void Reset()
    {
        machineType   = MachineType.AcidHydrolysisReactor;
        requiredWatts = 600f;
        powerPriority = 5;
        autoProcess   = true;
    }
}
