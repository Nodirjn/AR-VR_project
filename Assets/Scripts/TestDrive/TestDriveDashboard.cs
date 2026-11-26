using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The car's head-up display, projected 2.3 m ahead of the driver's eye like a windscreen HUD: speed, the town
/// speed limit, the selector (P R N D and the gear), a rev bar (a power / regeneration bar on the electric car), a
/// status line and a controls card that shows while the car stands. Drawn on top of the cabin.
/// </summary>
public sealed class TestDriveDashboard : MonoBehaviour
{
    public const int SpeedLimitKmh = 60;
    private const int Segments = 22;

    private TestDriveVehicle vehicle;
    private bool xr;
    private RectTransform root;
    private CanvasGroup group;
    private TextMeshProUGUI speed, unit, gear, status, limit, rpmLabel;
    private readonly TextMeshProUGUI[] selector = new TextMeshProUGUI[4];
    private readonly Image[] segments = new Image[Segments];
    private Image limitRing, holdFill;
    private RectTransform holdBar;
    private RectTransform helpCard;
    private CanvasGroup helpGroup;
    private TextMeshProUGUI toast;
    private float toastUntil;
    private float shownAt;
    private float visible;

    public static TestDriveDashboard Create(TestDriveVehicle vehicle, bool xr)
    {
        TestDriveDashboard dash = vehicle.HudAnchor.gameObject.AddComponent<TestDriveDashboard>();
        dash.vehicle = vehicle;
        dash.xr = xr;
        dash.Build();
        dash.SetVisible(false);
        return dash;
    }

    private void Build()
    {
        root = TestDriveUi.WorldCanvas("Head Up Display", transform, new Vector2(760f, 300f), 0.00085f, false, 500);
        root.localRotation = Quaternion.identity;
        group = CarExplorerHudStyle.Group(root);

        Image back = TestDriveUi.Rounded(root, "Backing", 26, TestDriveUi.HudBacking);
        CarExplorerHudStyle.Stretch(back.rectTransform);

        // Speed.
        speed = TestDriveUi.Label(root, "Speed", "0", 150f, TestDriveUi.HudWhite, TextAlignmentOptions.Right, FontStyles.Bold);
        TestDriveUi.Place(speed.rectTransform, 18f, 18f, 260f, 150f);
        unit = TestDriveUi.Label(root, "Unit", "km/h", 26f, TestDriveUi.HudDim, TextAlignmentOptions.Right);
        TestDriveUi.Place(unit.rectTransform, 18f, 166f, 260f, 32f);

        // Town speed limit sign.
        limitRing = CarExplorerHudStyle.Image("Limit Ring", root, CarExplorerHudStyle.Rounded(40, 9f), TestDriveUi.HudRed);
        TestDriveUi.Place(limitRing.rectTransform, 292f, 40f, 80f, 80f);
        Image limitFace = CarExplorerHudStyle.Image("Limit Face", limitRing.transform, CarExplorerHudStyle.Rounded(31), new Color(1f, 1f, 1f, 0.92f));
        TestDriveUi.Place(limitFace.rectTransform, 9f, 9f, 62f, 62f);
        limit = TestDriveUi.Label(limitRing.transform, "Limit", SpeedLimitKmh.ToString(), 30f, new Color(0.08f, 0.08f, 0.1f),
            TextAlignmentOptions.Center, FontStyles.Bold);
        CarExplorerHudStyle.Stretch(limit.rectTransform);

        // Selector and gear.
        Image gearBox = TestDriveUi.Rounded(root, "Gear Box", 18, new Color(1f, 1f, 1f, 0.07f));
        TestDriveUi.Place(gearBox.rectTransform, 392f, 24f, 140f, 120f);
        gear = TestDriveUi.Label(gearBox.transform, "Gear", "P", 72f, TestDriveUi.HudCyan, TextAlignmentOptions.Center, FontStyles.Bold);
        CarExplorerHudStyle.Stretch(gear.rectTransform);
        string[] letters = { "P", "R", "N", "D" };
        for (int i = 0; i < 4; i++)
        {
            selector[i] = TestDriveUi.Label(root, "Selector " + letters[i], letters[i], 26f, TestDriveUi.HudDim,
                TextAlignmentOptions.Center, FontStyles.Bold);
            TestDriveUi.Place(selector[i].rectTransform, 392f + i * 35f, 150f, 35f, 34f);
        }

        // Rev bar (or power bar).
        rpmLabel = TestDriveUi.Label(root, "Rev Label", "×1000 r/min", 20f, TestDriveUi.HudDim, TextAlignmentOptions.Left);
        TestDriveUi.Place(rpmLabel.rectTransform, 556f, 30f, 190f, 26f);
        for (int i = 0; i < Segments; i++)
        {
            Image segment = TestDriveUi.Rounded(root, "Rev " + i, 3, new Color(1f, 1f, 1f, 0.12f));
            float h = 30f + i * 2.6f;
            TestDriveUi.Place(segment.rectTransform, 556f + i * 8.6f, 150f - h, 6f, h);
            segments[i] = segment;
        }

        status = TestDriveUi.Label(root, "Status", "", 24f, TestDriveUi.HudDim, TextAlignmentOptions.Left);
        TestDriveUi.Place(status.rectTransform, 556f, 160f, 190f, 34f);

        toast = TestDriveUi.Label(root, "Toast", "", 26f, TestDriveUi.HudAmber, TextAlignmentOptions.Center, FontStyles.Bold);
        TestDriveUi.Place(toast.rectTransform, 20f, 214f, 720f, 34f);

        holdBar = CarExplorerHudStyle.Rect("Hold Bar", root);
        TestDriveUi.Place(holdBar, 180f, 258f, 400f, 10f);
        Image holdBack = TestDriveUi.Rounded(holdBar, "Back", 5, new Color(1f, 1f, 1f, 0.15f));
        CarExplorerHudStyle.Stretch(holdBack.rectTransform);
        holdFill = TestDriveUi.Rounded(holdBar, "Fill", 5, TestDriveUi.HudCyan);
        holdFill.rectTransform.anchorMin = new Vector2(0f, 0f);
        holdFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        holdFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        holdFill.rectTransform.offsetMin = Vector2.zero;
        holdFill.rectTransform.offsetMax = Vector2.zero;
        holdBar.gameObject.SetActive(false);

        BuildHelp();
        TestDriveUi.MakeOverlay(root);
    }

