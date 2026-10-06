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
        private Action callback;

        public void Configure(RectTransform newRect, Image newImage, Action newCallback, Color newNormalColor)
        {
            rect = newRect;
            callback = newCallback;
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
            if (hovered && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                MushSounds.PlayClick();
                callback?.Invoke();
            }
        }

        public void SetQuestRayHovered(bool hovered)
        {
            // Hover appearance is authored with the UI artwork.
        }

        public void SelectWithQuestRay()
        {
            MushSounds.PlayClick();
            callback?.Invoke();
        }
    }
}
