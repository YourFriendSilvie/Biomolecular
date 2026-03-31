using UnityEngine;

/// <summary>
/// Base class for all processing machines (crusher, smelter, kiln, etc.).
///
/// Behaviour:
///   - Each Update, if idle and autoProcess=true, scans availableRecipes for one whose
///     inputs are satisfied; starts the first match.
///   - While a recipe is active, ticks progress if powered (or if requiredWatts == 0).
///   - On completion, consumes inputs and deposits the output item into outputStorage.
///   - Registers/unregisters with PowerGrid as an IPowerConsumer automatically.
///
/// To create a new machine type:
///   1. Subclass ProcessingMachine.
///   2. Set machineType, requiredWatts, powerPriority defaults via Reset().
///   3. Optionally override OnRecipeStarted / OnRecipeCompleted for custom effects.
/// </summary>
public abstract class ProcessingMachine : MonoBehaviour, IPowerConsumer
{
    [Header("Machine Identity")]
    [SerializeField] protected MachineType machineType = MachineType.Any;

    [Header("Recipes")]
    [SerializeField] protected ProcessingRecipe[] availableRecipes;

    [Header("Storage")]
    [Tooltip("Items fed into this machine.")]
    [SerializeField] protected MachineItemStorage inputStorage;
    [Tooltip("Output items wait here until collected.")]
    [SerializeField] protected MachineItemStorage outputStorage;

    [Header("Power")]
    [SerializeField] protected float requiredWatts = 500f;
    [SerializeField, Min(0)] protected int powerPriority = 5;

    [Header("Behaviour")]
    [Tooltip("When true, the machine automatically starts processing whenever inputs are available.")]
    [SerializeField] protected bool autoProcess = true;

    // ── Runtime state ─────────────────────────────────────────────────────────
    protected ProcessingRecipe _activeRecipe;
    protected float            _progress; // 0–1

    // ── IPowerConsumer ────────────────────────────────────────────────────────
    public float RequiredWatts => IsActive ? requiredWatts : 0f;
    public bool  IsPowered     { get; set; }
    public int   PowerPriority => powerPriority;
    public bool  IsActive      => _activeRecipe != null;

    // ── Read-only state for UI ────────────────────────────────────────────────
    public float            Progress    => _progress;
    public ProcessingRecipe ActiveRecipe => _activeRecipe;
    public bool             IsProcessing => _activeRecipe != null && (IsPowered || requiredWatts <= 0f);
    public MachineItemStorage InputStorage  => inputStorage;
    public MachineItemStorage OutputStorage => outputStorage;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    protected virtual void OnEnable()  => PowerGrid.Instance?.Register(this);
    protected virtual void OnDisable() => PowerGrid.Instance?.Unregister(this);

    protected virtual void Update()
    {
        if (autoProcess && _activeRecipe == null)
            TryStartNextRecipe();

        if (_activeRecipe != null && (IsPowered || _activeRecipe.requiredWatts <= 0f))
            TickProcessing(Time.deltaTime);
    }

    // ── Processing pipeline ───────────────────────────────────────────────────

    protected void TryStartNextRecipe()
    {
        if (inputStorage == null || availableRecipes == null) return;

        foreach (var recipe in availableRecipes)
        {
            if (recipe == null) continue;
            if (recipe.requiredMachine != machineType && recipe.requiredMachine != MachineType.Any) continue;
            if (!recipe.CanProcess(inputStorage)) continue;

            StartRecipe(recipe);
            return;
        }
    }

    protected void StartRecipe(ProcessingRecipe recipe)
    {
        _activeRecipe = recipe;
        _progress     = 0f;
        OnRecipeStarted(recipe);
    }

    protected void TickProcessing(float dt)
    {
        _progress += dt / _activeRecipe.processingTimeSeconds;
        if (_progress >= 1f)
            FinishRecipe();
    }

    protected void FinishRecipe()
    {
        var recipe    = _activeRecipe;
        _activeRecipe = null;
        _progress     = 0f;

        if (!recipe.ConsumeInputs(inputStorage)) return;

        if (outputStorage != null && recipe.outputs != null)
        {
            foreach (var output in recipe.outputs)
            {
                if (output.composition == null) continue;
                float mass = output.massGrams * output.yieldFraction;
                if (mass <= 0f) continue;
                var item = new InventoryItem(output.composition, 1, mass);
                if (!outputStorage.TryInsert(item))
                    Debug.LogWarning($"[ProcessingMachine] Output storage full on '{gameObject.name}' " +
                                     $"after recipe '{recipe.recipeName}' (output: {output.composition.itemName}).");
            }
        }

        OnRecipeCompleted(recipe);
    }

    // ── Overridable hooks ─────────────────────────────────────────────────────
    protected virtual void OnRecipeStarted(ProcessingRecipe recipe)   { }
    protected virtual void OnRecipeCompleted(ProcessingRecipe recipe) { }
}
