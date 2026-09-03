using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyFeedDispenser : MonoBehaviour
    {
        private const float PourAngle = 48f;
        private static MushLobbyFeedDispenser activeDesktopDispenser;
        private MushLobbyFeedingStation station;
        private XRGrabInteractable interactable;
        private Renderer highlightRenderer;
        private Color restingColor;
        private Transform originalParent;
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private bool heldInVr;
        private bool heldOnDesktop;
        private float desktopTilt;

        public static bool IsDesktopCanisterHeld =>
            activeDesktopDispenser != null && activeDesktopDispenser.heldOnDesktop;

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
            if (XRSettings.isDeviceActive)
            {
                if (heldInVr && CurrentTiltDegrees() >= PourAngle)
                    station?.PourFrom(GetPourWorldPosition(), Time.deltaTime);
                return;
            }

            if (!heldOnDesktop || station == null)
                return;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                ReturnToStand();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            float tiltInput = 0f;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed) tiltInput += 1f;
                if (keyboard.rightArrowKey.isPressed) tiltInput -= 1f;
            }
            float targetTilt = tiltInput * 68f;
            desktopTilt = Mathf.MoveTowards(desktopTilt, targetTilt, Time.deltaTime * 95f);

            Vector3 pointerWorld = station.DesktopHoldWorld;
            if (mouse != null)
                station.TryGetDesktopPointerWorld(mouse.position.ReadValue(), out pointerWorld);
            transform.SetPositionAndRotation(pointerWorld, station.GetDesktopCanisterRotation(desktopTilt));
            if (Mathf.Abs(desktopTilt) >= PourAngle)
                station.PourFrom(GetPourWorldPosition(), Time.deltaTime);
        }

        public void Trigger()
        {
            if (XRSettings.isDeviceActive || heldOnDesktop)
                return;

            heldOnDesktop = true;
            activeDesktopDispenser = this;
            desktopTilt = 0f;
        }

        private float CurrentTiltDegrees()
        {
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(transform.up, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
        }

        private Vector3 GetPourWorldPosition()
        {
            Vector3 lowerSide = Vector3.Dot(transform.right, Vector3.up) < 0f
                ? transform.right
                : -transform.right;
            return transform.position + transform.up * 0.30f + lowerSide * 0.24f;
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            heldInVr = XRSettings.isDeviceActive;
            heldOnDesktop = false;
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            heldInVr = false;
            ReturnToStand();
        }

        private void ReturnToStand()
        {
            heldOnDesktop = false;
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
            desktopTilt = 0f;
            transform.SetParent(originalParent, false);
            transform.localPosition = originalLocalPosition;
            transform.localRotation = originalLocalRotation;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null)
                return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
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
    }
}
