using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Mush.Quest
{
    [DisallowMultipleComponent]
    public sealed class MushVrLobbyUiCamera : MonoBehaviour
    {
        public const int UiLayer = 30;
        private Camera source;
        private Camera overlay;
        private List<Camera> stack;
        private int originalMask;
        private bool installed;
        public Camera ViewCamera => source != null ? source : GetComponent<Camera>();

        public static Camera GetFor(Camera camera)
        {
            if (camera == null || !MushQuestTrackedInputRig.IsXrActive) return camera;
            MushVrLobbyUiCamera owner = camera.GetComponent<MushVrLobbyUiCamera>();
            if (owner == null) owner = camera.gameObject.AddComponent<MushVrLobbyUiCamera>();
            owner.EnsureInstalled(camera);
            return owner.overlay != null ? owner.overlay : camera;
        }

        private void EnsureInstalled(Camera camera)
        {
            if (installed) return;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            ScriptableRenderer renderer = data.scriptableRenderer;
            if (data.renderType != CameraRenderType.Base || renderer == null ||
                !renderer.SupportsCameraStackingType(CameraRenderType.Base) ||
                !renderer.SupportsCameraStackingType(CameraRenderType.Overlay)) return;
            source = camera;
            originalMask = camera.cullingMask;
            stack = data.cameraStack;
            GameObject root = new("Mush VR Lobby UI Camera");
            root.transform.SetParent(camera.transform, false);
            overlay = root.AddComponent<Camera>();
            overlay.CopyFrom(camera);
            overlay.cullingMask = 1 << UiLayer;
            overlay.nearClipPlane = 0.01f;
            overlay.farClipPlane = 10f;
            overlay.useOcclusionCulling = false;
            UniversalAdditionalCameraData uiData = overlay.GetUniversalAdditionalCameraData();
            uiData.renderType = CameraRenderType.Overlay;
            // New URP camera data clears depth by default; clearDepth is read-only.
            uiData.renderPostProcessing = false;
            uiData.renderShadows = false;
            uiData.requiresDepthTexture = false;
            uiData.requiresColorTexture = false;
            uiData.allowXRRendering = true;
            stack.Add(overlay);
            camera.cullingMask &= ~(1 << UiLayer);
            installed = true;
        }

        private void LateUpdate()
        {
            if (!installed || source == null || overlay == null) return;
            bool active = MushQuestTrackedInputRig.IsXrActive;
            overlay.enabled = active;
            source.cullingMask = active ? source.cullingMask & ~(1 << UiLayer) :
                (source.cullingMask & ~(1 << UiLayer)) | (originalMask & (1 << UiLayer));
        }

        private void OnDestroy()
        {
            if (source != null)
                source.cullingMask = (source.cullingMask & ~(1 << UiLayer)) | (originalMask & (1 << UiLayer));
            if (overlay != null)
            {
                stack?.Remove(overlay);
                Destroy(overlay.gameObject);
            }
        }
    }
}
