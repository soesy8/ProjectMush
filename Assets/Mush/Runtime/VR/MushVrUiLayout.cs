using UnityEngine;
using UnityEngine.UI;

namespace Mush.Quest
{
    public static class MushVrUiLayout
    {
        public static void PlaceFixed(Canvas canvas, Camera camera, float distance = 2.35f,
            float scale = 0.00125f, float heightOffset = 0f)
        {
            if (!MushQuestTrackedInputRig.IsXrActive || canvas == null || camera == null) return;
            RectTransform rect = canvas.GetComponent<RectTransform>();
            // Snapshot the current eye pose once; subsequent head movement never moves the menu.
            Vector3 forward = camera.transform.forward;
            rect.SetParent(null, true);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = new Vector2(1920f, 1080f);
            rect.localScale = Vector3.one * scale;
            rect.SetPositionAndRotation(camera.transform.position + forward * distance + camera.transform.up * heightOffset,
                camera.transform.rotation);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.overrideSorting = true;
            if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (canvas.GetComponent<MushCanvasQuestInput>() == null) canvas.gameObject.AddComponent<MushCanvasQuestInput>();
            Canvas.ForceUpdateCanvases();
        }
    }
}
