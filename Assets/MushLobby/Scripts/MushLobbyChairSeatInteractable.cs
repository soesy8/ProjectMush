using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyChairSeatInteractable : MonoBehaviour
    {
        private MushLobbyStationNavigator navigator;
        private XRSimpleInteractable xrInteractable;

        public void Configure(MushLobbyStationNavigator owner)
        {
            navigator = owner;
            BindXrSelection();
        }

        private void Awake()
        {
            BindXrSelection();
        }

        private void BindXrSelection()
        {
            XRSimpleInteractable candidate = GetComponent<XRSimpleInteractable>();
            if (candidate == xrInteractable)
                return;
            if (xrInteractable != null)
                xrInteractable.selectEntered.RemoveListener(OnSelected);
            xrInteractable = candidate;
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
            navigator?.TrySitAtFireplace();
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            Trigger();
        }
    }
}
