using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// The Car Explorer's information interface, built at runtime on ICarExplorerModel only: a breadcrumb, the view
/// switch and the language switch on top, the STRUCTURE tree on the left, the selected node's card on the right, one
/// key hint at the bottom, a help overlay (H) and Remy's welcome toast. On the desktop it is a screen-space overlay
/// whose explorer panels show near the car; in XR the same panels sit on one world-space plate beside the car, which
/// the controller rays use. Created when an explorer registers in CarExplorerModels; nothing is saved in the scene.
/// On the desktop the card's Inspect asks CarExplorerInspect for the inspect camera, and the HUD publishes the part
/// of the view its panels leave free as CarExplorerModels.HudSafeViewport.
/// The desktop layout keeps the middle of the view free: the tree folds into a STRUCTURE tab in the top bar and opens
/// under it on a click or P; the card shows only for a selection, with
/// its description cut to three lines ("More"); with nothing selected a small chip says what to do. In small windows
/// (CarExplorerHudScaler.Compact) the panels are narrower, the tree shows fewer rows and the hint fewer keys. I, or
/// the eye button beside the hint, hides every panel but the hint for the rest of the session.
/// </summary>
[DisallowMultipleComponent]
public sealed class CarExplorerHud : MonoBehaviour
{
    public const string RootName = "Car Explorer HUD";
    public const string ScreenCanvasName = "Car Explorer HUD (Screen)";
    public const string WorldCanvasName = "Car Explorer HUD (XR)";
    public const string EventSystemName = "Car Explorer HUD Event System";

    // Desktop layout, in canvas units of the 1920 x 1080 reference.
    private const float Margin = 24f;
    private const float BarHeight = 48f;
    private const float Gap = 16f;
    // The tree takes 20 % of the canvas width within these bounds (384 units at 1080p) and at most TreeRows rows.
    private const float TreeMinWidth = 352f;
    private const float TreeMaxWidth = 416f;
    private const float TreeWidthShare = 0.2f;
    private const int TreeRows = 14;
    private const float CardWidth = 400f;
    private const float HintHeight = 40f;
    private const float ToastWidth = 528f;
    private const float HelpWidth = 760f;
    private const float CrumbMaxWidth = 280f;
    private const float ControlHeight = 40f;
    // Compact layout (CarExplorerHudScaler.Compact): tighter margins, narrower panels, fewer rows.
    private const float CompactMargin = 16f;
    private const float CompactGap = 12f;
    private const float CompactTreeWidth = 300f;
    private const int CompactTreeRows = 7;
    private const float CompactCardWidth = 340f;
    private const float CompactToastWidth = 440f;
    private const float CompactControlHeight = 34f;
    // The card's description shows this many lines until "More" is pressed (desktop).
    private const int DescriptionLines = 3;

    // The XR plate: 1120 x 760 units of 0.95 mm (1.06 m wide; body text about 17 mm), at arm's length beside the car.
    private const float PlateWidth = 1120f;
    private const float PlateHeight = 760f;
    private const float PlateUnit = 0.00095f;
    private const float PlateReach = 1.15f;
    private const float PlateYaw = 34f;
    private const float PlateMaxYaw = 62f;
    private const float PlateDrop = 0.3f;

    // The desktop panels show near the car (with hysteresis), and whenever something is selected, out or in another
    // view.
    private const float NearDistance = 8f;
    private const float FarDistance = 9.5f;

    private static CarExplorerHud instance;

    private ICarExplorerModel model;
    private bool built;
    private bool dirty;
    private bool layoutDirty;
    private bool xrLayout;
    private bool modeApplied;

    private Canvas screenCanvas;
    private RectTransform screenRoot;
    private CarExplorerHudHover screenHover;
    private Canvas worldCanvas;
    private RectTransform plate;
    private CarExplorerHudHover worldHover;
    private RectTransform workspace;
    private Vector2 lastScreenSize;

    // Top bar.
    private RectTransform breadcrumbPanel;
    private readonly List<CarExplorerHudButton> crumbs = new List<CarExplorerHudButton>();
    private readonly List<TextMeshProUGUI> separators = new List<TextMeshProUGUI>();
    private readonly List<CarStructureNode> crumbNodes = new List<CarStructureNode>();
    private CarExplorerHudButton crumbEllipsis;
    private CarStructureNode ellipsisTarget;
    private readonly List<float> crumbWidths = new List<float>();
    private readonly List<float> crumbNatural = new List<float>();
    private TextMeshProUGUI crumbProbe;
    private RectTransform viewPanel;
    private readonly CarExplorerHudButton[] viewButtons = new CarExplorerHudButton[3];
    private RectTransform languagePanel;
    private CarExplorerHudButton englishButton;
    private CarExplorerHudButton uzbekButton;
    private CarExplorerHudButton helpButton;

    // Tree and card.
    private CarExplorerHudTree tree;
    private RectTransform treeTabPanel;
    private CarExplorerHudButton treeTab;
    private Image treeTabChevron;
    private RectTransform cardPanel;
    private TextMeshProUGUI cardKind;
    private RectTransform cardStatus;
    private TextMeshProUGUI cardStatusText;
    private TextMeshProUGUI cardTitle;
    private TextMeshProUGUI cardContext;
    private TextMeshProUGUI cardDescription;
    private LayoutElement cardDescriptionLayout;
    private TextMeshProUGUI descriptionProbe;
    private CarExplorerHudButton cardMore;
    private bool descriptionExpanded;
    private CarStructureNode descriptionNode;
    private CarExplorerHudButton cardClose;
    private TextMeshProUGUI cardFacts;
    private static readonly string FactsValueColour = ColorUtility.ToHtmlStringRGB(CarExplorerHudStyle.TextPrimary);
    private VerticalLayoutGroup cardLayout;
    private RectTransform cardActions;
    private RectTransform cardPrimaryRow;
    private RectTransform cardSecondaryRow;
    private CarExplorerHudButton cardOpen;
    private CarExplorerHudButton cardInspect;
    private CarExplorerHudButton cardCollapse;
    private CarExplorerHudButton cardPutBack;
    private CarExplorerHudButton cardReset;
    private CanvasGroup cardGroup;

    // The chip shown instead of a card while nothing is selected, and the eye button that hides the panels.
    private RectTransform chipPanel;
    private TextMeshProUGUI chipText;
    private CarExplorerHudButton chipReset;
    private RectTransform panelsButtonPanel;
    private CarExplorerHudButton panelsButton;

    // Hint, toast and help.
    private RectTransform hintPanel;
    private RectTransform hintRow;
    private readonly List<RectTransform> hintItems = new List<RectTransform>();
    private readonly List<Image> hintChips = new List<Image>();
    private readonly List<TextMeshProUGUI> hintKeys = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> hintLabels = new List<TextMeshProUGUI>();
    private HudKey[] hintSet;
    private int tabHintIndex = -1;
    private bool hintUzbek;
    private bool cursorLocked;
    private RectTransform toastPanel;
    private TextMeshProUGUI toastTitle;
    private TextMeshProUGUI toastBody;
    private RectTransform helpRoot;
    private RectTransform helpPanel;
    private TextMeshProUGUI helpTitle;
    private TextMeshProUGUI helpSection;
    private TextMeshProUGUI helpFooter;
    private readonly List<RectTransform> helpRows = new List<RectTransform>();
    private readonly List<TextMeshProUGUI> helpKeys = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> helpActions = new List<TextMeshProUGUI>();
    private bool helpOpen;

    // Fades.
    private sealed class Fader
    {
        public CanvasGroup Group;
        public float Target = 1f;
        public bool Interactive = true;
    }

    private readonly List<Fader> faders = new List<Fader>();
    private Fader workspaceFader;
    private Fader plateFader;
    private Fader hintFader;
    private Fader toastFader;
    private Fader helpFader;
    private Fader panelsButtonFader;

    // Small windows get the compact layout; the visitor's choices last for the Play session.
    private CarExplorerHudScaler scaler;
    private bool compact;
    private static bool panelsHidden;
    private static bool treeChosen;
    private static bool treeChosenOpen;

    // What the HUD last showed, so a Changed event only rebuilds what differs.
    private bool stateValid;
    private CarStructureNode lastRoot;
    private CarStructureNode lastSelected;
    private CarStructureNode lastTakenOut;
    private CarExplorerView lastView;
    private bool lastUzbek;
    private bool lastGreeting;
    private readonly List<CarStructureNode> lastOpenPath = new List<CarStructureNode>();
    private bool lastInspecting;
    private CarStructureNode lastInspectTarget;
    private CarStructureNode hoverNode;
    private CarStructureNode pendingReveal;

    // Presence and the XR plate's placement.
    private Transform carRoot;
    private Vector3 carCentre;
    private float carRadius;
    private bool near = true;
    private bool plateMoving;
    private bool platePlaced;
    private Vector3 plateVelocity;
    private float xrSince;
    private float nextEventSystemCheck;
    private GameObject ownEventSystem;

    /// <summary>The HUD in the scene, or null before an explorer registers.</summary>
    public static CarExplorerHud Instance => instance;

    /// <summary>True while the HUD draws the desktop key hint, so the walkthrough leaves its own out.</summary>
    public static bool DrawsDesktopHint =>
        instance != null && instance.isActiveAndEnabled && instance.model != null && !instance.model.IsXr;

    /// <summary>True while the HUD's help overlay covers the screen (the walkthrough hides its crosshair).</summary>
    public static bool HelpShown => DrawsDesktopHint && instance.helpOpen;

    public ICarExplorerModel Model => model;
    public bool Uzbek => model != null && model.Uzbek;
    public CarExplorerHudTree Tree => tree;
    /// <summary>The node whose card is shown: the inspected node while the inspect camera is on, else the selection.</summary>
    public CarStructureNode CardNode => model == null ? null : InspectTarget() ?? model.Selected;
    /// <summary>The breadcrumb's nodes, vehicle first (for tests).</summary>
    public IReadOnlyList<CarStructureNode> CrumbNodes => crumbNodes;
    /// <summary>The keys the hint line shows (for tests).</summary>
    public IReadOnlyList<HudKey> HintKeys => hintSet;
    public Canvas ScreenCanvas => screenCanvas;
    public Canvas WorldCanvas => worldCanvas;
    public bool HelpOpen => helpOpen;
    /// <summary>True while the visitor has hidden the desktop panels (I); kept for the Play session.</summary>
    public bool PanelsHidden => panelsHidden;
    /// <summary>True while the desktop uses the compact layout (a small window).</summary>
    public bool Compact => compact;
    /// <summary>True while the desktop STRUCTURE tree is open (not just its tab).</summary>
    public bool TreeOpen => tree != null && !tree.Collapsed;

