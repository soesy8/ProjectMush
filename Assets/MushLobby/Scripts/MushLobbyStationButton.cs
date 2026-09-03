using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyStationButton : MonoBehaviour
    {
        private MushLobbyStationNavigator navigator;
        private int stationIndex;
        private Renderer buttonRenderer;
        private XRSimpleInteractable xrInteractable;

        public void Configure(MushLobbyStationNavigator owner, int index, Renderer renderer)
        {
            navigator = owner;
            stationIndex = index;
            buttonRenderer = renderer;
        }

        private void Awake()
        {
            xrInteractable = GetComponent<XRSimpleInteractable>();
            if (xrInteractable != null)
                xrInteractable.selectEntered.AddListener(OnSelected);
        }

        private void OnDestroy()
        {
            if (xrInteractable != null)
                xrInteractable.selectEntered.RemoveListener(OnSelected);
        }

        public void Trigger()
        {
            navigator?.TravelTo(stationIndex);
        }

        public void SetSelected(bool selected, Material normal, Material highlighted)
        {
            if (buttonRenderer != null)
                buttonRenderer.sharedMaterial = selected ? highlighted : normal;
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            Trigger();
        }
    }
}
