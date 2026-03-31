using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[System.Serializable]
public class Composition
{
    public string resource;
    [Range(0, 100)]
    public float percentage;

    [Header("Random Range (Optional)")]
    [Tooltip("Enable to use random range instead of fixed percentage")]
    public bool useRandomRange = false;
    [Range(0, 100)]
    public float minPercentage = 0f;
    [Range(0, 100)]
    public float maxPercentage = 100f;
}

[CreateAssetMenu(fileName = "New Composition", menuName = "Biomolecular/CompositionInfo")]
public class CompositionInfo : ScriptableObject
{
    [Header("Name and texture")]
    public string itemName = "";
    public Texture2D texture;

    [Header("Resources")]
    [Tooltip("Composition of the item. Each resource listed includes a mass percentage.")]
    public List<Composition> composition = new List<Composition>();

    /// <summary>
    /// Generate random composition percentages that sum to 100%
    /// </summary>
    public List<Composition> GenerateRandomComposition()
    {
        List<Composition> randomComposition = new List<Composition>();

        // Check if any compositions use random ranges
        bool hasRandomRanges = false;
        foreach (var comp in composition)
        {
            if (comp.useRandomRange)
            {
                hasRandomRanges = true;
                break;
            }
        }

        // If no random ranges, return fixed composition
        if (!hasRandomRanges)
        {
            foreach (var comp in composition)
            {
                randomComposition.Add(new Composition
                {
                    resource = comp.resource,
                    percentage = comp.percentage,
                    useRandomRange = false
                });
            }
            return randomComposition;
        }

        // Generate random values within ranges
        float[] randomValues = new float[composition.Count];
        float totalMin = 0f;
        float totalMax = 0f;

        // Calculate total min and max constraints
        for (int i = 0; i < composition.Count; i++)
        {
            if (composition[i].useRandomRange)
            {
                totalMin += composition[i].minPercentage;
                totalMax += composition[i].maxPercentage;
            }
            else
            {
                totalMin += composition[i].percentage;
                totalMax += composition[i].percentage;
            }
        }

        // Initial random generation
        for (int i = 0; i < composition.Count; i++)
        {
            if (composition[i].useRandomRange)
                randomValues[i] = UnityEngine.Random.Range(composition[i].minPercentage, composition[i].maxPercentage);
            else
                randomValues[i] = composition[i].percentage;
        }

        // Normalize to sum to 100%
        float sum = 0f;
        for (int i = 0; i < randomValues.Length; i++)
        {
            sum += randomValues[i];
        }

        // Scale all values proportionally to sum to 100
        if (sum > 0)
        {
            for (int i = 0; i < randomValues.Length; i++)
            {
                randomValues[i] = (randomValues[i] / sum) * 100f;
            }
        }

        // Create result composition list
        for (int i = 0; i < composition.Count; i++)
        {
            randomComposition.Add(new Composition
            {
                resource = composition[i].resource,
                percentage = randomValues[i],
                useRandomRange = false
            });
        }

        return randomComposition;
    }

    /// <summary>
    /// Creates a detached copy of composition data so runtime items can mutate their own
    /// percentages without modifying the shared ScriptableObject asset.
    /// </summary>
    public static List<Composition> CopyComposition(IEnumerable<Composition> sourceComposition)
    {
        List<Composition> clonedComposition = new List<Composition>();

        if (sourceComposition == null)
        {
            return clonedComposition;
        }

        foreach (var comp in sourceComposition)
        {
            if (comp == null || string.IsNullOrWhiteSpace(comp.resource))
            {
                continue;
            }

            clonedComposition.Add(new Composition
            {
                resource = comp.resource,
                percentage = comp.percentage,
                useRandomRange = comp.useRandomRange,
                minPercentage = comp.minPercentage,
                maxPercentage = comp.maxPercentage
            });
        }

        return clonedComposition;
    }

