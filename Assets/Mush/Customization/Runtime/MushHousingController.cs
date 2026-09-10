using System;
using System.Collections;
using System.Collections.Generic;
using Mush.Quest;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Mush.Customization
{
    [DisallowMultipleComponent]
    public sealed class MushHousingController : MonoBehaviour
    {
        private static readonly Color Background = new(0.065f, 0.09f, 0.10f, 1f);
        private static readonly Color PanelColor = new(0.12f, 0.18f, 0.19f, 1f);
        private static readonly Color ButtonColor = new(0.22f, 0.31f, 0.32f, 1f);
        private static readonly Color Accent = new(0.80f, 0.48f, 0.19f, 1f);
        private static readonly Color Ink = new(0.94f, 0.91f, 0.82f, 1f);
        private static readonly Vector2 PlanSize = new(1120f, 740f);
        // The fixed lobby props and arrival space are shown as labelled blocks.
        private static readonly (string label, Rect area)[] FixedAreas =
        {
            ("벽난로", Rect.MinMaxRect(-3.95f, -6.07f, -1.65f, -5.30f)),
            ("집 꾸미기", Rect.MinMaxRect(-4.22f, -3.15f, -2.90f, -1.75f)),
            ("상점", Rect.MinMaxRect(2.95f, -3.10f, 4.22f, -1.80f)),
            ("먹이 공간", Rect.MinMaxRect(1.25f, -0.75f, 3.65f, 0.45f)),
            ("공 거치대", Rect.MinMaxRect(2.85f, 0.60f, 3.45f, 1.20f)),
            ("입장 공간", Rect.MinMaxRect(-0.65f, 1.40f, 0.65f, 2.57f)),
        };

        private readonly List<Material> runtimeMaterials = new();
        private readonly Dictionary<string, RenderTexture> thumbnails = new();
        private readonly GameObject[] placedModels = new GameObject[MushHousingLayout.PlacementCount];
        private MushCustomizationCatalog catalog;
        private MushCustomizationState workingState;
        private Font font;
        private Canvas canvas;
        private Camera viewCamera;
        private Camera planCamera;
        private RenderTexture planTexture;
        private Transform stage;
        private Transform furnitureRoot;
        private RectTransform planRect;
        private RectTransform inventoryRoot;
        private MushQuestTrackedInputRig questRig;
        private Text feedbackText;
        private Text selectionText;
        private Text gridText;
        private GameObject ghost;
        private GameObject footprint;
        private Material footprintMaterial;
        private int selectedIndex = -1;
        private int selectionFrame = -1;
        private XRNode activeHand = XRNode.RightHand;
        private Vector3 candidatePosition;
        private float candidateYaw;
        private bool snapToGrid = true;
        private bool returning;

        private void Awake()
        {
            catalog = MushCustomizationCatalog.Load();
            workingState = MushCustomizationSave.Load().Clone();
            font = catalog != null ? catalog.koreanFont : null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            BuildPreview();
            BuildInterface();
            RefreshFurniture();
            RefreshInventory();
            SetMessage("아래에서 가구를 선택하고 평면도의 원하는 위치를 눌러 주세요.");
        }

        private void Update()
        {
            if (questRig == null && XRSettings.isDeviceActive)
            {
                questRig = MushQuestTrackedInputRig.InstallForCamera(viewCamera);
                MushQuestTrackedInputRig.ConfigureWorldCanvas(canvas, viewCamera, 2.35f);
                questRig?.SetRayEnabled(true);
            }
        }

        private void LateUpdate()
        {
            if (returning) return;
            Keyboard keyboard = Keyboard.current;
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            bool back = questRig != null && questRig.BButtonPressedThisFrame;
            if (escape || back || (!XRSettings.isDeviceActive && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame))
            {
                if (selectedIndex >= 0) CancelSelection();
                else if (escape || back) SaveAndReturnToLobby();
                return;
            }
            if ((keyboard != null && keyboard.rKey.wasPressedThisFrame) || (questRig != null && questRig.XButtonPressedThisFrame))
                RotateSelection();
            if (keyboard != null && keyboard.deleteKey.wasPressedThisFrame) RemoveSelection();

            // UI buttons run in Update. Their click must not also drop the selected furniture.
            bool onPlan = TryGetPlanPointer(out Vector3 position, out bool pressed);
            if (selectedIndex < 0)
            {
                if (onPlan && pressed && selectionFrame != Time.frameCount) PickPlacedFurniture(position);
                return;
            }
            if (ghost == null) return;
            ghost.SetActive(onPlan);
            footprint.SetActive(onPlan);
            if (!onPlan)
            {
                SetMessage("평면도를 가리켜 주세요.\n취소하면 원래 배치를 유지합니다.");
                return;
            }
            candidatePosition = position;
            if (snapToGrid)
            {
                candidatePosition.x = Mathf.Round(position.x / MushHousingLayout.GridSize) * MushHousingLayout.GridSize;
                candidatePosition.z = Mathf.Round(position.z / MushHousingLayout.GridSize) * MushHousingLayout.GridSize;
            }
            ghost.transform.SetLocalPositionAndRotation(candidatePosition, Quaternion.Euler(0f, candidateYaw, 0f));
            Rect bounds = GetFootprint(ghost);
            bool valid = CanPlace(bounds, out string reason);
            footprint.transform.localPosition = new Vector3(bounds.center.x, 0.026f, bounds.center.y);
            footprint.transform.localScale = new Vector3(bounds.width + 0.09f, 0.012f, bounds.height + 0.09f);
            footprintMaterial.color = valid ? new Color(0.26f, 0.82f, 0.56f) : new Color(0.94f, 0.23f, 0.20f);
            SetMessage(valid ? "배치 가능\n트리거 / 클릭으로 놓기" : reason);
            if (pressed && valid && selectionFrame != Time.frameCount) CommitSelection();
        }

        private void BuildPreview()
        {
            GameObject cameraObject = new("Housing View Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            cameraObject.tag = "MainCamera";
            viewCamera = cameraObject.GetComponent<Camera>();
            viewCamera.transform.position = new Vector3(0f, 1.6f, -3f);
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = Background;
            viewCamera.nearClipPlane = 0.03f;
            viewCamera.farClipPlane = 20f;
            MushVrRenderPerformance.ConfigureCamera(viewCamera);

            stage = new GameObject("Housing Floor Plan Stage").transform;
            stage.SetParent(transform, false);
            stage.position = new Vector3(1000f, 0f, 1000f);
            furnitureRoot = new GameObject("Housing Furniture").transform;
            furnitureRoot.SetParent(stage, false);
            Material floor = CreateMaterial("Housing Wood", new Color(0.53f, 0.39f, 0.25f));
            Material wall = CreateMaterial("Housing Walls", new Color(0.87f, 0.80f, 0.65f));
            Material grid = CreateMaterial("Housing Grid", new Color(0.42f, 0.31f, 0.21f), true);
            Material fixedMaterial = CreateMaterial("Housing Fixed Props", new Color(0.30f, 0.36f, 0.36f));
            CreateCube("Floor", stage, new Vector3(0f, -0.07f, -1.75f), new Vector3(8.8f, 0.14f, 9f), floor);
            CreateCube("Left Wall", stage, new Vector3(-4.4f, 0.16f, -1.75f), new Vector3(0.16f, 0.32f, 9f), wall);
            CreateCube("Right Wall", stage, new Vector3(4.4f, 0.16f, -1.75f), new Vector3(0.16f, 0.32f, 9f), wall);
            CreateCube("Front Wall", stage, new Vector3(0f, 0.16f, -6.25f), new Vector3(8.8f, 0.32f, 0.16f), wall);
            CreateCube("Back Wall", stage, new Vector3(0f, 0.16f, 2.75f), new Vector3(8.8f, 0.32f, 0.16f), wall);
            for (float x = -4f; x <= 4f; x += 0.5f)
                CreateCube("Floor Grid X", stage, new Vector3(x, 0.006f, -1.75f), new Vector3(0.009f, 0.006f, 8.8f), grid);
            for (float z = -6f; z <= 2.5f; z += 0.5f)
                CreateCube("Floor Grid Z", stage, new Vector3(0f, 0.006f, z), new Vector3(8.6f, 0.006f, 0.009f), grid);
            foreach (var entry in FixedAreas)
                CreateCube(entry.label, stage, new Vector3(entry.area.center.x, 0.045f, entry.area.center.y),
                    new Vector3(entry.area.width, 0.08f, entry.area.height), fixedMaterial);
            footprintMaterial = CreateMaterial("Housing Placement Feedback", Color.green, true);
            footprint = CreateCube("Placement Footprint", stage, Vector3.zero, Vector3.one, footprintMaterial);
            footprint.SetActive(false);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.75f, 0.75f, 0.72f);
            GameObject lightObject = new("Housing Daylight", typeof(Light));
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            planTexture = CreateTexture("Housing Floor Plan", 1456, 962);
            planCamera = CreateTextureCamera("Housing Plan Camera", stage.position + new Vector3(0f, 16f, -1.75f),
                Quaternion.Euler(90f, 0f, 0f), planTexture);
            planCamera.orthographic = true;
            planCamera.orthographicSize = 4.95f;
        }

        private void BuildInterface()
        {
            GameObject canvasObject = new("Housing Floor Plan UI", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            CreatePanel(canvas.transform, "Housing Backdrop", Vector2.zero, new Vector2(1840f, 1030f), Background);
            CreateText(canvas.transform, "Title", new Vector2(-590f, 469f), new Vector2(540f, 58f), 38, Ink, "우리 집 꾸미기");
            CreateText(canvas.transform, "Subtitle", new Vector2(-56f, 465f), new Vector2(570f, 44f), 22, Ink, "가구를 골라 원하는 곳에 놓아 주세요");
            CreateButton(canvas.transform, "저장하고 나가기", new Vector2(635f, 465f), new Vector2(380f, 62f), SaveAndReturnToLobby, Accent);
            CreatePanel(canvas.transform, "Plan Border", new Vector2(-275f, 42f), PlanSize + new Vector2(16f, 16f), PanelColor);
            planRect = CreateRect(canvas.transform, "Floor Plan", new Vector2(-275f, 42f), PlanSize);
            RawImage planImage = planRect.gameObject.AddComponent<RawImage>();
            planImage.texture = planTexture;
            planImage.raycastTarget = false;
            foreach (var entry in FixedAreas)
            {
                Vector3 viewport = planCamera.WorldToViewportPoint(stage.position + new Vector3(entry.area.center.x, 0f, entry.area.center.y));
                CreateText(planRect, entry.label, new Vector2((viewport.x - 0.5f) * PlanSize.x, (viewport.y - 0.5f) * PlanSize.y),
                    new Vector2(140f, 34f), 19, Ink, entry.label);
            }
            CreatePanel(canvas.transform, "Tools", new Vector2(615f, 42f), new Vector2(420f, 756f), PanelColor);
            selectionText = CreateText(canvas.transform, "Selected Furniture", new Vector2(615f, 335f), new Vector2(380f, 92f), 30, Ink, "가구를 선택해 주세요");
            CreateText(canvas.transform, "Instructions", new Vector2(615f, 204f), new Vector2(368f, 145f), 23, Ink,
                "가구 선택 → 평면도 조준 → 놓기\n\n놓은 가구도 눌러서 옮길 수 있습니다");
            CreateButton(canvas.transform, "90° 회전  ·  X / R", new Vector2(615f, 78f), new Vector2(362f, 64f), RotateSelection, ButtonColor);
            CreateButton(canvas.transform, "보관함으로 회수", new Vector2(615f, -4f), new Vector2(362f, 64f), RemoveSelection, ButtonColor);
            CreateButton(canvas.transform, "이동 취소  ·  B / Esc", new Vector2(615f, -86f), new Vector2(362f, 64f), CancelSelection, ButtonColor);
            MushHousingUiButton gridButton = CreateButton(canvas.transform, "격자 맞춤 : 켜짐", new Vector2(615f, -168f), new Vector2(362f, 58f), ToggleGrid, ButtonColor);
            gridText = gridButton.GetComponentInChildren<Text>();
            feedbackText = CreateText(canvas.transform, "Placement Feedback", new Vector2(615f, -266f), new Vector2(372f, 104f), 23,
                new Color(1f, 0.81f, 0.47f), string.Empty);
            CreatePanel(canvas.transform, "Inventory Tray", new Vector2(0f, -429f), new Vector2(1710f, 178f), PanelColor);
            CreateText(canvas.transform, "Inventory Label", new Vector2(-750f, -426f), new Vector2(165f, 100f), 27, Ink, "보유 가구");
            inventoryRoot = CreateRect(canvas.transform, "Owned Furniture", new Vector2(95f, -428f), new Vector2(1440f, 160f));
        }

        private void RefreshFurniture()
        {
            for (int i = 0; i < placedModels.Length; i++)
            {
                RemoveObject(placedModels[i]);
                placedModels[i] = null;
                string itemId = workingState.GetHousingPlacement(i);
                if (!string.IsNullOrEmpty(itemId) && catalog != null)
                    placedModels[i] = CreateFurniture(itemId, i, workingState.GetHousingPosition(i), workingState.GetHousingRotation(i));
            }
        }

        private GameObject CreateFurniture(string itemId, int index, Vector3 position, Quaternion rotation)
        {
            GameObject model = MushCustomizationVisuals.CreateFittedModel(catalog.GetPrefab(itemId), furnitureRoot,
                "Housing " + itemId, MushHousingLayout.PreviewSize(index), position, true);
            if (model != null) model.transform.localRotation = rotation;
            return model;
        }

        private void RefreshInventory()
        {
            for (int i = inventoryRoot.childCount - 1; i >= 0; i--) RemoveObject(inventoryRoot.GetChild(i).gameObject);
            List<MushCustomizationItemDefinition> items = new();
            foreach (MushCustomizationItemDefinition item in MushCustomizationDatabase.Items)
                if (item.category == MushItemCategory.Housing && workingState.Owns(item.id)) items.Add(item);
            if (items.Count == 0)
            {
                CreateText(inventoryRoot, "Empty Inventory", Vector2.zero, new Vector2(1280f, 110f), 27, Ink,
                    "보유한 가구가 없습니다. 상점에서 가구를 구입해 주세요.");
                return;
            }
            float width = Mathf.Min(430f, 1400f / items.Count - 18f);
            for (int i = 0; i < items.Count; i++)
            {
                MushCustomizationItemDefinition item = items[i];
                int index = MushHousingLayout.PlacementForItem(item.id);
                bool placed = !string.IsNullOrEmpty(workingState.GetHousingPlacement(index));
                Color color = selectedIndex == index ? Accent : ButtonColor;
                RectTransform card = CreateRect(inventoryRoot, item.displayName + " Card",
                    new Vector2((i - (items.Count - 1) * 0.5f) * (width + 18f), 0f), new Vector2(width, 146f));
                Image image = card.gameObject.AddComponent<Image>();
                image.color = color;
                AddButton(card, image, () => BeginSelection(index), color);
                RectTransform thumbnailRect = CreateRect(card, "Thumbnail", new Vector2(-width * 0.5f + 92f, 0f), new Vector2(168f, 124f));
                RawImage thumbnail = thumbnailRect.gameObject.AddComponent<RawImage>();
                thumbnail.texture = GetThumbnail(item.id);
                thumbnail.raycastTarget = false;
                CreateText(card, "Name", new Vector2(85f, 21f), new Vector2(width - 188f, 72f), 27, Ink, item.displayName);
                CreateText(card, "State", new Vector2(85f, -39f), new Vector2(width - 188f, 40f), 21, Ink,
                    selectedIndex == index ? "위치 선택 중" : placed ? "배치됨 · 눌러서 이동" : "보관 중 · 눌러서 배치");
            }
        }

        private void BeginSelection(int index)
        {
            if (index < 0 || index >= MushHousingLayout.PlacementCount || catalog == null) return;
            string itemId = ItemId(index);
            if (!workingState.Owns(itemId) || catalog.GetPrefab(itemId) == null)
            {
                SetMessage("이 가구의 모델을 불러올 수 없습니다.");
                return;
            }
            ClearSelection();
            selectedIndex = index;
            selectionFrame = Time.frameCount;
            if (questRig != null && questRig.RightTriggerPressedThisFrame) activeHand = XRNode.RightHand;
            else if (questRig != null && questRig.LeftTriggerPressedThisFrame) activeHand = XRNode.LeftHand;
            candidatePosition = workingState.GetHousingPosition(index);
            candidateYaw = workingState.GetHousingRotation(index).eulerAngles.y;
            ghost = CreateFurniture(itemId, index, candidatePosition, Quaternion.Euler(0f, candidateYaw, 0f));
            if (placedModels[index] != null) placedModels[index].SetActive(false);
            if (ghost != null) ghost.SetActive(false);
            selectionText.text = MushCustomizationDatabase.Find(itemId).displayName + "\n위치를 선택해 주세요";
            RefreshInventory();
        }

        private void PickPlacedFurniture(Vector3 position)
        {
            for (int i = placedModels.Length - 1; i >= 0; i--)
                if (placedModels[i] != null && GetFootprint(placedModels[i]).Contains(new Vector2(position.x, position.z)))
                {
                    BeginSelection(i);
                    return;
                }
        }

        private void CommitSelection()
        {
            workingState.SetHousingPlacement(selectedIndex, ItemId(selectedIndex));
            workingState.SetHousingPose(selectedIndex, candidatePosition, candidateYaw);
            ClearSelection();
            RefreshFurniture();
            RefreshInventory();
            SetMessage("배치했습니다. 가구를 다시 누르면 옮길 수 있습니다.");
        }

        private void ClearSelection()
        {
            if (selectedIndex >= 0 && placedModels[selectedIndex] != null) placedModels[selectedIndex].SetActive(true);
            RemoveObject(ghost);
            ghost = null;
            footprint.SetActive(false);
            selectedIndex = -1;
            selectionFrame = Time.frameCount;
            selectionText.text = "가구를 선택해 주세요";
        }

        private void CancelSelection()
        {
            ClearSelection();
            RefreshInventory();
            SetMessage("이동을 취소했습니다. 기존 배치를 유지합니다.");
        }

        private void RemoveSelection()
        {
            if (selectedIndex < 0) { SetMessage("회수할 가구를 먼저 선택해 주세요."); return; }
            workingState.SetHousingPlacement(selectedIndex, string.Empty);
            ClearSelection();
            RefreshFurniture();
            RefreshInventory();
            SetMessage("가구를 보관함으로 돌려놓았습니다.");
        }

        private void RotateSelection()
        {
            if (selectedIndex >= 0) candidateYaw = Mathf.Repeat(candidateYaw + 90f, 360f);
            else SetMessage("회전할 가구를 먼저 선택해 주세요.");
        }

        private void ToggleGrid()
        {
            snapToGrid = !snapToGrid;
            gridText.text = snapToGrid ? "격자 맞춤 : 켜짐" : "격자 맞춤 : 꺼짐";
        }

        private bool TryGetPlanPointer(out Vector3 position, out bool pressed)
        {
            position = default;
            pressed = false;
            Vector2 local;
            if (XRSettings.isDeviceActive)
            {
                if (questRig == null) return false;
                if (selectedIndex < 0)
                {
                    if (questRig.RightTriggerPressedThisFrame) activeHand = XRNode.RightHand;
                    else if (questRig.LeftTriggerPressedThisFrame) activeHand = XRNode.LeftHand;
                }
                if (!questRig.TryGetPointerRay(activeHand, out Ray ray)) return false;
                Plane plane = new(planRect.forward, planRect.position);
                if (!plane.Raycast(ray, out float distance) || distance > 4.5f) return false;
                local = planRect.InverseTransformPoint(ray.GetPoint(distance));
                pressed = activeHand == XRNode.LeftHand ? questRig.LeftTriggerPressedThisFrame : questRig.RightTriggerPressedThisFrame;
            }
            else
            {
                Mouse mouse = Mouse.current;
                if (mouse == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(planRect, mouse.position.ReadValue(),
                        canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : viewCamera, out local)) return false;
                pressed = mouse.leftButton.wasPressedThisFrame;
            }
            Rect rect = planRect.rect;
            if (!rect.Contains(local)) return false;
            Vector2 uv = new((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
            Ray floorRay = planCamera.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
            Plane floor = new(Vector3.up, stage.position);
            if (!floor.Raycast(floorRay, out float floorDistance)) return false;
            position = stage.InverseTransformPoint(floorRay.GetPoint(floorDistance));
            position.y = 0f;
            return true;
        }

        private Rect GetFootprint(GameObject model)
        {
            if (!MushCustomizationVisuals.TryCalculateWorldBounds(model, out Bounds bounds))
                return new Rect(model.transform.localPosition.x - 0.5f, model.transform.localPosition.z - 0.5f, 1f, 1f);
            Vector3 min = stage.InverseTransformPoint(bounds.min);
            Vector3 max = stage.InverseTransformPoint(bounds.max);
            return Rect.MinMaxRect(min.x, min.z, max.x, max.z);
        }

        private bool CanPlace(Rect area, out string reason)
        {
            Rect floor = MushHousingLayout.FloorBounds;
            if (area.xMin < floor.xMin || area.xMax > floor.xMax || area.yMin < floor.yMin || area.yMax > floor.yMax)
            {
                reason = "벽 안쪽에 가구 전체가 들어오도록 놓아 주세요.";
                return false;
            }
            foreach (var entry in FixedAreas)
                if (area.Overlaps(entry.area)) { reason = entry.label + " 공간은 비워 주세요."; return false; }
            Rect padded = Rect.MinMaxRect(area.xMin - 0.04f, area.yMin - 0.04f, area.xMax + 0.04f, area.yMax + 0.04f);
            for (int i = 0; i < placedModels.Length; i++)
                if (i != selectedIndex && placedModels[i] != null && padded.Overlaps(GetFootprint(placedModels[i])))
                {
                    reason = "다른 가구와 겹칩니다. 조금 옮겨 주세요.";
                    return false;
                }
            reason = string.Empty;
            return true;
        }

        private RenderTexture GetThumbnail(string itemId)
        {
            if (thumbnails.TryGetValue(itemId, out RenderTexture cached)) return cached;
            if (catalog == null || catalog.GetPrefab(itemId) == null) return null;
            GameObject thumbnailStage = new("Housing Thumbnail " + itemId);
            thumbnailStage.transform.SetParent(transform, false);
            thumbnailStage.transform.position = stage.position + new Vector3(40f * (thumbnails.Count + 1), 0f, 0f);
            MushCustomizationVisuals.CreateFittedModel(catalog.GetPrefab(itemId), thumbnailStage.transform, "Model", 1.55f, Vector3.zero, true);
            Vector3 anchor = thumbnailStage.transform.position;
            Vector3 cameraPosition = anchor + new Vector3(2.5f, 2.1f, 3f);
            RenderTexture texture = CreateTexture("Housing Thumbnail " + itemId, 256, 192);
            Camera camera = CreateTextureCamera("Thumbnail Camera", cameraPosition,
                Quaternion.LookRotation(anchor + Vector3.up * 0.65f - cameraPosition), texture);
            camera.orthographic = true;
            camera.orthographicSize = 1.06f;
            camera.backgroundColor = ButtonColor;
            thumbnails.Add(itemId, texture);
            StartCoroutine(FinishThumbnail(camera, thumbnailStage));
            return texture;
        }

        private IEnumerator FinishThumbnail(Camera camera, GameObject thumbnailStage)
        {
            // URP renders normally once; keep the GPU texture without a synchronous pixel readback.
            yield return new WaitForEndOfFrame();
            camera.enabled = false;
            camera.targetTexture = null;
            Destroy(camera.gameObject);
            Destroy(thumbnailStage);
        }

        private Camera CreateTextureCamera(string name, Vector3 position, Quaternion rotation, RenderTexture texture)
        {
            GameObject cameraObject = new(name, typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.SetPositionAndRotation(position, rotation);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 30f;
            camera.depth = -10f;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = texture;
            camera.aspect = (float)texture.width / texture.height;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.allowXRRendering = false;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            return camera;
        }

        private static RenderTexture CreateTexture(string name, int width, int height)
        {
            RenderTexture texture = new(width, height, 24)
            {
                name = name, antiAliasing = 1, useMipMap = false, wrapMode = TextureWrapMode.Clamp,
            };
            texture.Create();
            return texture;
        }

        private Material CreateMaterial(string name, Color color, bool unlit = false)
        {
            Material material = new(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"))
            {
                name = name, color = color,
            };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            runtimeMaterials.Add(material);
            return material;
        }

        private static GameObject CreateCube(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = cube.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            return cube;
        }

        private static RectTransform CreateRect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static void CreatePanel(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            Image image = CreateRect(parent, name, position, size).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private MushHousingUiButton CreateButton(Transform parent, string label, Vector2 position, Vector2 size, Action callback, Color color)
        {
            RectTransform rect = CreateRect(parent, label, position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            CreateText(rect, "Label", Vector2.zero, size - new Vector2(16f, 6f), 26, Ink, label);
            return AddButton(rect, image, callback, color);
        }

        private static MushHousingUiButton AddButton(RectTransform rect, Image image, Action callback, Color color)
        {
            BoxCollider collider = rect.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(rect.sizeDelta.x, rect.sizeDelta.y, 12f);
            MushHousingUiButton button = rect.gameObject.AddComponent<MushHousingUiButton>();
            button.Configure(rect, image, callback, color);
            return button;
        }

        private Text CreateText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, Color color, string content)
        {
            Text text = CreateRect(parent, name, position, size).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = content;
            return text;
        }

        private static string ItemId(int index) => index switch
        {
            MushHousingLayout.ChairPlacement => MushCustomizationIds.FurnitureChair,
            MushHousingLayout.TablePlacement => MushCustomizationIds.FurnitureTable,
            _ => MushCustomizationIds.FurnitureDogBed,
        };

        private void SetMessage(string message)
        {
            if (feedbackText != null) feedbackText.text = message;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveSession();
        }

        private void OnApplicationQuit() => SaveSession();

        private void SaveSession()
        {
            if (workingState == null) return;
            // The working save contains confirmed placements; a held preview is excluded.
            MushCustomizationSave.Save(workingState);
            MushGameSave.EnterLobby();
        }

        private void SaveAndReturnToLobby()
        {
            if (returning) return;
            // Only confirmed placements are saved; a held model returns to its previous pose.
            ClearSelection();
            MushCustomizationSave.Save(workingState);
            MushGameSave.EnterLobby();
            if (!Application.CanStreamedLevelBeLoaded("MushLobby"))
            {
                RefreshInventory();
                SetMessage("저장했습니다. MushLobby 씬을 찾을 수 없습니다.");
                return;
            }
            returning = true;
            SceneManager.LoadScene("MushLobby");
        }

        private static void RemoveObject(GameObject target)
        {
            if (target == null) return;
            target.SetActive(false);
            Destroy(target);
        }

        private void OnDestroy()
        {
            if (planCamera != null) planCamera.targetTexture = null;
            if (planTexture != null) { planTexture.Release(); Destroy(planTexture); }
            foreach (RenderTexture texture in thumbnails.Values) { texture.Release(); Destroy(texture); }
            foreach (Material material in runtimeMaterials)
                if (material != null) Destroy(material);
        }
    }
}
