using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Car Explorer HUD's look: colours, type sizes, the sprites it draws in code (rounded 9-slice panels, rings,
/// soft shadows and small icons) and helpers that create its UI objects. Sizes are canvas units of the 1920 x 1080
/// reference, on an 8-unit grid.
/// </summary>
public static class CarExplorerHudStyle
{
    // Colours are sRGB, as the inspector shows them. The project blends in linear space, where even 3 % of a bright
    // background (a poster, Remy, a lamp) reads clearly through a dark panel, so panel and plate fills are opaque; the
    // soft shadow and the hairline border keep them light.
    public static readonly Color PanelFill = new Color(0.051f, 0.059f, 0.075f, 1f);
    public static readonly Color PanelBorder = new Color(1f, 1f, 1f, 0.09f);
    public static readonly Color PlateFill = new Color(0.035f, 0.04f, 0.051f, 1f);
    public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.42f);
    public static readonly Color Divider = new Color(1f, 1f, 1f, 0.08f);
    public static readonly Color TextPrimary = new Color(0.95f, 0.96f, 0.97f, 1f);
    public static readonly Color TextSecondary = new Color(0.68f, 0.71f, 0.76f, 1f);
    public static readonly Color TextTertiary = new Color(0.5f, 0.53f, 0.58f, 1f);
    public static readonly Color TextDisabled = new Color(1f, 1f, 1f, 0.28f);
    public static readonly Color Accent = new Color(0.949f, 0.639f, 0.227f, 1f);
    public static readonly Color AccentText = new Color(0.106f, 0.071f, 0.024f, 1f);
    public static readonly Color AccentSoft = new Color(0.949f, 0.639f, 0.227f, 0.17f);
    public static readonly Color AccentBorder = new Color(0.949f, 0.639f, 0.227f, 0.55f);
    public static readonly Color Hover = new Color(1f, 1f, 1f, 0.06f);
    public static readonly Color Pressed = new Color(1f, 1f, 1f, 0.11f);
    public static readonly Color ButtonFill = new Color(1f, 1f, 1f, 0.065f);
    public static readonly Color ButtonBorder = new Color(1f, 1f, 1f, 0.13f);
    public static readonly Color KeyFill = new Color(1f, 1f, 1f, 0.08f);
    public static readonly Color Dim = new Color(0.01f, 0.012f, 0.016f, 0.55f);
    public static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

    // Type scale.
    public const float TitleSize = 28f;
    public const float BodySize = 19f;
    public const float RowSize = 18f;
    public const float LabelSize = 17f;
    public const float CaptionSize = 15f;

    // Geometry.
    public const int PanelRadius = 14;
    public const int ControlRadius = 10;
    public const int ChipRadius = 6;
    public const float FadeSeconds = 0.15f;

    /// <summary>The built-in UI layer, so world-space panels render on the default culling masks.</summary>
    public const int UiLayer = 5;

    private const int Supersample = 2;
    private static readonly Dictionary<int, Sprite> rounded = new Dictionary<int, Sprite>();
    private static readonly Dictionary<int, Sprite> shadows = new Dictionary<int, Sprite>();
    private static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
    private static TMP_FontAsset font;

    public static TMP_FontAsset Font
    {
        get
        {
            if (font != null) return font;
            font = TMP_Settings.defaultFontAsset;
            if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            return font;
        }
    }

    // ------------------------------------------------------------------ sprites

    /// <summary>
    /// A white rounded rectangle for a sliced Image: radius in canvas units, ring > 0 draws only an inner border of
    /// that width. Drawn at twice the canvas resolution, so it stays sharp at 1440p.
    /// </summary>
    public static Sprite Rounded(int radius, float ring = 0f)
    {
        int key = radius * 1000 + Mathf.RoundToInt(ring * 100f);
        if (rounded.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        int r = radius * Supersample;
        int size = r * 2 + 2;
        float half = size * 0.5f;
        float band = ring * Supersample;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = RoundedDistance(x + 0.5f - half, y + 0.5f - half, half, r);
                float a = Mathf.Clamp01(0.5f - d);
                if (band > 0f) a *= Mathf.Clamp01(0.5f + d + band);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        Sprite sprite = MakeSprite("HUD Rounded " + key, pixels, size, size, new Vector4(r, r, r, r));
        rounded[key] = sprite;
        return sprite;
    }

    /// <summary>A soft shadow for a rounded panel: the panel's shape blurred over `blur` units, 9-sliced.</summary>
    public static Sprite SoftShadow(int radius, int blur)
    {
        int key = radius * 1000 + blur;
        if (shadows.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        int r = radius * Supersample;
        int b = blur * Supersample;
        int size = (r + b) * 2 + 2;
        float half = size * 0.5f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = RoundedDistance(x + 0.5f - half, y + 0.5f - half, half - b, r);
                float t = Mathf.Clamp01((d + b * 0.25f) / (b * 1.25f));
                float a = 1f - t * t * (3f - 2f * t);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * a * 255f));
            }
        }
        int border = r + b;
        var borders = new Vector4(border, border, border, border);
        Sprite sprite = MakeSprite("HUD Shadow " + key, pixels, size, size, borders);
        shadows[key] = sprite;
        return sprite;
    }

    /// <summary>
    /// Small line icons drawn as distance fields: chevron (pointing right), close, explode, focus, dot, square, tree
    /// (an indented list), eye and eye-off.
    /// </summary>
    public static Sprite Icon(string name)
    {
        if (icons.TryGetValue(name, out Sprite cached) && cached != null) return cached;

        const int size = 48;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Normalised coordinates, y up, one texel = 1 / size.
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float d = IconDistance(name, u, v) * size;
                float a = Mathf.Clamp01(0.5f - d);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        Sprite sprite = MakeSprite("HUD Icon " + name, pixels, size, size, Vector4.zero);
        icons[name] = sprite;
        return sprite;
    }

    private static float IconDistance(string name, float u, float v)
    {
        const float stroke = 0.055f;
        switch (name)
        {
            case "chevron":
                return Mathf.Min(Segment(u, v, 0.4f, 0.24f, 0.64f, 0.5f),
                    Segment(u, v, 0.64f, 0.5f, 0.4f, 0.76f)) - stroke;
            case "close":
                return Mathf.Min(Segment(u, v, 0.3f, 0.3f, 0.7f, 0.7f), Segment(u, v, 0.3f, 0.7f, 0.7f, 0.3f)) - stroke;
            case "explode":
            {
                // Four blocks pushed apart from the centre, with a short arrow out of each corner.
                float d = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                    float cx = 0.5f + sx * 0.17f, cy = 0.5f + sy * 0.17f;
                    float bx = Mathf.Abs(u - cx) - 0.09f, by = Mathf.Abs(v - cy) - 0.09f;
                    d = Mathf.Min(d, BoxDistance(bx, by) - 0.02f);
                    float arrow = Segment(u, v, 0.5f + sx * 0.33f, 0.5f + sy * 0.33f,
                        0.5f + sx * 0.44f, 0.5f + sy * 0.44f);
                    d = Mathf.Min(d, arrow - 0.04f);
                }
                return d;
            }
            case "focus":
            {
                // A viewfinder: four corner brackets around a small dot (frame the selection).
                float d = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                    float cx = 0.5f + sx * 0.3f, cy = 0.5f + sy * 0.3f;
                    d = Mathf.Min(d, Segment(u, v, cx, cy, cx - sx * 0.15f, cy));
                    d = Mathf.Min(d, Segment(u, v, cx, cy, cx, cy - sy * 0.15f));
                }
                return Mathf.Min(d - stroke, new Vector2(u - 0.5f, v - 0.5f).magnitude - 0.075f);
            }
            case "dot":
                return new Vector2(u - 0.5f, v - 0.5f).magnitude - 0.3f;
            case "tree":
            {
                // The STRUCTURE tab: a parent row and two indented child rows, each a dot and a line.
                float d = Mathf.Min(Segment(u, v, 0.4f, 0.74f, 0.8f, 0.74f), Segment(u, v, 0.52f, 0.5f, 0.8f, 0.5f));
                d = Mathf.Min(d, Segment(u, v, 0.52f, 0.26f, 0.8f, 0.26f)) - stroke;
                d = Mathf.Min(d, new Vector2(u - 0.24f, v - 0.74f).magnitude - 0.07f);
                d = Mathf.Min(d, new Vector2(u - 0.36f, v - 0.5f).magnitude - 0.07f);
                return Mathf.Min(d, new Vector2(u - 0.36f, v - 0.26f).magnitude - 0.07f);
            }
            case "eye":
                return EyeDistance(u, v);
            case "eye-off":
            {
                // The eye, cut round a slash through it (the panels are hidden).
                float slash = Segment(u, v, 0.2f, 0.2f, 0.8f, 0.8f);
                return Mathf.Min(Mathf.Max(EyeDistance(u, v), 0.1f - slash), slash - stroke);
            }
            case "square":
            {
                float bx = Mathf.Abs(u - 0.5f) - 0.2f, by = Mathf.Abs(v - 0.5f) - 0.2f;
                float outer = BoxDistance(bx, by) - 0.04f;
                return Mathf.Abs(outer + 0.05f) - 0.05f;
            }
            default:
                return 1f;
        }
    }

    // An eye: the outline of a lens (two circles' intersection, 0.68 wide and 0.4 high) round a round pupil.
    private static float EyeDistance(float u, float v)
    {
        const float radius = 0.389f, offset = 0.189f;
        float lens = Mathf.Max(new Vector2(u - 0.5f, v - 0.5f + offset).magnitude - radius,
            new Vector2(u - 0.5f, v - 0.5f - offset).magnitude - radius);
        return Mathf.Min(Mathf.Abs(lens) - 0.05f, new Vector2(u - 0.5f, v - 0.5f).magnitude - 0.09f);
    }

    // Signed distance to an axis-aligned box, from the point's distances past the box's half extents on each axis.
    private static float BoxDistance(float bx, float by) =>
        new Vector2(Mathf.Max(bx, 0f), Mathf.Max(by, 0f)).magnitude + Mathf.Min(Mathf.Max(bx, by), 0f);

    private static float Segment(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float t = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
        float ex = px - (ax + t * dx), ey = py - (ay + t * dy);
        return Mathf.Sqrt(ex * ex + ey * ey);
    }

    // Signed distance from a point (relative to the centre) to a rounded square of half size `half` and radius r.
    private static float RoundedDistance(float px, float py, float half, float r)
    {
        float qx = Mathf.Abs(px) - (half - r), qy = Mathf.Abs(py) - (half - r);
        float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    private static Sprite MakeSprite(string name, Color32[] pixels, int width, int height, Vector4 border)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        // 200 texels per unit against the canvas's 100 reference pixels: one canvas unit is two texels.
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f),
            100f * Supersample, 0, SpriteMeshType.FullRect, border);
        sprite.name = name;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    // ------------------------------------------------------------------ factories

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>A rect anchored and pivoted at one point of its parent (0..1 on each axis).</summary>
    public static RectTransform Rect(string name, Transform parent, Vector2 anchor)
    {
        RectTransform rect = Rect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        return rect;
    }

    public static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    public static Image Image(string name, Transform parent, Sprite sprite, Color color, bool raycast = false)
    {
        RectTransform rect = Rect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = raycast;
        if (sprite != null && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
        return image;
    }

    /// <summary>
    /// A rounded panel: soft shadow, translucent fill and a thin border, all behind the panel's content.
    /// </summary>
    public static Image Panel(RectTransform panel, Color fill, int radius = PanelRadius, bool shadow = true)
    {
        if (shadow)
        {
            Image shade = Image("Shadow", panel, SoftShadow(radius, 24), Shadow);
            Stretch(shade.rectTransform, -24f, -24f, -18f, -30f);
            shade.rectTransform.SetAsFirstSibling();
            var ignore = shade.gameObject.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;
        }
        Image back = Image("Fill", panel, Rounded(radius), fill, true);
        Stretch(back.rectTransform);
        back.rectTransform.SetSiblingIndex(shadow ? 1 : 0);
        back.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Image border = Image("Border", panel, Rounded(radius, 1f), PanelBorder);
        Stretch(border.rectTransform);
        border.rectTransform.SetSiblingIndex(shadow ? 2 : 1);
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return back;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, float size, Color color,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        RectTransform rect = Rect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = Font;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.margin = Vector4.zero;
        return text;
    }

    public static LayoutElement Layout(Component target, float preferredWidth = -1f, float preferredHeight = -1f,
        float flexibleWidth = -1f, float minHeight = -1f)
    {
        LayoutElement element = target.GetComponent<LayoutElement>();
        if (element == null) element = target.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = preferredWidth;
        element.preferredHeight = preferredHeight;
        element.flexibleWidth = flexibleWidth;
        element.minHeight = minHeight;
        return element;
    }

    public static CanvasGroup Group(Component target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        return group != null ? group : target.gameObject.AddComponent<CanvasGroup>();
    }
}
