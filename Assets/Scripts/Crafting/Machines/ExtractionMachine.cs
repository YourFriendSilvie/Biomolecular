using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Describes one molecule this machine can extract and what output it produces.
/// </summary>
[Serializable]
public class ExtractionTarget
{
    [Tooltip("Molecule to pull from input storage (e.g. 'Lipid', 'Wood Waxes', 'Iron Oxide').")]
    public string sourceMolecule;

    [Tooltip("The output CompositionInfo produced when this molecule is extracted.")]
    public CompositionInfo outputComposition;

    [Tooltip("Fraction of extracted mass that becomes output (0.9 = 90% yield).")]
    [Range(0.01f, 1f)]
    public float extractionEfficiency = 0.9f;
}

/// <summary>
/// General-purpose molecule extraction machine.
///
/// Eliminates boilerplate for any machine whose job is:
///   "Pull molecule X from mixed input items → produce a purified output item.
///    Leave all other molecules in the input storage untouched."
///
/// Configured entirely in the Inspector — no ProcessingRecipe assets needed.
/// One class covers: OilPress (Lipid → Plant Oil), SolventExtractor (waxes + Ethanol → Plant Oil),
/// or any future extraction step.
///
/// For multi-molecule fractionation (Cellulose + Hemicellulose + Lignin simultaneously),
/// use BiomassFractionator instead — it processes all targets in one batch.
///
/// Behaviour:
///   Each cycle, scans the targets list in order and processes the FIRST target that
///   has enough available mass in input storage (and enough reagent, if configured).
///   Only one target runs per cycle; the machine restarts automatically for the next.
/// </summary>
public class ExtractionMachine : MonoBehaviour, IPowerConsumer
{
    [Header("Storage")]
    [SerializeField] private MachineItemStorage inputStorage;
    [SerializeField] private MachineItemStorage outputStorage;

    [Header("Extraction Targets")]
    [Tooltip("Processed in order — first target with enough available mass runs next.")]
    [SerializeField] private List<ExtractionTarget> targets = new();

    [Header("Reagent (optional)")]
    [Tooltip("Molecule consumed as solvent or catalyst for ALL targets. Leave blank if not needed.\n" +
             "Example: 'Ethanol' for solvent extraction of waxes.")]
    [SerializeField] private string reagentMolecule = "";

    [Tooltip("Grams of reagent consumed per gram of target molecule extracted.\n" +
             "Only active when Reagent Molecule is set.")]
    [Min(0f)]
    [SerializeField] private float reagentGramsPerGramExtracted = 0f;

    [Header("Batch Sizing")]
    [Tooltip("Minimum grams of target molecule required to start a batch.")]
    [SerializeField, Min(0.001f)] private float minGramsToStart = 10f;

    [Tooltip("Maximum grams of target molecule consumed per batch.")]
    [SerializeField, Min(0.001f)] private float batchMaxGrams = 100f;

    [Header("Processing")]
    [SerializeField, Min(0.1f)] private float processingTimeSeconds = 30f;

    [Header("Power")]
    [SerializeField] private float requiredWatts = 200f;
    [SerializeField, Min(0)] private int powerPriority = 5;

    // ── IPowerConsumer ────────────────────────────────────────────────────────
    public float RequiredWatts => _processing ? requiredWatts : 0f;
    public bool  IsPowered     { get; set; }
    public int   PowerPriority => powerPriority;
    public bool  IsActive      => _processing;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private bool            _processing;
    private float           _progress;
    private ExtractionTarget _activeTarget;
    private float           _pendingOutputGrams;

    public float             Progress     => _progress;
    public bool              IsRunning    => _processing;
    public ExtractionTarget  ActiveTarget => _activeTarget;

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
        if (inputStorage == null || targets == null) return;

        bool needsReagent = !string.IsNullOrEmpty(reagentMolecule) && reagentGramsPerGramExtracted > 0f;

        foreach (var target in targets)
        {
            if (string.IsNullOrEmpty(target.sourceMolecule) || target.outputComposition == null)
                continue;

            float available = inputStorage.GetAvailableMassOf(target.sourceMolecule);
            if (available < minGramsToStart) continue;

            float toExtract = Mathf.Min(available, batchMaxGrams);

            // Validate reagent before committing
            if (needsReagent)
            {
                float reagentNeeded = toExtract * reagentGramsPerGramExtracted;
                if (inputStorage.GetAvailableMassOf(reagentMolecule) < reagentNeeded) continue;
                inputStorage.ConsumeMass(reagentMolecule, reagentNeeded);
            }

            inputStorage.ConsumeMass(target.sourceMolecule, toExtract);

            _activeTarget       = target;
            _pendingOutputGrams = toExtract * target.extractionEfficiency;
            _processing         = true;
            _progress           = 0f;
            return;
        }
    }

    void TickBatch(float dt)
    {
        _progress += dt / processingTimeSeconds;
        if (_progress >= 1f) FinishBatch();
    }

    void FinishBatch()
    {
        _processing = false;
        _progress   = 0f;

        if (_activeTarget?.outputComposition == null || _pendingOutputGrams < 0.001f || outputStorage == null)
        {
            _pendingOutputGrams = 0f;
            _activeTarget       = null;
            return;
        }

        var item = new InventoryItem(_activeTarget.outputComposition, 1, _pendingOutputGrams);
        if (!outputStorage.TryInsert(item))
            Debug.LogWarning($"[ExtractionMachine] Output storage full on '{gameObject.name}'; " +
                             $"lost {_pendingOutputGrams:F2}g of {_activeTarget.outputComposition.itemName}.");

        _pendingOutputGrams = 0f;
        _activeTarget       = null;
    }
}
