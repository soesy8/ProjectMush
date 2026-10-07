using System.Collections.Generic;
using Mush.Quest;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>Feeds the existing Quest pointer rays into the same authored buttons and sliders as the mouse.</summary>
[DefaultExecutionOrder(-250)]
public sealed class MushCanvasQuestInput : MonoBehaviour
{
    private const float TitleCanvasDistance = 2.35f;
    [SerializeField] private float vrTitleHeightOffset = 0.30f;
    [SerializeField] private float vrTitleScale = 0.00135f;

    [SerializeField] private Canvas canvas;
    [SerializeField] private GraphicRaycaster raycaster;
    private static readonly List<MushCanvasQuestInput> ActiveCanvases = new();
    private bool titleRigInstalled;
    private Camera titleSourceCamera;
    private Camera titleUiCamera;
    private List<Camera> titleCameraStack;
    private int titleOriginalCullingMask;
    private readonly List<RaycastResult> hits = new();
    private readonly PointerEventData[] pointers = new PointerEventData[2];
    private readonly GameObject[] hovered = new GameObject[2];
    private Graphic[] graphics;
    private Vector3 lastHitPosition;

    private void OnEnable()
    {
        canvas ??= GetComponent<Canvas>();
        raycaster ??= GetComponent<GraphicRaycaster>();
        graphics = GetComponentsInChildren<Graphic>(true);
        if (!ActiveCanvases.Contains(this)) ActiveCanvases.Add(this);
    }
    private void OnDisable()
    {
        ReleasePointer(0);
        ReleasePointer(1);
        ActiveCanvases.Remove(this);
    }

    public static void Release(XRNode hand)
    {
        foreach (MushCanvasQuestInput input in ActiveCanvases)
            if (input != null) input.ReleasePointer(hand == XRNode.LeftHand ? 0 : 1);
    }

    private void ReleasePointer(int hand)
    {
        PointerEventData pointer = pointers[hand];
        if (pointer == null) return;
        if (pointer.pointerPress != null) ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
        if (pointer.pointerDrag != null) ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
        if (hovered[hand] != null) ExecuteEvents.ExecuteHierarchy(hovered[hand], pointer, ExecuteEvents.pointerExitHandler);
        pointer.pointerPress = null;
        pointer.pointerDrag = null;
        hovered[hand] = null;
    }

    private void Start() => TryConfigureTitleVrCanvas();

    private void Update()
    {
        TryConfigureTitleVrCanvas();
        if (gameObject.scene.name == "PM_Lobby" && canvas != null &&
            canvas.renderMode != RenderMode.WorldSpace && MushQuestTrackedInputRig.IsXrActive)
            MushVrUiLayout.PlaceFixed(canvas, Camera.main);
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
        int handIndex = hand == XRNode.LeftHand ? 0 : 1;
        foreach (MushCanvasQuestInput input in ActiveCanvases)
        {
            if (input == null || !input.isActiveAndEnabled) continue;
            PointerEventData pointer = input.pointers[handIndex];
            if (pointer != null && (pointer.pointerPress != null || pointer.pointerDrag != null))
            {
                bool handled = input.Process(handIndex, ray, pressed, held);
                hitPosition = input.lastHitPosition;
                return handled;
            }
        }
        for (int i = ActiveCanvases.Count - 1; i >= 0; i--)
        {
            MushCanvasQuestInput input = ActiveCanvases[i];
            if (input != null && input.isActiveAndEnabled &&
                input.Process(handIndex, ray, pressed, held))
            {
                hitPosition = input.lastHitPosition;
                return true;
            }
        }
        return false;
    }

    private bool Process(int hand, Ray ray, bool pressed, bool held)
    {
        if (canvas == null || raycaster == null || !raycaster.isActiveAndEnabled ||
            canvas.worldCamera == null || EventSystem.current == null) return false;
        Plane plane = new(canvas.transform.forward, canvas.transform.position);
        bool intersects = plane.Raycast(ray, out float distance) && distance >= 0f && distance <= 4.5f;
        lastHitPosition = intersects ? ray.GetPoint(distance) : ray.GetPoint(4.5f);
        PointerEventData pointer = pointers[hand] ??= new PointerEventData(EventSystem.current)
        {
            pointerId = -100 - hand, button = PointerEventData.InputButton.Left, useDragThreshold = false,
        };
        if (intersects)
        {
            Vector2 position = canvas.worldCamera.WorldToScreenPoint(ray.GetPoint(distance));
            pointer.delta = position - pointer.position;
            pointer.position = position;
        }
        hits.Clear();
        if (intersects) RaycastWorldGraphics(ray);
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
            if (hovered[hand] != null) ExecuteEvents.ExecuteHierarchy(hovered[hand], pointer, ExecuteEvents.pointerExitHandler);
            hovered[hand] = target;
            if (target != null) ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerEnterHandler);
        }
        if (pressed && target != null)
        {
            Mush.Art.Test.ScreenClickVfx.PlayWorld(ray.GetPoint(distance), canvas.worldCamera);
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
        if (held && pointer.pointerDrag != null && intersects)
        {
            Slider slider = pointer.pointerDrag.GetComponent<Slider>();
            if (slider != null) SetSliderFromWorldRay(slider, ray);
            else ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.dragHandler);
        }
        if (!held && captured)
        {
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
            if (target != null && pointer.pointerPress == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target))
                ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerClickHandler);
            if (pointer.pointerDrag != null) ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
            pointer.pointerPress = null;
            pointer.pointerDrag = null;
            pointer.rawPointerPress = null;
            pointer.eligibleForClick = false;
            pointer.dragging = false;
        }
        return target != null || captured;
    }

    private void RaycastWorldGraphics(Ray ray)
    {
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
