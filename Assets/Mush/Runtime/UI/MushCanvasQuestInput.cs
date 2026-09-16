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
    private static MushCanvasQuestInput active;
    private bool titleRigInstalled;
    private readonly List<RaycastResult> hits = new();
    private readonly PointerEventData[] pointers = new PointerEventData[2];
    private readonly GameObject[] hovered = new GameObject[2];

    private void OnEnable() => active = this;
    private void OnDisable() { if (active == this) active = null; }

    private void Update()
    {
        if (titleRigInstalled || gameObject.scene.name != "MushTitle" || canvas == null ||
            canvas.worldCamera == null || !XRSettings.isDeviceActive) return;
        Camera camera = canvas.worldCamera;
        // Ride and lobby already own tracked rigs. Only the new title needs to install one.
        MushQuestTrackedInputRig rig = MushQuestTrackedInputRig.InstallForCamera(camera);
        titleRigInstalled = rig != null;
        rig?.SetRayEnabled(true);
    }

    public static bool Handle(XRNode hand, Ray ray, bool pressed, bool held)
    {
        return active != null && active.Process(hand == XRNode.LeftHand ? 0 : 1, ray, pressed, held);
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
