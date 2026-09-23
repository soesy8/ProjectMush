using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Mush.Lobby
{
    /// <summary>
    /// Desktop-only seated look driven by mouse movement. WASD is reserved for
    /// lobby locomotion. When a headset is active, normal headset tracking
    /// remains in full control of the camera.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class MushDesktopSeatedLook : MonoBehaviour
    {
        [SerializeField] private Transform cameraTransform;
        [SerializeField, Range(0.01f, 1f)] private float mouseSensitivity = 0.12f;
        [SerializeField] private float minimumPitch = -50f;
        [SerializeField] private float maximumPitch = 55f;

        [Header("Desktop Hand Preview")]
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField, Range(0.35f, 1.2f)] private float handDepth = 0.72f;

        private float yaw;
        private float pitch;
        private MushLobbyStationNavigator stationNavigator;
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
            if (stationNavigator == null)
                stationNavigator = FindAnyObjectByType<MushLobbyStationNavigator>();
            if (MushSceneUI.ModalOpen || MushLobbyMapPanel.IsOpen ||
                (stationNavigator != null && stationNavigator.IsMenuOpen) ||
                (MushLobbyPauseMenu.Active != null && MushLobbyPauseMenu.Active.IsOpen)) return;
            if (cameraTransform == null || XRSettings.isDeviceActive || !Application.isFocused)
                return;

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;
            Keyboard keyboard = Keyboard.current;
            Vector2 movement = mouse.delta.ReadValue();
            yaw = Mathf.Repeat(yaw + movement.x * mouseSensitivity + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch - movement.y * mouseSensitivity, minimumPitch, maximumPitch);

            if (keyboard != null && keyboard.homeKey.wasPressedThisFrame)
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
            rightHandViewport = new Vector2(pointer.x / Mathf.Max(1f, Screen.width),
                pointer.y / Mathf.Max(1f, Screen.height));
        }

        private void ApplyDesktopHandPose()
        {
            if (leftHandAnchor == null || rightHandAnchor == null)
                FindDesktopHands();
            SetHandVisible(leftHand, XRSettings.isDeviceActive);
            SetHandVisible(rightHand, true);
            if (XRSettings.isDeviceActive || cameraTransform == null) return;
            UpdateDesktopHandTargets();
            ApplyHandPose(rightHandAnchor, rightHand, rightHandViewport, 1f);
        }

        private static void SetHandVisible(Transform hand, bool visible)
        {
            if (hand == null) return;
            foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
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
