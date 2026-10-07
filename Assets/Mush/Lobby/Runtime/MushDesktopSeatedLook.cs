using Mush.Quest;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace Mush.Lobby
{
    /// <summary>
    /// Desktop-only seated look driven by mouse movement. WASD is reserved for
    /// lobby locomotion. When a headset is active, normal headset tracking
    /// remains in full control of the camera.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10000)]
    public sealed class MushDesktopSeatedLook : MonoBehaviour
    {
        public const float DesktopInteractionDistance = 7f;

        [SerializeField] private Transform cameraTransform;
        [SerializeField, Range(0.01f, 1f)] private float mouseSensitivity = 0.12f;
        [SerializeField] private float minimumPitch = -50f;
        [SerializeField] private float maximumPitch = 55f;

        [Header("Desktop Hand Cursor")]
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField, Range(0.35f, 1.2f)] private float handDepth = 0.72f;

        private float yaw;
        private float pitch;
        private static MushDesktopSeatedLook activeDesktopLook;
        private bool cursorCaptured;
        private int mouseInputFrame = -1;
        private bool obsoleteCursorRemoved;
        private MushLobbyStationNavigator stationNavigator;
        private Quaternion cameraRestRotation = Quaternion.identity;
        private TrackedPoseDriver cameraPoseDriver;
        private bool restoreCameraTracking;
        private bool handAttachedToCamera;
        private Transform originalHandParent;
        private Vector3 originalHandPosition;
        private Quaternion originalHandRotation;
        private Vector3 originalHandScale;
        private Vector3 handModelCentre;

        public static bool IsDesktopMenuOpen => MushSceneUI.ModalOpen || MushLobbyMapPanel.IsOpen ||
            (activeDesktopLook != null && activeDesktopLook.stationNavigator != null &&
             activeDesktopLook.stationNavigator.IsPointerMenuOpen) ||
            (MushLobbyPauseMenu.Active != null && MushLobbyPauseMenu.Active.IsOpen);

        public static Vector2 PointerScreenPosition
        {
            get
            {
                if (activeDesktopLook != null && activeDesktopLook.isActiveAndEnabled)
                {
                    activeDesktopLook.UpdateDesktopInput();
                    if (activeDesktopLook.cursorCaptured)
                        return ScreenCentre;
                }
                return Mouse.current != null ? Mouse.current.position.ReadValue()
                    : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }
        }

        private static Vector2 ScreenCentre => new(Screen.width * 0.5f, Screen.height * 0.5f);

        public Vector3 CurrentWorldViewDirection => CurrentWorldViewRotation * Vector3.forward;

        public Quaternion CurrentWorldViewRotation
        {
            get
            {
                UpdateDesktopInput();
                if (cameraTransform == null)
                    return transform.rotation;

                return cameraTransform.rotation;
            }
        }

        public static Ray GetDesktopPointerRay(Camera camera) => GetDesktopPointerRay(camera, PointerScreenPosition);

        public static Ray GetDesktopPointerRay(Camera camera, Vector2 screenPosition)
        {
            MushDesktopSeatedLook look = camera.GetComponentInParent<MushDesktopSeatedLook>();
            if (look != null && look.isActiveAndEnabled && !MushQuestTrackedInputRig.IsXrActive)
                look.UpdateDesktopInput();
            return camera.ScreenPointToRay(screenPosition);
        }

        public void Configure(Transform newCameraTransform)
        {
            if (cameraTransform != newCameraTransform)
            {
                RestoreHandParent();
                SetDesktopCameraControl(false);
            }
            cameraTransform = newCameraTransform;
            if (cameraTransform != null)
            {
                cameraRestRotation = cameraTransform.localRotation;
                cameraPoseDriver = cameraTransform.GetComponent<TrackedPoseDriver>();
                if (Application.isPlaying && isActiveAndEnabled)
                    SetDesktopCameraControl(!MushQuestTrackedInputRig.IsXrActive);
            }
            InitializeDesktopCursor();
        }

        public void RecenterView()
        {
            yaw = 0f;
            pitch = 0f;
            if (cameraTransform != null && !MushQuestTrackedInputRig.IsXrActive)
                cameraTransform.localRotation = cameraRestRotation;
        }

        private void Awake()
        {
            activeDesktopLook = this;
            if (cameraTransform != null)
            {
                cameraRestRotation = cameraTransform.localRotation;
                cameraPoseDriver = cameraTransform.GetComponent<TrackedPoseDriver>();
                SetDesktopCameraControl(!MushQuestTrackedInputRig.IsXrActive);
            }

            FindDesktopHands();
            Application.onBeforeRender += ApplyDesktopHandPose;
        }

        private void OnDestroy()
        {
            ReleaseDesktopCursor();
            RestoreHandParent();
            SetDesktopCameraControl(false);
            if (activeDesktopLook == this)
                activeDesktopLook = null;
            Application.onBeforeRender -= ApplyDesktopHandPose;
        }

        private void OnEnable()
        {
            activeDesktopLook = this;
            InitializeDesktopCursor();
        }

        private void Start() => InitializeDesktopCursor();

        private void InitializeDesktopCursor()
        {
            UpdateDesktopInput();
            ApplyDesktopHandPose();
        }

        private void OnDisable()
        {
            ReleaseDesktopCursor();
            RestoreHandParent();
            SetDesktopCameraControl(false);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                ReleaseDesktopCursor();
            else
                InitializeDesktopCursor();
        }

        private void UpdateDesktopInput()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
                return;
            SetDesktopCameraControl(!MushQuestTrackedInputRig.IsXrActive);
            if (MushQuestTrackedInputRig.IsXrActive)
                RestoreHandParent();
            RemoveObsoleteCursorVisual();
            if (stationNavigator == null)
                stationNavigator = FindAnyObjectByType<MushLobbyStationNavigator>();
            Mouse mouse = Mouse.current;
            bool allowLook = cameraTransform != null && !MushQuestTrackedInputRig.IsXrActive && Application.isFocused &&
                             mouse != null && !IsDesktopMenuOpen && Time.timeScale > 0f;
            if (!allowLook)
            {
                ReleaseDesktopCursor();
                mouseInputFrame = Time.frameCount;
                return;
            }

            if (!cursorCaptured || Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                cursorCaptured = true;
                // Capture can recenter the hardware cursor. Consume no movement on that frame.
                mouseInputFrame = Time.frameCount;
                ApplyViewRotation();
                return;
            }

            Cursor.visible = false;
            if (mouseInputFrame == Time.frameCount)
            {
                ApplyViewRotation();
                return;
            }
            mouseInputFrame = Time.frameCount;
            Vector2 movement = mouse.delta.ReadValue();

            // Apply relative mouse movement directly; the hand cursor shares the camera transform.
            yaw = Mathf.Repeat(yaw + movement.x * mouseSensitivity + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch - movement.y * mouseSensitivity, minimumPitch, maximumPitch);

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.homeKey.wasPressedThisFrame)
            {
                yaw = 0f;
                pitch = 0f;
            }
            ApplyViewRotation();
        }

        private void SetDesktopCameraControl(bool desktop)
        {
            if (cameraPoseDriver == null && cameraTransform != null)
                cameraPoseDriver = cameraTransform.GetComponent<TrackedPoseDriver>();
            if (cameraPoseDriver == null)
                return;

            if (desktop)
            {
                // Headset pose updates must not overwrite desktop mouse look before rendering.
                restoreCameraTracking |= cameraPoseDriver.enabled;
                cameraPoseDriver.enabled = false;
            }
            else if (restoreCameraTracking)
            {
                cameraPoseDriver.enabled = true;
                restoreCameraTracking = false;
            }
        }

        private void ApplyViewRotation()
        {
            if (cameraTransform != null && !MushQuestTrackedInputRig.IsXrActive)
                cameraTransform.localRotation = cameraRestRotation * Quaternion.Euler(pitch, yaw, 0f);
        }

        private void ReleaseDesktopCursor()
        {
            if (!cursorCaptured)
                return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (Application.isFocused && Mouse.current != null)
                Mouse.current.WarpCursorPosition(ScreenCentre);
            cursorCaptured = false;
        }

        private void Update()
        {
            UpdateDesktopInput();
            ApplyDesktopHandPose();
        }

        private void LateUpdate()
        {
            UpdateDesktopInput();
            ApplyDesktopHandPose();
        }

        private void RemoveObsoleteCursorVisual()
        {
            if (obsoleteCursorRemoved || !gameObject.scene.IsValid())
                return;
            obsoleteCursorRemoved = true;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (root.name != "Mush Desktop Cursor Canvas" || root.hideFlags != HideFlags.DontSave)
                    continue;
                // Remove the obsolete runtime arrow if a running scene survives a script reload.
                root.SetActive(false);
                foreach (UnityEngine.UI.Image image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    Sprite sprite = image.sprite;
                    if (sprite == null || sprite.texture == null || sprite.texture.name != "Mush Desktop Cursor")
                        continue;
                    Destroy(sprite.texture);
                    Destroy(sprite);
                }
                Destroy(root);
            }
        }

        private void FindDesktopHands()
        {
            if (leftHand == null)
                leftHand = FindDescendant("Lobby Left Hand", "Left Hand Model");
            if (rightHand == null)
                rightHand = FindDescendant("Lobby Right Hand", "Right Hand Model");

        }

        private Transform FindDescendant(params string[] names)
        {
            MushSeatedRigLock rig = cameraTransform != null ? cameraTransform.GetComponentInParent<MushSeatedRigLock>() : null;
            Transform searchRoot = rig != null ? rig.transform : transform;
            foreach (Transform child in searchRoot.GetComponentsInChildren<Transform>(true))
            {
                foreach (string targetName in names)
                {
                    if (child.name == targetName)
                        return child;
                }
            }

            return null;
        }

        [BeforeRenderOrder(10000)]
        private void ApplyDesktopHandPose()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
                return;
            if (rightHand == null)
                FindDesktopHands();
            SetHandVisible(leftHand, MushQuestTrackedInputRig.IsXrActive);
            SetHandVisible(rightHand, MushQuestTrackedInputRig.IsXrActive || cursorCaptured);
            if (MushQuestTrackedInputRig.IsXrActive || cameraTransform == null) return;
            ApplyViewRotation();
            if (cursorCaptured && rightHand != null)
                AttachHandCursor();
        }

        private static void SetHandVisible(Transform hand, bool visible)
        {
            if (hand == null) return;
            foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
        }

        private void AttachHandCursor()
        {
            if (!handAttachedToCamera)
            {
                originalHandParent = rightHand.parent;
                originalHandPosition = rightHand.localPosition;
                originalHandRotation = rightHand.localRotation;
                originalHandScale = rightHand.localScale;
                handModelCentre = GetHandModelCentre(rightHand);
                rightHand.SetParent(cameraTransform, false);
                handAttachedToCamera = true;
            }

            rightHand.gameObject.SetActive(true);
            Quaternion rotation = Quaternion.Euler(18f, 5f, 7f) * originalHandRotation;
            Vector3 position = Vector3.forward * handDepth -
                               rotation * Vector3.Scale(handModelCentre, originalHandScale);
            // A camera child cannot drift or follow a different controller pose during mouse look.
            rightHand.SetLocalPositionAndRotation(position, rotation);
            rightHand.localScale = originalHandScale;
        }

        private void RestoreHandParent()
        {
            if (!handAttachedToCamera)
                return;
            if (rightHand != null)
            {
                rightHand.SetParent(originalHandParent, false);
                rightHand.SetLocalPositionAndRotation(originalHandPosition, originalHandRotation);
                rightHand.localScale = originalHandScale;
                SetHandVisible(rightHand, true);
            }
            handAttachedToCamera = false;
        }

        private static Vector3 GetHandModelCentre(Transform hand)
        {
            Bounds bounds = new();
            bool found = false;
            foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is LineRenderer)
                    continue;
                Bounds localBounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new(
                        (corner & 1) == 0 ? localBounds.min.x : localBounds.max.x,
                        (corner & 2) == 0 ? localBounds.min.y : localBounds.max.y,
                        (corner & 4) == 0 ? localBounds.min.z : localBounds.max.z);
                    point = hand.InverseTransformPoint(renderer.transform.TransformPoint(point));
                    if (!found)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else bounds.Encapsulate(point);
                }
            }
            return found ? bounds.center : Vector3.zero;
        }
    }
}
