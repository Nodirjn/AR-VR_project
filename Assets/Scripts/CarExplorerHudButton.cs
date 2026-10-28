using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A Car Explorer HUD control: a rounded background with hover, press, selected and disabled states, an optional
/// label and icon, and callbacks for clicks and hover. Not a Selectable, so it never takes keyboard focus or
/// navigation keys from the walkthrough.
/// </summary>
[DisallowMultipleComponent]
public sealed class CarExplorerHudButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public enum Variant { Secondary, Primary, Segment, Ghost }

    public Variant Style;
    public Image Background;
    public Image Border;
    public TextMeshProUGUI Label;
    public Image Icon;
    public Action Clicked;
    public Action<bool> Hovered;
    /// <summary>Label and icon colour when idle; Ghost and Segment buttons use it, the others their own.</summary>
    public Color IdleContent = CarExplorerHudStyle.TextSecondary;
    /// <summary>
    /// A selected Segment shows a light fill instead of the accent (secondary switches such as EN / UZ).
    /// </summary>
    public bool SubtleSelection;

    private bool interactable = true;
    private bool selected;
    private bool hovered;
    private bool pressed;

    public bool Interactable
    {
        get => interactable;
        set
        {
            if (interactable == value) return;
            interactable = value;
            if (!value) pressed = false;
            Apply();
        }
    }

    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            Apply();
        }
    }

    public bool IsHovered => hovered;

    /// <summary>A button with a label (and optional leading icon), sized by the caller.</summary>
    public static CarExplorerHudButton Create(string name, Transform parent, Variant style, string label,
        float fontSize, Sprite icon = null, int radius = CarExplorerHudStyle.ControlRadius)
    {
        RectTransform rect = CarExplorerHudStyle.Rect(name, parent);
        var button = rect.gameObject.AddComponent<CarExplorerHudButton>();
        button.Style = style;
        button.Background = CarExplorerHudStyle.Image("Fill", rect, CarExplorerHudStyle.Rounded(radius), Color.clear,
            true);
        CarExplorerHudStyle.Stretch(button.Background.rectTransform);
        if (style == Variant.Secondary)
        {
            button.Border = CarExplorerHudStyle.Image("Border", rect, CarExplorerHudStyle.Rounded(radius, 1f),
                CarExplorerHudStyle.ButtonBorder);
            CarExplorerHudStyle.Stretch(button.Border.rectTransform);
        }
        if (icon != null)
        {
            button.Icon = CarExplorerHudStyle.Image("Icon", rect, icon, Color.white);
            button.Icon.preserveAspect = true;
            RectTransform iconRect = button.Icon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(20f, 20f);
        }
        if (label != null)
        {
            FontStyles weight = style == Variant.Ghost ? FontStyles.Normal : FontStyles.Bold;
            button.Label = CarExplorerHudStyle.Text("Label", rect, fontSize, Color.white, weight,
                TextAlignmentOptions.Center);
            CarExplorerHudStyle.Stretch(button.Label.rectTransform, 12f, 12f);
            button.Label.text = label;
        }
        button.Apply();
        return button;
    }

    /// <summary>Width that fits the label with the button's padding (16 units each side), at least `minimum`.</summary>
    public float PreferredWidth(float minimum)
    {
        if (Label == null) return minimum;
        float width = Label.GetPreferredValues(Label.text, 4000f, 100f).x + 32f;
        return Mathf.Max(minimum, Mathf.Ceil(width / 8f) * 8f);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        Apply();
        Hovered?.Invoke(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        pressed = false;
        Apply();
        Hovered?.Invoke(false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !interactable) return;
        pressed = true;
        Apply();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!pressed) return;
        pressed = false;
        Apply();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !interactable) return;
        Clicked?.Invoke();
    }

    private void OnDisable()
    {
        if (hovered) Hovered?.Invoke(false);
        hovered = false;
        pressed = false;
        Apply();
    }

    public void Apply()
    {
        Color fill, content;
        bool live = interactable;
        switch (Style)
        {
            case Variant.Primary:
                fill = !live ? CarExplorerHudStyle.ButtonFill
                    : pressed ? Color.Lerp(CarExplorerHudStyle.Accent, Color.black, 0.12f)
                    : hovered ? Color.Lerp(CarExplorerHudStyle.Accent, Color.white, 0.14f)
                    : CarExplorerHudStyle.Accent;
                content = live ? CarExplorerHudStyle.AccentText : CarExplorerHudStyle.TextDisabled;
                break;
            case Variant.Segment:
                fill = selected ? (SubtleSelection ? new Color(1f, 1f, 1f, 0.14f) : CarExplorerHudStyle.Accent)
                    : pressed ? CarExplorerHudStyle.Pressed
                    : hovered ? CarExplorerHudStyle.Hover
                    : CarExplorerHudStyle.Clear;
                Color chosen = SubtleSelection ? CarExplorerHudStyle.TextPrimary : CarExplorerHudStyle.AccentText;
                content = selected ? chosen
                    : !live ? CarExplorerHudStyle.TextDisabled
                    : hovered ? CarExplorerHudStyle.TextPrimary
                    : IdleContent;
                break;
            case Variant.Ghost:
                fill = !live ? CarExplorerHudStyle.Clear
                    : pressed ? CarExplorerHudStyle.Pressed
                    : hovered ? CarExplorerHudStyle.Hover
                    : CarExplorerHudStyle.Clear;
                content = !live ? CarExplorerHudStyle.TextDisabled
                    : hovered || selected ? CarExplorerHudStyle.TextPrimary
                    : IdleContent;
                break;
            default:
                fill = !live ? CarExplorerHudStyle.Clear
                    : pressed ? CarExplorerHudStyle.Pressed
                    : hovered ? new Color(1f, 1f, 1f, 0.1f)
                    : CarExplorerHudStyle.ButtonFill;
                content = live ? CarExplorerHudStyle.TextPrimary : CarExplorerHudStyle.TextDisabled;
                if (Border != null)
                    Border.color = live ? CarExplorerHudStyle.ButtonBorder : CarExplorerHudStyle.Divider;
                break;
        }
        if (Background != null) Background.color = fill;
        if (Label != null) Label.color = content;
        if (Icon != null) Icon.color = content;
    }
}
