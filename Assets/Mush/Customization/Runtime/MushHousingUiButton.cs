using System;
using Mush.Quest;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Mush.Customization
{
    public sealed class MushHousingUiButton : MonoBehaviour, IMushQuestRayTarget
    {
        private RectTransform rect;
        private Image image;
        private Action callback;
        private Color normalColor;
        private bool questHovered;

        public void Configure(RectTransform newRect, Image newImage, Action newCallback, Color newNormalColor)
        {
            rect = newRect;
            image = newImage;
            callback = newCallback;
            normalColor = newNormalColor;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (rect == null)
                return;

            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            bool hovered = !XRSettings.isDeviceActive && mouse != null &&
                           RectTransformUtility.RectangleContainsScreenPoint(rect, mouse.position.ReadValue(), eventCamera);
            if (image != null)
                image.color = hovered || questHovered ? Color.Lerp(normalColor, Color.white, 0.18f) : normalColor;
            if (hovered && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                MushSounds.PlayClick();
                callback?.Invoke();
            }
        }

        public void SetQuestRayHovered(bool hovered)
        {
            questHovered = hovered;
        }

        public void SelectWithQuestRay()
        {
            MushSounds.PlayClick();
            callback?.Invoke();
        }
    }
}