    // ------------------------------------------------------------------ creation

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        CarExplorerModels.CurrentChanged -= OnCurrentModelChanged;
        CarExplorerModels.PointerOverHud = false;
        instance = null;
        panelsHidden = false;
        treeChosen = false;
        treeChosenOpen = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        CarExplorerModels.CurrentChanged -= OnCurrentModelChanged;
        CarExplorerModels.CurrentChanged += OnCurrentModelChanged;
        OnCurrentModelChanged();
    }

    private static void OnCurrentModelChanged()
    {
        ICarExplorerModel current = CarExplorerModels.Current;
        if (instance == null)
        {
            if (current == null) return;
            instance = new GameObject(RootName).AddComponent<CarExplorerHud>();
        }
        instance.Bind(current);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        Build();
        CarExplorerInspect.ActiveChanged += OnModelChanged;
    }

    private void OnDestroy()
    {
        CarExplorerInspect.ActiveChanged -= OnModelChanged;
        if (model != null) model.Changed -= OnModelChanged;
        model = null;
        if (instance == this)
        {
            instance = null;
            CarExplorerModels.PointerOverHud = false;
            CarExplorerModels.HudSafeViewport = new Rect(0f, 0f, 1f, 1f);
        }
    }

    private void Bind(ICarExplorerModel next)
    {
        if (model == next) return;
        if (model != null)
        {
            model.Changed -= OnModelChanged;
            model.Highlight(null);
        }
        model = next;
        hoverNode = null;
        stateValid = false;
        lastRoot = null;
        if (model != null) model.Changed += OnModelChanged;
        dirty = true;
        modeApplied = false;
        bool live = model != null;
        if (screenCanvas != null) screenCanvas.gameObject.SetActive(live);
        if (worldCanvas != null && !live) worldCanvas.gameObject.SetActive(false);
        if (!live)
        {
            CarExplorerModels.PointerOverHud = false;
            CarExplorerModels.HudSafeViewport = new Rect(0f, 0f, 1f, 1f);
        }
        // Only a live model needs the car; on unregister (the explorer's OnDisable, also while the scene unloads)
        // GameObject.Find would assert.
        if (live) FindCar();
    }

    private void OnModelChanged() => dirty = true;

    private void FindCar()
    {
        GameObject car = GameObject.Find("CarConcept");
        carRoot = car != null ? car.transform : null;
        if (carRoot == null) return;
        // The car's rest bounds, once: its centre places the XR plate and decides when the desktop panels show.
        var renderers = carRoot.GetComponentsInChildren<Renderer>();
        bool any = false;
        Bounds bounds = new Bounds(carRoot.position, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled) continue;
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        carCentre = any ? bounds.center : carRoot.position;
        carRadius = any ? Mathf.Max(bounds.extents.x, bounds.extents.z) : 2.3f;
    }

    private void Build()
    {
        if (built) return;
        built = true;

        var screenObject = new GameObject(ScreenCanvasName, typeof(RectTransform));
        screenObject.layer = CarExplorerHudStyle.UiLayer;
        screenObject.transform.SetParent(transform, false);
        screenCanvas = screenObject.AddComponent<Canvas>();
        screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        screenCanvas.sortingOrder = 40;
        scaler = screenObject.AddComponent<CarExplorerHudScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        screenObject.AddComponent<GraphicRaycaster>();
        screenHover = screenObject.AddComponent<CarExplorerHudHover>();
        screenRoot = (RectTransform)screenObject.transform;

        var worldObject = new GameObject(WorldCanvasName, typeof(RectTransform));
        worldObject.layer = CarExplorerHudStyle.UiLayer;
        worldObject.transform.SetParent(transform, false);
        worldCanvas = worldObject.AddComponent<Canvas>();
        worldCanvas.renderMode = RenderMode.WorldSpace;
        var worldRect = (RectTransform)worldObject.transform;
        worldRect.sizeDelta = new Vector2(PlateWidth, PlateHeight);
        worldRect.localScale = Vector3.one * PlateUnit;
        worldObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
        worldObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        worldHover = worldObject.AddComponent<CarExplorerHudHover>();
        plate = CarExplorerHudStyle.Rect("Plate", worldRect);
        CarExplorerHudStyle.Stretch(plate);
        CarExplorerHudStyle.Panel(plate, CarExplorerHudStyle.PlateFill, 24, false);
        plateFader = AddFader(plate, true);
        worldObject.SetActive(false);

        workspace = CarExplorerHudStyle.Rect("Workspace", screenRoot);
        CarExplorerHudStyle.Stretch(workspace);
        workspaceFader = AddFader(workspace, true);

        BuildBreadcrumb();
        BuildViewSwitch();
        BuildLanguage();
        tree = new CarExplorerHudTree(this, workspace);
        BuildTreeTab();
        BuildCard();
        BuildChip();
        BuildHint();
        BuildPanelsButton();
        BuildToast();
        BuildHelp();
        screenObject.SetActive(model != null);
    }

    private Fader AddFader(Component target, bool visible)
    {
        var fader = new Fader { Group = CarExplorerHudStyle.Group(target), Target = visible ? 1f : 0f };
        fader.Group.alpha = fader.Target;
        fader.Group.blocksRaycasts = visible;
        faders.Add(fader);
        return fader;
    }

    // ------------------------------------------------------------------ top bar

    private void BuildBreadcrumb()
    {
        breadcrumbPanel = CarExplorerHudStyle.Rect("Breadcrumb", workspace, new Vector2(0f, 1f));
        CarExplorerHudStyle.Panel(breadcrumbPanel, CarExplorerHudStyle.PanelFill, 12);
        breadcrumbPanel.sizeDelta = new Vector2(200f, BarHeight);
        crumbEllipsis = CreateCrumb("Crumb …");
        crumbEllipsis.Label.text = "…";
        crumbEllipsis.Clicked = () => { if (ellipsisTarget != null) NavigateTo(ellipsisTarget); };
        crumbEllipsis.gameObject.SetActive(false);
        // Never drawn: measures crumb names (measuring a shown label would replace what it draws).
        crumbProbe = CarExplorerHudStyle.Text("Measure", breadcrumbPanel, CarExplorerHudStyle.RowSize,
            CarExplorerHudStyle.Clear);
        crumbProbe.enabled = false;
    }

    private CarExplorerHudButton CreateCrumb(string name)
    {
        CarExplorerHudButton crumb = CarExplorerHudButton.Create(name, breadcrumbPanel,
            CarExplorerHudButton.Variant.Ghost, string.Empty, CarExplorerHudStyle.RowSize, null, 8);
        RectTransform rect = (RectTransform)crumb.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
        CarExplorerHudStyle.Stretch(crumb.Label.rectTransform, 8f, 8f);
        crumb.Label.alignment = TextAlignmentOptions.Midline;
        return crumb;
    }

    private void BuildViewSwitch()
    {
        viewPanel = CarExplorerHudStyle.Rect("View Switch", workspace, new Vector2(0.5f, 1f));
        CarExplorerHudStyle.Panel(viewPanel, CarExplorerHudStyle.PanelFill, 12);
        for (int i = 0; i < viewButtons.Length; i++)
        {
            int index = i;
            CarExplorerHudButton button = CarExplorerHudButton.Create("View " + (CarExplorerView)i, viewPanel,
                CarExplorerHudButton.Variant.Segment, CarExplorerHudText.ViewButtons[i].English,
                CarExplorerHudStyle.LabelSize, null, 8);
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
            button.Clicked = () => model?.SetView((CarExplorerView)index);
            viewButtons[i] = button;
        }
    }

    private void BuildLanguage()
    {
        languagePanel = CarExplorerHudStyle.Rect("Language", workspace, new Vector2(1f, 1f));
        CarExplorerHudStyle.Panel(languagePanel, CarExplorerHudStyle.PanelFill, 12);
        englishButton = CarExplorerHudButton.Create("English", languagePanel, CarExplorerHudButton.Variant.Segment,
            "EN", CarExplorerHudStyle.LabelSize, null, 8);
        uzbekButton = CarExplorerHudButton.Create("Uzbek", languagePanel, CarExplorerHudButton.Variant.Segment, "UZ",
            CarExplorerHudStyle.LabelSize, null, 8);
        englishButton.SubtleSelection = uzbekButton.SubtleSelection = true;
        englishButton.Clicked = () => model?.SetLanguage(false);
        uzbekButton.Clicked = () => model?.SetLanguage(true);
        helpButton = CarExplorerHudButton.Create("Help", languagePanel, CarExplorerHudButton.Variant.Segment, "?",
            CarExplorerHudStyle.BodySize, null, 8);
        helpButton.SubtleSelection = true;
        helpButton.Clicked = () => SetHelp(!helpOpen);
        Image divider = CarExplorerHudStyle.Image("Divider", languagePanel, null, CarExplorerHudStyle.Divider);
        RectTransform dividerRect = divider.rectTransform;
        dividerRect.anchorMin = dividerRect.anchorMax = dividerRect.pivot = new Vector2(0f, 0.5f);
        dividerRect.sizeDelta = new Vector2(1f, 24f);
        dividerRect.anchoredPosition = new Vector2(4f + 56f * 2f + 8f, 0f);
        float x = 4f;
        foreach (CarExplorerHudButton button in new[] { englishButton, uzbekButton, null, helpButton })
        {
            if (button == null) { x += 9f; continue; }
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
            float width = button == helpButton ? 40f : 56f;
            rect.sizeDelta = new Vector2(width, 40f);
            rect.anchoredPosition = new Vector2(x, 0f);
            x += width;
        }
        languagePanel.sizeDelta = new Vector2(x + 4f, BarHeight);
    }

    // The desktop's STRUCTURE tab, left in the top bar: the tree opens under it and closes again (P too).
    private void BuildTreeTab()
    {
        treeTabPanel = CarExplorerHudStyle.Rect("Structure Tab", workspace, new Vector2(0f, 1f));
        CarExplorerHudStyle.Panel(treeTabPanel, CarExplorerHudStyle.PanelFill, 12);
        treeTab = CarExplorerHudButton.Create("Toggle", treeTabPanel, CarExplorerHudButton.Variant.Segment,
            CarExplorerHudText.StructureTab.English, CarExplorerHudStyle.LabelSize, CarExplorerHudStyle.Icon("tree"), 8);
        treeTab.SubtleSelection = true;
        CarExplorerHudStyle.Stretch((RectTransform)treeTab.transform, 4f, 4f, 4f, 4f);
        RectTransform iconRect = treeTab.Icon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(10f, 0f);
        iconRect.sizeDelta = new Vector2(20f, 20f);
        CarExplorerHudStyle.Stretch(treeTab.Label.rectTransform, 38f, 30f);
        treeTab.Label.alignment = TextAlignmentOptions.MidlineLeft;
        treeTabChevron = CarExplorerHudStyle.Image("Chevron", treeTab.transform, CarExplorerHudStyle.Icon("chevron"),
            CarExplorerHudStyle.TextTertiary);
        // Turned about its own centre: down while the tree is closed, up while it is open.
        RectTransform chevron = treeTabChevron.rectTransform;
        chevron.anchorMin = chevron.anchorMax = new Vector2(1f, 0.5f);
        chevron.pivot = new Vector2(0.5f, 0.5f);
        chevron.anchoredPosition = new Vector2(-16f, 0f);
        chevron.sizeDelta = new Vector2(16f, 16f);
        treeTab.Clicked = ToggleTree;
    }

    // The tab's width for its label in the current language.
    private void RebuildTreeTab()
    {
        treeTab.Label.text = CarExplorerHudText.StructureTab.Get(Uzbek);
        float width = Mathf.Ceil(treeTab.Label.GetPreferredValues(treeTab.Label.text, 4000f, 40f).x) + 38f + 30f + 8f;
        treeTabPanel.sizeDelta = new Vector2(width, BarHeight);
    }

    private void ApplyTreeTabState()
    {
        bool open = !tree.Collapsed;
        treeTab.Selected = open;
        treeTabChevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, open ? 90f : -90f);
        treeTabChevron.color = open ? CarExplorerHudStyle.TextPrimary : CarExplorerHudStyle.TextTertiary;
    }

    /// <summary>Opens or closes the desktop STRUCTURE tree (the tab, P); the choice lasts for the session.</summary>
    public void ToggleTree() => SetTreeOpen(tree.Collapsed);

    public void SetTreeOpen(bool open)
    {
        treeChosen = true;
        treeChosenOpen = open;
        if (open && panelsHidden) SetPanelsHidden(false);
        ApplyTreeRule();
    }

    // On the desktop only the tab shows until the visitor opens the tree (tab or P); then it stays as they left it.
    // It does not open by itself for a selection, on any screen: its column would then have to stay out of the free
    // view the explorer places parts in, and a taken-out assembly would go behind the car instead of beside it.
    // The plate keeps its own header.
    private void ApplyTreeRule()
    {
        if (model == null || xrLayout) return;
        bool open = treeChosen && treeChosenOpen;
        if (tree.Collapsed == open) tree.SetCollapsed(!open);
        ApplyTreeTabState();
        layoutDirty = true;
    }

    // While nothing is selected the desktop shows this chip instead of a card (with Reset when the car is not whole).
    private void BuildChip()
    {
        chipPanel = CarExplorerHudStyle.Rect("Explore Chip", workspace, new Vector2(0f, 0f));
        CarExplorerHudStyle.Panel(chipPanel, CarExplorerHudStyle.PanelFill, 12);
        var layout = chipPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 4, 4, 4);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        var fitter = chipPanel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        chipText = CarExplorerHudStyle.Text("Text", chipPanel, CarExplorerHudStyle.LabelSize - 1f,
            CarExplorerHudStyle.TextSecondary, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        CarExplorerHudStyle.Layout(chipText, -1f, 32f);
        chipReset = CarExplorerHudButton.Create("Reset", chipPanel, CarExplorerHudButton.Variant.Secondary,
            string.Empty, CarExplorerHudStyle.LabelSize - 1f, null, 8);
        CarExplorerHudStyle.Layout(chipReset, 88f, 32f);
        chipReset.Clicked = () => model?.ResetAll();
        chipPanel.sizeDelta = new Vector2(200f, HintHeight);
        chipPanel.gameObject.SetActive(false);
    }

    private void RebuildChip()
    {
        bool uzbek = Uzbek;
        chipText.text = CarExplorerHudText.ExploreChip.Get(uzbek);
        bool reset = model.TakenOut != null || model.View != CarExplorerView.Normal;
        chipReset.gameObject.SetActive(reset);
        if (reset)
        {
            chipReset.Label.text = CarExplorerHudText.Reset.Get(uzbek);
            CarExplorerHudStyle.Layout(chipReset, chipReset.PreferredWidth(72f), 32f);
        }
        RectOffset padding = chipPanel.GetComponent<HorizontalLayoutGroup>().padding;
        padding.right = reset ? 4 : 16;
        layoutDirty = true;
    }

    // The eye beside the hint: hides every panel but the hint (I), and shows them again.
    private void BuildPanelsButton()
    {
        panelsButtonPanel = CarExplorerHudStyle.Rect("Panels Toggle", screenRoot, new Vector2(1f, 0f));
        CarExplorerHudStyle.Panel(panelsButtonPanel, CarExplorerHudStyle.PanelFill, 12);
        panelsButtonPanel.sizeDelta = new Vector2(HintHeight + 8f, HintHeight);
        panelsButton = CarExplorerHudButton.Create("Toggle", panelsButtonPanel, CarExplorerHudButton.Variant.Segment,
            null, 0f, CarExplorerHudStyle.Icon("eye"), 8);
        panelsButton.SubtleSelection = true;
        CarExplorerHudStyle.Stretch((RectTransform)panelsButton.transform, 4f, 4f, 4f, 4f);
        panelsButton.Icon.rectTransform.sizeDelta = new Vector2(24f, 24f);
        panelsButton.Clicked = () => SetPanelsHidden(!panelsHidden);
        panelsButtonFader = AddFader(panelsButtonPanel, false);
        panelsButtonPanel.gameObject.SetActive(false);
    }

    /// <summary>Hides or shows the desktop panels (the eye button, I); the hint stays. Kept for the session.</summary>
    public void SetPanelsHidden(bool hidden)
    {
        if (panelsHidden == hidden) return;
        panelsHidden = hidden;
        ApplyPanelsButton();
        if (model != null && stateValid) RebuildHint();
        layoutDirty = true;
    }

    private void ApplyPanelsButton()
    {
        panelsButton.Icon.sprite = CarExplorerHudStyle.Icon(panelsHidden ? "eye-off" : "eye");
        panelsButton.Selected = panelsHidden;
    }

    private void RebuildViewSwitch()
    {
        bool uzbek = Uzbek;
        float width = 104f;
        for (int i = 0; i < viewButtons.Length; i++)
        {
            viewButtons[i].Label.text = CarExplorerHudText.ViewButtons[i].Get(uzbek);
            width = Mathf.Max(width, viewButtons[i].PreferredWidth(104f));
        }
        for (int i = 0; i < viewButtons.Length; i++)
        {
            RectTransform rect = (RectTransform)viewButtons[i].transform;
            rect.sizeDelta = new Vector2(width, 40f);
            rect.anchoredPosition = new Vector2(4f + i * (width + 2f), 0f);
        }
        viewPanel.sizeDelta = new Vector2(8f + width * 3f + 4f, BarHeight);
    }

    private void UpdateSwitchStates()
    {
        CarExplorerView view = model.View;
        for (int i = 0; i < viewButtons.Length; i++) viewButtons[i].Selected = (int)view == i;
        englishButton.Selected = !model.Uzbek;
        uzbekButton.Selected = model.Uzbek;
        helpButton.Selected = helpOpen;
    }

    private void RebuildBreadcrumb()
    {
        crumbNodes.Clear();
        CarStructureNode target = model.Selected;
        IReadOnlyList<CarStructureNode> path = model.OpenPath;
        if (target == null && path != null && path.Count > 0) target = path[path.Count - 1];
        if (target == null) target = model.TakenOut;
        if (target == null) target = model.Root;
        for (CarStructureNode n = target; n != null; n = n.Parent) crumbNodes.Add(n);
        crumbNodes.Reverse();
        // Until the explorer has built its tree there is nothing to show.
        breadcrumbPanel.gameObject.SetActive(crumbNodes.Count > 0);
        while (crumbs.Count < crumbNodes.Count)
        {
            int index = crumbs.Count;
            CarExplorerHudButton crumb = CreateCrumb("Crumb " + index);
            crumb.Clicked = () => { if (index < crumbNodes.Count) NavigateTo(crumbNodes[index]); };
            crumb.Hovered = on => HoverNode(on && index < crumbNodes.Count ? crumbNodes[index] : null);
            crumbs.Add(crumb);
            TextMeshProUGUI separator = CarExplorerHudStyle.Text("Separator", breadcrumbPanel,
                CarExplorerHudStyle.RowSize, CarExplorerHudStyle.TextTertiary, FontStyles.Normal,
                TextAlignmentOptions.Midline);
            separator.text = "›";
            RectTransform separatorRect = separator.rectTransform;
            separatorRect.anchorMin = separatorRect.anchorMax = separatorRect.pivot = new Vector2(0f, 0.5f);
            separatorRect.sizeDelta = new Vector2(16f, BarHeight);
            separators.Add(separator);
        }
        for (int i = 0; i < crumbs.Count; i++)
        {
            bool used = i < crumbNodes.Count;
            crumbs[i].gameObject.SetActive(used);
            separators[i].gameObject.SetActive(false);
            if (!used) continue;
            bool last = i == crumbNodes.Count - 1;
            // The text is set by LayoutBreadcrumb, whole or fitted to the room it gets.
            crumbs[i].Label.fontStyle = last ? FontStyles.Bold : FontStyles.Normal;
            crumbs[i].IdleContent = last ? CarExplorerHudStyle.TextPrimary : CarExplorerHudStyle.TextSecondary;
            crumbs[i].Apply();
        }
        LayoutBreadcrumb();
    }

    // Places the crumbs left to right within the space the top bar leaves; when they do not fit, the middle levels
    // fold into "…" (which goes to the deepest folded level). A middle crumb is at most CrumbMaxWidth wide; the last
    // one (the node in view) takes whatever room is left up to the view switch, and only then is its name cut, so
    // that it still tells it from its siblings ("Rocker Arm A… 5").
    private void LayoutBreadcrumb()
    {
        int count = crumbNodes.Count;
        if (count == 0) return;
        for (int i = 0; i < separators.Count; i++)
            if (separators[i].gameObject.activeSelf) separators[i].gameObject.SetActive(false);
        bool uzbek = Uzbek;
        crumbWidths.Clear();
        crumbNatural.Clear();
        for (int i = 0; i < count; i++)
        {
            bool last = i == count - 1;
            crumbProbe.fontStyle = last ? FontStyles.Bold : FontStyles.Normal;
            float natural = Mathf.Ceil(CarExplorerHudTree.Measure(crumbProbe, crumbNodes[i].Name(uzbek))) + 16f;
            crumbNatural.Add(natural);
            crumbWidths.Add(last ? natural : Mathf.Min(CrumbMaxWidth, natural));
        }
        float maxWidth = BreadcrumbMaxWidth();
        const float padding = 8f, separatorWidth = 16f;
        float ellipsisWidth = 36f;
        int foldEnd = 0; // crumbs [1, foldEnd) are folded
        float total = Measure(1, count, padding, separatorWidth, ellipsisWidth, out float lastWidth);
        while (total > maxWidth && foldEnd < count - 1)
        {
            foldEnd = foldEnd == 0 ? 2 : foldEnd + 1;
            if (foldEnd > count - 1) { foldEnd = count - 1; }
            total = Measure(foldEnd, count, padding, separatorWidth, ellipsisWidth, out lastWidth);
            if (foldEnd == count - 1) break;
        }
        // Still too wide: the vehicle (always the first crumb) gives way first, then the node in view.
        if (total > maxWidth && count > 1)
        {
            float shrink = Mathf.Min(total - maxWidth, Mathf.Max(0f, crumbWidths[0] - 72f));
            crumbWidths[0] -= shrink;
            total -= shrink;
        }
        if (total > maxWidth) lastWidth = Mathf.Max(72f, lastWidth - (total - maxWidth));

        float x = padding;
        bool folded = foldEnd > 1;
        ellipsisTarget = folded ? crumbNodes[foldEnd - 1] : null;
        crumbEllipsis.gameObject.SetActive(folded);
        int separatorIndex = 0;
        for (int i = 0; i < count; i++)
        {
            CarExplorerHudButton crumb = crumbs[i];
            if (folded && i >= 1 && i < foldEnd)
            {
                crumb.gameObject.SetActive(false);
                if (i != 1) continue;
                PlaceSeparator(separatorIndex++, ref x, separatorWidth);
                Place(crumbEllipsis, ref x, ellipsisWidth);
                continue;
            }
            if (i > 0) PlaceSeparator(separatorIndex++, ref x, separatorWidth);
            float width = i == count - 1 ? lastWidth : crumbWidths[i];
            SetCrumbText(i, width, uzbek);
            Place(crumb, ref x, width);
        }
        breadcrumbPanel.sizeDelta = new Vector2(Mathf.Ceil(x + padding), BarHeight);
    }

    // The crumb's name, whole when its width allows, else fitted (the label has 8 units of padding each side).
    private void SetCrumbText(int index, float width, bool uzbek)
    {
        CarStructureNode node = crumbNodes[index];
        string text;
        if (width >= crumbNatural[index] - 0.5f)
        {
            text = node.Name(uzbek);
        }
        else
        {
            crumbProbe.fontStyle = index == crumbNodes.Count - 1 ? FontStyles.Bold : FontStyles.Normal;
            text = CarExplorerHudTree.FitName(crumbProbe, node, uzbek, width - 17f);
        }
        TextMeshProUGUI label = crumbs[index].Label;
        if (label.text != text) label.text = text;
    }

    private float Measure(int foldEnd, int count, float padding, float separatorWidth, float ellipsisWidth,
        out float lastWidth)
    {
        float total = padding * 2f;
        lastWidth = crumbWidths[count - 1];
        for (int i = 0; i < count; i++)
        {
            if (foldEnd > 1 && i >= 1 && i < foldEnd)
            {
                if (i == 1) total += separatorWidth + ellipsisWidth;
                continue;
            }
            if (i > 0) total += separatorWidth;
            total += crumbWidths[i];
        }
        return total;
    }

    private void PlaceSeparator(int index, ref float x, float width)
    {
        TextMeshProUGUI separator = separators[index];
        separator.gameObject.SetActive(true);
        separator.rectTransform.anchoredPosition = new Vector2(x, 0f);
        x += width;
    }

    private static void Place(CarExplorerHudButton button, ref float x, float width)
    {
        button.gameObject.SetActive(true);
        var rect = (RectTransform)button.transform;
        rect.sizeDelta = new Vector2(width, 36f);
        rect.anchoredPosition = new Vector2(x, 0f);
        x += width;
    }

    private float BreadcrumbMaxWidth()
    {
        // On the plate and in a small window the switches sit together on the right; otherwise the view switch is
        // centred. The crumbs start right of the STRUCTURE tab on the desktop.
        if (xrLayout)
        {
            float switches = viewPanel.sizeDelta.x + languagePanel.sizeDelta.x;
            return Mathf.Max(240f, PlateWidth - Margin * 2f - switches - Gap * 2f);
        }
        float crumbLeft = breadcrumbPanel.anchoredPosition.x;
        float viewLeft = Small
            ? screenRoot.rect.width - CompactMargin - languagePanel.sizeDelta.x - CompactGap - viewPanel.sizeDelta.x
            : screenRoot.rect.width * 0.5f - viewPanel.sizeDelta.x * 0.5f;
        return Mathf.Max(160f, viewLeft - CurrentGap - crumbLeft);
    }

    /// <summary>
    /// A breadcrumb click goes back to that level: the vehicle or a system puts the assembly back; an assembly or a
    /// deeper level keeps its own level apart and closes the ones below it.
    /// </summary>
    private void NavigateTo(CarStructureNode node)
    {
        if (model == null || node == null) return;
        if (node.Kind <= CarNodeKind.System)
        {
            if (model.TakenOut != null) model.PutBack();
            model.Select(node.Kind == CarNodeKind.Vehicle ? null : node);
            return;
        }
        IReadOnlyList<CarStructureNode> path = model.OpenPath;
        int keep = 0;
        if (path != null)
            for (int i = 0; i < path.Count; i++)
                if (IsSelfOrAncestor(path[i], node)) keep = i + 1;
        if (path != null && keep < path.Count) model.CloseTo(keep);
        model.Select(node);
    }

    private static bool IsSelfOrAncestor(CarStructureNode ancestor, CarStructureNode node)
    {
        for (CarStructureNode n = node; n != null; n = n.Parent)
            if (n == ancestor) return true;
        return false;
    }

    // ------------------------------------------------------------------ card

    private void BuildCard()
    {
        cardPanel = CarExplorerHudStyle.Rect("Card", workspace, new Vector2(1f, 1f));
        CarExplorerHudStyle.Panel(cardPanel, CarExplorerHudStyle.PanelFill);
        cardGroup = CarExplorerHudStyle.Group(cardPanel);
        cardLayout = cardPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(24, 24, 20, 24);
        cardLayout.spacing = 8f;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;
        var fitter = cardPanel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Kind, status chip and the close button (clears the selection).
        RectTransform header = CarExplorerHudStyle.Rect("Header", cardPanel);
        var headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.spacing = 8f;
        CarExplorerHudStyle.Layout(header, -1f, 24f);
        cardKind = CarExplorerHudStyle.Text("Kind", header, CarExplorerHudStyle.CaptionSize, CarExplorerHudStyle.Accent,
            FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        cardKind.characterSpacing = 8f;
        CarExplorerHudStyle.Layout(cardKind, -1f, 24f, 1f);
        cardStatus = CarExplorerHudStyle.Rect("Status", header);
        Image statusRing = CarExplorerHudStyle.Image("Ring", cardStatus,
            CarExplorerHudStyle.Rounded(CarExplorerHudStyle.ChipRadius, 1f), CarExplorerHudStyle.AccentBorder);
        CarExplorerHudStyle.Stretch(statusRing.rectTransform);
        cardStatusText = CarExplorerHudStyle.Text("Text", cardStatus, 13f, CarExplorerHudStyle.Accent, FontStyles.Bold,
            TextAlignmentOptions.Midline);
        cardStatusText.characterSpacing = 6f;
        CarExplorerHudStyle.Stretch(cardStatusText.rectTransform, 8f, 8f);
        cardClose = CarExplorerHudButton.Create("Close", header, CarExplorerHudButton.Variant.Ghost, null, 0f,
            CarExplorerHudStyle.Icon("close"), 8);
        cardClose.Icon.rectTransform.sizeDelta = new Vector2(20f, 20f);
        CarExplorerHudStyle.Layout(cardClose, 28f, 28f).minWidth = 28f;
        cardClose.Clicked = () => model?.Select(null);

        cardTitle = CarExplorerHudStyle.Text("Title", cardPanel, CarExplorerHudStyle.TitleSize,
            CarExplorerHudStyle.TextPrimary, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        cardTitle.textWrappingMode = TextWrappingModes.Normal;
        cardTitle.lineSpacing = -6f;
        cardContext = CarExplorerHudStyle.Text("Context", cardPanel, CarExplorerHudStyle.LabelSize,
            CarExplorerHudStyle.TextSecondary, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        cardContext.textWrappingMode = TextWrappingModes.Normal;

        Image divider = CarExplorerHudStyle.Image("Divider", cardPanel, null, CarExplorerHudStyle.Divider);
        CarExplorerHudStyle.Layout(divider, -1f, 1f, -1f, 1f);

        cardDescription = CarExplorerHudStyle.Text("Description", cardPanel, CarExplorerHudStyle.BodySize,
            new Color(0.86f, 0.88f, 0.91f, 1f), FontStyles.Normal, TextAlignmentOptions.TopLeft);
        cardDescription.textWrappingMode = TextWrappingModes.Normal;
        cardDescription.lineSpacing = 6f;
        cardDescriptionLayout = CarExplorerHudStyle.Layout(cardDescription);
        // Never drawn: measures the description against three lines (measuring the shown text would redraw it).
        descriptionProbe = CarExplorerHudStyle.Text("Measure", workspace, CarExplorerHudStyle.BodySize,
            CarExplorerHudStyle.Clear);
        descriptionProbe.textWrappingMode = TextWrappingModes.Normal;
        descriptionProbe.lineSpacing = 6f;
        descriptionProbe.enabled = false;

        // "More" / "Less" under a description cut to three lines.
        RectTransform moreRow = CarExplorerHudStyle.Rect("More", cardPanel);
        var moreLayout = moreRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        moreLayout.childControlWidth = true;
        moreLayout.childControlHeight = true;
        moreLayout.childForceExpandWidth = false;
        moreLayout.childForceExpandHeight = false;
        moreLayout.padding = new RectOffset(-8, 0, -4, 0);
        cardMore = CarExplorerHudButton.Create("Toggle", moreRow, CarExplorerHudButton.Variant.Ghost, string.Empty,
            CarExplorerHudStyle.LabelSize, null, 6);
        cardMore.IdleContent = CarExplorerHudStyle.Accent;
        cardMore.Label.fontStyle = FontStyles.Bold;
        CarExplorerHudStyle.Stretch(cardMore.Label.rectTransform, 8f, 8f);
        CarExplorerHudStyle.Layout(cardMore, 80f, 26f);
        cardMore.Clicked = () =>
        {
            descriptionExpanded = !descriptionExpanded;
            layoutDirty = true;
        };
        moreRow.gameObject.SetActive(false);

        // Material and part count in one row ("Material  Steel  ·  7 parts"), wrapped between words if it must.
        cardFacts = CarExplorerHudStyle.Text("Facts", cardPanel, CarExplorerHudStyle.LabelSize,
            CarExplorerHudStyle.TextTertiary, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        cardFacts.textWrappingMode = TextWrappingModes.Normal;
        cardFacts.richText = true;
        cardFacts.lineSpacing = 4f;

        // Two rows: what to do with the node (take out or explode, inspect), then the way back (collapse, put back,
        // reset). Each row shows only when it has a button.
        cardActions = CarExplorerHudStyle.Rect("Actions", cardPanel);
        var actionsLayout = cardActions.gameObject.AddComponent<VerticalLayoutGroup>();
        actionsLayout.spacing = 8f;
        actionsLayout.padding = new RectOffset(0, 0, 8, 0);
        actionsLayout.childControlWidth = true;
        actionsLayout.childControlHeight = true;
        actionsLayout.childForceExpandWidth = true;
        actionsLayout.childForceExpandHeight = false;
        cardPrimaryRow = CreateActionRow("Primary");
        cardSecondaryRow = CreateActionRow("Secondary");
        cardOpen = CreateAction("Open", cardPrimaryRow, CarExplorerHudButton.Variant.Primary,
            CarExplorerHudStyle.Icon("explode"));
        cardOpen.Clicked = () => { CarStructureNode node = CardNode; if (node != null) model.Open(node); };
        cardInspect = CreateAction("Inspect", cardPrimaryRow, CarExplorerHudButton.Variant.Secondary,
            CarExplorerHudStyle.Icon("focus"));
        cardInspect.Clicked = ToggleInspect;
        cardCollapse = CreateAction("Collapse", cardSecondaryRow, CarExplorerHudButton.Variant.Secondary, null);
        cardCollapse.Clicked = () =>
        {
            int index = IndexInOpenPath(CardNode);
            if (index >= 0) model.CloseTo(index);
        };
        cardPutBack = CreateAction("Put Back", cardSecondaryRow, CarExplorerHudButton.Variant.Secondary, null);
        cardPutBack.Clicked = () => model?.PutBack();
        cardReset = CreateAction("Reset", cardSecondaryRow, CarExplorerHudButton.Variant.Secondary, null);
        cardReset.Clicked = () => model?.ResetAll();
    }

    private RectTransform CreateActionRow(string name)
    {
        RectTransform row = CarExplorerHudStyle.Rect(name, cardActions);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private CarExplorerHudButton CreateAction(string name, RectTransform row, CarExplorerHudButton.Variant style,
        Sprite icon)
    {
        CarExplorerHudButton button = CarExplorerHudButton.Create(name, row, style, string.Empty,
            CarExplorerHudStyle.LabelSize, icon, CarExplorerHudStyle.ControlRadius);
        if (icon != null)
        {
            RectTransform iconRect = button.Icon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(14f, 0f);
            iconRect.sizeDelta = new Vector2(18f, 18f);
            CarExplorerHudStyle.Stretch(button.Label.rectTransform, 38f, 14f);
        }
        CarExplorerHudStyle.Layout(button, 96f, 40f, 1f);
        return button;
    }

    private void RebuildCard(bool animate)
    {
        bool uzbek = Uzbek;
        CarStructureNode node = CardNode;
        CarStructureNode root = model.Root;
        // The desktop shows a card only for a selection (the chip otherwise); the plate keeps the overview card.
        CarStructureNode subject = node ?? (xrLayout ? root : null);
        bool chip = !xrLayout && node == null && root != null;
        if (chipPanel.gameObject.activeSelf != chip) chipPanel.gameObject.SetActive(chip);
        if (chip) RebuildChip();
        layoutDirty = true;
        if (subject == null)
        {
            cardPanel.gameObject.SetActive(false);
            descriptionNode = null;
            return;
        }
        cardPanel.gameObject.SetActive(true);
        bool overview = node == null;
        if (subject != descriptionNode)
        {
            descriptionNode = subject;
            descriptionExpanded = false;
        }

        int kind = Mathf.Clamp((int)subject.Kind, 0, CarExplorerHudText.Kinds.Length - 1);
        cardKind.text = CarExplorerHudText.Kinds[kind].Get(uzbek);
        bool apart = !overview && IsApart(subject);
        bool isOut = !overview && model.TakenOut == subject;
        cardStatus.gameObject.SetActive(apart || isOut);
        if (apart || isOut)
        {
            cardStatusText.text = (apart ? CarExplorerHudText.StatusApart : CarExplorerHudText.StatusOut).Get(uzbek);
            float statusWidth = cardStatusText.GetPreferredValues(cardStatusText.text, 600f, 24f).x;
            CarExplorerHudStyle.Layout(cardStatus, Mathf.Ceil(statusWidth) + 18f, 22f);
        }
        // × clears the selection; while inspecting the card shows the inspected node, left by Exit inspect instead.
        cardClose.gameObject.SetActive(!overview && InspectTarget() == null);

        cardTitle.text = subject.Name(uzbek);
        cardContext.text = overview
            ? CarExplorerHudText.ViewNames[(int)model.View].Get(uzbek)
            : Context(subject, uzbek);

        string description = subject.Description(uzbek);
        if (overview)
            description = string.IsNullOrEmpty(description)
                ? CarExplorerHudText.OverviewHint.Get(uzbek)
                : description + "\n\n" + CarExplorerHudText.OverviewHint.Get(uzbek);
        cardDescription.gameObject.SetActive(!string.IsNullOrEmpty(description));
        cardDescription.text = description ?? string.Empty;

        // Facts: the material (if any), then the units and parts it holds, in one row.
        string material = overview || string.IsNullOrEmpty(subject.Material)
            ? string.Empty
            : CarExplorerHudText.MaterialName(subject.Material, uzbek);
        string count = string.Empty;
        if (overview)
            count = subject.Children.Count + CarExplorerHudText.SystemsSuffix.Get(uzbek) + "  ·  " +
                    subject.PartCount + CarExplorerHudText.PartsSuffix.Get(uzbek);
        else if (subject.Children.Count > 0)
            count = (subject.Children.Count != subject.PartCount
                        ? subject.Children.Count + CarExplorerHudText.UnitsSuffix.Get(uzbek) + "  ·  "
                        : string.Empty) + subject.PartCount + CarExplorerHudText.PartsSuffix.Get(uzbek);
        bool hasMaterial = material.Length > 0;
        cardFacts.gameObject.SetActive(hasMaterial || count.Length > 0);
        if (hasMaterial || count.Length > 0)
        {
            // The material in the primary colour (never parsed as markup); counts never break inside "182 parts".
            string facts = hasMaterial
                ? CarExplorerHudText.Material.Get(uzbek) + "  <color=#" + FactsValueColour + "><noparse>" + material +
                  "</noparse></color>"
                : string.Empty;
            if (count.Length > 0)
                facts += (hasMaterial ? "   ·   " : string.Empty) + count.Replace(' ', '\u00A0').Replace("\u00A0·\u00A0", " · ");
            cardFacts.text = facts;
        }

        // A node that cannot come apart (a part, a level already apart) offers no disabled Explode.
        bool movable = !overview && subject.Kind >= CarNodeKind.Assembly && CanOpen(subject);
        cardOpen.gameObject.SetActive(movable);
        if (movable)
        {
            bool takeOut = subject.Kind == CarNodeKind.Assembly && model.TakenOut != subject;
            SetActionLabel(cardOpen, (takeOut ? CarExplorerHudText.TakeOut : CarExplorerHudText.Explode).Get(uzbek));
            cardOpen.Interactable = true;
        }
        // Inspect: the desktop's inspect camera frames the node; on the inspected node's card it walks on again.
        bool inspectable = !overview && !model.IsXr;
        cardInspect.gameObject.SetActive(inspectable);
        if (inspectable)
        {
            bool inspecting = InspectTarget() == subject;
            SetActionLabel(cardInspect, (inspecting ? CarExplorerHudText.ExitInspect : CarExplorerHudText.Inspect)
                .Get(uzbek));
            cardInspect.Interactable = inspecting || model.TryGetNodeBounds(subject, out _);
        }
        cardCollapse.gameObject.SetActive(apart);
        if (apart) SetActionLabel(cardCollapse, CarExplorerHudText.Collapse.Get(uzbek));
        bool anyOut = model.TakenOut != null;
        cardPutBack.gameObject.SetActive(anyOut);
        if (anyOut) SetActionLabel(cardPutBack, CarExplorerHudText.PutBack.Get(uzbek));
        bool reset = overview && (anyOut || model.View != CarExplorerView.Normal);
        cardReset.gameObject.SetActive(reset);
        if (reset) SetActionLabel(cardReset, CarExplorerHudText.Reset.Get(uzbek));
        FitPrimaryActions(movable, inspectable);
        cardPrimaryRow.gameObject.SetActive(movable || inspectable);
        cardSecondaryRow.gameObject.SetActive(apart || anyOut || reset);
        cardActions.gameObject.SetActive(movable || inspectable || apart || anyOut || reset);

        if (animate && cardGroup.alpha > 0.5f) cardGroup.alpha = 0.35f;
    }

    // The node the inspect camera frames, or null (never in XR).
    private CarStructureNode InspectTarget() =>
        CarExplorerInspect.Active && model != null && !model.IsXr ? CarExplorerInspect.Target : null;

    private void ToggleInspect()
    {
        if (model == null || model.IsXr) return;
        CarStructureNode node = CardNode;
        if (node == null) return;
        if (InspectTarget() == node) CarExplorerInspect.RequestExit();
        else CarExplorerInspect.Request(node);
    }

    private static void SetActionLabel(CarExplorerHudButton button, string label)
    {
        button.Label.text = label;
        bool icon = button.Icon != null && button.Icon.gameObject.activeSelf;
        float width = button.Label.GetPreferredValues(label, 4000f, 40f).x + (icon ? 52f : 28f);
        button.GetComponent<LayoutElement>().preferredWidth = Mathf.Ceil(width);
        button.GetComponent<LayoutElement>().minWidth = Mathf.Min(Mathf.Ceil(width), 88f);
    }

    // Take out / Explode and Inspect share a row: when their labels do not fit with the icons (a narrow card, long
    // Uzbek labels), the icons go and the labels stay whole.
    private void FitPrimaryActions(bool open, bool inspect)
    {
        float cardWidth = xrLayout ? PlateWidth - Margin * 2f - 440f - Gap : Small ? CompactCardWidth : CardWidth;
        float room = cardWidth - cardLayout.padding.horizontal;
        bool icons = true;
        for (int pass = 0; pass < 2; pass++)
        {
            SetActionIcon(cardOpen, icons);
            SetActionIcon(cardInspect, icons);
            if (open) SetActionLabel(cardOpen, cardOpen.Label.text);
            if (inspect) SetActionLabel(cardInspect, cardInspect.Label.text);
            float needed = (open ? cardOpen.GetComponent<LayoutElement>().preferredWidth : 0f) +
                           (inspect ? cardInspect.GetComponent<LayoutElement>().preferredWidth : 0f) +
                           (open && inspect ? 8f : 0f);
            if (needed <= room || !icons) break;
            icons = false;
        }
    }

    private static void SetActionIcon(CarExplorerHudButton button, bool show)
    {
        if (button.Icon == null || button.Icon.gameObject.activeSelf == show) return;
        button.Icon.gameObject.SetActive(show);
        if (show) CarExplorerHudStyle.Stretch(button.Label.rectTransform, 38f, 14f);
        else CarExplorerHudStyle.Stretch(button.Label.rectTransform, 12f, 12f);
    }

    // "System · parent" under the card title.
    private string Context(CarStructureNode node, bool uzbek)
    {
        CarStructureNode parent = node.Parent;
        if (node.Kind == CarNodeKind.System) return parent != null ? parent.Name(uzbek) : string.Empty;
        string system = string.IsNullOrEmpty(node.SystemName) ? string.Empty
            : uzbek ? CarSystems.ToUzbek(node.SystemName) : node.SystemName;
        if (parent == null || parent.Kind <= CarNodeKind.System) return system;
        return string.IsNullOrEmpty(system) ? parent.Name(uzbek) : system + "  ·  " + parent.Name(uzbek);
    }

    // ------------------------------------------------------------------ hint, toast, help

    private void BuildHint()
    {
        hintPanel = CarExplorerHudStyle.Rect("Hint", screenRoot, new Vector2(0.5f, 0f));
        CarExplorerHudStyle.Panel(hintPanel, CarExplorerHudStyle.PanelFill, 12);
        hintRow = CarExplorerHudStyle.Rect("Keys", hintPanel, new Vector2(0.5f, 0.5f));
        var layout = hintRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        var fitter = hintRow.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        hintFader = AddFader(hintPanel, true);
        hintFader.Interactive = false;
        hintFader.Group.blocksRaycasts = false;
    }

    // XR controllers; on the desktop the inspect camera's controls while it orbits a node, the walking keys away
    // from the car, I to show the panels while they are hidden, and near the car the explorer's keys (F inspect
    // while something is selected). A small window gets the short sets.
    private HudKey[] DesiredHintSet()
    {
        if (model.IsXr) return CarExplorerHudText.HintXr;
        if (InspectTarget() != null) return CarExplorerHudText.HintInspect;
        bool small = Small;
        if (!near) return small ? CarExplorerHudText.HintFarCompact : CarExplorerHudText.HintFar;
        if (panelsHidden) return small ? CarExplorerHudText.HintHiddenCompact : CarExplorerHudText.HintHidden;
        if (model.Selected != null)
            return small ? CarExplorerHudText.HintNearSelectedCompact : CarExplorerHudText.HintNearSelected;
        return small ? CarExplorerHudText.HintNearCompact : CarExplorerHudText.HintNear;
    }

    private void RebuildHint()
    {
        HudKey[] set = DesiredHintSet();
        bool uzbek = Uzbek;
        hintSet = set;
        hintUzbek = uzbek;
        tabHintIndex = -1;
        while (hintItems.Count < set.Length)
        {
            RectTransform item = CarExplorerHudStyle.Rect("Key", hintRow);
            var itemLayout = item.gameObject.AddComponent<HorizontalLayoutGroup>();
            itemLayout.spacing = 7f;
            itemLayout.childAlignment = TextAnchor.MiddleLeft;
            itemLayout.childControlWidth = true;
            itemLayout.childControlHeight = true;
            itemLayout.childForceExpandWidth = false;
            itemLayout.childForceExpandHeight = false;
            Image chip = CarExplorerHudStyle.Image("Chip", item,
                CarExplorerHudStyle.Rounded(CarExplorerHudStyle.ChipRadius), CarExplorerHudStyle.KeyFill);
            var chipLayout = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipLayout.padding = new RectOffset(8, 8, 3, 3);
            chipLayout.childControlWidth = true;
            chipLayout.childControlHeight = true;
            chipLayout.childForceExpandWidth = false;
            CarExplorerHudStyle.Layout(chip, -1f, 26f);
            TextMeshProUGUI key = CarExplorerHudStyle.Text("Key", chip.transform, CaptionKeySize,
                CarExplorerHudStyle.TextPrimary, FontStyles.Bold, TextAlignmentOptions.Midline);
            TextMeshProUGUI label = CarExplorerHudStyle.Text("Action", item, CarExplorerHudStyle.LabelSize - 1f,
                CarExplorerHudStyle.TextSecondary, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            hintItems.Add(item);
            hintChips.Add(chip);
            hintKeys.Add(key);
            hintLabels.Add(label);
        }
        for (int i = 0; i < hintItems.Count; i++)
        {
            bool used = i < set.Length;
            hintItems[i].gameObject.SetActive(used);
            if (!used) continue;
            hintKeys[i].text = set[i].Key.Get(uzbek);
            hintLabels[i].text = set[i].Action.Get(uzbek);
            if (set[i].Key.English == "Tab") tabHintIndex = i;
        }
        cursorLocked = Cursor.lockState == CursorLockMode.Locked;
        ApplyTabHint();
        layoutDirty = true;
    }

    private const float CaptionKeySize = 15f;

    // While the cursor is locked the Tab key glows: it is how the panels become clickable.
    private void ApplyTabHint()
    {
        if (tabHintIndex < 0 || tabHintIndex >= hintChips.Count) return;
        bool locked = cursorLocked && !model.IsXr;
        hintChips[tabHintIndex].color = locked ? CarExplorerHudStyle.Accent : CarExplorerHudStyle.KeyFill;
        hintKeys[tabHintIndex].color = locked ? CarExplorerHudStyle.AccentText : CarExplorerHudStyle.TextPrimary;
        HudString tabAction = locked ? CarExplorerHudText.CursorLocked : CarExplorerHudText.CursorFree;
        hintLabels[tabHintIndex].text = tabAction.Get(hintUzbek);
        hintLabels[tabHintIndex].color = locked ? CarExplorerHudStyle.Accent : CarExplorerHudStyle.TextSecondary;
        layoutDirty = true;
    }

    private void BuildToast()
    {
        toastPanel = CarExplorerHudStyle.Rect("Greeting", screenRoot, new Vector2(0.5f, 1f));
        CarExplorerHudStyle.Panel(toastPanel, CarExplorerHudStyle.PanelFill);
        var layout = toastPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 12, 16, 16);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        var fitter = toastPanel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Image avatar = CarExplorerHudStyle.Image("Avatar", toastPanel, CarExplorerHudStyle.Rounded(20),
            CarExplorerHudStyle.Accent);
        CarExplorerHudStyle.Layout(avatar, 40f, 40f).minWidth = 40f;
        TextMeshProUGUI initial = CarExplorerHudStyle.Text("Initial", avatar.transform, 20f,
            CarExplorerHudStyle.AccentText, FontStyles.Bold, TextAlignmentOptions.Midline);
        CarExplorerHudStyle.Stretch(initial.rectTransform);
        initial.text = "R";

        RectTransform column = CarExplorerHudStyle.Rect("Text", toastPanel);
        var columnLayout = column.gameObject.AddComponent<VerticalLayoutGroup>();
        columnLayout.spacing = 4f;
        columnLayout.childControlWidth = true;
        columnLayout.childControlHeight = true;
        columnLayout.childForceExpandWidth = true;
        columnLayout.childForceExpandHeight = false;
        CarExplorerHudStyle.Layout(column, 0f, -1f, 1f);
        toastTitle = CarExplorerHudStyle.Text("Title", column, CarExplorerHudStyle.LabelSize,
            CarExplorerHudStyle.TextPrimary, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        toastBody = CarExplorerHudStyle.Text("Body", column, CarExplorerHudStyle.LabelSize,
            CarExplorerHudStyle.TextSecondary, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        toastBody.textWrappingMode = TextWrappingModes.Normal;
        toastBody.lineSpacing = 4f;

        CarExplorerHudButton close = CarExplorerHudButton.Create("Close", toastPanel,
            CarExplorerHudButton.Variant.Ghost, null, 0f, CarExplorerHudStyle.Icon("close"), 8);
        CarExplorerHudStyle.Layout(close, 32f, 32f).minWidth = 32f;
        close.Clicked = () => model?.DismissGreeting();
        toastFader = AddFader(toastPanel, false);
    }

    private void UpdateToast()
    {
        bool uzbek = Uzbek;
        toastTitle.text = CarExplorerHudText.GreetingTitle.Get(uzbek);
        toastBody.text = CarExplorerHudText.GreetingBody.Get(uzbek);
        Show(toastFader, model.GreetingShown);
        layoutDirty = true;
    }

    private void BuildHelp()
    {
        helpRoot = CarExplorerHudStyle.Rect("Help", screenRoot);
        CarExplorerHudStyle.Stretch(helpRoot);
        Image dim = CarExplorerHudStyle.Image("Dim", helpRoot, null, CarExplorerHudStyle.Dim, true);
        CarExplorerHudStyle.Stretch(dim.rectTransform);
        // Clicking around the panel closes it; the dim has no hover look of its own.
        var dimButton = dim.gameObject.AddComponent<CarExplorerHudButton>();
        dimButton.Style = CarExplorerHudButton.Variant.Ghost;
        dimButton.Clicked = () => SetHelp(false);

        helpPanel = CarExplorerHudStyle.Rect("Panel", helpRoot, new Vector2(0.5f, 0.5f));
        CarExplorerHudStyle.Panel(helpPanel, CarExplorerHudStyle.PanelFill);
        // Swallow clicks on the panel so they do not reach the dim behind it.
        var block = helpPanel.gameObject.AddComponent<CarExplorerHudButton>();
        block.Style = CarExplorerHudButton.Variant.Ghost;
        var layout = helpPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(32, 32, 24, 28);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = helpPanel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        helpPanel.sizeDelta = new Vector2(HelpWidth, 400f);

        RectTransform header = CarExplorerHudStyle.Rect("Header", helpPanel);
        CarExplorerHudStyle.Layout(header, -1f, 44f);
        helpTitle = CarExplorerHudStyle.Text("Title", header, 24f, CarExplorerHudStyle.TextPrimary, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft);
        CarExplorerHudStyle.Stretch(helpTitle.rectTransform, 0f, 48f);
        CarExplorerHudButton close = CarExplorerHudButton.Create("Close", header,
            CarExplorerHudButton.Variant.Ghost, null, 0f, CarExplorerHudStyle.Icon("close"), 8);
        RectTransform closeRect = (RectTransform)close.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 0.5f);
        closeRect.sizeDelta = new Vector2(36f, 36f);
        closeRect.anchoredPosition = new Vector2(8f, 0f);
        close.Clicked = () => SetHelp(false);

        helpSection = CarExplorerHudStyle.Text("Section", helpPanel, CarExplorerHudStyle.CaptionSize,
            CarExplorerHudStyle.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        helpSection.characterSpacing = 8f;
        CarExplorerHudStyle.Layout(helpSection, -1f, 32f);

        int rows = Mathf.Max(CarExplorerHudText.HelpDesktopKeys.Length, CarExplorerHudText.HelpXrKeys.Length);
        for (int i = 0; i < rows; i++)
        {
            RectTransform row = CarExplorerHudStyle.Rect("Row " + i, helpPanel);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 16f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            CarExplorerHudStyle.Layout(row, -1f, -1f, -1f, 34f);
            RectTransform keyCell = CarExplorerHudStyle.Rect("Key Cell", row);
            // A fixed key column: a long action wraps instead of squeezing it.
            CarExplorerHudStyle.Layout(keyCell, 120f, 30f).minWidth = 120f;
            Image chip = CarExplorerHudStyle.Image("Chip", keyCell,
                CarExplorerHudStyle.Rounded(CarExplorerHudStyle.ChipRadius), CarExplorerHudStyle.KeyFill);
            chip.rectTransform.anchorMin = new Vector2(0f, 0f);
            chip.rectTransform.anchorMax = new Vector2(0f, 1f);
            chip.rectTransform.pivot = new Vector2(0f, 0.5f);
            TextMeshProUGUI key = CarExplorerHudStyle.Text("Key", chip.transform, CarExplorerHudStyle.CaptionSize,
                CarExplorerHudStyle.TextPrimary, FontStyles.Bold, TextAlignmentOptions.Midline);
            CarExplorerHudStyle.Stretch(key.rectTransform, 8f, 8f);
            TextMeshProUGUI action = CarExplorerHudStyle.Text("Action", row, CarExplorerHudStyle.BodySize - 1f,
                CarExplorerHudStyle.TextSecondary, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            action.textWrappingMode = TextWrappingModes.Normal;
            CarExplorerHudStyle.Layout(action, -1f, -1f, 1f);
            helpRows.Add(row);
            helpKeys.Add(key);
            helpActions.Add(action);
        }

        helpFooter = CarExplorerHudStyle.Text("Footer", helpPanel, CarExplorerHudStyle.CaptionSize,
            CarExplorerHudStyle.TextTertiary, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        CarExplorerHudStyle.Layout(helpFooter, -1f, 36f);
        helpFader = AddFader(helpRoot, false);
        helpRoot.gameObject.SetActive(false);
    }

    private void RebuildHelp()
    {
        bool uzbek = Uzbek;
        bool xr = model.IsXr;
        HudKey[] keys = xr ? CarExplorerHudText.HelpXrKeys : CarExplorerHudText.HelpDesktopKeys;
        helpTitle.text = CarExplorerHudText.HelpTitle.Get(uzbek);
        helpSection.text = (xr ? CarExplorerHudText.HelpXr : CarExplorerHudText.HelpDesktop).Get(uzbek);
        helpFooter.text = (xr ? CarExplorerHudText.HelpCloseXr : CarExplorerHudText.HelpCloseDesktop).Get(uzbek);
        for (int i = 0; i < helpRows.Count; i++)
        {
            bool used = i < keys.Length;
            helpRows[i].gameObject.SetActive(used);
            if (!used) continue;
            helpKeys[i].text = keys[i].Key.Get(uzbek);
            helpActions[i].text = keys[i].Action.Get(uzbek);
            float width = helpKeys[i].GetPreferredValues(helpKeys[i].text, 400f, 30f).x + 18f;
            RectTransform chip = (RectTransform)helpKeys[i].transform.parent;
            chip.sizeDelta = new Vector2(Mathf.Ceil(Mathf.Max(34f, width)), 0f);
            chip.anchoredPosition = Vector2.zero;
        }
        layoutDirty = true;
    }

    public void SetHelp(bool open)
    {
        if (helpOpen == open) return;
        helpOpen = open;
        Show(helpFader, open);
        if (model != null) UpdateSwitchStates();
        layoutDirty = true;
    }

    // ------------------------------------------------------------------ model calls from the panels

    public bool IsApart(CarStructureNode node) => IndexInOpenPath(node) >= 0;

    private int IndexInOpenPath(CarStructureNode node)
    {
        if (node == null || model == null) return -1;
        IReadOnlyList<CarStructureNode> path = model.OpenPath;
        if (path == null) return -1;
        for (int i = 0; i < path.Count; i++)
            if (path[i] == node) return i;
        return -1;
    }

    /// <summary>
    /// True when Open would do something for the node: take its assembly out or take it apart a level.
    /// </summary>
    public bool CanOpen(CarStructureNode node)
    {
        if (model == null || node == null || node.Kind < CarNodeKind.Assembly || IsApart(node)) return false;
        return node.CanOpen || (node.Kind == CarNodeKind.Assembly && model.TakenOut != node);
    }

    public void HoverNode(CarStructureNode node)
    {
        if (node == hoverNode || model == null) return;
        hoverNode = node;
        model.Highlight(node);
    }

    public void SelectNode(CarStructureNode node) => model?.Select(node);

    public void OpenNode(CarStructureNode node)
    {
        if (model == null || node == null) return;
        if (model.Selected != node) model.Select(node);
        model.Open(node);
    }

    public void OnTreeSizeChanged() => layoutDirty = true;

    // The compact layout applies to the desktop only; the XR plate keeps its size.
    private bool Small => compact && !xrLayout;

    private void ApplyCompact()
    {
        bool small = Small;
        cardLayout.padding = small ? new RectOffset(18, 18, 14, 16) : new RectOffset(24, 24, 20, 24);
        cardLayout.spacing = small ? 6f : 8f;
        cardTitle.fontSize = small ? 24f : CarExplorerHudStyle.TitleSize;
        float control = small ? CompactControlHeight : ControlHeight;
        foreach (CarExplorerHudButton button in new[] { cardOpen, cardInspect, cardCollapse, cardPutBack, cardReset })
            button.GetComponent<LayoutElement>().preferredHeight = control;
        tree.SetNarrow(small);
        if (stateValid)
        {
            RebuildHint();
            RebuildCard(false);
        }
        ApplyTreeRule();
        layoutDirty = true;
    }

    // ------------------------------------------------------------------ per frame

    private void Update()
    {
        if (model == null || !built) return;
        bool xr = model.IsXr;
        if (!modeApplied || xr != xrLayout) ApplyMode(xr);
        if (dirty) Refresh();

        Keyboard keyboard = Keyboard.current;
        if (!xr && keyboard != null)
        {
            if (keyboard.hKey.wasPressedThisFrame) SetHelp(!helpOpen);
            if (keyboard.iKey.wasPressedThisFrame) SetPanelsHidden(!panelsHidden);
            if (keyboard.pKey.wasPressedThisFrame) ToggleTree();
        }

        UpdatePresence(xr);
        if (!xr)
        {
            bool small = scaler != null && scaler.Compact;
            if (small != compact)
            {
                compact = small;
                ApplyCompact();
            }
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (locked != cursorLocked)
            {
                cursorLocked = locked;
                ApplyTabHint();
            }
            Vector2 size = screenRoot.rect.size;
            if (size != lastScreenSize)
            {
                lastScreenSize = size;
                layoutDirty = true;
            }
        }
        else
        {
            PlacePlate(false);
        }
        if (layoutDirty) Relayout();
        StepFades(Time.unscaledDeltaTime);
        EnsureEventSystem(xr);
        CarExplorerModels.PointerOverHud = xr ? worldHover.Any && worldCanvas.gameObject.activeInHierarchy
            : screenHover.Any && !cursorLocked;
    }

    private void Refresh()
    {
        dirty = false;
        CarStructureNode root = model.Root;
        bool uzbek = model.Uzbek;
        bool rootChanged = root != lastRoot || !stateValid;
        bool languageChanged = uzbek != lastUzbek || !stateValid;
        bool viewChanged = model.View != lastView || !stateValid;
        bool greetingChanged = model.GreetingShown != lastGreeting || !stateValid;
        bool pathChanged = rootChanged || model.Selected != lastSelected || model.TakenOut != lastTakenOut ||
                           !SameOpenPath();
        bool selectionChanged = model.Selected != lastSelected;
        CarStructureNode inspectTarget = InspectTarget();
        bool inspectChanged = (inspectTarget != null) != lastInspecting || inspectTarget != lastInspectTarget ||
                              !stateValid;
        // Changed also fires for motion; when nothing the HUD shows differs there is nothing to do.
        if (!(rootChanged || languageChanged || viewChanged || greetingChanged || pathChanged || inspectChanged))
            return;

        if (rootChanged) tree.SetRoot(root);
        if (pathChanged) tree.SyncPath(model);
        if (languageChanged || rootChanged)
        {
            tree.RefreshAll();
            RebuildViewSwitch();
            RebuildTreeTab();
            RebuildHint();
            RebuildHelp();
        }
        else if (pathChanged) tree.RefreshStates();
        if (pathChanged || languageChanged || viewChanged || inspectChanged)
        {
            RebuildBreadcrumb();
            RebuildCard((selectionChanged || inspectChanged) && stateValid);
        }
        if (viewChanged || languageChanged) UpdateSwitchStates();
        if (greetingChanged || languageChanged) UpdateToast();
        if (selectionChanged && model.Selected != null) pendingReveal = model.Selected;
        if (pathChanged) ApplyTreeRule();
        if (hintSet != DesiredHintSet()) RebuildHint();

        stateValid = true;
        lastInspecting = inspectTarget != null;
        lastInspectTarget = inspectTarget;
        lastRoot = root;
        lastUzbek = uzbek;
        lastView = model.View;
        lastGreeting = model.GreetingShown;
        lastSelected = model.Selected;
        lastTakenOut = model.TakenOut;
        lastOpenPath.Clear();
        IReadOnlyList<CarStructureNode> path = model.OpenPath;
        if (path != null)
            for (int i = 0; i < path.Count; i++) lastOpenPath.Add(path[i]);
        layoutDirty = true;
    }

    private bool SameOpenPath()
    {
        IReadOnlyList<CarStructureNode> path = model.OpenPath;
        int count = path != null ? path.Count : 0;
        if (count != lastOpenPath.Count) return false;
        for (int i = 0; i < count; i++)
            if (path[i] != lastOpenPath[i]) return false;
        return true;
    }

    private void UpdatePresence(bool xr)
    {
        bool engaged = model.Selected != null || model.TakenOut != null || model.View != CarExplorerView.Normal ||
                       helpOpen || InspectTarget() != null;
        bool wasNear = near;
        Camera view = model.ViewCamera;
        if (engaged || carRoot == null || view == null) near = true;
        else
        {
            Vector3 offset = view.transform.position - carCentre;
            offset.y = 0f;
            float distance = offset.magnitude;
            near = distance < (near ? FarDistance : NearDistance);
        }
        if (xr)
        {
            Show(workspaceFader, true);
            Show(panelsButtonFader, false);
            if (near && !wasNear) platePlaced = false;
            Show(plateFader, near);
        }
        else
        {
            bool shown = near && !panelsHidden;
            if (Mathf.Approximately(workspaceFader.Target, 0f) == shown) layoutDirty = true;
            Show(workspaceFader, shown);
            Show(panelsButtonFader, near);
        }
        if (near != wasNear && !xr)
        {
            RebuildHint();
            PublishSafeViewport();
        }
    }

    private void Show(Fader fader, bool visible)
    {
        float target = visible ? 1f : 0f;
        if (Mathf.Approximately(fader.Target, target) && fader.Group.gameObject.activeSelf == visible) return;
        fader.Target = target;
        fader.Group.blocksRaycasts = visible && fader.Interactive;
        fader.Group.interactable = visible;
        if (visible && !fader.Group.gameObject.activeSelf)
        {
            fader.Group.alpha = 0f;
            fader.Group.gameObject.SetActive(true);
        }
    }

    private void StepFades(float deltaTime)
    {
        float step = deltaTime / CarExplorerHudStyle.FadeSeconds;
        for (int i = 0; i < faders.Count; i++)
        {
            Fader fader = faders[i];
            CanvasGroup group = fader.Group;
            if (Mathf.Approximately(group.alpha, fader.Target))
            {
                if (fader.Target <= 0f && group.gameObject.activeSelf) group.gameObject.SetActive(false);
                continue;
            }
            group.alpha = Mathf.MoveTowards(group.alpha, fader.Target, step);
        }
        if (cardGroup != null && cardGroup.alpha < 1f) cardGroup.alpha = Mathf.MoveTowards(cardGroup.alpha, 1f, step);
    }

    // ------------------------------------------------------------------ layout

    private void ApplyMode(bool xr)
    {
        modeApplied = true;
        xrLayout = xr;
        xrSince = Time.unscaledTime;
        screenCanvas.gameObject.SetActive(!xr);
        // The plate appears once it has been placed (PlacePlate), never for a frame at the world origin.
        worldCanvas.gameObject.SetActive(false);
        if (xr)
        {
            worldCanvas.worldCamera = model.ViewCamera;
            workspace.SetParent(plate, false);
            CarExplorerHudStyle.Stretch(workspace);
            hintPanel.SetParent(plate, false);
            // Remy's toast stays outside the plate, so it shows while the plate is hidden away from the car.
            toastPanel.SetParent(worldCanvas.transform, false);
            helpRoot.SetParent(plate, false);
            platePlaced = false;
            // The plate has room: its tree keeps its own header and starts open, as before.
            tree.SetHeaderShown(true);
            tree.SetCollapsed(false);
        }
        else
        {
            workspace.SetParent(screenRoot, false);
            hintPanel.SetParent(screenRoot, false);
            toastPanel.SetParent(screenRoot, false);
            helpRoot.SetParent(screenRoot, false);
            tree.SetHeaderShown(false);
        }
        CarExplorerHudStyle.Stretch(workspace);
        CarExplorerHudStyle.Stretch(helpRoot);
        hintPanel.SetAsLastSibling();
        panelsButtonPanel.SetAsLastSibling();
        toastPanel.SetAsLastSibling();
        helpRoot.SetAsLastSibling();
        ApplyPanelsButton();
        ApplyCompact();
        if (stateValid) RebuildHelp();
        layoutDirty = true;
    }

    private float CurrentMargin => Small ? CompactMargin : Margin;
    private float CurrentGap => Small ? CompactGap : Gap;

    private void Relayout()
    {
        layoutDirty = false;
        bool xr = xrLayout;
        bool small = Small;
        float width = xr ? PlateWidth : screenRoot.rect.width;
        float height = xr ? PlateHeight : screenRoot.rect.height;
        float margin = CurrentMargin;
        float gap = CurrentGap;

        // Top bar: the STRUCTURE tab (desktop) and the breadcrumb left, the view switch centred (beside the language
        // in a small window and on the plate), the language and help right.
        languagePanel.anchorMin = languagePanel.anchorMax = languagePanel.pivot = new Vector2(1f, 1f);
        languagePanel.anchoredPosition = new Vector2(-margin, -margin);
        if (xr || small)
        {
            viewPanel.anchorMin = viewPanel.anchorMax = viewPanel.pivot = new Vector2(1f, 1f);
            viewPanel.anchoredPosition = new Vector2(-margin - languagePanel.sizeDelta.x - gap, -margin);
        }
        else
        {
            viewPanel.anchorMin = viewPanel.anchorMax = viewPanel.pivot = new Vector2(0.5f, 1f);
            viewPanel.anchoredPosition = new Vector2(0f, -margin);
        }
        if (treeTabPanel.gameObject.activeSelf == xr) treeTabPanel.gameObject.SetActive(!xr);
        float crumbLeft = margin;
        if (!xr)
        {
            SetTopLeft(treeTabPanel, margin, margin);
            crumbLeft += treeTabPanel.sizeDelta.x + gap;
        }
        SetTopLeft(breadcrumbPanel, crumbLeft, margin);
        if (stateValid) LayoutBreadcrumb();

        float bottomY = xr ? 20f : margin;
        LayoutBottomRow(width, margin, gap, bottomY);

        float top = margin + BarHeight + gap;
        float bottom = bottomY + HintHeight + gap;
        float available = Mathf.Max(160f, height - top - bottom);

        // The tree: the whole height on the plate; on the desktop at most TreeRows rows, and only while open.
        float treeWidth, treeHeight;
        if (xr)
        {
            treeWidth = 440f;
            treeHeight = !tree.Collapsed ? available : Mathf.Min(available, tree.PreferredHeight);
            if (!tree.Panel.gameObject.activeSelf) tree.Panel.gameObject.SetActive(true);
        }
        else
        {
            treeWidth = small ? CompactTreeWidth
                : Mathf.Clamp(Mathf.Round(width * TreeWidthShare / 8f) * 8f, TreeMinWidth, TreeMaxWidth);
            float most = tree.HeightForRows(small ? CompactTreeRows : TreeRows);
            treeHeight = Mathf.Min(available, Mathf.Min(most, tree.PreferredHeight));
            if (tree.Panel.gameObject.activeSelf == tree.Collapsed) tree.Panel.gameObject.SetActive(!tree.Collapsed);
        }
        SetTopLeft(tree.Panel, margin, top);
        tree.Panel.sizeDelta = new Vector2(treeWidth, treeHeight);
        tree.SetWidth(treeWidth);
        float rowsHeight = treeHeight - tree.TopInset - CarExplorerHudTree.BottomPadding;
        if (!tree.Collapsed) tree.SetViewportHeight(rowsHeight);

        float cardWidth = xr ? width - margin * 2f - treeWidth - Gap : small ? CompactCardWidth : CardWidth;
        cardPanel.anchorMin = cardPanel.anchorMax = cardPanel.pivot = new Vector2(1f, 1f);
        cardPanel.anchoredPosition = new Vector2(-margin, -top);
        cardPanel.sizeDelta = new Vector2(cardWidth, cardPanel.sizeDelta.y);
        FitCard(available, cardWidth - cardLayout.padding.horizontal);

        // Remy's toast: top centre below the top bar on the desktop (at the top while the panels are hidden), above
        // the plate in XR.
        float toastWidth = xr ? ToastWidth : Mathf.Min(small ? CompactToastWidth : ToastWidth, width - margin * 2f);
        toastPanel.anchorMin = toastPanel.anchorMax = new Vector2(0.5f, 1f);
        toastPanel.pivot = new Vector2(0.5f, xr ? 0f : 1f);
        bool panels = workspaceFader.Target > 0f;
        toastPanel.anchoredPosition = new Vector2(0f, xr ? 16f : -(panels ? top : margin));
        toastPanel.sizeDelta = new Vector2(toastWidth, toastPanel.sizeDelta.y);
        LayoutRebuilder.ForceRebuildLayoutImmediate(toastPanel);

        helpPanel.sizeDelta = new Vector2(Mathf.Min(HelpWidth, width - margin * 2f), helpPanel.sizeDelta.y);
        LayoutRebuilder.ForceRebuildLayoutImmediate(helpPanel);
        float helpScale = Mathf.Min(1f, (height - margin * 2f) / Mathf.Max(1f, helpPanel.rect.height));
        helpPanel.localScale = new Vector3(helpScale, helpScale, 1f);

        if (pendingReveal != null)
        {
            tree.Reveal(pendingReveal);
            pendingReveal = null;
        }
        PublishSafeViewport();
    }

    // The bottom row: the hint centred on the screen when it has the room, else centred between the explore chip
    // (left, desktop) and the eye button (right, desktop), and scaled down if even that is too narrow.
    private void LayoutBottomRow(float width, float margin, float gap, float y)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(hintRow);
        float hintWidth = hintRow.rect.width + 32f;
        hintPanel.anchorMin = hintPanel.anchorMax = hintPanel.pivot = new Vector2(0.5f, 0f);
        hintPanel.sizeDelta = new Vector2(hintWidth, HintHeight);
        float left = margin, right = width - margin;
        if (!xrLayout)
        {
            if (chipPanel.gameObject.activeSelf)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(chipPanel);
                chipPanel.anchorMin = chipPanel.anchorMax = chipPanel.pivot = Vector2.zero;
                chipPanel.anchoredPosition = new Vector2(margin, y);
                if (workspaceFader.Target > 0f) left = margin + chipPanel.rect.width + gap;
            }
            panelsButtonPanel.anchorMin = panelsButtonPanel.anchorMax = panelsButtonPanel.pivot = new Vector2(1f, 0f);
            panelsButtonPanel.anchoredPosition = new Vector2(-margin, y);
            if (panelsButtonFader.Target > 0f) right = width - margin - panelsButtonPanel.sizeDelta.x - gap;
        }
        float scale = Mathf.Min(1f, Mathf.Max(1f, right - left) / hintWidth);
        float half = hintWidth * scale * 0.5f;
        float centre = width * 0.5f;
        if (centre - half < left - 0.5f || centre + half > right + 0.5f) centre = (left + right) * 0.5f;
        hintPanel.anchoredPosition = new Vector2(centre - width * 0.5f, y);
        hintPanel.localScale = new Vector3(scale, scale, 1f);
    }

    /// <summary>
    /// Publishes CarExplorerModels.HudSafeViewport: on the desktop near the car, the view below the top bar, above the
    /// bottom row (hint, chip, eye button), left of the card's column, right of the tree's while it is open, and below
    /// Remy's toast while it shows, with a gap to every panel; with the panels hidden (I) everything above the bottom
    /// row. The explorer's commands select what they move, so the card's column is kept clear before anything is
    /// selected too: what the explorer places then is not covered when the card appears. The whole view in XR,
    /// without a model and while the panels are hidden away from the car, except that Remy's toast is kept
    /// clear while it shows.
    /// </summary>
    private void PublishSafeViewport()
    {
        Rect safe = new Rect(0f, 0f, 1f, 1f);
        float width = screenRoot != null ? screenRoot.rect.width : 0f;
        float height = screenRoot != null ? screenRoot.rect.height : 0f;
        bool toast = toastFader.Target > 0f && toastPanel.parent == screenRoot;
        if (model != null && !xrLayout && (near || toast) && screenCanvas.gameObject.activeSelf && width > 1f &&
            height > 1f)
        {
            float margin = CurrentMargin, gap = CurrentGap;
            bool panels = near && workspaceFader.Target > 0f;
            float top = panels ? margin + BarHeight + gap : 0f;
            float left = 0f, right = width;
            if (panels && !tree.Collapsed) left = margin + tree.Panel.sizeDelta.x + gap;
            if (panels) right = width - margin - cardPanel.sizeDelta.x - gap;
            float bottom = near ? margin + HintHeight + gap : 0f;
            // Remy's greeting toast hangs under the top bar in the middle of the view: while it shows, the band
            // starts below it (also while the panels are hidden away from the car).
            if (toast)
                top = Mathf.Max(top, -toastPanel.anchoredPosition.y + toastPanel.rect.height + gap);
            // A window too small for a free band still gets one, centred where the band would be.
            float minWidth = width * 0.2f, minHeight = height * 0.3f;
            if (right - left < minWidth)
            {
                float middle = (left + right) * 0.5f;
                left = middle - minWidth * 0.5f;
                right = middle + minWidth * 0.5f;
            }
            float upper = height - top;
            if (upper - bottom < minHeight)
            {
                float middle = (upper + bottom) * 0.5f;
                bottom = middle - minHeight * 0.5f;
                upper = middle + minHeight * 0.5f;
            }
            safe = Rect.MinMaxRect(Mathf.Clamp01(left / width), Mathf.Clamp01(bottom / height),
                Mathf.Clamp01(right / width), Mathf.Clamp01(upper / height));
        }
        if (CarExplorerModels.HudSafeViewport != safe) CarExplorerModels.HudSafeViewport = safe;
    }

    private static readonly string ClampLines = BuildClampLines();

    private static string BuildClampLines()
    {
        var lines = new System.Text.StringBuilder("X");
        for (int i = 1; i < DescriptionLines; i++) lines.Append("\nX");
        return lines.ToString();
    }

    // The card grows with its text. On the desktop a description longer than three lines is cut there with an
    // ellipsis until "More" is pressed; any description too long for the screen is cut to fit.
    private void FitCard(float available, float innerWidth)
    {
        if (!cardPanel.gameObject.activeSelf) return;
        cardDescriptionLayout.preferredHeight = -1f;
        cardDescription.overflowMode = TextOverflowModes.Overflow;
        bool hasText = cardDescription.gameObject.activeSelf;
        bool longText = false;
        float clampHeight = 0f;
        if (hasText && !xrLayout && innerWidth > 1f)
        {
            descriptionProbe.fontSize = cardDescription.fontSize;
            float full = descriptionProbe.GetPreferredValues(cardDescription.text, innerWidth, 10000f).y;
            clampHeight = Mathf.Ceil(descriptionProbe.GetPreferredValues(ClampLines, innerWidth, 10000f).y);
            longText = full > clampHeight + 2f;
        }
        GameObject moreRow = cardMore.transform.parent.gameObject;
        if (moreRow.activeSelf != longText) moreRow.SetActive(longText);
        if (longText)
        {
            string label = (descriptionExpanded ? CarExplorerHudText.Less : CarExplorerHudText.More).Get(Uzbek);
            if (cardMore.Label.text != label) cardMore.Label.text = label;
            CarExplorerHudStyle.Layout(cardMore, cardMore.PreferredWidth(56f) - 16f, 26f);
            if (!descriptionExpanded)
            {
                cardDescriptionLayout.preferredHeight = clampHeight;
                cardDescription.overflowMode = TextOverflowModes.Ellipsis;
            }
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(cardPanel);
        float excess = cardPanel.rect.height - available;
        if (excess <= 0f || !hasText) return;
        float lineHeight = CarExplorerHudStyle.BodySize * 1.5f;
        float fitted = cardDescription.rectTransform.rect.height - excess;
        cardDescriptionLayout.preferredHeight = Mathf.Max(lineHeight * 2f, fitted);
        cardDescription.overflowMode = TextOverflowModes.Ellipsis;
        LayoutRebuilder.ForceRebuildLayoutImmediate(cardPanel);
    }

    private static void SetTopLeft(RectTransform rect, float left, float top)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
    }

    // The plate stands beside the car as the visitor sees it, at chest height, turned to face them. It stays put
    // until the visitor has turned or walked far enough that it would sit badly, then glides to the new spot.
    private void PlacePlate(bool snap)
    {
        Camera view = model.ViewCamera;
        if (view == null) return;
        if (worldCanvas.worldCamera != view) worldCanvas.worldCamera = view;
        Transform head = view.transform;
        Vector3 eye = head.position;
        Vector3 forward = head.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = head.up;
        forward.y = 0f;
        forward.Normalize();
        Vector3 toCar = carRoot != null ? carCentre - eye : forward;
        toCar.y = 0f;
        float carDistance = toCar.magnitude;
        // Near the car the plate stands to the left of it, just clear of the car as the visitor sees it; away from
        // the car, it stays ahead and to the left of the visitor.
        bool besideCar = carRoot != null && carDistance > 0.5f && carDistance < 7f &&
                         Vector3.Dot(toCar / carDistance, forward) > 0.2f;
        Vector3 anchor = besideCar ? toCar / carDistance : forward;
        float yaw = PlateYaw;
        if (besideCar)
        {
            float carHalf = Mathf.Atan2(carRadius, carDistance) * Mathf.Rad2Deg;
            float plateHalf = Mathf.Atan2(PlateWidth * PlateUnit * 0.5f, PlateReach) * Mathf.Rad2Deg;
            yaw = Mathf.Clamp(carHalf + plateHalf + 3f, PlateYaw, PlateMaxYaw);
        }
        Vector3 direction = Quaternion.Euler(0f, -yaw, 0f) * anchor;
        Vector3 target = eye + direction * PlateReach;
        target.y = eye.y - PlateDrop;
        Quaternion rotation = Quaternion.LookRotation(target - eye, Vector3.up);
        Vector3 euler = rotation.eulerAngles;
        rotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), -5f, 25f), euler.y, 0f);

        Transform plateTransform = worldCanvas.transform;
        if (snap || !platePlaced)
        {
            plateTransform.SetPositionAndRotation(target, rotation);
            platePlaced = true;
            plateMoving = false;
            plateVelocity = Vector3.zero;
            if (!worldCanvas.gameObject.activeSelf) worldCanvas.gameObject.SetActive(true);
            return;
        }
        Vector3 toPlate = plateTransform.position - eye;
        float distance = toPlate.magnitude;
        Vector3 flat = toPlate;
        flat.y = 0f;
        float angle = flat.sqrMagnitude > 1e-4f ? Vector3.Angle(forward, flat) : 0f;
        bool offTarget = Vector3.Distance(plateTransform.position, target) > 1.4f;
        if (!plateMoving && (angle > 80f || distance > 2.1f || distance < 0.55f || offTarget)) plateMoving = true;
        if (!plateMoving) return;
        float deltaTime = Time.unscaledDeltaTime;
        Vector3 position = Vector3.SmoothDamp(plateTransform.position, target, ref plateVelocity, 0.28f, 6f, deltaTime);
        Quaternion turned = Quaternion.Slerp(plateTransform.rotation, rotation, 1f - Mathf.Exp(-10f * deltaTime));
        plateTransform.SetPositionAndRotation(position, turned);
        if (Vector3.Distance(position, target) < 0.02f && Quaternion.Angle(turned, rotation) < 1f) plateMoving = false;
    }

    // ------------------------------------------------------------------ input

    // The walkthrough's desktop needs a mouse-driven EventSystem; in XR the rig's interactors register with an XR UI
    // module (XRI makes one if none exists). Only when no EventSystem is active does the HUD add its own.
    private void EnsureEventSystem(bool xr)
    {
        float now = Time.unscaledTime;
        if (now < nextEventSystemCheck) return;
        nextEventSystemCheck = now + 0.5f;
        EventSystem current = EventSystem.current;
        if (current != null && current.isActiveAndEnabled)
        {
            if (!xr)
            {
                // After the XR fallback the rig's XR UI module may remain; let it read the mouse too.
                if (current.TryGetComponent(out XRUIInputModule xrModule) && xrModule.enabled &&
                    !xrModule.enableMouseInput)
                    xrModule.enableMouseInput = true;
            }
            return;
        }
        if (xr && now - xrSince < 1.5f) return;
        if (ownEventSystem != null)
        {
            ownEventSystem.SetActive(true);
            return;
        }
        ownEventSystem = new GameObject(EventSystemName);
        ownEventSystem.transform.SetParent(transform, false);
        ownEventSystem.AddComponent<EventSystem>().sendNavigationEvents = false;
        if (xr) ownEventSystem.AddComponent<XRUIInputModule>();
        else ownEventSystem.AddComponent<InputSystemUIInputModule>();
    }
}
