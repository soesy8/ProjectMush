using System;
using UnityEngine;

namespace Mush.Quest
{
    [DisallowMultipleComponent]
    public sealed class MushQuestRayAction : MonoBehaviour, IMushQuestRayTarget
    {
        private Action action;
        private Renderer targetRenderer;
        private Color normalColor;
        private Color hoverColor;
        private MaterialPropertyBlock colors;
        private int rayHoverCount;
        private bool mouseHovered;
        private bool soundHovered;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        public void Configure(Action newAction, Renderer newTargetRenderer, Color newHoverColor)
        {
            action = newAction;
            targetRenderer = newTargetRenderer;
            hoverColor = newHoverColor;
            Material material = targetRenderer != null ? targetRenderer.sharedMaterial : null;
            normalColor = material != null && material.HasProperty(BaseColor) ? material.GetColor(BaseColor) :
                material != null && material.HasProperty(ColorProperty) ? material.GetColor(ColorProperty) : Color.white;
            RefreshAppearance();
        }

        public void SetQuestRayHovered(bool hovered)
        {
            rayHoverCount = Mathf.Max(0, rayHoverCount + (hovered ? 1 : -1));
            RefreshAppearance();
        }

        public void SelectWithQuestRay()
        {
            MushSounds.PlayClick();
            action?.Invoke();
        }

        private void OnMouseEnter() { mouseHovered = true; RefreshAppearance(); }
        private void OnMouseExit() { mouseHovered = false; RefreshAppearance(); }
        private void OnDisable() { mouseHovered = false; rayHoverCount = 0; RefreshAppearance(); }

        private void RefreshAppearance()
        {
            if (targetRenderer == null) return;
            bool hovered = mouseHovered || rayHoverCount > 0;
            if (hovered && !soundHovered) MushSounds.PlayHover();
            soundHovered = hovered;
            colors ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(colors);
            Color color = mouseHovered || rayHoverCount > 0 ? hoverColor : normalColor;
            colors.SetColor(BaseColor, color);
            colors.SetColor(ColorProperty, color);
            targetRenderer.SetPropertyBlock(colors);
        }
    }
}
