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

        public void Configure(Action newAction, Renderer newTargetRenderer, Color newHoverColor)
        {
            action = newAction;
            targetRenderer = newTargetRenderer;
            hoverColor = newHoverColor;
            if (targetRenderer != null)
                normalColor = targetRenderer.material.color;
        }

        public void SetQuestRayHovered(bool hovered)
        {
            if (targetRenderer != null)
                targetRenderer.material.color = hovered ? hoverColor : normalColor;
        }

        public void SelectWithQuestRay()
        {
            action?.Invoke();
        }
    }
}
