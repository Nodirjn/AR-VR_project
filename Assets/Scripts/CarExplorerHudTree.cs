using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The STRUCTURE panel of the Car Explorer HUD: the vehicle, its systems, assemblies, sub-assemblies and parts as an
/// expandable tree. Only the rows in view exist, a pool re-bound as the list scrolls (row k always shows the indices
/// k, k + pool, ...), so scrolling one row rebinds one row and a tree of a thousand nodes costs what a short one does.
/// The path to the selection, the assembly that is out and every level that is apart open by themselves. A name too
/// long for its row is cut so that it still tells the row from its siblings ("Rocker Arm A… 5"); fitted names are
/// cached per node, language and width. On the XR plate the panel has its own header (STRUCTURE, the part count and a
/// chevron that folds it to that line); on the desktop the HUD's STRUCTURE tab in the top bar opens and closes it, so
/// the panel is the list alone and is hidden while collapsed.
/// </summary>
public sealed class CarExplorerHudTree
{
    public const float RowHeight = 32f;
    public const float HeaderHeight = 48f;
    public const float BottomPadding = 8f;
    // Above the first row when the panel has no header (the desktop).
    private const float ListTop = 8f;
    // Systems and assemblies step in 14 units a level, deeper levels 10, so a part six levels down keeps its name.
    private const float Indent = 14f;
    private const float DeepIndent = 10f;
    private const float RowInset = 8f;
    // Name column: after the chevron or bullet, and before the count, the out / apart dot and the explode button.
    private const float NameLead = 26f;
    private const float BranchTrail = 100f;
    // A narrow panel (the HUD's compact layout) drops the part-count column, so names keep 36 units more.
    private const float NarrowBranchTrail = 64f;
    private const float LeafTrail = 28f;
    public const string Ellipsis = "…";
    private const float DoubleClickSeconds = 0.4f;

    public readonly RectTransform Panel;
    private readonly CarExplorerHud hud;
    private readonly RectTransform viewport;
    private readonly RectTransform content;
    private readonly ScrollRect scroll;
    private readonly Image thumb;
    private readonly TextMeshProUGUI title;
    private readonly TextMeshProUGUI total;
    private readonly List<CarExplorerHudRow> rows = new List<CarExplorerHudRow>();
    private readonly List<int> rowIndex = new List<int>();
    private readonly List<CarStructureNode> visible = new List<CarStructureNode>();
    private readonly HashSet<CarStructureNode> pathOpen = new HashSet<CarStructureNode>();
    private readonly HashSet<CarStructureNode> userOpen = new HashSet<CarStructureNode>();
    private readonly HashSet<CarStructureNode> userClosed = new HashSet<CarStructureNode>();
    private CarStructureNode root;
    private CarStructureNode lastClicked;
    private float lastClickTime = -10f;
    private CarExplorerHudRow hoveredRow;
    private float viewportHeight;
    private bool textUzbek;
    private readonly CarExplorerHudButton header;
    private readonly Image divider;
    private bool collapsed;
    private bool headerShown = true;
    private bool narrow;
    private float panelWidth;
    // Never drawn: measures names for fitting (measuring a shown text would replace what it draws).
    private readonly TextMeshProUGUI probe;
    private readonly Dictionary<CarStructureNode, FittedName> fitted = new Dictionary<CarStructureNode, FittedName>();

    private struct FittedName
    {
        public bool Uzbek;
        public float Width;
        public string Text;
    }

