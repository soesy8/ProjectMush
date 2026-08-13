using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Mush.Lobby
{
    /// <summary>
    /// Keeps the lobby controller laser visuals at a fixed visual length while
    /// leaving the XR Interaction Toolkit interactors themselves untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MushLobbyFixedRayVisuals : MonoBehaviour
    {
        [SerializeField] private float rayLength = 4.5f; // 로비의 지도/상점/집 꾸미기까지 닿으면서도 방 전체를 가로지르지 않는 고정 시각 길이다.
        [SerializeField] private float startWidth = 0.005f; // 손 근처 레이 시작 부분의 두께를 얇게 유지해 시야를 덜 가린다.
        [SerializeField] private float endWidth = 0.0015f; // 레이 끝으로 갈수록 더 가늘어져 VR에서 선이 과하게 두껍게 느껴지지 않게 한다.

        private readonly List<Transform> rayOrigins = new(); // 왼손/오른손 Near-Far Interactor의 실제 조준 Transform들을 저장한다.
        private readonly List<LineRenderer> fixedLines = new(); // 각 조준 Transform에 대응하는 고정 길이 LineRenderer를 저장한다.

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
            RebuildVisuals(); // 런타임 시작 시 현재 XR 리그의 왼손/오른손 인터랙터를 찾아 고정 레이를 준비한다.
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return; // 편집 모드에서는 프리팹/씬 구조를 건드리지 않고 설정값만 저장한다.
            if (rayOrigins.Count == 0 || fixedLines.Count == 0)
                RebuildVisuals(); // 씬 재활성화나 도메인 리로드 뒤 참조가 비어 있으면 안전하게 다시 구성한다.
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
                return; // 편집 모드에서는 레이 위치 갱신을 하지 않는다.
            bool visible = XRSettings.isDeviceActive; // 실제 XR 기기가 활성화된 동안에만 고정 레이를 화면에 표시한다.
            int count = Mathf.Min(rayOrigins.Count, fixedLines.Count); // 양쪽 리스트 크기가 혹시 달라도 존재하는 쌍만 안전하게 갱신한다.
            for (int index = 0; index < count; index++)
            {
                Transform origin = rayOrigins[index]; // 현재 손의 Near-Far Interactor 조준 원점을 가져온다.
                LineRenderer line = fixedLines[index]; // 해당 손의 고정 레이 LineRenderer를 가져온다.
                if (origin == null || line == null)
                    continue; // 프리팹 구조가 바뀌어 참조가 사라진 경우 해당 손만 건너뛴다.

                line.enabled = visible; // XR이 켜졌을 때만 레이를 보이게 하고 에디터 일반 화면에서는 숨긴다.
                if (!visible)
                    continue; // 보이지 않는 프레임에는 불필요한 위치 계산을 하지 않는다.

                Vector3 start = origin.position; // XRI가 실제로 사용하는 조준 Transform의 월드 위치에서 레이를 시작한다.
                Vector3 direction = origin.forward.normalized; // XRI의 포인터 방향을 그대로 사용해 클릭 판정 방향과 시각 방향이 어긋나지 않게 한다.
                line.SetPosition(0, start); // 레이 시작점을 현재 컨트롤러 조준 원점으로 갱신한다.
                line.SetPosition(1, start + direction * rayLength); // 충돌 여부와 관계없이 매 프레임 항상 같은 길이로 끝점을 유지한다.
            }
        }

        private void RebuildVisuals()
        {
            rayOrigins.Clear(); // 이전 XR 리그에서 찾은 조준 Transform 참조를 모두 비운다.
            fixedLines.Clear(); // 이전에 저장했던 LineRenderer 참조도 같이 비운다.

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (!IsNearFarInteractor(child.name))
                    continue; // Near-Far Interactor가 아닌 카메라/손/포크 인터랙터 등은 고정 레이 대상으로 사용하지 않는다.

                DisableDynamicCurveVisual(child); // 기본 XRI의 수축/확장 CurveVisualController와 기존 LineRenderer를 꺼 길이 변화를 제거한다.
                LineRenderer fixedLine = EnsureFixedLine(child); // 해당 인터랙터 아래에 우리 고정 길이 레이 오브젝트를 만들거나 기존 것을 재사용한다.
                if (fixedLine == null)
                    continue; // 어떤 이유로 LineRenderer를 만들지 못했다면 그 손만 안전하게 건너뛴다.

                rayOrigins.Add(child); // LateUpdate에서 사용할 실제 조준 Transform을 저장한다.
                fixedLines.Add(fixedLine); // 같은 인덱스에 대응하는 LineRenderer를 저장한다.
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
    }
}
