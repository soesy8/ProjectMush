using System;
using UnityEngine;
using UnityEngine.UI;

namespace Mush.Quest
{
    [DisallowMultipleComponent]
    public sealed class MushQuestRayAction : MonoBehaviour, IMushQuestRayTarget
    {
        private Action action;
        private Renderer targetRenderer;
        private Image targetImage;
        private Color normalImageColor;
        private Vector3 normalImageScale;
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
            targetImage = GetComponentInChildren<Image>(true);
            if (targetImage != null)
            {
                normalImageColor = targetImage.color;
                normalImageScale = targetImage.rectTransform.localScale;
            }
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

        public void SetMouseHovered(bool hovered) { mouseHovered = hovered; RefreshAppearance(); }
        private void OnDisable() { mouseHovered = false; rayHoverCount = 0; RefreshAppearance(); }

        private void RefreshAppearance()
        {
            bool hovered = mouseHovered || rayHoverCount > 0;
            if (hovered && !soundHovered) MushSounds.PlayHover();
            soundHovered = hovered;
            if (targetImage != null)
            {
                targetImage.color = hovered
                    ? Color.Lerp(normalImageColor, new Color(1f, 0.88f, 0.48f, normalImageColor.a), 0.30f)
                    : normalImageColor;
                targetImage.rectTransform.localScale = normalImageScale * (hovered ? 1.04f : 1f);
                if (rayHoverCount > 0)
                {
                    targetImage.color = new Color(normalImageColor.r * 1.20f, normalImageColor.g * 1.20f,
                        normalImageColor.b * 1.20f, normalImageColor.a);
                    targetImage.rectTransform.localScale = normalImageScale * 1.06f;
                }
                MushVrUiHighlight.Set(targetImage, rayHoverCount > 0);
            }
            if (targetRenderer == null) return;
            colors ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(colors);
            Color color = rayHoverCount > 0 ? new Color(0.15f, 1.8f, 2f, normalColor.a) :
                mouseHovered ? hoverColor : normalColor;
            colors.SetColor(BaseColor, color);
            colors.SetColor(ColorProperty, color);
            targetRenderer.SetPropertyBlock(colors);
        }
    }
}
