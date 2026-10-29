using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD's canvas scaler: scales with the screen against the 1920 x 1080 reference like CanvasScaler, but not below
/// `minimumScale`, so the text stays legible in small windows. Narrow windows still keep `minimumWidth` reference
/// units across, room for both side panels and the view between them. A screen smaller than `compactBelow` pixels
/// (small windows, a small editor Game view) is Compact: the HUD lays out narrower panels and fewer tree rows, and the
/// scale floor is `compactScale` (1280 x 720 gets 0.68 instead of 0.75), never below `compactMinimumScale` (0.6 at
/// 708 x 440), so the type is slightly smaller but stays readable.
/// </summary>
public sealed class CarExplorerHudScaler : CanvasScaler
{
    public float minimumScale = 0.75f;
    public float minimumWidth = 1200f;
    public Vector2 compactBelow = new Vector2(1440f, 810f);
    public float compactScale = 0.68f;
    public float compactMinimumScale = 0.6f;

    /// <summary>True when the screen (or the camera's target) is smaller than `compactBelow` pixels.</summary>
    public bool Compact { get; private set; }

    protected override void HandleScaleWithScreenSize()
    {
        base.HandleScaleWithScreenSize();
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) return;
        Vector2 size = canvas.renderingDisplaySize;
        Compact = size.x < compactBelow.x || size.y < compactBelow.y;
        float floor = Compact
            ? Mathf.Min(compactScale, Mathf.Max(compactMinimumScale, size.x / minimumWidth))
            : Mathf.Min(minimumScale, size.x / minimumWidth);
        if (canvas.scaleFactor < floor) SetScaleFactor(floor);
    }
}
