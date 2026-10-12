using Mush.Quest;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using XRNode = UnityEngine.XR.XRNode;

namespace Mush.Lobby
{
    /// <summary>One trigger/ray path for lobby UI, world actions, and food-canister grabs.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(12000)]
    public sealed class MushLobbyFixedRayVisuals : MonoBehaviour
    {
        [SerializeField] private float rayLength = 4.5f;
        [SerializeField] private float startWidth = 0.005f;
        [SerializeField] private float endWidth = 0.0015f;
        private readonly InputAction[] positions = new InputAction[2];
        private readonly InputAction[] rotations = new InputAction[2];
        private readonly InputAction[] triggers = new InputAction[2];
        private readonly InputAction[] tiltAxes = new InputAction[2];
        private readonly Transform[] hands = new Transform[2];
        private readonly LineRenderer[] lines = new LineRenderer[2];
        private readonly Component[] hovered = new Component[2];
        private readonly MushLobbyFeedDispenser[] heldFood = new MushLobbyFeedDispenser[2];
        private readonly bool[] previouslyHeld = new bool[2];
        private readonly RaycastHit[] worldHits = new RaycastHit[64];
        private NearFarInteractor[] interactors;
        private LineRenderer[] nativeLines;
        private MonoBehaviour[] nativeVisuals;
        private readonly Dictionary<NearFarInteractor, (bool ui, bool near, bool far)> interactorStates = new();
        private readonly Dictionary<Behaviour, bool> visualStates = new();
        private readonly Dictionary<LineRenderer, bool> lineStates = new();
        private readonly Dictionary<GameObject, bool> teleportStates = new();
        private Transform trackingSpace;
        private MushLobbyStationNavigator navigator;
        private bool nativeInputOverridden;

        public void Configure(float newRayLength) => rayLength = Mathf.Max(0.5f, newRayLength);

        private void Awake() => Initialize();

