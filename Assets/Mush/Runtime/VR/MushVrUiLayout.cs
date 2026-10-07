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
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            rect.SetParent(null, true);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = new Vector2(1920f, 1080f);
            rect.localScale = Vector3.one * scale;
            rect.SetPositionAndRotation(camera.transform.position + forward * distance + Vector3.up * heightOffset,
                Quaternion.LookRotation(forward, Vector3.up));
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.overrideSorting = true;
            if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (canvas.GetComponent<MushCanvasQuestInput>() == null) canvas.gameObject.AddComponent<MushCanvasQuestInput>();
            Canvas.ForceUpdateCanvases();
        }
    }
}