    /// <summary>
    /// Returns true only when two compositions contain the same resources with the same
    /// percentages. Use this when exact composition equality matters.
    /// </summary>
    public static bool HasExactCompositionMatch(
        IEnumerable<Composition> firstComposition,
        IEnumerable<Composition> secondComposition,
        float percentageTolerance = 0.001f)
    {
        var normalizedFirst = GetNormalizedComposition(firstComposition);
        var normalizedSecond = GetNormalizedComposition(secondComposition);

        if (normalizedFirst.Count != normalizedSecond.Count)
        {
            return false;
        }

        for (int i = 0; i < normalizedFirst.Count; i++)
        {
            if (!string.Equals(normalizedFirst[i].resource, normalizedSecond[i].resource, StringComparison.Ordinal))
            {
                return false;
            }

            if (Mathf.Abs(normalizedFirst[i].percentage - normalizedSecond[i].percentage) > percentageTolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns true when two compositions contain the same resource names, ignoring the
    /// actual percentages. This is the stacking rule for material families.
    /// </summary>
    public static bool HasMatchingResourceSet(
        IEnumerable<Composition> firstComposition,
        IEnumerable<Composition> secondComposition)
    {
        var firstResources = GetNormalizedComposition(firstComposition)
            .Select(comp => comp.resource)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var secondResources = GetNormalizedComposition(secondComposition)
            .Select(comp => comp.resource)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return firstResources.SequenceEqual(secondResources, StringComparer.Ordinal);
    }

    /// <summary>
    /// Blends two compositions together using their masses so the result reflects the
    /// actual combined material percentages after stacking.
    /// </summary>
    public static List<Composition> BlendCompositionByMass(
        IEnumerable<Composition> firstComposition,
        float firstMass,
        IEnumerable<Composition> secondComposition,
        float secondMass)
    {
        float combinedMass = Mathf.Max(0f, firstMass) + Mathf.Max(0f, secondMass);
        if (combinedMass <= 0f)
        {
            return new List<Composition>();
        }

        Dictionary<string, float> resourceMasses = new Dictionary<string, float>(StringComparer.Ordinal);
        AccumulateResourceMasses(resourceMasses, firstComposition, Mathf.Max(0f, firstMass));
        AccumulateResourceMasses(resourceMasses, secondComposition, Mathf.Max(0f, secondMass));

        return resourceMasses
            .Where(pair => pair.Value > 0f)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new Composition
            {
                resource = pair.Key,
                percentage = (pair.Value / combinedMass) * 100f
            })
            .ToList();
    }

    /// <summary>
    /// Legacy asset-to-asset comparison: same item name and exact composition match.
    /// Useful for comparing two base CompositionInfo assets directly.
    /// </summary>
    public bool HasExactCompositionMatch(CompositionInfo otherCompositionInfo)
    {
        if (otherCompositionInfo == null)
        {
            return false;
        }

        return string.Equals(itemName, otherCompositionInfo.itemName, StringComparison.Ordinal) &&
               HasExactCompositionMatch(composition, otherCompositionInfo.composition);
    }

    /// <summary>
    /// Produces a cleaned, deterministic ordering for comparisons by removing invalid or
    /// zero-percent entries and sorting by resource name.
    /// </summary>
    private static List<Composition> GetNormalizedComposition(IEnumerable<Composition> sourceComposition)
    {
        return CopyComposition(sourceComposition)
            .Where(comp => comp != null && !string.IsNullOrWhiteSpace(comp.resource) && comp.percentage > 0f)
            .OrderBy(comp => comp.resource, StringComparer.Ordinal)
            .ThenBy(comp => comp.percentage)
            .ToList();
    }

    /// <summary>
    /// Converts percentages into absolute mass contributions and accumulates them into the
    /// provided dictionary. Used internally by BlendCompositionByMass.
    /// </summary>
    private static void AccumulateResourceMasses(
        IDictionary<string, float> resourceMasses,
        IEnumerable<Composition> sourceComposition,
        float sourceMass)
    {
        if (sourceMass <= 0f)
        {
            return;
        }

        foreach (var comp in GetNormalizedComposition(sourceComposition))
        {
            float resourceMass = (comp.percentage / 100f) * sourceMass;

            if (resourceMasses.ContainsKey(comp.resource))
            {
                resourceMasses[comp.resource] += resourceMass;
            }
            else
            {
                resourceMasses[comp.resource] = resourceMass;
            }
        }
    }
}
