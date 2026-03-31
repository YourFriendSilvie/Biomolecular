using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Organosolv/hydrothermal biomass fractionator.
///
/// Real-world basis (Organosolv):
///   Ethanol-water solvent (60:40) at 160-200°C, 10-20 bar, 90 min residence time.
///   Dissolves lignin and hemicellulose; cellulose fiber structure is preserved.
///   Yield per 100g dry wood: ~45g cellulose + ~18g hemicellulose + ~25g lignin + ~12g extractive loss.
///
/// Game model:
///   Scans inputStorage for items containing Cellulose/Hemicellulose/Lignin.
///   Consumes up to batchMassGrams of each target molecule (proportionally capped).
///   Produces separate output items for each fraction.
///   Water and extractives (Tannin, Waxes, etc.) are removed during fractionation and not output.
///
/// This enables the automation dream: dump raw wood into a hopper → fractionator pulls
/// all three structural molecules → separate streams go to fermentation, acid hydrolysis,
/// and pyrolysis reactors.
/// </summary>
public class BiomassFractionator : MonoBehaviour, IPowerConsumer
{
    [Header("Storage")]
    [SerializeField] private MachineItemStorage inputStorage;
    [SerializeField] private MachineItemStorage outputStorage;

    [Header("Output Compositions")]
    [Tooltip("CompositionInfo for the cellulose fraction output item.")]
    [SerializeField] private CompositionInfo celluloseOutput;
    [Tooltip("CompositionInfo for the hemicellulose fraction output item.")]
    [SerializeField] private CompositionInfo hemicelluloseOutput;
    [Tooltip("CompositionInfo for the lignin fraction output item.")]
    [SerializeField] private CompositionInfo ligninOutput;

    [Header("Processing")]
    [SerializeField, Min(1f)]
    [Tooltip("Maximum total grams of all fractions to process per cycle.")]
    private float batchMassGrams = 100f;

    [SerializeField, Min(0.1f)]
    [Tooltip("Minimum grams of Cellulose required to start a batch.")]
    private float minCellulosToStart = 10f;

    [SerializeField, Min(1f)]
    private float processingTimeSeconds = 45f;

    [Header("Power")]
    [SerializeField] private float requiredWatts = 800f;
    [SerializeField, Min(0)] private int powerPriority = 6;

    // ── IPowerConsumer ────────────────────────────────────────────────────────
    public float RequiredWatts => _processing ? requiredWatts : 0f;
    public bool  IsPowered     { get; set; }
    public int   PowerPriority => powerPriority;
    public bool  IsActive      => _processing;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private bool  _processing;
    private float _progress;
    private float _pendingCellulose;
    private float _pendingHemicellulose;
    private float _pendingLignin;

    public float Progress    => _progress;
    public bool  IsRunning   => _processing;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void OnEnable()  => PowerGrid.Instance?.Register(this);
    void OnDisable() => PowerGrid.Instance?.Unregister(this);

    void Update()
    {
        if (!_processing)
            TryStartBatch();

        if (_processing && IsPowered)
            TickBatch(Time.deltaTime);
    }

    // ── Batch logic ───────────────────────────────────────────────────────────

    void TryStartBatch()
    {
        if (inputStorage == null) return;
        if (inputStorage.GetAvailableMassOf("Cellulose") < minCellulosToStart) return;

        float availCellulose  = inputStorage.GetAvailableMassOf("Cellulose");
        float availHemi       = inputStorage.GetAvailableMassOf("Hemicellulose");
        float availLignin     = inputStorage.GetAvailableMassOf("Lignin");
        float totalAvail      = availCellulose + availHemi + availLignin;

        if (totalAvail <= 0f) return;

        // Cap to batch size, preserving ratio
        float scale = Mathf.Min(1f, batchMassGrams / totalAvail);
        _pendingCellulose     = availCellulose * scale;
        _pendingHemicellulose = availHemi      * scale;
        _pendingLignin        = availLignin    * scale;

        // Consume inputs immediately (locked in batch, even if power cuts out mid-process)
        inputStorage.ConsumeMass("Cellulose",     _pendingCellulose);
        inputStorage.ConsumeMass("Hemicellulose", _pendingHemicellulose);
        inputStorage.ConsumeMass("Lignin",        _pendingLignin);

        _processing = true;
        _progress   = 0f;
    }

    void TickBatch(float dt)
    {
        _progress += dt / processingTimeSeconds;
        if (_progress >= 1f)
            FinishBatch();
    }

    void FinishBatch()
    {
        _processing = false;
        _progress   = 0f;

        TryOutput(celluloseOutput,     _pendingCellulose,     "Cellulose Pulp");
        TryOutput(hemicelluloseOutput, _pendingHemicellulose, "Hemicellulose Fraction");
        TryOutput(ligninOutput,        _pendingLignin,        "Lignin Fraction");
    }

    void TryOutput(CompositionInfo comp, float massGrams, string label)
    {
        if (comp == null || massGrams < 0.001f || outputStorage == null) return;
        var item = new InventoryItem(comp, 1, massGrams);
        if (!outputStorage.TryInsert(item))
            Debug.LogWarning($"[BiomassFractionator] Output storage full; lost {massGrams:F2}g of {label}.");
    }
}
