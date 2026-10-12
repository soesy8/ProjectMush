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
        private Outline outline;
        private bool originalEnabled;
        private Color originalColor;
        private Vector2 originalDistance;
        private bool originalUseAlpha;

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
            outline = GetComponent<Outline>();
            if (outline != null)
            {
                originalEnabled = outline.enabled;
                originalColor = outline.effectColor;
                originalDistance = outline.effectDistance;
                originalUseAlpha = outline.useGraphicAlpha;
            }
            else
            {
                outline = gameObject.AddComponent<Outline>();
                originalEnabled = false;
                outline.enabled = false;
            }
        }

        private void Refresh()
        {
            if (outline == null) return;
            if (hoveredPointers.Count == 0) { Restore(); return; }
            outline.effectColor = pressedPointers.Count > 0
                ? new Color(0.25f, 1f, 0.30f, 1f) : new Color(0.05f, 1f, 1f, 1f);
            outline.effectDistance = new Vector2(7f, -7f);
            outline.useGraphicAlpha = true;
            outline.enabled = true;
        }

        private void Restore()
        {
            if (outline == null) return;
            outline.effectColor = originalColor;
            outline.effectDistance = originalDistance;
            outline.useGraphicAlpha = originalUseAlpha;
            outline.enabled = originalEnabled;
        }

        private void OnDisable()
        {
            hoveredPointers.Clear();
            pressedPointers.Clear();
            Restore();
        }
    }
}
