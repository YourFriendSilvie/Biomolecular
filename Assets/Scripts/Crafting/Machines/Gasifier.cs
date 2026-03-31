/// <summary>
/// Downdraft biomass gasifier: converts dry biomass to syngas via partial oxidation.
///
/// Real-world basis:
///   Key reactions:
///     C + O2   → CO2          (combustion, exothermic, sustains temperature)
///     C + CO2  → 2 CO         (Boudouard, endothermic)
///     C + H2O  → CO + H2      (water-gas, endothermic)
///   Temperature: 700-1000°C.
///   Per 100g dry biomass: ~75g syngas (CO 15%, H2 12%, CO2 10%, CH4 3%, N2 60%) + 25g char.
///   Syngas energy: ~4 MJ/m³ (3.89 MJ/m³ at STP for typical composition).
///   Game abstraction: syngas tracked as 12 MJ/kg compressed gas equivalent.
///
/// Char byproduct (28.5 MJ/kg) is valuable:
///   - Recirculate to gasifier for more syngas
///   - Use as reducing agent in Smelter (replaces ~10g charcoal per unit)
///   - Burn directly in SteamGenerator
///
/// Requires DRY feedstock (<20% moisture). Wet wood should go through Kiln first.
///
/// Game scale: 600W, 30s per cycle.
///   Recipe inputs: Cellulose 70g + Lignin 30g (or any dry biomass molecules)
///   Recipe outputs: Syngas 75g + Biochar 20g
///
/// Inspector setup:
///   - machineType   = Gasifier
///   - requiredWatts = 600
///   - powerPriority = 5
/// </summary>
public class Gasifier : ProcessingMachine
{
    void Reset()
    {
        machineType   = MachineType.Gasifier;
        requiredWatts = 600f;
        powerPriority = 5;
        autoProcess   = true;
    }
}
