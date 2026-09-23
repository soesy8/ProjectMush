using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Mush.Lobby
{
    /// <summary>
    /// Keeps the lobby controller laser visuals at a fixed visual length while
    /// leaving the XR Interaction Toolkit interactors themselves untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MushLobbyFixedRayVisuals : MonoBehaviour
    {
        private const float InteractionDistance = 30f;
        [SerializeField] private float rayLength = 4.5f; // 로비의 지도/상점/집 꾸미기까지 닿으면서도 방 전체를 가로지르지 않는 고정 시각 길이다.
        [SerializeField] private float startWidth = 0.005f; // 손 근처 레이 시작 부분의 두께를 얇게 유지해 시야를 덜 가린다.
        [SerializeField] private float endWidth = 0.0015f; // 레이 끝으로 갈수록 더 가늘어져 VR에서 선이 과하게 두껍게 느껴지지 않게 한다.

        private readonly List<Transform> rayOrigins = new(); // 왼손/오른손 Near-Far Interactor의 실제 조준 Transform들을 저장한다.
        private readonly List<LineRenderer> fixedLines = new(); // 각 조준 Transform에 대응하는 고정 길이 LineRenderer를 저장한다.
        private readonly List<NearFarInteractor> rayInteractors = new();
        private readonly List<bool> rayIsLeftHand = new();
        private readonly List<MushLobbyInteractable> directHovered = new();
        private InputAction leftPointerPosition;
        private InputAction rightPointerPosition;
        private InputAction leftPointerRotation;
        private InputAction rightPointerRotation;
        private InputAction leftTrigger;
        private InputAction rightTrigger;
        private int lastSelectionFrame = -1;

        public void Configure(float newRayLength)
        {
            rayLength = Mathf.Max(0.5f, newRayLength); // 잘못된 값이 들어와도 최소 0.5m 이상의 유효한 길이만 사용한다.
            if (Application.isPlaying)
                RebuildVisuals(); // 플레이 중 설정을 바꾼 경우에만 런타임 LineRenderer를 즉시 다시 구성한다.
        }

        private void Awake()
        {
            if (!Application.isPlaying)
                return; // 에디터 씬 패치 중에는 런타임 전용 LineRenderer와 재질을 만들지 않는다.
            ConfigureDirectInput();
            Mush.Quest.MushPlayerHands.InstallLobby(transform);
            RebuildVisuals(); // 런타임 시작 시 현재 XR 리그의 왼손/오른손 인터랙터를 찾아 고정 레이를 준비한다.
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return; // 편집 모드에서는 프리팹/씬 구조를 건드리지 않고 설정값만 저장한다.
            SetDirectInputEnabled(true);
            if (rayOrigins.Count == 0 || fixedLines.Count == 0)
                RebuildVisuals(); // 씬 재활성화나 도메인 리로드 뒤 참조가 비어 있으면 안전하게 다시 구성한다.
        }

        private void OnDisable()
        {
            for (int i = 0; i < directHovered.Count; i++) SetDirectHover(i, null);
            MushCanvasQuestInput.Release(UnityEngine.XR.XRNode.LeftHand);
            MushCanvasQuestInput.Release(UnityEngine.XR.XRNode.RightHand);
            SetDirectInputEnabled(false);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
                return; // 편집 모드에서는 레이 위치 갱신을 하지 않는다.
            bool visible = HasDirectXrInput();
            int count = Mathf.Min(rayOrigins.Count,
                Mathf.Min(fixedLines.Count, Mathf.Min(rayInteractors.Count, rayIsLeftHand.Count)));
            for (int index = 0; index < count; index++)
            {
                Transform origin = rayOrigins[index]; // 현재 손의 Near-Far Interactor 조준 원점을 가져온다.
                LineRenderer line = fixedLines[index]; // 해당 손의 고정 레이 LineRenderer를 가져온다.
                NearFarInteractor interactor = rayInteractors[index];
                if (origin == null || line == null || interactor == null)
                    continue; // 프리팹 구조가 바뀌어 참조가 사라진 경우 해당 손만 건너뛴다.

                UnityEngine.XR.XRNode hand = rayIsLeftHand[index] ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand;
                UnityEngine.XR.InputDevice device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(hand);
                bool tracked = visible && device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool isTracked) && isTracked;
                bool modal = MushLobbyMapPanel.IsOpen || (MushLobbyPauseMenu.Active != null && MushLobbyPauseMenu.Active.IsOpen);
                interactor.enableNearCasting = tracked && !modal;
                interactor.enableFarCasting = tracked && !modal;
                line.enabled = tracked;
                if (!tracked)
                {
                    SetDirectHover(index, null);
                    MushCanvasQuestInput.Release(hand);
                    continue;
                }

                GetPointerRay(rayIsLeftHand[index], interactor.curveOrigin != null ? interactor.curveOrigin : origin,
                    out Vector3 start, out Vector3 direction);
                line.SetPosition(0, start); // 레이 시작점을 현재 컨트롤러 조준 원점으로 갱신한다.
                line.SetPosition(1, start + direction * rayLength); // 충돌 여부와 관계없이 매 프레임 항상 같은 길이로 끝점을 유지한다.

                InputAction trigger = rayIsLeftHand[index] ? leftTrigger : rightTrigger;
                Ray pointerRay = new(start, direction);
                if (MushCanvasQuestInput.Handle(hand, pointerRay, trigger != null && trigger.WasPressedThisFrame(), trigger != null && trigger.IsPressed()) || modal)
                {
                    SetDirectHover(index, null);
                    continue;
                }
                MushLobbyInteractable hovered = null;
                if (Physics.Raycast(pointerRay, out RaycastHit hoverHit, rayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                    hovered = hoverHit.collider.GetComponentInParent<MushLobbyInteractable>();
                SetDirectHover(index, hovered);
                if (!interactor.hasSelection && trigger != null && trigger.WasPressedThisFrame() &&
                    lastSelectionFrame != Time.frameCount)
                {
                    lastSelectionFrame = Time.frameCount;
                    TriggerNearestTarget(new Ray(start, direction));
                }
            }
        }

        private void RebuildVisuals()
        {
            for (int i = 0; i < directHovered.Count; i++) SetDirectHover(i, null);
            directHovered.Clear();
            rayOrigins.Clear(); // 이전 XR 리그에서 찾은 조준 Transform 참조를 모두 비운다.
            fixedLines.Clear(); // 이전에 저장했던 LineRenderer 참조도 같이 비운다.
            rayInteractors.Clear();
            rayIsLeftHand.Clear();

            DisableTeleportInteractors();
            ConfigureTriggerSelection(); // 상점/하우징과 마찬가지로 로비 광선 선택도 검지 트리거 하나로 통일한다.

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (!IsNearFarInteractor(child.name))
                    continue; // Near-Far Interactor가 아닌 카메라/손/포크 인터랙터 등은 고정 레이 대상으로 사용하지 않는다.

                NearFarInteractor interactor = child.GetComponent<NearFarInteractor>();
                if (interactor == null)
                    continue;

                DisableDynamicCurveVisual(child); // 기본 XRI의 수축/확장 CurveVisualController와 기존 LineRenderer를 꺼 길이 변화를 제거한다.
                LineRenderer fixedLine = EnsureFixedLine(child); // 해당 인터랙터 아래에 우리 고정 길이 레이 오브젝트를 만들거나 기존 것을 재사용한다.
                if (fixedLine == null)
                    continue; // 어떤 이유로 LineRenderer를 만들지 못했다면 그 손만 안전하게 건너뛴다.

                rayOrigins.Add(child); // LateUpdate에서 사용할 실제 조준 Transform을 저장한다.
                fixedLines.Add(fixedLine); // 같은 인덱스에 대응하는 LineRenderer를 저장한다.
                rayInteractors.Add(interactor);
                rayIsLeftHand.Add(IsLeftHand(child));
                directHovered.Add(null);
            }
        }

        private void SetDirectHover(int index, MushLobbyInteractable target)
        {
            if (directHovered[index] == target) return;
            if (directHovered[index] != null) directHovered[index].SetQuestRayHovered(false);
            directHovered[index] = target;
            if (target != null) target.SetQuestRayHovered(true);
        }

        private void ConfigureDirectInput()
        {
            DisposeDirectInput();
            leftPointerPosition = CreateAction("Lobby Left Pointer Position", InputActionType.Value,
                "<XRController>{LeftHand}/pointerPosition");
            rightPointerPosition = CreateAction("Lobby Right Pointer Position", InputActionType.Value,
                "<XRController>{RightHand}/pointerPosition");
            leftPointerRotation = CreateAction("Lobby Left Pointer Rotation", InputActionType.Value,
                "<XRController>{LeftHand}/pointerRotation");
            rightPointerRotation = CreateAction("Lobby Right Pointer Rotation", InputActionType.Value,
                "<XRController>{RightHand}/pointerRotation");
            leftTrigger = CreateAction("Lobby Left Trigger", InputActionType.Button,
                "<XRController>{LeftHand}/triggerPressed");
            rightTrigger = CreateAction("Lobby Right Trigger", InputActionType.Button,
                "<XRController>{RightHand}/triggerPressed");
            SetDirectInputEnabled(true);
        }

        private static InputAction CreateAction(string actionName, InputActionType type, string binding)
        {
            return new InputAction(actionName, type, binding);
        }

        private void SetDirectInputEnabled(bool enabled)
        {
            SetActionEnabled(leftPointerPosition, enabled);
            SetActionEnabled(rightPointerPosition, enabled);
            SetActionEnabled(leftPointerRotation, enabled);
            SetActionEnabled(rightPointerRotation, enabled);
            SetActionEnabled(leftTrigger, enabled);
            SetActionEnabled(rightTrigger, enabled);
        }

        private static void SetActionEnabled(InputAction action, bool enabled)
        {
            if (action == null)
                return;
            if (enabled && !action.enabled)
                action.Enable();
            else if (!enabled && action.enabled)
                action.Disable();
        }

        private bool HasDirectXrInput()
        {
            return leftPointerPosition?.controls.Count > 0 || rightPointerPosition?.controls.Count > 0 ||
                   leftPointerRotation?.controls.Count > 0 || rightPointerRotation?.controls.Count > 0;
        }

        private void GetPointerRay(bool leftHand, Transform fallback, out Vector3 start, out Vector3 direction)
        {
            InputAction positionAction = leftHand ? leftPointerPosition : rightPointerPosition;
            InputAction rotationAction = leftHand ? leftPointerRotation : rightPointerRotation;
            start = fallback != null ? fallback.position : transform.position;
            direction = fallback != null ? fallback.forward : transform.forward;
            Transform trackingSpace = FindTrackingSpace(fallback);

            if (positionAction?.activeControl != null)
                start = trackingSpace.TransformPoint(positionAction.ReadValue<Vector3>());
            if (rotationAction?.activeControl != null)
                direction = trackingSpace.TransformDirection(rotationAction.ReadValue<Quaternion>() * Vector3.forward);
            if (direction.sqrMagnitude < 0.000001f)
                direction = fallback != null ? fallback.forward : Vector3.forward;
            direction.Normalize();
        }

        private Transform FindTrackingSpace(Transform child)
        {
            for (Transform current = child; current != null; current = current.parent)
            {
                if (string.Equals(current.name, "Camera Offset", StringComparison.OrdinalIgnoreCase))
                    return current;
                if (current == transform)
                    break;
            }
            return transform;
        }

        private void TriggerNearestTarget(Ray ray)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                rayLength,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, static (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<MushLobbyStationButton>() is MushLobbyStationButton stationButton)
                {
                    stationButton.Trigger();
                    return;
                }
                if (hit.collider.GetComponentInParent<MushLobbyInteractable>() is MushLobbyInteractable interactable)
                {
                    interactable.Trigger();
                    return;
                }
                if (hit.collider.GetComponentInParent<MushLobbyShopItem>() is MushLobbyShopItem shopItem)
                {
                    shopItem.Trigger();
                    return;
                }
                if (hit.collider.GetComponentInParent<MushLobbyChairSeatInteractable>() is MushLobbyChairSeatInteractable chairSeat)
                {
                    chairSeat.Trigger();
                    return;
                }
                if (hit.collider.GetComponentInParent<MushLobbyDogInteraction>() is MushLobbyDogInteraction dog)
                {
                    dog.Pet();
                    return;
                }
                if (!hit.collider.isTrigger) return;
            }
        }

        private void ConfigureTriggerSelection()
        {
            foreach (NearFarInteractor interactor in GetComponentsInChildren<NearFarInteractor>(true))
            {
                interactor.enableUIInteraction = false; // The explicit canvas bridge owns UI clicks and drags.
                XRInputButtonReader trigger = interactor.activateInput; // XRI 기본 Activate는 Quest 검지 트리거에 연결되어 있다.
                if (trigger == null)
                    continue;

                XRInputButtonReader triggerSelect = new("Lobby Trigger Select")
                {
                    inputSourceMode = trigger.inputSourceMode,
                    inputActionPerformed = trigger.inputActionPerformed,
                    inputActionValue = trigger.inputActionValue,
                    inputActionReferencePerformed = trigger.inputActionReferencePerformed,
                    inputActionReferenceValue = trigger.inputActionReferenceValue
                };
                if (trigger.inputSourceMode == XRInputButtonReader.InputSourceMode.ObjectReference)
                    triggerSelect.SetObjectReference(trigger.GetObjectReference());

                interactor.selectInput = triggerSelect; // 기존 Grip Select를 트리거 입력으로 교체해 모든 로비 선택을 동일하게 만든다.
            }
        }

        private void DisableTeleportInteractors()
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child != null && string.Equals(child.name, "Teleport Interactor", StringComparison.OrdinalIgnoreCase))
                    child.gameObject.SetActive(false);
            }
        }

        private void DisableDynamicCurveVisual(Transform interactorRoot)
        {
            foreach (MonoBehaviour behaviour in interactorRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue; // 삭제된 스크립트 슬롯 등이 있어도 예외 없이 지나간다.
                if (string.Equals(behaviour.GetType().Name, "CurveVisualController", StringComparison.Ordinal))
                    behaviour.enabled = false; // XRI 3.4 Starter Assets의 길이 수축/확장을 담당하는 컴포넌트만 비활성화한다.
            }

            foreach (LineRenderer line in interactorRoot.GetComponentsInChildren<LineRenderer>(true))
            {
                if (line != null && line.gameObject.name != "Mush Fixed Ray Line")
                    line.enabled = false; // 기존 동적 레이 LineRenderer만 숨기고 새 고정 레이는 건드리지 않는다.
            }
        }

        private LineRenderer EnsureFixedLine(Transform interactorRoot)
        {
            Transform existing = interactorRoot.Find("Mush Fixed Ray Line"); // 이미 한 번 생성된 고정 레이가 있으면 중복 오브젝트를 만들지 않게 찾는다.
            GameObject rayObject;
            if (existing != null)
            {
                rayObject = existing.gameObject; // 기존 고정 레이 오브젝트를 그대로 재사용한다.
            }
            else
            {
                rayObject = new GameObject("Mush Fixed Ray Line"); // 현재 손 전용 고정 레이 자식 오브젝트를 새로 만든다.
                rayObject.transform.SetParent(interactorRoot, false); // 조준 Transform 아래에 두어 리그 이동/회전과 함께 따라가게 한다.
            }

            LineRenderer line = rayObject.GetComponent<LineRenderer>(); // 기존 LineRenderer가 있는지 먼저 확인한다.
            if (line == null)
                line = rayObject.AddComponent<LineRenderer>(); // 없다면 시각 레이를 그릴 LineRenderer를 새로 추가한다.

            line.useWorldSpace = true; // 월드 좌표 두 점을 직접 갱신해 부모 스케일의 영향을 받지 않게 한다.
            line.positionCount = 2; // 직선 레이이므로 시작점과 끝점 두 개만 사용한다.
            line.startWidth = startWidth; // 설정한 얇은 시작 두께를 적용한다.
            line.endWidth = endWidth; // 설정한 더 얇은 끝 두께를 적용한다.
            line.numCapVertices = 4; // 선 끝을 약간 둥글게 만들어 거친 사각 끝 느낌을 줄인다.
            line.startColor = new Color(0.35f, 0.82f, 1f, 0.90f); // 기존 로비 레이와 비슷한 밝은 청색을 사용한다.
            line.endColor = new Color(0.75f, 0.94f, 1f, 0.42f); // 멀어질수록 투명해져 시야를 덜 방해하도록 한다.

            if (line.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"); // URP 우선, 없으면 기본 스프라이트 셰이더를 폴백으로 찾는다.
                if (shader != null)
                {
                    Material material = new Material(shader) { name = "Mush Lobby Fixed Ray Material" }; // 런타임 전용 레이 재질을 만들어 씬 에셋을 추가로 요구하지 않게 한다.
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", Color.white); // URP Unlit의 기본 색을 흰색으로 두어 LineRenderer의 그라디언트가 그대로 보이게 한다.
                    if (material.HasProperty("_Color"))
                        material.SetColor("_Color", Color.white); // 다른 셰이더에서도 같은 목적으로 색을 흰색으로 초기화한다.
                    line.material = material; // 생성한 재질을 이 손의 고정 레이에 적용한다.
                }
            }

            line.enabled = false; // XR 기기가 실제 활성화되기 전에는 레이가 미리 보이지 않도록 숨겨 둔다.
            return line; // 준비가 끝난 고정 LineRenderer를 호출자에게 반환한다.
        }

        private static bool IsNearFarInteractor(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return false; // 이름이 없는 Transform은 Near-Far Interactor로 취급하지 않는다.
            string normalized = objectName.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty); // 프리팹마다 하이픈/밑줄/공백 표기가 달라도 같은 이름으로 비교한다.
            return normalized.IndexOf("NearFarInteractor", StringComparison.OrdinalIgnoreCase) >= 0; // 왼손/오른손 Near-Far Interactor만 true를 반환한다.
        }

        private static bool IsLeftHand(Transform interactor)
        {
            for (Transform current = interactor; current != null; current = current.parent)
            {
                if (current.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (current.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            return false;
        }

        private void DisposeDirectInput()
        {
            leftPointerPosition?.Dispose();
            rightPointerPosition?.Dispose();
            leftPointerRotation?.Dispose();
            rightPointerRotation?.Dispose();
            leftTrigger?.Dispose();
            rightTrigger?.Dispose();
            leftPointerPosition = null;
            rightPointerPosition = null;
            leftPointerRotation = null;
            rightPointerRotation = null;
            leftTrigger = null;
            rightTrigger = null;
        }

        private void OnDestroy()
        {
            DisposeDirectInput();
        }
    }
}
