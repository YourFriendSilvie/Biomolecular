using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panel UI for interacting with a ProcessingMachine (view input/output, deposit, collect).
/// ONE instance lives in the scene; MachineInteractionTrigger finds it via Instance.
///
/// ── REQUIRED HIERARCHY ──────────────────────────────────────────────────────
///
///   Canvas (Screen Space - Overlay)
///   └── MachinePanel  [assign to → panel]
///       ├── TitleText        (TextMeshProUGUI)  [assign to → titleText]
///       ├── StatusText       (TextMeshProUGUI)  [assign to → statusText]
///       ├── InputLabel       (TextMeshProUGUI)  [assign to → inputLabel]   ← e.g. "Input"
///       ├── InputSlotsParent (Transform)        [assign to → inputSlotContainer]
///       ├── OutputLabel      (TextMeshProUGUI)  [assign to → outputLabel]  ← e.g. "Output"
///       ├── OutputSlotsParent(Transform)        [assign to → outputSlotContainer]
///       ├── DepositAllButton (Button)            [assign to → depositAllButton]
///       ├── CollectAllButton (Button)            [assign to → collectAllButton]
///       └── CloseButton      (Button)            [assign to → closeButton]
///
/// ── SETUP NOTES ─────────────────────────────────────────────────────────────
///   - Slot containers need a VerticalLayoutGroup (or Grid).
///   - Font size 18–22 works well for slot lines.
///   - Panel starts hidden; this script toggles it.
///   - Add ONE MachineInteractionUI component anywhere in the scene.
/// </summary>
public class MachineInteractionUI : MonoBehaviour
{
    public static MachineInteractionUI Instance { get; private set; }

    [Header("Panel root")]
    [SerializeField] private GameObject panel;

    [Header("Labels")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI inputLabel;
    [SerializeField] private TextMeshProUGUI outputLabel;

    [Header("Slot containers — children are auto-created text lines")]
    [SerializeField] private Transform inputSlotContainer;
    [SerializeField] private Transform outputSlotContainer;

    [Header("Action buttons")]
    [SerializeField] private Button depositAllButton;
    [SerializeField] private Button collectAllButton;
    [SerializeField] private Button closeButton;

    [Header("Slot style")]
    [SerializeField] private int slotFontSize = 18;

    private ProcessingMachine _machine;
    private Inventory         _playerInventory;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    void Start()
    {
        if (closeButton      != null) closeButton.onClick.AddListener(Close);
        if (depositAllButton != null) depositAllButton.onClick.AddListener(DepositAll);
        if (collectAllButton != null) collectAllButton.onClick.AddListener(CollectAll);

        if (inputLabel  != null) inputLabel.text  = "Input";
        if (outputLabel != null) outputLabel.text = "Output";
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public bool IsOpen(ProcessingMachine machine) => panel != null && panel.activeSelf && _machine == machine;

    public void Open(ProcessingMachine machine, Inventory playerInventory)
    {
        _machine         = machine;
        _playerInventory = playerInventory;
        Refresh();
        if (panel != null) panel.SetActive(true);
    }

    public void Close()
    {
        if (panel != null) panel.SetActive(false);
        _machine         = null;
        _playerInventory = null;
    }

    // ── Refresh display ────────────────────────────────────────────────────────

    public void Refresh()
    {
        if (_machine == null) return;

        if (titleText  != null) titleText.text  = _machine.gameObject.name;
        if (statusText != null) statusText.text = _machine.IsProcessing
            ? $"Processing… ({_machine.Progress:P0})"
            : _machine.IsPowered
                ? "Idle — waiting for input"
                : "Unpowered";

        RefreshSlots(inputSlotContainer,  _machine.InputStorage);
        RefreshSlots(outputSlotContainer, _machine.OutputStorage);
    }

    void RefreshSlots(Transform container, MachineItemStorage storage)
    {
        if (container == null) return;

        // Clear old lines
        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);

        if (storage == null)
        {
            MakeSlotLine(container, "(no storage)");
            return;
        }

        var items = storage.GetItems();
        bool any = false;
        foreach (var item in items)
        {
            any = true;
            string label = $"{item.ItemName}  {item.totalMass / 1000f:F2} kg";
            MakeSlotLine(container, label);
        }
        if (!any)
            MakeSlotLine(container, "(empty)");
    }

    void MakeSlotLine(Transform parent, string text)
    {
        var go  = new GameObject("Slot", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text     = text;
        tmp.fontSize = slotFontSize;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
    }

    // ── Deposit / Collect ──────────────────────────────────────────────────────

    void DepositAll()
    {
        if (_machine == null || _playerInventory == null) return;
        var storage = _machine.InputStorage;
        if (storage == null) return;

        int moved = 0;
        // Iterate in reverse so index removal stays safe
        var items = _playerInventory.Items;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (storage.TryInsert(item))
            {
                _playerInventory.RemoveItemAt(i);
                moved++;
            }
        }

        SetStatus(moved > 0 ? $"Deposited {moved} item(s)" : "Input storage full");
        Refresh();
    }

    void CollectAll()
    {
        if (_machine == null || _playerInventory == null) return;
        int collected = _machine.OutputStorage.TransferAllTo(_playerInventory);
        SetStatus(collected > 0 ? $"Collected {collected} item(s)" : "Output is empty");
        Refresh();
    }

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }
}
