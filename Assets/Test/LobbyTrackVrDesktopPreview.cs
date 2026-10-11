using Mush.Lobby;
using UnityEngine;

namespace Mush.Testing
{
    /// <summary>Previews the runtime VR layout only while the lobby's track selector is open.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("Mush Tests/Lobby Track VR Desktop Preview")]
    public sealed class LobbyTrackVrDesktopPreview : DesktopVrUiPreview
    {
        [Header("Preview starts when this track panel opens")]
        [SerializeField] private MushLobbyMapPanel trackSelectionPanel;

        private Transform originalPanelParent;
        private int originalPanelSibling;
        private Vector3 originalPanelPosition;
        private Quaternion originalPanelRotation;
        private Vector3 originalPanelScale;

        protected override bool SupportsScene => gameObject.scene.name == "PM_Lobby";
        // Match the VR arguments in MushLobbyMapPanel.Configure.
        protected override float CanvasScale => 0.00125f;
        protected override float CanvasHeightOffset => 0.12f;
        protected override bool WantsPreview => trackSelectionPanel != null &&
            trackSelectionPanel.isActiveAndEnabled && !MushSceneUI.ModalOpen &&
            (MushLobbyPauseMenu.Active == null || !MushLobbyPauseMenu.Active.IsOpen);
        protected override bool DetachCanvasFromParent => false;

        protected override void OnPreviewStarting()
        {
            Transform panel = trackSelectionPanel.transform;
            originalPanelParent = panel.parent;
            originalPanelSibling = panel.GetSiblingIndex();
            originalPanelPosition = panel.localPosition;
            originalPanelRotation = panel.localRotation;
            originalPanelScale = panel.localScale;
            // Keep the Canvas under the panel so Close also hides it immediately.
            // Detach the panel from the camera so looking around cannot move the UI.
            panel.SetParent(null, true);
        }

        protected override void OnPreviewStopped()
        {
            if (trackSelectionPanel == null) return;
            Transform panel = trackSelectionPanel.transform;
            panel.SetParent(originalPanelParent, false);
            panel.SetSiblingIndex(originalPanelSibling);
            panel.SetLocalPositionAndRotation(originalPanelPosition, originalPanelRotation);
            panel.localScale = originalPanelScale;
        }
    }
}
