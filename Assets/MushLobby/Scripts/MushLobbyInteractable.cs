using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    public enum MushLobbyAction
    {
        OpenMapBoard = 0,
        OpenShop = 1,
        OpenHousing = 2,
        SelectSnowfield = 4,
        SelectForest = 5,
        BuyScarf = 6,
        BuyForest = 7,
        HousingSlotA = 10,
        HousingSlotB = 11,
        HousingSlotC = 12,
        ClosePanel = 13
    }

    [DisallowMultipleComponent]
    public sealed class MushLobbyInteractable : MonoBehaviour
    {
        [SerializeField] private MushLobbyController controller;
        [SerializeField] private MushLobbyAction action;
        [SerializeField] private Renderer highlightRenderer;
        [SerializeField] private Color highlightColor = new Color(1f, 0.72f, 0.2f, 1f);

        private XRSimpleInteractable xrInteractable;
        private Color restingColor;
        private bool hasColor;

        public void Configure(
            MushLobbyController newController,
            MushLobbyAction newAction,
            Renderer newHighlightRenderer = null)
        {
            controller = newController;
            action = newAction;
            highlightRenderer = newHighlightRenderer;
        }

        private void Awake()
        {
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

        private void OnXrSelected(SelectEnterEventArgs args)
        {
            Trigger();
        }

        private void OnXrHoverEntered(HoverEnterEventArgs args)
        {
            SetHighlighted(true);
        }

        private void OnXrHoverExited(HoverExitEventArgs args)
        {
            SetHighlighted(false);
        }

        private void OnMouseEnter()
        {
            SetHighlighted(true);
        }

        private void OnMouseExit()
        {
            SetHighlighted(false);
        }

        private void SetHighlighted(bool highlighted)
        {
            if (!hasColor || highlightRenderer == null)
                return;

            highlightRenderer.material.color = highlighted ? highlightColor : restingColor;
        }
    }
}
