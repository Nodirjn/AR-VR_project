using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Fades the view to black and back for teleports (into the city, into the seat, back to the showroom), so
/// the head never jumps in view. A black panel in front of the camera, drawn with the overlay material.</summary>
public sealed class TestDriveFader : MonoBehaviour
{
    private RectTransform canvas;
    private Image black;
    private float alpha;

    public bool Opaque => alpha > 0.99f;

    private void Build()
    {
        if (canvas != null) return;
        canvas = TestDriveUi.WorldCanvas("Test Drive Fade", transform, new Vector2(1000f, 1000f), 0.003f, false, 32000);
        black = CarExplorerHudStyle.Image("Black", canvas, null, Color.black);
        CarExplorerHudStyle.Stretch(black.rectTransform);
        TestDriveUi.MakeOverlay(black);
        // Above the head-up display and the hints.
        if (black.material != null) black.material = new Material(black.material) { renderQueue = 4500 };
        canvas.gameObject.SetActive(false);
    }

    private void Attach(Transform head)
    {
        Build();
        if (head == null) return;
        if (canvas.parent != head)
        {
            canvas.SetParent(head, false);
            canvas.localPosition = new Vector3(0f, 0f, 0.16f);
            canvas.localRotation = Quaternion.identity;
        }
    }

    public IEnumerator FadeOut(Transform head, float seconds)
    {
        Attach(head);
        canvas.gameObject.SetActive(true);
        yield return Run(1f, seconds);
    }

    public IEnumerator FadeIn(Transform head, float seconds)
    {
        Attach(head);
        canvas.gameObject.SetActive(true);
        yield return Run(0f, seconds);
        canvas.gameObject.SetActive(false);
    }

    private IEnumerator Run(float target, float seconds)
    {
        float start = alpha;
        float t = 0f;
        while (t < 1f)
        {
            t = seconds > 0f ? Mathf.Min(1f, t + Time.unscaledDeltaTime / seconds) : 1f;
            alpha = Mathf.Lerp(start, target, t * t * (3f - 2f * t));
            black.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }
    }

    /// <summary>Keeps the panel in front of the current head (the camera can change parents while black).</summary>
    public void Follow(Transform head)
    {
        if (canvas != null && canvas.gameObject.activeSelf) Attach(head);
    }
}
