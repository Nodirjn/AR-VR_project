using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD's canvas scaler: scales with the screen against the 1920 x 1080 reference like CanvasScaler, but not below
/// `minimumScale`, so the text stays legible in small windows (1280 x 720 gets 0.75 instead of 0.67). Narrow windows
/// still keep `minimumWidth` reference units across, room for both side panels and the view between them.
/// </summary>
public sealed class CarExplorerHudScaler : CanvasScaler
{
    public float minimumScale = 0.75f;
    public float minimumWidth = 1200f;

    protected override void HandleScaleWithScreenSize()
    {
        base.HandleScaleWithScreenSize();
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) return;
        float floor = Mathf.Min(minimumScale, canvas.renderingDisplaySize.x / minimumWidth);
        if (canvas.scaleFactor < floor) SetScaleFactor(floor);
    }
}
