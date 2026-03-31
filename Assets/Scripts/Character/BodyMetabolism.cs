using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

public class BodyMetabolism : MonoBehaviour
{
    [Header("Body Archetype")]
    [SerializeField] private CharacterBody body;

    [Header("References")]
    [SerializeField] private Inventory inventory;
    [Tooltip("Reads intentional move/sprint input to determine drain rate. " +
             "Knockback, falling, and external forces never affect drain.")]
    [SerializeField] private StarterAssetsInputs starterInput;

    [Header("Consume Key")]
    [SerializeField] private Key consumeKey = Key.F;

    [Header("Caloric Drain (kcal/s)")]
    [SerializeField] private float caloricDrainResting = 0.3f;
    [SerializeField] private float caloricDrainWalking  = 0.6f;
    [SerializeField] private float caloricDrainRunning  = 1.5f;

    [Header("Hydration Drain (g/s)")]
    [SerializeField] private float hydrationDrainResting = 0.15f;
    [SerializeField] private float hydrationDrainWalking  = 0.4f;
    [SerializeField] private float hydrationDrainRunning  = 1.2f;

    [Header("Consume Portions")]
    [SerializeField] private float consumePortionCalories = 250f;
    [SerializeField] private float consumePortionWater    = 200f;

    // Runtime state
    private float _caloricReserve;
    private float _hydrationReserve;
    private CharacterActivity _currentActivity;
    private bool _isDepleted;

    // Events
    public event Action<float, float> CaloricChanged;
    public event Action<float, float> HydrationChanged;
    public event Action OnCaloricDepleted;
    public event Action OnHydrationDepleted;
    public event Action OnRevived;

    // Public properties
    public float CaloricFraction     => body != null ? _caloricReserve   / body.maxCaloricReserveKcal    : 0f;
    public float HydrationFraction   => body != null ? _hydrationReserve / body.maxHydrationReserveGrams : 0f;
    public float CaloricReserve      => _caloricReserve;
    public float HydrationReserve    => _hydrationReserve;
    public float MaxCaloricReserve   => body != null ? body.maxCaloricReserveKcal    : 1f;
    public float MaxHydrationReserve => body != null ? body.maxHydrationReserveGrams : 1f;
    public bool  IsDepleted          => _isDepleted;

    // Caloric value lookup (kcal per gram)
    private static readonly Dictionary<string, float> s_CaloriesPerGram = new()
    {
        { "Glucose",      4f }, { "Sucrose",      4f }, { "Fructose", 4f }, { "Starch",    4f },
        { "Fat",          9f }, { "Lipid",        9f }, { "Protein",  4f }, { "Amino Acids", 4f },
        { "Cellulose",    0f }, { "Lignin",       0f }, { "Tannin",   0f }, { "Water",     0f }
    };

    private void Awake()
    {
        _caloricReserve   = body.maxCaloricReserveKcal    * body.startingCaloricFraction;
        _hydrationReserve = body.maxHydrationReserveGrams * body.startingHydrationFraction;
    }

    private void Update()
    {
        if (_isDepleted) return;

        InferActivityFromInput();
        DrainReserves();

        if (Keyboard.current != null && Keyboard.current[consumeKey].wasPressedThisFrame)
            TryConsume();
    }

    // Infers activity from intentional input only — knockback/falling never inflate drain.
    private void InferActivityFromInput()
    {
        if (starterInput == null) return;

        bool isMoving = starterInput.move != Vector2.zero;

        if (!isMoving)
            _currentActivity = CharacterActivity.Resting;
        else if (starterInput.sprint)
            _currentActivity = CharacterActivity.Running;
        else
            _currentActivity = CharacterActivity.Walking;
    }

    private void DrainReserves()
    {
        float caloricDrain   = GetCaloricDrainRate()   * Time.deltaTime;
        float hydrationDrain = GetHydrationDrainRate() * Time.deltaTime;

        bool caloricWasPositive   = _caloricReserve   > 0f;
        bool hydrationWasPositive = _hydrationReserve > 0f;

        _caloricReserve   = Mathf.Max(0f, _caloricReserve   - caloricDrain);
        _hydrationReserve = Mathf.Max(0f, _hydrationReserve - hydrationDrain);

        CaloricChanged?.Invoke(_caloricReserve,   body.maxCaloricReserveKcal);
        HydrationChanged?.Invoke(_hydrationReserve, body.maxHydrationReserveGrams);

        if (caloricWasPositive && _caloricReserve <= 0f && !_isDepleted)
        {
            _isDepleted = true;
            OnCaloricDepleted?.Invoke();
        }

        if (hydrationWasPositive && _hydrationReserve <= 0f && !_isDepleted)
        {
            _isDepleted = true;
            OnHydrationDepleted?.Invoke();
        }
    }

