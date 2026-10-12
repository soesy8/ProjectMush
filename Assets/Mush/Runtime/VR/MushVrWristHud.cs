using UnityEngine;
using UnityEngine.XR;

namespace Mush.Quest
{
    /// <summary>Temporary, non-interactive presentation of an authored HUD above the left wrist.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(13000)]
    public sealed class MushVrWristHud : MonoBehaviour
    {
        private RectTransform content;
        private RectTransform wristCanvas;
        private Camera viewCamera;
        private MushQuestTrackedInputRig rig;
        private CanvasGroup visibility;
        private Transform originalParent;
        private int originalSibling;
        private Vector2 originalAnchorMin, originalAnchorMax, originalPivot, originalSize;
        private Vector3 originalPosition, originalScale;
        private Quaternion originalRotation;
        private float originalAlpha;
        private bool originalRaycasts, originalInteractable;
        private Transform[] layeredChildren;
        private int[] originalLayers;
        private MushCanvasQuestInput originalInput;
        private bool originalInputEnabled;
        private Vector3 wristOffset;
        private bool readable;
#if UNITY_EDITOR
        private bool editorPreview;
#endif

        public bool IsAttached => wristCanvas != null;
        public bool MenuOpen { get; set; }

        private void OnEnable() => Application.onBeforeRender += BeforeRender;

        private void OnDisable()
        {
            Application.onBeforeRender -= BeforeRender;
            Release();
        }

        public void Configure(Camera camera, Vector3 offset, float width)
        {
            if (camera == null || !MushQuestTrackedInputRig.IsXrActive) return;
            ConfigurePresentation(camera, offset, width);
        }

#if UNITY_EDITOR
        public void ConfigureEditorPreview(Camera camera, Vector3 offset, float width)
        {
            if (camera == null) return;
            editorPreview = true;
            ConfigurePresentation(camera, offset, width);
        }

        public void RefreshEditorPreview(Transform hand, bool tracked, float deltaTime)
            => RefreshPose(hand, tracked, deltaTime);
#endif

        private void ConfigurePresentation(Camera camera, Vector3 offset, float width)
        {
            MushVrLobbyUiCamera compositor = camera.GetComponent<MushVrLobbyUiCamera>();
            Camera source = compositor != null ? compositor.ViewCamera : camera;
            if (viewCamera != source) rig = null;
            viewCamera = source;
            wristOffset = offset;
            if (!IsAttached) Attach();
            if (wristCanvas != null)
                wristCanvas.localScale = Vector3.one * (Mathf.Max(0.1f, width) / wristCanvas.sizeDelta.x);
        }

        private void Attach()
        {
            content = transform as RectTransform;
            if (content == null) return;
            originalParent = content.parent;
            originalSibling = content.GetSiblingIndex();
            originalAnchorMin = content.anchorMin;
            originalAnchorMax = content.anchorMax;
            originalPivot = content.pivot;
            originalSize = content.sizeDelta;
            originalPosition = content.anchoredPosition3D;
            originalScale = content.localScale;
            originalRotation = content.localRotation;
            Vector2 size = content.rect.size;
            if (size.x <= 0f || size.y <= 0f) size = new Vector2(620f, 210f);

            visibility = GetComponent<CanvasGroup>();
            if (visibility == null) visibility = gameObject.AddComponent<CanvasGroup>();
            originalAlpha = visibility.alpha;
            originalRaycasts = visibility.blocksRaycasts;
            originalInteractable = visibility.interactable;
            visibility.alpha = 0f;
            visibility.blocksRaycasts = false;
            visibility.interactable = false;
            readable = false;

            // The original screen canvas must not auto-convert this HUD back to a menu.
            Canvas screen = GetComponentInParent<Canvas>();
            originalInput = screen != null ? screen.GetComponent<MushCanvasQuestInput>() : null;
            if (originalInput != null)
            {
                originalInputEnabled = originalInput.enabled;
                originalInput.enabled = false;
            }

            GameObject root = new("Mush VR Left Wrist HUD", typeof(RectTransform), typeof(Canvas));
            wristCanvas = root.GetComponent<RectTransform>();
            wristCanvas.sizeDelta = size;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
#if UNITY_EDITOR
            canvas.worldCamera = editorPreview ? viewCamera : MushVrLobbyUiCamera.GetFor(viewCamera);
#else
            canvas.worldCamera = MushVrLobbyUiCamera.GetFor(viewCamera);
#endif
            canvas.overrideSorting = true;
            canvas.sortingOrder = 250;
            int layer = canvas.worldCamera != viewCamera ? MushVrLobbyUiCamera.UiLayer : gameObject.layer;
            root.layer = layer;

            layeredChildren = GetComponentsInChildren<Transform>(true);
            originalLayers = new int[layeredChildren.Length];
            for (int i = 0; i < layeredChildren.Length; i++)
            {
                originalLayers[i] = layeredChildren[i].gameObject.layer;
                layeredChildren[i].gameObject.layer = layer;
            }
            content.SetParent(wristCanvas, false);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = size;
            content.anchoredPosition3D = Vector3.zero;
            content.localScale = Vector3.one;
            content.localRotation = Quaternion.identity;
        }

