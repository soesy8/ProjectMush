using System.Collections.Generic;
using Mush.Quest;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>Feeds the existing Quest pointer rays into the same authored buttons and sliders as the mouse.</summary>
public sealed class MushCanvasQuestInput : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private GraphicRaycaster raycaster;
    private static readonly List<MushCanvasQuestInput> ActiveCanvases = new();
    private bool titleRigInstalled;
    private readonly List<RaycastResult> hits = new();
    private readonly PointerEventData[] pointers = new PointerEventData[2];
    private readonly GameObject[] hovered = new GameObject[2];

    private void OnEnable()
    {
        canvas ??= GetComponent<Canvas>();
        raycaster ??= GetComponent<GraphicRaycaster>();
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

    private void Update()
    {
        if (titleRigInstalled || gameObject.scene.name is not ("MushTitle" or "Title") || canvas == null ||
            !XRSettings.isDeviceActive) return;
        if (canvas.worldCamera == null)
        {
            Camera titleCamera = Camera.main;
            if (titleCamera == null)
                return;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = titleCamera;
            canvas.planeDistance = 2f;
            canvas.sortingOrder = 100;
        }
        Camera camera = canvas.worldCamera;
        // Ride and lobby already own tracked rigs. Only the new title needs to install one.
        MushQuestTrackedInputRig rig = MushQuestTrackedInputRig.InstallForCamera(camera);
        titleRigInstalled = rig != null;
        rig?.SetRayEnabled(true);
    }

    public static bool Handle(XRNode hand, Ray ray, bool pressed, bool held)
    {
        int handIndex = hand == XRNode.LeftHand ? 0 : 1;
        foreach (MushCanvasQuestInput input in ActiveCanvases)
        {
            if (input == null || !input.isActiveAndEnabled) continue;
            PointerEventData pointer = input.pointers[handIndex];
            if (pointer != null && (pointer.pointerPress != null || pointer.pointerDrag != null))
                return input.Process(handIndex, ray, pressed, held);
        }
        for (int i = ActiveCanvases.Count - 1; i >= 0; i--)
        {
            MushCanvasQuestInput input = ActiveCanvases[i];
            if (input != null && input.isActiveAndEnabled &&
                input.Process(hand == XRNode.LeftHand ? 0 : 1, ray, pressed, held)) return true;
        }
        return false;
    }

    private bool Process(int hand, Ray ray, bool pressed, bool held)
    {
        if (canvas == null || raycaster == null || canvas.worldCamera == null || EventSystem.current == null) return false;
        Plane plane = new(canvas.transform.forward, canvas.transform.position);
        bool intersects = plane.Raycast(ray, out float distance) && distance <= 4.5f;
        PointerEventData pointer = pointers[hand] ??= new PointerEventData(EventSystem.current)
        {
            pointerId = -100 - hand, button = PointerEventData.InputButton.Left,
        };
        if (intersects)
        {
            Vector2 position = canvas.worldCamera.WorldToScreenPoint(ray.GetPoint(distance));
            pointer.delta = position - pointer.position;
            pointer.position = position;
        }
        hits.Clear();
        if (intersects) raycaster.Raycast(pointer, hits);
        GameObject target = hits.Count > 0 ? hits[0].gameObject : null;
        pointer.pointerCurrentRaycast = hits.Count > 0 ? hits[0] : default;
        if (hovered[hand] != target)
        {
            if (hovered[hand] != null) ExecuteEvents.ExecuteHierarchy(hovered[hand], pointer, ExecuteEvents.pointerExitHandler);
            hovered[hand] = target;
            if (target != null) ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerEnterHandler);
        }
        if (pressed && target != null)
        {
            pointer.pressPosition = pointer.position;
            pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler)
                ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            pointer.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            if (pointer.pointerDrag != null)
            {
                ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.initializePotentialDrag);
                ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.beginDragHandler);
            }
        }
        bool captured = pointer.pointerPress != null || pointer.pointerDrag != null;
        if (held && pointer.pointerDrag != null && intersects)
            ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.dragHandler);
        if (!held && captured)
        {
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
            if (target != null && pointer.pointerPress == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target))
                ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerClickHandler);
            if (pointer.pointerDrag != null) ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
            pointer.pointerPress = null;
            pointer.pointerDrag = null;
        }
        return target != null || captured;
    }
}
