using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Mush.Quest
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(11000)]
    public sealed class MushVrLobbyScreenCanvas : MonoBehaviour
    {
        private const float Distance = 1.15f;
        private Canvas canvas;
        private Camera view;
        private RectTransform rect;
        private RenderMode originalMode;
        private Camera originalCamera;
        private Vector2 originalMin, originalMax, originalPivot, originalSize, originalPosition;
        private Vector3 originalScale, originalLocalPosition;
        private Quaternion originalRotation;
        private readonly Dictionary<GameObject, int> layers = new();
        private bool configured;
        private float heightOffset;
        private bool vrApplied;
        private bool originalOverrideSorting;
        private bool layersDirty = true;
        private int originalSortingOrder;
        private readonly Dictionary<Graphic, Material> originalMaterials = new();
        private readonly List<Material> fallbackMaterials = new();

        public void Configure(Canvas target, Camera camera, float newHeightOffset = 0f)
        {
            canvas = target;
            heightOffset = newHeightOffset;
            MushVrLobbyUiCamera compositor = camera.GetComponentInParent<MushVrLobbyUiCamera>();
            view = compositor != null ? compositor.ViewCamera : camera;
            rect = target.GetComponent<RectTransform>();
            if (rect == null) return;
            if (!configured)
            {
                originalMode = canvas.renderMode;
                originalCamera = canvas.worldCamera;
                originalOverrideSorting = canvas.overrideSorting;
                originalSortingOrder = canvas.sortingOrder;
                originalMin = rect.anchorMin; originalMax = rect.anchorMax;
                originalPivot = rect.pivot; originalSize = rect.sizeDelta;
                originalPosition = rect.anchoredPosition;
                originalLocalPosition = rect.localPosition;
                originalScale = rect.localScale; originalRotation = rect.localRotation;
                configured = true;
            }
            UpdatePose();
        }

        private void OnEnable() => Application.onBeforeRender += UpdatePose;
        private void OnDisable() => Application.onBeforeRender -= UpdatePose;
        private void OnTransformChildrenChanged() => layersDirty = true;
        private void LateUpdate() => UpdatePose();

        // The controller hit test must use the same current head pose as the rendered menu.
        public void RefreshForInput() => UpdatePose();

        private void UpdatePose()
        {
            if (!configured || canvas == null || rect == null || view == null) return;
            if (!MushQuestTrackedInputRig.IsXrActive)
            {
                if (vrApplied) { Restore(); vrApplied = false; layersDirty = true; }
                return;
            }
            vrApplied = true;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = MushVrLobbyUiCamera.GetFor(view);
            if (canvas.worldCamera != view && originalMaterials.Count > 0) RestoreMaterials();
            canvas.overrideSorting = true;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = new Vector2(1920f, 1080f);
            float scale = 2f * Distance * Mathf.Tan(27.5f * Mathf.Deg2Rad) / 1080f;
            Vector3 parentScale = rect.parent != null ? rect.parent.lossyScale : Vector3.one;
            rect.localScale = new Vector3(scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
            rect.SetPositionAndRotation(view.transform.position + view.transform.forward * Distance +
                view.transform.up * heightOffset,
                view.transform.rotation);
            if (!layersDirty) return;
            layersDirty = false;
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (!layers.ContainsKey(child.gameObject)) layers.Add(child.gameObject, child.gameObject.layer);
                child.gameObject.layer = MushVrLobbyUiCamera.UiLayer;
            }
            if (canvas.worldCamera == view) ApplyDepthFallback();
        }

        private void ApplyDepthFallback()
        {
            Shader imageShader = Resources.Load<Shader>("MushVrLobbyUi");
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (originalMaterials.ContainsKey(graphic)) continue;
                TMP_Text text = graphic as TMP_Text;
                Material source = text != null ? text.fontSharedMaterial : graphic.material;
                originalMaterials.Add(graphic, source);
                if (source == null) continue;
                Material material = new(source) { name = source.name + " VR Lobby Overlay" };
                if (text != null)
                {
                    if (material.HasProperty("_ZTestMode")) material.SetInt("_ZTestMode", 8);
                    else
                    {
                        Shader shader = Shader.Find("TextMeshPro/Distance Field Overlay");
                        if (shader != null) material.shader = shader;
                    }
                    text.fontSharedMaterial = material;
                }
                else
                {
                    if (imageShader != null) material.shader = imageShader;
                    graphic.material = material;
                }
                fallbackMaterials.Add(material);
            }
        }

        private void Restore()
        {
            canvas.renderMode = originalMode;
            canvas.worldCamera = originalCamera;
            canvas.overrideSorting = originalOverrideSorting;
            canvas.sortingOrder = originalSortingOrder;
            rect.anchorMin = originalMin; rect.anchorMax = originalMax; rect.pivot = originalPivot;
            rect.sizeDelta = originalSize; rect.localScale = originalScale;
            rect.localRotation = originalRotation; rect.localPosition = originalLocalPosition;
            rect.anchoredPosition = originalPosition;
            foreach (var state in layers) if (state.Key != null) state.Key.layer = state.Value;
            layers.Clear();
            RestoreMaterials();
        }

        private void RestoreMaterials()
        {
            foreach (var state in originalMaterials)
                if (state.Key is TMP_Text text) text.fontSharedMaterial = state.Value;
                else if (state.Key != null) state.Key.material = state.Value;
            originalMaterials.Clear();
            foreach (Material material in fallbackMaterials) Destroy(material);
            fallbackMaterials.Clear();
        }

        private void OnDestroy()
        {
            foreach (Material material in fallbackMaterials) Destroy(material);
        }
    }
}
