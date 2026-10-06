using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Shares hover feedback between mouse, touch and Quest rays.</summary>
[DisallowMultipleComponent]
public sealed class MushUiButtonSounds : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    private readonly HashSet<int> hoverPointers = new();
    private readonly HashSet<int> pressedPointers = new();
    private Button button;
    private bool externalHovered;
    private bool wasHovered;

    private void Awake() => button = GetComponent<Button>();

    public void SetHovered(bool hovered)
    {
        externalHovered = hovered;
        RefreshHover();
    }

    public void OnPointerEnter(PointerEventData data)
    {
        hoverPointers.Add(data.pointerId);
        RefreshHover();
    }

    public void OnPointerExit(PointerEventData data)
    {
        hoverPointers.Remove(data.pointerId);
        RefreshHover();
    }

    public void OnPointerDown(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left || !CanPlay()) return;
        pressedPointers.Add(data.pointerId);
        MushSounds.PlayClick();
    }

    public void OnPointerUp(PointerEventData data)
    {
        if (pressedPointers.Remove(data.pointerId)) MushSounds.SuppressClickThisFrame();
    }

    private bool CanPlay() => isActiveAndEnabled && (button == null || button.IsInteractable());

    private void RefreshHover()
    {
        bool hovered = externalHovered || hoverPointers.Count > 0;
        if (hovered && !wasHovered && CanPlay()) MushSounds.PlayHover();
        wasHovered = hovered;
    }

    private void OnDisable()
    {
        hoverPointers.Clear();
        pressedPointers.Clear();
        externalHovered = wasHovered = false;
    }
}
