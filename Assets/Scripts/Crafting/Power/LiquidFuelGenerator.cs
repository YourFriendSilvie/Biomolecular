using UnityEngine;

/// <summary>
/// Internal combustion generator that burns refined liquid or gaseous fuels
/// directly to produce electricity.
///
/// Differences from SteamGenerator:
///   - Burns only high-energy-density fuels (configurable minimumFuelMJPerKg threshold)
///   - Lower thermal efficiency (~35%) because internal combustion is less efficient than
///     a steam boiler, but the fuels themselves are 2-3× more energy-dense than raw biomass
///   - No large thermal buffer — responds to load within seconds (tiny surge buffer only)
///   - Suitable for high-power applications (default 5 kW vs steam generator's 2 kW)
///
/// Real-world basis:
///   Spark-ignition engine on ethanol: ~28-35% brake thermal efficiency
///   Compression-ignition on biodiesel: ~38-42%
///   Syngas engine (producer gas): ~30-38%
///
/// Supported fuels (via FuelCombustionValues):
///   Ethanol (26.8), Furfural (24.3), Syngas (12.0), Biodiesel FAME (38.5),
///   Biodiesel HDO (44.5), Biocrude (33.0), Glycerol (16.0), Methanol (19.9)
/// </summary>
public class LiquidFuelGenerator : MonoBehaviour, IPowerProducer
{
    [Header("Fuel Storage")]
    [SerializeField] private MachineItemStorage fuelStorage;

    [Header("Generator Parameters")]
    [SerializeField, Range(0.1f, 0.5f)]
    [Tooltip("Fraction of fuel chemical energy converted to electricity. 0.35 = 35% (realistic IC engine).")]
    private float thermalEfficiency = 0.35f;

    [SerializeField, Min(1f)]
    [Tooltip("Maximum electrical output in Watts.")]
    private float maxOutputWatts = 5000f;

    [SerializeField, Min(0f)]
    [Tooltip("Only accept fuels with at least this many MJ/kg. Prevents using raw biomass in an IC engine. " +
             "Set 0 to accept any combustible.")]
    private float minimumFuelMJPerKg = 12f;

    [Header("Combustion Rate")]
    [SerializeField, Min(0.01f)]
    [Tooltip("Grams of fuel burned per second at full load.")]
    private float burnRateGramsPerSecond = 2f;

    [Header("Surge Buffer")]
    [SerializeField, Min(100f)]
    [Tooltip("Small capacitor-like buffer in Joules for handling brief load spikes. 10 kJ default.")]
    private float surgeBufferJoules = 10_000f;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private float _bufferJoules;

    // ── IPowerProducer ────────────────────────────────────────────────────────
    public float CurrentOutputWatts { get; private set; }

    // ── Diagnostics ───────────────────────────────────────────────────────────
    public bool  HasFuel            => fuelStorage != null && HasQualifyingFuel();
    public float ThermalEfficiency  => thermalEfficiency;
    public float MaxOutputWatts     => maxOutputWatts;
    public float SurgeBufferFill    => surgeBufferJoules > 0f ? _bufferJoules / surgeBufferJoules : 0f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (fuelStorage == null)
            fuelStorage = GetComponent<MachineItemStorage>();
    }

    void OnEnable()  => PowerGrid.Instance?.Register(this);
    void OnDisable() => PowerGrid.Instance?.Unregister(this);

    void Update()
    {
        BurnFuelIntoBuffer(Time.deltaTime);
        DrainBufferForOutput(Time.deltaTime);
    }

    // ── Combustion ────────────────────────────────────────────────────────────

    void BurnFuelIntoBuffer(float dt)
    {
        if (fuelStorage == null || _bufferJoules >= surgeBufferJoules) return;

        float massNeeded = burnRateGramsPerSecond * dt;
        if (!fuelStorage.TryBurnMass(massNeeded, out float massConsumed, out var comp)) return;

        float mjPerKg = FuelCombustionValues.CalculateEffectiveMJPerKg(comp);
        if (mjPerKg < minimumFuelMJPerKg)
        {
            // Fuel too low-grade for IC engine — put mass back or just waste it
            // Game simplification: mass is already consumed; we just don't generate power
            return;
        }

        float joulesIn = FuelCombustionValues.MassGramsToJoules(massConsumed, mjPerKg) * thermalEfficiency;
        _bufferJoules = Mathf.Min(_bufferJoules + joulesIn, surgeBufferJoules);
    }

    void DrainBufferForOutput(float dt)
    {
        float gridDemand   = PowerGrid.Instance != null ? PowerGrid.Instance.TotalDemandWatts : maxOutputWatts;
        float targetOutput = Mathf.Min(gridDemand, maxOutputWatts);
        float joulesToSpend = targetOutput * dt;

        if (_bufferJoules >= joulesToSpend)
        {
            _bufferJoules     -= joulesToSpend;
            CurrentOutputWatts = targetOutput;
        }
        else
        {
            CurrentOutputWatts = dt > 0f ? _bufferJoules / dt : 0f;
            _bufferJoules      = 0f;
        }
    }

    bool HasQualifyingFuel()
    {
        foreach (var item in fuelStorage.GetItems())
        {
            float mjPerKg = FuelCombustionValues.CalculateEffectiveMJPerKg(item.GetComposition());
            if (mjPerKg >= minimumFuelMJPerKg) return true;
        }
        return false;
    }
}
