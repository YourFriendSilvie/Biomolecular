using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders caloric and hydration reserves as segmented draining bars with numeric labels.
///
/// SCENE SETUP
/// -----------
/// 1. Canvas (Screen Space – Overlay, CanvasScaler 1920×1080 scale with screen size)
/// 2. HUDPanel — RectTransform anchored bottom-left, e.g. (20, 20) offset, ~260×120 size
///    ├── CaloricGroup    — Vertical Layout Group, child force expand width
///    │   ├── CaloricHeader  — Horizontal Layout Group
///    │   │   ├── CaloricIcon     — Image (optional icon)
///    │   │   └── CaloricLabel    — TextMeshProUGUI "Calories"
///    │   ├── CaloricSegments — RectTransform (~240×16), NO layout component needed
///    │   └── CaloricValue    — TextMeshProUGUI "1500 kcal", font size 12, right-aligned
///    └── HydrationGroup  — same structure as CaloricGroup
///        ├── HydrationHeader → HydrationLabel "Hydration"
///        ├── HydrationSegments — RectTransform
///        └── HydrationValue  — TextMeshProUGUI "1500 g"
///
/// 3. Assign the two *Segments RectTransforms and *Value TMP labels in the Inspector.
///    The script fills each container with `segmentCount` child Images at runtime.
///    No manual segment setup needed.
/// </summary>
public class MetabolismHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BodyMetabolism metabolism;

    [Header("Caloric Bar")]
    [SerializeField] private RectTransform caloricSegmentContainer;
    [SerializeField] private TextMeshProUGUI caloricValueLabel;
    [SerializeField] private string caloricFormat = "{0:0} kcal";

    [Header("Hydration Bar")]
    [SerializeField] private RectTransform hydrationSegmentContainer;
    [SerializeField] private TextMeshProUGUI hydrationValueLabel;
    [SerializeField] private string hydrationFormat = "{0:0} g";

    [Header("Segment Style")]
    [SerializeField] private int   segmentCount    = 20;
    [SerializeField] private float segmentGapRatio = 0.08f;   // gap as fraction of segment width
    [SerializeField] private Color filledHealthy   = new Color(0.85f, 0.65f, 0.1f, 1f);
    [SerializeField] private Color filledWarning   = new Color(0.85f, 0.3f,  0.1f, 1f);
    [SerializeField] private Color filledDepleted  = new Color(0.7f,  0.1f,  0.1f, 1f);
    [SerializeField] private Color emptyColor      = new Color(0.15f, 0.15f, 0.15f, 0.6f);
    [SerializeField][Range(0f, 1f)] private float warningThreshold = 0.25f;

    private Image[] _caloricSegments;
    private Image[] _hydrationSegments;

    private void Awake()
    {
        // Don't build here — RectTransform sizes are 0 until after the first layout pass.
    }

    private void Start()
    {
        // Force a layout pass so container.rect.width is correct before we read it.
        Canvas.ForceUpdateCanvases();
        _caloricSegments   = BuildSegments(caloricSegmentContainer);
        _hydrationSegments = BuildSegments(hydrationSegmentContainer);
        RefreshAll();
    }

    private void OnEnable()
    {
        if (metabolism == null) return;
        metabolism.CaloricChanged      += OnCaloricChanged;
        metabolism.HydrationChanged    += OnHydrationChanged;
        metabolism.OnCaloricDepleted   += OnCaloricDepleted;
        metabolism.OnHydrationDepleted += OnHydrationDepleted;
        metabolism.OnRevived           += OnRevived;
        // Refresh only if Start has already run (segments built); otherwise Start will do it.
        if (_caloricSegments != null) RefreshAll();
    }

    private void OnDisable()
    {
        if (metabolism == null) return;
        metabolism.CaloricChanged      -= OnCaloricChanged;
        metabolism.HydrationChanged    -= OnHydrationChanged;
        metabolism.OnCaloricDepleted   -= OnCaloricDepleted;
        metabolism.OnHydrationDepleted -= OnHydrationDepleted;
        metabolism.OnRevived           -= OnRevived;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnCaloricChanged(float current, float max)
    {
        UpdateSegments(_caloricSegments, max > 0f ? current / max : 0f);
        if (caloricValueLabel != null)
            caloricValueLabel.text = string.Format(caloricFormat, current);
    }

    private void OnHydrationChanged(float current, float max)
    {
        UpdateSegments(_hydrationSegments, max > 0f ? current / max : 0f);
        if (hydrationValueLabel != null)
            hydrationValueLabel.text = string.Format(hydrationFormat, current);
    }

    private void OnCaloricDepleted()   => UpdateSegments(_caloricSegments,   0f);
    private void OnHydrationDepleted() => UpdateSegments(_hydrationSegments, 0f);
    private void OnRevived()           => RefreshAll();

    // ── Segment rendering ─────────────────────────────────────────────────────

    private void UpdateSegments(Image[] segments, float fraction)
    {
        if (segments == null) return;

        // How many segments should be lit
        float filled = fraction * segments.Length;
        Color activeColor = fraction <= 0f      ? filledDepleted
                          : fraction <= warningThreshold ? filledWarning
                          : filledHealthy;

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            // Segment i is fully lit if i < floor(filled), partially lit at the boundary
            bool isLit = i < Mathf.CeilToInt(filled - 0.001f);
            segments[i].color = isLit ? activeColor : emptyColor;
        }
    }

    private void RefreshAll()
    {
        if (metabolism == null) return;
        OnCaloricChanged(metabolism.CaloricReserve,    metabolism.MaxCaloricReserve);
        OnHydrationChanged(metabolism.HydrationReserve, metabolism.MaxHydrationReserve);
    }

    // ── Segment generation ────────────────────────────────────────────────────

    /// <summary>
    /// Procedurally fills <paramref name="container"/> with <see cref="segmentCount"/> Image children,
    /// laid out horizontally with a small gap between each.
    /// </summary>
    private Image[] BuildSegments(RectTransform container)
    {
        if (container == null) return null;

        // Clear any existing children (e.g. editor placeholder)
        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);

        float totalWidth    = container.rect.width;
        float totalHeight   = container.rect.height;

        if (totalWidth <= 0f || totalHeight <= 0f)
        {
            Debug.LogWarning($"[MetabolismHUD] Container '{container.name}' has zero size ({totalWidth}×{totalHeight}). Segments not built.", this);
            return null;
        }

        // segmentGapRatio = gap width as a fraction of each segment's width.
        // totalWidth = segmentCount * segWidth + (segmentCount - 1) * segWidth * gapRatio
        //            = segWidth * (segmentCount + (segmentCount - 1) * gapRatio)
        float segmentWidth = totalWidth / (segmentCount + (segmentCount - 1) * segmentGapRatio);
        float gapWidth     = segmentWidth * segmentGapRatio;

        var segments = new Image[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            var go = new GameObject($"Seg_{i}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(container, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot     = Vector2.zero;

            rt.anchoredPosition = new Vector2(i * (segmentWidth + gapWidth), 0f);
            rt.sizeDelta        = new Vector2(segmentWidth, totalHeight);

            var img = go.GetComponent<Image>();
            img.color = emptyColor;

            segments[i] = img;
        }

        return segments;
    }
}
