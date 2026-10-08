using Mush.Quest;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mush.Testing
{
    /// <summary>
    /// Temporary desktop look for Track_v2. Remove the scene's test object and
    /// Assets/Test to remove this feature; no production scripts are modified.
    /// Hands stay at their authored positions in front of the sled.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class TrackMouseLookTest : MonoBehaviour
    {
        private const string TrackScenePath = "Assets/Scenes/Track_v2.unity";

        [Header("Track scene references")]
        [SerializeField] private MushMapRideBootstrap ride;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Transform leftGrip;
        [SerializeField] private Transform rightGrip;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;

        [Header("P toggles mouse look")]
        [SerializeField] private bool startEnabled = true;
        [SerializeField, Range(0.01f, 1f)] private float mouseSensitivity = 0.12f;
        [SerializeField, Range(1f, 89f)] private float pitchLimit = 70f;

        private Transform[] fixedHands;
        private Vector3[] handPositions;
        private Quaternion[] handRotations;
        private Quaternion cameraRestRotation;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool initialized;
        private bool mouseLookEnabled;
        private bool viewOverridden;
        private bool cursorCaptured;
        private float yaw;
        private float pitch;

        public bool MouseLookEnabled => mouseLookEnabled;

        private void Start()
        {
            // Only the object placed in Track_v2 enables this test.
            if (gameObject.scene.path != TrackScenePath || cameraTransform == null ||
                leftGrip == null || rightGrip == null || leftHand == null || rightHand == null)
            {
                Debug.LogWarning("[TrackMouseLookTest] Assign the track camera and both hands.", this);
                enabled = false;
                return;
            }

            cameraRestRotation = ride != null ? ride.RideViewLocalRotation : cameraTransform.localRotation;
            fixedHands = new[] { leftGrip, rightGrip, leftHand, rightHand };
            handPositions = new Vector3[fixedHands.Length];
            handRotations = new Quaternion[fixedHands.Length];
            for (int index = 0; index < fixedHands.Length; index++)
            {
                handPositions[index] = fixedHands[index].localPosition;
                handRotations[index] = fixedHands[index].localRotation;
            }

            initialized = true;
            mouseLookEnabled = startEnabled;
        }

        private void Update()
        {
            if (!initialized)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
                SetMouseLookEnabled(!mouseLookEnabled);

            if (!CanOverrideView() || !Application.isFocused)
            {
                ReleaseCursor();
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            // Skip the capture frame so recentering cannot jump the view.
            if (!cursorCaptured)
            {
                previousCursorLock = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                cursorCaptured = true;
                return;
            }

            // Escape in the Editor releases the pointer until the Game view is clicked.
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (!mouse.leftButton.wasPressedThisFrame)
                    return;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            Vector2 movement = mouse.delta.ReadValue();
            yaw = Mathf.Repeat(yaw + movement.x * mouseSensitivity + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch - movement.y * mouseSensitivity, -pitchLimit, pitchLimit);
        }

        private void LateUpdate()
        {
            if (!CanOverrideView())
            {
                RestoreViewAndHands();
                return;
            }

            // Apply after the ride's camera shake so it cannot cancel mouse look.
            cameraTransform.localRotation = cameraRestRotation * Quaternion.Euler(pitch, yaw, 0f);
            ApplyFixedHands();
            viewOverridden = true;
        }

        public void SetMouseLookEnabled(bool value)
        {
            mouseLookEnabled = value;
            yaw = 0f;
            pitch = 0f;
            if (!value)
            {
                RestoreViewAndHands();
                ReleaseCursor();
            }
        }

        private bool CanOverrideView()
        {
            return initialized && mouseLookEnabled && cameraTransform != null &&
                   gameObject.scene.path == TrackScenePath &&
                   !MushQuestTrackedInputRig.IsXrActive && !MushSceneUI.ModalOpen &&
                   Time.timeScale > 0f && (ride == null || (!ride.IsPaused && !ride.HasFinished));
        }

        private void ApplyFixedHands()
        {
            for (int index = 0; index < fixedHands.Length; index++)
            {
                if (fixedHands[index] != null)
                    fixedHands[index].SetLocalPositionAndRotation(handPositions[index], handRotations[index]);
            }
        }

        private void RestoreViewAndHands()
        {
            if (!viewOverridden)
                return;

            if (cameraTransform != null)
                cameraTransform.localRotation = cameraRestRotation;
            ApplyFixedHands();
            viewOverridden = false;
        }

        private void ReleaseCursor()
        {
            if (!cursorCaptured)
                return;

            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            cursorCaptured = false;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                ReleaseCursor();
        }

        private void OnDisable()
        {
            RestoreViewAndHands();
            ReleaseCursor();
        }
    }
}
