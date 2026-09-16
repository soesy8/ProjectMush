using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyShopItem : MonoBehaviour
    {
        [SerializeField] private MushLobbyController controller;
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private TextMesh stateText;
        [SerializeField] private Transform previewRoot;
        [SerializeField] private float previewTurnSpeed = 24f;

        private XRSimpleInteractable interactable;

        public void Configure(
            MushLobbyController newController,
            string newItemId,
            string newDisplayName,
            TextMesh newStateText,
            Transform newPreviewRoot)
        {
            controller = newController;
            itemId = newItemId;
            displayName = newDisplayName;
            stateText = newStateText;
            previewRoot = newPreviewRoot;
        }

        private void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            if (interactable != null)
                interactable.selectEntered.AddListener(OnSelected);
            RefreshState();
        }

        private void OnDestroy()
        {
            if (interactable != null)
                interactable.selectEntered.RemoveListener(OnSelected);
        }

        private void Update()
        {
            if (previewRoot != null)
                previewRoot.Rotate(0f, previewTurnSpeed * Time.deltaTime, 0f, Space.Self);
        }

        public void Trigger()
        {
            controller?.AcquireShopItem(itemId, displayName);
            RefreshState();
        }

        private void RefreshState()
        {
            if (stateText != null)
                stateText.text = controller != null && controller.HasShopItem(itemId) ? "보유 중" : "눌러서 받기";
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            Trigger();
        }
    }
}
