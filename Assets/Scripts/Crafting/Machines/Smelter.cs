/// <summary>
/// High-temperature smelting furnace for metal extraction and refining.
///
/// Real-world basis:
///   - Bloomery (primitive): 1100–1200°C, 2–4 h per bloom; ~20–30% yield
///   - Iron smelting: Fe₂O₃ + 3CO → 2Fe + 3CO₂ at 600–1600°C
///   - Copper smelting (malachite): Cu₂(OH)₂CO₃ → 2CuO + H₂O + CO₂ at 350–400°C
///                                  CuO + C → Cu + CO₂ at ~1000–1100°C
///   Game scale: 1000 W, 90–120 s per 100 g batch.
///   Recipes may require a Carbon/Charcoal input as the reducing agent.
///
/// Inspector setup:
///   - machineType   = Smelter
///   - requiredWatts = 1000
///   - powerPriority = 4 (slightly higher priority than crusher/separator)
///   - Recipes specify both the ore AND charcoal as inputs (the reducing agent).
/// </summary>
public class Smelter : ProcessingMachine
{
    void Reset()
    {
        machineType   = MachineType.Smelter;
        requiredWatts = 1000f;
        powerPriority = 4;
        autoProcess   = true;
    }
}
