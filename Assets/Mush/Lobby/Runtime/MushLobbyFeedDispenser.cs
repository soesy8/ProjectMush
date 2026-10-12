using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10010)]
    public sealed class MushLobbyFeedDispenser : MonoBehaviour
    {
        private const float PourAngle = 35f;
        private const float VrHoldDistance = 0.95f;
        private const float VrTiltSpeed = 90f;
        private const float VrTiltDeadzone = 0.20f;
        private static readonly MushLobbyFeedDispenser[] activeVrDispensers = new MushLobbyFeedDispenser[2];
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
        private AudioSource grabAudio;
        private AudioSource setDownAudio;
        private AudioSource pourAudio;
        private bool rayGrabActive;
        private bool rayGrabInteractableEnabled;
        private bool rayGrabBodyKinematic;
        private bool restoreStandPending;
        private int rayHoverCount;
        private Transform vrHand;
        private int vrHandIndex = -1;
        private Quaternion vrGrabHandRotation;
        private Quaternion vrUprightRotation;
        private float vrStickTilt;

        public static bool IsDesktopCanisterHeld =>
            activeDesktopDispenser != null && activeDesktopDispenser.heldOnDesktop;
        public bool IsHeld => heldInVr || heldOnDesktop;

        public static bool IsVrCanisterHeld(XRNode hand)
        {
            MushLobbyFeedDispenser dispenser = activeVrDispensers[hand == XRNode.LeftHand ? 0 : 1];
            return dispenser != null && dispenser.rayGrabActive && dispenser.heldInVr;
        }

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
            MushSoundBank soundBank = MushSoundBank.Load();
            grabAudio = CreateAudio("Canister Grab SFX", soundBank != null ? soundBank.canisterGrab : null, 0.8f);
            setDownAudio = CreateAudio("Canister Set Down SFX", soundBank != null ? soundBank.canisterSetDown : null, 0.9f);
            pourAudio = CreateAudio("Kibble Pour SFX", soundBank != null ? soundBank.kibblePour : null, 0.35f);
            pourAudio.loop = true;
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
                // The ray bridge applies the current hand pose before pouring in LateUpdate.
                if (!rayGrabActive)
                    UpdatePouring(Time.timeScale > 0f && CurrentTiltDegrees() >= PourAngle);
                return;
            }

            if (!heldOnDesktop || station == null || !Application.isFocused ||
                Time.timeScale <= 0f || MushDesktopSeatedLook.IsDesktopMenuOpen)
            {
                StopPouring();
                return;
            }

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
            if (restoreStandPending)
            {
                restoreStandPending = false;
                ReturnToStand();
            }
            if (heldInVr)
                return;
            if (!heldOnDesktop || station == null || !Application.isFocused || Time.timeScale <= 0f ||
                MushDesktopSeatedLook.IsDesktopMenuOpen)
            {
                StopPouring();
                return;
            }
            if (!station.TryGetDesktopPointerWorld(MushDesktopSeatedLook.PointerScreenPosition, out Vector3 pointerWorld))
            {
                StopPouring();
                return;
            }
            transform.SetPositionAndRotation(pointerWorld, station.GetDesktopCanisterRotation(desktopTilt));
            UpdatePouring(Mathf.Abs(desktopTilt) >= PourAngle);
        }

        public void Trigger()
        {
            if (!isActiveAndEnabled || station == null || heldOnDesktop || heldInVr)
                return;

            heldOnDesktop = true;
            activeDesktopDispenser = this;
            desktopTilt = 0f;
            PlayAction(grabAudio);
        }

        public bool TryBeginVrRayGrab(Transform hand, Ray pointerRay, XRNode node)
        {
            if (!Mush.Quest.MushQuestTrackedInputRig.IsXrActive || !isActiveAndEnabled ||
                station == null || hand == null || heldInVr || heldOnDesktop || Time.timeScale <= 0f ||
                (interactable != null && interactable.isSelected)) return false;
            int handIndex = node == XRNode.LeftHand ? 0 : 1;
            if (activeVrDispensers[handIndex] != null) return false;
            StopPouring();
            rayGrabActive = true;
            heldInVr = true;
            vrHand = hand;
            vrHandIndex = handIndex;
            activeVrDispensers[handIndex] = this;
            vrGrabHandRotation = hand.rotation;
            vrStickTilt = 0f;
            Vector3 forward = Vector3.ProjectOnPlane(pointerRay.direction, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            vrUprightRotation = Quaternion.LookRotation(
                forward.sqrMagnitude >= 0.0001f ? forward.normalized : Vector3.forward, Vector3.up);
            rayGrabInteractableEnabled = interactable != null && interactable.enabled;
            if (interactable != null) interactable.enabled = false;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                rayGrabBodyKinematic = body.isKinematic;
                body.isKinematic = true;
            }
            // Match the PC pointer distance and start upright even when the controller aims down.
            transform.SetParent(originalParent, true);
            transform.SetPositionAndRotation(pointerRay.GetPoint(VrHoldDistance), vrUprightRotation);
            PlayAction(grabAudio);
            return true;
        }

        public void UpdateVrRayGrab(Ray pointerRay, Vector2 stick)
        {
            if (!rayGrabActive || !heldInVr || vrHand == null) return;
            if (!Mush.Quest.MushQuestTrackedInputRig.IsXrActive || Time.timeScale <= 0f ||
                !Application.isFocused)
            {
                StopPouring();
                return;
            }
            float tiltInput = Mathf.Abs(stick.x) > Mathf.Abs(stick.y) ? stick.x : stick.y;
            if (Mathf.Abs(tiltInput) >= VrTiltDeadzone)
                vrStickTilt = Mathf.Clamp(vrStickTilt + tiltInput * VrTiltSpeed * Time.deltaTime, -68f, 68f);
            Quaternion handTilt = vrHand.rotation * Quaternion.Inverse(vrGrabHandRotation);
            Quaternion rotation = handTilt * vrUprightRotation * Quaternion.Euler(0f, 0f, vrStickTilt);
            transform.SetPositionAndRotation(pointerRay.GetPoint(VrHoldDistance), rotation);
            UpdatePouring(CurrentTiltDegrees() >= PourAngle);
        }

        public void ReleaseVrRayGrab()
        {
            if (!rayGrabActive) return;
            if (gameObject.activeInHierarchy) ReturnToStand();
            else
            {
                restoreStandPending = true;
                heldInVr = false;
                RestoreRayGrabState();
            }
        }

        public void SetVrRayHovered(bool value)
        {
            rayHoverCount = Mathf.Max(0, rayHoverCount + (value ? 1 : -1));
            SetHighlighted(rayHoverCount > 0);
        }

        private void RestoreRayGrabState()
        {
            if (!rayGrabActive) return;
            rayGrabActive = false;
            if (vrHandIndex >= 0 && activeVrDispensers[vrHandIndex] == this)
                activeVrDispensers[vrHandIndex] = null;
            vrHandIndex = -1;
            vrHand = null;
            vrStickTilt = 0f;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = rayGrabBodyKinematic;
            if (interactable != null) interactable.enabled = rayGrabInteractableEnabled;
        }

        private float CurrentTiltDegrees()
        {
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(transform.up, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
        }

        private Vector3 GetPourWorldPosition()
        {
            if (pourOrigin != null)
                return pourOrigin.position;
            Vector3 lowerSide = Vector3.ProjectOnPlane(Vector3.down, transform.up);
            lowerSide = lowerSide.sqrMagnitude > 0.0001f ? lowerSide.normalized : -transform.right;
            return transform.position + transform.up * 0.30f + lowerSide * 0.24f;
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            bool alreadyHeld = heldInVr || heldOnDesktop;
            heldInVr = true;
            heldOnDesktop = false;
            if (!alreadyHeld) PlayAction(grabAudio);
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
            StopPouring();
            heldInVr = false;
            heldOnDesktop = false;
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
            desktopTilt = 0f;
            transform.SetParent(originalParent, false);
            transform.localPosition = originalLocalPosition;
            transform.localRotation = originalLocalRotation;
            RestoreRayGrabState();
            if (wasHeld) PlayAction(setDownAudio);
            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null || body.isKinematic)
                return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private AudioSource CreateAudio(string objectName, AudioClip clip, float volume)
        {
            GameObject audioRoot = new(objectName);
            audioRoot.transform.SetParent(transform, false);
            AudioSource source = audioRoot.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = clip;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 8f;
            source.dopplerLevel = 0f;
            audioRoot.AddComponent<MushAudioChannel>().SetVolume(volume);
            return source;
        }

        private static void PlayAction(AudioSource source)
        {
            if (source != null && source.clip != null) source.Play();
        }

        private void UpdatePouring(bool pouring)
        {
            if (!pouring || station == null || !station.isActiveAndEnabled || Time.deltaTime <= 0f)
            {
                StopPouring();
                return;
            }

            Vector3 origin = GetPourWorldPosition();
            station.PourFrom(origin, Time.deltaTime);
            if (pourAudio == null || pourAudio.clip == null)
                return;
            pourAudio.transform.position = origin;
            if (!pourAudio.isPlaying) pourAudio.Play();
        }

        private void StopPouring()
        {
            if (pourAudio != null && pourAudio.isPlaying) pourAudio.Stop();
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
            if (rayGrabActive) { restoreStandPending = true; RestoreRayGrabState(); }
            rayHoverCount = 0;
            StopPouring();
            if (grabAudio != null) grabAudio.Stop();
            if (setDownAudio != null) setDownAudio.Stop();
            heldOnDesktop = false;
            heldInVr = false;
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
        }
    }
}
