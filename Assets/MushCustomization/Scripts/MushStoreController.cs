using System;
using System.Collections.Generic;
using Mush.Quest;
using Mush.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Mush.Customization
{
    [DisallowMultipleComponent]
    public sealed class MushStoreController : MonoBehaviour
    {
        private enum MainPage { Store, Customize }
        private enum SledPage { Body, Decoration }
        private enum DogPage { Hat, Neck }

        private readonly List<Material> runtimeMaterials = new();
        private MushCustomizationCatalog catalog;
        private MushCustomizationState workingState;
        private Font font;
        private Camera previewCamera;
        private Canvas canvas;
        private RectTransform dynamicUi;
        private Text statusText;
        private Text previewCaption;
        private Transform previewRoot;
        private MainPage mainPage = MainPage.Store;
        private MushItemCategory storeCategory = MushItemCategory.Sled;
        private MushItemCategory customCategory = MushItemCategory.Sled;
        private SledPage sledPage = SledPage.Body;
        private DogPage dogPage = DogPage.Hat;
        private int selectedDog;
        private string storePreviewItem = MushCustomizationIds.SledNatural;
        private string transientStatus = string.Empty;
        private MushQuestTrackedInputRig questRig;
        private bool questUiConfigured;

        private static readonly Color PanelColor = new(0.055f, 0.075f, 0.105f, 0.93f);
        private static readonly Color ButtonColor = new(0.16f, 0.23f, 0.31f, 0.96f);
        private static readonly Color SelectedColor = new(0.82f, 0.40f, 0.08f, 0.98f);
        private static readonly Color AcquiredColor = new(0.16f, 0.48f, 0.30f, 0.98f);

        private void Awake()
        {
            catalog = MushCustomizationCatalog.Load();
            workingState = MushCustomizationSave.Load().Clone();
            font = catalog != null ? catalog.koreanFont : null;

            BuildPreviewStage();
            BuildInterface();
            RefreshPage();
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

        private void BuildPreviewStage()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.44f, 0.55f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.18f, 0.24f, 0.31f);
            RenderSettings.ambientGroundColor = new Color(0.07f, 0.08f, 0.10f);

            GameObject cameraObject = new("Customization Preview Camera");
            previewCamera = cameraObject.AddComponent<Camera>();
            MushVrRenderPerformance.ConfigureCamera(previewCamera);
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.085f, 0.13f, 0.19f);
            previewCamera.fieldOfView = 42f;
            previewCamera.nearClipPlane = 0.03f;
            previewCamera.farClipPlane = 80f;
            previewCamera.transform.position = new Vector3(0f, 1.65f, -11.5f);
            previewCamera.transform.rotation = Quaternion.Euler(1.5f, 0f, 0f);
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<AudioListener>();

            GameObject keyLightObject = new("Preview Key Light");
            Light keyLight = keyLightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.82f, 0.64f);
            keyLight.intensity = 1.45f;
            keyLight.shadows = LightShadows.Soft;
            keyLightObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);

            GameObject fillLightObject = new("Preview Fill Light");
            Light fillLight = fillLightObject.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.46f, 0.64f, 1f);
            fillLight.intensity = 0.72f;
            fillLight.shadows = LightShadows.None;
            fillLightObject.transform.rotation = Quaternion.Euler(22f, 145f, 0f);

            Material floorMaterial = CreateMaterial("Customization Floor", new Color(0.075f, 0.10f, 0.13f), 0.22f);
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Preview Floor";
            floor.transform.position = new Vector3(0f, -1.32f, 1.5f);
            floor.transform.localScale = new Vector3(2.2f, 1f, 1.45f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            Destroy(floor.GetComponent<Collider>());

            previewRoot = new GameObject("Current Equipment Preview").transform;
        }

        private void BuildInterface()
        {
            GameObject canvasObject = new("Store And Customization UI");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            CreatePanel(canvas.transform, "Top Bar", new Vector2(0f, 474f), new Vector2(1920f, 132f), PanelColor);
            CreatePanel(canvas.transform, "Bottom Item Area", new Vector2(0f, -370f), new Vector2(1780f, 300f), PanelColor);
            CreateText(canvas.transform, "Title", new Vector2(0f, 512f), new Vector2(720f, 48f),
                34, Color.white, "머쉬 상점 · 커스터마이징");

            CreateButton(canvas.transform, "상점", new Vector2(-500f, 454f), new Vector2(230f, 58f),
                () => { mainPage = MainPage.Store; RefreshPage(); }, ButtonColor);
            CreateButton(canvas.transform, "커스텀", new Vector2(-245f, 454f), new Vector2(230f, 58f),
                () => { mainPage = MainPage.Customize; RefreshPage(); }, ButtonColor);
            CreateButton(canvas.transform, "저장하고 로비로", new Vector2(700f, 454f), new Vector2(330f, 58f),
                SaveAndReturnToLobby, SelectedColor);

            statusText = CreateText(canvas.transform, "Status", new Vector2(0f, -510f), new Vector2(1500f, 42f),
                22, new Color(1f, 0.82f, 0.48f), string.Empty);
            previewCaption = CreateText(canvas.transform, "Preview Caption", new Vector2(0f, 265f), new Vector2(1050f, 56f),
                25, Color.white, string.Empty);

            GameObject dynamicObject = new("Dynamic Tabs And Items");
            dynamicUi = dynamicObject.AddComponent<RectTransform>();
            dynamicUi.SetParent(canvas.transform, false);
            dynamicUi.anchorMin = Vector2.one * 0.5f;
            dynamicUi.anchorMax = Vector2.one * 0.5f;
            dynamicUi.sizeDelta = new Vector2(1920f, 1080f);
        }

        private void RefreshPage()
        {
            if (dynamicUi != null)
            {
                dynamicUi.gameObject.SetActive(false);
                Destroy(dynamicUi.gameObject);
            }

            GameObject dynamicObject = new("Dynamic Tabs And Items");
            dynamicUi = dynamicObject.AddComponent<RectTransform>();
            dynamicUi.SetParent(canvas.transform, false);
            dynamicUi.anchorMin = Vector2.one * 0.5f;
            dynamicUi.anchorMax = Vector2.one * 0.5f;
            dynamicUi.sizeDelta = new Vector2(1920f, 1080f);

            if (mainPage == MainPage.Store)
                BuildStorePage();
            else
                BuildCustomizationPage();

            RefreshPreview();
        }

        private void BuildStorePage()
        {
            MushCustomizationItemDefinition previewDefinition = MushCustomizationDatabase.Find(storePreviewItem);
            if (previewDefinition == null || previewDefinition.category != storeCategory)
                storePreviewItem = FirstItemId(storeCategory);

            CreateCategoryButton("썰매", MushItemCategory.Sled, -300f);
            CreateCategoryButton("개", MushItemCategory.Dog, 0f);
            CreateCategoryButton("하우징", MushItemCategory.Housing, 300f);

            List<MushCustomizationItemDefinition> items = new();
            foreach (MushCustomizationItemDefinition item in MushCustomizationDatabase.Items)
            {
                if (item.category == storeCategory)
                    items.Add(item);
            }

            for (int index = 0; index < items.Count; index++)
            {
                MushCustomizationItemDefinition item = items[index];
                int column = index % 4;
                int row = index / 4;
                Vector2 position = new(-630f + column * 420f, -315f - row * 105f);
                bool owned = workingState.Owns(item.id);
                string label = item.displayName + (owned ? "\n보유 중" : "\n눌러서 획득");
                CreateButton(dynamicUi, label, position, new Vector2(360f, 84f),
                    () => AcquireItem(item), owned ? AcquiredColor : ButtonColor);
            }

            statusText.text = string.IsNullOrEmpty(transientStatus)
                ? $"상점 · {CategoryName(storeCategory)}     보유 물품 {workingState.ownedItems.Count}개"
                : transientStatus;
        }

        private void BuildCustomizationPage()
        {
            CreateButton(dynamicUi, "썰매", new Vector2(-165f, 382f), new Vector2(280f, 56f),
                () => { customCategory = MushItemCategory.Sled; RefreshPage(); },
                customCategory == MushItemCategory.Sled ? SelectedColor : ButtonColor);
            CreateButton(dynamicUi, "개", new Vector2(165f, 382f), new Vector2(280f, 56f),
                () => { customCategory = MushItemCategory.Dog; RefreshPage(); },
                customCategory == MushItemCategory.Dog ? SelectedColor : ButtonColor);

            if (customCategory == MushItemCategory.Sled)
                BuildSledCustomization();
            else
                BuildDogCustomization();
        }

        private void BuildSledCustomization()
        {
            CreateButton(dynamicUi, "썰매 본체", new Vector2(-165f, 316f), new Vector2(280f, 50f),
                () => { sledPage = SledPage.Body; RefreshPage(); },
                sledPage == SledPage.Body ? SelectedColor : ButtonColor);
            CreateButton(dynamicUi, "장식", new Vector2(165f, 316f), new Vector2(280f, 50f),
                () => { sledPage = SledPage.Decoration; RefreshPage(); },
                sledPage == SledPage.Decoration ? SelectedColor : ButtonColor);

            MushEquipmentSlot slot = sledPage == SledPage.Body
                ? MushEquipmentSlot.SledBody
                : MushEquipmentSlot.SledDecoration;
            BuildOwnedEquipmentGrid(slot);
            statusText.text = sledPage == SledPage.Body
                ? $"장착 썰매: {DisplayName(workingState.equippedSledBody)}"
                : $"장착 장식: {DisplayNameOrNone(workingState.equippedSledDecoration)}";
        }

        private void BuildDogCustomization()
        {
            CreateButton(dynamicUi, "첫째 개", new Vector2(-330f, 316f), new Vector2(260f, 50f),
                () => { selectedDog = 0; RefreshPage(); }, selectedDog == 0 ? SelectedColor : ButtonColor);
            CreateButton(dynamicUi, "둘째 개", new Vector2(-45f, 316f), new Vector2(260f, 50f),
                () => { selectedDog = 1; RefreshPage(); }, selectedDog == 1 ? SelectedColor : ButtonColor);
            CreateButton(dynamicUi, "모자", new Vector2(285f, 316f), new Vector2(220f, 50f),
                () => { dogPage = DogPage.Hat; RefreshPage(); }, dogPage == DogPage.Hat ? SelectedColor : ButtonColor);
            CreateButton(dynamicUi, "목 장비", new Vector2(530f, 316f), new Vector2(220f, 50f),
                () => { dogPage = DogPage.Neck; RefreshPage(); }, dogPage == DogPage.Neck ? SelectedColor : ButtonColor);

            MushEquipmentSlot slot = dogPage == DogPage.Hat
                ? MushEquipmentSlot.DogHat
                : MushEquipmentSlot.DogNeck;
            BuildOwnedEquipmentGrid(slot);
            statusText.text = $"{(selectedDog == 0 ? "첫째 개" : "둘째 개")} · " +
                              $"모자 {DisplayNameOrNone(workingState.GetDogHat(selectedDog))} · " +
                              $"목 장비 {DisplayNameOrNone(workingState.GetDogNeck(selectedDog))}";
        }

        private void BuildOwnedEquipmentGrid(MushEquipmentSlot slot)
        {
            List<MushCustomizationItemDefinition> owned = new();
            foreach (MushCustomizationItemDefinition item in MushCustomizationDatabase.Items)
            {
                if (item.slot == slot && workingState.Owns(item.id))
                    owned.Add(item);
            }

            int startIndex = 0;
            if (slot != MushEquipmentSlot.SledBody)
            {
                CreateButton(dynamicUi, "장착 해제", new Vector2(-630f, -315f), new Vector2(360f, 84f),
                    () => EquipItem(slot, string.Empty), ButtonColor);
                startIndex = 1;
            }

            for (int index = 0; index < owned.Count; index++)
            {
                MushCustomizationItemDefinition item = owned[index];
                int visualIndex = index + startIndex;
                int column = visualIndex % 4;
                int row = visualIndex / 4;
                Vector2 position = new(-630f + column * 420f, -315f - row * 105f);
                bool equipped = IsEquipped(item.id, slot);
                CreateButton(dynamicUi, item.displayName + (equipped ? "\n장착 중" : "\n장착"),
                    position, new Vector2(360f, 84f), () => EquipItem(slot, item.id),
                    equipped ? SelectedColor : ButtonColor);
            }

            if (owned.Count == 0 && slot == MushEquipmentSlot.SledBody)
                statusText.text = "상점에서 먼저 썰매를 획득하세요";
        }

        private void CreateCategoryButton(string label, MushItemCategory category, float x)
        {
            CreateButton(dynamicUi, label, new Vector2(x, 382f), new Vector2(270f, 56f),
                () => { storeCategory = category; storePreviewItem = FirstItemId(category); RefreshPage(); },
                storeCategory == category ? SelectedColor : ButtonColor);
        }

        private void AcquireItem(MushCustomizationItemDefinition item)
        {
            storePreviewItem = item.id;
            bool acquired = workingState.Acquire(item.id);
            MushCustomizationSave.Save(workingState);
            transientStatus = acquired
                ? item.displayName + "을(를) 획득했습니다"
                : item.displayName + "은(는) 이미 보유 중입니다";
            RefreshPage();
        }

        private void EquipItem(MushEquipmentSlot slot, string itemId)
        {
            if (!string.IsNullOrEmpty(itemId) && !workingState.Owns(itemId))
                return;

            switch (slot)
            {
                case MushEquipmentSlot.SledBody:
                    if (!string.IsNullOrEmpty(itemId)) workingState.equippedSledBody = itemId;
                    break;
                case MushEquipmentSlot.SledDecoration:
                    workingState.equippedSledDecoration = itemId;
                    break;
                case MushEquipmentSlot.DogHat:
                    workingState.SetDogHat(selectedDog, itemId);
                    break;
                case MushEquipmentSlot.DogNeck:
                    workingState.SetDogNeck(selectedDog, itemId);
                    break;
            }
            RefreshPage();
        }

        private bool IsEquipped(string itemId, MushEquipmentSlot slot)
        {
            return slot switch
            {
                MushEquipmentSlot.SledBody => workingState.equippedSledBody == itemId,
                MushEquipmentSlot.SledDecoration => workingState.equippedSledDecoration == itemId,
                MushEquipmentSlot.DogHat => workingState.GetDogHat(selectedDog) == itemId,
                MushEquipmentSlot.DogNeck => workingState.GetDogNeck(selectedDog) == itemId,
                _ => false,
            };
        }

        private void RefreshPreview()
        {
            if (previewRoot != null)
            {
                previewRoot.gameObject.SetActive(false);
                Destroy(previewRoot.gameObject);
            }
            previewRoot = new GameObject("Current Equipment Preview").transform;

            if (mainPage == MainPage.Store)
                BuildStorePreview();
            else if (customCategory == MushItemCategory.Sled)
                BuildSledPreview(workingState);
            else
                BuildDogPairPreview(workingState);
        }

        private void BuildStorePreview()
        {
            MushCustomizationItemDefinition item = MushCustomizationDatabase.Find(storePreviewItem);
            if (item == null)
                return;

            previewCaption.text = "상품 미리보기 · " + item.displayName;
            if (item.category == MushItemCategory.Sled)
            {
                MushCustomizationState previewState = workingState.Clone();
                if (item.slot == MushEquipmentSlot.SledBody)
                    previewState.equippedSledBody = item.id;
                else
                    previewState.equippedSledDecoration = item.id;
                BuildSledPreview(previewState);
            }
            else if (item.category == MushItemCategory.Dog)
            {
                MushCustomizationState previewState = workingState.Clone();
                if (item.slot == MushEquipmentSlot.DogHat) previewState.dogOneHat = item.id;
                else previewState.dogOneNeck = item.id;
                BuildSingleDogPreview(previewState);
            }
            else
            {
                GameObject holder = MushCustomizationVisuals.CreateFittedModel(
                    catalog != null ? catalog.GetPrefab(item.id) : null,
                    previewRoot, item.displayName + " Preview", 3.0f, new Vector3(0f, -1.15f, 0f), true);
                if (holder != null) holder.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
            }
        }

        private void BuildSledPreview(MushCustomizationState state)
        {
            previewCaption.text = $"현재 썰매 · {DisplayName(state.equippedSledBody)} / {DisplayNameOrNone(state.equippedSledDecoration)}";
            GameObject prefab = catalog != null ? catalog.GetPrefab(state.equippedSledBody) : null;
            GameObject holder = MushCustomizationVisuals.CreateFittedModel(
                prefab, previewRoot, "Selected Sled", 4.3f, new Vector3(0f, -1.12f, 0f), true);
            if (holder == null)
                return;
            holder.transform.localRotation = Quaternion.Euler(2f, 25f, 0f);
            MushCustomizationVisuals.ApplySledDecoration(holder.transform, state, 2.4f);
        }

        private void BuildSingleDogPreview(MushCustomizationState state)
        {
            previewCaption.text = "개 장비 상품 미리보기";
            GameObject holder = MushCustomizationVisuals.CreateFittedModel(
                catalog != null ? catalog.husky : null,
                previewRoot, "Husky Preview", 3.1f, new Vector3(0f, -1.15f, 0f), true);
            if (holder == null)
                return;
            holder.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            MushCustomizationVisuals.ApplyDogLoadout(holder.transform, false, state, 0);
        }

        private void BuildDogPairPreview(MushCustomizationState state)
        {
            previewCaption.text = $"개별 장착 미리보기 · {(selectedDog == 0 ? "첫째 개 선택" : "둘째 개 선택")}";
            GameObject first = MushCustomizationVisuals.CreateFittedModel(
                catalog != null ? catalog.husky : null,
                previewRoot, "First Dog Preview", 2.65f, new Vector3(-2.0f, -1.18f, 0f), true);
            GameObject second = MushCustomizationVisuals.CreateFittedModel(
                catalog != null ? catalog.malamute : null,
                previewRoot, "Second Dog Preview", 2.65f, new Vector3(2.0f, -1.18f, 0f), true);
            if (first != null)
            {
                first.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                MushCustomizationVisuals.ApplyDogLoadout(first.transform, false, state, 0);
            }
            if (second != null)
            {
                second.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                MushCustomizationVisuals.ApplyDogLoadout(second.transform, true, state, 1);
            }
            CreateSelectionPedestal(selectedDog == 0 ? -2f : 2f);
        }

        private void CreateSelectionPedestal(float x)
        {
            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "Selected Dog Marker";
            pedestal.transform.SetParent(previewRoot, false);
            pedestal.transform.localPosition = new Vector3(x, -1.26f, 0f);
            pedestal.transform.localScale = new Vector3(1.18f, 0.035f, 1.18f);
            pedestal.GetComponent<Renderer>().sharedMaterial = CreateMaterial("Selected Dog Glow", new Color(1f, 0.36f, 0.03f), 0.34f);
            Destroy(pedestal.GetComponent<Collider>());
        }

        private void SaveAndReturnToLobby()
        {
            MushCustomizationSave.Save(workingState);
            if (Application.CanStreamedLevelBeLoaded("MushLobby"))
                SceneManager.LoadScene("MushLobby");
            else
                statusText.text = "MushLobby 씬을 찾을 수 없습니다";
        }

        private string FirstItemId(MushItemCategory category)
        {
            foreach (MushCustomizationItemDefinition item in MushCustomizationDatabase.Items)
            {
                if (item.category == category)
                    return item.id;
            }
            return string.Empty;
        }

        private static string CategoryName(MushItemCategory category)
        {
            return category switch
            {
                MushItemCategory.Sled => "썰매",
                MushItemCategory.Dog => "개",
                MushItemCategory.Housing => "하우징",
                _ => string.Empty,
            };
        }

        private static string DisplayName(string itemId)
        {
            MushCustomizationItemDefinition item = MushCustomizationDatabase.Find(itemId);
            return item != null ? item.displayName : itemId;
        }

        private static string DisplayNameOrNone(string itemId)
        {
            return string.IsNullOrEmpty(itemId) ? "없음" : DisplayName(itemId);
        }

        private Material CreateMaterial(string materialName, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new(shader) { name = materialName };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            runtimeMaterials.Add(material);
            return material;
        }

        private static Image CreatePanel(Transform parent, string objectName, Vector2 position, Vector2 size, Color color)
        {
            Image themedPanel = MushUiPanelSkin.CreateCanvasPanel(parent, objectName, position, size);
            if (themedPanel != null)
                return themedPanel;

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

        private MushStoreUiButton CreateButton(
            Transform parent,
            string label,
            Vector2 position,
            Vector2 size,
            Action callback,
            Color color)
        {
            GameObject buttonObject = new(label + " Button");
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
                label.Contains("\n") ? 20 : 23, Color.white, label);
            text.raycastTarget = false;

            MushStoreUiButton button = buttonObject.AddComponent<MushStoreUiButton>();
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

}
