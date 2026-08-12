using System.Collections.Generic;
using UnityEngine;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyFurnitureObstacle : MonoBehaviour
    {
        private static readonly List<MushLobbyFurnitureObstacle> Active = new();

        private Vector2 center;
        private float radius = 0.5f;
        private bool hasBounds;

        private void OnEnable()
        {
            if (!Active.Contains(this))
                Active.Add(this);
            RefreshBounds();
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        public void RefreshBounds()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!initialized)
            {
                hasBounds = false;
                return;
            }

            center = new Vector2(bounds.center.x, bounds.center.z);
            radius = Mathf.Max(0.28f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            hasBounds = true;
        }

        public static bool IsBlocked(Vector3 worldPosition, float padding)
        {
            Vector2 point = new(worldPosition.x, worldPosition.z);
            for (int index = Active.Count - 1; index >= 0; index--)
            {
                MushLobbyFurnitureObstacle obstacle = Active[index];
                if (obstacle == null)
                {
                    Active.RemoveAt(index);
                    continue;
                }
                if (!obstacle.isActiveAndEnabled || !obstacle.hasBounds)
                    continue;
                float allowed = obstacle.radius + padding;
                if ((point - obstacle.center).sqrMagnitude < allowed * allowed)
                    return true;
            }
            return false;
        }

        public static Vector3 FindOpenDirection(
            Vector3 worldOrigin,
            Vector3 desiredWorldDirection,
            float lookAhead,
            float padding)
        {
            desiredWorldDirection = Vector3.ProjectOnPlane(desiredWorldDirection, Vector3.up);
            if (desiredWorldDirection.sqrMagnitude < 0.0001f)
                return Vector3.zero;
            desiredWorldDirection.Normalize();

            float[] angles = { 0f, 38f, -38f, 72f, -72f, 108f, -108f };
            foreach (float angle in angles)
            {
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * desiredWorldDirection;
                if (!IsBlocked(worldOrigin + direction * lookAhead, padding))
                    return direction;
            }
            return Vector3.zero;
        }
    }
}
