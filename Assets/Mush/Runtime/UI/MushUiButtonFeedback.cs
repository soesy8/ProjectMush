using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mush.UI
{
    /// <summary>Shows hover, focus and chosen states on the visible button artwork.</summary>
    [DisallowMultipleComponent]
    public sealed class MushUiButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private Image target;
        [SerializeField] private Sprite highlightedSprite;
        [SerializeField] private Sprite pressedSprite;
        [SerializeField] private Sprite disabledSprite;
        [SerializeField] private bool highlightOnFocus = true;
        private readonly HashSet<int> hoverPointers = new();
        private readonly HashSet<int> pressedPointers = new();
        private Button button;
        private MushUiButtonSounds sounds;
        private Color normalColor;
        private bool initialized;
        private bool focused;
        private bool chosen;
        private bool externalHovered;
        private float pressedUntil;
        private int previousState = -1;

        public static MushUiButtonFeedback Attach(GameObject owner, Image image,
            Sprite highlight = null, Sprite pressed = null, Sprite disabled = null)
        {
            MushUiButtonFeedback feedback = owner.GetComponent<MushUiButtonFeedback>();
            if (feedback == null) feedback = owner.AddComponent<MushUiButtonFeedback>();
            feedback.Configure(image, highlight, pressed);
            if (disabled != null)
            {
                feedback.disabledSprite = disabled;
                feedback.previousState = -1;
                feedback.Refresh();
            }
            return feedback;
        }

        public void Configure(Image image, Sprite highlight = null, Sprite pressed = null)
        {
            RestoreArtwork();
            target = image;
            highlightedSprite = highlight;
            pressedSprite = pressed;
            initialized = false;
            Initialize();
            previousState = -1;
            Refresh();
        }

        private void Initialize()
        {
            if (initialized || target == null) return;
            button = GetComponent<Button>();
            sounds = GetComponent<MushUiButtonSounds>();
            if (sounds == null) sounds = gameObject.AddComponent<MushUiButtonSounds>();
            if (button != null)
            {
                SpriteState states = button.spriteState;
                if (highlightedSprite == null) highlightedSprite = states.highlightedSprite;
                if (pressedSprite == null) pressedSprite = states.pressedSprite;
                if (disabledSprite == null) disabledSprite = states.disabledSprite;
                button.targetGraphic = target;
                button.transition = Selectable.Transition.None;
            }
            normalColor = target.color;
            initialized = true;
        }

        private void OnEnable()
        {
            Initialize();
            focused = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            previousState = -1;
            Refresh();
        }

        private void LateUpdate() => Refresh();

        private void OnDisable()
        {
            hoverPointers.Clear();
            pressedPointers.Clear();
            externalHovered = focused = false;
            pressedUntil = 0f;
            RestoreArtwork();
            previousState = -1;
        }

        private void RestoreArtwork()
        {
            if (!initialized || target == null) return;
            target.overrideSprite = null;
            target.color = normalColor;
        }

        public void SetHighlightOnFocus(bool value) { highlightOnFocus = value; Refresh(); }
        public void SetChosen(bool value) { chosen = value; Refresh(); }
        public void SetHovered(bool value) { externalHovered = value; sounds?.SetHovered(value); Refresh(); }
        public void PulsePressed() { pressedUntil = Time.unscaledTime + 0.12f; Refresh(); }
        public void OnPointerEnter(PointerEventData data)
        {
            hoverPointers.Add(data.pointerId);
            Refresh();
        }
        public void OnPointerExit(PointerEventData data)
        {
            hoverPointers.Remove(data.pointerId);
            Refresh();
        }
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
            pressedPointers.Add(data.pointerId);
            Refresh();
        }
        public void OnPointerUp(PointerEventData data) { pressedPointers.Remove(data.pointerId); Refresh(); }
        public void OnSelect(BaseEventData data) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { focused = false; Refresh(); }

        private void Refresh()
        {
            Initialize();
            if (!initialized || target == null) return;
            bool selected = chosen || (highlightOnFocus && focused);
            bool hovered = externalHovered || hoverPointers.Count > 0;
            int state = button != null && !button.IsInteractable() ? 3 :
                pressedPointers.Count > 0 || Time.unscaledTime < pressedUntil ? 1 :
                hovered ? 2 : selected ? 4 : 0;
            bool selectedHover = state == 2 && selected;
            int visualState = state * 2 + (selectedHover ? 1 : 0);
            if (previousState == visualState) return;
            previousState = visualState;
            Sprite stateSprite = state == 3 ? disabledSprite : state == 2 || state == 4 ? highlightedSprite :
                state == 1 ? pressedSprite : null;
            target.overrideSprite = stateSprite;
            // Use the authored sprites in both desktop and VR; tint only when extra feedback is needed.
            target.color = normalColor;
            Color warmHighlight = new(1f, 0.9f, 0.68f, normalColor.a);
            if (stateSprite == null)
            {
                if (state == 3)
                    target.color = new Color(normalColor.r * 0.5f, normalColor.g * 0.5f,
                        normalColor.b * 0.5f, normalColor.a * 0.6f);
                else if (state == 1)
                    target.color = new Color(normalColor.r * 0.9f, normalColor.g * 0.9f,
                        normalColor.b * 0.9f, normalColor.a);
                else if (state == 2 || state == 4)
                    target.color = Color.Lerp(normalColor, warmHighlight, selectedHover ? 0.24f : selected ? 0.2f : 0.12f);
            }
            else if (selectedHover)
                target.color = Color.Lerp(normalColor, warmHighlight, 0.12f);
        }
    }
}