    public CarExplorerHudTree(CarExplorerHud owner, Transform parent)
    {
        hud = owner;
        Panel = CarExplorerHudStyle.Rect("Structure", parent, new Vector2(0f, 1f));
        CarExplorerHudStyle.Panel(Panel, CarExplorerHudStyle.PanelFill);

        title = CarExplorerHudStyle.Text("Title", Panel, CarExplorerHudStyle.CaptionSize,
            CarExplorerHudStyle.TextSecondary, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 8f;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.offsetMin = new Vector2(20f, -HeaderHeight);
        titleRect.offsetMax = new Vector2(-120f, 0f);

        total = CarExplorerHudStyle.Text("Total", Panel, CarExplorerHudStyle.CaptionSize,
            CarExplorerHudStyle.TextTertiary, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
        RectTransform totalRect = total.rectTransform;
        totalRect.anchorMin = new Vector2(1f, 1f);
        totalRect.anchorMax = new Vector2(1f, 1f);
        totalRect.pivot = new Vector2(1f, 1f);
        totalRect.sizeDelta = new Vector2(140f, HeaderHeight);
        totalRect.anchoredPosition = new Vector2(-44f, 0f);

        // The header folds the panel to one line, for more of the car.
        header = CarExplorerHudButton.Create("Header", Panel, CarExplorerHudButton.Variant.Ghost, null, 0f,
            CarExplorerHudStyle.Icon("chevron"), 10);
        RectTransform headerRect = (RectTransform)header.transform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.offsetMin = new Vector2(6f, -HeaderHeight + 4f);
        headerRect.offsetMax = new Vector2(-6f, -4f);
        headerRect.SetSiblingIndex(title.transform.GetSiblingIndex());
        RectTransform chevronRect = header.Icon.rectTransform;
        chevronRect.anchorMin = chevronRect.anchorMax = new Vector2(1f, 0.5f);
        chevronRect.anchoredPosition = new Vector2(-18f, 0f);
        chevronRect.sizeDelta = new Vector2(18f, 18f);
        chevronRect.localEulerAngles = new Vector3(0f, 0f, -90f);
        header.IdleContent = CarExplorerHudStyle.TextTertiary;
        header.Apply();
        header.Clicked = ToggleCollapsed;

        divider = CarExplorerHudStyle.Image("Divider", Panel, null, CarExplorerHudStyle.Divider);
        RectTransform dividerRect = divider.rectTransform;
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.offsetMin = new Vector2(12f, -HeaderHeight - 1f);
        dividerRect.offsetMax = new Vector2(-12f, -HeaderHeight);

        viewport = CarExplorerHudStyle.Rect("Viewport", Panel);
        CarExplorerHudStyle.Stretch(viewport, 0f, 0f, HeaderHeight + 4f, BottomPadding);
        viewport.gameObject.AddComponent<RectMask2D>();
        // Catches wheel and drag events over the empty part of the list.
        Image hit = CarExplorerHudStyle.Image("Hit", viewport, null, CarExplorerHudStyle.Clear, true);
        CarExplorerHudStyle.Stretch(hit.rectTransform);

        content = CarExplorerHudStyle.Rect("Rows", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, 0f);

        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.08f;
        // Three rows per wheel notch (the Input System UI module reports 6 per notch).
        scroll.scrollSensitivity = RowHeight * 0.5f;
        scroll.onValueChanged.AddListener(OnScrolled);

        thumb = CarExplorerHudStyle.Image("Scroll Thumb", Panel, CarExplorerHudStyle.Rounded(2),
            new Color(1f, 1f, 1f, 0.18f));
        RectTransform thumbRect = thumb.rectTransform;
        thumbRect.anchorMin = thumbRect.anchorMax = new Vector2(1f, 1f);
        thumbRect.pivot = new Vector2(1f, 1f);
        thumb.enabled = false;

        probe = CarExplorerHudStyle.Text("Measure", Panel, CarExplorerHudStyle.RowSize, CarExplorerHudStyle.Clear);
        probe.enabled = false;
    }

    /// <summary>Called by the HUD after it sized the panel: names are fitted to the new width.</summary>
    public void SetWidth(float width)
    {
        if (Mathf.Approximately(panelWidth, width)) return;
        panelWidth = width;
        Rebind(true);
    }

    /// <summary>
    /// The narrow layout (small windows): rows show no part count, which leaves the names more room. Re-fits every name.
    /// </summary>
    public void SetNarrow(bool value)
    {
        if (narrow == value) return;
        narrow = value;
        fitted.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].TextDepth = -1;
            rows[i].TextWidth = -1f;
        }
        Rebind(true);
    }

