using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyStationButton : MonoBehaviour
    {
        private MushLobbyStationNavigator navigator;
        private int stationIndex;
        private Renderer buttonRenderer;
        private XRSimpleInteractable xrInteractable;
        private MaterialPropertyBlock colors;
        private Color normalColor = Color.white;
        private bool selected;
        private bool mouseHovered;
        private bool soundHovered;
        private int rayHoverCount;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        public void Configure(MushLobbyStationNavigator owner, int index, Renderer renderer)
        {
            navigator = owner;
            stationIndex = index;
            buttonRenderer = renderer;
            Material material = renderer != null ? renderer.sharedMaterial : null;
            normalColor = material != null && material.HasProperty(BaseColor) ? material.GetColor(BaseColor) :
                material != null && material.HasProperty(ColorProperty) ? material.GetColor(ColorProperty) : Color.white;
            RefreshAppearance();
        }

        private void Awake()
        {
            xrInteractable = GetComponent<XRSimpleInteractable>();
            if (xrInteractable == null) return;
            xrInteractable.selectEntered.AddListener(OnSelected);
            xrInteractable.hoverEntered.AddListener(OnHoverEntered);
            xrInteractable.hoverExited.AddListener(OnHoverExited);
        }

        private void OnDestroy()
        {
            if (xrInteractable == null) return;
            xrInteractable.selectEntered.RemoveListener(OnSelected);
            xrInteractable.hoverEntered.RemoveListener(OnHoverEntered);
            xrInteractable.hoverExited.RemoveListener(OnHoverExited);
        }

        public void Trigger()
        {
            MushSounds.PlayClick();
            navigator?.TravelTo(stationIndex);
        }

        public void SetSelected(bool value)
        {
            selected = value;
            RefreshAppearance();
        }

        private void OnSelected(SelectEnterEventArgs args) => Trigger();
        private void OnHoverEntered(HoverEnterEventArgs args) { rayHoverCount++; RefreshAppearance(); }
        private void OnHoverExited(HoverExitEventArgs args) { rayHoverCount = Mathf.Max(0, rayHoverCount - 1); RefreshAppearance(); }
        private void OnMouseEnter() { mouseHovered = true; RefreshAppearance(); }
        private void OnMouseExit() { mouseHovered = false; RefreshAppearance(); }
        private void OnDisable() { mouseHovered = false; rayHoverCount = 0; RefreshAppearance(); }

        private void RefreshAppearance()
        {
            if (buttonRenderer == null) return;
            bool hovered = mouseHovered || rayHoverCount > 0;
            if (hovered && !soundHovered) MushSounds.PlayHover();
            soundHovered = hovered;
            colors ??= new MaterialPropertyBlock();
            buttonRenderer.GetPropertyBlock(colors);
            Color color = selected || mouseHovered || rayHoverCount > 0
                ? new Color(1.4f, 1.15f, 0.45f, normalColor.a) : normalColor;
            colors.SetColor(BaseColor, color);
            colors.SetColor(ColorProperty, color);
            buttonRenderer.SetPropertyBlock(colors);
        }
    }
}
