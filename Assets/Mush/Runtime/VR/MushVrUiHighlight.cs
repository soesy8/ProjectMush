using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mush.Quest
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class MushVrUiHighlight : MonoBehaviour
    {
        private readonly HashSet<int> hoveredPointers = new();
        private readonly HashSet<int> pressedPointers = new();
        private Image image;
        private Color originalColor;
        private bool highlighted;

        public static void Set(Image image, bool hovered, bool pressed = false, int pointerId = -1)
        {
            if (image == null) return;
            MushVrUiHighlight highlight = image.GetComponent<MushVrUiHighlight>();
            if (highlight == null)
            {
                if (!hovered) return;
                highlight = image.gameObject.AddComponent<MushVrUiHighlight>();
            }
            if (hovered) highlight.hoveredPointers.Add(pointerId);
            else highlight.hoveredPointers.Remove(pointerId);
            if (hovered && pressed) highlight.pressedPointers.Add(pointerId);
            else highlight.pressedPointers.Remove(pointerId);
            highlight.Refresh();
        }

        private void Awake()
        {
            image = GetComponent<Image>();
        }

        private void Refresh()
        {
            if (image == null) return;
            if (hoveredPointers.Count == 0) { Restore(); return; }
            if (!highlighted)
            {
                originalColor = image.color;
                highlighted = true;
            }
            // Keep slider handles at their authored size and preserve their original alpha.
            image.color = pressedPointers.Count > 0
                ? new Color(originalColor.r * 0.9f, originalColor.g * 0.9f, originalColor.b * 0.9f, originalColor.a)
                : Color.Lerp(originalColor, new Color(1f, 0.9f, 0.68f, originalColor.a), 0.12f);
        }

        private void Restore()
        {
            if (!highlighted) return;
            if (image != null) image.color = originalColor;
            highlighted = false;
        }

        private void OnDisable()
        {
            hoveredPointers.Clear();
            pressedPointers.Clear();
            Restore();
        }
    }
}
