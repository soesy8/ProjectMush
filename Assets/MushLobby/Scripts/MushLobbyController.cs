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
                holder.transform.localPosition = MushHousingLayout.Position(index);
                holder.transform.localRotation = MushHousingLayout.Rotation(index);
                holder.SetActive(occupiedHousingSlots[index]);
                if (!occupiedHousingSlots[index] || catalog == null)
                    continue;

                MushCustomizationVisuals.PrepareHousingSlot(
                    holder.transform,
                    catalog.GetPrefab(placedItem),
                    MushHousingLayout.PreviewSize(index));
                MushLobbyFurnitureObstacle obstacle = holder.GetComponent<MushLobbyFurnitureObstacle>();
                if (obstacle == null)
                    obstacle = holder.AddComponent<MushLobbyFurnitureObstacle>();
                obstacle.RefreshBounds();
            }

            SetDefaultDogCareVisible(
                customization.GetHousingPlacement(MushHousingLayout.DogRestPlacement) ==
                MushCustomizationIds.HousingDefaultDogCare);

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
                0 => MushCustomizationIds.FurnitureTable,
                1 => MushCustomizationIds.FurnitureChair,
                2 => MushCustomizationIds.FurnitureDogBed,
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
