using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One pooled row of the HUD's STRUCTURE tree. The tree binds it to a node as the list scrolls; the row reports hover
/// and clicks back to the tree. Its chevron and explode buttons are separate controls on top of it.
/// </summary>
[DisallowMultipleComponent]
public sealed class CarExplorerHudRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public CarExplorerHudTree Tree;
    public RectTransform Rect;
    public Image Background;
    public Image AccentBar;
    public CarExplorerHudButton Chevron;
    public Image Bullet;
    public TextMeshProUGUI Name;
    public TextMeshProUGUI Count;
    public Image StatusDot;
    public CarExplorerHudButton OpenButton;

    /// <summary>The bound node, or null while the row is unused.</summary>
    public CarStructureNode Node;
    public bool Hovered;

    // What the row's texts were last set for, so rebinding while scrolling only touches rows that changed.
    public CarStructureNode TextNode;
    public bool TextUzbek;
    public float TextWidth = -1f;
    public int TextDepth = -1;

    public void OnPointerEnter(PointerEventData eventData)
    {
        Hovered = true;
        Tree?.OnRowHover(this, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Hovered = false;
        Tree?.OnRowHover(this, false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging) return;
        Tree?.OnRowClick(this);
    }

    private void OnDisable()
    {
        if (!Hovered) return;
        Hovered = false;
        Tree?.OnRowHover(this, false);
    }
}
