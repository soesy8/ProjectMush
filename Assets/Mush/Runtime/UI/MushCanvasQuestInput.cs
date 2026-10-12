using System.Collections.Generic;
using Mush.Quest;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>Feeds the existing Quest pointer rays into the same authored buttons and sliders as the mouse.</summary>
[DefaultExecutionOrder(-250)]
public sealed class MushCanvasQuestInput : MonoBehaviour
{
    private const float TitleCanvasDistance = 2.35f;
    [SerializeField] private float vrTitleHeightOffset = 0.55f;
    [SerializeField] private float vrTitleScale = 0.00135f;

    [SerializeField] private Canvas canvas;
    [SerializeField] private GraphicRaycaster raycaster;
    private static readonly List<MushCanvasQuestInput> ActiveCanvases = new();
    private static MushCanvasQuestInput[] activeSnapshot = System.Array.Empty<MushCanvasQuestInput>();
    private bool titleRigInstalled;
    private Camera titleSourceCamera;
    private Camera titleUiCamera;
    private List<Camera> titleCameraStack;
    private int titleOriginalCullingMask;
    private readonly List<RaycastResult> hits = new();
    private readonly PointerEventData[] pointers = new PointerEventData[2];
    private readonly GameObject[] hovered = new GameObject[2];
    private readonly Image[] highlightedSliderImages = new Image[2];

    public static bool IsQuestPointer(int pointerId) => pointerId == -100 || pointerId == -101;
    private readonly List<Graphic> graphics = new();
    private readonly List<Selectable> lobbyControls = new();
    private MushVrLobbyScreenCanvas lobbyScreen;
    private int graphicsRefreshFrame = -1;
    private Vector3 lastHitPosition;

    private void OnEnable()
    {
        canvas ??= GetComponent<Canvas>();
        raycaster ??= GetComponent<GraphicRaycaster>();
        graphicsRefreshFrame = -1;
        if (!ActiveCanvases.Contains(this))
        {
            ActiveCanvases.Add(this);
            activeSnapshot = ActiveCanvases.ToArray();
        }
    }
    private void OnDisable()
    {
        ReleasePointer(0);
        ReleasePointer(1);
        ActiveCanvases.Remove(this);
        activeSnapshot = ActiveCanvases.ToArray();
    }

    public void ConfigureLobby(Canvas target, GraphicRaycaster targetRaycaster, MushVrLobbyScreenCanvas screen)
    {
        canvas = target;
        raycaster = targetRaycaster;
        lobbyScreen = screen;
        graphicsRefreshFrame = -1;
    }

    public static void Release(XRNode hand)
    {
        foreach (MushCanvasQuestInput input in activeSnapshot)
            if (input != null) input.ReleasePointer(hand == XRNode.LeftHand ? 0 : 1);
    }

    private void ReleasePointer(int hand)
    {
        MushVrUiHighlight.Set(highlightedSliderImages[hand], false, false, -100 - hand);
        highlightedSliderImages[hand] = null;
        PointerEventData pointer = pointers[hand];
        if (pointer == null) return;
        GameObject press = pointer.pointerPress;
        GameObject drag = pointer.pointerDrag;
        GameObject previousHover = hovered[hand];
        pointer.pointerPress = null;
        pointer.pointerDrag = null;
        pointer.rawPointerPress = null;
        pointer.eligibleForClick = false;
        pointer.dragging = false;
        pointer.pointerEnter = null;
        hovered[hand] = null;
        if (press != null) ExecuteEvents.Execute(press, pointer, ExecuteEvents.pointerUpHandler);
        if (drag != null) ExecuteEvents.Execute(drag, pointer, ExecuteEvents.endDragHandler);
        if (previousHover != null) ExecuteEvents.ExecuteHierarchy(previousHover, pointer, ExecuteEvents.pointerExitHandler);
    }

    private void Start() => TryConfigureTitleVrCanvas();

    private void Update()
    {
        TryConfigureTitleVrCanvas();
        if (gameObject.scene.name == "PM_Lobby" && canvas != null &&
            canvas.renderMode != RenderMode.WorldSpace && MushQuestTrackedInputRig.IsXrActive)
            MushVrUiLayout.PlaceLobbyScreen(canvas, Camera.main);
    }

