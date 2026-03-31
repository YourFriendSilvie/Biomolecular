using UnityEngine;

/// <summary>
/// Electrical battery that buffers grid supply and demand.
///
/// Charges when grid supply exceeds demand; discharges when demand exceeds supply.
/// Implements both IPowerProducer (discharging) and IPowerConsumer (charging).
///
/// Real-world basis (LiFePO4):
///   - Specific energy: ~120-130 Wh/kg
///   - Round-trip efficiency: ~90-95%
///   - Charge/discharge rate: up to 1-2C (1C = full charge in 1 hour)
///
/// Game defaults (small battery bank):
///   - Capacity: 432,000 J = 120 Wh (~1 kg LiFePO4 cell)
///   - Charge rate: 500 W (absorbs surplus up to 500W)
///   - Discharge rate: 1000 W (provides up to 1000W burst when grid is short)
///   - Round-trip efficiency: 90%
///
/// Grid integration:
///   The battery reads PowerGrid.NetBalanceWatts each frame.
///   If surplus (net > 0) → charge mode; if deficit (net < 0) → discharge mode.
///   One-frame lag is imperceptible at gameplay timescales.
/// </summary>
public class Battery : MonoBehaviour, IPowerProducer, IPowerConsumer
{
    [Header("Capacity")]
    [SerializeField, Min(1000f)]
    [Tooltip("Maximum energy storage in Joules. 432000 J = 120 Wh (small LiFePO4 bank).")]
    private float capacityJoules = 432_000f;

    [Header("Charge/Discharge Rates")]
    [SerializeField, Min(1f)]
    [Tooltip("Maximum charge rate in Watts (absorbed from grid when surplus available).")]
    private float chargeRateWatts = 500f;

    [SerializeField, Min(1f)]
    [Tooltip("Maximum discharge rate in Watts (injected to grid when demand exceeds supply).")]
    private float dischargeRateWatts = 1000f;

    [Header("Efficiency")]
    [SerializeField, Range(0.5f, 1f)]
    [Tooltip("Round-trip efficiency (charge energy out / charge energy in). 0.9 = 90% LiFePO4.")]
    private float roundTripEfficiency = 0.90f;

    [Header("Priority")]
    [SerializeField, Min(0)]
    [Tooltip("Lower = higher priority. Battery charges at low priority (high number) so critical machines " +
             "are served first. Discharged power goes to all consumers equally via PowerGrid.")]
    private int chargePriority = 9;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private float _storedJoules;
    private bool  _isCharging;

    // ── IPowerProducer ────────────────────────────────────────────────────────
    public float CurrentOutputWatts { get; private set; }

    // ── IPowerConsumer ────────────────────────────────────────────────────────
    public float RequiredWatts => _isCharging && _storedJoules < capacityJoules ? chargeRateWatts : 0f;
    public bool  IsPowered     { get; set; }
    public int   PowerPriority => chargePriority;
    public bool  IsActive      => _isCharging && _storedJoules < capacityJoules;

    // ── Diagnostics ───────────────────────────────────────────────────────────
    public float StateOfCharge    => capacityJoules > 0f ? _storedJoules / capacityJoules : 0f;
    public float StoredKilojoules => _storedJoules / 1000f;
    public float StoredWattHours  => _storedJoules / 3600f;
    public bool  IsCharging       => _isCharging;
    public bool  IsDischarging    => !_isCharging && CurrentOutputWatts > 0f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void OnEnable()
    {
        PowerGrid.Instance?.Register((IPowerProducer)this);
        PowerGrid.Instance?.Register((IPowerConsumer)this);
    }

    void OnDisable()
    {
        PowerGrid.Instance?.Unregister((IPowerProducer)this);
        PowerGrid.Instance?.Unregister((IPowerConsumer)this);
    }

    void Update()
    {
        var grid = PowerGrid.Instance;
        if (grid == null) return;

        // Remove our own supply contribution to get the true grid balance from other sources.
        // NetBalanceWatts = TotalSupply - TotalDemand (battery's CurrentOutputWatts is in TotalSupply).
        // Subtracting it gives the balance as if the battery weren't discharging.
        float gridBalance = grid.NetBalanceWatts - CurrentOutputWatts;

        if (gridBalance > 0.1f && _storedJoules < capacityJoules)
        {
            // Surplus on grid → switch to charge mode
            _isCharging        = true;
            CurrentOutputWatts = 0f;

            if (IsPowered)
            {
                float absorb   = Mathf.Min(chargeRateWatts, gridBalance) * Time.deltaTime;
                _storedJoules  = Mathf.Min(capacityJoules, _storedJoules + absorb * roundTripEfficiency);
            }
        }
        else if (gridBalance < -0.1f && _storedJoules > 0f)
        {
            // Deficit on grid → switch to discharge mode
            _isCharging = false;

            float dischargeW   = Mathf.Min(dischargeRateWatts, -gridBalance);
            float maxByStorage = Time.deltaTime > 0f ? _storedJoules / Time.deltaTime : dischargeRateWatts;
            dischargeW         = Mathf.Min(dischargeW, maxByStorage);

            CurrentOutputWatts = dischargeW;
            _storedJoules      = Mathf.Max(0f, _storedJoules - dischargeW * Time.deltaTime);
        }
        else
        {
            _isCharging        = false;
            CurrentOutputWatts = 0f;
        }
    }
}
