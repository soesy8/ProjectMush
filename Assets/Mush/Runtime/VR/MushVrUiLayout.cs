using UnityEngine;
using UnityEngine.UI;

namespace Mush.Quest
{
    public static class MushVrUiLayout
    {
        public const float MenuDistance = 1f;
        public const float ResultDistance = 1.5f;

        public static void PlaceLobbyScreen(Canvas canvas, Camera camera)
            => PlaceScreen(canvas, camera);

        public static void PlaceScreen(Canvas canvas, Camera camera, float heightOffset = 0f)
        {
            if (!MushQuestTrackedInputRig.IsXrActive || canvas == null || camera == null) return;
            // Keep nested canvases under their menu owner so opening/closing the owner controls the UI.
            canvas.enabled = true;
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null) raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
            raycaster.enabled = true;
            MushCanvasQuestInput input = canvas.GetComponent<MushCanvasQuestInput>();
            if (input == null) input = canvas.gameObject.AddComponent<MushCanvasQuestInput>();
            MushVrLobbyScreenCanvas screen = canvas.GetComponent<MushVrLobbyScreenCanvas>();
            if (screen == null) screen = canvas.gameObject.AddComponent<MushVrLobbyScreenCanvas>();
            screen.Configure(canvas, camera, heightOffset);
            input.ConfigureLobby(canvas, raycaster, screen);
            Canvas.ForceUpdateCanvases();
        }

        public static void PlaceFixed(Canvas canvas, Camera camera, float distance = MenuDistance,
            float scale = 0.00125f / 2.35f, float heightOffset = 0f)
        {
            if (!MushQuestTrackedInputRig.IsXrActive || canvas == null || camera == null) return;
            RectTransform rect = canvas.GetComponent<RectTransform>();
            // Keep the owner hierarchy so its visibility still controls nested menus.
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = new Vector2(1920f, 1080f);
            Vector3 parentScale = rect.parent != null ? rect.parent.lossyScale : Vector3.one;
            rect.localScale = new Vector3(scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.overrideSorting = true;
            if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (canvas.GetComponent<MushCanvasQuestInput>() == null) canvas.gameObject.AddComponent<MushCanvasQuestInput>();
            PlaceWorldUi(rect, camera, distance, heightOffset);
            Canvas.ForceUpdateCanvases();
        }

        public static void PlaceWorldUi(Transform target, Camera camera, float distance = MenuDistance,
            float heightOffset = 0f)
        {
            if (!MushQuestTrackedInputRig.IsXrActive || target == null || camera == null) return;
            MushVrWorldUiAnchor anchor = target.GetComponent<MushVrWorldUiAnchor>();
            if (anchor == null) anchor = target.gameObject.AddComponent<MushVrWorldUiAnchor>();
            anchor.Configure(camera, distance, heightOffset);
        }
    }
}
