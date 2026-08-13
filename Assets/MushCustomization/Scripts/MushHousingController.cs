using System;
using System.Collections.Generic;
using Mush.Quest;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Mush.Customization
{
    [DisallowMultipleComponent]
    public sealed class MushHousingController : MonoBehaviour
    {
        private readonly List<Material> runtimeMaterials = new();

        private MushCustomizationCatalog catalog;
        private MushCustomizationState workingState;
        private Font font;
        private Transform furnitureRoot;
        private Canvas canvas;
        private Camera previewCamera;
        private RectTransform dynamicUi;
        private Text statusText; // 현재 세 하우징 슬롯에 어떤 가구가 장착돼 있는지 상단에 보여 준다.
        private Text feedbackText; // 버튼을 눌렀을 때 무엇이 장착됐는지 즉시 알려 주는 짧은 피드백 문구다.
        private MushQuestTrackedInputRig questRig;
        private bool questUiConfigured;
        private string lastHousingMessage = "가구를 누르면 정해진 위치에 바로 미리보기로 장착됩니다"; // 처음 화면에 들어왔을 때 조작 방법을 짧게 알려 준다.

        private static readonly Color HeaderColor = new(0.045f, 0.065f, 0.09f, 0.96f);
        private static readonly Color InventoryColor = new(0.05f, 0.075f, 0.10f, 0.96f);
        private static readonly Color ButtonColor = new(0.16f, 0.23f, 0.31f, 0.98f);
        private static readonly Color SelectedColor = new(0.82f, 0.40f, 0.08f, 0.98f);
        private static readonly Color RemoveColor = new(0.38f, 0.16f, 0.14f, 0.98f);

        private void Awake()
        {
            catalog = MushCustomizationCatalog.Load();
            workingState = MushCustomizationSave.Load().Clone();
            font = catalog != null && catalog.koreanFont != null
                ? catalog.koreanFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildLobbyPreview();
            BuildInterface();
            RefreshAll();
        }

        private void Update()
        {
            EnsureQuestUi();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SaveAndReturnToLobby();
        }

        private void EnsureQuestUi()
        {
            if (questUiConfigured || !XRSettings.isDeviceActive || previewCamera == null || canvas == null)
                return;

            questUiConfigured = true;
            questRig = MushQuestTrackedInputRig.InstallForCamera(previewCamera);
            MushQuestTrackedInputRig.ConfigureWorldCanvas(canvas, previewCamera);
            questRig?.SetRayEnabled(true);
        }

        private void BuildLobbyPreview()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight; // 하우징 화면도 실제 로비와 비슷한 따뜻한 실내 톤으로 보이게 한다.
            RenderSettings.ambientSkyColor = new Color(0.44f, 0.35f, 0.30f); // 천장 쪽의 기본 간접광을 따뜻한 갈색으로 맞춘다.
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.18f, 0.16f); // 벽 높이에서 받는 간접광을 낮춰 가구 실루엣이 읽히게 한다.
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.065f, 0.065f); // 바닥 아래에서 올라오는 빛은 어둡게 유지한다.

            GameObject stage = new("Housing Lobby Preview"); // 예전 작은 Mush_Lobby.fbx를 통째로 불러오지 않고 현재 하우징 슬롯 크기에 맞는 전용 미리보기 방을 만든다.
            BuildHousingPreviewRoom(stage.transform); // 실제 로비와 같은 가구 좌표를 볼 수 있도록 넓은 바닥과 세 벽을 절차적으로 생성한다.

            furnitureRoot = new GameObject("Housing Furniture Preview").transform; // 장착/해제할 가구 모델만 모아 매번 새로 그리는 루트를 만든다.
            furnitureRoot.SetParent(stage.transform, false); // 방과 같은 월드 좌표계에서 MushHousingLayout의 고정 위치를 그대로 사용한다.

            GameObject cameraObject = new("Housing Preview Camera"); // 하우징 화면 전용 카메라를 런타임에 만든다.
            Camera camera = cameraObject.AddComponent<Camera>(); // 장착된 의자·탁자·침대를 실제 배치와 비슷한 구도로 보여 줄 카메라다.
            previewCamera = camera; // VR 전환과 UI 월드 캔버스 설정에서도 같은 카메라를 쓰도록 필드에 저장한다.
            camera.clearFlags = CameraClearFlags.SolidColor; // 미리보기 방 밖은 단색으로 정리해 로비와 UI가 뒤섞여 보이지 않게 한다.
            camera.backgroundColor = new Color(0.12f, 0.10f, 0.11f); // 산장 내부와 어울리는 어두운 배경색을 사용한다.
            camera.fieldOfView = 52f; // 세 가구 슬롯이 한 화면에 읽히면서도 지나치게 광각으로 찌그러지지 않는 화각을 사용한다.
            camera.nearClipPlane = 0.03f; // VR에서 카메라 가까이 들어온 UI나 컨트롤러가 잘리지 않도록 근거리 클리핑을 짧게 둔다.
            camera.farClipPlane = 80f; // 작은 하우징 미리보기에는 충분한 원거리 클리핑 거리다.
            camera.transform.position = new Vector3(0f, 2.55f, 3.45f); // 새 산장의 뒤쪽 중앙에서 세 가구를 내려다보는 위치에 카메라를 둔다.
            camera.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 0.72f, -3.55f) - camera.transform.position,
                Vector3.up); // 의자·탁자·침대가 있는 실제 슬롯 깊이까지 시야 중심이 향하도록 카메라를 기울인다.
            cameraObject.tag = "MainCamera"; // 기존 Quest UI 설치 코드가 이 카메라를 메인 카메라처럼 사용할 수 있게 한다.
            cameraObject.AddComponent<AudioListener>(); // 씬에 오디오 리스너가 하나도 없는 경고를 막고 미리보기 소리를 들을 수 있게 한다.

            CreateDirectionalLight("Housing Warm Light", new Color(1f, 0.76f, 0.55f), 1.45f,
                Quaternion.Euler(42f, -34f, 0f), true); // 가구 앞면을 읽기 쉽게 따뜻한 주광을 비춘다.
            CreateDirectionalLight("Housing Fill Light", new Color(0.48f, 0.62f, 1f), 0.52f,
                Quaternion.Euler(28f, 145f, 0f), false); // 반대편이 새까맣게 뭉개지지 않도록 약한 차가운 보조광을 넣는다.
        }

        private void BuildHousingPreviewRoom(Transform parent)
        {
            Material floorMaterial = CreateRuntimeMaterial("Housing Preview Floor", new Color(0.34f, 0.23f, 0.20f), 0.18f); // 새 로비 마루와 비슷한 따뜻한 갈색 바닥 재질을 만든다.
            Material wallMaterial = CreateRuntimeMaterial("Housing Preview Wall", new Color(0.24f, 0.14f, 0.13f), 0.12f); // 가구가 잘 보이도록 조금 어두운 산장 벽 재질을 만든다.
            Material beamMaterial = CreateRuntimeMaterial("Housing Preview Beam", new Color(0.085f, 0.065f, 0.065f), 0.20f); // 모서리와 벽 상단을 구분할 어두운 목재 보 재질을 만든다.

            CreatePreviewPrimitive("Preview Floor", parent, new Vector3(0f, -0.08f, -2.05f), new Vector3(8.80f, 0.16f, 7.10f), floorMaterial); // 실제 하우징 슬롯 z=-4.45까지 전부 바닥 위에 들어오게 넓은 마루를 만든다.
            CreatePreviewPrimitive("Preview Back Wall", parent, new Vector3(0f, 1.55f, -5.55f), new Vector3(8.80f, 3.10f, 0.16f), wallMaterial); // 가구 뒤쪽을 막아 모델이 빈 허공에 떠 보이지 않게 한다.
            CreatePreviewPrimitive("Preview Left Wall", parent, new Vector3(-4.40f, 1.55f, -2.05f), new Vector3(0.16f, 3.10f, 7.10f), wallMaterial); // 왼쪽 의자·탁자 코너에 실제 방 경계를 보여 준다.
            CreatePreviewPrimitive("Preview Right Wall", parent, new Vector3(4.40f, 1.55f, -2.05f), new Vector3(0.16f, 3.10f, 7.10f), wallMaterial); // 오른쪽 개 침대 코너에도 같은 방 경계를 만든다.

            CreatePreviewPrimitive("Preview Back Top Beam", parent, new Vector3(0f, 2.98f, -5.43f), new Vector3(8.80f, 0.18f, 0.18f), beamMaterial); // 뒤벽과 천장 경계가 그냥 판처럼 보이지 않게 상단 목재 보를 넣는다.
            CreatePreviewPrimitive("Preview Left Corner Beam", parent, new Vector3(-4.29f, 1.55f, -5.43f), new Vector3(0.18f, 3.10f, 0.18f), beamMaterial); // 뒤왼쪽 모서리에 기둥을 세워 산장 골격 느낌을 남긴다.
            CreatePreviewPrimitive("Preview Right Corner Beam", parent, new Vector3(4.29f, 1.55f, -5.43f), new Vector3(0.18f, 3.10f, 0.18f), beamMaterial); // 뒤오른쪽 모서리에도 같은 기둥을 세운다.
        }

        private Material CreateRuntimeMaterial(string materialName, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit"); // 현재 URP 프로젝트에서 사용할 Lit 셰이더를 먼저 찾는다.
            if (shader == null)
                shader = Shader.Find("Standard"); // 예외적으로 URP 셰이더를 못 찾을 때 에디터 미리보기라도 보이도록 기본 셰이더로 대체한다.
            Material material = new(shader) { name = materialName, color = color }; // 런타임 전용 재질을 만들고 식별하기 쉬운 이름과 기본 색을 설정한다.
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness); // URP Lit이 지원하면 가구보다 덜 번들거리는 목재 질감을 위해 부드러움을 지정한다.
            runtimeMaterials.Add(material); // 씬을 나갈 때 OnDestroy에서 직접 파괴할 수 있도록 생성한 재질을 목록에 보관한다.
            return material;
        }

        private static GameObject CreatePreviewPrimitive(string objectName, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); // 프로토타입 하우징 미리보기 구조는 별도 FBX 없이 큐브로 만든다.
            primitive.name = objectName; // Hierarchy에서 어느 벽/바닥인지 바로 구분할 수 있는 이름을 지정한다.
            primitive.transform.SetParent(parent, false); // 미리보기 스테이지와 함께 정리되도록 같은 부모 아래에 둔다.
            primitive.transform.localPosition = position; // 실제 하우징 좌표계에 맞는 로컬 위치를 적용한다.
            primitive.transform.localRotation = Quaternion.identity; // 바닥과 벽은 축 정렬된 직육면체이므로 추가 회전은 사용하지 않는다.
            primitive.transform.localScale = scale; // 새 미리보기 방 치수에 맞는 폭·높이·깊이를 적용한다.
            Renderer renderer = primitive.GetComponent<Renderer>(); // 생성된 큐브의 렌더러를 가져온다.
            if (renderer != null)
                renderer.sharedMaterial = material; // 방 파츠끼리 같은 런타임 재질을 공유하도록 설정한다.
            Collider collider = primitive.GetComponent<Collider>(); // 하우징 UI 레이에 방 벽이 불필요하게 걸리지 않도록 기본 콜라이더를 찾는다.
            if (collider != null)
                UnityEngine.Object.Destroy(collider); // 미리보기 방은 배경일 뿐 클릭 대상이 아니므로 콜라이더를 제거한다.
            return primitive; // 필요하면 이후 추가 장식을 붙일 수 있도록 생성된 오브젝트를 반환한다.
        }

        private void BuildInterface()
        {
            GameObject canvasObject = new("Housing Customization UI");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            CreatePanel(canvas.transform, "Header", new Vector2(0f, 470f), new Vector2(1920f, 140f), HeaderColor);
            CreateText(canvas.transform, "Title", new Vector2(0f, 492f), new Vector2(760f, 52f),
                36, Color.white, "로비 집 꾸미기");
            statusText = CreateText(canvas.transform, "Placement Status", new Vector2(0f, 448f),
                new Vector2(1180f, 40f), 22, new Color(1f, 0.78f, 0.42f), string.Empty); // 장착 현황은 항상 화면 위쪽에서 확인할 수 있게 한다.
            feedbackText = CreateText(canvas.transform, "Housing Feedback", new Vector2(0f, 412f),
                new Vector2(1180f, 30f), 18, new Color(0.78f, 0.86f, 0.92f), lastHousingMessage); // 클릭 직후 실제로 무엇이 바뀌었는지 별도 줄에서 바로 보여 준다.
            CreateButton(canvas.transform, "저장하고 로비로", new Vector2(735f, 480f), new Vector2(310f, 64f),
                SaveAndReturnToLobby, SelectedColor);

            CreatePanel(canvas.transform, "Owned Housing Inventory", new Vector2(0f, -390f),
                new Vector2(1920f, 300f), InventoryColor);
            CreateText(canvas.transform, "Owned Caption", new Vector2(0f, -282f), new Vector2(1000f, 42f),
                25, Color.white, "보유한 가구 · 누르면 해당 고정 위치에 바로 장착됩니다"); // 현재 방식이 자유 배치가 아니라 슬롯별 모델 교체라는 점을 UI에서 바로 설명한다.

            GameObject dynamicObject = new("Housing Inventory Cards");
            dynamicUi = dynamicObject.AddComponent<RectTransform>();
            dynamicUi.SetParent(canvas.transform, false);
            dynamicUi.anchorMin = Vector2.one * 0.5f;
            dynamicUi.anchorMax = Vector2.one * 0.5f;
            dynamicUi.anchoredPosition = Vector2.zero;
            dynamicUi.sizeDelta = new Vector2(1700f, 230f);
        }

        private void RefreshAll()
        {
            workingState.Normalize();
            RefreshFurniturePreview();
            RefreshInventory();
            RefreshStatus();
        }

        private void RefreshFurniturePreview()
        {
            for (int index = furnitureRoot.childCount - 1; index >= 0; index--)
                Destroy(furnitureRoot.GetChild(index).gameObject); // 이전 장착 상태의 미리보기 모델을 먼저 모두 지워 중복 모델이 겹치지 않게 한다.

            BuildPlacementPreview(MushHousingLayout.ChairPlacement, MushCustomizationIds.FurnitureChair); // 의자 슬롯에 실제로 선택된 의자가 있으면 새 미리보기 방의 고정 위치에 다시 만든다.
            BuildPlacementPreview(MushHousingLayout.TablePlacement, MushCustomizationIds.FurnitureTable); // 탁자 슬롯도 같은 방식으로 현재 장착 상태만 다시 만든다.
            BuildPlacementPreview(MushHousingLayout.DogRestPlacement, MushCustomizationIds.FurnitureDogBed); // 세 번째 슬롯은 개 침대만 담당하며 옛 '기본 개 돌보기' 물체는 하우징 항목으로 취급하지 않는다.
        }

        private void BuildPlacementPreview(int placementIndex, string expectedItem)
        {
            if (workingState.GetHousingPlacement(placementIndex) != expectedItem || catalog == null)
                return; // 이 슬롯에 해당 아이템이 장착되지 않았거나 카탈로그를 못 읽었다면 아무 모델도 만들지 않는다.

            GameObject prefab = catalog.GetPrefab(expectedItem); // 상점에서 쓰는 것과 동일한 실제 FBX 프리팹을 가져온다.
            GameObject holder = MushCustomizationVisuals.CreateFittedModel(
                prefab,
                furnitureRoot,
                "Preview " + expectedItem,
                MushHousingLayout.PreviewSize(placementIndex),
                MushHousingLayout.Position(placementIndex),
                true); // 실제 로비와 같은 위치·크기로 가구를 만들어 버튼을 누른 즉시 변화가 화면에 보이게 한다.
            if (holder != null)
                holder.transform.localRotation = MushHousingLayout.Rotation(placementIndex); // 실제 로비 슬롯과 같은 회전값까지 적용해 미리보기와 장착 결과가 다르지 않게 한다.
        }

        private void RefreshInventory()
        {
            for (int index = dynamicUi.childCount - 1; index >= 0; index--)
                Destroy(dynamicUi.GetChild(index).gameObject); // 장착 상태가 바뀔 때 기존 카드들을 지우고 '장착 중' 표시까지 새 상태로 다시 만든다.

            List<Action> actions = new(); // 각 카드가 눌렸을 때 실행할 장착 함수를 카드 순서대로 저장한다.
            List<string> labels = new(); // 카드에 표시할 가구 이름과 장착 상태 문구를 저장한다.
            List<Color> colors = new(); // 장착 중인 카드는 오렌지색, 장착 가능한 카드는 어두운 색으로 구분한다.

            AddOwnedCard(MushCustomizationIds.FurnitureChair, MushHousingLayout.ChairPlacement,
                "의자 · 포근한 의자", actions, labels, colors); // 현재 보유한 의자 모델을 의자 전용 슬롯 카드로 추가한다.
            AddOwnedCard(MushCustomizationIds.FurnitureTable, MushHousingLayout.TablePlacement,
                "탁자 · 작은 탁자", actions, labels, colors); // 현재 보유한 탁자 모델을 탁자 전용 슬롯 카드로 추가한다.
            AddOwnedCard(MushCustomizationIds.FurnitureDogBed, MushHousingLayout.DogRestPlacement,
                "개 침대", actions, labels, colors); // 개 침대도 다른 가구처럼 독립적인 고정 슬롯 하나만 사용한다.

            if (labels.Count == 0)
            {
                labels.Add("보유한 가구가 없습니다\n상점에서 가구를 먼저 구입하세요"); // 아직 아무 가구도 없으면 빈 버튼 대신 이유를 바로 알려 준다.
                actions.Add(() => lastHousingMessage = "상점에서 하우징 가구를 구입한 뒤 다시 들어오세요"); // 눌러도 상태가 바뀌지 않는 카드이므로 안내 문구만 갱신한다.
                colors.Add(ButtonColor); // 비어 있는 안내 카드도 다른 UI와 어울리는 기본 색을 사용한다.
            }

            float cardWidth = 330f; // 세 종류가 한 줄에 넉넉히 들어오고 VR에서도 읽기 쉽도록 기존보다 카드 폭을 키운다.
            float gap = 32f; // 카드 사이를 조금 더 벌려 레이로 다른 버튼을 잘못 누르는 일을 줄인다.
            float totalWidth = labels.Count * cardWidth + Mathf.Max(0, labels.Count - 1) * gap; // 카드 수에 따라 전체 폭을 계산한다.
            float startX = -totalWidth * 0.5f + cardWidth * 0.5f; // 카드 묶음이 화면 가운데에 오도록 첫 카드 x좌표를 계산한다.
            for (int index = 0; index < labels.Count; index++)
            {
                int captured = index; // 람다에서 반복문의 마지막 index만 참조하지 않도록 현재 값을 따로 캡처한다.
                CreateButton(dynamicUi, labels[index], new Vector2(startX + index * (cardWidth + gap), -390f),
                    new Vector2(cardWidth, 122f), () =>
                    {
                        actions[captured](); // 선택한 카드의 장착 함수를 실행한다.
                        RefreshAll(); // 장착 즉시 3D 미리보기·버튼 색·상단 상태 문구를 한 번에 갱신한다.
                    }, colors[index]); // 현재 장착 중이면 SelectedColor가 그대로 보이므로 무엇이 선택됐는지 즉시 알 수 있다.
            }
        }

        private void AddOwnedCard(
            string itemId,
            int placementIndex,
            string displayName,
            List<Action> actions,
            List<string> labels,
            List<Color> colors)
        {
            if (!workingState.Owns(itemId))
                return; // 아직 상점에서 획득하지 않은 모델은 하우징 장착 목록에 노출하지 않는다.

            bool selected = workingState.GetHousingPlacement(placementIndex) == itemId; // 해당 슬롯이 지금 이 모델을 사용 중인지 확인한다.
            labels.Add(displayName + "\n" + (selected ? "● 장착 중" : "장착하기")); // 단순 색 차이뿐 아니라 글자로도 현재 장착 여부를 명확하게 표시한다.
            actions.Add(() => EquipPlacement(placementIndex, itemId, displayName)); // 이미 장착된 모델을 다시 눌러도 제거하지 않고 같은 슬롯에 확실히 장착하도록 한다.
            colors.Add(selected ? SelectedColor : ButtonColor); // 장착 중인 카드만 눈에 띄는 오렌지색으로 유지한다.
        }

        private void EquipPlacement(int placementIndex, string itemId, string displayName)
        {
            workingState.SetHousingPlacement(placementIndex, itemId); // 선택한 모델 ID를 해당 고정 하우징 슬롯의 현재 장착값으로 저장한다.
            lastHousingMessage = displayName + " 장착"; // 버튼을 누른 직후 어떤 모델이 바뀌었는지 상단 피드백에 남긴다.
        }

        private void RefreshStatus()
        {
            string chair = workingState.GetHousingPlacement(MushHousingLayout.ChairPlacement) == MushCustomizationIds.FurnitureChair
                ? "포근한 의자"
                : "없음"; // 의자 슬롯은 실제 장착된 모델이 있을 때만 이름을 표시한다.
            string table = workingState.GetHousingPlacement(MushHousingLayout.TablePlacement) == MushCustomizationIds.FurnitureTable
                ? "작은 탁자"
                : "없음"; // 탁자 슬롯도 현재 선택된 실제 모델 이름만 표시한다.
            string bed = workingState.GetHousingPlacement(MushHousingLayout.DogRestPlacement) == MushCustomizationIds.FurnitureDogBed
                ? "개 침대"
                : "없음"; // '기본 개 돌보기'라는 가상 항목을 제거하고 세 번째 슬롯은 개 침대 장착 여부만 보여 준다.

            statusText.text = $"의자: {chair}     탁자: {table}     개 침대: {bed}"; // 세 고정 슬롯을 같은 형식으로 나열해 현재 상태를 한눈에 읽게 한다.
            if (feedbackText != null)
                feedbackText.text = lastHousingMessage; // 마지막 버튼 조작 결과도 별도 줄에서 계속 보여 준다.
        }

        private void SaveAndReturnToLobby()
        {
            MushCustomizationSave.Save(workingState);
            if (Application.CanStreamedLevelBeLoaded("MushLobby"))
                SceneManager.LoadScene("MushLobby");
            else
                statusText.text = "MushLobby 씬을 찾을 수 없습니다";
        }

        private void CreateDirectionalLight(
            string lightName,
            Color color,
            float intensity,
            Quaternion rotation,
            bool shadows)
        {
            GameObject lightObject = new(lightName);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            lightObject.transform.rotation = rotation;
        }

        private static Image CreatePanel(
            Transform parent,
            string objectName,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            GameObject panel = new(objectName);
            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.one * 0.5f;
            rect.anchorMax = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = panel.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private MushHousingUiButton CreateButton(
            Transform parent,
            string label,
            Vector2 position,
            Vector2 size,
            Action callback,
            Color color)
        {
            GameObject buttonObject = new(label.Replace('\n', ' ') + " Button");
            RectTransform rect = buttonObject.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.one * 0.5f;
            rect.anchorMax = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = buttonObject.AddComponent<Image>();
            image.color = color;
            BoxCollider collider = buttonObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, 12f);

            Text text = CreateText(buttonObject.transform, "Label", Vector2.zero, size - new Vector2(18f, 8f),
                label.Contains("\n") ? 21 : 24, Color.white, label);
            text.raycastTarget = false;

            MushHousingUiButton button = buttonObject.AddComponent<MushHousingUiButton>();
            button.Configure(rect, image, callback, color);
            return button;
        }

        private Text CreateText(
            Transform parent,
            string objectName,
            Vector2 position,
            Vector2 size,
            int fontSize,
            Color color,
            string content)
        {
            GameObject textObject = new(objectName);
            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.one * 0.5f;
            rect.anchorMax = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = content;
            return text;
        }

        private void OnDestroy()
        {
            foreach (Material material in runtimeMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
        }
    }

    public sealed class MushHousingUiButton : MonoBehaviour, IMushQuestRayTarget
    {
        private RectTransform rect;
        private Image image;
        private Action callback;
        private Color normalColor;
        private bool questHovered;

        public void Configure(RectTransform newRect, Image newImage, Action newCallback, Color newNormalColor)
        {
            rect = newRect;
            image = newImage;
            callback = newCallback;
            normalColor = newNormalColor;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (rect == null)
                return;

            bool hovered = mouse != null &&
                           RectTransformUtility.RectangleContainsScreenPoint(rect, mouse.position.ReadValue(), null);
            if (image != null)
                image.color = hovered || questHovered ? Color.Lerp(normalColor, Color.white, 0.18f) : normalColor;
            if (hovered && mouse != null && mouse.leftButton.wasPressedThisFrame)
                callback?.Invoke();
        }

        public void SetQuestRayHovered(bool hovered)
        {
            questHovered = hovered;
        }

        public void SelectWithQuestRay()
        {
            callback?.Invoke();
        }
    }
}
