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
        private readonly HashSet<int> vrHoverPointers = new();
        private readonly HashSet<int> pressedPointers = new();
        private Button button;
        private MushUiButtonSounds sounds;
        private Color normalColor;
        private Vector3 normalScale;
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
                button.targetGraphic = target;
                button.transition = Selectable.Transition.None;
            }
            normalColor = target.color;
            normalScale = target.rectTransform.localScale;
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
            vrHoverPointers.Clear();
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
            target.rectTransform.localScale = normalScale;
            Mush.Quest.MushVrUiHighlight.Set(target, false);
        }

        public void SetHighlightOnFocus(bool value) { highlightOnFocus = value; Refresh(); }
        public void SetChosen(bool value) { chosen = value; Refresh(); }
        public void SetHovered(bool value) { externalHovered = value; sounds?.SetHovered(value); Refresh(); }
        public void PulsePressed() { pressedUntil = Time.unscaledTime + 0.12f; Refresh(); }
        public void OnPointerEnter(PointerEventData data)
        {
            hoverPointers.Add(data.pointerId);
            if (MushCanvasQuestInput.IsQuestPointer(data.pointerId)) vrHoverPointers.Add(data.pointerId);
            Refresh();
        }
        public void OnPointerExit(PointerEventData data)
        {
            hoverPointers.Remove(data.pointerId);
            vrHoverPointers.Remove(data.pointerId);
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
            bool liftSelectedHover = state == 2 && selected;
            bool vrHovered = vrHoverPointers.Count > 0 ||
                (externalHovered && Mush.Quest.MushQuestTrackedInputRig.IsXrActive);
            bool vrEmphasis = vrHovered && state != 3;
            int visualState = state * 4 + (liftSelectedHover ? 1 : 0) + (vrEmphasis ? 2 : 0);
            if (previousState == visualState) return;
            previousState = visualState;
            Sprite stateSprite = state == 3 ? disabledSprite : state == 2 || state == 4 ? highlightedSprite :
                state == 1 ? pressedSprite : null;
            target.overrideSprite = stateSprite;
            // Chosen maps keep their highlight, with an extra response when the pointer enters.
            target.rectTransform.localScale = normalScale * (liftSelectedHover ? 1.025f : 1f);
            target.color = stateSprite != null || state == 0 ? normalColor : state == 3
                ? new Color(normalColor.r * 0.5f, normalColor.g * 0.5f, normalColor.b * 0.5f, normalColor.a * 0.6f)
                : Color.Lerp(normalColor, state == 1 ? Color.white : new Color(1f, 0.83f, 0.25f, normalColor.a),
                    state == 1 ? 0.3f : 0.65f);
            if (vrEmphasis)
            {
                target.color = state == 1 ? new Color(0.65f, 1f, 0.70f, normalColor.a) :
                    new Color(normalColor.r * 1.20f, normalColor.g * 1.20f, normalColor.b * 1.20f, normalColor.a);
                target.rectTransform.localScale = normalScale * (state == 1 ? 1.02f : 1.06f);
            }
            Mush.Quest.MushVrUiHighlight.Set(target, vrEmphasis, state == 1);
        }
    }
}
