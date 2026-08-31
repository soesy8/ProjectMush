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
        private RectTransform dynamicUi; // 의자/탁자/개 침대 탭과 모델 카드, 장착/빼기 버튼을 매번 다시 그리는 UI 루트다.
        private readonly Dictionary<string, Texture2D> thumbnailCache = new(); // 실제 가구 FBX를 한 번 렌더한 작은 미리보기 이미지를 아이템 ID별로 보관한다.
        private int selectedPlacementIndex = MushHousingLayout.ChairPlacement; // 현재 열어 둔 하우징 탭이다. 시작은 의자 탭으로 둔다.
        private string selectedItemId = string.Empty; // 현재 탭에서 카드로 선택한 모델이다. 장착 버튼을 누르기 전까지 저장값은 바뀌지 않는다.
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
            workingState.Normalize(); // 구형 저장값을 먼저 현재 세 슬롯 구조로 정리한 뒤 탭의 초기 선택 모델을 찾는다.
            selectedItemId = FindDefaultSelectedItem(selectedPlacementIndex); // 현재 장착 모델이 있으면 그 모델, 없으면 첫 보유 모델을 카드 선택 상태로 둔다.
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
            MushVrRenderPerformance.ConfigureCamera(camera); // OpenXR 자동 동적 해상도가 이 VR 카메라에도 적용되게 한다.
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
            GameObject canvasObject = new("Housing Customization UI"); // 하우징 화면에서만 사용하는 UI 루트를 만든다.
            canvas = canvasObject.AddComponent<Canvas>(); // 일반 화면과 Quest 월드 캔버스 전환 모두 같은 Canvas를 사용한다.
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; // 에디터/PC에서는 화면 위에 바로 보이고 Quest가 활성화되면 기존 ConfigureWorldCanvas가 월드 캔버스로 바꾼다.
            canvas.sortingOrder = 20; // 3D 미리보기 방보다 UI가 항상 앞에 보이게 한다.
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>(); // 해상도가 달라도 같은 비율로 배치되도록 스케일러를 붙인다.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; // 1920x1080을 기준으로 전체 UI를 비례 확대/축소한다.
            scaler.referenceResolution = new Vector2(1920f, 1080f); // 현재 프로토타입 UI의 기준 해상도다.
            scaler.matchWidthOrHeight = 0.5f; // 가로와 세로 변화의 중간값으로 UI 스케일을 맞춘다.

            CreatePanel(canvas.transform, "Header", new Vector2(0f, 470f), new Vector2(1920f, 140f), HeaderColor); // 제목·현재 장착 상태·저장 버튼이 있는 상단 바를 만든다.
            CreateText(canvas.transform, "Title", new Vector2(0f, 500f), new Vector2(760f, 46f),
                34, Color.white, "로비 집 꾸미기"); // 하우징 화면 제목을 가운데 표시한다.
            statusText = CreateText(canvas.transform, "Placement Status", new Vector2(-130f, 458f),
                new Vector2(1280f, 34f), 20, new Color(1f, 0.78f, 0.42f), string.Empty); // 의자/탁자/침대의 실제 장착 상태를 상단에 계속 보여 준다.
            feedbackText = CreateText(canvas.transform, "Housing Feedback", new Vector2(-130f, 425f),
                new Vector2(1280f, 28f), 17, new Color(0.78f, 0.86f, 0.92f), lastHousingMessage); // 마지막으로 선택/장착/제거한 내용을 짧게 알려 준다.
            CreateButton(canvas.transform, "저장하고 로비로", new Vector2(735f, 480f), new Vector2(310f, 64f),
                SaveAndReturnToLobby, SelectedColor); // 현재 workingState를 저장하고 로비로 돌아가는 버튼이다.

            CreatePanel(canvas.transform, "Housing Browser Panel", new Vector2(0f, 205f),
                new Vector2(1760f, 410f), InventoryColor); // 탭과 작은 모델 카드가 들어가는 하나의 큰 브라우저 패널을 만든다.
            CreateText(canvas.transform, "Housing Browser Caption", new Vector2(0f, 392f), new Vector2(1180f, 32f),
                18, new Color(0.82f, 0.88f, 0.94f), "카테고리 선택 → 모델 선택 → 장착 / 빼기"); // 자유 배치가 아니라 슬롯의 모델을 교체하는 화면임을 짧게 설명한다.

            GameObject dynamicObject = new("Housing Category And Model Browser"); // 탭을 바꿀 때 카드와 버튼을 통째로 다시 그릴 동적 UI 루트를 만든다.
            dynamicUi = dynamicObject.AddComponent<RectTransform>(); // 모든 동적 요소를 한 부모 아래에서 관리한다.
            dynamicUi.SetParent(canvas.transform, false); // Canvas 중심 좌표를 그대로 사용하도록 부모에 붙인다.
            dynamicUi.anchorMin = Vector2.one * 0.5f; // 화면 중앙을 기준점으로 사용한다.
            dynamicUi.anchorMax = Vector2.one * 0.5f; // 해상도 변화 때 기준점이 흔들리지 않게 같은 앵커를 쓴다.
            dynamicUi.anchoredPosition = Vector2.zero; // 자식 버튼들이 지정한 좌표를 그대로 화면 중앙 기준으로 해석한다.
            dynamicUi.sizeDelta = new Vector2(1760f, 410f); // 브라우저 패널과 같은 크기로 잡아 배치 계산을 단순하게 유지한다.
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
                Destroy(furnitureRoot.GetChild(index).gameObject); // 이전 장착 모델을 모두 지우고 저장 상태 그대로 다시 만들어 중복을 막는다.

            BuildPlacementPreview(MushHousingLayout.ChairPlacement); // 의자 슬롯에 실제로 장착된 모델만 실제 위치에 보여 준다.
            BuildPlacementPreview(MushHousingLayout.TablePlacement); // 탁자 슬롯도 현재 장착 모델만 보여 준다.
            BuildPlacementPreview(MushHousingLayout.DogRestPlacement); // 개 침대 슬롯도 현재 장착 모델만 보여 준다.
        }

        private void BuildPlacementPreview(int placementIndex)
        {
            if (catalog == null)
                return; // 카탈로그를 읽지 못했다면 프리팹을 찾을 수 없으므로 미리보기 생성을 중단한다.

            string itemId = workingState.GetHousingPlacement(placementIndex); // 해당 고정 슬롯에 실제 저장된 아이템 ID를 읽는다.
            if (string.IsNullOrEmpty(itemId))
                return; // 빼기 상태라면 그 슬롯에는 아무 모델도 만들지 않는다.

            GameObject prefab = catalog.GetPrefab(itemId); // 현재 장착 아이템의 실제 FBX 프리팹을 카탈로그에서 찾는다.
            if (prefab == null)
                return; // 카탈로그에 모델이 연결되지 않은 아이템은 잘못된 빈 프리뷰를 만들지 않는다.

            GameObject holder = MushCustomizationVisuals.CreateFittedModel(
                prefab,
                furnitureRoot,
                "Preview " + itemId,
                MushHousingLayout.PreviewSize(placementIndex),
                MushHousingLayout.Position(placementIndex),
                true); // 실제 로비와 같은 위치·크기로 모델을 배치해 하우징 화면과 로비 결과가 다르지 않게 한다.
            if (holder != null)
                holder.transform.localRotation = MushHousingLayout.Rotation(placementIndex); // 실제 슬롯과 같은 Y축 회전까지 적용한다.
        }

        private void RefreshInventory()
        {
            for (int index = dynamicUi.childCount - 1; index >= 0; index--)
                Destroy(dynamicUi.GetChild(index).gameObject); // 탭 변경/선택 변경 때 이전 탭·카드·버튼을 지우고 현재 상태로 다시 만든다.

            CreateCategoryTab(MushHousingLayout.ChairPlacement, "의자", -300f); // 첫 번째 탭은 의자 슬롯만 보여 준다.
            CreateCategoryTab(MushHousingLayout.TablePlacement, "탁자", 0f); // 두 번째 탭은 탁자 슬롯만 보여 준다.
            CreateCategoryTab(MushHousingLayout.DogRestPlacement, "개 침대", 300f); // 세 번째 탭은 개 침대 슬롯만 보여 준다.

            string categoryName = PlacementDisplayName(selectedPlacementIndex); // 현재 탭 이름을 카드 영역 제목에 사용한다.
            CreateText(dynamicUi, "Selected Category", new Vector2(0f, 292f), new Vector2(700f, 36f),
                24, Color.white, categoryName + " 모델"); // 어떤 종류의 모델을 고르는 중인지 카드 바로 위에 표시한다.

            List<MushCustomizationItemDefinition> items = GetOwnedHousingItems(selectedPlacementIndex); // 현재 탭에 해당하면서 실제로 보유한 모델만 카드 후보로 모은다.
            if (items.Count == 0)
            {
                selectedItemId = string.Empty; // 보유 모델이 없으면 이전 탭의 선택 ID가 남아 장착되는 일을 막는다.
                CreateText(dynamicUi, "No Models", new Vector2(0f, 150f), new Vector2(900f, 80f),
                    24, new Color(0.78f, 0.84f, 0.90f), "이 종류의 보유 모델이 없습니다\n상점에서 먼저 구입하세요"); // 빈 카드 줄 대신 이유를 직접 안내한다.
            }
            else
            {
                bool selectionStillValid = false; // 탭을 다시 그리기 전에 현재 selectedItemId가 이 탭의 보유 목록 안에 있는지 검사한다.
                foreach (MushCustomizationItemDefinition item in items)
                {
                    if (item.id == selectedItemId)
                    {
                        selectionStillValid = true; // 같은 탭에서 이미 고른 카드라면 그 선택을 그대로 유지한다.
                        break;
                    }
                }
                if (!selectionStillValid)
                    selectedItemId = FindDefaultSelectedItem(selectedPlacementIndex); // 탭을 처음 열었거나 선택이 사라졌다면 장착 중 모델/첫 보유 모델을 자동 선택한다.

                float cardWidth = 230f; // 작은 네모 카드 하나의 가로 크기다.
                float cardHeight = 205f; // 모델 이미지와 이름이 함께 들어갈 카드 세로 크기다.
                float gap = 28f; // 카드가 여러 개 생겨도 서로 붙지 않게 간격을 둔다.
                float totalWidth = items.Count * cardWidth + Mathf.Max(0, items.Count - 1) * gap; // 현재 보유 카드 수에 맞는 전체 폭을 계산한다.
                float startX = -totalWidth * 0.5f + cardWidth * 0.5f; // 카드 묶음을 화면 중앙에 맞춘다.

                for (int index = 0; index < items.Count; index++)
                {
                    MushCustomizationItemDefinition item = items[index]; // 현재 카드가 보여 줄 아이템 정의를 가져온다.
                    bool selected = item.id == selectedItemId; // 카드 테두리/배경을 밝힐 현재 선택 여부다.
                    bool equipped = workingState.GetHousingPlacement(selectedPlacementIndex) == item.id; // 이 모델이 실제 로비 슬롯에 장착 중인지 따로 확인한다.
                    CreateModelCard(item, new Vector2(startX + index * (cardWidth + gap), 150f),
                        new Vector2(cardWidth, cardHeight), selected, equipped); // 실제 FBX 스냅샷이 들어간 작은 모델 카드를 만든다.
                }
            }

            string equippedId = workingState.GetHousingPlacement(selectedPlacementIndex); // 현재 탭 슬롯에 실제 장착된 아이템을 읽는다.
            string equippedName = string.IsNullOrEmpty(equippedId) ? "없음" : DisplayNameForItem(equippedId); // 빼기 상태도 명확히 표시할 이름을 만든다.
            string selectedName = string.IsNullOrEmpty(selectedItemId) ? "선택 없음" : DisplayNameForItem(selectedItemId); // 장착 전에 카드로 고른 모델 이름도 별도로 보여 준다.
            CreateText(dynamicUi, "Selected Model Info", new Vector2(0f, 22f), new Vector2(900f, 34f),
                20, new Color(0.86f, 0.90f, 0.94f), $"선택: {selectedName}     현재 장착: {equippedName}"); // 선택과 실제 장착이 다른 상태를 헷갈리지 않게 한 줄로 보여 준다.

            CreateButton(dynamicUi, "장착", new Vector2(-155f, -48f), new Vector2(270f, 72f),
                EquipSelectedModel, SelectedColor); // 카드로 선택한 모델을 현재 탭의 고정 슬롯에 실제 장착하는 버튼이다.
            CreateButton(dynamicUi, "빼기", new Vector2(155f, -48f), new Vector2(270f, 72f),
                RemoveCurrentPlacement, RemoveColor); // 현재 탭의 슬롯을 비워 로비에서 해당 가구 모델을 제거하는 버튼이다.
        }

        private void CreateCategoryTab(int placementIndex, string label, float x)
        {
            bool selected = selectedPlacementIndex == placementIndex; // 현재 열린 탭만 오렌지색으로 강조한다.
            CreateButton(dynamicUi, label, new Vector2(x, 345f), new Vector2(260f, 64f), () =>
            {
                selectedPlacementIndex = placementIndex; // 눌린 탭을 현재 하우징 카테고리로 바꾼다.
                selectedItemId = FindDefaultSelectedItem(placementIndex); // 그 슬롯의 장착 모델이 있으면 우선 선택하고, 없으면 첫 보유 모델을 선택한다.
                lastHousingMessage = label + " 모델을 선택하세요"; // 탭 전환 자체도 상단 피드백에 짧게 알려 준다.
                RefreshInventory(); // 방 전체를 다시 만들 필요 없이 카드/탭 UI만 즉시 갱신한다.
                RefreshStatus(); // 상단 안내 문구도 새 탭 상태로 맞춘다.
            }, selected ? SelectedColor : ButtonColor); // 선택 탭과 비선택 탭을 색으로 확실히 구분한다.
        }

        private void CreateModelCard(
            MushCustomizationItemDefinition item,
            Vector2 position,
            Vector2 size,
            bool selected,
            bool equipped)
        {
            GameObject cardObject = new(item.displayName + " Model Card"); // 모델 이미지·이름·장착 상태를 한 카드에 담을 부모 오브젝트를 만든다.
            RectTransform rect = cardObject.AddComponent<RectTransform>(); // 화면상의 카드 위치와 크기를 제어할 RectTransform을 붙인다.
            rect.SetParent(dynamicUi, false); // 현재 탭을 다시 그릴 때 카드도 같이 정리되도록 동적 UI 아래에 둔다.
            rect.anchorMin = Vector2.one * 0.5f; // 화면 중앙을 기준으로 카드 위치를 해석한다.
            rect.anchorMax = Vector2.one * 0.5f; // 앵커를 한 점에 고정해 카드 폭이 해상도에 따라 찌그러지지 않게 한다.
            rect.anchoredPosition = position; // 계산된 카드 줄의 위치에 둔다.
            rect.sizeDelta = size; // 작은 정사각형에 가까운 카드 크기를 적용한다.

            Image background = cardObject.AddComponent<Image>(); // 카드 배경이 선택/장착 상태를 색으로 보여 준다.
            Color cardColor = selected ? SelectedColor : equipped ? Color.Lerp(ButtonColor, SelectedColor, 0.35f) : ButtonColor; // 현재 선택을 가장 강하게, 장착 중이지만 다른 카드는 중간 정도로 표시한다.
            background.color = cardColor; // 계산한 상태색을 실제 카드 배경에 적용한다.
            BoxCollider collider = cardObject.AddComponent<BoxCollider>(); // Quest 고정 레이가 카드 전체를 실제 클릭 대상으로 인식할 수 있게 콜라이더를 붙인다.
            collider.size = new Vector3(size.x, size.y, 12f); // 카드 Rect 크기와 같은 클릭 영역을 사용한다.

            GameObject imageObject = new("Model Thumbnail"); // 실제 FBX 모습을 보여 줄 RawImage 자식을 만든다.
            RectTransform imageRect = imageObject.AddComponent<RectTransform>(); // 썸네일 영역의 위치/크기를 카드 안에서 따로 제어한다.
            imageRect.SetParent(cardObject.transform, false); // 카드가 움직이면 썸네일도 함께 움직이도록 자식으로 둔다.
            imageRect.anchorMin = Vector2.one * 0.5f; // 카드 중심 기준으로 배치한다.
            imageRect.anchorMax = Vector2.one * 0.5f; // 한 점 앵커를 사용한다.
            imageRect.anchoredPosition = new Vector2(0f, 18f); // 모델 이미지를 카드 위쪽에 배치한다.
            imageRect.sizeDelta = new Vector2(size.x - 28f, size.y - 62f); // 아래쪽 이름 한 줄을 제외한 대부분을 모델 이미지에 쓴다.
            RawImage thumbnail = imageObject.AddComponent<RawImage>(); // Texture2D 스냅샷을 UI에 그대로 보여 주는 RawImage다.
            thumbnail.texture = GetOrCreateThumbnail(item.id); // 실제 아이템 FBX를 렌더한 작은 이미지를 캐시에서 가져오거나 처음 한 번 만든다.
            thumbnail.color = Color.white; // 모델 고유 재질색이 그대로 보이도록 별도 틴트를 넣지 않는다.
            thumbnail.raycastTarget = false; // 클릭은 카드 부모가 받게 해서 이미지 자체가 Quest 레이를 가로채지 않게 한다.

            string stateText = equipped ? "  ·  장착 중" : string.Empty; // 실제 슬롯에 장착된 모델이면 카드 이름 옆에 상태를 표시한다.
            Text label = CreateText(cardObject.transform, "Card Label", new Vector2(0f, -78f),
                new Vector2(size.x - 16f, 36f), 19, Color.white, item.displayName + stateText); // 모델 이름과 장착 여부를 카드 아래에 표시한다.
            label.raycastTarget = false; // 글자가 부모 카드의 클릭을 막지 않게 한다.

            MushHousingUiButton button = cardObject.AddComponent<MushHousingUiButton>(); // 마우스와 Quest 레이가 같은 방식으로 카드를 선택하도록 공용 버튼 컴포넌트를 붙인다.
            button.Configure(rect, background, () =>
            {
                selectedItemId = item.id; // 카드 클릭은 즉시 장착하지 않고 우선 이 모델을 선택 상태로만 바꾼다.
                lastHousingMessage = item.displayName + " 선택"; // 사용자가 무엇을 골랐는지 상단 피드백에 알려 준다.
                RefreshInventory(); // 선택된 카드 색과 아래 선택 모델 문구를 바로 갱신한다.
                RefreshStatus(); // 피드백 텍스트도 즉시 갱신한다.
            }, cardColor); // 현재 상태의 카드 색을 hover 복귀 기준색으로 저장한다.
        }

        private List<MushCustomizationItemDefinition> GetOwnedHousingItems(int placementIndex)
        {
            List<MushCustomizationItemDefinition> result = new(); // 현재 탭에 들어갈 보유 모델 정의를 순서대로 모을 목록이다.
            foreach (MushCustomizationItemDefinition item in MushCustomizationDatabase.Items)
            {
                if (item == null || item.category != MushItemCategory.Housing)
                    continue; // 썰매·개 장비는 하우징 카드에 섞이지 않게 제외한다.
                if (!ItemMatchesPlacement(item.id, placementIndex))
                    continue; // 의자 탭에는 의자, 탁자 탭에는 탁자처럼 슬롯 종류가 맞는 모델만 남긴다.
                if (!workingState.Owns(item.id))
                    continue; // 상점에서 아직 얻지 않은 가구는 카드에 노출하지 않는다.
                result.Add(item); // 모든 조건을 만족한 실제 보유 모델을 카드 후보로 추가한다.
            }
            return result; // 현재 탭에서 보여 줄 모델 목록을 반환한다.
        }

        private static bool ItemMatchesPlacement(string itemId, int placementIndex)
        {
            if (string.IsNullOrEmpty(itemId))
                return false; // 빈 ID는 어떤 하우징 슬롯에도 속하지 않는다.

            return placementIndex switch
            {
                MushHousingLayout.ChairPlacement => itemId == MushCustomizationIds.FurnitureChair || itemId.StartsWith("furniture_chair_", StringComparison.Ordinal), // 현재 의자와 이후 추가될 의자 변형 ID를 같은 탭으로 묶는다.
                MushHousingLayout.TablePlacement => itemId == MushCustomizationIds.FurnitureTable || itemId.StartsWith("furniture_table_", StringComparison.Ordinal), // 현재 탁자와 이후 탁자 변형을 같은 탭으로 묶는다.
                MushHousingLayout.DogRestPlacement => itemId == MushCustomizationIds.FurnitureDogBed || itemId.StartsWith("furniture_dog_bed_", StringComparison.Ordinal), // 현재 개 침대와 이후 침대 변형을 같은 탭으로 묶는다.
                _ => false, // 정의되지 않은 슬롯에는 어떤 아이템도 허용하지 않는다.
            };
        }

        private string FindDefaultSelectedItem(int placementIndex)
        {
            string equipped = workingState.GetHousingPlacement(placementIndex); // 해당 탭에서 현재 실제 장착 중인 모델을 먼저 확인한다.
            if (!string.IsNullOrEmpty(equipped) && workingState.Owns(equipped) && ItemMatchesPlacement(equipped, placementIndex))
                return equipped; // 장착 모델이 정상적인 보유 아이템이면 그 카드를 처음부터 선택 상태로 둔다.

            List<MushCustomizationItemDefinition> owned = GetOwnedHousingItems(placementIndex); // 장착 모델이 없으면 이 종류의 보유 모델을 찾는다.
            return owned.Count > 0 ? owned[0].id : string.Empty; // 최소 하나가 있으면 첫 모델을 선택하고 아무것도 없으면 빈 선택을 반환한다.
        }

        private void EquipSelectedModel()
        {
            if (string.IsNullOrEmpty(selectedItemId) || !workingState.Owns(selectedItemId) || !ItemMatchesPlacement(selectedItemId, selectedPlacementIndex))
            {
                lastHousingMessage = "장착할 모델을 먼저 선택하세요"; // 유효한 카드 선택이 없으면 저장값을 건드리지 않고 안내만 보여 준다.
                RefreshStatus(); // 상단 안내 문구만 즉시 갱신한다.
                return;
            }

            workingState.SetHousingPlacement(selectedPlacementIndex, selectedItemId); // 현재 탭의 고정 슬롯에 선택된 모델 ID를 실제 장착값으로 기록한다.
            lastHousingMessage = PlacementDisplayName(selectedPlacementIndex) + " · " + DisplayNameForItem(selectedItemId) + " 장착"; // 어떤 슬롯에 무엇을 장착했는지 명확하게 알려 준다.
            RefreshAll(); // 실제 3D 방 미리보기·카드의 장착 상태·상단 현황을 한 번에 갱신한다.
        }

        private void RemoveCurrentPlacement()
        {
            string removed = workingState.GetHousingPlacement(selectedPlacementIndex); // 빼기 전 모델 이름을 피드백에 쓸 수 있게 먼저 읽는다.
            workingState.SetHousingPlacement(selectedPlacementIndex, string.Empty); // 현재 탭의 슬롯 ID를 비워 실제 로비에서도 해당 가구를 제거하도록 한다.
            lastHousingMessage = string.IsNullOrEmpty(removed)
                ? PlacementDisplayName(selectedPlacementIndex) + " 슬롯은 이미 비어 있습니다"
                : PlacementDisplayName(selectedPlacementIndex) + " · " + DisplayNameForItem(removed) + " 제거"; // 실제 제거 여부를 자연스럽게 알려 준다.
            RefreshAll(); // 3D 미리보기에서 가구가 즉시 사라지고 카드의 장착 표시도 없어지게 한다.
        }

        private static string PlacementDisplayName(int placementIndex)
        {
            return placementIndex switch
            {
                MushHousingLayout.ChairPlacement => "의자", // 의자 슬롯의 UI 표시명이다.
                MushHousingLayout.TablePlacement => "탁자", // 탁자 슬롯의 UI 표시명이다.
                MushHousingLayout.DogRestPlacement => "개 침대", // 개 수면 가구 슬롯의 UI 표시명이다.
                _ => "가구", // 예외 슬롯은 일반적인 이름으로 표시한다.
            };
        }

        private static string DisplayNameForItem(string itemId)
        {
            MushCustomizationItemDefinition definition = MushCustomizationDatabase.Find(itemId); // 데이터베이스에서 실제 한글 표시명을 찾는다.
            return definition != null ? definition.displayName : itemId; // 정의가 없는 임시 아이템도 ID 자체는 보여 주어 빈 글자가 되지 않게 한다.
        }

        private Texture2D GetOrCreateThumbnail(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || catalog == null)
                return null; // 아이템이나 카탈로그가 없으면 생성할 모델이 없으므로 빈 이미지를 반환한다.
            if (thumbnailCache.TryGetValue(itemId, out Texture2D cached) && cached != null)
                return cached; // 같은 모델은 매번 카메라 렌더를 하지 않고 처음 만든 이미지를 재사용한다.

            GameObject prefab = catalog.GetPrefab(itemId); // 카드에 보여 줄 실제 가구 FBX를 가져온다.
            if (prefab == null)
                return null; // 카탈로그 연결이 빠진 모델은 빈 카드 이미지로 남긴다.

            float stageOffset = thumbnailCache.Count * 40f; // 여러 썸네일을 같은 프레임에 만들 때 서로 카메라에 섞이지 않도록 스테이지를 멀리 떨어뜨린다.
            Vector3 anchor = new Vector3(1000f + stageOffset, 0f, 1000f); // 실제 하우징 방과 완전히 떨어진 월드 위치를 썸네일 촬영장으로 사용한다.
            GameObject stage = new("Housing Thumbnail Stage - " + itemId); // 이 모델 하나만 잠깐 렌더할 임시 스테이지를 만든다.
            stage.transform.position = anchor; // 다른 장면 오브젝트가 썸네일에 들어오지 않을 만큼 멀리 이동시킨다.

            GameObject holder = MushCustomizationVisuals.CreateFittedModel(
                prefab, stage.transform, "Thumbnail Model", 1.55f, Vector3.zero, true); // 카드 안에서 종류별 크기 차이가 너무 심하지 않도록 가장 큰 변을 약 1.55m로 맞춘다.
            if (holder != null)
                holder.transform.localRotation = Quaternion.Euler(0f, -32f, 0f); // 정면만 보는 것보다 형태가 잘 읽히도록 살짝 3/4 방향으로 돌린다.

            GameObject cameraObject = new("Housing Thumbnail Camera - " + itemId); // 이 카드 스냅샷만 찍고 사라질 임시 카메라를 만든다.
            Camera thumbnailCamera = cameraObject.AddComponent<Camera>(); // RenderTexture에 실제 3D 모델을 렌더할 카메라다.
            cameraObject.transform.position = anchor + new Vector3(2.35f, 1.55f, 2.65f); // 의자·탁자·침대 모두 읽히는 비스듬한 높이에 카메라를 둔다.
            cameraObject.transform.rotation = Quaternion.LookRotation(anchor + new Vector3(0f, 0.65f, 0f) - cameraObject.transform.position, Vector3.up); // 모델 중심보다 약간 위를 바라봐 바닥과 등받이까지 함께 담는다.
            thumbnailCamera.clearFlags = CameraClearFlags.SolidColor; // 카드 배경이 모델 뒤로 비치지 않게 단색으로 지운다.
            thumbnailCamera.backgroundColor = new Color(0.055f, 0.07f, 0.085f, 1f); // UI 패널과 어울리는 짙은 청회색 배경을 사용한다.
            thumbnailCamera.fieldOfView = 34f; // 작은 카드에서도 가구가 너무 작게 보이지 않는 화각을 사용한다.
            thumbnailCamera.nearClipPlane = 0.03f; // 가까운 가구 앞면이 잘리지 않게 근거리 클립을 줄인다.
            thumbnailCamera.farClipPlane = 14f; // 40m 이상 떨어진 다른 임시 스테이지나 실제 방은 절대 렌더하지 않게 짧게 제한한다.
            thumbnailCamera.enabled = false; // 일반 프레임마다 렌더하지 않고 아래에서 딱 한 번 Camera.Render()만 호출한다.

            RenderTexture renderTexture = new(256, 256, 16, RenderTextureFormat.ARGB32)
            {
                name = "Housing Thumbnail RT - " + itemId, // 디버깅 시 어떤 모델의 임시 렌더 텍스처인지 알 수 있게 이름을 붙인다.
            };
            renderTexture.Create(); // 카메라가 렌더할 GPU 텍스처를 실제로 생성한다.
            thumbnailCamera.targetTexture = renderTexture; // 임시 카메라 출력을 화면이 아니라 이 RenderTexture로 보낸다.
            thumbnailCamera.Render(); // 현재 모델과 조명을 한 프레임 즉시 렌더한다.

            RenderTexture previous = RenderTexture.active; // 다른 렌더 작업이 쓰던 활성 RenderTexture를 나중에 복원하기 위해 저장한다.
            RenderTexture.active = renderTexture; // ReadPixels가 방금 찍은 카드 이미지를 읽도록 활성 텍스처를 바꾼다.
            Texture2D thumbnail = new(256, 256, TextureFormat.RGBA32, false)
            {
                name = "Housing Thumbnail - " + itemId, // 캐시와 메모리 프로파일러에서 모델별 이미지를 알아보기 쉽게 이름을 붙인다.
            };
            thumbnail.ReadPixels(new Rect(0f, 0f, 256f, 256f), 0, 0, false); // GPU 결과를 UI RawImage가 쓸 수 있는 Texture2D 픽셀로 복사한다.
            thumbnail.Apply(false, false); // 밉맵 없이 바로 카드에 표시할 최종 픽셀을 적용한다.
            RenderTexture.active = previous; // 다른 카메라/렌더 코드가 영향을 받지 않도록 이전 활성 RenderTexture를 복원한다.

            thumbnailCamera.targetTexture = null; // 임시 카메라가 제거될 때 RenderTexture 참조를 붙잡지 않게 연결을 끊는다.
            renderTexture.Release(); // GPU 메모리를 즉시 반환한다.
            Destroy(renderTexture); // 런타임 객체도 프레임 종료 때 정리한다.
            Destroy(cameraObject); // 한 번 찍은 썸네일 카메라는 더 이상 필요 없으므로 제거한다.
            Destroy(stage); // 모델을 올려둔 임시 스테이지도 함께 제거한다.

            thumbnailCache[itemId] = thumbnail; // 다음 탭 전환부터는 이 Texture2D를 그대로 재사용한다.
            return thumbnail; // 방금 만든 실제 모델 모습을 카드 RawImage에 전달한다.
        }

        private void RefreshStatus()
        {
            string chairId = workingState.GetHousingPlacement(MushHousingLayout.ChairPlacement); // 의자 슬롯의 현재 실제 아이템 ID를 읽는다.
            string tableId = workingState.GetHousingPlacement(MushHousingLayout.TablePlacement); // 탁자 슬롯의 현재 실제 아이템 ID를 읽는다.
            string bedId = workingState.GetHousingPlacement(MushHousingLayout.DogRestPlacement); // 개 침대 슬롯의 현재 실제 아이템 ID를 읽는다.
            string chair = string.IsNullOrEmpty(chairId) ? "없음" : DisplayNameForItem(chairId); // 빈 슬롯이면 없음, 장착돼 있으면 실제 모델 이름을 표시한다.
            string table = string.IsNullOrEmpty(tableId) ? "없음" : DisplayNameForItem(tableId); // 탁자도 같은 규칙으로 표시한다.
            string bed = string.IsNullOrEmpty(bedId) ? "없음" : DisplayNameForItem(bedId); // 침대도 같은 규칙으로 표시한다.

            statusText.text = $"의자: {chair}     탁자: {table}     개 침대: {bed}"; // 세 슬롯의 현재 장착 상태를 한 줄에 보여 준다.
            if (feedbackText != null)
                feedbackText.text = lastHousingMessage; // 마지막 탭/카드/장착/제거 행동을 바로 아래 줄에 표시한다.
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
                    Destroy(material); // 하우징 미리보기 방에서 런타임으로 만든 재질을 씬 종료 때 정리한다.
            }

            foreach (Texture2D thumbnail in thumbnailCache.Values)
            {
                if (thumbnail != null)
                    Destroy(thumbnail); // 모델 카드용으로 CPU 메모리에 보관한 Texture2D 썸네일도 하우징 화면을 나갈 때 모두 해제한다.
            }
            thumbnailCache.Clear(); // 파괴된 텍스처 참조가 정적처럼 남아 있지 않게 캐시도 비운다.
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
