using System.Collections.Generic;
using UnityEngine;

namespace Mush.Quest
{
    /// <summary>Keeps an opened VR UI at its world position and turns its front toward the player.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(12000)]
    public sealed class MushVrWorldUiAnchor : MonoBehaviour
    {
        private static readonly List<MushVrWorldUiAnchor> ActiveAnchors = new();
        private Camera view;
        private Vector3 worldPosition;
        private float distance;
        private float heightOffset;
        private bool hasColliders;
        private bool colliderPoseDirty;

        public void Configure(Camera camera, float newDistance = MushVrUiLayout.MenuDistance,
            float newHeightOffset = 0f)
        {
            if (camera == null) return;
            MushVrLobbyUiCamera compositor = camera.GetComponentInParent<MushVrLobbyUiCamera>();
            view = compositor != null ? compositor.ViewCamera : camera;
            distance = newDistance;
            heightOffset = newHeightOffset;
            hasColliders = GetComponentInChildren<Collider>(true) != null;
            enabled = true;
            Recenter();
        }

        public void Recenter()
        {
            if (view == null) return;
            worldPosition = view.transform.position + view.transform.forward * distance +
                view.transform.up * heightOffset;
            RefreshForInput();
        }

        private void OnEnable()
        {
            ActiveAnchors.Add(this);
            Application.onBeforeRender += UpdatePose;
            if (MushQuestTrackedInputRig.IsXrActive) Recenter();
        }

        private void OnDisable()
        {
            ActiveAnchors.Remove(this);
            Application.onBeforeRender -= UpdatePose;
        }

        private void LateUpdate() => UpdatePose();

        private void UpdatePose()
        {
            if (MushQuestTrackedInputRig.IsXrActive) RefreshForInput();
        }

        public bool RefreshForInput()
        {
            if (view == null) return false;
            // Unity UI's visible front is its negative Z side.
            Vector3 forward = worldPosition - view.transform.position;
            Quaternion rotation = transform.rotation;
            if (forward.sqrMagnitude > 0.000001f)
            {
                Vector3 up = Vector3.Cross(forward.normalized, Vector3.up).sqrMagnitude > 0.000001f
                    ? Vector3.up : Vector3.forward;
                rotation = Quaternion.LookRotation(forward, up);
            }
            bool moved = transform.position != worldPosition || transform.rotation != rotation;
            transform.SetPositionAndRotation(worldPosition, rotation);
            colliderPoseDirty |= moved && hasColliders;
            return moved;
        }

        public static void RefreshAllForInput()
        {
            bool syncColliders = false;
            foreach (MushVrWorldUiAnchor anchor in ActiveAnchors)
            {
                if (anchor == null) continue;
                anchor.RefreshForInput();
                syncColliders |= anchor.colliderPoseDirty;
                anchor.colliderPoseDirty = false;
            }
            if (syncColliders) Physics.SyncTransforms();
        }
    }
}
