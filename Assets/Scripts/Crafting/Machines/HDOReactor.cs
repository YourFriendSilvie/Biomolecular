/// <summary>
/// Hydrodeoxygenation (HDO) reactor: upgrades bio-oil to high-quality biodiesel.
///
/// Real-world basis:
///   Bio-oil (C~10H~12O~3) + H2 --Ni/SiO2 cat, 300-400°C, 50-150 bar--> hydrocarbons + H2O
///   Example: phenol + 3H2 → cyclohexane + H2O at 300°C, 70 bar Ni catalyst.
///   Per 100g bio-oil: 75g biodiesel (44.5 MJ/kg) + 12g water + 13g off-gas.
///   H2 consumption: ~4g per 100g bio-oil.
///
/// Game simplification: Water serves as the in-situ H2 source (simplified steam reforming).
///   This avoids requiring a separate H2 production chain in early game.
///   The player can later build a Gasifier + water-gas shift chain for proper H2.
///
/// Game scale: 1200W, 90s per cycle.
///   Recipe inputs: Bio-oil 100g + Water 30g (H2 proxy)
///   Recipe outputs: Biodiesel (HDO) 75g + Water 15g (condensate byproduct)
///
/// Inspector setup:
///   - machineType   = HDOReactor
///   - requiredWatts = 1200  (high-pressure, high-temperature)
///   - powerPriority = 4
/// </summary>
public class HDOReactor : ProcessingMachine
{
    void Reset()
    {
        machineType   = MachineType.HDOReactor;
        requiredWatts = 1200f;
        powerPriority = 4;
        autoProcess   = true;
    }
}
