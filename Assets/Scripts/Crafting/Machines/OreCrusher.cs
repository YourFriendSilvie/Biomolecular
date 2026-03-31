/// <summary>
/// Crushes raw ore/rock chunks into fine particles, enabling subsequent
/// magnetic separation or flotation.
///
/// Real-world basis:
///   Industrial jaw crusher + ball mill cascade: 5–15 kW, ambient temperature.
///   No chemical change — purely physical size reduction.
///   Game scale: 500 W, configurable batch time (default 15 s per 100 g).
///
/// Inspector setup:
///   - machineType  = Crusher
///   - requiredWatts = 500
///   - Assign inputStorage, outputStorage, and at least one ProcessingRecipe
///     (requiredMachine = Crusher).
/// </summary>
public class OreCrusher : ProcessingMachine
{
    void Reset()
    {
        machineType   = MachineType.Crusher;
        requiredWatts = 500f;
        powerPriority = 5;
        autoProcess   = true;
    }
}
