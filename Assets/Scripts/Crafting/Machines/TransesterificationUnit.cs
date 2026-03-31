/// <summary>
/// Transesterification unit: converts plant oils/lipids → biodiesel (FAME) + glycerol.
///
/// Real-world basis:
///   Triglyceride + 3 CH3OH --NaOH cat, 55°C, 45min--> 3 FAME (biodiesel) + glycerol
///   Stoichiometry: 100g oil + 11g methanol → 98g FAME + 10.5g glycerol (nearly 100% yield).
///   FAME (Fatty Acid Methyl Ester) calorific value: 38.5 MJ/kg — excellent liquid fuel.
///   Glycerol (calorific value: 16 MJ/kg) is a useful byproduct: burnable, chemical feedstock.
///
/// Plant oil sources in-game:
///   - Nut/seed harvests (hazelnut, bigleaf maple seeds, red alder catkin seeds): ~50% oil content
///   - Fruiting bodies: serviceberry seeds have ~10-20% oil (not tracked yet in compositions)
///   - Future: algae cultivation (50% oil by dry mass)
///
/// Game scale: 300W, 60s per cycle.
///   Recipe inputs: Lipid 100g + Methanol 11g
///   Recipe outputs: Biodiesel 98g + Glycerol 10g
///
/// Inspector setup:
///   - machineType   = TransesterificationUnit
///   - requiredWatts = 300  (mild heating to 55°C)
///   - powerPriority = 6
/// </summary>
public class TransesterificationUnit : ProcessingMachine
{
    void Reset()
    {
        machineType   = MachineType.TransesterificationUnit;
        requiredWatts = 300f;
        powerPriority = 6;
        autoProcess   = true;
    }
}
