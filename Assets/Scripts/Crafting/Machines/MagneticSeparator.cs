/// <summary>
/// Separates ferro-magnetic minerals (e.g., magnetite, Fe₃O₄) from crushed ore
/// using a magnetic field.  Copper-bearing minerals pass through as tailings.
///
/// Real-world basis:
///   High-gradient magnetic separator: 2–5 kW, ~70–80% Fe recovery from magnetite.
///   Works best on finely crushed ore (output of OreCrusher).
///   Game scale: 200 W, 20 s cycle.
///
/// Inspector setup:
///   - machineType   = MagneticSeparator
///   - requiredWatts = 200
///   - Recipe inputs:  "Iron Oxide" or "Magnetite" (by resource name in composition)
///   - Recipe outputs: Iron Concentrate + Tailings (two recipes, or one combined)
/// </summary>
public class MagneticSeparator : ProcessingMachine
{
    void Reset()
    {
        machineType   = MachineType.MagneticSeparator;
        requiredWatts = 200f;
        powerPriority = 5;
        autoProcess   = true;
    }
}
