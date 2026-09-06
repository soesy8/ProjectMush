using System;
using System.Collections.Generic;
using Mush.Customization;
using Mush.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyController : MonoBehaviour
    {
        public static Font ActiveKoreanFont { get; private set; }

        [Header("View")]
        [SerializeField] private Camera lobbyCamera;
        [SerializeField] private TextMesh lobbyStatusText;
        [SerializeField] private Font koreanFont;

        [Header("Panels")]
        [SerializeField] private GameObject mapPanel;
        [SerializeField] private GameObject shopPanel;
        [SerializeField] private GameObject housingPanel;
        [SerializeField] private TextMesh mapStatusText;
        [SerializeField] private TextMesh shopStatusText;
        [SerializeField] private TextMesh housingStatusText;

        [Header("Lobby State")]
        [SerializeField] private int startingGold = 150;
        [SerializeField] private GameObject[] dogScarves;
        [SerializeField] private GameObject[] placedFurniture;
        [SerializeField] private MushLobbyDogRoamer[] dogs;

        private int gold;
        private bool scarfPurchased;
        private readonly bool[] occupiedHousingSlots = new bool[3];
        private MushCustomizationState customization;
        private string selectedMap = "기본 설원";
        private string selectedSceneName = "snow";
        private string transientMessage = "마우스 또는 컨트롤러 광선으로 원하는 항목을 선택하세요";

        private const string RightControllerSecondaryButtonBinding = "<XRController>{RightHand}/secondaryButton"; // OpenXR의 오른손 XR 컨트롤러 보조 버튼을 지정한다. Quest Touch 계열에서는 이 경로가 B 버튼에 대응한다.
        private InputAction callDogsVrAction; // 로비에서 오른손 B 버튼을 눌렀을 때 개들을 부르기 위한 New Input System 액션을 런타임에 보관한다.
        private readonly List<MeshRenderer> suppressedLobbyTextRenderers = new();
        private MushLobbyStationNavigator stationNavigator;
        private MushLobbyDogRoamer lapDog; // 벽난로 좌석에서 호출해 현재 무릎으로 올라오는 한 마리를 기억한다.
        private Vector2 previousPetPointerPosition;
        private bool petPointerReady;

        private static readonly Dictionary<string, string> KoreanLabels = new Dictionary<string, string>
        {
            ["MUSH LODGE"] = "머쉬 산장",
            ["MAPS"] = "지도",
            ["SHOP"] = "상점",
            ["HOUSE"] = "집 꾸미기",
            ["MAP BOARD"] = "맵 게시판",
            ["SNOWFIELD"] = "기본 설원",
            ["PINE FOREST"] = "나무 숲",
            ["MONEY BAG SHOP"] = "주머니 상점",
            ["HOUSE FLOOR PLAN"] = "집 꾸미기",
            ["DOG SCARF 30G"] = "개 목도리 30골드",
            ["FOREST 100G"] = "나무 숲 지도",
            ["DOG SCARF"] = "개 목도리",
            ["FOREST MAP"] = "나무 숲 지도",
            ["SLOT 1"] = "공간 1",
            ["SLOT 2"] = "공간 2",
            ["SLOT 3"] = "공간 3",
            ["CLOSE"] = "닫기",
            ["STATUS"] = "상태",
            ["MUSH MODEL SHOP"] = "머쉬 모형 상점",
            ["CLICK A MODEL TO ACQUIRE"] = "원하는 모형을 눌러 받으세요",
            ["CLICK TO GET"] = "눌러서 받기",
            ["OWNED"] = "보유 중",
            ["SMALL TABLE"] = "작은 탁자",
            ["COZY CHAIR"] = "포근한 의자",
            ["DOG BED"] = "개 침대",
            ["NATURAL SLED"] = "기본 썰매",
            ["RED SLED"] = "빨간 썰매",
            ["BLUE SLED"] = "파란 썰매",
            ["BLACK SLED"] = "검은 썰매",
            ["SANTA SLED"] = "산타 썰매",
            ["FRONT LANTERN"] = "앞 등불",
        };

        public void Configure(
            Camera newLobbyCamera,
            TextMesh newLobbyStatusText,
            GameObject newMapPanel,
            GameObject newShopPanel,
            GameObject newHousingPanel,
            TextMesh newMapStatusText,
            TextMesh newShopStatusText,
            TextMesh newHousingStatusText,
            GameObject[] newDogScarves,
            GameObject[] newPlacedFurniture,
            MushLobbyDogRoamer[] newDogs)
        {
            lobbyCamera = newLobbyCamera;
            lobbyStatusText = newLobbyStatusText;
            mapPanel = newMapPanel;
            shopPanel = newShopPanel;
            housingPanel = newHousingPanel;
            mapStatusText = newMapStatusText;
            shopStatusText = newShopStatusText;
            housingStatusText = newHousingStatusText;
            dogScarves = newDogScarves;
            placedFurniture = newPlacedFurniture;
            dogs = newDogs;
        }

        private void Awake()
        {
            gold = startingGold;
            Font themeFont = MushUiPanelSkin.ThemeFont;
            if (themeFont != null)
                koreanFont = themeFont;
            ActiveKoreanFont = koreanFont;
            LocalizeLobbyText();
            EnsureSharpCurveMapButton();
            MushUiPanelSkin.ApplyPanel(mapPanel != null ? mapPanel.transform : null, new Vector2(3.0f, 1.75f));
            MushUiPanelSkin.ApplyPanel(shopPanel != null ? shopPanel.transform : null, new Vector2(4.85f, 3.35f));
            MushUiPanelSkin.ApplyPanel(housingPanel != null ? housingPanel.transform : null, new Vector2(3.0f, 1.75f));
            SetAllPanels(false);
            SetObjectsActive(dogScarves, false);
            SetObjectsActive(placedFurniture, false);
            RefreshAllText();
        }

        private void Start()
        {
            customization = MushCustomizationSave.Load();
            ApplySavedCustomization();
            MushLobbyFireplaceVfx.Install(transform.parent); // 누워 있던 FBX 벽난로를 세우고 가벼운 불꽃 파티클과 광원 흔들림을 설치한다.
            MushLobbyFireplaceRestSpot.Install(transform.parent); // 벽난로 앞 좌우에 개별 예약 가능한 휴식 자리를 만들어 개들이 겹치지 않고 눕게 한다.
            MushLobbyFetchBall.Install(lobbyCamera, dogs, transform.parent); // 오른쪽 개 놀이 구역의 거치대와 공 물어오기 놀이를 로비에 한 번만 설치한다.
            MushLobbyFeedingStation.Install(lobbyCamera, dogs, transform.parent); // 별도 먹이주기 지점에 직접 옮기고 기울여 채우는 사료통·밥그릇과 먹기 행동을 설치한다.
            stationNavigator = MushLobbyStationNavigator.Install(lobbyCamera, this, transform.parent); // Q/왼쪽 스틱 클릭으로 여는 좌식 고정 지점 이동 메뉴다.
            MushShadowPerformance.DisableForLoadedScenes(); // 로비에서 런타임 생성한 벽난로·공·먹이주기 오브젝트까지 그림자 패스에서 제외한다.
            RefreshAllText();
        }

        private void OnEnable() // 이 로비 컨트롤러가 활성화될 때 VR 호출 입력도 함께 활성화한다.
        {
            callDogsVrAction ??= new InputAction( // 씬이나 프리팹에 별도 InputActionReference를 연결하지 않아도 동작하도록 호출 전용 액션을 한 번만 만든다.
                name: "Call Dogs (VR B)", // Input Debugger에서 어떤 액션인지 바로 알아볼 수 있도록 호출 용도를 이름에 남긴다.
                type: InputActionType.Button, // B 버튼의 눌림 상태만 필요하므로 축이 아닌 Button 타입으로 만든다.
                binding: RightControllerSecondaryButtonBinding); // 오른손 XR 컨트롤러의 secondaryButton만 바인딩해서 왼손 Y 버튼과 섞이지 않게 한다.
            callDogsVrAction.Enable(); // 활성화된 액션만 입력 이벤트를 읽을 수 있으므로 로비가 켜질 때 입력 감지를 시작한다.
        }

        private void OnDisable() // 로비 컨트롤러가 비활성화되면 호출 입력도 같이 멈춰 다른 화면에서 불필요한 입력을 받지 않게 한다.
        {
            callDogsVrAction?.Disable(); // 액션이 아직 만들어지지 않은 경우도 안전하도록 null 조건 연산자로 비활성화한다.
        }

        private void OnDestroy() // 로비 씬을 떠나면서 이 컴포넌트가 파괴될 때 런타임 생성 액션의 네이티브 자원도 정리한다.
        {
            callDogsVrAction?.Dispose(); // 직접 생성한 InputAction은 더 이상 필요 없을 때 Dispose해서 입력 시스템 자원을 명확하게 해제한다.
            callDogsVrAction = null; // 파괴 과정에서 폐기된 액션을 다시 참조하지 않도록 필드도 비운다.
        }

        private void Update()
        {
            if (lapDog != null && (stationNavigator == null || !stationNavigator.IsSeatedAtFireplace))
            {
                lapDog.LeaveLap(); // 벽난로 좌석을 벗어나면 개가 카메라를 따라 날아오지 않고 의자 옆 바닥으로 내려간다.
                lapDog = null;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame)
                stationNavigator?.ToggleMenu();

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && lobbyCamera != null)
            {
                Ray ray = lobbyCamera.ScreenPointToRay(mouse.position.ReadValue());
                if (stationNavigator != null && stationNavigator.IsMenuOpen)
                {
                    stationNavigator.TryHandleDesktopClick(ray);
                }
                else if (TryGetDogUnderPointer(ray, out MushLobbyDogInteraction pointedDog))
                {
                    pointedDog.Pet(); // PC에서는 화면 중앙이 아니라 실제 마우스 커서 아래의 개를 우선 쓰다듬는다.
                }
                else if (Physics.Raycast(ray, out RaycastHit hit, 12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                {
                    MushLobbyInteractable interactable = hit.collider.GetComponentInParent<MushLobbyInteractable>();
                    if (interactable != null)
                        interactable.Trigger();
                    else if (hit.collider.GetComponentInParent<MushLobbyShopItem>() is MushLobbyShopItem shopItem)
                        shopItem.Trigger();
                    else if (hit.collider.GetComponentInParent<MushLobbyFeedDispenser>() is MushLobbyFeedDispenser feedDispenser)
                        feedDispenser.Trigger();
                    else if (hit.collider.GetComponentInParent<MushLobbyChairSeatInteractable>() is MushLobbyChairSeatInteractable chairSeat)
                        chairSeat.Trigger();
                    else
                        hit.collider.GetComponentInParent<MushLobbyDogInteraction>()?.Pet();
                }
            }
            HandleDesktopPointerPetting(mouse);

            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (stationNavigator != null && stationNavigator.IsMenuOpen)
                        stationNavigator.CloseMenu();
                    else
                        ClosePanels();
                }
                if (keyboard.spaceKey.wasPressedThisFrame)
                    CallDogs();
                if (keyboard.enterKey.wasPressedThisFrame)
                    LoadSelectedMap();
            }

            if (callDogsVrAction != null && callDogsVrAction.WasPressedThisFrame()) // Quest 오른손 B 버튼이 이번 프레임에 새로 눌렸을 때만 한 번 호출한다.
                CallDogs(); // 기존 스페이스 호출과 완전히 같은 함수를 사용하므로 개의 이동·도착·쓰다듬기 흐름은 바꾸지 않는다.
        }

        private void HandleDesktopPointerPetting(Mouse mouse)
        {
            if (XRSettings.isDeviceActive || mouse == null || lobbyCamera == null ||
                (stationNavigator != null && stationNavigator.IsMenuOpen))
            {
                petPointerReady = false;
                return;
            }

            Vector2 pointerPosition = mouse.position.ReadValue();
            if (!petPointerReady || mouse.leftButton.wasPressedThisFrame)
            {
                previousPetPointerPosition = pointerPosition;
                petPointerReady = true;
                return;
            }

            Vector2 pointerDelta = pointerPosition - previousPetPointerPosition;
            previousPetPointerPosition = pointerPosition;
            if (!mouse.leftButton.isPressed || pointerDelta.sqrMagnitude < 2f * 2f)
                return;

            Ray pointerRay = lobbyCamera.ScreenPointToRay(pointerPosition);
            if (TryGetDogUnderPointer(pointerRay, out MushLobbyDogInteraction dog))
                dog.Pet(); // 좌클릭을 누른 채 커서를 개 위에서 움직이는 동안 실제 쓰다듬기 입력으로 처리한다.
        }

        private static bool TryGetDogUnderPointer(Ray pointerRay, out MushLobbyDogInteraction dog)
        {
            dog = null;
            float nearestDistance = float.PositiveInfinity;
            RaycastHit[] hits = Physics.RaycastAll(pointerRay, 12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            foreach (RaycastHit hit in hits)
            {
                MushLobbyDogInteraction candidate = hit.collider.GetComponentInParent<MushLobbyDogInteraction>();
                if (candidate == null || hit.distance >= nearestDistance)
                    continue;

                nearestDistance = hit.distance;
                dog = candidate;
            }
            return dog != null;
        }

        private void LoadSelectedMap()
        {
            LoadMap(selectedSceneName, selectedMap);
        }

        private void LoadMap(string sceneName, string displayName)
        {
            selectedSceneName = sceneName;
            selectedMap = displayName;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                transientMessage = $"{displayName} 씬을 찾을 수 없습니다";
                RefreshAllText();
                return;
            }

            transientMessage = displayName + " 출발 중";
            RefreshAllText();
            SceneManager.LoadScene(sceneName);
        }

        public void SetKoreanFont(Font font)
        {
            koreanFont = font;
            ActiveKoreanFont = font;
            LocalizeLobbyText();
        }

        public static string TranslateLobbyText(string text)
        {
            return !string.IsNullOrEmpty(text) && KoreanLabels.TryGetValue(text, out string translated)
                ? translated
                : text;
        }

        public void SetDogScarves(GameObject[] newDogScarves)
        {
            dogScarves = newDogScarves;
            SetObjectsActive(dogScarves, scarfPurchased);
        }

        public void SetDogs(MushLobbyDogRoamer[] newDogs)
        {
            dogs = newDogs;
        }

        public void SetShopPanel(GameObject newShopPanel, TextMesh newShopStatusText)
        {
            shopPanel = newShopPanel;
            shopStatusText = newShopStatusText;
            if (Application.isPlaying)
                MushUiPanelSkin.ApplyPanel(shopPanel != null ? shopPanel.transform : null, new Vector2(4.85f, 3.35f));
        }

        public bool AcquireShopItem(string itemId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            customization ??= MushCustomizationSave.Load();
            if (!customization.Acquire(itemId))
            {
                transientMessage = TranslateLobbyText(displayName) + "은(는) 이미 보유 중입니다";
                RefreshAllText();
                return false;
            }

            MushCustomizationSave.Save(customization);
            transientMessage = TranslateLobbyText(displayName) + "을(를) 받았습니다";
            RefreshAllText();
            return true;
        }

        public bool HasShopItem(string itemId)
        {
            customization ??= MushCustomizationSave.Load();
            return !string.IsNullOrWhiteSpace(itemId) && customization.Owns(itemId);
        }

        public void HandleAction(MushLobbyAction action)
        {
            switch (action)
            {
                case MushLobbyAction.OpenMapBoard:
                    ShowOnly(mapPanel);
                    transientMessage = "맵 목록을 열었습니다";
                    break;
                case MushLobbyAction.OpenShop:
                    OpenStoreScene();
                    return;
                case MushLobbyAction.OpenHousing:
                    OpenHousingScene();
                    return;
                case MushLobbyAction.SelectSnowfield:
                    LoadMap("snow", "기본 설원");
                    return;
                case MushLobbyAction.SelectForest:
                    LoadMap("Tree", "나무 숲");
                    return;
                case MushLobbyAction.SelectSharpCurve:
                    LoadMap("SharpCurve", "급커브맵");
                    return;
                case MushLobbyAction.BuyScarf:
                    BuyScarf();
                    break;
                case MushLobbyAction.BuyForest:
                    BuyForest();
                    break;
                case MushLobbyAction.HousingSlotA:
                    ToggleHousingSlot(0);
                    break;
                case MushLobbyAction.HousingSlotB:
                    ToggleHousingSlot(1);
                    break;
                case MushLobbyAction.HousingSlotC:
                    ToggleHousingSlot(2);
                    break;
                case MushLobbyAction.ClosePanel:
                    ClosePanels();
                    break;
            }

            RefreshAllText();
        }

        private void BuyScarf()
        {
            if (scarfPurchased)
            {
                transientMessage = "개 목도리를 이미 보유하고 있습니다";
                return;
            }

            if (!SpendGold(30))
                return;

            scarfPurchased = true;
            SetObjectsActive(dogScarves, true);
            transientMessage = "개들에게 목도리를 착용시켰습니다";
            CelebrateDogs();
        }

        private void BuyForest()
        {
            transientMessage = "나무 숲은 맵 게시판에서 바로 출발할 수 있습니다";
        }

        private void OpenStoreScene()
        {
            SetAllPanels(false);
            if (!Application.CanStreamedLevelBeLoaded("MushStore"))
            {
                transientMessage = "상점 씬을 찾을 수 없습니다";
                RefreshAllText();
                return;
            }

            SceneManager.LoadScene("MushStore");
        }

        private void OpenHousingScene()
        {
            SetAllPanels(false);
            if (!Application.CanStreamedLevelBeLoaded("MushHousing"))
            {
                transientMessage = "하우징 씬을 찾을 수 없습니다";
                RefreshAllText();
                return;
            }

            SceneManager.LoadScene("MushHousing");
        }

        private void CallDogs()
        {
            if (dogs == null || lobbyCamera == null)
                return;

            if (stationNavigator != null && stationNavigator.IsSeatedAtFireplace)
            {
                if (lapDog == null || !lapDog.IsInLapRoutine)
                {
                    float nearestDistance = float.PositiveInfinity;
                    MushLobbyDogRoamer nearestDog = null;
                    foreach (MushLobbyDogRoamer dog in dogs)
                    {
                        if (dog == null || dog.IsFetching)
                            continue;
                        float distance = (dog.transform.position - lobbyCamera.transform.position).sqrMagnitude;
                        if (distance >= nearestDistance)
                            continue;
                        nearestDistance = distance;
                        nearestDog = dog;
                    }
                    if (nearestDog != null && nearestDog.CallToLap(lobbyCamera.transform))
                        lapDog = nearestDog;
                }

                transientMessage = lapDog != null
                    ? "개가 의자 옆으로 와서 무릎에 앉습니다"
                    : "지금은 무릎으로 부를 수 있는 개가 없습니다";
                RefreshAllText();
                return; // 두 마리가 같은 무릎 위치에 겹치지 않도록 벽난로 좌석 호출은 한 마리만 처리한다.
            }

            foreach (MushLobbyDogRoamer dog in dogs)
                dog?.CallTo(lobbyCamera.transform);
            transientMessage = "개들이 이쪽으로 오고 있습니다";
            RefreshAllText();
        }

        private bool SpendGold(int amount)
        {
            if (gold < amount)
            {
                transientMessage = "골드가 부족합니다";
                return false;
            }

            gold -= amount;
            return true;
        }

        private void ToggleHousingSlot(int index)
        {
            if (index < 0 || index >= occupiedHousingSlots.Length ||
                placedFurniture == null || index >= placedFurniture.Length)
                return;

            customization ??= MushCustomizationSave.Load();
            string itemId = HousingItemId(index);
            if (!customization.Owns(itemId))
            {
                transientMessage = "상점에서 해당 하우징 물품을 먼저 획득하세요";
                return;
            }

            occupiedHousingSlots[index] = !occupiedHousingSlots[index];
            customization.SetHousingPlaced(index, occupiedHousingSlots[index]);
            MushCustomizationSave.Save(customization);
            if (placedFurniture[index] != null)
                placedFurniture[index].SetActive(occupiedHousingSlots[index]);
            transientMessage = occupiedHousingSlots[index]
                ? $"공간 {index + 1}에 가구를 놓았습니다"
                : $"공간 {index + 1}에서 가구를 치웠습니다";
        }

        private void CelebrateDogs()
        {
            if (dogs == null)
                return;
            foreach (MushLobbyDogRoamer dog in dogs)
                dog?.Celebrate();
        }

        private void ClosePanels()
        {
            SetAllPanels(false);
            transientMessage = "로비 화면";
        }

        public void ClosePanelsForTravel()
        {
            ClosePanels();
            RefreshAllText();
        }

        public void SetStationMenuVisible(bool visible)
        {
            if (visible)
            {
                SetAllPanels(false);
                SetBackgroundLobbyTextVisible(false);
            }
            else
            {
                SetBackgroundLobbyTextVisible(true);
            }
        }

        private void ShowOnly(GameObject panel)
        {
            SetAllPanels(false);
            if (panel != null)
            {
                PositionPanelForCurrentView(panel);
                panel.SetActive(true);
                SetBackgroundLobbyTextVisible(false);
            }
        }

        private void PositionPanelForCurrentView(GameObject panel)
        {
            if (panel == null || lobbyCamera == null)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(lobbyCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(lobbyCamera.transform.root.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            panel.transform.position = lobbyCamera.transform.position + forward * 1.20f - Vector3.up * 0.13f;
            panel.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void EnsureSharpCurveMapButton()
        {
            if (mapPanel == null)
                return;

            Transform snowButton = mapPanel.transform.Find("SNOWFIELD Button") ??
                                   mapPanel.transform.Find("기본 설원 Button");
            Transform forestButton = mapPanel.transform.Find("PINE FOREST Button") ??
                                     mapPanel.transform.Find("나무 숲 Button");
            Vector3 snowPosition = new(-0.84f, -0.14f, -0.075f);
            Vector3 forestPosition = new(0f, -0.14f, -0.075f);
            Vector3 sharpPosition = new(0.84f, -0.14f, -0.075f);
            ArrangeMapButton(snowButton, "기본 설원", snowPosition);
            ArrangeMapButton(forestButton, "나무 숲", forestPosition);

            Transform sharpButton = mapPanel.transform.Find("SHARP CURVE Button") ??
                                    mapPanel.transform.Find("급커브맵 Button");
            if (sharpButton == null)
            {
                GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
                button.name = "SHARP CURVE Button";
                button.transform.SetParent(mapPanel.transform, false);
                button.transform.localPosition = sharpPosition;
                button.transform.localRotation = Quaternion.identity;
                button.transform.localScale = new Vector3(0.70f, 0.34f, 0.10f);

                Renderer renderer = button.GetComponent<Renderer>();
                Renderer template = snowButton != null ? snowButton.GetComponent<Renderer>() :
                    forestButton != null ? forestButton.GetComponent<Renderer>() : null;
                if (renderer != null && template != null)
                    renderer.sharedMaterial = template.sharedMaterial;

                button.AddComponent<XRSimpleInteractable>();
                MushLobbyInteractable interactable = button.AddComponent<MushLobbyInteractable>();
                interactable.Configure(this, MushLobbyAction.SelectSharpCurve, renderer);
                sharpButton = button.transform;

                GameObject labelObject = new("Sharp Curve Label");
                labelObject.transform.SetParent(mapPanel.transform, false);
                labelObject.transform.localPosition = sharpPosition + new Vector3(0f, 0f, -0.065f);
                labelObject.transform.localRotation = Quaternion.identity;
                TextMesh label = labelObject.AddComponent<TextMesh>();
                label.text = "급커브맵";
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.characterSize = 0.027f;
                label.fontSize = 64;
                label.color = Color.white;
                if (koreanFont != null)
                {
                    label.font = koreanFont;
                    if (labelObject.TryGetComponent(out MeshRenderer textRenderer))
                        textRenderer.sharedMaterial = koreanFont.material;
                }
            }

            ArrangeMapButton(sharpButton, "급커브맵", sharpPosition);
            NormalizeMapPanelLayout();
        }

        private void ArrangeMapButton(Transform button, string label, Vector3 position)
        {
            if (button == null)
                return;

            button.localPosition = position;
            button.localRotation = Quaternion.identity;
            button.localScale = new Vector3(0.70f, 0.34f, 0.10f);

            TextMesh buttonLabel = button.GetComponentInChildren<TextMesh>(true);
            if (buttonLabel == null)
            {
                foreach (TextMesh candidate in mapPanel.GetComponentsInChildren<TextMesh>(true))
                {
                    if (candidate.text != label)
                        continue;
                    buttonLabel = candidate;
                    break;
                }
            }

            if (buttonLabel == null)
                return;

            // Old map labels were children of non-uniformly scaled cubes,
            // while the runtime sharp-curve label was a direct panel child.
            // Make every label a sibling of its button so one size means the
            // same visible size for all three maps.
            buttonLabel.transform.SetParent(mapPanel.transform, false);
            buttonLabel.transform.localPosition = position + new Vector3(0f, 0f, -0.065f);
            buttonLabel.transform.localRotation = Quaternion.identity;
            buttonLabel.transform.localScale = Vector3.one;
            ConfigurePanelText(buttonLabel, 0.016f, 0.90f);
        }

        private void NormalizeMapPanelLayout()
        {
            if (mapPanel == null)
                return;

            TextMesh title = null;
            Transform titleTransform = mapPanel.transform.Find("Title");
            if (titleTransform != null)
                title = titleTransform.GetComponent<TextMesh>();
            if (title != null)
            {
                title.transform.localPosition = new Vector3(0f, 0.61f, -0.085f);
                title.transform.localRotation = Quaternion.identity;
                title.transform.localScale = Vector3.one;
                ConfigurePanelText(title, 0.023f, 0.90f);
            }

            if (mapStatusText != null)
            {
                mapStatusText.transform.localPosition = new Vector3(0f, 0.26f, -0.085f);
                mapStatusText.transform.localRotation = Quaternion.identity;
                mapStatusText.transform.localScale = Vector3.one;
                ConfigurePanelText(mapStatusText, 0.012f, 0.78f);
            }

            Transform closeButton = mapPanel.transform.Find("CLOSE Button") ??
                                    mapPanel.transform.Find("닫기 Button");
            if (closeButton == null)
                return;

            closeButton.localPosition = new Vector3(0f, -0.61f, -0.075f);
            closeButton.localRotation = Quaternion.identity;
            closeButton.localScale = new Vector3(0.72f, 0.24f, 0.10f);
            TextMesh closeLabel = closeButton.GetComponentInChildren<TextMesh>(true);
            if (closeLabel == null)
            {
                foreach (TextMesh candidate in mapPanel.GetComponentsInChildren<TextMesh>(true))
                {
                    if (candidate.text == "닫기")
                    {
                        closeLabel = candidate;
                        break;
                    }
                }
            }

            if (closeLabel == null)
                return;
            closeLabel.transform.SetParent(mapPanel.transform, false);
            closeLabel.transform.localPosition = closeButton.localPosition + new Vector3(0f, 0f, -0.065f);
            closeLabel.transform.localRotation = Quaternion.identity;
            closeLabel.transform.localScale = Vector3.one;
            ConfigurePanelText(closeLabel, 0.015f, 0.90f);
        }

        private static void ConfigurePanelText(TextMesh text, float characterSize, float lineSpacing)
        {
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 64;
            text.characterSize = characterSize;
            text.lineSpacing = lineSpacing;
        }

        private void SetBackgroundLobbyTextVisible(bool visible)
        {
            if (visible)
            {
                foreach (MeshRenderer renderer in suppressedLobbyTextRenderers)
                {
                    if (renderer != null)
                        renderer.enabled = true;
                }
                suppressedLobbyTextRenderers.Clear();
                return;
            }

            if (suppressedLobbyTextRenderers.Count > 0)
                return;

            foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
            {
                foreach (TextMesh text in sceneRoot.GetComponentsInChildren<TextMesh>(true))
                {
                    if (text == null || IsPanelText(text.transform) || !text.gameObject.activeInHierarchy)
                        continue;
                    if (!text.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled)
                        continue;
                    renderer.enabled = false;
                    suppressedLobbyTextRenderers.Add(renderer);
                }
            }
        }

        private bool IsPanelText(Transform candidate)
        {
            return candidate != null &&
                   ((mapPanel != null && candidate.IsChildOf(mapPanel.transform)) ||
                    (shopPanel != null && candidate.IsChildOf(shopPanel.transform)) ||
                    (housingPanel != null && candidate.IsChildOf(housingPanel.transform)));
        }

        private void SetAllPanels(bool active)
        {
            if (mapPanel != null) mapPanel.SetActive(active);
            if (shopPanel != null) shopPanel.SetActive(active);
            if (housingPanel != null) housingPanel.SetActive(active);
            if (!active)
                SetBackgroundLobbyTextVisible(true);
        }

        private void RefreshAllText()
        {
            if (lobbyStatusText != null)
                lobbyStatusText.gameObject.SetActive(false);

            if (mapStatusText != null)
                mapStatusText.text = $"선택: {selectedMap}\n버튼을 누르면 바로 출발합니다";

            if (shopStatusText != null)
                shopStatusText.text = "별도 상점 화면에서 물품을 획득하고 장착할 수 있습니다";

            if (housingStatusText != null)
                housingStatusText.text = "집 꾸미기 상자를 누르면 별도 하우징 화면으로 이동합니다";

        }

        private string SlotState(int index)
        {
            if (customization != null && !customization.Owns(HousingItemId(index)))
                return "미보유";
            return occupiedHousingSlots[index] ? "사용" : "비어 있음";
        }

        private void ApplySavedCustomization()
        {
            if (customization == null)
                return;

            MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
            string[] housingItems =
            {
                MushCustomizationIds.FurnitureChair,
                MushCustomizationIds.FurnitureTable,
                MushCustomizationIds.FurnitureDogBed,
            };

            for (int index = 0; index < occupiedHousingSlots.Length; index++)
            {
                string placedItem = customization.GetHousingPlacement(index);
                occupiedHousingSlots[index] = placedItem == housingItems[index] && customization.Owns(placedItem);
                if (placedFurniture == null || index >= placedFurniture.Length || placedFurniture[index] == null)
                    continue;

                GameObject holder = placedFurniture[index];
                holder.name = index switch
                {
                    MushHousingLayout.ChairPlacement => "Placed Housing Chair",
                    MushHousingLayout.TablePlacement => "Placed Housing Table",
                    _ => "Placed Housing Dog Bed",
                };
                holder.transform.localPosition = MushHousingLayout.Position(index); // 하우징 종류가 바뀌어도 슬롯 자체의 위치는 항상 같은 고정 좌표를 사용한다.
                holder.transform.localRotation = MushHousingLayout.Rotation(index); // 슬롯별 고정 회전도 모델 교체와 무관하게 유지한다.
                holder.SetActive(occupiedHousingSlots[index]); // 현재 저장 상태에서 실제로 장착된 가구 슬롯만 로비에 보이게 한다.

                MushLobbyDogBedSpot bedSpot = holder.GetComponent<MushLobbyDogBedSpot>(); // 개 침대 슬롯에는 수면 접근/예약 지점 컴포넌트가 이미 있는지 확인한다.
                bool activeDogBed = index == MushHousingLayout.DogRestPlacement && occupiedHousingSlots[index] &&
                                    placedItem == MushCustomizationIds.FurnitureDogBed; // 세 번째 슬롯에 실제 개 침대가 장착된 경우만 수면 대상으로 인정한다.
                if (activeDogBed && bedSpot == null)
                    bedSpot = holder.AddComponent<MushLobbyDogBedSpot>(); // 장착된 침대 모델을 개 AI가 찾을 수 있도록 수면 슬롯 컴포넌트를 추가한다.
                if (bedSpot != null)
                    bedSpot.enabled = activeDogBed; // 침대 제거/교체 시 즉시 수면 후보 목록에서 빠지도록 컴포넌트를 함께 끈다.

                if (!occupiedHousingSlots[index] || catalog == null)
                    continue; // 장착되지 않은 슬롯은 모델/장애물 갱신을 하지 않는다.

                MushCustomizationVisuals.PrepareHousingSlot(
                    holder.transform,
                    catalog.GetPrefab(placedItem),
                    MushHousingLayout.PreviewSize(index)); // 해당 슬롯에 선택된 실제 FBX 모델을 고정 위치에 교체 장착한다.
                MushLobbyFurnitureObstacle obstacle = holder.GetComponent<MushLobbyFurnitureObstacle>(); // 장착된 가구가 내비메시에서 실제 장애물로 등록되어 있는지 확인한다.
                if (obstacle == null)
                    obstacle = holder.AddComponent<MushLobbyFurnitureObstacle>(); // 없으면 NavMeshObstacle carving까지 관리하는 가구 장애물 컴포넌트를 추가한다.
                obstacle.RefreshBounds(); // 새로 장착된 모델의 실제 Renderer 크기로 회피/내비메시 장애물 범위를 다시 계산한다.
                if (bedSpot != null && activeDogBed)
                    bedSpot.RefreshBounds(); // 개 침대라면 모델 교체가 끝난 뒤 실제 침대 크기로 접근점과 수면 높이도 다시 계산한다.
            }

            SetDefaultDogCareVisible(true); // 기본 밥그릇은 하우징 슬롯이 아니라 로비 기본 소품이므로 개 침대 장착 여부와 상관없이 항상 보이게 한다.

            if (dogs == null)
                return;
            if (dogs.Length > 0 && dogs[0] != null)
                MushCustomizationVisuals.ApplyDogLoadout(dogs[0].VisualRoot, false, customization, 0);
            if (dogs.Length > 1 && dogs[1] != null)
                MushCustomizationVisuals.ApplyDogLoadout(dogs[1].VisualRoot, true, customization, 1);
        }

        private static string HousingItemId(int index)
        {
            return index switch
            {
                MushHousingLayout.ChairPlacement => MushCustomizationIds.FurnitureChair, // 슬롯 0은 실제 레이아웃 정의와 동일하게 의자 아이템을 가리킨다.
                MushHousingLayout.TablePlacement => MushCustomizationIds.FurnitureTable, // 슬롯 1은 탁자 아이템을 가리켜 예전 table/chair 순서 뒤바뀜을 없앤다.
                MushHousingLayout.DogRestPlacement => MushCustomizationIds.FurnitureDogBed, // 슬롯 2는 개 침대 전용이다.
                _ => string.Empty,
            };
        }

        private void SetDefaultDogCareVisible(bool visible)
        {
            Transform sceneRoot = transform.root;
            foreach (Transform candidate in sceneRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!candidate.name.StartsWith("INT_DogBowl", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (Renderer renderer in candidate.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = visible;
            }
        }

        private void LocalizeLobbyText()
        {
            foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
            {
                foreach (TextMesh textMesh in sceneRoot.GetComponentsInChildren<TextMesh>(true))
                {
                    if (textMesh == null)
                        continue;

                    textMesh.text = TranslateLobbyText(textMesh.text);
                    if (koreanFont == null)
                        continue;

                    textMesh.font = koreanFont;
                    if (textMesh.TryGetComponent(out MeshRenderer renderer))
                        renderer.sharedMaterial = koreanFont.material;
                }
            }
        }

        private static void SetObjectsActive(GameObject[] objects, bool active)
        {
            if (objects == null)
                return;
            foreach (GameObject item in objects)
            {
                if (item != null)
                    item.SetActive(active);
            }
        }
    }
}
