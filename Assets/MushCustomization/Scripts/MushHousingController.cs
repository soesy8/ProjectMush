using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Mush.Customization
{
    [DisallowMultipleComponent]
    public sealed class MushHousingController : MonoBehaviour
    {
        private readonly List<Material> runtimeMaterials = new();
        private readonly List<Renderer> defaultDogCareRenderers = new();

        private MushCustomizationCatalog catalog;
        private MushCustomizationState workingState;
        private Font font;
        private Transform previewEnvironment;
        private Transform furnitureRoot;
        private Canvas canvas;
        private RectTransform dynamicUi;
        private Text statusText;

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
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SaveAndReturnToLobby();
        }

        private void BuildLobbyPreview()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.44f, 0.35f, 0.30f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.18f, 0.16f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.065f, 0.065f);

            GameObject stage = new("Housing Lobby Preview");
            if (catalog != null && catalog.lobbyEnvironment != null)
            {
                GameObject environment = Instantiate(catalog.lobbyEnvironment, stage.transform);
                environment.name = "Lobby Environment - No Dogs Or Labels";
                environment.transform.localPosition = Vector3.zero;
                previewEnvironment = environment.transform;

                foreach (Camera importedCamera in environment.GetComponentsInChildren<Camera>(true))
                    importedCamera.enabled = false;
                foreach (AudioListener listener in environment.GetComponentsInChildren<AudioListener>(true))
                    listener.enabled = false;
                foreach (Light importedLight in environment.GetComponentsInChildren<Light>(true))
                    importedLight.enabled = false;
                foreach (Collider collider in environment.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                foreach (TextMesh text in environment.GetComponentsInChildren<TextMesh>(true))
                    text.gameObject.SetActive(false);

                CacheDefaultDogCareRenderers(environment.transform);
            }

            furnitureRoot = new GameObject("Housing Furniture Preview").transform;
            furnitureRoot.SetParent(stage.transform, false);

            GameObject cameraObject = new("Housing Preview Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.10f, 0.11f);
            camera.fieldOfView = 53f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 80f;
            camera.transform.position = new Vector3(0f, 3.05f, 4.65f);
            camera.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 1.10f, -0.45f) - camera.transform.position,
                Vector3.up);
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<AudioListener>();

            CreateDirectionalLight("Housing Warm Light", new Color(1f, 0.76f, 0.55f), 1.45f,
                Quaternion.Euler(42f, -34f, 0f), true);
            CreateDirectionalLight("Housing Fill Light", new Color(0.48f, 0.62f, 1f), 0.52f,
                Quaternion.Euler(28f, 145f, 0f), false);
        }

        private void CacheDefaultDogCareRenderers(Transform environment)
        {
            defaultDogCareRenderers.Clear();
            foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                Transform current = renderer.transform;
                bool dogCare = false;
                while (current != null && current != environment)
                {
                    if (current.name.StartsWith("INT_DogBowl", StringComparison.OrdinalIgnoreCase))
                    {
                        dogCare = true;
                        break;
                    }
                    current = current.parent;
                }
                if (dogCare)
                    defaultDogCareRenderers.Add(renderer);
            }
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
            statusText = CreateText(canvas.transform, "Placement Status", new Vector2(0f, 444f),
                new Vector2(1180f, 44f), 22, new Color(1f, 0.78f, 0.42f), string.Empty);
            CreateButton(canvas.transform, "저장하고 로비로", new Vector2(735f, 480f), new Vector2(310f, 64f),
                SaveAndReturnToLobby, SelectedColor);

            CreatePanel(canvas.transform, "Owned Housing Inventory", new Vector2(0f, -390f),
                new Vector2(1920f, 300f), InventoryColor);
            CreateText(canvas.transform, "Owned Caption", new Vector2(0f, -282f), new Vector2(900f, 42f),
                25, Color.white, "보유한 하우징 물품 · 누르면 설치하거나 제거합니다");

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
                Destroy(furnitureRoot.GetChild(index).gameObject);

            bool useDefaultDogCare = workingState.GetHousingPlacement(MushHousingLayout.DogRestPlacement) ==
                                     MushCustomizationIds.HousingDefaultDogCare;
            foreach (Renderer renderer in defaultDogCareRenderers)
            {
                if (renderer != null)
                    renderer.enabled = useDefaultDogCare;
            }

            BuildPlacementPreview(MushHousingLayout.ChairPlacement, MushCustomizationIds.FurnitureChair);
            BuildPlacementPreview(MushHousingLayout.TablePlacement, MushCustomizationIds.FurnitureTable);
            BuildPlacementPreview(MushHousingLayout.DogRestPlacement, MushCustomizationIds.FurnitureDogBed);
        }

        private void BuildPlacementPreview(int placementIndex, string expectedItem)
        {
            if (workingState.GetHousingPlacement(placementIndex) != expectedItem || catalog == null)
                return;

            GameObject prefab = catalog.GetPrefab(expectedItem);
            GameObject holder = MushCustomizationVisuals.CreateFittedModel(
                prefab,
                furnitureRoot,
                "Preview " + expectedItem,
                MushHousingLayout.PreviewSize(placementIndex),
                MushHousingLayout.Position(placementIndex),
                true);
            if (holder != null)
                holder.transform.localRotation = MushHousingLayout.Rotation(placementIndex);
        }

        private void RefreshInventory()
        {
            for (int index = dynamicUi.childCount - 1; index >= 0; index--)
                Destroy(dynamicUi.GetChild(index).gameObject);

            List<Action> actions = new();
            List<string> labels = new();
            List<Color> colors = new();

            AddOwnedCard(MushCustomizationIds.FurnitureChair, MushHousingLayout.ChairPlacement,
                "포근한 의자", actions, labels, colors);
            AddOwnedCard(MushCustomizationIds.FurnitureTable, MushHousingLayout.TablePlacement,
                "작은 탁자", actions, labels, colors);

            bool defaultSelected = workingState.GetHousingPlacement(MushHousingLayout.DogRestPlacement) ==
                                   MushCustomizationIds.HousingDefaultDogCare;
            labels.Add("기본 개 돌보기\n" + (defaultSelected ? "사용 중" : "교체 가능"));
            actions.Add(() => SetDogRest(MushCustomizationIds.HousingDefaultDogCare));
            colors.Add(defaultSelected ? SelectedColor : ButtonColor);

            if (workingState.Owns(MushCustomizationIds.FurnitureDogBed))
            {
                bool dogBedSelected = workingState.GetHousingPlacement(MushHousingLayout.DogRestPlacement) ==
                                      MushCustomizationIds.FurnitureDogBed;
                labels.Add("개 침대\n" + (dogBedSelected ? "설치 중" : "보유 중"));
                actions.Add(() => SetDogRest(MushCustomizationIds.FurnitureDogBed));
                colors.Add(dogBedSelected ? SelectedColor : ButtonColor);
            }

            bool dogRestEmpty = string.IsNullOrEmpty(
                workingState.GetHousingPlacement(MushHousingLayout.DogRestPlacement));
            labels.Add("개 공간 비우기\n" + (dogRestEmpty ? "비어 있음" : "눌러서 제거"));
            actions.Add(() => SetDogRest(string.Empty));
            colors.Add(dogRestEmpty ? SelectedColor : RemoveColor);

            float cardWidth = 250f;
            float gap = 24f;
            float totalWidth = labels.Count * cardWidth + Mathf.Max(0, labels.Count - 1) * gap;
            float startX = -totalWidth * 0.5f + cardWidth * 0.5f;
            for (int index = 0; index < labels.Count; index++)
            {
                int captured = index;
                CreateButton(dynamicUi, labels[index], new Vector2(startX + index * (cardWidth + gap), -390f),
                    new Vector2(cardWidth, 112f), () => actions[captured](), colors[index]);
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
                return;

            bool selected = workingState.GetHousingPlacement(placementIndex) == itemId;
            labels.Add(displayName + "\n" + (selected ? "설치 중" : "보유 중"));
            actions.Add(() => TogglePlacement(placementIndex, itemId));
            colors.Add(selected ? SelectedColor : ButtonColor);
        }

        private void TogglePlacement(int placementIndex, string itemId)
        {
            string current = workingState.GetHousingPlacement(placementIndex);
            workingState.SetHousingPlacement(placementIndex, current == itemId ? string.Empty : itemId);
            RefreshAll();
        }

        private void SetDogRest(string itemId)
        {
            workingState.SetHousingPlacement(MushHousingLayout.DogRestPlacement, itemId);
            RefreshAll();
        }

        private void RefreshStatus()
        {
            string chair = string.IsNullOrEmpty(workingState.GetHousingPlacement(MushHousingLayout.ChairPlacement))
                ? "없음"
                : "포근한 의자";
            string table = string.IsNullOrEmpty(workingState.GetHousingPlacement(MushHousingLayout.TablePlacement))
                ? "없음"
                : "작은 탁자";
            string rest = workingState.GetHousingPlacement(MushHousingLayout.DogRestPlacement) switch
            {
                MushCustomizationIds.HousingDefaultDogCare => "기본 개 돌보기",
                MushCustomizationIds.FurnitureDogBed => "개 침대",
                _ => "없음",
            };
            statusText.text = $"의자: {chair}     탁자: {table}     개 공간: {rest}";
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

    public sealed class MushHousingUiButton : MonoBehaviour
    {
        private RectTransform rect;
        private Image image;
        private Action callback;
        private Color normalColor;

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
            if (mouse == null || rect == null)
                return;

            bool hovered = RectTransformUtility.RectangleContainsScreenPoint(rect, mouse.position.ReadValue(), null);
            if (image != null)
                image.color = hovered ? Color.Lerp(normalColor, Color.white, 0.18f) : normalColor;
            if (hovered && mouse.leftButton.wasPressedThisFrame)
                callback?.Invoke();
        }
    }
}
