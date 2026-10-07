using System;
using Mush.Quest;
using Mush.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Mush.Customization
{
    public sealed class MushStoreUiButton : MonoBehaviour, IMushQuestRayTarget
    {
        private RectTransform rect;
        private Action callback;
        private Action hoverCallback;
        private Canvas canvas;
        private MushUiButtonFeedback feedback;
        private int rayHoverCount;
        private bool mouseHovered;
        private bool wasHovered;

        public void Configure(RectTransform newRect, Image newImage, Action newCallback, Color newNormalColor,
            Action newHoverCallback = null)
        {
            rect = newRect;
            callback = newCallback;
            hoverCallback = newHoverCallback;
            canvas = rect != null ? rect.GetComponentInParent<Canvas>() : null;
            if (newImage != null)
            {
                newImage.color = newNormalColor;
                feedback = MushUiButtonFeedback.Attach(gameObject, newImage);
            }
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (rect == null) return;
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            mouseHovered = !MushQuestTrackedInputRig.IsXrActive && mouse != null &&
                RectTransformUtility.RectangleContainsScreenPoint(rect, mouse.position.ReadValue(), eventCamera);
            ApplyHoverState();
            if (mouseHovered && mouse.leftButton.wasPressedThisFrame) SelectWithQuestRay();
        }

        public void SetQuestRayHovered(bool hovered)
        {
            rayHoverCount = Mathf.Max(0, rayHoverCount + (hovered ? 1 : -1));
            ApplyHoverState();
        }

        private void ApplyHoverState()
        {
            bool hovered = mouseHovered || rayHoverCount > 0;
            feedback?.SetHovered(hovered);
            bool entered = hovered && !wasHovered;
            wasHovered = hovered;
            if (entered) hoverCallback?.Invoke();
        }

        public void SelectWithQuestRay()
        {
            feedback?.PulsePressed();
            MushSounds.PlayClick();
            callback?.Invoke();
        }

        private void OnDisable()
        {
            rayHoverCount = 0;
            mouseHovered = false;
            wasHovered = false;
            feedback?.SetHovered(false);
        }
    }
}
