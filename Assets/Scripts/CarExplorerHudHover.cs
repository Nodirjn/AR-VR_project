using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Sits on a HUD canvas and keeps the pointers (the mouse, XR controller rays) that are over any of its controls.
/// The HUD publishes it as CarExplorerModels.PointerOverHud, so the explorer leaves those clicks to the HUD.
/// </summary>
[DisallowMultipleComponent]
public sealed class CarExplorerHudHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private readonly List<int> pointers = new List<int>(4);

    public bool Any => pointers.Count > 0;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!pointers.Contains(eventData.pointerId)) pointers.Add(eventData.pointerId);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointers.Remove(eventData.pointerId);
    }

    private void OnDisable() => pointers.Clear();
}
