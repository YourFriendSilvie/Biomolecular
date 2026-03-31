/// <summary>
/// Biological fermentation vessel: converts cellulose/glucose → ethanol via yeast.
///
/// Real-world basis:
///   C6H10O5 (cellulose) + H2O → C6H12O6 (glucose) [enzymatic hydrolysis, 45°C, 24-48h]
///   C6H12O6 → 2 C2H5OH + 2 CO2 [yeast fermentation, 30-35°C, 24-72h]
///   Theoretical yield: 56.8g ethanol per 100g cellulose.
///   Practical (game-scaled, distillation included): ~25g ethanol per 100g cellulose.
///   CO2 escapes to atmosphere; water used in hydrolysis.
///
/// Game scale: 100W, 180s per cycle.
///   Recipe inputs: Cellulose 100g + Water 50g
///   Recipe outputs: Ethanol 25g
///
/// Inspector setup:
///   - machineType   = FermentationVessel
///   - requiredWatts = 100  (low heat to maintain 35°C)
///   - powerPriority = 7
///   - Assign recipes with cellulose + water → ethanol output
/// </summary>
public class FermentationVessel : ProcessingMachine
{
    void Reset()
    {
        machineType   = MachineType.FermentationVessel;
        requiredWatts = 100f;
        powerPriority = 7;
        autoProcess   = true;
    }
}
