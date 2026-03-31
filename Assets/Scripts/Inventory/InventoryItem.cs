using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class InventoryItem
{
    public CompositionInfo compositionInfo;
    public int quantity;
    public float totalMass; // in grams or your preferred unit
    public List<Composition> effectiveComposition = new List<Composition>();

    public InventoryItem(CompositionInfo info, int qty = 1, float mass = 0f, IEnumerable<Composition> composition = null)
    {
        compositionInfo = info;
        quantity = qty;
        totalMass = mass;
        effectiveComposition = CompositionInfo.CopyComposition(composition ?? info?.composition);
    }

    /// <summary>
    /// Get the total amount of a specific resource across all items
    /// </summary>
    public float GetResourceAmount(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName) || effectiveComposition == null)
            return 0f;

        foreach (var comp in effectiveComposition)
        {
            if (comp.resource == resourceName)
            {
                return (comp.percentage / 100f) * totalMass;
            }
        }
        return 0f;
    }

    /// <summary>
    /// Check if this item can stack with another
    /// </summary>
    public bool CanStackWith(InventoryItem other)
    {
        return other != null &&
               string.Equals(ItemName, other.ItemName, StringComparison.Ordinal) &&
               CompositionInfo.HasMatchingResourceSet(effectiveComposition, other.effectiveComposition);
    }

    public void AddToStack(int additionalQuantity, float additionalMass, IEnumerable<Composition> addedComposition)
    {
        effectiveComposition = CompositionInfo.BlendCompositionByMass(
            effectiveComposition,
            totalMass,
            addedComposition,
            additionalMass);
        quantity += additionalQuantity;
        totalMass += additionalMass;
    }

    public void RemoveQuantity(int quantityToRemove)
    {
        if (quantityToRemove <= 0 || quantity <= 0)
        {
            return;
        }

        if (quantityToRemove >= quantity)
        {
            quantity = 0;
            totalMass = 0f;
            effectiveComposition.Clear();
            return;
        }

        float averageMassPerItem = totalMass / quantity;
        quantity -= quantityToRemove;
        totalMass = Mathf.Max(0f, totalMass - (averageMassPerItem * quantityToRemove));
    }

    public bool TryExtractResource(string resourceName, float massToExtract)
    {
        if (string.IsNullOrWhiteSpace(resourceName) || massToExtract <= 0f || effectiveComposition == null || effectiveComposition.Count == 0 || totalMass <= 0f)
        {
            return false;
        }

        List<float> componentMasses = new List<float>(effectiveComposition.Count);
        int targetIndex = -1;

        for (int i = 0; i < effectiveComposition.Count; i++)
        {
            float componentMass = (effectiveComposition[i].percentage / 100f) * totalMass;
            componentMasses.Add(componentMass);

            if (targetIndex < 0 && string.Equals(effectiveComposition[i].resource, resourceName, StringComparison.Ordinal))
            {
                targetIndex = i;
            }
        }

        if (targetIndex < 0)
        {
            return false;
        }

        float extractedMass = Mathf.Min(componentMasses[targetIndex], massToExtract);
        if (extractedMass <= 0f)
        {
            return false;
        }

        componentMasses[targetIndex] -= extractedMass;
        totalMass -= extractedMass;

        if (totalMass <= 0f)
        {
            totalMass = 0f;
            effectiveComposition.Clear();
            quantity = 0;
            return true;
        }

        List<Composition> updatedComposition = new List<Composition>();
        for (int i = 0; i < effectiveComposition.Count; i++)
        {
            if (componentMasses[i] <= 0f)
            {
                continue;
            }

            updatedComposition.Add(new Composition
            {
                resource = effectiveComposition[i].resource,
                percentage = (componentMasses[i] / totalMass) * 100f
            });
        }

        effectiveComposition = updatedComposition;
        return true;
    }

    public IReadOnlyList<Composition> GetComposition() => effectiveComposition;

    public string ItemName => compositionInfo != null ? compositionInfo.itemName : string.Empty;
}