    private void TryConfigureTitleVrCanvas()
    {
        if (titleRigInstalled || gameObject.scene.name is not ("MushTitle" or "Title") || canvas == null ||
            !MushQuestTrackedInputRig.IsXrActive) return;
        Camera camera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        RectTransform rect = canvas.GetComponent<RectTransform>();
        if (camera == null || rect == null)
            return;

        // Ride and lobby already own tracked rigs. Only the new title needs to install one.
        MushQuestTrackedInputRig rig = camera.GetComponentInParent<MushQuestTrackedInputRig>();
        if (rig == null)
            rig = MushQuestTrackedInputRig.InstallForCamera(camera);
        if (rig == null)
            return;
        if (!rig.IsTracking) return;

        // Render the complete authored title canvas in both headset eyes, including the logo and options.
        // Configure it even when a camera is already assigned: an overlay does not become VR UI by itself.
        Camera uiCamera = CreateTitleUiCamera(camera);
        MushVrUiLayout.PlaceFixed(canvas, uiCamera, TitleCanvasDistance, vrTitleScale, vrTitleHeightOffset);
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;
        Canvas.ForceUpdateCanvases();
        rig.SetRayEnabled(true);
        titleRigInstalled = true;
    }

    private Camera CreateTitleUiCamera(Camera source)
    {
        UniversalAdditionalCameraData baseData = source.GetUniversalAdditionalCameraData();
        ScriptableRenderer renderer = baseData.scriptableRenderer;
        if (baseData.renderType != CameraRenderType.Base || renderer == null ||
            !renderer.SupportsCameraStackingType(CameraRenderType.Base) ||
            !renderer.SupportsCameraStackingType(CameraRenderType.Overlay))
            return source;

        // Render the authored title after the landscape, with its own cleared depth buffer.
        const int uiLayer = 5;
        const int uiMask = 1 << uiLayer;
        titleSourceCamera = source;
        titleOriginalCullingMask = source.cullingMask;
        titleCameraStack = baseData.cameraStack;
        GameObject cameraObject = new("Mush VR Title UI Camera");
        cameraObject.transform.SetParent(source.transform, false);
        titleUiCamera = cameraObject.AddComponent<Camera>();
        titleUiCamera.CopyFrom(source);
        titleUiCamera.cullingMask = uiMask;
        titleUiCamera.nearClipPlane = 0.01f;
        titleUiCamera.farClipPlane = 10f;
        titleUiCamera.useOcclusionCulling = false;
        titleUiCamera.enabled = true;

        UniversalAdditionalCameraData uiData = titleUiCamera.GetUniversalAdditionalCameraData();
        uiData.renderType = CameraRenderType.Overlay;
        uiData.renderPostProcessing = false;
        uiData.renderShadows = false;
        uiData.requiresDepthTexture = false;
        uiData.requiresColorTexture = false;
        uiData.allowXRRendering = true;
        baseData.allowXRRendering = true;
        foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = uiLayer;
        source.cullingMask &= ~uiMask;
        titleCameraStack.Add(titleUiCamera);
        return titleUiCamera;
    }

    private void OnDestroy()
    {
        if (titleUiCamera != null)
        {
            titleCameraStack?.Remove(titleUiCamera);
            Destroy(titleUiCamera.gameObject);
        }
        if (titleSourceCamera != null)
            titleSourceCamera.cullingMask = titleOriginalCullingMask;
    }

    public static bool Handle(XRNode hand, Ray ray, bool pressed, bool held)
        => Handle(hand, ray, pressed, held, out _);

