using System;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

public class PlayerInteraction : MonoBehaviour
{
    // Prefer a trigger harvestable (e.g. water surface) over a solid harvestable
    // only when the trigger is strictly nearer. A value of 0f means: return solid
    // when solid distance <= trigger distance (solid is at least as close), and
    // return trigger when trigger distance < solid distance (trigger is nearer).
    // Using 4f here caused solid to ALWAYS win within the 3m interaction range,
    // making freshwater triggers permanently unreachable.
    private const float TriggerBypassDistanceMeters = 0f;

    [Header("References")]
    [SerializeField] private Inventory playerInventory;
    [SerializeField] private Camera playerCamera;
    
    [Header("Interaction Settings")]
    [SerializeField] private float interactionRange = 3f;
    [SerializeField] private Key interactKey = Key.E;
    [SerializeField] private LayerMask interactableLayers = -1; // -1 = Everything
    
    [Header("UI Feedback")]
    [SerializeField] private GameObject interactionPrompt;
    [SerializeField] private TMPro.TextMeshProUGUI promptText;
    
    private IHarvestable currentTarget;

    private void Start()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        if (playerInventory == null)
        {
            playerInventory = GetComponent<Inventory>();
        }

        if (interactionPrompt != null)
        {
            interactionPrompt.SetActive(false);
        }
    }

    private void Update()
    {
        // Raycast to find harvestable objects
        CheckForInteractable();

        // Handle interaction input
        if (Keyboard.current != null && Keyboard.current[interactKey].wasPressedThisFrame && currentTarget != null)
        {
            HarvestObject(currentTarget);
        }
    }

    /// <summary>
    /// Check for harvestable objects in front of player
    /// </summary>
    private void CheckForInteractable()
    {
        if (playerCamera == null)
            return;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, interactionRange, interactableLayers, QueryTriggerInteraction.Collide);
        if (hits != null && hits.Length > 0)
        {
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            IHarvestable harvestable = ResolveHarvestable(hits);
            if (harvestable != null)
            {
                currentTarget = harvestable;
                ShowInteractionPrompt(harvestable);
                return;
            }
        }

        // No valid target found
        currentTarget = null;
        HideInteractionPrompt();
    }

    private static IHarvestable ResolveHarvestable(RaycastHit[] hits)
    {
        if (hits == null || hits.Length == 0)
        {
            return null;
        }

        IHarvestable nearestTriggerHarvestable = null;
        float nearestTriggerDistance = float.PositiveInfinity;
        IHarvestable nearestSolidHarvestable = null;
        float nearestSolidDistance = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            IHarvestable candidate = ResolveHarvestable(hits[i]);
            if (candidate == null || hits[i].collider == null)
            {
                continue;
            }

            if (hits[i].collider.isTrigger)
            {
                if (hits[i].distance < nearestTriggerDistance)
                {
                    nearestTriggerHarvestable = candidate;
                    nearestTriggerDistance = hits[i].distance;
                }
            }
            else if (hits[i].distance < nearestSolidDistance)
            {
                nearestSolidHarvestable = candidate;
                nearestSolidDistance = hits[i].distance;
            }
        }

        if (nearestTriggerHarvestable != null && nearestSolidHarvestable != null)
        {
            return nearestSolidDistance <= nearestTriggerDistance + TriggerBypassDistanceMeters
                ? nearestSolidHarvestable
                : nearestTriggerHarvestable;
        }

        return nearestSolidHarvestable ?? nearestTriggerHarvestable;
    }

    private static IHarvestable ResolveHarvestable(RaycastHit hit)
    {
        IHarvestable directHarvestable = FindHarvestable(hit.collider);
        if (directHarvestable != null)
        {
            return directHarvestable;
        }

        return FindRaycastHarvestable(hit);
    }

    /// <summary>
    /// Show interaction prompt UI
    /// </summary>
    private void ShowInteractionPrompt(IHarvestable target)
    {
        if (interactionPrompt == null)
            return;

        interactionPrompt.SetActive(true);
        
        if (promptText != null)
        {
            string itemName = target.GetHarvestDisplayName();
            string prompt = $"Press [{interactKey}] to harvest {itemName}";
            string preview = FormatPreviewForPrompt(itemName, target.GetHarvestPreview());
            if (!string.IsNullOrWhiteSpace(preview))
            {
                prompt += "\n" + preview;
            }

            promptText.text = prompt;
        }
    }

    private static string FormatPreviewForPrompt(string itemName, string preview)
    {
        if (string.IsNullOrWhiteSpace(preview) ||
            string.Equals(preview.Trim(), "Nothing to harvest", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string normalizedPreview = preview.Replace("\r\n", "\n").Trim();
        if (!string.IsNullOrWhiteSpace(itemName) &&
            normalizedPreview.StartsWith(itemName + "\n", StringComparison.Ordinal))
        {
            normalizedPreview = normalizedPreview.Substring(itemName.Length + 1).TrimStart();
        }

        string[] lines = normalizedPreview.Split('\n');
        int maxLines = Mathf.Min(lines.Length, 6);
        for (int i = 0; i < maxLines; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }

        string compactPreview = string.Join("\n", lines, 0, maxLines).Trim();
        if (lines.Length > maxLines)
        {
            compactPreview += "\n...";
        }

        const int maxCharacters = 320;
        if (compactPreview.Length > maxCharacters)
        {
            compactPreview = compactPreview.Substring(0, maxCharacters - 3).TrimEnd() + "...";
        }

        return compactPreview;
    }

    /// <summary>
    /// Hide interaction prompt UI
    /// </summary>
    private void HideInteractionPrompt()
    {
        if (interactionPrompt != null)
        {
            interactionPrompt.SetActive(false);
        }
    }

    /// <summary>
    /// Harvest the target object
    /// </summary>
    private void HarvestObject(IHarvestable target)
    {
        if (playerInventory == null)
        {
            Debug.LogWarning("Player inventory not assigned!");
            return;
        }

        bool success = target.Harvest(playerInventory);
        
        if (success)
        {
            Debug.Log($"Successfully harvested {target.GetHarvestDisplayName()}");
            // Add feedback here (sound, animation, etc.)
        }
    }

    private static IHarvestable FindHarvestable(Collider collider)
    {
        if (collider == null)
        {
            return null;
        }

        return collider.GetComponentsInParent<MonoBehaviour>(true)
            .OfType<IHarvestable>()
            .FirstOrDefault();
    }

    private static IHarvestable FindRaycastHarvestable(RaycastHit hit)
    {
        if (hit.collider == null)
        {
            return null;
        }

        foreach (IRaycastHarvestableProvider provider in hit.collider.GetComponentsInParent<MonoBehaviour>(true).OfType<IRaycastHarvestableProvider>())
        {
            if (provider.TryGetHarvestable(hit, out IHarvestable harvestable) && harvestable != null)
            {
                return harvestable;
            }
        }

        return null;
    }

    // Draw interaction range in editor
    private void OnDrawGizmosSelected()
    {
        if (playerCamera == null)
            return;

        Gizmos.color = Color.yellow;
        Vector3 forward = playerCamera.transform.forward;
        Gizmos.DrawRay(playerCamera.transform.position, forward * interactionRange);
    }
}