    public bool Narrow => narrow;

    private float Trail(bool branch) => branch ? (narrow ? NarrowBranchTrail : BranchTrail) : LeafTrail;

    /// <summary>Left edge of a row's chevron or bullet at this depth.</summary>
    public static float IndentOf(int depth) =>
        RowInset + Mathf.Min(depth, 2) * Indent + Mathf.Max(0, depth - 2) * DeepIndent;

    /// <summary>The text the node's row shows in that language: its name, fitted to the row (for tests too).</summary>
    public string LabelFor(CarStructureNode node, bool uzbek) => node == null ? string.Empty : Fitted(node, uzbek);

    /// <summary>Width the node's name has in its row at the current panel width; 0 before the first layout.</summary>
    public float NameWidth(CarStructureNode node)
    {
        if (panelWidth <= 0f || node == null) return 0f;
        return panelWidth - IndentOf(node.Depth) - NameLead - Trail(node.Children.Count > 0);
    }

    private string Fitted(CarStructureNode node, bool uzbek)
    {
        float width = NameWidth(node);
        if (width <= 0f) return node.Name(uzbek);
        if (fitted.TryGetValue(node, out FittedName cached) && cached.Uzbek == uzbek &&
            Mathf.Approximately(cached.Width, width))
            return cached.Text;
        probe.fontStyle = node.Kind == CarNodeKind.Vehicle ? FontStyles.Bold : FontStyles.Normal;
        // A unit of slack against rounding, so TMP's own ellipsis never cuts a fitted name again.
        string text = FitName(probe, node, uzbek, width - 1f);
        fitted[node] = new FittedName { Uzbek = uzbek, Width = width, Text = text };
        return text;
    }

    /// <summary>Single-line width of the text in the probe's font, size and style.</summary>
    public static float Measure(TMP_Text probe, string text) =>
        string.IsNullOrEmpty(text) ? 0f : probe.GetPreferredValues(text).x;

    /// <summary>
    /// The node's name fitted to one line of `width` units in `probe`'s font: whole when it fits; otherwise cut with
    /// an ellipsis that keeps the words telling it from its siblings. A name that parts from them early keeps its
    /// start ("Front Left Suspensi…"); one that parts only near the end keeps the end after a middle ellipsis
    /// ("Rocker Arm A… 5", "Wheel stud M1… 3"). Two siblings with different names never read the same.
    /// </summary>
    public static string FitName(TMP_Text probe, CarStructureNode node, bool uzbek, float width)
    {
        string name = node.Name(uzbek) ?? string.Empty;
        if (name.Length < 2 || Measure(probe, name) <= width) return name;

        // How far the name runs together with its most similar sibling, from the start and from the end.
        int prefix = 0, suffix = 0;
        CarStructureNode parent = node.Parent;
        if (parent != null)
        {
            List<CarStructureNode> siblings = parent.Children;
            for (int i = 0; i < siblings.Count; i++)
            {
                if (siblings[i] == node) continue;
                string other = siblings[i].Name(uzbek);
                if (string.IsNullOrEmpty(other) || other == name) continue;
                prefix = Mathf.Max(prefix, CommonPrefix(name, other));
                suffix = Mathf.Max(suffix, CommonSuffix(name, other));
            }
        }
        int length = name.Length;

        // The start, cut after the word where the name first parts from its siblings.
        int firstDiffer = Mathf.Min(prefix, length - 1);
        int wordEnd = name.IndexOf(' ', firstDiffer);
        if (wordEnd < 0) wordEnd = length;
        int head = LongestHead(probe, name, string.Empty, width, length - 1);
        if (head >= wordEnd) return Head(name, head) + Ellipsis;

        // Otherwise the end, from the word where it last parts from them, after a middle ellipsis.
        int lastDiffer = Mathf.Clamp(length - 1 - suffix, 0, length - 1);
        int tailStart = name.LastIndexOf(' ', lastDiffer);
        if (tailStart <= 0) tailStart = lastDiffer;
        string tail = name.Substring(tailStart);
        int kept = LongestHead(probe, name, tail, width, tailStart);
        if (kept > 0) return Head(name, kept) + Ellipsis + tail;

        // Very narrow: as much of the end as fits, still from the last differing character.
        for (int start = tailStart; start <= lastDiffer; start++)
        {
            string end = Ellipsis + name.Substring(start).TrimStart();
            if (Measure(probe, end) <= width || start == lastDiffer) return end;
        }
        return Ellipsis + name.Substring(lastDiffer);
    }

