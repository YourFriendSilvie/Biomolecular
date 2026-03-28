using System;
using System.Collections.Generic;
using UnityEngine;

public static class CompositionInfoRegistry
{
    private static Dictionary<string, CompositionInfo> compositionsByItemName;

    public static bool TryGetByItemName(string itemName, out CompositionInfo compositionInfo)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(itemName))
        {
            compositionInfo = null;
            return false;
        }

        return compositionsByItemName.TryGetValue(itemName, out compositionInfo);
    }

    private static void EnsureLoaded()
    {
        if (compositionsByItemName != null)
        {
            return;
        }

        compositionsByItemName = new Dictionary<string, CompositionInfo>(StringComparer.Ordinal);
        CompositionInfo[] loadedCompositions = Resources.LoadAll<CompositionInfo>(string.Empty);

        foreach (var compositionInfo in loadedCompositions)
        {
            if (compositionInfo == null || string.IsNullOrWhiteSpace(compositionInfo.itemName))
            {
                continue;
            }

            if (compositionsByItemName.ContainsKey(compositionInfo.itemName))
            {
                Debug.LogWarning($"Duplicate CompositionInfo item name found in Resources: {compositionInfo.itemName}");
                continue;
            }

            compositionsByItemName.Add(compositionInfo.itemName, compositionInfo);
        }
    }
}