    public static bool Handle(XRNode hand, Ray ray, bool pressed, bool held, out Vector3 hitPosition)
    {
        hitPosition = ray.GetPoint(4.5f);
        if (!MushQuestTrackedInputRig.IsXrActive) return false;
        EnsureEventSystem();
        int handIndex = hand == XRNode.LeftHand ? 0 : 1;
        MushCanvasQuestInput[] snapshot = activeSnapshot;
        MushCanvasQuestInput selected = null;
        RaycastResult best = default;
        foreach (MushCanvasQuestInput input in snapshot)
        {
            if (input == null || !input.isActiveAndEnabled) continue;
            PointerEventData pointer = input.pointers[handIndex];
            if (pointer != null && (pointer.pointerPress != null || pointer.pointerDrag != null))
            {
                selected = input;
                break;
            }
        }
        if (selected == null)
        {
            foreach (MushCanvasQuestInput input in snapshot)
            {
                if (input == null || !input.isActiveAndEnabled || !input.FindHit(ray)) continue;
                RaycastResult hit = input.hits[0];
                if (selected == null || CompareHits(hit, best) < 0)
                {
                    selected = input;
                    best = hit;
                }
            }
        }
        foreach (MushCanvasQuestInput input in snapshot)
            if (input != null && input != selected) input.ReleasePointer(handIndex);
        if (selected == null) return false;
        bool handled = selected.Process(handIndex, ray, pressed, held);
        hitPosition = selected.lastHitPosition;
        return handled;
    }

    private bool FindHit(Ray ray)
    {
        canvas ??= GetComponent<Canvas>();
        raycaster ??= GetComponent<GraphicRaycaster>();
        lobbyScreen ??= GetComponent<MushVrLobbyScreenCanvas>();
        lobbyScreen?.RefreshForInput();
        hits.Clear();
        if (canvas == null || !canvas.isActiveAndEnabled || EventSystem.current == null) return false;
        if (lobbyScreen != null) RaycastLobbyControls(ray);
        else if (raycaster != null && raycaster.isActiveAndEnabled && canvas.worldCamera != null)
            RaycastWorldGraphics(ray);
        return hits.Count > 0;
    }

    private static int CompareHits(RaycastResult left, RaycastResult right)
    {
        int order = SortingLayer.GetLayerValueFromID(right.sortingLayer)
            .CompareTo(SortingLayer.GetLayerValueFromID(left.sortingLayer));
        if (order == 0) order = right.sortingOrder.CompareTo(left.sortingOrder);
        if (order == 0) order = left.distance.CompareTo(right.distance);
        if (order == 0) order = right.depth.CompareTo(left.depth);
        return order;
    }

