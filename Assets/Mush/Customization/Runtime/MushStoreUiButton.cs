using System;
using Mush.Quest;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Mush.Customization
{
    public sealed class MushStoreUiButton : MonoBehaviour, IMushQuestRayTarget
    {
        private RectTransform rect;
        private Image image;
        private Action callback;
        private Action hoverCallback;
        private Color normalColor;
        private bool mouseHovered;
        private bool questHovered;
        private bool hoverActive;

        public void Configure(RectTransform newRect, Image newImage, Action newCallback, Color newNormalColor,
            Action newHoverCallback = null)
        {
            rect = newRect;
            image = newImage;
            callback = newCallback;
            hoverCallback = newHoverCallback;
            normalColor = newNormalColor;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (rect == null)
                return;

            mouseHovered = mouse != null &&
                           RectTransformUtility.RectangleContainsScreenPoint(rect, mouse.position.ReadValue(), null);
            RefreshHoverState();
            if (mouseHovered && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                MushSounds.PlayClick();
                callback?.Invoke();
            }
        }

        public void SetQuestRayHovered(bool hovered)
        {
            questHovered = hovered;
            RefreshHoverState();
        }

        private void RefreshHoverState()
        {
            bool hovered = mouseHovered || questHovered;
            if (image != null)
                image.color = hovered ? Color.Lerp(normalColor, Color.white, 0.18f) : normalColor;
            bool entered = hovered && !hoverActive;
            hoverActive = hovered;
            if (entered)
                hoverCallback?.Invoke();
        }

        private void OnDisable()
        {
            mouseHovered = false;
            questHovered = false;
            hoverActive = false;
        }

        public void SelectWithQuestRay()
        {
            MushSounds.PlayClick();
            callback?.Invoke();
        }
    }
}
