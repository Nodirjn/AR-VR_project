using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Shared look of the test drive's panels. It reuses the Car Explorer HUD's sprites, font and palette, and adds the
/// overlay material (Resources/TestDriveOverlay.shader) for the head-up display, which draws on top of the cabin.
/// </summary>
public static class TestDriveUi
{
    public static readonly Color HudWhite = new Color(0.93f, 0.97f, 1f, 1f);
    public static readonly Color HudCyan = new Color(0.36f, 0.84f, 1f, 1f);
    public static readonly Color HudDim = new Color(0.62f, 0.72f, 0.8f, 0.85f);
    public static readonly Color HudAmber = new Color(1f, 0.7f, 0.25f, 1f);
    public static readonly Color HudRed = new Color(1f, 0.33f, 0.28f, 1f);
    public static readonly Color HudGreen = new Color(0.38f, 0.92f, 0.55f, 1f);
    public static readonly Color HudBacking = new Color(0.01f, 0.02f, 0.035f, 0.42f);

    private const string OverlayShaderPath = "TestDriveOverlay";
    private static Material spriteOverlay;
    private static Material textOverlay;
    private static bool overlayChecked;

    /// <summary>A world-space canvas; <paramref name="metresPerPixel"/> sets its size in the world.</summary>
    public static RectTransform WorldCanvas(string name, Transform parent, Vector2 sizePixels, float metresPerPixel,
        bool interactive, int sortingOrder = 0)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = CarExplorerHudStyle.UiLayer };
        go.transform.SetParent(parent, false);
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = sortingOrder;
        canvas.worldCamera = Camera.main;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = sizePixels;
        rect.localScale = Vector3.one * metresPerPixel;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
        if (interactive)
        {
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            go.AddComponent<GraphicRaycaster>();
        }
        return rect;
    }

    /// <summary>Draws every image and text under <paramref name="root"/> on top of the scene (no depth test).
    /// Without the shader in the build the panel keeps the default UI materials.</summary>
    public static void MakeOverlay(Transform root)
    {
        if (!EnsureOverlay()) return;
        foreach (Image image in root.GetComponentsInChildren<Image>(true)) image.material = spriteOverlay;
        foreach (RawImage raw in root.GetComponentsInChildren<RawImage>(true)) raw.material = spriteOverlay;
        foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            Material m = TextOverlayFor(text.font);
            if (m != null) text.fontSharedMaterial = m;
        }
    }

    public static void MakeOverlay(TextMeshProUGUI text)
    {
        if (!EnsureOverlay() || text == null) return;
        Material m = TextOverlayFor(text.font);
        if (m != null) text.fontSharedMaterial = m;
    }

    public static void MakeOverlay(Image image)
    {
        if (EnsureOverlay() && image != null) image.material = spriteOverlay;
    }

    private static bool EnsureOverlay()
    {
        if (spriteOverlay != null) return true;
        if (overlayChecked) return false;
        overlayChecked = true;
        Shader shader = Resources.Load<Shader>(OverlayShaderPath);
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("[Test Drive] Resources/" + OverlayShaderPath + ".shader is missing or unsupported; " +
                             "the head-up display uses the default depth-tested UI materials.");
            return false;
        }
        spriteOverlay = new Material(shader) { name = "Test Drive Overlay Sprite", renderQueue = 4000 };
        spriteOverlay.SetFloat("_Sdf", 0f);
        return true;
    }

    private static Material TextOverlayFor(TMP_FontAsset font)
    {
        if (font == null || font.atlasTexture == null) return null;
        if (textOverlay != null && textOverlay.mainTexture == font.atlasTexture) return textOverlay;
        textOverlay = new Material(spriteOverlay) { name = "Test Drive Overlay Text", renderQueue = 4001 };
        textOverlay.mainTexture = font.atlasTexture;
        textOverlay.SetFloat("_Sdf", 1f);
        textOverlay.SetFloat("_Softness", 0.8f);
        return textOverlay;
    }

    public static TextMeshProUGUI Label(Transform parent, string name, string value, float size, Color color,
        TextAlignmentOptions alignment, FontStyles style = FontStyles.Normal)
    {
        TextMeshProUGUI text = CarExplorerHudStyle.Text(name, parent, size, color, style, alignment);
        text.text = value;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    /// <summary>A rect placed by its top-left corner (y grows down), in the parent's pixels.</summary>
    public static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    public static Image Rounded(Transform parent, string name, int radius, Color color, bool raycast = false)
    {
        return CarExplorerHudStyle.Image(name, parent, CarExplorerHudStyle.Rounded(radius), color, raycast);
    }
}
