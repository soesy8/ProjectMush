using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Mush.Lobby
{
    /// <summary>
    /// Desktop-only seated look driven by the arrow keys. WASD is reserved for
    /// lobby locomotion. When a headset is active, normal headset tracking
    /// remains in full control of the camera.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class MushDesktopSeatedLook : MonoBehaviour
    {
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float lookSpeed = 72f;
        [SerializeField] private float minimumPitch = -50f;
        [SerializeField] private float maximumPitch = 55f;

        [Header("Desktop Hand Preview")]
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField, Range(0.35f, 1.2f)] private float handDepth = 0.72f;

        private float yaw;
        private float pitch;
        private Quaternion cameraRestRotation = Quaternion.identity;
        private Transform leftHandAnchor;
        private Transform rightHandAnchor;
        private Vector2 leftHandViewport = new Vector2(0.32f, 0.25f);
        private Vector2 rightHandViewport = new Vector2(0.68f, 0.25f);

        public Vector3 CurrentWorldViewDirection
        {
            get
            {
                if (cameraTransform == null)
                    return transform.forward;

                Quaternion desiredLocalRotation =
                    cameraRestRotation * Quaternion.Euler(pitch, yaw, 0f);
                Quaternion desiredWorldRotation = cameraTransform.parent != null
                    ? cameraTransform.parent.rotation * desiredLocalRotation
                    : desiredLocalRotation;
                return (desiredWorldRotation * Vector3.forward).normalized;
            }
        }

        public void Configure(Transform newCameraTransform)
        {
            cameraTransform = newCameraTransform;
            if (cameraTransform != null)
                cameraRestRotation = cameraTransform.localRotation;
        }

        public void RecenterView()
        {
            yaw = 0f;
            pitch = 0f;
            if (cameraTransform != null && !XRSettings.isDeviceActive)
                cameraTransform.localRotation = cameraRestRotation;
        }

        private void Awake()
        {
            if (cameraTransform != null)
                cameraRestRotation = cameraTransform.localRotation;

            FindDesktopHands();
            Application.onBeforeRender += ApplyDesktopHandPose;
        }

        private void OnDestroy()
        {
            Application.onBeforeRender -= ApplyDesktopHandPose;
        }

        private void LateUpdate()
        {
            if (cameraTransform == null || XRSettings.isDeviceActive)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            float horizontal = 0f;
            float vertical = 0f;
            if (!MushLobbyFeedDispenser.IsDesktopCanisterHeld)
            {
                if (keyboard.leftArrowKey.isPressed) horizontal -= 1f;
                if (keyboard.rightArrowKey.isPressed) horizontal += 1f;
            }
            if (keyboard.upArrowKey.isPressed) vertical += 1f;
            if (keyboard.downArrowKey.isPressed) vertical -= 1f;

            yaw += horizontal * lookSpeed * Time.deltaTime;
            pitch = Mathf.Clamp(pitch - vertical * lookSpeed * Time.deltaTime, minimumPitch, maximumPitch);

            if (keyboard.homeKey.wasPressedThisFrame)
            {
                yaw = 0f;
                pitch = 0f;
            }

            cameraTransform.localRotation = cameraRestRotation * Quaternion.Euler(pitch, yaw, 0f);
            UpdateDesktopHandTargets();
            ApplyDesktopHandPose();
        }

        private void FindDesktopHands()
        {
            if (leftHand == null)
                leftHand = FindDescendant("Lobby Left Hand", "Left Hand Model");
            if (rightHand == null)
                rightHand = FindDescendant("Lobby Right Hand", "Right Hand Model");

            leftHandAnchor = leftHand != null && leftHand.parent != null ? leftHand.parent : leftHand;
            rightHandAnchor = rightHand != null && rightHand.parent != null ? rightHand.parent : rightHand;
        }

        private Transform FindDescendant(params string[] names)
        {
            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
            {
                foreach (string targetName in names)
                {
                    if (child.name == targetName)
                        return child;
                }
            }

            return null;
        }

        private void UpdateDesktopHandTargets()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            Vector2 pointer = mouse.position.ReadValue();
            Vector2 normalized = new Vector2(
                Mathf.Clamp01(pointer.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(pointer.y / Mathf.Max(1f, Screen.height)));

            if (mouse.leftButton.isPressed && !mouse.rightButton.isPressed)
            {
                leftHandViewport = new Vector2(
                    Mathf.Lerp(0.08f, 0.56f, normalized.x),
                    Mathf.Lerp(0.10f, 0.72f, normalized.y));
            }
            else if (mouse.rightButton.isPressed && !mouse.leftButton.isPressed)
            {
                rightHandViewport = new Vector2(
                    Mathf.Lerp(0.44f, 0.92f, normalized.x),
                    Mathf.Lerp(0.10f, 0.72f, normalized.y));
            }
            else
            {
                Vector2 offset = (normalized - new Vector2(0.5f, 0.5f)) * 2f;
                leftHandViewport = new Vector2(0.32f + offset.x * 0.08f, 0.25f + offset.y * 0.10f);
                rightHandViewport = new Vector2(0.68f + offset.x * 0.08f, 0.25f + offset.y * 0.10f);
            }
        }

        private void ApplyDesktopHandPose()
        {
            if (XRSettings.isDeviceActive || cameraTransform == null)
                return;

            if (leftHandAnchor == null || rightHandAnchor == null)
                FindDesktopHands();

            ApplyHandPose(leftHandAnchor, leftHand, leftHandViewport, -1f);
            ApplyHandPose(rightHandAnchor, rightHand, rightHandViewport, 1f);
        }

        private void ApplyHandPose(
            Transform anchor,
            Transform hand,
            Vector2 viewport,
            float side)
        {
            if (anchor == null || hand == null)
                return;

            if (!anchor.gameObject.activeSelf)
                anchor.gameObject.SetActive(true);
            if (!hand.gameObject.activeSelf)
                hand.gameObject.SetActive(true);

            foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;

            Camera camera = cameraTransform.GetComponent<Camera>();
            if (camera == null)
                camera = Camera.main;
            if (camera == null)
                return;

            anchor.position = camera.ViewportToWorldPoint(
                new Vector3(viewport.x, viewport.y, handDepth));
            anchor.rotation = cameraTransform.rotation * Quaternion.Euler(18f, side * 5f, side * 7f);
        }
    }
}