    // The longest start of the name (at most `limit` characters) that fits with the ellipsis and `tail`; -1 if none.
    private static int LongestHead(TMP_Text probe, string name, string tail, float width, int limit)
    {
        int low = 1, high = Mathf.Min(limit, name.Length), best = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (Measure(probe, Head(name, mid) + Ellipsis + tail) <= width)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return best;
    }

    private static string Head(string name, int length) => name.Substring(0, length).TrimEnd();

    private static int CommonPrefix(string a, string b)
    {
        int n = Mathf.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    private static int CommonSuffix(string a, string b)
    {
        int n = Mathf.Min(a.Length, b.Length), i = 0;
        while (i < n && a[a.Length - 1 - i] == b[b.Length - 1 - i]) i++;
        return i;
    }

    /// <summary>Height above the first row: the header on the plate, a small inset on the desktop.</summary>
    public float TopInset => headerShown ? HeaderHeight + 4f : ListTop;

    /// <summary>Height the panel needs to show every visible row (just the header while folded).</summary>
    public float PreferredHeight =>
        collapsed ? HeaderHeight : TopInset + Mathf.Max(1, visible.Count) * RowHeight + BottomPadding;

    /// <summary>Height of the panel that shows `rows` rows.</summary>
    public float HeightForRows(int rows) => TopInset + rows * RowHeight + BottomPadding;

    public bool Collapsed => collapsed;

    public bool HeaderShown => headerShown;

    public void ToggleCollapsed() => SetCollapsed(!collapsed);

    public void SetCollapsed(bool fold)
    {
        if (collapsed == fold) return;
        collapsed = fold;
        viewport.gameObject.SetActive(!collapsed);
        divider.enabled = !collapsed && headerShown;
        if (collapsed) thumb.enabled = false;
        header.Icon.rectTransform.localEulerAngles = new Vector3(0f, 0f, collapsed ? 0f : -90f);
        hud.OnTreeSizeChanged();
    }

    /// <summary>The panel's own header (the XR plate) or the list alone under the desktop's STRUCTURE tab.</summary>
    public void SetHeaderShown(bool shown)
    {
        if (headerShown == shown) return;
        headerShown = shown;
        header.gameObject.SetActive(shown);
        title.gameObject.SetActive(shown);
        total.gameObject.SetActive(shown);
        divider.enabled = shown && !collapsed;
        CarExplorerHudStyle.Stretch(viewport, 0f, 0f, TopInset, BottomPadding);
        hud.OnTreeSizeChanged();
    }

    public int VisibleCount => visible.Count;

    /// <summary>Row objects in the pool (for tests: stays near the number of rows in view).</summary>
    public int PoolSize => rows.Count;

    /// <summary>The visible rows' nodes, top to bottom (for tests).</summary>
    public IReadOnlyList<CarStructureNode> VisibleNodes => visible;

    /// <summary>A new tree from the explorer: everything closed except the vehicle.</summary>
    public void SetRoot(CarStructureNode node)
    {
        root = node;
        userOpen.Clear();
        userClosed.Clear();
        pathOpen.Clear();
        fitted.Clear();
        lastClicked = null;
        content.anchoredPosition = Vector2.zero;
        Flatten();
    }

    /// <summary>Opens the path to the selection, the assembly that is out and the levels that are apart.</summary>
    public void SyncPath(ICarExplorerModel model)
    {
        pathOpen.Clear();
        if (root != null) pathOpen.Add(root);
        AddAncestors(model.Selected, false);
        AddAncestors(model.TakenOut, false);
        IReadOnlyList<CarStructureNode> path = model.OpenPath;
        if (path != null)
            for (int i = 0; i < path.Count; i++) AddAncestors(path[i], true);
        // A path the visitor follows in the car overrides rows they closed by hand.
        foreach (CarStructureNode node in pathOpen) userClosed.Remove(node);
        Flatten();
    }

    private void AddAncestors(CarStructureNode node, bool includeSelf)
    {
        if (node == null) return;
        for (CarStructureNode n = includeSelf ? node : node.Parent; n != null; n = n.Parent) pathOpen.Add(n);
    }

    public bool IsExpanded(CarStructureNode node) =>
        node != null && !userClosed.Contains(node) && (userOpen.Contains(node) || pathOpen.Contains(node));

    public void Toggle(CarStructureNode node)
    {
        if (node == null || node.Children.Count == 0) return;
        if (IsExpanded(node))
        {
            userOpen.Remove(node);
            userClosed.Add(node);
        }
        else
        {
            userClosed.Remove(node);
            userOpen.Add(node);
        }
        Flatten();
        hud.OnTreeSizeChanged();
    }

    private void Flatten()
    {
        visible.Clear();
        if (root != null) AddVisible(root);
        content.sizeDelta = new Vector2(0f, visible.Count * RowHeight);
        ClampScroll();
        Rebind(true);
    }

    private void AddVisible(CarStructureNode node)
    {
        // A system without parts on this car (CarSystems lists them all) is left out.
        if (node.Kind == CarNodeKind.System && node.Children.Count == 0) return;
        visible.Add(node);
        if (!IsExpanded(node)) return;
        List<CarStructureNode> children = node.Children;
        for (int i = 0; i < children.Count; i++) AddVisible(children[i]);
    }

    /// <summary>Called by the HUD after it sized the panel: grows the row pool to fill the viewport.</summary>
    public void SetViewportHeight(float height)
    {
        viewportHeight = Mathf.Max(RowHeight, height);
        int needed = Mathf.CeilToInt(viewportHeight / RowHeight) + 1;
        while (rows.Count < needed)
        {
            rows.Add(CreateRow());
            rowIndex.Add(-1);
        }
        // A smaller viewport (a resized window, or a size seen for one frame while the canvas rescales) gives rows
        // back.
        while (rows.Count > needed)
        {
            int last = rows.Count - 1;
            if (hoveredRow == rows[last]) hoveredRow = null;
            Object.Destroy(rows[last].gameObject);
            rows.RemoveAt(last);
            rowIndex.RemoveAt(last);
        }
        ClampScroll();
        Rebind(true);
    }

    private void ClampScroll()
    {
        float max = Mathf.Max(0f, visible.Count * RowHeight - viewportHeight);
        Vector2 position = content.anchoredPosition;
        if (position.y > max || position.y < 0f)
        {
            content.anchoredPosition = new Vector2(position.x, Mathf.Clamp(position.y, 0f, max));
            scroll.velocity = Vector2.zero;
        }
    }

    /// <summary>Scrolls the node's row into view if it is outside it.</summary>
    public void Reveal(CarStructureNode node)
    {
        if (node == null) return;
        int index = visible.IndexOf(node);
        if (index < 0) return;
        float top = content.anchoredPosition.y;
        float rowTop = index * RowHeight;
        if (rowTop >= top && rowTop + RowHeight <= top + viewportHeight) return;
        float max = Mathf.Max(0f, visible.Count * RowHeight - viewportHeight);
        // Whole rows, so the list does not start with half a row under the header.
        float target = Mathf.Round((rowTop - viewportHeight * 0.4f) / RowHeight) * RowHeight;
        content.anchoredPosition = new Vector2(0f, Mathf.Clamp(target, 0f, max));
        scroll.velocity = Vector2.zero;
        Rebind(false);
    }

    /// <summary>
    /// Header texts and every row's texts for the current language; row states for the current model.
    /// </summary>
    public void RefreshAll()
    {
        bool uzbek = hud.Uzbek;
        if (uzbek != textUzbek || string.IsNullOrEmpty(title.text))
        {
            textUzbek = uzbek;
            title.text = CarExplorerHudText.Structure.Get(uzbek);
        }
        total.text = root != null ? root.PartCount + CarExplorerHudText.PartsSuffix.Get(uzbek) : string.Empty;
        Rebind(true);
    }

    private void OnScrolled(Vector2 _) => Rebind(false);

    private void Rebind(bool force)
    {
        if (rows.Count == 0) return;
        float top = Mathf.Max(0f, content.anchoredPosition.y);
        int first = Mathf.FloorToInt(top / RowHeight);
        int count = rows.Count;
        for (int i = 0; i < count; i++)
        {
            int index = first + i;
            CarExplorerHudRow row = rows[index % count];
            int slot = index % count;
            if (index >= visible.Count)
            {
                if (row.gameObject.activeSelf) row.gameObject.SetActive(false);
                row.Node = null;
                rowIndex[slot] = -1;
                continue;
            }
            if (!force && rowIndex[slot] == index && row.Node == visible[index]) continue;
            rowIndex[slot] = index;
            if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
            row.Rect.anchoredPosition = new Vector2(0f, -index * RowHeight);
            Bind(row, visible[index]);
        }
        UpdateThumb();
        // A row that moved under a resting pointer now shows another node: highlight that one instead.
        if (hoveredRow != null && hoveredRow.Hovered) hud.HoverNode(hoveredRow.Node);
    }

    private void UpdateThumb()
    {
        float contentHeight = visible.Count * RowHeight;
        bool show = !collapsed && contentHeight > viewportHeight + 1f;
        if (thumb.enabled != show) thumb.enabled = show;
        if (!show) return;
        float trackTop = TopInset + 4f;
        float track = viewportHeight - 8f;
        float size = Mathf.Max(24f, track * viewportHeight / contentHeight);
        float t = Mathf.Clamp01(content.anchoredPosition.y / (contentHeight - viewportHeight));
        RectTransform rect = thumb.rectTransform;
        rect.sizeDelta = new Vector2(4f, size);
        rect.anchoredPosition = new Vector2(-4f, -(trackTop + (track - size) * t));
    }

    private void Bind(CarExplorerHudRow row, CarStructureNode node)
    {
        row.Node = node;
        bool uzbek = hud.Uzbek;
        bool branch = node.Children.Count > 0;
        float nameWidth = NameWidth(node);
        if (row.TextNode != node || row.TextUzbek != uzbek || !Mathf.Approximately(row.TextWidth, nameWidth))
        {
            row.TextNode = node;
            row.TextUzbek = uzbek;
            row.TextWidth = nameWidth;
            row.Name.text = Fitted(node, uzbek);
            row.Name.fontStyle = node.Kind == CarNodeKind.Vehicle ? FontStyles.Bold : FontStyles.Normal;
            if (branch) row.Count.SetText("{0}", node.PartCount);
            else row.Count.SetText(string.Empty);
        }
        if (row.TextDepth != node.Depth || row.Chevron.gameObject.activeSelf != branch)
        {
            row.TextDepth = node.Depth;
            float x = IndentOf(node.Depth);
            row.Chevron.gameObject.SetActive(branch);
            row.Bullet.gameObject.SetActive(!branch);
            ((RectTransform)row.Chevron.transform).anchoredPosition = new Vector2(x, 0f);
            row.Bullet.rectTransform.anchoredPosition = new Vector2(x + 6f, 0f);
            RectTransform name = row.Name.rectTransform;
            name.offsetMin = new Vector2(x + NameLead, 0f);
            name.offsetMax = new Vector2(-Trail(branch), 0f);
            // Right side, from the edge: explode button, part count (not in the narrow layout), out / apart dot.
            row.StatusDot.rectTransform.anchoredPosition = new Vector2(branch ? (narrow ? -48f : -84f) : -12f, 0f);
            row.Count.enabled = !narrow;
        }
        if (branch) row.Chevron.Icon.rectTransform.localEulerAngles = new Vector3(0f, 0f, IsExpanded(node) ? -90f : 0f);
        ApplyState(row);
    }

    /// <summary>Selection, hover, out / apart marker and the explode button of a bound row.</summary>
    public void ApplyState(CarExplorerHudRow row)
    {
        CarStructureNode node = row.Node;
        if (node == null) return;
        ICarExplorerModel model = hud.Model;
        bool selected = model != null && model.Selected == node;
        bool active = model != null && (model.TakenOut == node || hud.IsApart(node));
        row.Background.color = selected
            ? (row.Hovered ? new Color(0.949f, 0.639f, 0.227f, 0.24f) : CarExplorerHudStyle.AccentSoft)
            : row.Hovered ? CarExplorerHudStyle.Hover : CarExplorerHudStyle.Clear;
        if (row.AccentBar.enabled != selected) row.AccentBar.enabled = selected;
        row.Name.color = selected || node.Kind != CarNodeKind.Part
            ? CarExplorerHudStyle.TextPrimary
            : CarExplorerHudStyle.TextSecondary;
        if (row.StatusDot.enabled != active) row.StatusDot.enabled = active;
        bool open = (row.Hovered || selected) && hud.CanOpen(node);
        if (row.OpenButton.gameObject.activeSelf != open) row.OpenButton.gameObject.SetActive(open);
    }

    /// <summary>Re-applies selection and markers to the rows in view (after a selection or open-path change).</summary>
    public void RefreshStates()
    {
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Node != null) ApplyState(rows[i]);
    }

