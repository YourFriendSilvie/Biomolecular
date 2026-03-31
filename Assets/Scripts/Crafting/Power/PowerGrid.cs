using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton MonoBehaviour that manages the global electrical grid.
/// Producers register their output; consumers register their demand.
/// Each frame the grid totals supply, totals demand, and assigns IsPowered
/// to consumers in priority order — lowest PowerPriority value served first.
/// When supply is insufficient the lowest-priority consumers are cut.
///
/// Place one PowerGrid component on a persistent Manager GameObject in each scene.
/// </summary>
public class PowerGrid : MonoBehaviour
{
    public static PowerGrid Instance { get; private set; }

    private readonly List<IPowerProducer> _producers = new();
    private readonly List<IPowerConsumer> _consumers = new();

    // ── Public read-only grid state ───────────────────────────────────────────
    public float TotalSupplyWatts  { get; private set; }
    public float TotalDemandWatts  { get; private set; }
    public float NetBalanceWatts   => TotalSupplyWatts - TotalDemandWatts;
    public bool  IsOverloaded      => TotalDemandWatts > TotalSupplyWatts;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Registration ──────────────────────────────────────────────────────────
    public void Register(IPowerProducer producer)
    {
        if (producer != null && !_producers.Contains(producer))
            _producers.Add(producer);
    }

    public void Register(IPowerConsumer consumer)
    {
        if (consumer != null && !_consumers.Contains(consumer))
            _consumers.Add(consumer);
    }

    public void Unregister(IPowerProducer producer) => _producers.Remove(producer);
    public void Unregister(IPowerConsumer consumer) => _consumers.Remove(consumer);

    // ── Grid simulation ───────────────────────────────────────────────────────
    void Update()
    {
        // 1. Sum total available supply
        TotalSupplyWatts = 0f;
        foreach (var p in _producers)
            TotalSupplyWatts += p.CurrentOutputWatts;

        // 2. Sum active demand (only machines currently trying to run)
        TotalDemandWatts = 0f;
        foreach (var c in _consumers)
            if (c.IsActive) TotalDemandWatts += c.RequiredWatts;

        // 3. Distribute: serve highest-priority consumers first
        float remaining = TotalSupplyWatts;
        _consumers.Sort((a, b) => a.PowerPriority.CompareTo(b.PowerPriority));

        foreach (var c in _consumers)
        {
            if (!c.IsActive)
            {
                c.IsPowered = false;
                continue;
            }

            if (remaining >= c.RequiredWatts)
            {
                c.IsPowered = true;
                remaining  -= c.RequiredWatts;
            }
            else
            {
                c.IsPowered = false;
            }
        }
    }
}
