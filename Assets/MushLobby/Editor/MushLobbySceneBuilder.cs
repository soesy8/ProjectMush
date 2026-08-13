using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mush.Customization;
using Mush.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby.Editor
{
    [InitializeOnLoad]
    public static class MushLobbySceneBuilder
    {
        private const string LobbyRoot = "Assets/MushLobby";
        private const string MaterialRoot = LobbyRoot + "/Materials";
        private const string ScenePath = "Assets/Scenes/MushLobby.unity";
        private const string LobbyModelPath = "Assets/Scenes/Mush_Lobby.fbx";
        private const string XrRigPath = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Variant.prefab";
        private const string LeftHandPath = "Assets/Oculus Hands/Prefabs/Left Hand Model.prefab";
        private const string RightHandPath = "Assets/Oculus Hands/Prefabs/Right Hand Model.prefab";
        private const string AxisRevisionMarker = "Mush Lobby Revision 15 - Housing Preview And Slot Cleanup";
        private const float ProceduralCabinWidth = 8.80f; // 마지막 좌식 배치에서 확정한 가로 폭이다. 기존 FBX를 늘리지 않고 새 골격을 이 절대 치수로 만든다.
        private const float ProceduralCabinDepth = 9.00f; // 줄인 가로 공간을 정면 깊이로 넘긴 최종 프로토타입 깊이다.
        private const float ProceduralCabinCenterZ = -1.75f; // 정면 벽 z=-6.25, 뒤벽 z=+2.75가 되도록 방 중심을 고정한다.
        private const float ProceduralCabinWallHeight = 2.62f; // 기존 산장의 벽-박공 접합 높이를 유지해 소품 높이와 어울리게 한다.
        private const float ProceduralCabinRidgeHeight = 4.34f; // 기존 산장 지붕 꼭대기 높이를 유지해 VR에서 천장이 과하게 높아지지 않게 한다.
        private static int stableEditFrames;

        private struct PanelButtonSpec
        {
            public string label;
            public MushLobbyAction action;

            public PanelButtonSpec(string newLabel, MushLobbyAction newAction)
            {
                label = newLabel;
                action = newAction;
            }
        }

        static MushLobbySceneBuilder()
        {
            QueueAutomaticMaintenance();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    QueueAutomaticMaintenance();
                else
                    CancelAutomaticMaintenance();
            };
        }

        private static void QueueAutomaticMaintenance()
        {
            stableEditFrames = 0;
            EditorApplication.update -= ApplyAfterStableEditFrames;
            EditorApplication.update += ApplyAfterStableEditFrames;
        }

        private static void CancelAutomaticMaintenance()
        {
            stableEditFrames = 0;
            EditorApplication.update -= ApplyAfterStableEditFrames;
        }

        private static void ApplyAfterStableEditFrames()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                stableEditFrames = 0;
                return;
            }

            if (++stableEditFrames < 3)
                return;

            CancelAutomaticMaintenance();
            EnsureSceneExists();
            ApplyAxisRevision();
        }

        [MenuItem("Mush/Lobby/Create Seated Lobby Prototype")]
        public static void CreateFromMenu()
        {
            if (File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("Mush Lobby", "MushLobby scene already exists.", "OK");
                return;
            }

            BuildScene();
        }

        public static void BuildFromCommandLine()
        {
            BuildScene();
        }

        private static void EnsureSceneExists()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!File.Exists(ScenePath) && AssetDatabase.LoadAssetAtPath<GameObject>(LobbyModelPath) != null)
                BuildScene();
        }

        private static void ApplyAxisRevision()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ScenePath))
                return;

            Scene previousScene = SceneManager.GetActiveScene();
            Scene targetScene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForRevision = !targetScene.IsValid() || !targetScene.isLoaded;
            if (openedForRevision)
                targetScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            if (!targetScene.IsValid() || !targetScene.isLoaded || FindInScene(targetScene, AxisRevisionMarker) != null)
            {
                if (openedForRevision && targetScene.IsValid() && targetScene.isLoaded)
                    EditorSceneManager.CloseScene(targetScene, true);
                return;
            }

            SceneManager.SetActiveScene(targetScene);
            try
            {
                GameObject cabin = FindInScene(targetScene, "Mush Lobby Cabin"); // 현재 씬에 이미 들어 있는 오두막 루트를 찾아, 이전 패치의 비율을 다시 곱하지 않고 정확한 최종 좌표로 덮어쓴다.
                if (cabin != null)
                    RebuildProceduralCabinShell(cabin.transform); // 기존 FBX 골격을 고무줄처럼 늘리지 않고, 기존 재질만 재사용해 최종 치수의 산장 골격을 처음부터 다시 만든다.

                SetTransform(targetScene, "Seated XR Player - No Locomotion", new Vector3(0f, 0f, 2.00f), new Vector3(0f, 180f, 0f)); // 좌석은 뒤쪽 벽 가까이에 유지하되, 넓어진 앞 공간 전체가 정면 시야에 들어오게 한다.
                GameObject seatedRig = FindInScene(targetScene, "Seated XR Player - No Locomotion");
                if (seatedRig != null)
                {
                    DisableUnusedGazeFeatures(seatedRig); // 컨트롤러 레이만 사용하는 게임이므로 스타터 에셋의 눈 추적 Gaze Interactor를 비활성화해 장치 없음 경고를 없앤다.
                    MushDesktopSeatedLook desktopLook = seatedRig.GetComponent<MushDesktopSeatedLook>(); // 에디터 테스트에서도 실제 좌식 VR처럼 고개만 좌우로 돌려 배치를 확인할 수 있게 한다.
                    if (desktopLook == null)
                        desktopLook = seatedRig.AddComponent<MushDesktopSeatedLook>(); // 구형 씬에 컴포넌트가 빠졌다면 자동으로 보충한다.
                    Camera seatedCamera = seatedRig.GetComponentInChildren<Camera>(true); // 실제 XR 카메라 Transform을 찾아 데스크톱 시야 테스트에 연결한다.
                    desktopLook.Configure(seatedCamera != null ? seatedCamera.transform : null); // 카메라가 존재할 때만 안전하게 추적 대상을 연결한다.

                    MushLobbyFixedRayVisuals fixedRays = seatedRig.GetComponent<MushLobbyFixedRayVisuals>(); // 좌식 XR 리그에 고정 길이 레이 시각화가 이미 있는지 확인한다.
                    if (fixedRays == null)
                        fixedRays = seatedRig.AddComponent<MushLobbyFixedRayVisuals>(); // 구형 씬에는 새 고정 레이 관리 컴포넌트를 자동으로 추가한다.
                    fixedRays.Configure(4.5f); // 지도/상점/집 꾸미기까지 닿는 4.5m 길이로 고정하고 XRI의 수축/확장 시각화를 끈다.
                    EditorUtility.SetDirty(fixedRays); // 새 컴포넌트 설정이 씬 저장에 확실히 기록되도록 변경 상태를 표시한다.
                }

                SetTransform(targetScene, "INT_MoneyBag Scene Position", new Vector3(3.55f, 0f, -0.91f), Vector3.zero); // 상점 주머니를 플레이어 옆 사각지대에서 빼고 정면 오른쪽 안쪽으로 올리기 위해 원본 자식 오프셋까지 계산해 그룹을 이동한다.
                SetTransform(targetScene, "PROP_MoneyBagStool Scene Position", new Vector3(3.55f, 0f, -0.91f), Vector3.zero); // 상점 받침도 주머니와 같은 그룹 오프셋으로 옮겨 서로 분리되지 않게 한다.
                SetTransform(targetScene, "INT_DogBowl Scene Position", new Vector3(2.30f, 0f, -3.55f), Vector3.zero); // 기본 개 돌보기 그릇을 오른쪽 개 침대 슬롯과 같은 생활 구역으로 옮겨 하우징 교체 의미를 맞춘다.
                SetTransform(targetScene, "INT_HousingChest", new Vector3(-1.90f, 0.39f, -1.35f), Vector3.zero); // 집 꾸미기 상자는 플레이어 옆이 아니라 정면 왼쪽 안쪽으로 올려 좌식 VR에서 쉽게 보이게 한다.
                SetTransform(targetScene, "Map Board Interaction", new Vector3(0.00f, 1.55f, -1.93f), Vector3.zero); // 지도는 좌우 사각지대를 피하도록 플레이어 정면 중앙의 독립 스탠드에 두고 짧은 레이로 선택하게 한다.
                SetTransform(targetScene, "Money Bag Interaction", new Vector3(1.90f, 0.61f, -1.35f), Vector3.zero); // 상점 핫스팟은 실제 주머니와 같은 정면 오른쪽 위치에 두어 옆을 크게 돌아보지 않아도 선택할 수 있게 한다.
                SetTransform(targetScene, "Housing Chest Interaction", new Vector3(-1.90f, 0.56f, -1.35f), Vector3.zero); // 집 꾸미기 핫스팟도 실제 상자와 같은 정면 왼쪽 위치로 옮겨 양옆 사각지대를 비운다.
                SetTransform(targetScene, "Lobby Status Board", new Vector3(2.55f, 2.62f, -6.14f), Vector3.zero); // 멀어진 정면 벽의 오른쪽 빈 공간은 작은 산장 상태판으로 채워 벽이 통째로 비어 보이지 않게 한다.
                SetTransform(targetScene, "Lobby Status", new Vector3(2.55f, 2.62f, -6.05f), new Vector3(0f, 180f, 0f)); // 상태 글자도 상태판 바로 앞에 맞춰 천장이나 지붕에 붙지 않게 한다.
                SetTransform(targetScene, "Fireplace Light", new Vector3(-2.80f, 0.82f, -5.78f), Vector3.zero); // 깊어진 정면 벽의 벽난로 위치까지 따뜻한 광원을 함께 옮긴다.

                foreach (string panelName in new[] { "MAP BOARD Panel", "MONEY BAG SHOP Panel", "HOUSE FLOOR PLAN Panel" })
                    SetTransform(targetScene, panelName, new Vector3(0f, 1.37f, -0.08f), new Vector3(0f, 180f, 0f)); // 실제 메뉴 패널은 좌석 가까운 기존 위치를 유지해 방 확장과 UI 조작 거리를 분리한다.

                SetTransform(targetScene, "Mochi - Gray Husky", new Vector3(-0.72f, 0f, 0.35f), Vector3.zero); // 구형 프로토타입 허스키 이름도 중앙 안전 통로 시작점으로 맞춘다.
                SetTransform(targetScene, "Bori - Brown Husky", new Vector3(0.63f, 0f, -0.55f), Vector3.zero); // 구형 프로토타입 말라뮤트 이름도 같은 기준으로 맞춘다.
                SetTransform(targetScene, "Mochi - Husky", new Vector3(-0.72f, 0f, 0.35f), Vector3.zero); // 현재 로비에서 실제 사용하는 허스키 루트도 중앙 안전 통로에서 시작한다.
                SetTransform(targetScene, "Bori - Malamute", new Vector3(0.63f, 0f, -0.55f), Vector3.zero); // 현재 말라뮤트 루트도 가구 바깥 중앙 통로에서 시작한다.
                SetTransform(targetScene, "Housing Slot 1 - Stool", MushHousingLayout.Position(MushHousingLayout.ChairPlacement), MushHousingLayout.Rotation(MushHousingLayout.ChairPlacement).eulerAngles); // 의자 슬롯을 실제 하우징 배치 좌표와 일치시킨다.
                SetTransform(targetScene, "Housing Slot 2 - Plant", MushHousingLayout.Position(MushHousingLayout.TablePlacement), MushHousingLayout.Rotation(MushHousingLayout.TablePlacement).eulerAngles); // 탁자 슬롯도 실제 하우징 배치 좌표와 일치시킨다.
                SetTransform(targetScene, "Housing Slot 3 - Side Table", MushHousingLayout.Position(MushHousingLayout.DogRestPlacement), MushHousingLayout.Rotation(MushHousingLayout.DogRestPlacement).eulerAngles); // 세 번째 슬롯은 개 침대의 고정 위치만 유지한다.
                DisableLegacyHousingSlotChildren(FindInScene(targetScene, "Housing Slot 1 - Stool")); // 예전 프로토타입 스툴이 실제 의자 아래 받침처럼 남지 않도록 슬롯 자식 시각물을 꺼 둔다.
                DisableLegacyHousingSlotChildren(FindInScene(targetScene, "Housing Slot 2 - Plant")); // 예전 화분 슬롯 시각물도 실제 탁자 모델과 겹치지 않게 꺼 둔다.
                DisableLegacyHousingSlotChildren(FindInScene(targetScene, "Housing Slot 3 - Side Table")); // 예전 사이드테이블 시각물도 개 침대 아래에 남지 않게 꺼 둔다.

                SetTransform(targetScene, "PROP_FireplaceRoot", new Vector3(-2.80f, 0f, -6.05f), Vector3.zero); // 벽난로는 깊어진 정면 벽 왼쪽에 붙여 실제로 방 앞쪽이 확장됐다는 기준점을 만든다.
                SetTransform(targetScene, "PROP_WindowRoot", new Vector3(0f, 2.08f, -6.20f), Vector3.zero); // 창문도 새 정면 벽으로 이동해 예전 벽 위치에 떠 있는 현상을 막는다.
                SetTransform(targetScene, "INT_MapBoard", new Vector3(0.00f, 1.55f, -2.05f), Vector3.zero); // 지도 실물은 독립 스탠드에 붙여 가까운 핫스팟과 시각적으로 일치시킨다.
                GameObject centerRug = FindInScene(targetScene, "PROP_CenterRug"); // 중앙에서 시야를 가리는 커다란 러그/판 오브젝트를 찾는다.
                if (centerRug != null)
                    centerRug.SetActive(false); // 기능이 없는 장식인데 좌식 시점에서 거대한 판처럼 보이므로 완전히 숨겨 바닥과 개 시야를 열어 둔다.

                Material darkWood = AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + "/CabinDarkWood.mat"); // 지도판을 벽에서 떼었으므로 기존 오두막 재질로 간단한 독립 스탠드를 만든다.
                GameObject sceneRoot = FindInScene(targetScene, "Mush Lobby Prototype"); // 새 스탠드와 개 전용 내비메시 런타임을 오두막 FBX와 분리된 씬 루트에서 관리한다.
                if (sceneRoot != null)
                    EnsureDogNavMeshRuntime(sceneRoot); // 산장 내부 치수에 맞는 개 전용 런타임 NavMesh를 씬 시작 전에 만들도록 컴포넌트를 보장한다.
                if (sceneRoot != null && darkWood != null)
                {
                    GameObject mapStand = EnsureNearMapStand(sceneRoot.transform, darkWood); // 지도판 아래 두 기둥과 받침을 만들어 공중에 떠 보이지 않게 한다.
                    EnsureFurnitureObstacle(mapStand); // 독립 지도 스탠드도 개가 통과하지 않는 고정 장애물로 등록한다.

                    GameObject statusTextObject = FindInScene(targetScene, "Lobby Status"); // 구형 씬에서 상태 글자가 상태판 자식으로 들어가 비정상 스케일을 물려받은 흔적을 찾는다.
                    if (statusTextObject != null)
                    {
                        statusTextObject.transform.SetParent(sceneRoot.transform, false); // 상태 글자를 씬 루트로 되돌려 보드의 비균일 스케일 영향을 끊는다.
                        statusTextObject.transform.localScale = Vector3.one; // 이전 부모에게서 물려받은 찌그러진 역스케일을 완전히 초기화한다.
                        statusTextObject.transform.localPosition = new Vector3(2.55f, 2.62f, -6.05f); // 새 정면 벽 상태판 바로 앞의 최종 위치를 다시 지정한다.
                        statusTextObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // 좌석을 바라보는 기존 글자 방향을 유지한다.
                    }
                }

                EnsureFurnitureObstacle(FindInScene(targetScene, "INT_MoneyBag Scene Position")); // 상점 주머니 자체도 개가 진입하지 않는 고정 장애물로 등록한다.
                EnsureFurnitureObstacle(FindInScene(targetScene, "INT_HousingChest")); // 집 꾸미기 상자도 고정 장애물로 등록해 개가 상자와 벽 사이에서 떨지 않게 한다.

                SetLabel(targetScene, "MAPS Label", 0.18f); // 지도 라벨은 새 스탠드 크기에 맞는 기존 가독성을 유지한다.
                SetLabel(targetScene, "SHOP Label", 0.48f); // 상점 라벨은 가까운 주머니 위에서 읽히도록 기존 크기를 유지한다.
                SetLabel(targetScene, "HOUSE Label", 0.55f); // 집 꾸미기 라벨도 가까운 상자 위에서 읽히도록 유지한다.

                foreach (string dogName in new[] { "Mochi - Gray Husky", "Bori - Brown Husky", "Mochi - Husky", "Bori - Malamute" })
                {
                    GameObject dog = FindInScene(targetScene, dogName); // 현재 로비의 두 개 루트를 이름으로 찾는다.
                    if (dog == null)
                        continue; // 한쪽 개가 없는 임시 테스트 씬에서도 수정 스크립트가 중단되지 않게 한다.
                    MushLobbyDogRoamer roamer = dog.GetComponent<MushLobbyDogRoamer>(); // 실제 생활 AI 컴포넌트를 가져온다.
                    if (roamer != null)
                        roamer.Configure(FindChild(dog.transform, "Dog Visual"), FindChild(dog.transform, "Tail"), new Vector2(-3.25f, -5.20f), new Vector2(3.25f, 0.85f)); // 넓어진 앞쪽 영역을 허용하되 실제 이동은 스크립트의 중앙 안전 경로만 사용한다.
                }

                GameObject marker = new GameObject(AxisRevisionMarker);
                marker.hideFlags = HideFlags.HideInHierarchy;
                SceneManager.MoveGameObjectToScene(marker, targetScene);
                EditorSceneManager.MarkSceneDirty(targetScene);
                EditorSceneManager.SaveScene(targetScene);
                Debug.Log("[Mush] Corrected MushLobby FBX axis direction and seated view.");
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                if (openedForRevision && targetScene.IsValid() && targetScene.isLoaded)
                    EditorSceneManager.CloseScene(targetScene, true);
            }
        }

        private static void SetTransform(Scene scene, string objectName, Vector3 localPosition, Vector3 localEuler)
        {
            GameObject target = FindInScene(scene, objectName);
            if (target == null)
                return;
            target.transform.localPosition = localPosition;
            target.transform.localRotation = Quaternion.Euler(localEuler);
        }

        private static void SetLabel(Scene scene, string objectName, float localZ)
        {
            GameObject label = FindInScene(scene, objectName);
            if (label == null)
                return;
            Vector3 position = label.transform.localPosition;
            label.transform.localPosition = new Vector3(position.x, position.y, localZ);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        private static void BuildScene()
        {
            EnsureFolder(MaterialRoot);

            Scene previousScene = SceneManager.GetActiveScene();
            bool replaceEmptyUntitledScene = string.IsNullOrEmpty(previousScene.path) && !previousScene.isDirty;
            Scene lobbyScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                replaceEmptyUntitledScene ? NewSceneMode.Single : NewSceneMode.Additive);
            lobbyScene.name = "MushLobby";
            SceneManager.SetActiveScene(lobbyScene);

            try
            {
                Material wall = GetMaterial("CabinWall", new Color(0.53f, 0.30f, 0.15f), 0.20f);
                Material darkWood = GetMaterial("CabinDarkWood", new Color(0.20f, 0.085f, 0.035f), 0.16f);
                Material floorWood = GetMaterial("CabinFloor", new Color(0.43f, 0.22f, 0.09f), 0.22f);
                Material cream = GetMaterial("CabinCream", new Color(0.82f, 0.70f, 0.50f), 0.26f);
                Material stone = GetMaterial("FireplaceStone", new Color(0.32f, 0.29f, 0.28f), 0.15f);
                Material charcoal = GetMaterial("FireplaceInner", new Color(0.055f, 0.04f, 0.035f), 0.08f);
                Material fire = GetMaterial("FireGlow", new Color(1f, 0.30f, 0.025f), 0.24f, Color.white * 2.6f);
                Material rug = GetMaterial("LobbyRug", new Color(0.35f, 0.055f, 0.045f), 0.20f);
                Material gold = GetMaterial("LobbyGold", new Color(0.90f, 0.57f, 0.08f), 0.38f);
                Material glass = GetMaterial("WindowBlue", new Color(0.27f, 0.55f, 0.68f), 0.72f);
                Material panel = GetMaterial("PanelBackground", new Color(0.09f, 0.055f, 0.032f), 0.16f);
                Material button = GetMaterial("PanelButton", new Color(0.58f, 0.30f, 0.10f), 0.24f);
                Material accent = GetMaterial("PanelAccent", new Color(0.95f, 0.58f, 0.10f), 0.30f);

                BuildLighting();

                GameObject sceneRoot = new GameObject("Mush Lobby Prototype");
                GameObject model = InstantiateLobbyModel(sceneRoot.transform);
                if (model == null)
                    throw new FileNotFoundException("Mush_Lobby.fbx could not be loaded.", LobbyModelPath);

                ColorCabinModel(model.transform, wall, darkWood, floorWood, cream, stone, charcoal, fire, rug, gold, glass);
                GroupAndOffset(model.transform, "INT_MoneyBag", new Vector3(3.55f, 0f, -0.91f)); // 실제 상점 주머니가 플레이어 오른쪽 가까운 위치에 오도록 원본 모델 자식 오프셋까지 포함해 이동한다.
                GroupAndOffset(model.transform, "PROP_MoneyBagStool", new Vector3(3.55f, 0f, -0.91f)); // 상점 받침도 주머니와 같은 위치 보정을 적용해 따로 떨어지지 않게 한다.
                GroupAndOffset(model.transform, "INT_DogBowl", new Vector3(2.30f, 0f, -3.55f)); // 기본 개 돌보기 그릇은 개 침대 교체 슬롯과 같은 오른쪽 생활 구역으로 이동한다.
                AddEnvironmentColliders(model.transform);
                RebuildProceduralCabinShell(model.transform); // 새 씬도 완성형 FBX 골격을 늘리지 않고 같은 절대 치수의 산장 껍데기를 새로 만든다.
                SetChildTransform(model.transform, "INT_HousingChest", new Vector3(-1.90f, 0.39f, -1.35f), Vector3.zero); // 집 꾸미기 상자 실물을 좌석 가까운 왼쪽으로 옮긴다.
                SetChildTransform(model.transform, "INT_MapBoard", new Vector3(0.00f, 1.55f, -2.05f), Vector3.zero); // 지도판 실물은 가까운 독립 스탠드 위치로 옮긴다.
                SetChildTransform(model.transform, "PROP_WindowRoot", new Vector3(0f, 2.08f, -6.20f), Vector3.zero); // 창문은 새 정면 벽으로 옮겨 깊이 확장이 구조적으로 보이게 한다.
                GameObject mapStand = EnsureNearMapStand(sceneRoot.transform, darkWood); // 벽에서 떼어낸 지도판 아래에 간단한 목재 스탠드를 만든다.
                EnsureFurnitureObstacle(mapStand); // 지도 스탠드도 개가 통과하지 않는 고정 장애물로 등록한다.
                EnsureFurnitureObstacle(FindChild(model.transform, "INT_MoneyBag Scene Position")?.gameObject); // 상점 주머니 영역도 개가 들어가지 않는 고정 장애물로 등록한다.
                EnsureFurnitureObstacle(FindChild(model.transform, "INT_HousingChest")?.gameObject); // 집 꾸미기 상자도 개 회피 대상으로 등록한다.

                GameObject xrRig = BuildXrRig(sceneRoot.transform, out Camera camera);

                GameObject controllerObject = new GameObject("Lobby Game State");
                controllerObject.transform.SetParent(sceneRoot.transform, false);
                MushLobbyController controller = controllerObject.AddComponent<MushLobbyController>();
                controller.SetKoreanFont(AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/Hakgyoansim_PosterB.ttf"));

                EnsureInteractionManager(sceneRoot.transform, xrRig);

                GameObject mapHotspot = CreateHotspot(
                    "Map Board Interaction", sceneRoot.transform, new Vector3(0.00f, 1.55f, -1.93f),
                    new Vector3(1.65f, 1.25f, 0.30f), controller, MushLobbyAction.OpenMapBoard);
                GameObject shopHotspot = CreateHotspot(
                    "Money Bag Interaction", sceneRoot.transform, new Vector3(1.90f, 0.61f, -1.35f),
                    new Vector3(0.95f, 1.22f, 0.90f), controller, MushLobbyAction.OpenShop);
                GameObject housingHotspot = CreateHotspot(
                    "Housing Chest Interaction", sceneRoot.transform, new Vector3(-1.90f, 0.56f, -1.35f),
                    new Vector3(1.25f, 1.15f, 0.95f), controller, MushLobbyAction.OpenHousing);
                CreateLabel("지도", mapHotspot.transform, new Vector3(0f, 0.78f, 0.18f), 0.038f, new Color(1f, 0.76f, 0.25f));
                CreateLabel("상점", shopHotspot.transform, new Vector3(0f, 0.77f, 0.48f), 0.036f, new Color(1f, 0.76f, 0.25f));
                CreateLabel("집 꾸미기", housingHotspot.transform, new Vector3(0f, 0.72f, 0.55f), 0.034f, new Color(1f, 0.76f, 0.25f));

                GameObject statusBoard = CreatePrimitive(
                    "Lobby Status Board", PrimitiveType.Cube, sceneRoot.transform,
                    new Vector3(2.55f, 2.62f, -6.14f), new Vector3(2.30f, 0.46f, 0.08f), Vector3.zero, darkWood, false); // 지붕선 아래에 작은 상태판으로 두어 확장된 벽의 구조와 충돌하지 않게 한다.
                TextMesh lobbyStatus = CreateText(
                    "Lobby Status", sceneRoot.transform, new Vector3(2.55f, 2.62f, -6.05f), 0.020f, // 상태 글자는 보드의 자식이 아니라 씬 루트에 두어 보드 스케일에 찌그러지지 않게 한다.
                    TextAnchor.MiddleCenter, new Color(1f, 0.80f, 0.42f), "머쉬 산장");
                lobbyStatus.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                GameObject mapPanel = CreatePanel(
                    "맵 게시판", controller, sceneRoot.transform, panel, button, accent,
                    new[]
                    {
                        new PanelButtonSpec("기본 설원", MushLobbyAction.SelectSnowfield),
                        new PanelButtonSpec("나무 숲", MushLobbyAction.SelectForest)
                    }, out TextMesh mapStatus);

                GameObject shopPanel = CreatePanel(
                    "주머니 상점", controller, sceneRoot.transform, panel, button, accent,
                    new[]
                    {
                        new PanelButtonSpec("개 목도리 30골드", MushLobbyAction.BuyScarf),
                        new PanelButtonSpec("나무 숲 지도", MushLobbyAction.BuyForest)
                    }, out TextMesh shopStatus);

                GameObject housingPanel = CreatePanel(
                    "집 꾸미기", controller, sceneRoot.transform, panel, button, accent,
                    new[]
                    {
                        new PanelButtonSpec("공간 1", MushLobbyAction.HousingSlotA),
                        new PanelButtonSpec("공간 2", MushLobbyAction.HousingSlotB),
                        new PanelButtonSpec("공간 3", MushLobbyAction.HousingSlotC)
                    }, out TextMesh housingStatus, true);

                BuildDogTeam(sceneRoot.transform, out MushLobbyDogRoamer[] dogs, out GameObject[] scarves);
                GameObject[] furniture = BuildHousingFurniture(sceneRoot.transform);

                controller.Configure(
                    camera, lobbyStatus,
                    mapPanel, shopPanel, housingPanel,
                    mapStatus, shopStatus, housingStatus,
                    scarves, furniture, dogs);

                mapPanel.SetActive(false);
                shopPanel.SetActive(false);
                housingPanel.SetActive(false);
                foreach (GameObject scarf in scarves) scarf.SetActive(false);
                foreach (GameObject item in furniture) item.SetActive(false);

                EditorSceneManager.SaveScene(lobbyScene, ScenePath);
                PutLobbyFirstInBuildSettings();
                AssetDatabase.SaveAssets();
                Debug.Log("[Mush] Created seated lobby prototype: " + ScenePath);
            }
            finally
            {
                if (!replaceEmptyUntitledScene && previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);

                if (!replaceEmptyUntitledScene && lobbyScene.IsValid() && lobbyScene.isLoaded)
                    EditorSceneManager.CloseScene(lobbyScene, true);
            }
        }

        private static void BuildLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.33f, 0.24f);
            RenderSettings.ambientEquatorColor = new Color(0.26f, 0.18f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.10f, 0.065f, 0.04f);
            RenderSettings.fog = false;

            GameObject moonlight = new GameObject("Soft Window Light");
            Light directional = moonlight.AddComponent<Light>();
            directional.type = LightType.Directional;
            directional.color = new Color(0.72f, 0.82f, 1f);
            directional.intensity = 0.75f;
            directional.shadows = LightShadows.Soft;
            moonlight.transform.rotation = Quaternion.Euler(38f, -18f, 0f);

            GameObject firelight = new GameObject("Fireplace Light");
            firelight.transform.position = new Vector3(-2.80f, 0.82f, -5.78f); // 벽난로를 왼쪽으로 이동한 위치에 맞춰 광원도 함께 옮긴다.
            Light point = firelight.AddComponent<Light>();
            point.type = LightType.Point;
            point.color = new Color(1f, 0.38f, 0.10f);
            point.intensity = 2.8f;
            point.range = 5.2f;
            point.shadows = LightShadows.Soft;
        }

        private static GameObject InstantiateLobbyModel(Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyModelPath);
            if (prefab == null)
                return null;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = "Mush Lobby Cabin";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return instance;
        }

        private static void ColorCabinModel(
            Transform model, Material wall, Material darkWood, Material floorWood, Material cream,
            Material stone, Material charcoal, Material fire, Material rug, Material gold, Material glass)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                string name = renderer.gameObject.name;
                Material material = wall;

                if (name.Contains("Beam") || name.Contains("Roof") || name.Contains("Ridge")) material = darkWood;
                else if (name.Contains("Floor")) material = floorWood;
                else if (name.Contains("MapBoard")) material = name.Contains("Paper") ? cream : darkWood;
                else if (name.Contains("DogBowl_Food")) material = darkWood;
                else if (name.Contains("DogBowl")) material = cream;
                else if (name.Contains("HousingChest_Band") || name.Contains("HousingChest_Lock")) material = gold;
                else if (name.Contains("HousingChest")) material = darkWood;
                else if (name.Contains("MoneyBag")) material = name.Contains("Stool") ? darkWood : gold;
                else if (name.Contains("CenterRug")) material = rug;
                else if (name.Contains("FireFlame")) material = fire;
                else if (name.Contains("FireLog")) material = darkWood;
                else if (name.Contains("Fireplace_Inner")) material = charcoal;
                else if (name.Contains("Fireplace")) material = stone;
                else if (name.Contains("WindowGlass")) material = glass;
                else if (name.Contains("WindowFrame")) material = darkWood;

                renderer.sharedMaterial = material;
            }
        }

        private static void GroupAndOffset(Transform model, string namePrefix, Vector3 offset)
        {
            List<Transform> matches = model.GetComponentsInChildren<Transform>(true)
                .Where(item => item != model && item.name.StartsWith(namePrefix))
                .ToList();
            if (matches.Count == 0)
                return;

            GameObject group = new GameObject(namePrefix + " Scene Position");
            group.transform.SetParent(model, false);
            foreach (Transform item in matches)
                item.SetParent(group.transform, true);
            group.transform.localPosition += offset;
        }

        private static void ExpandCabinForDogLifePrototype(Transform model)
        {
            ApplyDeepSeatedCabinLayout(model); // 새 씬 생성과 기존 씬 보정이 같은 정확한 좌표표를 사용하게 해 확장을 여러 번 적용해도 크기가 계속 커지지 않게 한다.
        }

        private static void ApplyDeepSeatedCabinLayout(Transform model)
        {
            if (model == null)
                return; // 오두막 모델을 찾지 못한 임시 씬에서는 아무 Transform도 건드리지 않는다.

            foreach (Transform item in model.GetComponentsInChildren<Transform>(true))
            {
                if (item == model)
                    continue; // FBX/오두막 루트는 월드 원점에 그대로 두고 실제 구조 파츠만 정확한 최종값으로 맞춘다.

                string itemName = item.name; // 파츠 이름을 기준으로 바닥, 벽, 기둥, 지붕의 최종 Transform을 구분한다.
                Vector3 position = item.localPosition; // 대상이 아닌 축은 기존 값을 유지할 수 있도록 현재 위치를 복사한다.
                Vector3 scale = item.localScale; // 대상이 아닌 축은 기존 값을 유지할 수 있도록 현재 스케일을 복사한다.
                bool changed = false; // 실제 수정 대상인지 기록해 불필요한 Transform 갱신을 피한다.

                if (itemName.StartsWith("ENV_FloorPlank_", System.StringComparison.Ordinal))
                {
                    if (int.TryParse(itemName.Substring("ENV_FloorPlank_".Length), out int plankIndex))
                        position.x = (6.5f - plankIndex) * 0.73f; // 12장 바닥 판재를 약 x=-4.0~+4.0에 다시 모아 파란 표시만큼 가로 폭을 줄인다.
                    position.z = -1.95f; // 줄인 가로 폭만큼 정면 깊이를 늘린 새 방의 중심으로 바닥을 옮겨 좌식 시야 앞쪽 공간을 넓힌다.
                    scale.x = 1.46f; // 파란 표시만큼 좌우 폭을 줄여 옆으로 과하게 고개를 돌려야 하는 공간을 없앤다.
                    scale.z = 1.72f; // 줄인 가로 폭을 정면 깊이로 넘겨 방이 가로로 납작하지 않고 깊게 느껴지게 한다.
                    changed = true;
                }
                else if (itemName == "ENV_FloorBase")
                {
                    position.z = -1.95f; // 바닥 받침도 판재와 같은 새 깊이 중심으로 맞춘다.
                    scale = new Vector3(1.46f, scale.y, 1.72f); // 바닥 받침은 줄어든 폭 1.46배와 늘어난 깊이 1.72배를 사용해 새 방 비율을 그대로 받친다.
                    changed = true;
                }
                else if (itemName == "ENV_LeftWall")
                {
                    position = new Vector3(4.38f, position.y, -1.95f); // 왼쪽 벽은 줄어든 새 가로 끝으로 안쪽 이동시키고 길어진 방의 깊이 중심에 맞춘다.
                    scale.z = 1.72f; // 옆벽도 새 정면 벽까지 끊김 없이 이어 빨간 표시처럼 벽/지붕 사이 빈 띠가 생기지 않게 한다.
                    changed = true;
                }
                else if (itemName == "ENV_RightWall")
                {
                    position = new Vector3(-4.38f, position.y, -1.95f); // 오른쪽 벽도 반대편과 대칭으로 안쪽 이동해 플레이어가 새 가로 폭의 중앙에 놓이게 한다.
                    scale.z = 1.72f; // 오른쪽 옆벽도 새 깊이 전체를 막아 빈 띠가 남지 않게 한다.
                    changed = true;
                }
                else if (itemName == "ENV_BackWall")
                {
                    position.z = -6.25f; // 가로에서 줄인 공간을 정면으로 넘겨 정면 벽을 z=-6.25까지 밀고 실제 앞뒤 깊이를 늘린다.
                    scale.x = 1.46f; // 정면 벽은 줄어든 새 가로 폭과 정확히 맞춘다.
                    changed = true;
                }
                else if (itemName == "ENV_VerticalBeam_01")
                {
                    position = new Vector3(4.12f, position.y, -6.11f); // 정면 오른쪽 기둥을 새 정면 벽 모서리까지 이동한다.
                    changed = true;
                }
                else if (itemName == "ENV_VerticalBeam_02")
                {
                    position = new Vector3(-4.12f, position.y, -6.11f); // 정면 왼쪽 기둥도 새 정면 벽 모서리까지 이동한다.
                    changed = true;
                }
                else if (itemName == "ENV_VerticalBeam_03")
                {
                    position = new Vector3(4.12f, position.y, 2.36f); // 플레이어 뒤쪽 오른쪽 기둥은 원래 위치를 유지해 뒤 공간을 쓸데없이 늘리지 않는다.
                    changed = true;
                }
                else if (itemName == "ENV_VerticalBeam_04")
                {
                    position = new Vector3(-4.12f, position.y, 2.36f); // 플레이어 뒤쪽 왼쪽 기둥도 원래 위치를 유지한다.
                    changed = true;
                }
                else if (itemName == "ENV_CeilingBeam_01")
                {
                    position.z = 1.55f; // 뒤쪽 천장 가로보는 좌석 뒤 경계를 그대로 보여 준다.
                    scale.x = 1.46f; // 줄어든 새 방 폭 끝까지 가로보를 정확히 연결한다.
                    changed = true;
                }
                else if (itemName == "ENV_CeilingBeam_02")
                {
                    position.z = -1.90f; // 가운데 가로보는 길어진 방의 중간 지점으로 이동해 천장이 텅 빈 느낌을 줄인다.
                    scale.x = 1.46f; // 가운데 가로보도 새 좌우 벽 사이만 정확히 잇는다.
                    changed = true;
                }
                else if (itemName == "ENV_CeilingBeam_03")
                {
                    position.z = -5.35f; // 앞쪽 가로보는 새 정면 벽 안쪽에 배치해 깊어진 부분에도 구조물이 이어지게 한다.
                    scale.x = 1.46f; // 앞쪽 가로보도 새 가로 폭에 맞춘다.
                    changed = true;
                }
                else if (itemName == "ENV_Roof_Left")
                {
                    position = new Vector3(2.22f, position.y, -1.95f); // 왼쪽 지붕 절반은 줄어든 폭과 늘어난 깊이를 동시에 반영해 새 벽 위를 정확히 덮는다.
                    scale.x = 1.46f; // 지붕 좌우 폭도 새 외벽 폭에 맞춰 과하게 옆으로 튀어나오지 않게 한다.
                    scale.z = 1.72f; // 지붕은 늘어난 정면 깊이 끝까지 이어 빨간 표시의 빈 천장 띠를 덮는다.
                    changed = true;
                }
                else if (itemName == "ENV_Roof_Right")
                {
                    position = new Vector3(-2.22f, position.y, -1.95f); // 오른쪽 지붕 절반도 반대편과 대칭으로 맞춘다.
                    scale.x = 1.46f; // 오른쪽 지붕도 같은 새 폭을 사용한다.
                    scale.z = 1.72f; // 오른쪽 지붕도 정면 벽까지 같은 깊이로 이어진다.
                    changed = true;
                }
                else if (itemName == "ENV_RoofRidge")
                {
                    position.z = -1.95f; // 지붕 마룻대 중심도 새 방 깊이 중심에 맞춘다.
                    scale.z = 1.72f; // 마룻대도 새 정면 끝까지 이어 천장 구조가 중간에서 끊겨 보이지 않게 한다.
                    changed = true;
                }
                else if (itemName == "PROP_CenterRug")
                {
                    item.gameObject.SetActive(false); // 중앙 장식 러그는 좌식 카메라에서 판처럼 시야를 가리므로 새 씬을 만들 때부터 사용하지 않는다.
                    continue; // 숨긴 오브젝트의 위치나 스케일은 더 이상 수정할 필요가 없으므로 다음 파츠로 넘어간다.
                }
                else if (itemName == "PROP_FireplaceRoot")
                {
                    position = new Vector3(-2.80f, 0f, -6.05f); // 벽난로를 새 정면 벽 왼쪽으로 이동해 확장된 앞 공간의 시각적 기준점을 만든다.
                    changed = true;
                }
                else if (itemName == "PROP_WindowRoot")
                {
                    position = new Vector3(0f, 2.08f, -6.20f); // 창문은 새 정면 벽 중앙으로 이동한다.
                    changed = true;
                }
                else if (itemName == "INT_MapBoard")
                {
                    position = new Vector3(0.00f, 1.55f, -2.05f); // 지도판은 멀어진 벽 대신 좌석 가까운 독립 스탠드 위치를 사용한다.
                    changed = true;
                }

                if (!changed)
                    continue; // 대상이 아닌 세부 소품은 원래 로컬 배치를 그대로 유지한다.
                item.localPosition = position; // 계산한 최종 위치를 적용한다.
                item.localScale = scale; // 계산한 최종 스케일을 적용한다.
            }
        }

        private static void RebuildProceduralCabinShell(Transform cabinRoot)
        {
            if (cabinRoot == null)
                return; // 오두막 루트가 없는 임시 씬에서는 구조를 만들지 않는다.

            // 기존 FBX의 완성형 건물 골격은 원래 작은 산장 치수에 맞춰진 메시다.
            // 이 조각들을 Transform으로 늘리면 벽/지붕/보의 로컬 축과 경사가 서로 달라 접합부가 벌어지므로,
            // 구조 파츠만 숨기고 원래 재질과 소품을 재사용한 새 골격을 절대 치수로 만든다.
            foreach (Transform child in cabinRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child == cabinRoot)
                    continue;
                if (child.name.StartsWith("ENV_", System.StringComparison.Ordinal))
                    child.gameObject.SetActive(false); // 원본 바닥/벽/기둥/보/지붕은 렌더러와 콜라이더를 함께 끈다.
            }

            Transform oldShell = FindChild(cabinRoot, "Mush Procedural Cabin Shell");
            if (oldShell != null)
                Object.DestroyImmediate(oldShell.gameObject); // 이전 Revision에서 만든 껍데기가 있으면 중복 생성되지 않게 먼저 제거한다.

            Transform oldGable = FindChild(cabinRoot, "Back Gable Fill - Scene Fix");
            if (oldGable != null)
                Object.DestroyImmediate(oldGable.gameObject); // 기존 FBX 치수에 맞춘 임시 박공도 새 골격과 겹치므로 제거한다.

            Material wallMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + "/CabinWall.mat");
            Material darkWoodMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + "/CabinDarkWood.mat");
            Material floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + "/CabinFloor.mat");
            if (wallMaterial == null || darkWoodMaterial == null || floorMaterial == null)
                return; // 기존 산장 재질이 없으면 회색 기본 큐브를 남기지 않고 안전하게 중단한다.

            GameObject shell = new("Mush Procedural Cabin Shell");
            shell.transform.SetParent(cabinRoot, false); // 창문/벽난로/지도 같은 기존 소품과 같은 오두막 로컬 좌표계를 사용한다.

            float halfWidth = ProceduralCabinWidth * 0.5f;
            float halfDepth = ProceduralCabinDepth * 0.5f;
            float frontZ = ProceduralCabinCenterZ - halfDepth; // -6.25 : 플레이어 정면의 먼 벽이다.
            float backZ = ProceduralCabinCenterZ + halfDepth;  // +2.75 : 플레이어 바로 뒤쪽의 닫힌 벽이다.
            const float floorThickness = 0.16f;
            const float wallThickness = 0.16f;
            const float beamThickness = 0.18f;
            const float roofThickness = 0.14f;
            const float roofOverhang = 0.28f;

            CreatePrimitive("Cabin Floor Base", PrimitiveType.Cube, shell.transform,
                new Vector3(0f, -floorThickness * 0.75f, ProceduralCabinCenterZ),
                new Vector3(ProceduralCabinWidth, floorThickness, ProceduralCabinDepth),
                Vector3.zero, darkWoodMaterial, true); // 바닥 아래 받침은 하나의 연속 면으로 만들어 판재 사이로 바깥이 보이지 않게 한다.

            const int plankCount = 12;
            float plankGap = 0.018f;
            float plankWidth = (ProceduralCabinWidth - plankGap * (plankCount - 1)) / plankCount;
            for (int index = 0; index < plankCount; index++)
            {
                float x = -halfWidth + plankWidth * 0.5f + index * (plankWidth + plankGap);
                CreatePrimitive("Cabin Floor Plank " + (index + 1).ToString("00"), PrimitiveType.Cube, shell.transform,
                    new Vector3(x, -0.02f, ProceduralCabinCenterZ),
                    new Vector3(plankWidth, 0.08f, ProceduralCabinDepth),
                    Vector3.zero, floorMaterial, true); // 기존 마루 느낌은 유지하되 새 방 전체를 정확히 덮는 12장의 판재로 다시 만든다.
            }

            CreatePrimitive("Cabin Left Wall", PrimitiveType.Cube, shell.transform,
                new Vector3(-halfWidth, ProceduralCabinWallHeight * 0.5f, ProceduralCabinCenterZ),
                new Vector3(wallThickness, ProceduralCabinWallHeight, ProceduralCabinDepth),
                Vector3.zero, wallMaterial, true); // 왼쪽 벽은 바닥 깊이와 완전히 같은 길이로 이어져 중간 빈 띠가 생기지 않는다.
            CreatePrimitive("Cabin Right Wall", PrimitiveType.Cube, shell.transform,
                new Vector3(halfWidth, ProceduralCabinWallHeight * 0.5f, ProceduralCabinCenterZ),
                new Vector3(wallThickness, ProceduralCabinWallHeight, ProceduralCabinDepth),
                Vector3.zero, wallMaterial, true); // 오른쪽 벽도 동일한 절대 치수로 만든다.
            CreatePrimitive("Cabin Front Wall", PrimitiveType.Cube, shell.transform,
                new Vector3(0f, ProceduralCabinWallHeight * 0.5f, frontZ),
                new Vector3(ProceduralCabinWidth, ProceduralCabinWallHeight, wallThickness),
                Vector3.zero, wallMaterial, true); // 벽난로/창문 뒤의 정면 벽을 한 장으로 완전히 막아 뜯긴 건물처럼 보이지 않게 한다.
            CreatePrimitive("Cabin Back Wall", PrimitiveType.Cube, shell.transform,
                new Vector3(0f, ProceduralCabinWallHeight * 0.5f, backZ),
                new Vector3(ProceduralCabinWidth, ProceduralCabinWallHeight, wallThickness),
                Vector3.zero, wallMaterial, true); // 좌석 뒤쪽도 닫힌 벽으로 만들어 산장 외피를 완결한다.

            Vector3[] postPositions =
            {
                new(-halfWidth + 0.10f, ProceduralCabinWallHeight * 0.5f, frontZ + 0.10f),
                new( halfWidth - 0.10f, ProceduralCabinWallHeight * 0.5f, frontZ + 0.10f),
                new(-halfWidth + 0.10f, ProceduralCabinWallHeight * 0.5f, backZ - 0.10f),
                new( halfWidth - 0.10f, ProceduralCabinWallHeight * 0.5f, backZ - 0.10f),
            };
            for (int index = 0; index < postPositions.Length; index++)
                CreatePrimitive("Cabin Corner Post " + (index + 1), PrimitiveType.Cube, shell.transform,
                    postPositions[index], new Vector3(0.22f, ProceduralCabinWallHeight, 0.22f),
                    Vector3.zero, darkWoodMaterial, false); // 네 모서리 기둥이 벽과 지붕의 접합 위치를 시각적으로 묶어 준다.

            CreatePrimitive("Cabin Left Top Beam", PrimitiveType.Cube, shell.transform,
                new Vector3(-halfWidth + 0.05f, ProceduralCabinWallHeight - 0.09f, ProceduralCabinCenterZ),
                new Vector3(beamThickness, beamThickness, ProceduralCabinDepth),
                Vector3.zero, darkWoodMaterial, false); // 옆벽 윗선을 하나의 긴 보로 연결해 지붕 아래 빈 틈처럼 보이는 경계를 없앤다.
            CreatePrimitive("Cabin Right Top Beam", PrimitiveType.Cube, shell.transform,
                new Vector3(halfWidth - 0.05f, ProceduralCabinWallHeight - 0.09f, ProceduralCabinCenterZ),
                new Vector3(beamThickness, beamThickness, ProceduralCabinDepth),
                Vector3.zero, darkWoodMaterial, false);

            float[] crossBeamZ = { backZ - 0.72f, ProceduralCabinCenterZ, frontZ + 0.72f };
            for (int index = 0; index < crossBeamZ.Length; index++)
                CreatePrimitive("Cabin Ceiling Beam " + (index + 1), PrimitiveType.Cube, shell.transform,
                    new Vector3(0f, ProceduralCabinWallHeight - 0.10f, crossBeamZ[index]),
                    new Vector3(ProceduralCabinWidth, beamThickness, beamThickness),
                    Vector3.zero, darkWoodMaterial, false); // 앞/중앙/뒤 세 가로보를 새 폭으로 정확히 맞춰 깊어진 공간도 구조적으로 이어 보이게 한다.

            float roofRun = halfWidth + roofOverhang;
            float roofRise = ProceduralCabinRidgeHeight - ProceduralCabinWallHeight;
            float roofSlopeLength = Mathf.Sqrt(roofRun * roofRun + roofRise * roofRise);
            float roofAngle = Mathf.Atan2(roofRise, roofRun) * Mathf.Rad2Deg;
            float roofCenterY = ProceduralCabinWallHeight + roofRise * 0.5f;
            float roofCenterX = roofRun * 0.5f;
            float roofDepth = ProceduralCabinDepth + roofOverhang * 2f;

            CreatePrimitive("Cabin Roof Left", PrimitiveType.Cube, shell.transform,
                new Vector3(-roofCenterX, roofCenterY, ProceduralCabinCenterZ),
                new Vector3(roofSlopeLength, roofThickness, roofDepth),
                new Vector3(0f, 0f, roofAngle), darkWoodMaterial, false); // 왼쪽 경사 지붕은 벽 폭과 ridge 높이에서 직접 계산해 벽과 항상 맞물리게 한다.
            CreatePrimitive("Cabin Roof Right", PrimitiveType.Cube, shell.transform,
                new Vector3(roofCenterX, roofCenterY, ProceduralCabinCenterZ),
                new Vector3(roofSlopeLength, roofThickness, roofDepth),
                new Vector3(0f, 0f, -roofAngle), darkWoodMaterial, false); // 오른쪽 지붕도 같은 계산식의 대칭값을 사용해 두 장 사이가 벌어지지 않는다.
            CreatePrimitive("Cabin Roof Ridge", PrimitiveType.Cube, shell.transform,
                new Vector3(0f, ProceduralCabinRidgeHeight, ProceduralCabinCenterZ),
                new Vector3(0.22f, 0.22f, roofDepth),
                Vector3.zero, darkWoodMaterial, false); // 마룻대는 두 지붕의 계산된 꼭대기를 한 줄로 덮어 중앙 틈을 가린다.

            CreateProceduralGable(shell.transform, "Cabin Front Gable", frontZ + wallThickness * 0.25f, wallMaterial, false); // 정면 벽 위 삼각형을 같은 폭/높이에서 만들어 지붕 아래가 비지 않게 한다.
            CreateProceduralGable(shell.transform, "Cabin Back Gable", backZ - wallThickness * 0.25f, wallMaterial, true); // 좌석 뒤쪽 박공도 닫아 외부에서 봐도 완성된 산장 형태를 유지한다.
        }

        private static void CreateProceduralGable(Transform parent, string name, float z, Material material, bool reverseWinding)
        {
            Mesh sourceMesh = GetProceduralGableMesh(); // 씬 저장 후에도 참조가 유지되도록 프로젝트 에셋으로 저장된 공용 박공 메시를 사용한다.
            if (sourceMesh == null)
                return;

            GameObject gable = new(name);
            gable.transform.SetParent(parent, false);
            gable.transform.localPosition = new Vector3(0f, ProceduralCabinWallHeight, z);
            gable.transform.localRotation = reverseWinding ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity; // 같은 양면 두께 메시를 뒤쪽에서도 같은 방향으로 재사용한다.
            MeshFilter filter = gable.AddComponent<MeshFilter>();
            filter.sharedMesh = sourceMesh;
            MeshRenderer renderer = gable.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
        }

        private static Mesh GetProceduralGableMesh()
        {
            const string path = MaterialRoot + "/ProceduralCabinGable.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Mush Procedural Cabin Gable" };
                AssetDatabase.CreateAsset(mesh, path); // 에디터에서 만든 Mesh를 씬 임시 객체로 두지 않고 Assets에 저장해 씬 재로드 후에도 사라지지 않게 한다.
            }

            float halfWidth = ProceduralCabinWidth * 0.5f;
            float peakHeight = ProceduralCabinRidgeHeight - ProceduralCabinWallHeight;
            const float halfThickness = 0.05f;
            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(-halfWidth, 0f, -halfThickness), new Vector3(0f, peakHeight, -halfThickness), new Vector3(halfWidth, 0f, -halfThickness),
                new Vector3(-halfWidth, 0f,  halfThickness), new Vector3(0f, peakHeight,  halfThickness), new Vector3(halfWidth, 0f,  halfThickness),
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 3, 5, 4,
                0, 3, 4, 0, 4, 1,
                1, 4, 5, 1, 5, 2,
                2, 5, 3, 2, 3, 0,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh); // Revision에서 치수가 바뀌어도 저장된 박공 메시가 새 절대 치수로 갱신되게 한다.
            return mesh;
        }

        private static void DisableUnusedGazeFeatures(GameObject rig)
        {
            if (rig == null)
                return;

            foreach (MonoBehaviour behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;
                if (behaviour.GetType().Name == "GazeInputManager")
                {
                    behaviour.enabled = false; // 인스펙터에서도 눈 추적 관리자가 사용되지 않는 상태임을 명확히 남긴다.
                    behaviour.gameObject.SetActive(false); // Awake는 비활성 컴포넌트에도 호출될 수 있으므로 GameObject 자체를 꺼 장치 없음 경고가 플레이 시작 때 발생하지 않게 한다.
                    EditorUtility.SetDirty(behaviour);
                }
            }

            foreach (Transform child in rig.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Gaze Interactor")
                    child.gameObject.SetActive(false); // 눈 추적 인터랙터 자체도 꺼서 컨트롤러 레이와 불필요하게 공존하지 않게 한다.
            }
        }

        private static void SetChildTransform(Transform root, string childName, Vector3 localPosition, Vector3 localEuler)
        {
            Transform child = FindChild(root, childName); // 새 씬 생성 중 이미 존재하는 FBX 자식 파츠를 이름으로 찾는다.
            if (child == null)
                return; // 해당 파츠가 없는 변형 FBX에서도 생성 과정이 중단되지 않게 한다.
            child.localPosition = localPosition; // 지정한 오두막 로컬 좌표로 실물을 옮긴다.
            child.localRotation = Quaternion.Euler(localEuler); // 필요한 경우 지정한 로컬 회전도 함께 적용한다.
        }

        private static GameObject EnsureNearMapStand(Transform parent, Material material)
        {
            if (parent == null || material == null)
                return null; // 씬 루트나 목재 재질이 없으면 불완전한 스탠드를 만들지 않는다.
            Transform existing = FindChild(parent, "Near Map Stand"); // 이전 패치에서 만든 지도 스탠드가 남아 있으면 새 방 중심 배치와 충돌할 수 있으므로 먼저 찾는다.
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject); // 절대 좌표로 만들어진 구형 스탠드를 제거하고 현재 배치 기준으로 다시 만들어 옆 사각지대를 비운다.

            GameObject stand = new("Near Map Stand"); // 벽에서 떼어낸 지도판을 받치는 프로토타입 목재 스탠드 루트를 만든다.
            stand.transform.SetParent(parent, false); // 로비 씬 루트 아래에 두어 오두막 FBX와 독립적으로 관리한다.
            CreatePrimitive("Map Stand Left Post", PrimitiveType.Cube, stand.transform,
                new Vector3(-0.54f, 0.72f, -2.13f), new Vector3(0.09f, 1.44f, 0.09f), Vector3.zero, material, false); // 지도판 왼쪽을 바닥에서 받치는 세로 기둥이다.
            CreatePrimitive("Map Stand Right Post", PrimitiveType.Cube, stand.transform,
                new Vector3(0.54f, 0.72f, -2.13f), new Vector3(0.09f, 1.44f, 0.09f), Vector3.zero, material, false); // 지도판 오른쪽을 받치는 세로 기둥이다.
            CreatePrimitive("Map Stand Bottom Brace", PrimitiveType.Cube, stand.transform,
                new Vector3(0.00f, 0.08f, -2.13f), new Vector3(1.38f, 0.12f, 0.36f), Vector3.zero, material, false); // 두 기둥이 공중에 떠 보이지 않도록 바닥 받침을 연결한다.
            return stand; // 호출 측에서 이 스탠드를 개 회피 장애물로도 등록할 수 있도록 생성한 루트를 반환한다.
        }

        private static void EnsureDogNavMeshRuntime(GameObject sceneRoot)
        {
            if (sceneRoot == null)
                return; // 로비 씬 루트가 없는 임시 편집 상태에서는 내비메시 런타임을 만들지 않는다.

            MushLobbyNavMeshRuntime navigation = sceneRoot.GetComponent<MushLobbyNavMeshRuntime>(); // 이미 이번 씬에 개 전용 런타임 내비메시가 붙어 있는지 확인한다.
            if (navigation == null)
                navigation = sceneRoot.AddComponent<MushLobbyNavMeshRuntime>(); // 없으면 UnityEngine.AI NavMeshBuilder를 사용하는 로비 전용 컴포넌트를 추가한다.

            navigation.Configure(
                new Vector3(0f, -0.06f, ProceduralCabinCenterZ), // 내비메시 박스의 윗면이 실제 마루 높이 Y=0에 오도록 절반 두께만 아래로 둔다.
                new Vector3(ProceduralCabinWidth - 0.70f, 0.12f, ProceduralCabinDepth - 0.65f)); // 벽 안쪽에서 개 반지름만큼 여유를 남긴 실내 전용 보행 범위를 사용한다.
            EditorUtility.SetDirty(navigation); // 설정값이 현재 MushLobby 씬에 저장되게 변경 상태를 표시한다.
        }

        private static void EnsureFurnitureObstacle(GameObject target)
        {
            if (target == null)
                return; // 대상 실물이 없는 씬에서는 장애물 컴포넌트를 추가하지 않는다.
            MushLobbyFurnitureObstacle obstacle = target.GetComponent<MushLobbyFurnitureObstacle>(); // 이미 회피 장애물로 등록되어 있는지 먼저 확인한다.
            if (obstacle == null)
                obstacle = target.AddComponent<MushLobbyFurnitureObstacle>(); // 상점/집 꾸미기처럼 고정된 큰 소품도 개가 파고들지 않도록 회피 대상으로 등록한다.
            obstacle.RefreshBounds(); // 이동이 끝난 최종 Renderer Bounds를 즉시 다시 계산해 정확한 금지 반경을 사용한다.
        }

        private static void AddEnvironmentColliders(Transform model)
        {
            string[] collidablePrefixes = { "ENV_FloorBase", "ENV_LeftWall", "ENV_RightWall", "ENV_BackWall" };
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
            {
                if (!collidablePrefixes.Any(prefix => child.name.StartsWith(prefix)))
                    continue;
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (filter == null || child.GetComponent<Collider>() != null)
                    continue;
                MeshCollider collider = child.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }
        }

        private static void CreateGableFill(Transform parent, Material material)
        {
            Mesh mesh = GetGableMesh();
            GameObject gable = new GameObject("Back Gable Fill - Scene Fix");
            gable.transform.SetParent(parent, false);
            gable.transform.localPosition = new Vector3(0f, 0f, -3.75f); // 박공 메시 원본의 z≈-2.5 오프셋을 고려해 -3.75를 더해 새 정면 벽 z≈-6.25에 맞춘다.
            gable.transform.localScale = new Vector3(1.46f, 1f, 1f); // 줄어든 정면 벽의 1.46배 폭 전체를 삼각 박공으로 막아 위쪽 틈이 남지 않게 한다.
            MeshFilter filter = gable.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = gable.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
        }

        private static Mesh GetGableMesh()
        {
            const string path = MaterialRoot + "/BackGableV2.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
                return existing;

            Mesh mesh = new Mesh { name = "Mush Back Gable" };
            mesh.vertices = new[]
            {
                new Vector3(-2.96f, 2.62f, -2.47f),
                new Vector3(0f, 4.34f, -2.47f),
                new Vector3(2.96f, 2.62f, -2.47f),
                new Vector3(-2.96f, 2.62f, -2.57f),
                new Vector3(0f, 4.34f, -2.57f),
                new Vector3(2.96f, 2.62f, -2.57f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1,
                3, 4, 5,
                0, 3, 4, 0, 4, 1,
                1, 4, 5, 1, 5, 2,
                2, 5, 3, 2, 3, 0
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static GameObject BuildXrRig(Transform parent, out Camera camera)
        {
            camera = null;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrRigPath);
            GameObject rig;

            if (prefab != null)
            {
                rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }
            else
            {
                rig = new GameObject("XR Player Fallback");
                rig.transform.SetParent(parent, false);
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.transform.SetParent(rig.transform, false);
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                cameraObject.tag = "MainCamera";
            }

            rig.name = "Seated XR Player - No Locomotion";
            rig.transform.localPosition = new Vector3(0f, 0f, 2.00f); // 좌석을 +Z 벽 가까이에 두어 뒤쪽 공간을 플레이 영역으로 쓰지 않는다.
            rig.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            rig.AddComponent<MushSeatedRigLock>();

            Transform locomotion = FindChild(rig.transform, "Locomotion");
            if (locomotion != null)
                locomotion.gameObject.SetActive(false);

            DisableUnusedGazeFeatures(rig); // 새로 만드는 로비에서도 눈 추적용 Gaze Interactor를 처음부터 꺼 컨트롤러 레이만 남긴다.

            Transform leftController = FindChild(rig.transform, "Left Controller");
            Transform rightController = FindChild(rig.transform, "Right Controller");
            if (leftController != null)
            {
                HideControllerMeshes(leftController);
                AddHand(leftController, LeftHandPath, "Lobby Left Hand");
            }
            if (rightController != null)
            {
                HideControllerMeshes(rightController);
                AddHand(rightController, RightHandPath, "Lobby Right Hand");
            }

            camera = rig.GetComponentInChildren<Camera>(true);
            if (camera != null)
            {
                // The prefab's Camera Offset already supplies seated eye height.
                camera.transform.localPosition = Vector3.zero;
                camera.transform.localRotation = Quaternion.identity;
                camera.nearClipPlane = 0.04f;
                camera.farClipPlane = 60f;
                camera.fieldOfView = 84f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.11f, 0.15f);
                camera.gameObject.tag = "MainCamera";
            }

            MushDesktopSeatedLook desktopLook = rig.AddComponent<MushDesktopSeatedLook>();
            desktopLook.Configure(camera != null ? camera.transform : null);

            return rig;
        }

        private static void EnsureInteractionManager(Transform parent, GameObject rig)
        {
            if (rig != null && rig.GetComponentInChildren<XRInteractionManager>(true) != null)
                return;

            GameObject managerObject = new GameObject("XR Interaction Manager");
            managerObject.transform.SetParent(parent, false);
            managerObject.AddComponent<XRInteractionManager>();
        }

        private static GameObject CreateHotspot(
            string name, Transform parent, Vector3 position, Vector3 size,
            MushLobbyController controller, MushLobbyAction action)
        {
            GameObject hotspot = new GameObject(name);
            hotspot.transform.SetParent(parent, false);
            hotspot.transform.localPosition = position;
            BoxCollider collider = hotspot.AddComponent<BoxCollider>();
            collider.size = size;
            hotspot.AddComponent<XRSimpleInteractable>();
            MushLobbyInteractable interactable = hotspot.AddComponent<MushLobbyInteractable>();
            interactable.Configure(controller, action);
            return hotspot;
        }

        private static GameObject CreatePanel(
            string title, MushLobbyController controller, Transform parent,
            Material backgroundMaterial, Material buttonMaterial, Material accentMaterial,
            PanelButtonSpec[] buttons, out TextMesh statusText, bool floorPlan = false)
        {
            GameObject root = new GameObject(title + " Panel");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 1.37f, -0.08f);
            root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            CreatePrimitive("Panel Back", PrimitiveType.Cube, root.transform,
                Vector3.zero, new Vector3(2.85f, 1.62f, 0.06f), Vector3.zero, backgroundMaterial, false);
            CreatePrimitive("Panel Header", PrimitiveType.Cube, root.transform,
                new Vector3(0f, 0.61f, -0.045f), new Vector3(2.55f, 0.24f, 0.06f), Vector3.zero, accentMaterial, false);
            CreateText("Title", root.transform, new Vector3(0f, 0.61f, -0.085f), 0.034f,
                TextAnchor.MiddleCenter, Color.white, title);

            statusText = CreateText("Status", root.transform, new Vector3(0f, 0.29f, -0.085f), 0.020f,
                TextAnchor.MiddleCenter, new Color(1f, 0.83f, 0.50f), "상태");

            float spacing = buttons.Length == 3 ? 0.84f : 0.98f;
            float startX = -spacing * (buttons.Length - 1) * 0.5f;
            float y = floorPlan ? -0.18f : -0.14f;
            for (int index = 0; index < buttons.Length; index++)
            {
                CreatePanelButton(
                    buttons[index].label, root.transform,
                    new Vector3(startX + index * spacing, y, -0.075f),
                    buttons.Length == 3 ? new Vector3(0.70f, 0.34f, 0.10f) : new Vector3(0.88f, 0.34f, 0.10f),
                    controller, buttons[index].action, buttonMaterial);
            }

            CreatePanelButton(
                "닫기", root.transform, new Vector3(0f, -0.61f, -0.075f),
                new Vector3(0.72f, 0.24f, 0.10f), controller, MushLobbyAction.ClosePanel, accentMaterial);

            return root;
        }

        private static void CreatePanelButton(
            string label, Transform parent, Vector3 localPosition, Vector3 scale,
            MushLobbyController controller, MushLobbyAction action, Material material)
        {
            GameObject button = CreatePrimitive(
                label + " Button", PrimitiveType.Cube, parent,
                localPosition, scale, Vector3.zero, material, true);
            XRSimpleInteractable xrInteractable = button.AddComponent<XRSimpleInteractable>();
            xrInteractable.selectMode = InteractableSelectMode.Single;
            MushLobbyInteractable interactable = button.AddComponent<MushLobbyInteractable>();
            interactable.Configure(controller, action, button.GetComponent<Renderer>());
            CreateText("Label", parent, localPosition + new Vector3(0f, 0f, -0.065f), 0.030f,
                TextAnchor.MiddleCenter, Color.white, label);
        }

        private static void BuildDogTeam(
            Transform parent, out MushLobbyDogRoamer[] dogs, out GameObject[] scarves)
        {
            Material gray = GetMaterial("LobbyDogGray", new Color(0.24f, 0.29f, 0.34f), 0.25f);
            Material brown = GetMaterial("LobbyDogBrown", new Color(0.48f, 0.25f, 0.12f), 0.24f);
            Material white = GetMaterial("LobbyDogWhite", new Color(0.88f, 0.86f, 0.80f), 0.25f);
            Material black = GetMaterial("LobbyDogBlack", new Color(0.025f, 0.025f, 0.03f), 0.16f);
            Material red = GetMaterial("LobbyDogScarf", new Color(0.72f, 0.045f, 0.035f), 0.24f);

            MushLobbyDogRoamer left = BuildLobbyDog(
                "Mochi - Gray Husky", parent, new Vector3(-0.72f, 0f, 0.35f), gray, white, black, red,
                out GameObject leftScarf);
            MushLobbyDogRoamer right = BuildLobbyDog(
                "Bori - Brown Husky", parent, new Vector3(0.63f, 0f, -0.55f), brown, white, black, red,
                out GameObject rightScarf);

            dogs = new[] { left, right };
            scarves = new[] { leftScarf, rightScarf };
        }

        private static MushLobbyDogRoamer BuildLobbyDog(
            string name, Transform parent, Vector3 position,
            Material coat, Material white, Material black, Material scarfMaterial,
            out GameObject scarf)
        {
            GameObject dog = new GameObject(name);
            dog.transform.SetParent(parent, false);
            dog.transform.localPosition = position;

            GameObject visual = new GameObject("Dog Visual");
            visual.transform.SetParent(dog.transform, false);
            visual.transform.localScale = Vector3.one * 0.58f;

            CreatePrimitive("Body", PrimitiveType.Capsule, visual.transform,
                new Vector3(0f, 0.78f, 0f), new Vector3(0.50f, 0.72f, 0.50f), new Vector3(90f, 0f, 0f), coat, false);
            CreatePrimitive("Chest", PrimitiveType.Sphere, visual.transform,
                new Vector3(0f, 0.82f, 0.45f), new Vector3(0.43f, 0.55f, 0.40f), Vector3.zero, white, false);
            CreatePrimitive("Head", PrimitiveType.Sphere, visual.transform,
                new Vector3(0f, 1.25f, 0.73f), new Vector3(0.48f, 0.49f, 0.48f), Vector3.zero, coat, false);
            CreatePrimitive("Muzzle", PrimitiveType.Sphere, visual.transform,
                new Vector3(0f, 1.15f, 1.04f), new Vector3(0.31f, 0.24f, 0.34f), Vector3.zero, white, false);
            CreatePrimitive("Nose", PrimitiveType.Sphere, visual.transform,
                new Vector3(0f, 1.17f, 1.23f), new Vector3(0.14f, 0.10f, 0.11f), Vector3.zero, black, false);
            CreatePrimitive("Left Ear", PrimitiveType.Cube, visual.transform,
                new Vector3(-0.18f, 1.57f, 0.70f), new Vector3(0.17f, 0.33f, 0.14f), new Vector3(-8f, 0f, -12f), coat, false);
            CreatePrimitive("Right Ear", PrimitiveType.Cube, visual.transform,
                new Vector3(0.18f, 1.57f, 0.70f), new Vector3(0.17f, 0.33f, 0.14f), new Vector3(-8f, 0f, 12f), coat, false);

            for (int side = -1; side <= 1; side += 2)
            {
                CreatePrimitive(side < 0 ? "Left Eye" : "Right Eye", PrimitiveType.Sphere, visual.transform,
                    new Vector3(side * 0.17f, 1.33f, 1.06f), new Vector3(0.072f, 0.072f, 0.052f), Vector3.zero, black, false);
                for (int row = -1; row <= 1; row += 2)
                {
                    float z = row * 0.36f;
                    CreatePrimitive("Leg", PrimitiveType.Cylinder, visual.transform,
                        new Vector3(side * 0.23f, 0.38f, z), new Vector3(0.105f, 0.30f, 0.105f), Vector3.zero, coat, false);
                    CreatePrimitive("Paw", PrimitiveType.Sphere, visual.transform,
                        new Vector3(side * 0.23f, 0.10f, z + 0.04f), new Vector3(0.16f, 0.10f, 0.21f), Vector3.zero, white, false);
                }
            }

            GameObject tail = CreatePrimitive("Tail", PrimitiveType.Capsule, visual.transform,
                new Vector3(0f, 1.05f, -0.74f), new Vector3(0.18f, 0.46f, 0.18f), new Vector3(-50f, 0f, 0f), coat, false);

            scarf = new GameObject("Equipped Scarf");
            scarf.transform.SetParent(visual.transform, false);
            CreatePrimitive("Scarf Collar", PrimitiveType.Cylinder, scarf.transform,
                new Vector3(0f, 1.02f, 0.48f), new Vector3(0.36f, 0.045f, 0.36f), Vector3.zero, scarfMaterial, false);
            CreatePrimitive("Scarf Tail", PrimitiveType.Cube, scarf.transform,
                new Vector3(0.22f, 0.83f, 0.43f), new Vector3(0.16f, 0.38f, 0.07f), new Vector3(0f, 0f, -18f), scarfMaterial, false);

            MushLobbyDogRoamer roamer = dog.AddComponent<MushLobbyDogRoamer>();
            roamer.Configure(visual.transform, tail.transform, new Vector2(-3.25f, -5.20f), new Vector2(3.25f, 0.85f)); // 넓어진 좌우 공간을 쓰되 플레이어 뒤쪽으로는 가지 않는다.
            return roamer;
        }

        private static GameObject[] BuildHousingFurniture(Transform parent)
        {
            GameObject chairSlot = new GameObject("Housing Slot 1 - Stool"); // 이름은 기존 씬 참조 호환을 위해 유지하지만 실제 시각물은 만들지 않는 의자 전용 슬롯 루트다.
            chairSlot.transform.SetParent(parent, false); // 로비 루트와 함께 움직이도록 같은 부모 아래에 둔다.
            chairSlot.transform.SetLocalPositionAndRotation(MushHousingLayout.Position(MushHousingLayout.ChairPlacement), MushHousingLayout.Rotation(MushHousingLayout.ChairPlacement)); // 의자 모델은 이 고정 위치에서 교체만 된다.

            GameObject tableSlot = new GameObject("Housing Slot 2 - Plant"); // 예전 화분 모형 대신 아무것도 없는 탁자 전용 슬롯 루트만 만든다.
            tableSlot.transform.SetParent(parent, false); // 다른 하우징 슬롯과 같은 부모 계층을 사용한다.
            tableSlot.transform.SetLocalPositionAndRotation(MushHousingLayout.Position(MushHousingLayout.TablePlacement), MushHousingLayout.Rotation(MushHousingLayout.TablePlacement)); // 탁자 모델은 이 위치에서만 교체된다.

            GameObject dogBedSlot = new GameObject("Housing Slot 3 - Side Table"); // 예전 사이드테이블 모형 대신 개 침대 전용 빈 슬롯 루트만 만든다.
            dogBedSlot.transform.SetParent(parent, false); // 로비 루트 아래에서 고정 위치를 유지한다.
            dogBedSlot.transform.SetLocalPositionAndRotation(MushHousingLayout.Position(MushHousingLayout.DogRestPlacement), MushHousingLayout.Rotation(MushHousingLayout.DogRestPlacement)); // 개 침대 모델이 장착될 때만 이 슬롯 아래에 실제 모델이 생긴다.

            return new[] { chairSlot, tableSlot, dogBedSlot }; // 컨트롤러에는 의자→탁자→개 침대 순서의 빈 슬롯 세 개만 전달한다.
        }

        private static void DisableLegacyHousingSlotChildren(GameObject slot)
        {
            if (slot == null)
                return; // 구형 씬에 해당 슬롯이 없으면 수정 작업을 건너뛴다.

            for (int index = 0; index < slot.transform.childCount; index++)
            {
                Transform child = slot.transform.GetChild(index); // 예전 씬에 저장돼 있는 스툴·화분·사이드테이블 프로토타입 자식을 확인한다.
                if (child == null || child.name == "Mush Housing Model")
                    continue; // 런타임에 장착되는 실제 가구 모델 루트는 비활성화하지 않는다.
                child.gameObject.SetActive(false); // 슬롯 위치 표시에 쓰던 임시 시각물을 통째로 꺼 실제 가구 아래 받침처럼 보이지 않게 한다.
            }

            EditorUtility.SetDirty(slot); // 자식 활성 상태 변경이 MushLobby.unity에 저장되도록 슬롯 오브젝트를 변경 상태로 표시한다.
        }

        private static void AddHand(Transform controller, string path, string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return;
            GameObject hand = (GameObject)PrefabUtility.InstantiatePrefab(prefab, controller);
            hand.name = name;
            hand.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            hand.transform.localScale = Vector3.one;
        }

        private static void HideControllerMeshes(Transform controller)
        {
            foreach (MeshRenderer renderer in controller.GetComponentsInChildren<MeshRenderer>(true))
                renderer.enabled = false;
            foreach (SkinnedMeshRenderer renderer in controller.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.enabled = false;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child;
            }
            return null;
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform match = FindChild(root.transform, name);
                if (match != null)
                    return match.gameObject;
            }
            return null;
        }

        private static GameObject CreatePrimitive(
            string name, PrimitiveType type, Transform parent,
            Vector3 localPosition, Vector3 localScale, Vector3 localEuler,
            Material material, bool keepCollider)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localRotation = Quaternion.Euler(localEuler);
            primitive.transform.localScale = localScale;
            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            if (!keepCollider)
            {
                Collider collider = primitive.GetComponent<Collider>();
                if (collider != null)
                    Object.DestroyImmediate(collider);
            }
            return primitive;
        }

        private static void CreateLabel(
            string text, Transform parent, Vector3 localPosition, float characterSize, Color color)
        {
            TextMesh label = CreateText(text + " Label", parent, localPosition, characterSize, TextAnchor.MiddleCenter, color, text);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        private static TextMesh CreateText(
            string name, Transform parent, Vector3 localPosition, float characterSize,
            TextAnchor anchor, Color color, string text)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = localPosition;
            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = anchor;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 64;
            textMesh.characterSize = characterSize;
            textMesh.color = color;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                textMesh.font = font;
                textObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            return textMesh;
        }

        private static Material GetMaterial(
            string name, Color color, float smoothness, Color? emission = null)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            Material material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }

        private static void PutLobbyFirstInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(scene => scene.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
