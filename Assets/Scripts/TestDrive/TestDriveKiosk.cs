using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A slim "Test drive" stand beside each showroom display: brushed metal post, an angled glass-black panel with the
/// car's figures and a button. The button works with the XR controller rays and the desktop mouse; on the desktop,
/// G while looking at the car or the stand does the same.
/// </summary>
public sealed class TestDriveKiosk : MonoBehaviour
{
    public int Index;
    public Transform Display;
    public TestDriveCarProfile Profile;
    public System.Action<int> Requested;

    private Image buttonFill;
    private TextMeshProUGUI buttonText;
    private float pulse;

    public static TestDriveKiosk Create(Transform parent, int index, Transform display, TestDriveCarProfile profile,
        Vector3 position, Quaternion rotation, Material postMaterial)
    {
        var go = new GameObject("Test Drive Stand - " + profile.DisplayName);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        TestDriveKiosk kiosk = go.AddComponent<TestDriveKiosk>();
        kiosk.Index = index;
        kiosk.Display = display;
        kiosk.Profile = profile;
        kiosk.BuildStand(postMaterial);
        kiosk.BuildPanel();
        return kiosk;
    }

    private void BuildStand(Material material)
    {
        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        post.name = "Stand Post";
        post.transform.SetParent(transform, false);
        post.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        post.transform.localScale = new Vector3(0.06f, 1f, 0.04f);
        GameObject foot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        foot.name = "Stand Foot";
        foot.transform.SetParent(transform, false);
        foot.transform.localPosition = new Vector3(0f, 0.008f, 0f);
        foot.transform.localScale = new Vector3(0.36f, 0.016f, 0.26f);
        foreach (GameObject part in new[] { post, foot })
        {
            Renderer r = part.GetComponent<Renderer>();
            if (material != null) r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        // One collider for the whole stand, so the walking body and the G key both find it.
        Destroy(post.GetComponent<Collider>());
        Destroy(foot.GetComponent<Collider>());
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        // Below the panel, so the controller rays reach the button without hitting the box first.
        box.center = new Vector3(0f, 0.48f, 0f);
        box.size = new Vector3(0.4f, 0.96f, 0.26f);
    }

    private void BuildPanel()
    {
        var pivot = new GameObject("Panel Tilt").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = new Vector3(0f, 1.08f, 0.02f);
        pivot.localRotation = Quaternion.Euler(-24f, 0f, 0f);

        // 520 x 400 px at 1 mm per px: a 52 x 40 cm screen.
        RectTransform canvas = TestDriveUi.WorldCanvas("Test Drive Panel", pivot, new Vector2(520f, 400f), 0.001f, true);
        canvas.localPosition = new Vector3(0f, 0.2f, 0f);
        canvas.localRotation = Quaternion.Euler(0f, 180f, 0f);
        RectTransform panel = CarExplorerHudStyle.Rect("Panel", canvas);
        CarExplorerHudStyle.Stretch(panel);
        CarExplorerHudStyle.Panel(panel, CarExplorerHudStyle.PlateFill, 22, false);

        TestDriveUi.Place(TestDriveUi.Label(panel, "Eyebrow", "TEST DRIVE", 17f, CarExplorerHudStyle.Accent,
            TextAlignmentOptions.Left, FontStyles.Bold).rectTransform, 30f, 26f, 460f, 24f);
        TextMeshProUGUI title = TestDriveUi.Label(panel, "Model", Profile.DisplayName, 36f, CarExplorerHudStyle.TextPrimary,
            TextAlignmentOptions.Left, FontStyles.Bold);
        TestDriveUi.Place(title.rectTransform, 30f, 54f, 460f, 46f);
        TestDriveUi.Place(TestDriveUi.Label(panel, "Tagline", Profile.Tagline, 17f, CarExplorerHudStyle.TextSecondary,
            TextAlignmentOptions.Left).rectTransform, 30f, 104f, 460f, 24f);

        string[] values = { Profile.PowerText, Profile.SprintText, Profile.TopSpeedText };
        string[] labels = { "Power", "0–100 km/h", "Top speed" };
        for (int i = 0; i < 3; i++)
        {
            float x = 30f + i * 156f;
            Image cell = TestDriveUi.Rounded(panel, "Figure " + i, 12, CarExplorerHudStyle.ButtonFill);
            TestDriveUi.Place(cell.rectTransform, x, 146f, 144f, 92f);
            TestDriveUi.Place(TestDriveUi.Label(cell.transform, "Value", values[i], 27f, CarExplorerHudStyle.TextPrimary,
                TextAlignmentOptions.Left, FontStyles.Bold).rectTransform, 16f, 14f, 120f, 34f);
            TestDriveUi.Place(TestDriveUi.Label(cell.transform, "Label", labels[i], 15f, CarExplorerHudStyle.TextTertiary,
                TextAlignmentOptions.Left).rectTransform, 16f, 54f, 120f, 22f);
        }

        buttonFill = TestDriveUi.Rounded(panel, "Start Button", 16, CarExplorerHudStyle.Accent, true);
        TestDriveUi.Place(buttonFill.rectTransform, 30f, 266f, 460f, 72f);
        Button button = buttonFill.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { if (Requested != null) Requested(Index); });
        var hover = buttonFill.gameObject.AddComponent<EventTrigger>();
        AddTrigger(hover, EventTriggerType.PointerEnter, () => pulse = 1f);
        AddTrigger(hover, EventTriggerType.PointerExit, () => pulse = 0f);
        buttonText = TestDriveUi.Label(buttonFill.transform, "Label", "Start test drive", 26f, CarExplorerHudStyle.AccentText,
            TextAlignmentOptions.Center, FontStyles.Bold);
        CarExplorerHudStyle.Stretch(buttonText.rectTransform);

        TestDriveUi.Place(TestDriveUi.Label(panel, "Hint", "Trigger on the button  ·  desktop: look here and press G", 14f,
            CarExplorerHudStyle.TextTertiary, TextAlignmentOptions.Center).rectTransform, 30f, 350f, 460f, 24f);
    }

    private static void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    private void Update()
    {
        if (buttonFill == null) return;
        float glow = pulse > 0f ? 0.18f + 0.08f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
        buttonFill.color = Color.Lerp(CarExplorerHudStyle.Accent, Color.white, glow);
    }
}
