using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class InventorySlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI References")]
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private Image backgroundImage;
    
    [Header("Visual Settings")]
    [SerializeField] private Color normalColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
    [SerializeField] private Color highlightColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color emptyColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
    
    private int slotIndex;
    private InventoryUI inventoryUI;
    private InventoryItem currentItem;

    /// <summary>
    /// Initialize the slot with its index
    /// </summary>
    public void Initialize(int index, InventoryUI ui)
    {
        slotIndex = index;
        inventoryUI = ui;
        
        if (backgroundImage != null)
        {
            backgroundImage.color = emptyColor;
        }
    }

    /// <summary>
    /// Update slot display with item data
    /// </summary>
    public void UpdateSlot(InventoryItem item)
    {
        currentItem = item;

        if (item == null || item.compositionInfo == null)
        {
            // Empty slot
            if (itemIcon != null)
                itemIcon.enabled = false;
                
            if (quantityText != null)
                quantityText.text = "";
                
            if (backgroundImage != null)
                backgroundImage.color = emptyColor;
        }
        else
        {
            // Slot has item
            if (backgroundImage != null)
                backgroundImage.color = normalColor;

            // Set item icon
            if (itemIcon != null)
            {
                if (item.compositionInfo.texture != null)
                {
                    Sprite sprite = Sprite.Create(
                        item.compositionInfo.texture,
                        new Rect(0, 0, item.compositionInfo.texture.width, item.compositionInfo.texture.height),
                        new Vector2(0.5f, 0.5f)
                    );
                    itemIcon.sprite = sprite;
                    itemIcon.enabled = true;
                }
                else
                {
                    itemIcon.enabled = false;
                }
            }

            // Set quantity text
            if (quantityText != null)
            {
                // Just show mass with appropriate unit
                quantityText.text = FormatMass(item.totalMass);
            }
        }
    }

    /// <summary>
    /// Format mass value with appropriate unit (g, kg, or tons)
    /// </summary>
    private string FormatMass(float grams)
    {
        if (grams >= 1000000f) // 1,000,000g = 1 metric ton
        {
            return $"{(grams / 1000000f):F2}t";
        }
        else if (grams >= 1000f) // 1,000g = 1 kg
        {
            return $"{(grams / 1000f):F1}kg";
        }
        else
        {
            return $"{grams:F0}g";
        }
    }

    /// <summary>
    /// Handle pointer click
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (inventoryUI != null)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                // Left click - select/view details
                inventoryUI.OnSlotClicked(slotIndex);
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                // Right click - use item
                if (currentItem != null)
                {
                    inventoryUI.UseItem(slotIndex);
                }
            }
        }
    }

    /// <summary>
    /// Handle pointer enter (hover)
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (backgroundImage != null && currentItem != null)
        {
            backgroundImage.color = highlightColor;
        }
    }

    /// <summary>
    /// Handle pointer exit (hover end)
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        if (backgroundImage != null)
        {
            if (currentItem != null)
            {
                backgroundImage.color = normalColor;
            }
            else
            {
                backgroundImage.color = emptyColor;
            }
        }
    }

    public int SlotIndex => slotIndex;
    public InventoryItem CurrentItem => currentItem;
}