        private void LateUpdate() => UpdatePresentation(Time.unscaledDeltaTime);
        private void BeforeRender() => UpdatePresentation(0f);

        private void UpdatePresentation(float deltaTime)
        {
#if UNITY_EDITOR
            if (editorPreview) return;
#endif
            if (!IsAttached) return;
            if (!MushQuestTrackedInputRig.IsXrActive)
            {
                Release();
                return;
            }
            if (rig == null && viewCamera != null)
                rig = viewCamera.GetComponentInParent<MushQuestTrackedInputRig>();
            Transform hand = rig != null ? rig.LeftController : null;
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool tracked = rig != null && rig.IsTracking && hand != null && device.isValid &&
                (device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) ? isTracked :
                    device.TryGetFeatureValue(CommonUsages.devicePosition, out _) &&
                    device.TryGetFeatureValue(CommonUsages.deviceRotation, out _));
            RefreshPose(hand, tracked, deltaTime);
        }

        private void RefreshPose(Transform hand, bool tracked, float deltaTime)
        {
            if (!IsAttached || visibility == null) return;
            if (!tracked || hand == null || viewCamera == null || MenuOpen)
            {
                readable = false;
                visibility.alpha = 0f;
                return;
            }

            Vector3 position = hand.TransformPoint(wristOffset);
            Vector3 direction = position - viewCamera.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.99f
                    ? viewCamera.transform.forward : Vector3.up;
                wristCanvas.SetPositionAndRotation(position, Quaternion.LookRotation(direction, up));
            }

            // Head direction approximates looking at the wrist; no eye-tracking hardware is required.
            // Wider exit limits keep the panel from flickering at the edge of the viewing area.
            float distance = direction.magnitude;
            float gazeLimit = Mathf.Cos((readable ? 65f : 55f) * Mathf.Deg2Rad);
            readable = hand.position.y >= viewCamera.transform.position.y - (readable ? 0.6f : 0.5f) &&
                distance >= 0.18f && distance <= (readable ? 1f : 0.9f) &&
                Vector3.Dot(viewCamera.transform.forward, direction.normalized) >= gazeLimit;
            visibility.alpha = Mathf.MoveTowards(visibility.alpha, readable ? originalAlpha : 0f,
                Mathf.Max(0f, deltaTime) * 8f);
        }

        public void Release()
        {
            if (!IsAttached) return;
            GameObject root = wristCanvas.gameObject;
            wristCanvas = null;
            if (content != null)
            {
                content.SetParent(originalParent, false);
                content.SetSiblingIndex(originalSibling);
                content.anchorMin = originalAnchorMin;
                content.anchorMax = originalAnchorMax;
                content.pivot = originalPivot;
                content.sizeDelta = originalSize;
                content.anchoredPosition3D = originalPosition;
                content.localScale = originalScale;
                content.localRotation = originalRotation;
            }
            if (visibility != null)
            {
                visibility.alpha = originalAlpha;
                visibility.blocksRaycasts = originalRaycasts;
                visibility.interactable = originalInteractable;
            }
            for (int i = 0; i < layeredChildren.Length; i++)
                if (layeredChildren[i] != null) layeredChildren[i].gameObject.layer = originalLayers[i];
            if (originalInput != null) originalInput.enabled = originalInputEnabled;
            readable = false;
#if UNITY_EDITOR
            editorPreview = false;
#endif
            root.SetActive(false);
            if (Application.isPlaying) Destroy(root);
            else DestroyImmediate(root);
        }
    }
}