    public void OnRowHover(CarExplorerHudRow row, bool on)
    {
        if (on) hoveredRow = row;
        else if (hoveredRow == row) hoveredRow = null;
        ApplyState(row);
        hud.HoverNode(on ? row.Node : null);
    }

    public void OnRowClick(CarExplorerHudRow row)
    {
        CarStructureNode node = row.Node;
        if (node == null) return;
        float now = Time.unscaledTime;
        if (node == lastClicked && now - lastClickTime < DoubleClickSeconds)
        {
            lastClicked = null;
            hud.OpenNode(node);
            return;
        }
        lastClicked = node;
        lastClickTime = now;
        hud.SelectNode(node);
    }

    /// <summary>The row currently showing the node, or null when it is scrolled out of view or collapsed.</summary>
    public CarExplorerHudRow FindRow(CarStructureNode node)
    {
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Node == node && rows[i].gameObject.activeSelf) return rows[i];
        return null;
    }

    private CarExplorerHudRow CreateRow()
    {
        RectTransform rect = CarExplorerHudStyle.Rect("Row", content);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, RowHeight);
        var row = rect.gameObject.AddComponent<CarExplorerHudRow>();
        row.Tree = this;
        row.Rect = rect;

        row.Background = CarExplorerHudStyle.Image("Fill", rect, CarExplorerHudStyle.Rounded(6),
            CarExplorerHudStyle.Clear, true);
        CarExplorerHudStyle.Stretch(row.Background.rectTransform, 6f, 10f, 1f, 1f);

        row.AccentBar = CarExplorerHudStyle.Image("Accent", rect, CarExplorerHudStyle.Rounded(2),
            CarExplorerHudStyle.Accent);
        RectTransform bar = row.AccentBar.rectTransform;
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 0.5f);
        bar.sizeDelta = new Vector2(3f, 16f);
        bar.anchoredPosition = new Vector2(6f, 0f);
        row.AccentBar.enabled = false;

        row.Chevron = CarExplorerHudButton.Create("Expand", rect, CarExplorerHudButton.Variant.Ghost, null, 0f,
            CarExplorerHudStyle.Icon("chevron"), 6);
        row.Chevron.IdleContent = CarExplorerHudStyle.TextTertiary;
        row.Chevron.Apply();
        RectTransform chevron = (RectTransform)row.Chevron.transform;
        chevron.anchorMin = chevron.anchorMax = chevron.pivot = new Vector2(0f, 0.5f);
        chevron.sizeDelta = new Vector2(24f, 24f);
        row.Chevron.Icon.rectTransform.sizeDelta = new Vector2(18f, 18f);
        row.Chevron.Clicked = () => Toggle(row.Node);

        row.Bullet = CarExplorerHudStyle.Image("Leaf", rect, CarExplorerHudStyle.Icon("square"),
            CarExplorerHudStyle.TextTertiary);
        RectTransform bullet = row.Bullet.rectTransform;
        bullet.anchorMin = bullet.anchorMax = bullet.pivot = new Vector2(0f, 0.5f);
        bullet.sizeDelta = new Vector2(12f, 12f);

        row.Name = CarExplorerHudStyle.Text("Name", rect, CarExplorerHudStyle.RowSize, CarExplorerHudStyle.TextPrimary,
            FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        RectTransform name = row.Name.rectTransform;
        name.anchorMin = new Vector2(0f, 0f);
        name.anchorMax = new Vector2(1f, 1f);
        name.pivot = new Vector2(0f, 0.5f);

        row.Count = CarExplorerHudStyle.Text("Count", rect, CarExplorerHudStyle.CaptionSize,
            CarExplorerHudStyle.TextTertiary, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
        RectTransform count = row.Count.rectTransform;
        count.anchorMin = count.anchorMax = count.pivot = new Vector2(1f, 0.5f);
        count.sizeDelta = new Vector2(40f, RowHeight);
        count.anchoredPosition = new Vector2(-40f, 0f);

        row.StatusDot = CarExplorerHudStyle.Image("Out", rect, CarExplorerHudStyle.Icon("dot"),
            CarExplorerHudStyle.Accent);
        RectTransform dot = row.StatusDot.rectTransform;
        dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(1f, 0.5f);
        dot.sizeDelta = new Vector2(10f, 10f);
        row.StatusDot.enabled = false;

        row.OpenButton = CarExplorerHudButton.Create("Explode", rect, CarExplorerHudButton.Variant.Ghost, null, 0f,
            CarExplorerHudStyle.Icon("explode"), 6);
        row.OpenButton.IdleContent = CarExplorerHudStyle.Accent;
        row.OpenButton.Apply();
        RectTransform open = (RectTransform)row.OpenButton.transform;
        open.anchorMin = open.anchorMax = open.pivot = new Vector2(1f, 0.5f);
        open.sizeDelta = new Vector2(28f, 26f);
        open.anchoredPosition = new Vector2(-8f, 0f);
        row.OpenButton.Icon.rectTransform.sizeDelta = new Vector2(18f, 18f);
        row.OpenButton.Clicked = () =>
        {
            if (row.Node == null) return;
            hud.OpenNode(row.Node);
        };
        row.OpenButton.gameObject.SetActive(false);

        row.gameObject.SetActive(false);
        return row;
    }
}