    private bool Process(int hand, Ray ray, bool pressed, bool held)
    {
        FindHit(ray);
        if (canvas == null || !canvas.isActiveAndEnabled || EventSystem.current == null) return false;
        Plane plane = new(canvas.transform.forward, canvas.transform.position);
        bool intersects = plane.Raycast(ray, out float distance) && distance >= 0f && distance <= 4.5f;
        lastHitPosition = intersects ? ray.GetPoint(distance) : ray.GetPoint(4.5f);
        PointerEventData pointer = pointers[hand] ??= new PointerEventData(EventSystem.current)
        {
            pointerId = -100 - hand, button = PointerEventData.InputButton.Left, useDragThreshold = false,
        };
        if (intersects && canvas.worldCamera != null)
        {
            Vector2 position = canvas.worldCamera.WorldToScreenPoint(ray.GetPoint(distance));
            pointer.delta = position - pointer.position;
            pointer.position = position;
        }
        GameObject target = hits.Count > 0 ? hits[0].gameObject : null;
        pointer.pointerCurrentRaycast = hits.Count > 0 ? hits[0] : default;
        if (hits.Count > 0)
        {
            lastHitPosition = hits[0].worldPosition;
            pointer.delta += hits[0].screenPosition - pointer.position;
            pointer.position = hits[0].screenPosition;
        }
        if (hovered[hand] != target)
        {
            if (target != null) EnsureVrButtonFeedback(target);
            if (hovered[hand] != null) ExecuteEvents.ExecuteHierarchy(hovered[hand], pointer, ExecuteEvents.pointerExitHandler);
            hovered[hand] = target;
            pointer.pointerEnter = target;
            if (target != null) ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerEnterHandler);
        }
        if (pressed && target != null)
        {
            Mush.Art.Test.ScreenClickVfx.PlayWorld(lastHitPosition, canvas.worldCamera);
            pointer.pressPosition = pointer.position;
            pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
            pointer.eligibleForClick = true;
            pointer.rawPointerPress = target;
            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler)
                ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            pointer.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            if (pointer.pointerDrag != null)
            {
                ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.initializePotentialDrag);
                ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.beginDragHandler);
                pointer.dragging = true;
            }
        }
        bool captured = pointer.pointerPress != null || pointer.pointerDrag != null;
        UpdateVrSliderHighlight(hand, target, held && captured);
        if (held && pointer.pointerDrag != null && intersects)
        {
            Slider slider = pointer.pointerDrag.GetComponent<Slider>();
            if (slider != null) SetSliderFromWorldRay(slider, ray);
            else ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.dragHandler);
        }
        if (!held && captured)
        {
            GameObject press = pointer.pointerPress;
            GameObject drag = pointer.pointerDrag;
            bool click = target != null && press == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            pointer.pointerPress = null;
            pointer.pointerDrag = null;
            pointer.rawPointerPress = null;
            pointer.eligibleForClick = false;
            pointer.dragging = false;
            if (press != null) ExecuteEvents.Execute(press, pointer, ExecuteEvents.pointerUpHandler);
            if (drag != null) ExecuteEvents.Execute(drag, pointer, ExecuteEvents.endDragHandler);
            // A close/open callback may disable this canvas; clear capture before dispatching it.
            if (click && press != null) ExecuteEvents.Execute(press, pointer, ExecuteEvents.pointerClickHandler);
        }
        return target != null || captured;
    }

    private static void EnsureVrButtonFeedback(GameObject target)
    {
        Button button = target.GetComponentInParent<Button>();
        if (button == null || button.GetComponent<Mush.UI.MushUiButtonFeedback>() != null ||
            button.targetGraphic is not Image artwork) return;
        SpriteState states = button.spriteState;
        Mush.UI.MushUiButtonFeedback.Attach(button.gameObject, artwork, states.highlightedSprite,
            states.pressedSprite, states.disabledSprite).SetHighlightOnFocus(false);
    }

    public static void EnsureEventSystem()
    {
        if (!MushQuestTrackedInputRig.IsXrActive || EventSystem.current != null) return;
        GameObject root = new("Mush VR EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        root.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    private void UpdateVrSliderHighlight(int hand, GameObject target, bool pressed)
    {
        Slider slider = target != null ? target.GetComponentInParent<Slider>() : null;
        Image image = slider != null && slider.IsInteractable()
            ? slider.handleRect != null ? slider.handleRect.GetComponent<Image>() : slider.targetGraphic as Image
            : null;
        int pointerId = -100 - hand;
        if (highlightedSliderImages[hand] != image)
            MushVrUiHighlight.Set(highlightedSliderImages[hand], false, false, pointerId);
        highlightedSliderImages[hand] = image;
        MushVrUiHighlight.Set(image, image != null, pressed, pointerId);
    }

    private void RaycastWorldGraphics(Ray ray)
    {
        if (graphicsRefreshFrame != Time.frameCount)
        {
            graphicsRefreshFrame = Time.frameCount;
            graphics.Clear();
            GetComponentsInChildren(true, graphics);
        }
        // Intersect the controller ray with the actual UI, without a headset-eye screen ray.
        foreach (Graphic graphic in graphics)
        {
            if (graphic == null || !graphic.isActiveAndEnabled || !graphic.raycastTarget) continue;
            RectTransform rect = graphic.rectTransform;
            Plane plane = new(rect.forward, rect.position);
            if (!plane.Raycast(ray, out float distance) || distance < 0f || distance > 4.5f) continue;
            Vector3 worldPoint = ray.GetPoint(distance);
            Vector3 local = rect.InverseTransformPoint(worldPoint);
            if (!rect.rect.Contains(new Vector2(local.x, local.y))) continue;
            Vector2 screenPoint = canvas.worldCamera.WorldToScreenPoint(worldPoint);
            if (!graphic.Raycast(screenPoint, canvas.worldCamera)) continue;
            // Decorative art must not consume a ray aimed at the actual button or slider.
            if (ExecuteEvents.GetEventHandler<IPointerDownHandler>(graphic.gameObject) == null &&
                ExecuteEvents.GetEventHandler<IPointerClickHandler>(graphic.gameObject) == null &&
                ExecuteEvents.GetEventHandler<IDragHandler>(graphic.gameObject) == null) continue;
            hits.Add(new RaycastResult
            {
                gameObject = graphic.gameObject, module = raycaster, distance = distance,
                worldPosition = worldPoint, worldNormal = rect.forward, screenPosition = screenPoint,
                depth = graphic.depth, sortingLayer = graphic.canvas.sortingLayerID,
                sortingOrder = graphic.canvas.sortingOrder,
            });
        }
        hits.Sort((left, right) =>
        {
            int order = right.sortingOrder.CompareTo(left.sortingOrder);
            return order != 0 ? order : right.depth.CompareTo(left.depth);
        });
    }

    private void RaycastLobbyControls(Ray ray)
    {
        if (canvas.worldCamera == null || raycaster == null || !raycaster.isActiveAndEnabled) return;
        if (graphicsRefreshFrame != Time.frameCount)
        {
            graphicsRefreshFrame = Time.frameCount;
            lobbyControls.Clear();
            GetComponentsInChildren(true, lobbyControls);
        }
        // Test the visible control in world space. Decorative graphics and their
        // raycastTarget flags must not hide the button that receives the events.
        foreach (Selectable control in lobbyControls)
        {
            if (control == null || !control.isActiveAndEnabled || !control.IsInteractable()) continue;
            Graphic artwork = control.targetGraphic;
            RectTransform rect = control is Button && artwork != null
                ? artwork.rectTransform : control.transform as RectTransform;
            if (rect == null || !new Plane(rect.forward, rect.position).Raycast(ray, out float distance) ||
                distance < 0f || distance > 4.5f) continue;
            Vector3 worldPoint = ray.GetPoint(distance);
            Vector3 local = rect.InverseTransformPoint(worldPoint);
            if (!rect.rect.Contains(new Vector2(local.x, local.y))) continue;
            Vector2 screenPoint = canvas.worldCamera.WorldToScreenPoint(worldPoint);
            // Preserve the authored CanvasGroup and mask filters, even when
            // the target artwork is not registered as a raycast graphic.
            if (artwork != null && (!artwork.isActiveAndEnabled ||
                !artwork.Raycast(screenPoint, canvas.worldCamera))) continue;
            Canvas owner = artwork != null ? artwork.canvas : canvas;
            if (owner == null) continue;
            hits.Add(new RaycastResult
            {
                gameObject = control.gameObject, module = raycaster, distance = distance,
                worldPosition = worldPoint, worldNormal = rect.forward, screenPosition = screenPoint,
                depth = artwork != null ? artwork.depth : 0, sortingLayer = owner.sortingLayerID,
                sortingOrder = owner.sortingOrder,
            });
        }
        hits.Sort(CompareHits);
    }

    private static void SetSliderFromWorldRay(Slider slider, Ray ray)
    {
        if (!slider.IsInteractable()) return;
        RectTransform area = slider.handleRect != null ? slider.handleRect.parent as RectTransform :
            slider.fillRect != null ? slider.fillRect.parent as RectTransform : slider.GetComponent<RectTransform>();
        if (area == null || !new Plane(area.forward, area.position).Raycast(ray, out float distance)) return;
        Vector3 local = area.InverseTransformPoint(ray.GetPoint(distance));
        Rect bounds = area.rect;
        bool horizontal = slider.direction is Slider.Direction.LeftToRight or Slider.Direction.RightToLeft;
        float value = horizontal ? Mathf.InverseLerp(bounds.xMin, bounds.xMax, local.x) :
            Mathf.InverseLerp(bounds.yMin, bounds.yMax, local.y);
        if (slider.direction is Slider.Direction.RightToLeft or Slider.Direction.TopToBottom) value = 1f - value;
        slider.normalizedValue = value;
    }
}
