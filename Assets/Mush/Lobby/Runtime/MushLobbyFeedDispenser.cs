using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10010)]
    public sealed class MushLobbyFeedDispenser : MonoBehaviour
    {
        private const float PourAngle = 35f;
        private static MushLobbyFeedDispenser activeDesktopDispenser;
        private MushLobbyFeedingStation station;
        private XRGrabInteractable interactable;
        private Renderer highlightRenderer;
        private Color restingColor;
        [SerializeField] private Transform pourOrigin;
        private Transform originalParent;
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private bool heldInVr;
        private bool heldOnDesktop;
        private float desktopTilt;
        private AudioSource interactionAudio;
        private MushSoundBank soundBank;

        public static bool IsDesktopCanisterHeld =>
            activeDesktopDispenser != null && activeDesktopDispenser.heldOnDesktop;
        public bool IsHeld => heldInVr || heldOnDesktop;

        public void Configure(MushLobbyFeedingStation newStation, Renderer newHighlightRenderer)
        {
            station = newStation;
            highlightRenderer = newHighlightRenderer;
            if (highlightRenderer != null)
                restingColor = highlightRenderer.material.color;
        }

        private void Awake()
        {
            originalParent = transform.parent;
            originalLocalPosition = transform.localPosition;
            originalLocalRotation = transform.localRotation;
            soundBank = MushSoundBank.Load();
            GameObject audioRoot = new("Canister Interaction SFX");
            audioRoot.transform.SetParent(transform, false);
            interactionAudio = audioRoot.AddComponent<AudioSource>();
            interactionAudio.playOnAwake = false;
            interactionAudio.spatialBlend = 0f;
            audioRoot.AddComponent<MushAudioChannel>();
            interactable = GetComponent<XRGrabInteractable>();
            if (interactable == null)
                return;
            interactable.selectEntered.AddListener(OnSelected);
            interactable.selectExited.AddListener(OnSelectExited);
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
        }

        private void Update()
        {
            if (heldInVr)
            {
                if (CurrentTiltDegrees() >= PourAngle)
                    station?.PourFrom(GetPourWorldPosition(), Time.deltaTime);
                return;
            }

            if (!heldOnDesktop || station == null)
                return;
            if (!Application.isFocused || Time.timeScale <= 0f || MushDesktopSeatedLook.IsDesktopMenuOpen)
                return;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                ReturnToStand();
                return;
            }

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f)
                    desktopTilt = Mathf.Clamp(desktopTilt + Mathf.Sign(scroll) * 10f, -68f, 68f);
            }

        }

        private void LateUpdate()
        {
            if (!heldOnDesktop || station == null || !Application.isFocused || Time.timeScale <= 0f ||
                MushDesktopSeatedLook.IsDesktopMenuOpen)
                return;
            if (!station.TryGetDesktopPointerWorld(MushDesktopSeatedLook.PointerScreenPosition, out Vector3 pointerWorld))
                return;
            transform.SetPositionAndRotation(pointerWorld, station.GetDesktopCanisterRotation(desktopTilt));
            if (Mathf.Abs(desktopTilt) >= PourAngle)
                station.PourFrom(GetPourWorldPosition(), Time.deltaTime);
        }

        public void Trigger()
        {
            if (!isActiveAndEnabled || station == null || heldOnDesktop || heldInVr)
                return;

            heldOnDesktop = true;
            activeDesktopDispenser = this;
            desktopTilt = 0f;
            PlayInteraction(soundBank != null ? soundBank.canisterGrab : null);
        }

        private float CurrentTiltDegrees()
        {
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(transform.up, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
        }

        private Vector3 GetPourWorldPosition()
        {
            if (pourOrigin != null)
                return pourOrigin.position;
            Vector3 lowerSide = Vector3.Dot(transform.right, Vector3.up) < 0f
                ? transform.right
                : -transform.right;
            return transform.position + transform.up * 0.30f + lowerSide * 0.24f;
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            bool alreadyHeld = heldInVr || heldOnDesktop;
            heldInVr = true;
            heldOnDesktop = false;
            if (!alreadyHeld) PlayInteraction(soundBank != null ? soundBank.canisterGrab : null);
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (!heldInVr || (interactable != null && interactable.isSelected)) return;
            ReturnToStand();
        }

        private void ReturnToStand()
        {
            bool wasHeld = heldInVr || heldOnDesktop;
            heldInVr = false;
            heldOnDesktop = false;
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
            desktopTilt = 0f;
            transform.SetParent(originalParent, false);
            transform.localPosition = originalLocalPosition;
            transform.localRotation = originalLocalRotation;
            if (wasHeld) PlayInteraction(soundBank != null ? soundBank.canisterSetDown : null);
            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null || body.isKinematic)
                return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private void PlayInteraction(AudioClip clip)
        {
            if (interactionAudio != null && clip != null) interactionAudio.PlayOneShot(clip);
        }

        private void OnHoverEntered(HoverEnterEventArgs args) => SetHighlighted(true);
        private void OnHoverExited(HoverExitEventArgs args) => SetHighlighted(false);
        private void OnMouseEnter() => SetHighlighted(true);
        private void OnMouseExit() => SetHighlighted(false);

        private void SetHighlighted(bool highlighted)
        {
            if (highlightRenderer == null)
                return;
            highlightRenderer.material.color = highlighted
                ? new Color(0.74f, 0.43f, 0.13f)
                : restingColor;
        }

        private void OnDestroy()
        {
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
            if (interactable == null)
                return;
            interactable.selectEntered.RemoveListener(OnSelected);
            interactable.selectExited.RemoveListener(OnSelectExited);
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
        }

        private void OnDisable()
        {
            heldOnDesktop = false;
            heldInVr = false;
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
        }
    }
}
