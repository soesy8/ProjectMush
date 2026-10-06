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

            bool hovered = mouse != null &&
                           RectTransformUtility.RectangleContainsScreenPoint(rect, mouse.position.ReadValue(), null);
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
