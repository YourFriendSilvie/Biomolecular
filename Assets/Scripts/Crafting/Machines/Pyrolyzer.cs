/// <summary>
/// Fast pyrolysis reactor: thermally decomposes dry biomass/lignin at 400-600°C
/// in the absence of oxygen.
///
/// Real-world basis (per 100g dry lignin):
///   - Bio-oil (liquid condensate):  50g  @ 16.5 MJ/kg  — phenolics, aldehydes, water
///   - Biochar (solid residue):      25g  @ 28.5 MJ/kg  — 80-90% fixed carbon; good reducing agent
///   - Syngas (non-condensable gas): 20g  @ 12.0 MJ/kg  — CO, H2, CO2, CH4
///   - Losses (CO2, water vapor):     5g
///
/// All three outputs are valuable:
///   - Bio-oil → input to HDOReactor → biodiesel
///   - Biochar → fuel for SteamGenerator, or reducing agent in Smelter
///   - Syngas → LiquidFuelGenerator or further processing
///
/// Game scale: 800W, 45s per cycle.
///
/// Inspector setup:
///   - machineType   = Pyrolyzer
///   - requiredWatts = 800
///   - powerPriority = 5
///   - Recipe must have 3 outputs: bio-oil + biochar + syngas
/// </summary>
public class Pyrolyzer : ProcessingMachine
{
    void Reset()
    {
        machineType   = MachineType.Pyrolyzer;
        requiredWatts = 800f;
        powerPriority = 5;
        autoProcess   = true;
    }
}
