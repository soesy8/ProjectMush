using System;
using System.Collections.Generic;
using Mush.Customization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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
            ActiveKoreanFont = koreanFont;
            LocalizeLobbyText();
            SetAllPanels(false);
            SetObjectsActive(dogScarves, false);
            SetObjectsActive(placedFurniture, false);
            RefreshAllText();
        }

        private void Start()
        {
            customization = MushCustomizationSave.Load();
            ApplySavedCustomization();
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
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && lobbyCamera != null)
            {
                Ray ray = lobbyCamera.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit, 12f))
                {
                    MushLobbyInteractable interactable = hit.collider.GetComponentInParent<MushLobbyInteractable>();
                    if (interactable != null)
                        interactable.Trigger();
                    else if (hit.collider.GetComponentInParent<MushLobbyShopItem>() is MushLobbyShopItem shopItem)
                        shopItem.Trigger();
                    else
                        hit.collider.GetComponentInParent<MushLobbyDogInteraction>()?.Pet();
                }
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                    ClosePanels();
                if (keyboard.spaceKey.wasPressedThisFrame)
                    CallDogs();
                if (keyboard.enterKey.wasPressedThisFrame)
                    LoadSelectedMap();
            }

            if (callDogsVrAction != null && callDogsVrAction.WasPressedThisFrame()) // Quest 오른손 B 버튼이 이번 프레임에 새로 눌렸을 때만 한 번 호출한다.
                CallDogs(); // 기존 스페이스 호출과 완전히 같은 함수를 사용하므로 개의 이동·도착·쓰다듬기 흐름은 바꾸지 않는다.
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

        private void ShowOnly(GameObject panel)
        {
            SetAllPanels(false);
            if (panel != null)
                panel.SetActive(true);
        }

        private void SetAllPanels(bool active)
        {
            if (mapPanel != null) mapPanel.SetActive(active);
            if (shopPanel != null) shopPanel.SetActive(active);
            if (housingPanel != null) housingPanel.SetActive(active);
        }

        private void RefreshAllText()
        {
            if (lobbyStatusText != null)
                lobbyStatusText.text = $"머쉬 산장     {gold} 골드\n{transientMessage}";

            if (mapStatusText != null)
                mapStatusText.text = $"출발할 맵: {selectedMap}\n두 맵 모두 이용 가능\n버튼을 누르면 바로 출발합니다";

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
            foreach (TextMesh textMesh in Resources.FindObjectsOfTypeAll<TextMesh>())
            {
                if (textMesh == null || textMesh.gameObject.scene != gameObject.scene)
                    continue;

                textMesh.text = TranslateLobbyText(textMesh.text);
                if (koreanFont == null)
                    continue;

                textMesh.font = koreanFont;
                if (textMesh.TryGetComponent(out MeshRenderer renderer))
                    renderer.sharedMaterial = koreanFont.material;
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