    private void BuildHelp()
    {
        helpCard = CarExplorerHudStyle.Rect("Controls", root);
        TestDriveUi.Place(helpCard, -420f, 0f, 400f, 300f);
        helpGroup = CarExplorerHudStyle.Group(helpCard);
        Image back = TestDriveUi.Rounded(helpCard, "Backing", 22, TestDriveUi.HudBacking);
        CarExplorerHudStyle.Stretch(back.rectTransform);
        TestDriveUi.Place(TestDriveUi.Label(helpCard, "Title", "CONTROLS", 18f, TestDriveUi.HudCyan, TextAlignmentOptions.Left,
            FontStyles.Bold).rectTransform, 22f, 16f, 360f, 24f);
        string[,] rows = xr
            ? new[,]
            {
                { "Right trigger", "Accelerate" }, { "Left trigger", "Brake" }, { "Thumbstick", "Steer" },
                { "A", "Drive / Reverse" }, { "Right grip", "Handbrake" }, { "B", "Get out (stopped)" },
                { "Y  ·  X", "Horn  ·  Lights" }, { "Stick click", "Recentre seat" }, { "Hold Menu", "Back to showroom" }
            }
            : new[,]
            {
                { "W / S", "Accelerate / brake" }, { "A / D", "Steer" }, { "R", "Drive / Reverse" },
                { "Space", "Handbrake" }, { "E", "Get out (stopped)" }, { "H  ·  L", "Horn  ·  Lights" },
                { "V  ·  C", "Camera  ·  Recentre" }, { "Mouse", "Look around" }, { "Hold Backspace", "Back to showroom" }
            };
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            float y = 48f + i * 27f;
            TestDriveUi.Place(TestDriveUi.Label(helpCard, "Key " + i, rows[i, 0], 19f, TestDriveUi.HudWhite, TextAlignmentOptions.Left,
                FontStyles.Bold).rectTransform, 22f, y, 160f, 26f);
            TestDriveUi.Place(TestDriveUi.Label(helpCard, "Action " + i, rows[i, 1], 19f, TestDriveUi.HudDim, TextAlignmentOptions.Left)
                .rectTransform, 186f, y, 200f, 26f);
        }
    }

    public void SetVisible(bool on)
    {
        if (root == null) return;
        root.gameObject.SetActive(on);
        if (on) shownAt = Time.time;
        visible = on ? 1f : 0f;
    }

    public void Toast(string text, float seconds)
    {
        if (toast == null) return;
        toast.text = text;
        toastUntil = Time.time + seconds;
    }

    /// <summary>Progress (0..1) of the "back to showroom" hold; 0 hides the bar.</summary>
    public void SetHold(float progress)
    {
        if (holdBar == null) return;
        bool on = progress > 0.01f;
        if (holdBar.gameObject.activeSelf != on) holdBar.gameObject.SetActive(on);
        if (on) holdFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
    }

    private void LateUpdate()
    {
        if (vehicle == null || root == null || !root.gameObject.activeSelf) return;
        TestDriveCarProfile profile = vehicle.Profile;
        int kmh = Mathf.RoundToInt(vehicle.SpeedKmh);
        speed.text = kmh.ToString();
        bool speeding = kmh > SpeedLimitKmh + 2;
        speed.color = speeding ? TestDriveUi.HudAmber : TestDriveUi.HudWhite;
        limitRing.color = speeding ? Color.Lerp(TestDriveUi.HudRed, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 8f)) : TestDriveUi.HudRed;

        gear.text = vehicle.GearLabel;
        gear.color = vehicle.ShiftedRecently ? Color.white : (vehicle.CurrentSelector == TestDriveVehicle.Selector.R ? TestDriveUi.HudAmber : TestDriveUi.HudCyan);
        for (int i = 0; i < 4; i++)
            selector[i].color = (int)vehicle.CurrentSelector == i ? TestDriveUi.HudCyan : new Color(1f, 1f, 1f, 0.3f);

        float fraction;
        if (profile.IsElectric)
        {
            rpmLabel.text = "POWER";
            fraction = vehicle.EngineRunning ? Mathf.Clamp01(vehicle.Throttle * Mathf.Clamp01(vehicle.SpeedKmh / 8f + 0.2f)) : 0f;
        }
        else
        {
            rpmLabel.text = (vehicle.Rpm / 1000f).ToString("0.0") + "  ×1000 r/min";
            fraction = Mathf.Clamp01(vehicle.Rpm / (profile.RedlineRpm * 1.05f));
        }
        int lit = Mathf.RoundToInt(fraction * Segments);
        for (int i = 0; i < Segments; i++)
        {
            float at = (i + 0.5f) / Segments;
            Color on = at > 0.86f ? TestDriveUi.HudRed : at > 0.72f ? TestDriveUi.HudAmber : TestDriveUi.HudCyan;
            if (profile.IsElectric) on = TestDriveUi.HudGreen;
            segments[i].color = i < lit ? on : new Color(1f, 1f, 1f, 0.1f);
        }

        if (vehicle.EngineStarting) status.text = profile.IsElectric ? "Starting…" : "Cranking…";
        else if (!vehicle.EngineRunning) status.text = "Engine off";
        else if (vehicle.Handbrake && vehicle.CurrentSelector != TestDriveVehicle.Selector.P) status.text = "Handbrake";
        else if (vehicle.CurrentSelector == TestDriveVehicle.Selector.P) status.text = profile.IsElectric ? "Ready · press A" : "Ready · press A";
        else status.text = vehicle.LightsOn ? "Lights on" : "";
        if (!xr && vehicle.CurrentSelector == TestDriveVehicle.Selector.P && vehicle.EngineRunning) status.text = "Ready · press R";

        if (Time.time > toastUntil && toast.text.Length > 0) toast.text = "";

        // Controls card: while the car stands, and for the first seconds of the drive.
        bool showHelp = vehicle.SpeedKmh < 3f || Time.time - shownAt < 10f;
        helpGroup.alpha = Mathf.MoveTowards(helpGroup.alpha, showHelp ? 1f : 0f, Time.deltaTime * 2.5f);
        group.alpha = Mathf.MoveTowards(group.alpha, visible, Time.deltaTime * 3f);
    }
}
