using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Mush.Quest
{
    public interface IMushQuestRayTarget
    {
        void SetQuestRayHovered(bool hovered);
        void SelectWithQuestRay();
    }

    /// <summary>
    /// Small device-agnostic OpenXR bridge used by the Quest 2 build. It maps
    /// the tracked head and Touch controllers into an existing seated scene
    /// without adding locomotion, haptics or physics constraints.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class MushQuestTrackedInputRig : MonoBehaviour
    {
        private const float RayLength = 4.5f; // 좌식 UI에서 필요한 범위만 남겨 레이가 방 전체를 가로지르지 않게 한다.

        private Camera trackedCamera;
        private Transform coordinateRoot;
        private Transform leftController;
        private Transform rightController;
        private Vector3 desiredCameraLocalPosition;
        private Quaternion desiredCameraLocalRotation;
        private Vector3 calibrationPosition;
        private Quaternion calibrationRotation = Quaternion.identity;
        private bool calibrated;
        private bool rayEnabled;
        private bool previousLeftTrigger;
        private bool previousRightTrigger;
        private bool previousRightSecondary;
        private LineRenderer leftRay;
        private LineRenderer rightRay;
        private InputAction leftPointerPositionAction; // 왼손 컨트롤러의 실제 포인터 조준 시작 위치를 읽는 새 입력 시스템 액션이다.
        private InputAction rightPointerPositionAction; // 오른손 컨트롤러의 실제 포인터 조준 시작 위치를 읽는 새 입력 시스템 액션이다.
        private InputAction leftPointerRotationAction; // 왼손 컨트롤러의 실제 포인터 조준 회전을 읽는 새 입력 시스템 액션이다.
        private InputAction rightPointerRotationAction; // 오른손 컨트롤러의 실제 포인터 조준 회전을 읽는 새 입력 시스템 액션이다.
        private IMushQuestRayTarget leftHoveredTarget;
        private IMushQuestRayTarget rightHoveredTarget;

        public Transform LeftController => leftController;
        public Transform RightController => rightController;
        public bool IsTracking => calibrated && XRSettings.isDeviceActive;
        public bool LeftGripHeld { get; private set; }
        public bool RightGripHeld { get; private set; }
        public bool LeftTriggerHeld { get; private set; }
        public bool RightTriggerHeld { get; private set; }
        public bool LeftTriggerPressedThisFrame { get; private set; }
        public bool RightTriggerPressedThisFrame { get; private set; }
        public bool BButtonPressedThisFrame { get; private set; }

        public void Configure(
            Camera newTrackedCamera,
            Transform newCoordinateRoot,
            Vector3 newDesiredCameraLocalPosition,
            Quaternion newDesiredCameraLocalRotation,
            Transform existingLeftController = null,
            Transform existingRightController = null)
        {
            trackedCamera = newTrackedCamera;
            coordinateRoot = newCoordinateRoot;
            desiredCameraLocalPosition = newDesiredCameraLocalPosition;
            desiredCameraLocalRotation = newDesiredCameraLocalRotation;

            if (coordinateRoot == null)
            {
                GameObject rootObject = new("Quest Seated Tracking Space");
                coordinateRoot = rootObject.transform;
            }

            if (trackedCamera != null)
            {
                trackedCamera.transform.SetParent(coordinateRoot, false);
                trackedCamera.transform.localPosition = desiredCameraLocalPosition;
                trackedCamera.transform.localRotation = desiredCameraLocalRotation;
                trackedCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            }

            leftController = existingLeftController ?? CreateControllerAnchor("Quest Left Controller");
            rightController = existingRightController ?? CreateControllerAnchor("Quest Right Controller");
            leftController.SetParent(coordinateRoot, true);
            rightController.SetParent(coordinateRoot, true);

            leftRay = BuildRay("Quest Left Ray", leftController);
            rightRay = BuildRay("Quest Right Ray", rightController);
            ConfigurePointerRotationActions(); // 기기 그립 회전이 아니라 OpenXR 포인터 조준축을 읽도록 입력 액션을 준비한다.
            SetRayEnabled(false);
            calibrated = false;
        }

        public static MushQuestTrackedInputRig InstallForCamera(Camera camera)
        {
            if (camera == null)
                return null;

            Transform originalParent = camera.transform.parent;
            GameObject rootObject = new("Quest Seated UI Tracking Space");
            Transform root = rootObject.transform;
            if (originalParent != null)
                root.SetParent(originalParent, false);
            root.position = camera.transform.position;
            root.rotation = Quaternion.identity;

            Vector3 desiredLocalPosition = root.InverseTransformPoint(camera.transform.position);
            Quaternion desiredLocalRotation = Quaternion.Inverse(root.rotation) * camera.transform.rotation;
            MushQuestTrackedInputRig rig = rootObject.AddComponent<MushQuestTrackedInputRig>();
            rig.Configure(camera, root, desiredLocalPosition, desiredLocalRotation);
            return rig;
        }

        public void SetRayEnabled(bool enabled)
        {
            rayEnabled = enabled;
            SetRayVisible(leftRay, enabled && XRSettings.isDeviceActive);
            SetRayVisible(rightRay, enabled && XRSettings.isDeviceActive);
            if (!enabled)
            {
                SetHoveredTarget(ref leftHoveredTarget, null);
                SetHoveredTarget(ref rightHoveredTarget, null);
            }
        }

        public static void ConfigureWorldCanvas(Canvas canvas, Camera camera, float distance = 2.35f)
        {
            if (canvas == null || camera == null)
                return;

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            RectTransform rect = canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1920f, 1080f);
            rect.localScale = Vector3.one * 0.00175f;
            rect.position = camera.transform.position + camera.transform.forward * distance;
            rect.rotation = camera.transform.rotation;
        }

        private void Update()
        {
            LeftTriggerPressedThisFrame = false;
            RightTriggerPressedThisFrame = false;
            BButtonPressedThisFrame = false;

            if (!XRSettings.isDeviceActive)
            {
                SetRayVisible(leftRay, false);
                SetRayVisible(rightRay, false);
                return;
            }

            UpdateTrackedPoses();
            UpdateButtons();

            bool leftSelect = LeftTriggerPressedThisFrame && !RightTriggerPressedThisFrame;
            bool rightSelect = RightTriggerPressedThisFrame;
            UpdateRay(XRNode.LeftHand, leftController, leftRay, ref leftHoveredTarget, leftSelect);
            UpdateRay(XRNode.RightHand, rightController, rightRay, ref rightHoveredTarget, rightSelect);
        }

        private void UpdateTrackedPoses()
        {
            if (!TryGetPose(XRNode.Head, out Vector3 headPosition, out Quaternion headRotation))
                return;

            if (!calibrated)
            {
                calibrationRotation = desiredCameraLocalRotation * Quaternion.Inverse(headRotation);
                calibrationPosition = desiredCameraLocalPosition - calibrationRotation * headPosition;
                calibrated = true;
            }

            ApplyPose(trackedCamera != null ? trackedCamera.transform : null, headPosition, headRotation);
            if (TryGetPose(XRNode.LeftHand, out Vector3 leftPosition, out Quaternion leftRotation))
                ApplyPose(leftController, leftPosition, leftRotation);
            if (TryGetPose(XRNode.RightHand, out Vector3 rightPosition, out Quaternion rightRotation))
                ApplyPose(rightController, rightPosition, rightRotation);
        }

        private void UpdateButtons()
        {
            LeftGripHeld = ReadButton(XRNode.LeftHand, UnityEngine.XR.CommonUsages.gripButton);
            RightGripHeld = ReadButton(XRNode.RightHand, UnityEngine.XR.CommonUsages.gripButton);
            LeftTriggerHeld = ReadButton(XRNode.LeftHand, UnityEngine.XR.CommonUsages.triggerButton);
            RightTriggerHeld = ReadButton(XRNode.RightHand, UnityEngine.XR.CommonUsages.triggerButton);
            bool rightSecondary = ReadButton(XRNode.RightHand, UnityEngine.XR.CommonUsages.secondaryButton);

            LeftTriggerPressedThisFrame = LeftTriggerHeld && !previousLeftTrigger;
            RightTriggerPressedThisFrame = RightTriggerHeld && !previousRightTrigger;
            BButtonPressedThisFrame = rightSecondary && !previousRightSecondary;
            previousLeftTrigger = LeftTriggerHeld;
            previousRightTrigger = RightTriggerHeld;
            previousRightSecondary = rightSecondary;
        }

        private void ApplyPose(Transform target, Vector3 devicePosition, Quaternion deviceRotation)
        {
            if (target == null)
                return;
            target.localPosition = calibrationPosition + calibrationRotation * devicePosition;
            target.localRotation = calibrationRotation * deviceRotation;
        }

        private void UpdateRay(
            XRNode node,
            Transform origin,
            LineRenderer line,
            ref IMushQuestRayTarget hoveredTarget,
            bool selectPressed)
        {
            bool visible = rayEnabled && calibrated && origin != null;
            SetRayVisible(line, visible);
            if (!visible)
            {
                SetHoveredTarget(ref hoveredTarget, null);
                return;
            }

            GetPointerWorldRay(node, origin, out Vector3 start, out Vector3 direction); // Quest의 포인터 시작 위치와 조준 회전을 함께 사용해 상점/하우징에서 위로 솟는 그립축 문제를 없앤다.
            Vector3 end = start + direction * RayLength; // 시각 레이는 충돌 여부와 관계없이 항상 같은 길이를 유지한다.
            IMushQuestRayTarget target = null;
            if (Physics.Raycast(start, direction, out RaycastHit hit, RayLength, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide))
            {
                target = FindRayTarget(hit.collider); // 클릭 판정만 충돌 지점을 사용하고 LineRenderer의 끝점은 줄이지 않는다.
            }

            line.SetPosition(0, start);
            line.SetPosition(1, end);
            SetHoveredTarget(ref hoveredTarget, target);
            if (selectPressed)
                target?.SelectWithQuestRay();
        }


        private void ConfigurePointerRotationActions()
        {
            leftPointerPositionAction?.Dispose(); // 재설정 시 이전 왼손 포인터 위치 액션을 폐기해 중복 바인딩을 남기지 않는다.
            rightPointerPositionAction?.Dispose(); // 재설정 시 이전 오른손 포인터 위치 액션도 같은 이유로 폐기한다.
            leftPointerRotationAction?.Dispose(); // 재설정 시 이전 왼손 포인터 회전 액션을 폐기한다.
            rightPointerRotationAction?.Dispose(); // 재설정 시 이전 오른손 포인터 회전 액션도 폐기한다.

            leftPointerPositionAction = new InputAction(
                name: "Mush Left Pointer Position",
                type: InputActionType.Value,
                binding: "<XRController>{LeftHand}/pointerPosition"); // OpenXR/XRI가 사용하는 왼손 포인터 시작 위치에 직접 연결한다.
            rightPointerPositionAction = new InputAction(
                name: "Mush Right Pointer Position",
                type: InputActionType.Value,
                binding: "<XRController>{RightHand}/pointerPosition"); // 오른손 레이도 실제 조준 시작 위치를 사용한다.
            leftPointerRotationAction = new InputAction(
                name: "Mush Left Pointer Rotation",
                type: InputActionType.Value,
                binding: "<XRController>{LeftHand}/pointerRotation"); // OpenXR/XRI가 사용하는 왼손 포인터 회전 컨트롤에 직접 연결한다.
            rightPointerRotationAction = new InputAction(
                name: "Mush Right Pointer Rotation",
                type: InputActionType.Value,
                binding: "<XRController>{RightHand}/pointerRotation"); // 오른손도 그립 축이 아니라 실제 조준축을 읽는다.

            leftPointerPositionAction.Enable(); // 씬이 활성화된 동안 왼손 포인터 위치를 계속 읽을 수 있게 한다.
            rightPointerPositionAction.Enable(); // 씬이 활성화된 동안 오른손 포인터 위치도 계속 읽을 수 있게 한다.
            leftPointerRotationAction.Enable(); // 씬이 활성화된 동안 왼손 포인터 회전을 계속 읽을 수 있게 한다.
            rightPointerRotationAction.Enable(); // 씬이 활성화된 동안 오른손 포인터 회전도 계속 읽을 수 있게 한다.
        }

        private void GetPointerWorldRay(XRNode node, Transform fallbackOrigin, out Vector3 start, out Vector3 direction)
        {
            InputAction positionAction = node == XRNode.LeftHand ? leftPointerPositionAction : rightPointerPositionAction; // 현재 손에 맞는 포인터 위치 액션을 선택한다.
            InputAction rotationAction = node == XRNode.LeftHand ? leftPointerRotationAction : rightPointerRotationAction; // 같은 손의 포인터 회전 액션도 선택한다.
            bool hasPointerPosition = positionAction != null && positionAction.enabled && positionAction.activeControl != null; // OpenXR 포인터 위치 컨트롤이 실제 장치에 연결됐는지 확인한다.
            bool hasPointerRotation = rotationAction != null && rotationAction.enabled && rotationAction.activeControl != null; // OpenXR 포인터 회전 컨트롤도 실제 장치에 연결됐는지 확인한다.

            start = fallbackOrigin != null ? fallbackOrigin.position : transform.position; // 포인터 위치를 못 읽는 기기를 위해 기존 컨트롤러 위치를 기본 시작점으로 둔다.
            direction = fallbackOrigin != null ? fallbackOrigin.forward : transform.forward; // 포인터 회전을 못 읽는 기기를 위해 기존 컨트롤러 방향을 기본 조준축으로 둔다.

            if (hasPointerPosition)
            {
                Vector3 pointerTrackingPosition = positionAction.ReadValue<Vector3>(); // OpenXR이 제공하는 실제 포인터 시작점을 추적 공간 기준으로 읽는다.
                Vector3 pointerLocalPosition = calibrationPosition + calibrationRotation * pointerTrackingPosition; // 헤드셋 시작 위치/방향 보정값을 포인터 위치에도 동일하게 적용한다.
                start = coordinateRoot != null ? coordinateRoot.TransformPoint(pointerLocalPosition) : pointerLocalPosition; // 좌식 추적 공간 기준 위치를 최종 월드 위치로 변환한다.
            }

            if (hasPointerRotation)
            {
                Quaternion pointerTrackingRotation = rotationAction.ReadValue<Quaternion>(); // OpenXR이 제공하는 실제 포인터/조준 자세 회전을 추적 공간 기준으로 읽는다.
                Quaternion pointerLocalRotation = calibrationRotation * pointerTrackingRotation; // 헤드셋 시작 방향 보정값을 포인터에도 동일하게 적용한다.
                Vector3 localDirection = pointerLocalRotation * Vector3.forward; // Unity 포인터 자세의 +Z 축을 실제 조준 방향으로 사용한다.
                Vector3 worldDirection = coordinateRoot != null ? coordinateRoot.TransformDirection(localDirection) : localDirection; // 좌식 추적 공간이 회전돼 있어도 월드 방향으로 정확히 변환한다.
                if (worldDirection.sqrMagnitude > 0.000001f)
                    direction = worldDirection.normalized; // 정상적인 포인터 회전을 읽었다면 그 방향으로 기존 그립축 방향을 교체한다.
            }

            if (direction.sqrMagnitude < 0.000001f)
                direction = Vector3.forward; // 드문 장치 오류로 0벡터가 들어와도 Physics.Raycast에 잘못된 방향을 넘기지 않게 한다.
            else
                direction.Normalize(); // 정상 방향은 항상 단위 벡터로 정규화해 고정 레이 길이가 정확히 유지되게 한다.
        }

        private static IMushQuestRayTarget FindRayTarget(Collider collider)
        {
            if (collider == null)
                return null;
            foreach (MonoBehaviour behaviour in collider.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (behaviour is IMushQuestRayTarget target)
                    return target;
            }
            return null;
        }

        private static void SetHoveredTarget(ref IMushQuestRayTarget current, IMushQuestRayTarget next)
        {
            if (current is UnityEngine.Object currentObject && currentObject == null)
                current = null;
            if (next is UnityEngine.Object nextObject && nextObject == null)
                next = null;
            if (ReferenceEquals(current, next))
                return;
            current?.SetQuestRayHovered(false);
            current = next;
            current?.SetQuestRayHovered(true);
        }

        private Transform CreateControllerAnchor(string objectName)
        {
            GameObject controllerObject = new(objectName);
            Transform anchor = controllerObject.transform;
            anchor.SetParent(coordinateRoot, false);
            return anchor;
        }

        private static LineRenderer BuildRay(string objectName, Transform parent)
        {
            GameObject rayObject = new(objectName);
            rayObject.transform.SetParent(parent, false);
            LineRenderer line = rayObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = 0.007f;
            line.endWidth = 0.002f;
            line.numCapVertices = 4;
            line.startColor = new Color(0.35f, 0.82f, 1f, 0.92f);
            line.endColor = new Color(0.75f, 0.94f, 1f, 0.45f);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                Material material = new(shader) { name = "Quest Pointer Ray Material" };
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", Color.white);
                line.material = material;
            }
            line.enabled = false;
            return line;
        }

        private static void SetRayVisible(LineRenderer line, bool visible)
        {
            if (line != null)
                line.enabled = visible;
        }

        private static bool TryGetPose(XRNode node, out Vector3 position, out Quaternion rotation)
        {
            UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            position = Vector3.zero;
            rotation = Quaternion.identity;
            bool hasPosition = device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out position);
            bool hasRotation = device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out rotation);
            return hasPosition && hasRotation;
        }

        private static bool ReadButton(XRNode node, InputFeatureUsage<bool> usage)
        {
            UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(usage, out bool pressed) && pressed;
        }

        private void OnDestroy()
        {
            SetHoveredTarget(ref leftHoveredTarget, null); // 현재 왼손 호버 강조를 해제한다.
            SetHoveredTarget(ref rightHoveredTarget, null); // 현재 오른손 호버 강조도 해제한다.
            leftPointerPositionAction?.Disable(); // 씬 종료 시 왼손 포인터 위치 입력 액션을 중지한다.
            rightPointerPositionAction?.Disable(); // 씬 종료 시 오른손 포인터 위치 입력 액션을 중지한다.
            leftPointerRotationAction?.Disable(); // 씬 종료 시 왼손 포인터 회전 입력 액션을 중지한다.
            rightPointerRotationAction?.Disable(); // 씬 종료 시 오른손 포인터 회전 입력 액션을 중지한다.
            leftPointerPositionAction?.Dispose(); // 런타임에 만든 왼손 포인터 위치 액션을 명시적으로 폐기한다.
            rightPointerPositionAction?.Dispose(); // 런타임에 만든 오른손 포인터 위치 액션도 폐기한다.
            leftPointerRotationAction?.Dispose(); // 런타임에 만든 왼손 포인터 회전 액션을 명시적으로 폐기한다.
            rightPointerRotationAction?.Dispose(); // 런타임에 만든 오른손 포인터 회전 액션도 명시적으로 폐기한다.
        }
    }

    [DisallowMultipleComponent]
    public sealed class MushQuestRayAction : MonoBehaviour, IMushQuestRayTarget
    {
        private Action action;
        private Renderer targetRenderer;
        private Color normalColor;
        private Color hoverColor;

        public void Configure(Action newAction, Renderer newTargetRenderer, Color newHoverColor)
        {
            action = newAction;
            targetRenderer = newTargetRenderer;
            hoverColor = newHoverColor;
            if (targetRenderer != null)
                normalColor = targetRenderer.material.color;
        }

        public void SetQuestRayHovered(bool hovered)
        {
            if (targetRenderer != null)
                targetRenderer.material.color = hovered ? hoverColor : normalColor;
        }

        public void SelectWithQuestRay()
        {
            action?.Invoke();
        }
    }
}