    private void TryConsume()
    {
        if (HydrationFraction <= CaloricFraction)
        {
            if (!TryDrink()) TryEat();
        }
        else
        {
            if (!TryEat()) TryDrink();
        }
    }

    private bool TryDrink()
    {
        if (inventory == null) return false;

        int   bestSlot  = -1;
        float bestWater = 0f;

        for (int i = 0; i < inventory.MaxSlots; i++)
        {
            InventoryItem item = inventory.GetItemAt(i);
            if (item == null) continue;
            float w = item.GetResourceAmount("Water");
            if (w > bestWater) { bestWater = w; bestSlot = i; }
        }

        if (bestSlot < 0 || bestWater <= 0f) return false;

        InventoryItem best      = inventory.GetItemAt(bestSlot);
        float         toExtract = Mathf.Min(bestWater, consumePortionWater);
        best.TryExtractResource("Water", toExtract);

        _hydrationReserve = Mathf.Clamp(
            _hydrationReserve + toExtract, 0f, body.maxHydrationReserveGrams);

        if (best.totalMass <= 0.01f)
            inventory.RemoveItemAt(bestSlot, best.quantity);

        HydrationChanged?.Invoke(_hydrationReserve, body.maxHydrationReserveGrams);
        CheckRevival();
        return true;
    }

    private bool TryEat()
    {
        if (inventory == null) return false;

        int   bestSlot     = -1;
        float bestCalories = 0f;

        for (int i = 0; i < inventory.MaxSlots; i++)
        {
            InventoryItem item = inventory.GetItemAt(i);
            if (item == null || item.quantity <= 0) continue;
            float cal = GetItemCalories(item);
            if (cal > bestCalories) { bestCalories = cal; bestSlot = i; }
        }

        if (bestSlot < 0 || bestCalories <= 0f) return false;

        InventoryItem bestItem = inventory.GetItemAt(bestSlot);
        float calsPerUnit = GetItemCalories(bestItem) / bestItem.quantity;
        float calsToAdd   = Mathf.Min(calsPerUnit, consumePortionCalories);

        inventory.RemoveItemAt(bestSlot, 1);

        _caloricReserve = Mathf.Clamp(
            _caloricReserve + calsToAdd, 0f, body.maxCaloricReserveKcal);

        CaloricChanged?.Invoke(_caloricReserve, body.maxCaloricReserveKcal);
        CheckRevival();
        return true;
    }

    private float GetItemCalories(InventoryItem item)
    {
        if (item == null || item.totalMass <= 0f) return 0f;
        float total = 0f;
        foreach (var comp in item.GetComposition())
            if (s_CaloriesPerGram.TryGetValue(comp.resource, out float kcalPerGram))
                total += (comp.percentage / 100f) * item.totalMass * kcalPerGram;
        return total;
    }

    private float GetCaloricDrainRate() => _currentActivity switch
    {
        CharacterActivity.Running  => caloricDrainRunning,
        CharacterActivity.Walking  => caloricDrainWalking,
        CharacterActivity.Wading   => caloricDrainWalking,
        CharacterActivity.Airborne => caloricDrainWalking,
        _                          => caloricDrainResting
    };

    private float GetHydrationDrainRate() => _currentActivity switch
    {
        CharacterActivity.Running  => hydrationDrainRunning,
        CharacterActivity.Walking  => hydrationDrainWalking,
        CharacterActivity.Wading   => hydrationDrainWalking,
        CharacterActivity.Airborne => hydrationDrainWalking,
        _                          => hydrationDrainResting
    };

    public void AddCalories(float kcal)
    {
        if (kcal <= 0f) return;
        _caloricReserve = Mathf.Clamp(_caloricReserve + kcal, 0f, body.maxCaloricReserveKcal);
        CaloricChanged?.Invoke(_caloricReserve, body.maxCaloricReserveKcal);
        CheckRevival();
    }

    public void AddHydration(float grams)
    {
        if (grams <= 0f) return;
        _hydrationReserve = Mathf.Clamp(_hydrationReserve + grams, 0f, body.maxHydrationReserveGrams);
        HydrationChanged?.Invoke(_hydrationReserve, body.maxHydrationReserveGrams);
        CheckRevival();
    }

    private void CheckRevival()
    {
        if (_isDepleted && _caloricReserve > 0f && _hydrationReserve > 0f)
        {
            _isDepleted = false;
            OnRevived?.Invoke();
        }
    }
}
