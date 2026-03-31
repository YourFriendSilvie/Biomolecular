using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Burns fuel items to generate electrical power via a steam buffer.
///
/// Real-world basis:
///   - Small biomass boiler: 55–65% thermal efficiency
///   - Wood composition (dry): Cellulose 17.3 MJ/kg, Hemicellulose 18.0 MJ/kg, Lignin 27.2 MJ/kg
///   - Water penalty: 2.26 MJ/kg of water present (latent heat of vaporization)
///   - Moisture >40%: item barely burns (game: 0 energy)
///
/// The generator burns fuel at a configurable grams/second rate, converting chemical
/// energy into a joule buffer.  The buffer drains to meet grid demand up to maxOutputWatts.
/// This decouples burn rate from demand spikes, mimicking real boiler thermal mass.
/// </summary>
public class SteamGenerator : MonoBehaviour, IPowerProducer
{
    [Header("Fuel Storage")]
    [SerializeField] private MachineItemStorage fuelStorage;

    [Header("Generator Parameters")]
    [SerializeField, Range(0.1f, 1f)]
    [Tooltip("Fraction of fuel chemical energy converted to electricity. 0.6 = 60% (realistic small boiler).")]
    private float thermalEfficiency = 0.60f;

    [SerializeField, Min(1f)]
    [Tooltip("Maximum electrical output in Watts. Default 2 kW suits early-game machines.")]
    private float maxOutputWatts = 2000f;

    [Header("Buffer")]
    [SerializeField, Min(1000f)]
    [Tooltip("Maximum Joules stored in the steam/thermal buffer. 100 kJ ≈ 50 s at 2 kW.")]
    private float maxBufferJoules = 100_000f;

    [Header("Combustion Rate")]
    [SerializeField, Min(0.1f)]
    [Tooltip("Grams of fuel burned per second. Increase for faster heat-up; decrease to stretch fuel.")]
    private float burnRateGramsPerSecond = 5f;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private float _bufferJoules;

    // ── IPowerProducer ────────────────────────────────────────────────────────
    public float CurrentOutputWatts { get; private set; }

    // ── Diagnostics (read-only for HUD/debug) ────────────────────────────────
    public float BufferFillFraction  => maxBufferJoules > 0f ? _bufferJoules / maxBufferJoules : 0f;
    public bool  HasFuel             => fuelStorage != null && HasCombustibleFuel();
    public float ThermalEfficiency   => thermalEfficiency;
    public float MaxOutputWatts      => maxOutputWatts;

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
    private void BurnFuelIntoBuffer(float dt)
    {
        if (fuelStorage == null || _bufferJoules >= maxBufferJoules) return;

        float massNeeded = burnRateGramsPerSecond * dt;

        if (fuelStorage.TryBurnMass(massNeeded, out float massConsumed, out List<Composition> comp))
        {
            float mjPerKg  = FuelCombustionValues.CalculateEffectiveMJPerKg(comp);
            float joulesIn = FuelCombustionValues.MassGramsToJoules(massConsumed, mjPerKg) * thermalEfficiency;
            _bufferJoules  = Mathf.Min(_bufferJoules + joulesIn, maxBufferJoules);
        }
    }

    private void DrainBufferForOutput(float dt)
    {
        // Deliver up to maxOutputWatts; limited by what the grid actually needs
        float gridDemand   = PowerGrid.Instance != null ? PowerGrid.Instance.TotalDemandWatts : maxOutputWatts;
        float targetOutput = Mathf.Min(gridDemand, maxOutputWatts);
        float joulesToSpend = targetOutput * dt;

        if (_bufferJoules >= joulesToSpend)
        {
            _bufferJoules      -= joulesToSpend;
            CurrentOutputWatts  = targetOutput;
        }
        else
        {
            // Buffer nearly empty — output whatever remains
            CurrentOutputWatts = dt > 0f ? _bufferJoules / dt : 0f;
            _bufferJoules      = 0f;
        }
    }

    private bool HasCombustibleFuel()
    {
        foreach (var item in fuelStorage.GetItems())
            if (FuelCombustionValues.IsCombustible(item.GetComposition())) return true;
        return false;
    }
}
