using System;
using System.Collections.Generic;
using System.Reflection;
using Mush.Quest;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Mush.Testing
{
    /// <summary>
    /// Isolated UI distance override with a desktop VR preview. In XR, retain the
    /// headset cameras/input and only bring the fixed panel closer. Disable/remove
    /// the scene's test root to restore the original layout and desktop settings.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public abstract class DesktopVrUiPreview : MonoBehaviour
    {
        [Header("UI preview references")]
        [SerializeField] protected Camera viewCamera;
        // Preserve the serialized field name used by the existing Title test object.
        [SerializeField, InspectorName("UI Canvas")] protected Canvas titleCanvas;
        [SerializeField] protected MushCanvasQuestInput questInput;
        [SerializeField] private Shader pointerShader;

        [Header("Mouse move: look / Left click: select or drag / Tab: release or capture mouse")]
        [SerializeField, Range(0.01f, 1f)] private float mouseSensitivity = 0.12f;
        [SerializeField, Range(1f, 89f)] private float pitchLimit = 75f;

        [Header("Quest 2 combined rendered view approximation (104 H x 98 V)")]
        // Recorded Quest 2 render frusta: 97 H per eye, 90 H overlap, 104 H total.
        // Source: https://risa2000.github.io/hmdgdb/hmd_cfgs/Quest2_Native_R72.html
        [Tooltip("Combined-eye angular extent shown by one desktop camera. Lens masking, eye separation and individual visible FOV require headset validation.")]
        [SerializeField, Range(30f, 150f)] private float binocularHorizontalFieldOfView = 104f;
        [SerializeField, Range(30f, 150f)] private float binocularVerticalFieldOfView = 98f;

        [Header("UI distance override: headset and desktop preview")]
        [Tooltip("Meters in front of the player. Disabling this test object restores the original placement.")]
        // A new field name also updates test roots that serialized the old 2.35m default.
        [SerializeField, Min(0.1f)] private float uiDistance = 1.8f;

        [Header("75% transparency = 25% opacity")]
        [SerializeField, Range(0f, 100f)] private float laserTransparencyPercent = 75f;
        [SerializeField] private Color laserColor = new(0.35f, 0.82f, 1f, 1f);
        [SerializeField, Min(0.001f)] private float hitDotRadius = 0.0125f;

        private const int UiLayer = 5;
        private const int PointerHand = 1;
        private const float RayLength = 4.5f;
        private static readonly Vector2 ReferenceResolution = new(1920f, 1080f);
        private delegate bool ProcessPointer(int hand, Ray ray, bool pressed, bool held);

        private ProcessPointer processPointer;
        private Action<int> releasePointer;
        private FieldInfo hitPositionField;
        private FieldInfo pointerCanvasField;
        private FieldInfo pointerRaycasterField;
        private Canvas originalPointerCanvas;
        private UnityEngine.UI.GraphicRaycaster originalPointerRaycaster;
        private bool started;
        private bool initialized;
        private bool captureRequested;
        private bool cursorCaptured;
        private bool skipMouseDelta;
        private float yaw;
        private float pitch;
        private Quaternion originalCameraRotation;
        private float originalFieldOfView;
        private float originalAspect;
        private bool originalAspectFollowsTarget;
        private Rect originalViewport;
        private int originalCullingMask;
        private CursorLockMode originalCursorLock;
        private bool originalCursorVisible;
        private bool originalQuestInputEnabled;
        private RectState originalRect;
        private RenderMode originalRenderMode;
        private Camera originalCanvasCamera;
        private bool originalOverrideSorting;
        private int originalSortingOrder;
        private readonly Dictionary<GameObject, int> originalLayers = new();
        private readonly Dictionary<BaseInputModule, bool> originalInputModules = new();
        private GameObject runtimeRoot;
        private Camera frameCamera;
        private Camera uiCamera;
        private List<Camera> cameraStack;
        private LineRenderer laser;
        private Transform hitDot;
        private Material pointerMaterial;
        private Mesh dotMesh;
        private bool xrDistanceApplied;
        private Vector3 xrOriginalCanvasPosition;
        private Vector3 xrAdjustedCanvasPosition;

        public bool PreviewActive => initialized;
        public float LaserOpacity => 1f - laserTransparencyPercent / 100f;

        protected abstract bool SupportsScene { get; }
        protected abstract float CanvasScale { get; }
        protected abstract float CanvasHeightOffset { get; }
        protected virtual bool WantsPreview => true;
        protected virtual bool DetachCanvasFromParent => true;
        protected virtual void OnPreviewStarting() { }
        protected virtual void OnPreviewStopped() { }

        protected virtual void Start()
        {
            started = true;
            TryInitialize();
        }

        protected virtual void OnEnable()
        {
            if (started) TryInitialize();
        }

        private void TryInitialize()
        {
            if (initialized || !Application.isEditor || !SupportsScene || !WantsPreview ||
                MushQuestTrackedInputRig.IsXrActive)
                return;

            if (viewCamera == null || titleCanvas == null || questInput == null ||
                pointerShader == null || EventSystem.current == null)
            {
                Debug.LogWarning("[DesktopVrUiPreview] Assign the camera, UI canvas, Quest input and pointer shader.", this);
                enabled = false;
                return;
            }

            // Reuse the exact controller button/slider path without enabling XR or
            // changing production code. Resolve once, and fail visibly if that API changes.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo process = typeof(MushCanvasQuestInput).GetMethod("Process", flags);
            MethodInfo release = typeof(MushCanvasQuestInput).GetMethod("ReleasePointer", flags);
            hitPositionField = typeof(MushCanvasQuestInput).GetField("lastHitPosition", flags);
            pointerCanvasField = typeof(MushCanvasQuestInput).GetField("canvas", flags);
            pointerRaycasterField = typeof(MushCanvasQuestInput).GetField("raycaster", flags);
            if (process == null || release == null || hitPositionField == null ||
                pointerCanvasField == null || pointerRaycasterField == null)
            {
                Debug.LogError("[DesktopVrUiPreview] The Quest pointer API changed; update this test adapter.", this);
                enabled = false;
                return;
            }
            processPointer = (ProcessPointer)Delegate.CreateDelegate(typeof(ProcessPointer), questInput, process);
            releasePointer = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), questInput, release);

            originalCameraRotation = viewCamera.transform.localRotation;
            originalFieldOfView = viewCamera.fieldOfView;
            originalAspect = viewCamera.aspect;
            Vector2 targetSize = GetTargetSize();
            originalAspectFollowsTarget = Mathf.Approximately(originalAspect, targetSize.x / targetSize.y);
            originalViewport = viewCamera.rect;
            originalCullingMask = viewCamera.cullingMask;
            originalCursorLock = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            originalQuestInputEnabled = questInput.enabled;
            originalPointerCanvas = (Canvas)pointerCanvasField.GetValue(questInput);
            originalPointerRaycaster = (UnityEngine.UI.GraphicRaycaster)pointerRaycasterField.GetValue(questInput);
            originalRect = new RectState((RectTransform)titleCanvas.transform);
            originalRenderMode = titleCanvas.renderMode;
            originalCanvasCamera = titleCanvas.worldCamera;
            originalOverrideSorting = titleCanvas.overrideSorting;
            originalSortingOrder = titleCanvas.sortingOrder;
            foreach (Transform child in titleCanvas.GetComponentsInChildren<Transform>(true))
                originalLayers[child.gameObject] = child.gameObject.layer;
            foreach (BaseInputModule module in EventSystem.current.GetComponents<BaseInputModule>())
                originalInputModules[module] = module.enabled;

            // Set this before allocating so OnDisable can also clean up partial setup.
            initialized = true;
            OnPreviewStarting();
            questInput.enabled = false;
            // Inactive authored lobby panels can retain missing serialized pointer
            // references. Supply this preview's targets only for its lifetime.
            pointerCanvasField.SetValue(questInput, titleCanvas);
            pointerRaycasterField.SetValue(questInput, titleCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>());
            foreach (BaseInputModule module in originalInputModules.Keys) module.enabled = false;
            runtimeRoot = new GameObject("Preview Runtime") { hideFlags = HideFlags.DontSave };
            runtimeRoot.transform.SetParent(transform, false);
            CreateFrameCamera();
            ApplyPreviewProjection();
            CreateUiCamera();
            PlaceFixedCanvas();
            CreatePointer();
            Application.onBeforeRender += ApplyPreviewView;
            yaw = pitch = 0f;
            captureRequested = true;
            skipMouseDelta = true;
        }

        private Vector2 GetTargetSize()
        {
            RenderTexture target = viewCamera.targetTexture;
            return target != null
                ? new Vector2(Mathf.Max(1, target.width), Mathf.Max(1, target.height))
                : new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        }

        private void CreateFrameCamera()
        {
            GameObject cameraObject = new("Preview Frame Background");
            cameraObject.transform.SetParent(runtimeRoot.transform, false);
            frameCamera = cameraObject.AddComponent<Camera>();
            frameCamera.clearFlags = CameraClearFlags.SolidColor;
            frameCamera.backgroundColor = Color.black;
            frameCamera.cullingMask = 0;
            frameCamera.depth = viewCamera.depth - 1f;
            frameCamera.rect = originalViewport;
            frameCamera.targetTexture = viewCamera.targetTexture;
            frameCamera.targetDisplay = viewCamera.targetDisplay;
            frameCamera.allowHDR = false;
            frameCamera.allowMSAA = false;
            UniversalAdditionalCameraData data = frameCamera.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.allowXRRendering = false;
        }

        private void ApplyPreviewProjection()
        {
            float vertical = Mathf.Clamp(binocularVerticalFieldOfView, 30f, 150f);
            float horizontal = Mathf.Clamp(binocularHorizontalFieldOfView, 30f, 150f);
            float aspect = Mathf.Tan(horizontal * 0.5f * Mathf.Deg2Rad) /
                           Mathf.Tan(vertical * 0.5f * Mathf.Deg2Rad);
            Vector2 target = GetTargetSize();
            float availableAspect = target.x * originalViewport.width / (target.y * originalViewport.height);
            Rect viewport = originalViewport;
            // Fit both angular limits without stretching the image. An FHD view
            // gets side margins; a narrow window gets top/bottom margins instead.
            if (availableAspect > aspect)
            {
                viewport.width *= aspect / availableAspect;
                viewport.x += (originalViewport.width - viewport.width) * 0.5f;
            }
            else
            {
                viewport.height *= availableAspect / aspect;
                viewport.y += (originalViewport.height - viewport.height) * 0.5f;
            }
            // Use whole pixels and their actual ratio. A fractional viewport can
            // round differently from the camera's projection and subtly stretch it.
            float width = Mathf.Max(1f, Mathf.Floor(viewport.width * target.x));
            float height = Mathf.Max(1f, Mathf.Floor(viewport.height * target.y));
            float x = Mathf.Floor(originalViewport.x * target.x +
                                  (originalViewport.width * target.x - width) * 0.5f);
            float y = Mathf.Floor(originalViewport.y * target.y +
                                  (originalViewport.height * target.y - height) * 0.5f);
            viewCamera.pixelRect = new Rect(x, y, width, height);
            viewCamera.aspect = width / height;
            viewCamera.fieldOfView = vertical;
        }

        private void CreateUiCamera()
        {
            UniversalAdditionalCameraData baseData = viewCamera.GetUniversalAdditionalCameraData();
            ScriptableRenderer renderer = baseData.scriptableRenderer;
            if (baseData.renderType != CameraRenderType.Base || renderer == null ||
                !renderer.SupportsCameraStackingType(CameraRenderType.Overlay))
                return;

            GameObject cameraObject = new("Preview UI Camera");
            cameraObject.transform.SetParent(runtimeRoot.transform, false);
            uiCamera = cameraObject.AddComponent<Camera>();
            uiCamera.CopyFrom(viewCamera);
            uiCamera.cullingMask = 1 << UiLayer;
            uiCamera.nearClipPlane = 0.01f;
            uiCamera.farClipPlane = 10f;
            uiCamera.useOcclusionCulling = false;
            UniversalAdditionalCameraData uiData = uiCamera.GetUniversalAdditionalCameraData();
            uiData.renderType = CameraRenderType.Overlay;
            uiData.renderPostProcessing = false;
            uiData.renderShadows = false;
            uiData.requiresDepthTexture = false;
            uiData.requiresColorTexture = false;
            uiData.allowXRRendering = false;
            viewCamera.cullingMask &= ~(1 << UiLayer);
            cameraStack = baseData.cameraStack;
            cameraStack.Add(uiCamera);
            SyncUiCamera();
        }

        private void PlaceFixedCanvas()
        {
            RectTransform rect = (RectTransform)titleCanvas.transform;
            // Keep runtime VR scale/height and an upright, fixed panel; use the
            // same closer distance override as the headset path below.
            Vector3 forward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            Transform parent = rect.parent;
            rect.SetParent(null, true);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = ReferenceResolution;
            rect.localScale = Vector3.one * CanvasScale;
            rect.SetPositionAndRotation(viewCamera.transform.position + forward * uiDistance +
                                        Vector3.up * CanvasHeightOffset, Quaternion.LookRotation(forward, Vector3.up));
            // The lobby retains its close/hide lifecycle while keeping this world pose.
            if (!DetachCanvasFromParent) rect.SetParent(parent, true);
            titleCanvas.renderMode = RenderMode.WorldSpace;
            titleCanvas.worldCamera = uiCamera != null ? uiCamera : viewCamera;
            titleCanvas.overrideSorting = true;
            titleCanvas.sortingOrder = 100;
            foreach (GameObject child in originalLayers.Keys) child.layer = UiLayer;
            Canvas.ForceUpdateCanvases();
        }

        private void CreatePointer()
        {
            pointerMaterial = new Material(pointerShader) { name = "UI Preview Pointer (25% opacity)" };
            GameObject rayObject = new("Center Selection Laser") { layer = UiLayer };
            rayObject.transform.SetParent(runtimeRoot.transform, false);
            laser = rayObject.AddComponent<LineRenderer>();
            laser.sharedMaterial = pointerMaterial;
            laser.useWorldSpace = true;
            laser.positionCount = 2;
            laser.startWidth = 0.003f;
            laser.endWidth = 0.001f;
            laser.startColor = laser.endColor = Color.white;
            laser.sortingOrder = 101;
            laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            laser.receiveShadows = false;

            GameObject dotObject = new("Center Hit Dot") { layer = UiLayer };
            dotObject.transform.SetParent(runtimeRoot.transform, false);
            hitDot = dotObject.transform;
            const int segments = 24;
            Vector3[] vertices = new Vector3[segments + 1];
            int[] triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }
            dotMesh = new Mesh { name = "UI Preview Hit Dot", vertices = vertices, triangles = triangles };
            dotMesh.RecalculateBounds();
            dotObject.AddComponent<MeshFilter>().sharedMesh = dotMesh;
            MeshRenderer dotRenderer = dotObject.AddComponent<MeshRenderer>();
            dotRenderer.sharedMaterial = pointerMaterial;
            dotRenderer.sortingOrder = 102;
            dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dotRenderer.receiveShadows = false;
        }

        protected virtual void Update()
        {
            if (!initialized) return;
            if (!WantsPreview)
            {
                Restore();
                return;
            }
            if (Keyboard.current?.tabKey.wasPressedThisFrame == true)
            {
                captureRequested = !captureRequested;
                skipMouseDelta = true;
            }
            if (!captureRequested || !Application.isFocused)
            {
                ReleaseControl();
                return;
            }
            if (cursorCaptured && Cursor.lockState != CursorLockMode.Locked)
            {
                // Respect the Editor's Escape-to-unlock behavior. Tab captures again.
                captureRequested = false;
                ReleaseControl();
                return;
            }
            if (!cursorCaptured)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                cursorCaptured = true;
                skipMouseDelta = true;
            }
            if (skipMouseDelta)
            {
                skipMouseDelta = false;
                return;
            }
            Vector2 delta = Mouse.current?.delta.ReadValue() ?? Vector2.zero;
            yaw = Mathf.Repeat(yaw + delta.x * mouseSensitivity + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -pitchLimit, pitchLimit);
        }

        protected virtual void LateUpdate()
        {
            if (MushQuestTrackedInputRig.IsXrActive)
            {
                if (initialized) Restore();
                ApplyXrDistance();
                return;
            }
            RestoreXrDistance();
            if (!WantsPreview)
            {
                Restore();
                return;
            }
            if (!initialized)
            {
                if (started) TryInitialize();
                return;
            }
            ApplyPreviewView();
            Ray ray = new(viewCamera.transform.position, viewCamera.transform.forward);
            Mouse mouse = Mouse.current;
            bool canSelect = cursorCaptured && Application.isFocused;
            bool hit = processPointer(PointerHand, ray, canSelect && mouse?.leftButton.wasPressedThisFrame == true,
                                      canSelect && mouse?.leftButton.isPressed == true);
            // A UI click can close its panel. Restore before this frame renders.
            if (!WantsPreview)
            {
                Restore();
                return;
            }
            Vector3 end = hit ? (Vector3)hitPositionField.GetValue(questInput) : ray.GetPoint(RayLength);
            Color color = laserColor;
            color.a = Mathf.Clamp01(LaserOpacity);
            pointerMaterial.SetColor("_Color", color);
            laser.SetPosition(0, ray.origin);
            laser.SetPosition(1, end);
            // A ray viewed exactly along its own axis looks like a point. Keep that
            // point visible at the actual hit, with the same requested transparency.
            hitDot.SetPositionAndRotation(end - ray.direction * 0.005f, viewCamera.transform.rotation);
            hitDot.localScale = Vector3.one * hitDotRadius;
        }

        private void ApplyXrDistance()
        {
            if (!SupportsScene || !WantsPreview || titleCanvas == null || viewCamera == null ||
                titleCanvas.renderMode != RenderMode.WorldSpace)
            {
                RestoreXrDistance();
                return;
            }
            // Apply once after the production VR layout, and again only if it is
            // rebuilt. Head motion must not make a fixed panel follow the player.
            Vector3 position = titleCanvas.transform.position;
            if (xrDistanceApplied && (position - xrAdjustedCanvasPosition).sqrMagnitude < 0.000001f)
                return;
            Vector3 forward = Vector3.ProjectOnPlane(titleCanvas.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) return;
            xrOriginalCanvasPosition = position;
            float currentDistance = Vector3.Dot(position - viewCamera.transform.position, forward);
            xrAdjustedCanvasPosition = position + forward * (Mathf.Max(0.1f, uiDistance) - currentDistance);
            titleCanvas.transform.position = xrAdjustedCanvasPosition;
            xrDistanceApplied = true;
        }

        private void RestoreXrDistance()
        {
            if (!xrDistanceApplied) return;
            // Respect any layout change made by another system after our override.
            if (titleCanvas != null &&
                (titleCanvas.transform.position - xrAdjustedCanvasPosition).sqrMagnitude < 0.000001f)
                titleCanvas.transform.position = xrOriginalCanvasPosition;
            xrDistanceApplied = false;
        }

        [BeforeRenderOrder(20000)]
        private void ApplyPreviewView()
        {
            if (!initialized || !WantsPreview || MushQuestTrackedInputRig.IsXrActive) return;
            // The lobby also applies its desktop pose before rendering. Apply this
            // test pose afterwards so the displayed view matches the selection ray.
            viewCamera.transform.localRotation = originalCameraRotation * Quaternion.Euler(pitch, yaw, 0f);
            ApplyPreviewProjection();
            SyncUiCamera();
        }

        private void SyncUiCamera()
        {
            if (uiCamera == null) return;
            uiCamera.transform.SetPositionAndRotation(viewCamera.transform.position, viewCamera.transform.rotation);
            uiCamera.rect = viewCamera.rect;
            uiCamera.fieldOfView = viewCamera.fieldOfView;
            uiCamera.aspect = viewCamera.aspect;
        }

        private void ReleaseControl()
        {
            releasePointer?.Invoke(PointerHand);
            if (!cursorCaptured) return;
            Cursor.lockState = originalCursorLock;
            Cursor.visible = originalCursorVisible;
            cursorCaptured = false;
            skipMouseDelta = true;
        }

        protected virtual void OnApplicationFocus(bool focused)
        {
            if (!focused) ReleaseControl();
        }

        protected virtual void OnDisable()
        {
            Restore();
            RestoreXrDistance();
        }

        protected virtual void OnDestroy()
        {
            Restore();
            RestoreXrDistance();
        }

        private void Restore()
        {
            if (!initialized) return;
            initialized = false;
            Application.onBeforeRender -= ApplyPreviewView;
            ReleaseControl();
            if (titleCanvas != null)
            {
                titleCanvas.renderMode = originalRenderMode;
                titleCanvas.worldCamera = originalCanvasCamera;
                titleCanvas.overrideSorting = originalOverrideSorting;
                titleCanvas.sortingOrder = originalSortingOrder;
                originalRect.Restore((RectTransform)titleCanvas.transform);
            }
            foreach (var pair in originalLayers)
                if (pair.Key != null) pair.Key.layer = pair.Value;
            originalLayers.Clear();
            if (uiCamera != null)
            {
                uiCamera.enabled = false;
                cameraStack?.Remove(uiCamera);
            }
            if (runtimeRoot != null)
            {
                runtimeRoot.SetActive(false);
                Destroy(runtimeRoot);
            }
            if (pointerMaterial != null) Destroy(pointerMaterial);
            if (dotMesh != null) Destroy(dotMesh);
            if (viewCamera != null)
            {
                viewCamera.transform.localRotation = originalCameraRotation;
                viewCamera.fieldOfView = originalFieldOfView;
                viewCamera.rect = originalViewport;
                if (originalAspectFollowsTarget) viewCamera.ResetAspect();
                else viewCamera.aspect = originalAspect;
                viewCamera.cullingMask = originalCullingMask;
            }
            foreach (var pair in originalInputModules)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            originalInputModules.Clear();
            if (questInput != null)
            {
                pointerCanvasField.SetValue(questInput, originalPointerCanvas);
                pointerRaycasterField.SetValue(questInput, originalPointerRaycaster);
                questInput.enabled = originalQuestInputEnabled;
            }
            OnPreviewStopped();
            runtimeRoot = null;
            frameCamera = null;
            uiCamera = null;
            cameraStack = null;
        }

        private readonly struct RectState
        {
            private readonly Transform parent;
            private readonly int sibling;
            private readonly Vector2 anchorMin, anchorMax, pivot, sizeDelta;
            private readonly Vector3 position, scale;
            private readonly Quaternion rotation;

            public RectState(RectTransform rect)
            {
                parent = rect.parent;
                sibling = rect.GetSiblingIndex();
                anchorMin = rect.anchorMin;
                anchorMax = rect.anchorMax;
                pivot = rect.pivot;
                sizeDelta = rect.sizeDelta;
                position = rect.anchoredPosition3D;
                rotation = rect.localRotation;
                scale = rect.localScale;
            }

            public void Restore(RectTransform rect)
            {
                rect.SetParent(parent, false);
                rect.SetSiblingIndex(sibling);
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.pivot = pivot;
                rect.sizeDelta = sizeDelta;
                rect.anchoredPosition3D = position;
                rect.localRotation = rotation;
                rect.localScale = scale;
            }
        }
    }
}
