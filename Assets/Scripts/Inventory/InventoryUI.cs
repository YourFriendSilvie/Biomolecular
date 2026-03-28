using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Inventory inventory;
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private Transform slotsContainer;
    
    [Header("Slot Prefab")]
    [SerializeField] private GameObject slotPrefab;
    
    [Header("Detail Panel")]
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI itemDescriptionText;
    [SerializeField] private Image itemImage;
    
    [Header("Settings")]
    [SerializeField] private Key toggleKey = Key.Tab;
    
    private InventorySlot[] slotUIElements;
    private bool isOpen = false;

    private void Start()
    {
        // Subscribe to inventory changes
        if (inventory != null)
        {
            inventory.OnInventoryChanged += RefreshUI;
        }

        // Initialize UI slots
        InitializeSlots();
        
        // Start with inventory closed
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }
        
        if (detailPanel != null)
        {
            detailPanel.SetActive(false);
        }
    }

    private void Update()
    {
        // Toggle inventory with key
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            ToggleInventory();
        }
    }

    /// <summary>
    /// Initialize inventory slot UI elements
    /// </summary>
    private void InitializeSlots()
    {
        if (inventory == null || slotsContainer == null)
            return;

        // Clear existing slots
        foreach (Transform child in slotsContainer)
        {
            Destroy(child.gameObject);
        }

        // Create slot UI elements
        slotUIElements = new InventorySlot[inventory.MaxSlots];
        
        for (int i = 0; i < inventory.MaxSlots; i++)
        {
            GameObject slotObj = Instantiate(slotPrefab, slotsContainer);
            InventorySlot slot = slotObj.GetComponent<InventorySlot>();
            
            if (slot != null)
            {
                slot.Initialize(i, this);
                slotUIElements[i] = slot;
            }
        }

        RefreshUI();
    }

    /// <summary>
    /// Refresh all inventory slots
    /// </summary>
    public void RefreshUI()
    {
        if (inventory == null || slotUIElements == null)
            return;

        for (int i = 0; i < slotUIElements.Length; i++)
        {
            InventoryItem item = inventory.GetItemAt(i);
            
            if (slotUIElements[i] != null)
            {
                slotUIElements[i].UpdateSlot(item);
            }
        }
    }

    /// <summary>
    /// Toggle inventory open/closed
    /// </summary>
    public void ToggleInventory()
    {
        isOpen = !isOpen;
        
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(isOpen);
        }

        if (isOpen)
        {
            RefreshUI();
            // Pause game or unlock cursor as needed
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            if (detailPanel != null)
                detailPanel.SetActive(false);
            
            // Resume game or lock cursor as needed
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>
    /// Show item details when slot is selected
    /// </summary>
    public void ShowItemDetails(InventoryItem item)
    {
        if (detailPanel == null || item == null || item.compositionInfo == null)
        {
            if (detailPanel != null)
                detailPanel.SetActive(false);
            return;
        }

        detailPanel.SetActive(true);

        // Update item name
        if (itemNameText != null)
        {
            itemNameText.text = item.ItemName;
        }

        // Update item description (composition breakdown)
        if (itemDescriptionText != null)
        {
            string description = $"Total Mass: {FormatMass(item.totalMass)}\n\n";
            description += "Composition:\n";
            
            foreach (var comp in item.GetComposition())
            {
                float amount = (comp.percentage / 100f) * item.totalMass;
                description += $"• {comp.resource}: {comp.percentage:F1}% ({FormatMass(amount)})\n";
            }
            
            itemDescriptionText.text = description;
        }

        // Update item image
        if (itemImage != null && item.compositionInfo.texture != null)
        {
            Sprite sprite = Sprite.Create(
                item.compositionInfo.texture,
                new Rect(0, 0, item.compositionInfo.texture.width, item.compositionInfo.texture.height),
                new Vector2(0.5f, 0.5f)
            );
            itemImage.sprite = sprite;
            itemImage.enabled = true;
        }
        else if (itemImage != null)
        {
            itemImage.enabled = false;
        }
    }

    /// <summary>
    /// Handle slot click for item interactions
    /// </summary>
    public void OnSlotClicked(int slotIndex)
    {
        InventoryItem item = inventory.GetItemAt(slotIndex);
        ShowItemDetails(item);
    }

    /// <summary>
    /// Use/consume an item from inventory
    /// </summary>
    public void UseItem(int slotIndex)
    {
        InventoryItem item = inventory.GetItemAt(slotIndex);
        
        if (item != null)
        {
            Debug.Log($"Using item: {item.compositionInfo.itemName}");
            // Add your item use logic here
            // For now, just remove one from the stack
            inventory.RemoveItemAt(slotIndex, 1);
        }
    }

    /// <summary>
    /// Drop an item from inventory
    /// </summary>
    public void DropItem(int slotIndex)
    {
        InventoryItem item = inventory.GetItemAt(slotIndex);
        
        if (item != null)
        {
            Debug.Log($"Dropping item: {item.compositionInfo.itemName}");
            // Add drop logic here (spawn item in world, etc.)
            inventory.RemoveItemAt(slotIndex, 1);
        }
    }

    public bool IsOpen => isOpen;

    private void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.OnInventoryChanged -= RefreshUI;
        }
    }

    /// <summary>
    /// Format mass value with appropriate unit (g, kg, or tons)
    /// </summary>
    private string FormatMass(float grams)
    {
        if (grams >= 1000000f) // 1,000,000g = 1 metric ton
        {
            return $"{(grams / 1000000f):F2} tons";
        }
        else if (grams >= 1000f) // 1,000g = 1 kg
        {
            return $"{(grams / 1000f):F2} kg";
        }
        else
        {
            return $"{grams:F2} g";
        }
    }
}
