using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Mush.Quest;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace Mush.Lobby
{
    public enum MushLobbyAction
    {
        OpenMapBoard = 0,
        OpenShop = 1,
        OpenHousing = 2,
        OpenCustomization = 3,
        SelectSnowfield = 4,
        SelectForest = 5,
        SelectSharpCurve = 8,
        BuyScarf = 6,
        BuyForest = 7,
        HousingSlotA = 10,
        HousingSlotB = 11,
        HousingSlotC = 12,
        ClosePanel = 13
    }

    [DisallowMultipleComponent]
    public sealed class MushLobbyInteractable : MonoBehaviour, IMushQuestRayTarget
    {
        [SerializeField] private MushLobbyController controller;
        [SerializeField] private MushLobbyAction action;
        [SerializeField] private Renderer highlightRenderer;
        [SerializeField] private Color highlightColor = new Color(1f, 0.72f, 0.2f, 1f);
        [Tooltip("공통 디자인 원본입니다. PM_Lobby 씬의 LobbyMenuUI를 수정하면 네 안내 패널에 반영됩니다.")]
        [SerializeField] private GameObject hoverPanelPrefab;

        private XRSimpleInteractable xrInteractable;
        private Color restingColor;
        private bool hasColor;
        [SerializeField] private GameObject hoverPanelCanvas;
        private Camera hoverCamera;
        private bool mouseHovered;
        private int questHoverCount;
        private bool xrHovered;

        public void Configure(
            MushLobbyController newController,
            MushLobbyAction newAction,
            Renderer newHighlightRenderer = null,
            GameObject newHoverPanelPrefab = null)
        {
            controller = newController;
            action = newAction;
            highlightRenderer = newHighlightRenderer;
            if (newHoverPanelPrefab != null)
                hoverPanelPrefab = newHoverPanelPrefab;
            if (highlightRenderer != null && highlightRenderer.material != null)
            {
                restingColor = highlightRenderer.material.color;
                hasColor = true;
            }
            if (Application.isPlaying)
                BindAuthoredHoverPanel();
        }

        public void SetController(MushLobbyController newController) => controller = newController;

        private void Awake()
        {
            ConfigureSelectionColliders();

            xrInteractable = GetComponent<XRSimpleInteractable>();
            if (xrInteractable != null)
            {
                xrInteractable.selectEntered.AddListener(OnXrSelected);
                xrInteractable.hoverEntered.AddListener(OnXrHoverEntered);
                xrInteractable.hoverExited.AddListener(OnXrHoverExited);
            }

            if (highlightRenderer != null && highlightRenderer.material != null)
            {
                restingColor = highlightRenderer.material.color;
                hasColor = true;
            }
            BindAuthoredHoverPanel();
        }

        private void OnEnable()
        {
            // OnEnable also runs after a Play Mode script reload, whereas Awake
            // may not. This prevents an already-running scene from retaining
            // the old solid map/shop/housing selection boxes.
            ConfigureSelectionColliders();
            if (hoverPanelCanvas != null)
                hoverPanelCanvas.SetActive(false);
        }

        private void ConfigureSelectionColliders()
        {
            // These colliders are only generous mouse/XR-ray selection zones.
            // Leaving them solid makes thrown physics objects hit an invisible
            // box well before reaching the visible model.
            foreach (Collider selectionCollider in GetComponents<Collider>())
            {
                if (selectionCollider != null)
                    selectionCollider.isTrigger = true;
            }
        }

        private void OnDestroy()
        {
            if (xrInteractable == null)
                return;

            xrInteractable.selectEntered.RemoveListener(OnXrSelected);
            xrInteractable.hoverEntered.RemoveListener(OnXrHoverEntered);
            xrInteractable.hoverExited.RemoveListener(OnXrHoverExited);
        }

        public void Trigger()
        {
            controller?.HandleAction(action);
        }

        private void OnDisable()
        {
            mouseHovered = false;
            questHoverCount = 0;
            xrHovered = false;
            SetHighlighted(false);
        }

        public void SetQuestRayHovered(bool hovered)
        {
            questHoverCount = Mathf.Max(0, questHoverCount + (hovered ? 1 : -1));
            RefreshHighlight();
        }
        public void SelectWithQuestRay() => Trigger();

        private void OnXrSelected(SelectEnterEventArgs args)
        {
            Trigger();
        }

        private void OnXrHoverEntered(HoverEnterEventArgs args)
        {
            xrHovered = true;
            RefreshHighlight();
        }

        private void OnXrHoverExited(HoverExitEventArgs args)
        {
            xrHovered = xrInteractable != null && xrInteractable.isHovered;
            RefreshHighlight();
        }

        private void RefreshHighlight()
        {
            SetHighlighted(mouseHovered || xrHovered || questHoverCount > 0);
        }

        private void SetHighlighted(bool highlighted)
        {
            if (hoverPanelCanvas != null)
            {
                hoverPanelCanvas.SetActive(highlighted);
                if (highlighted)
                    UpdateHoverPanelPose();
            }

            if (!hasColor || highlightRenderer == null)
                return;

            highlightRenderer.material.color = highlighted ? highlightColor : restingColor;
        }

        private void LateUpdate()
        {
            if (hoverPanelCanvas == null && !hasColor)
                return;
            if (hoverCamera == null)
                hoverCamera = Camera.main;
            Mouse mouse = Mouse.current;
            bool hovered = false;
            if (mouse != null && hoverCamera != null && Application.isFocused &&
                !UnityEngine.XR.XRSettings.isDeviceActive &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                Ray ray = hoverCamera.ScreenPointToRay(mouse.position.ReadValue());
                hovered = Physics.Raycast(ray, out RaycastHit hit, 12f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide) &&
                    hit.collider.GetComponentInParent<MushLobbyInteractable>() == this;
            }
            if (mouseHovered != hovered)
            {
                mouseHovered = hovered;
                RefreshHighlight();
            }
            if (hoverPanelCanvas != null && hoverPanelCanvas.activeSelf)
                UpdateHoverPanelPose();
        }

        private void BindAuthoredHoverPanel()
        {
            if (hoverPanelCanvas == null)
                hoverPanelCanvas = transform.Find(name + " Hover Panel")?.gameObject;
        }





        private void UpdateHoverPanelPose()
        {
            if (hoverPanelCanvas == null)
                return;
            if (hoverCamera == null)
                hoverCamera = Camera.main;
            if (hoverCamera == null)
                return;

            Collider selectionCollider = GetComponent<Collider>();
            Vector3 targetCenter = selectionCollider != null ? selectionCollider.bounds.center : transform.position;
            Vector3 towardCamera = hoverCamera.transform.position - targetCenter;
            if (towardCamera.sqrMagnitude < 0.0001f)
                towardCamera = -hoverCamera.transform.forward;
            towardCamera.Normalize();
            Vector3 surfacePoint = selectionCollider != null
                ? selectionCollider.ClosestPoint(hoverCamera.transform.position)
                : targetCenter;
            hoverPanelCanvas.transform.position = surfacePoint + towardCamera * 0.035f;
            hoverPanelCanvas.transform.rotation = Quaternion.LookRotation(-towardCamera, Vector3.up);
        }
    }
}
