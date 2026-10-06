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
        private Canvas canvas;
        private MushUiButtonFeedback feedback;
        private int rayHoverCount;
        private bool mouseHovered;

        public void Configure(RectTransform newRect, Image newImage, Action newCallback, Color newNormalColor)
        {
            rect = newRect;
            callback = newCallback;
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
            mouseHovered = !XRSettings.isDeviceActive && mouse != null &&
                RectTransformUtility.RectangleContainsScreenPoint(rect, mouse.position.ReadValue(), eventCamera);
            feedback?.SetHovered(mouseHovered || rayHoverCount > 0);
            if (mouseHovered && mouse.leftButton.wasPressedThisFrame) SelectWithQuestRay();
        }

        public void SetQuestRayHovered(bool hovered)
        {
            rayHoverCount = Mathf.Max(0, rayHoverCount + (hovered ? 1 : -1));
            feedback?.SetHovered(mouseHovered || rayHoverCount > 0);
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
            feedback?.SetHovered(false);
        }
    }
}