        private void Initialize()
        {
            if (!Application.isPlaying) return;
            XROrigin origin = GetComponent<XROrigin>();
            trackingSpace = origin != null && origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject.transform : transform.Find("Camera Offset") ?? transform;
            interactors = GetComponentsInChildren<NearFarInteractor>(true);
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                if (child.name.Replace(" ", string.Empty) == "TeleportInteractor")
                    teleportStates[child.gameObject] = child.gameObject.activeSelf;
            List<LineRenderer> oldLines = new();
            List<MonoBehaviour> oldVisuals = new();
            foreach (NearFarInteractor interactor in interactors)
            {
                oldLines.AddRange(interactor.GetComponentsInChildren<LineRenderer>(true));
                foreach (MonoBehaviour visual in interactor.GetComponentsInChildren<MonoBehaviour>(true))
                    if (visual != null && visual.GetType().Name == "CurveVisualController") oldVisuals.Add(visual);
            }
            nativeLines = oldLines.ToArray();
            nativeVisuals = oldVisuals.ToArray();
            MushPlayerHands.InstallLobby(transform);
            for (int hand = 0; hand < 2; hand++)
            {
                string side = hand == 0 ? "LeftHand" : "RightHand";
                positions[hand] = new InputAction(side + " Lobby Aim Position", InputActionType.Value,
                    "<XRController>{" + side + "}/pointerPosition");
                rotations[hand] = new InputAction(side + " Lobby Aim Rotation", InputActionType.Value,
                    "<XRController>{" + side + "}/pointerRotation");
                triggers[hand] = new InputAction(side + " Lobby Trigger", InputActionType.Button,
                    "<XRController>{" + side + "}/triggerPressed");
                triggers[hand].AddBinding("<XRController>{" + side + "}/trigger");
                tiltAxes[hand] = new InputAction(side + " Canister Tilt", InputActionType.Value,
                    "<XRController>{" + side + "}/primary2DAxis", expectedControlType: "Vector2");
                hands[hand] = trackingSpace.Find(side + " Mush Lobby Grip");
                if (hands[hand] == null)
                {
                    hands[hand] = new GameObject(side + " Mush Lobby Grip").transform;
                    hands[hand].SetParent(trackingSpace, false);
                }
                Transform ray = transform.Find(side + " Mush Lobby Ray");
                if (ray == null)
                {
                    ray = new GameObject(side + " Mush Lobby Ray").transform;
                    ray.SetParent(transform, false);
                }
                lines[hand] = ray.GetComponent<LineRenderer>();
                if (lines[hand] == null) lines[hand] = ray.gameObject.AddComponent<LineRenderer>();
                lines[hand].useWorldSpace = true;
                lines[hand].positionCount = 2;
                lines[hand].startWidth = startWidth;
                lines[hand].endWidth = endWidth;
                lines[hand].numCapVertices = 4;
                lines[hand].enabled = false;
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (hands[0] == null) Initialize();
            foreach (InputAction action in positions) action?.Enable();
            foreach (InputAction action in rotations) action?.Enable();
            foreach (InputAction action in triggers) action?.Enable();
            foreach (InputAction action in tiltAxes) action?.Enable();
        }

        private void OnDisable()
        {
            for (int hand = 0; hand < 2; hand++) ReleaseHand(hand);
            RestoreNativeInput();
            foreach (InputAction action in positions) action?.Disable();
            foreach (InputAction action in rotations) action?.Disable();
            foreach (InputAction action in triggers) action?.Disable();
            foreach (InputAction action in tiltAxes) action?.Disable();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || hands[0] == null) return;
            bool vr = MushQuestTrackedInputRig.IsXrActive;
            bool modal = MushLobbyMapPanel.IsOpen || MushSceneUI.ModalOpen ||
                (MushLobbyPauseMenu.Active != null && MushLobbyPauseMenu.Active.IsOpen);
            if (vr)
            {
                nativeInputOverridden = true;
                if (navigator == null)
                    foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                    {
                        navigator = root.GetComponentInChildren<MushLobbyStationNavigator>(true);
                        if (navigator != null) break;
                    }
                MushCanvasQuestInput.EnsureEventSystem();
                foreach (NearFarInteractor interactor in interactors)
                {
                    if (interactor == null) continue;
                    if (!interactorStates.ContainsKey(interactor))
                        interactorStates.Add(interactor, (interactor.enableUIInteraction,
                            interactor.enableNearCasting, interactor.enableFarCasting));
                    // Only this bridge dispatches trigger actions; XRI must not select the same target twice.
                    interactor.enableUIInteraction = false;
                    interactor.enableNearCasting = false;
                    interactor.enableFarCasting = false;
                }
                foreach (LineRenderer oldLine in nativeLines) DisableNativeVisual(oldLine);
                foreach (MonoBehaviour visual in nativeVisuals) DisableNativeVisual(visual);
                foreach (var state in teleportStates) if (state.Key != null) state.Key.SetActive(false);
            }
            else RestoreNativeInput();
            // Head-attached station buttons move after the physics update.
            // Synchronize their colliders before testing the visible controller rays.
            if (vr && navigator != null && navigator.IsMenuOpen) Physics.SyncTransforms();
            for (int hand = 0; hand < 2; hand++)
            {
                XRController controller = hand == 0 ? XRController.leftHand : XRController.rightHand;
                XRNode node = hand == 0 ? XRNode.LeftHand : XRNode.RightHand;
                UnityEngine.XR.InputDevice device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
                bool tracked = vr && (controller != null && controller.isTracked.isPressed ||
                    device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracking) && tracking);
                if (!tracked) { ReleaseHand(hand); continue; }
                Vector3 localPosition = controller != null ? controller.devicePosition.ReadValue() : Vector3.zero;
                Quaternion localRotation = controller != null ? controller.deviceRotation.ReadValue() : Quaternion.identity;
                if (controller == null)
                {
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out localPosition);
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out localRotation);
                }
                hands[hand].SetPositionAndRotation(trackingSpace.TransformPoint(localPosition),
                    trackingSpace.rotation * localRotation);
                Vector3 start = positions[hand].controls.Count > 0
                    ? trackingSpace.TransformPoint(positions[hand].ReadValue<Vector3>()) : hands[hand].position;
                Quaternion aim = rotations[hand].controls.Count > 0
                    ? trackingSpace.rotation * rotations[hand].ReadValue<Quaternion>() : hands[hand].rotation;
                Vector3 forward = aim * Vector3.forward;
                if (forward.sqrMagnitude < 0.001f) forward = hands[hand].forward;
                Ray ray = new(start, forward.normalized);
                bool held = triggers[hand].IsPressed();
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float amount)) held |= amount >= 0.55f;
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool button)) held |= button;
                bool pressed = held && !previouslyHeld[hand];
                previouslyHeld[hand] = held;
                if (heldFood[hand] != null)
                {
                    if (!held || modal) { heldFood[hand].ReleaseVrRayGrab(); heldFood[hand] = null; }
                    else
                    {
                        Vector2 stick = tiltAxes[hand].ReadValue<Vector2>();
                        if (tiltAxes[hand].activeControl == null && device.isValid)
                            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out stick);
                        heldFood[hand].UpdateVrRayGrab(ray, stick);
                        SetHover(hand, null);
                        MushCanvasQuestInput.Release(node);
                        MushVrPointerVisual.Update(lines[hand], start, ray.GetPoint(0.35f), true, true,
                            MushVrLobbyUiCamera.UiLayer);
                        continue;
                    }
                }
                bool ui = MushCanvasQuestInput.Handle(node, ray, pressed, held, out Vector3 uiPoint);
                if (ui || modal)
                {
                    SetHover(hand, null);
                    MushVrPointerVisual.Update(lines[hand], start, ui ? uiPoint : ray.GetPoint(rayLength),
                        true, ui, MushVrLobbyUiCamera.UiLayer);
                    continue;
                }
                Component target = FindWorldTarget(ray, out Vector3 end);
                if (navigator != null && navigator.IsMenuOpen && target is not MushLobbyStationButton) target = null;
                SetHover(hand, target);
                MushVrPointerVisual.Update(lines[hand], start, end, true, target != null,
                    MushVrLobbyUiCamera.UiLayer);
                if (!pressed || target == null) continue;
                switch (target)
                {
                    case MushLobbyFeedDispenser food:
                        if (food.TryBeginVrRayGrab(hands[hand], ray, node)) heldFood[hand] = food;
                        break;
                    case MushLobbyStationButton station: station.Trigger(); break;
                    case MushLobbyInteractable interactable: interactable.Trigger(); break;
                    case MushLobbyShopItem shop: shop.Trigger(); break;
                    case MushLobbyChairSeatInteractable chair: chair.Trigger(); break;
                    case MushLobbyDogInteraction dog: dog.Pet(); break;
                }
            }
        }

        private Component FindWorldTarget(Ray ray, out Vector3 end)
        {
            end = ray.GetPoint(rayLength);
            Component target = null;
            Component uiTarget = null;
            Vector3 uiEnd = end;
            float uiNearest = float.PositiveInfinity;
            float nearest = 8f;
            int count = Physics.RaycastNonAlloc(ray, worldHits, nearest, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = worldHits[i];
                if (hit.collider == null) continue;
                Component candidate = hit.collider.GetComponentInParent<MushLobbyFeedDispenser>();
                candidate ??= hit.collider.GetComponentInParent<MushLobbyStationButton>();
                candidate ??= hit.collider.GetComponentInParent<MushLobbyInteractable>();
                candidate ??= hit.collider.GetComponentInParent<MushLobbyShopItem>();
                candidate ??= hit.collider.GetComponentInParent<MushLobbyChairSeatInteractable>();
                candidate ??= hit.collider.GetComponentInParent<MushLobbyDogInteraction>();
                if (candidate != null && candidate.gameObject.layer == MushVrLobbyUiCamera.UiLayer)
                {
                    if (hit.distance < uiNearest) { uiNearest = hit.distance; uiTarget = candidate; uiEnd = hit.point; }
                    continue;
                }
                if (hit.distance >= nearest || candidate == null && hit.collider.transform.IsChildOf(transform)) continue;
                if (candidate == null && hit.collider.isTrigger) continue;
                nearest = hit.distance;
                end = hit.point;
                target = candidate;
            }
            if (uiTarget != null) { end = uiEnd; return uiTarget; }
            return target;
        }

        private void SetHover(int hand, Component target)
        {
            if (hovered[hand] == target) return;
            ApplyHover(hovered[hand], false);
            hovered[hand] = target;
            ApplyHover(target, true);
        }

        private void DisableNativeVisual(Behaviour visual)
        {
            if (visual == null) return;
            if (!visualStates.ContainsKey(visual)) visualStates.Add(visual, visual.enabled);
            visual.enabled = false;
        }

        private void DisableNativeVisual(LineRenderer line)
        {
            if (line == null) return;
            if (!lineStates.ContainsKey(line)) lineStates.Add(line, line.enabled);
            line.enabled = false;
        }

        private void RestoreNativeInput()
        {
            if (!nativeInputOverridden) return;
            nativeInputOverridden = false;
            foreach (var state in interactorStates)
                if (state.Key != null)
                {
                    state.Key.enableUIInteraction = state.Value.ui;
                    state.Key.enableNearCasting = state.Value.near;
                    state.Key.enableFarCasting = state.Value.far;
                }
            interactorStates.Clear();
            foreach (var state in visualStates) if (state.Key != null) state.Key.enabled = state.Value;
            visualStates.Clear();
            foreach (var state in lineStates) if (state.Key != null) state.Key.enabled = state.Value;
            lineStates.Clear();
            foreach (var state in teleportStates)
                if (state.Key != null && state.Key.activeSelf != state.Value) state.Key.SetActive(state.Value);
        }

        private static void ApplyHover(Component target, bool value)
        {
            if (target == null) return;
            if (target is IMushQuestRayTarget rayTarget) rayTarget.SetQuestRayHovered(value);
            else if (target is MushLobbyFeedDispenser food) food.SetVrRayHovered(value);
            else if (target is MushLobbyStationButton station) station.SetQuestRayHovered(value);
        }

        private void ReleaseHand(int hand)
        {
            if (heldFood[hand] != null) heldFood[hand].ReleaseVrRayGrab();
            heldFood[hand] = null;
            SetHover(hand, null);
            previouslyHeld[hand] = false;
            MushVrPointerVisual.Hide(lines[hand]);
            MushCanvasQuestInput.Release(hand == 0 ? XRNode.LeftHand : XRNode.RightHand);
        }

        private void OnDestroy()
        {
            foreach (InputAction action in positions) action?.Dispose();
            foreach (InputAction action in rotations) action?.Dispose();
            foreach (InputAction action in triggers) action?.Dispose();
            foreach (InputAction action in tiltAxes) action?.Dispose();
        }
    }
}
