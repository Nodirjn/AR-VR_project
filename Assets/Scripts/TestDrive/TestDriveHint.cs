using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A small floating label that turns to face the visitor, for "open the door", "get in" and similar
/// prompts beside the car. Drawn on top, so it shows through the bodywork.</summary>
public sealed class TestDriveHint : MonoBehaviour
{
    private RectTransform canvas;
    private TextMeshProUGUI title, detail;
    private Image dot;
    private Transform target;
    private Vector3 offset;

    public static TestDriveHint Create(Transform parent)
    {
        var go = new GameObject("Test Drive Hint");
        go.transform.SetParent(parent, false);
        TestDriveHint hint = go.AddComponent<TestDriveHint>();
        hint.Build();
        hint.Hide();
        return hint;
    }

    private void Build()
    {
        canvas = TestDriveUi.WorldCanvas("Hint Canvas", transform, new Vector2(420f, 120f), 0.0011f, false, 400);
        Image back = TestDriveUi.Rounded(canvas, "Backing", 20, new Color(0.02f, 0.03f, 0.05f, 0.78f));
        CarExplorerHudStyle.Stretch(back.rectTransform);
        dot = CarExplorerHudStyle.Image("Dot", canvas, CarExplorerHudStyle.Icon("dot"), CarExplorerHudStyle.Accent);
        TestDriveUi.Place(dot.rectTransform, 18f, 24f, 30f, 30f);
        title = TestDriveUi.Label(canvas, "Title", "", 30f, CarExplorerHudStyle.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
        TestDriveUi.Place(title.rectTransform, 58f, 16f, 350f, 40f);
        detail = TestDriveUi.Label(canvas, "Detail", "", 21f, CarExplorerHudStyle.TextSecondary, TextAlignmentOptions.Left);
        TestDriveUi.Place(detail.rectTransform, 58f, 62f, 350f, 40f);
        TestDriveUi.MakeOverlay(canvas);
    }

    public void Show(Transform anchor, Vector3 localOffset, string heading, string body)
    {
        target = anchor;
        offset = localOffset;
        title.text = heading;
        detail.text = body;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    public void Track(Transform head)
    {
        if (target == null || head == null) return;
        Vector3 at = target.TransformPoint(offset);
        transform.position = at;
        Vector3 away = at - head.position;
        away.y = 0f;
        if (away.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
        dot.color = new Color(CarExplorerHudStyle.Accent.r, CarExplorerHudStyle.Accent.g, CarExplorerHudStyle.Accent.b, pulse);
    }
}
