using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controls the build-mode selection panel.
/// You build the UI hierarchy yourself; this script just drives it.
///
/// ── REQUIRED HIERARCHY ──────────────────────────────────────────────────────
///
///   Canvas (Screen Space - Overlay)
///   └── BuildPanel  [assign to → panel]
///       ├── TitleText  (TextMeshProUGUI)   [assign to → titleText]
///       ├── StatusText (TextMeshProUGUI)   [assign to → statusText]   ← cost / error feedback
///       └── ScrollView (ScrollRect, horizontal=true, vertical=false)
///           └── Viewport
///               └── Content  (HorizontalLayoutGroup + ContentSizeFitter)  [assign to → buttonContainer]
///
/// ── INSPECTOR SETUP ─────────────────────────────────────────────────────────
///   - panel          : the root BuildPanel GameObject
///   - titleText      : the title label (optional)
///   - statusText     : shows cost / "Not enough materials" (optional)
///   - buttonContainer: the Content transform inside the ScrollRect
///   - catalog        : the BuildingCatalog ScriptableObject
///   - buildingSystem : optional — auto-found at runtime if left empty
///
/// ── RECOMMENDED LAYOUT SETTINGS ─────────────────────────────────────────────
///   BuildPanel:    anchored to bottom, full width, height ~110–140px
///   Content:       HorizontalLayoutGroup, spacing 8, padding 8
///                  ContentSizeFitter → Horizontal: Preferred Size
///   ScrollView:    Horizontal = true, Vertical = false, no scrollbar required
///   Button cards:  ~150–180px wide, preferred height to match panel
///   Font sizes:    title 22, button name 22, sub-info 14–16
/// </summary>
public class BuildModeUI : MonoBehaviour
{
    [Header("UI References — build this hierarchy yourself")]
    [SerializeField] private GameObject      panel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Transform       buttonContainer;

    [Header("Data")]
    [SerializeField] private BuildingCatalog catalog;
    [SerializeField] private BuildingSystem  buildingSystem;

    [Header("Button Style")]
    [SerializeField] private Color buttonNormal   = new Color(0.15f, 0.25f, 0.15f, 1f);
    [SerializeField] private Color buttonSelected = new Color(0.25f, 0.55f, 0.25f, 1f);
    [SerializeField] private Color buttonNoFunds  = new Color(0.35f, 0.15f, 0.15f, 1f);
    [SerializeField] private int   nameFontSize   = 22;
    [SerializeField] private int   infoFontSize   = 14;

    private readonly List<(Button btn, Image img, string name)> _buttons = new();
    private string _selectedName;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Start()
    {
        if (buildingSystem == null)
            buildingSystem = BuildingSystem.Instance;
        if (buildingSystem != null)
        {
            buildingSystem.BuildModeChanged += OnBuildModeChanged;
            buildingSystem.PlacementFailed  += OnPlacementFailed;
        }
        SetVisible(false);
    }

    void OnDestroy()
    {
        if (buildingSystem == null) return;
        buildingSystem.BuildModeChanged -= OnBuildModeChanged;
        buildingSystem.PlacementFailed  -= OnPlacementFailed;
    }

    // ── Events ────────────────────────────────────────────────────────────────

    void OnBuildModeChanged(bool entering)
    {
        if (entering)
        {
            if (buildingSystem == null) buildingSystem = BuildingSystem.Instance;
            PopulateButtons();
            SetStatusText("");
            SetVisible(true);
        }
        else
        {
            SetVisible(false);
            _selectedName = null;
        }
    }

    void OnPlacementFailed(string reason)
    {
        SetStatusText(reason);
    }

    // ── Button population ─────────────────────────────────────────────────────

    void PopulateButtons()
    {
        foreach (var (b, _, _) in _buttons)
            if (b != null) Destroy(b.gameObject);
        _buttons.Clear();

        if (catalog == null || buttonContainer == null) return;

        foreach (var entry in catalog.buildings)
        {
            if (entry?.prefab == null) continue;

            var pb    = entry.prefab;
            var bName = pb.buildingName;

            // ── Button root ───────────────────────────────────────────────────
            var btnGo = new GameObject(bName, typeof(RectTransform));
            btnGo.transform.SetParent(buttonContainer, false);

            var img = btnGo.AddComponent<Image>();
            img.color = buttonNormal;

            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = img;

            // ── Name label ────────────────────────────────────────────────────
            AddTMP(btnGo, "Name", bName,
                new Vector2(0f, 0.5f), new Vector2(1f, 1f),
                nameFontSize, TextAlignmentOptions.MidlineLeft,
                new Vector2(8f, 2f), new Vector2(-8f, -2f));

            // ── Cost + watt info ──────────────────────────────────────────────
            string costLine = BuildCostLine(pb);
            AddTMP(btnGo, "Info", costLine,
                new Vector2(0f, 0f), new Vector2(1f, 0.5f),
                infoFontSize, TextAlignmentOptions.MidlineLeft,
                new Vector2(8f, 2f), new Vector2(-8f, -2f),
                new Color(0.75f, 0.75f, 0.75f));

            string capturedName = bName;
            Image  capturedImg  = img;
            btn.onClick.AddListener(() => OnBuildingSelected(capturedName, capturedImg));
            _buttons.Add((btn, img, bName));
        }

        // Refresh button colors to reflect current affordability
        RefreshButtonColors();
    }

    string BuildCostLine(PlaceableBuilding pb)
    {
        var sb = new System.Text.StringBuilder();
        if (pb.requiredWatts > 0f)
            sb.Append($"{pb.requiredWatts:0}W  ");
        if (pb.buildCost != null)
            foreach (var c in pb.buildCost)
                sb.Append($"| {c.molecule} {(c.massGrams >= 1000f ? $"{c.massGrams / 1000f:F1}kg" : $"{c.massGrams:F0}g")} ");
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "No power · Free";
    }

    void RefreshButtonColors()
    {
        if (buildingSystem == null) return;
        foreach (var (_, img, bName) in _buttons)
        {
            if (img == null) continue;
            var entry = catalog?.FindByName(bName);
            if (entry?.prefab == null) continue;
            bool canAfford = buildingSystem.GetCostDescription(entry.prefab).IndexOf('✗') < 0;
            img.color = bName == _selectedName ? buttonSelected
                      : canAfford             ? buttonNormal
                      :                         buttonNoFunds;
        }
    }

    void OnBuildingSelected(string bName, Image img)
    {
        _selectedName = bName;
        RefreshButtonColors();
        SetStatusText("");
        buildingSystem?.SelectBuilding(bName);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void SetVisible(bool visible)
    {
        if (panel != null) panel.SetActive(visible);
    }

    void SetStatusText(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    static void AddTMP(GameObject parent, string goName, string text,
        Vector2 anchorMin, Vector2 anchorMax,
        int fontSize, TextAlignmentOptions align,
        Vector2 offsetMin, Vector2 offsetMax,
        Color? color = null)
    {
        var go = new GameObject(goName, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.alignment = align;
        if (color.HasValue) tmp.color = color.Value;
    }
}
