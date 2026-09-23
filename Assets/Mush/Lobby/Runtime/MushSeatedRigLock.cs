using UnityEngine;

namespace Mush.Lobby
{
    /// <summary>
    /// Keeps the XR Origin anchored to the seat while leaving head and hand
    /// tracking untouched inside the rig.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MushSeatedRigLock : MonoBehaviour
    {
        private const float PlayerRadius = 0.25f;
        private const float ArtificialPitchLimit = 55f;
        private static readonly Vector2 FallbackLobbyXBounds = new(-3.48f, 3.48f);
        private static readonly Vector2 FallbackLobbyZBounds = new(-5.52f, 2.28f);
        private readonly Collider[] collisionBuffer = new Collider[32];
        private readonly Collider[] currentCollisionBuffer = new Collider[32];

        private Vector3 lockedPosition;
        private Quaternion lockedRotation;
        private float lockedFloorHeight;
        private float artificialPitch;
        private Vector2 lobbyXBounds = FallbackLobbyXBounds;
        private Vector2 lobbyZBounds = FallbackLobbyZBounds;

        private void Awake()
        {
            lockedPosition = transform.position;
            lockedRotation = transform.rotation;
            lockedFloorHeight = transform.position.y;
            ResolveLobbyMovementBounds();
        }

        public void MoveSeat(Vector3 worldPosition, Quaternion worldRotation)
        {
            lockedPosition = worldPosition;
            lockedRotation = worldRotation;
            lockedFloorHeight = worldPosition.y;
            artificialPitch = 0f;
            transform.SetPositionAndRotation(lockedPosition, lockedRotation);
        }

        public bool TryMoveSeat(Vector3 worldDisplacement, Transform cameraTransform)
        {
            worldDisplacement = Vector3.ProjectOnPlane(worldDisplacement, Vector3.up);
            if (worldDisplacement.sqrMagnitude < 0.000001f)
                return false;

            Vector3 cameraPosition = cameraTransform != null
                ? cameraTransform.position
                : lockedPosition + Vector3.up * 1.55f;
            Vector3 requestedCameraPosition = cameraPosition + worldDisplacement;
            requestedCameraPosition.x = Mathf.Clamp(requestedCameraPosition.x, lobbyXBounds.x, lobbyXBounds.y);
            requestedCameraPosition.z = Mathf.Clamp(requestedCameraPosition.z, lobbyZBounds.x, lobbyZBounds.y);
            Vector3 clampedDisplacement = Vector3.ProjectOnPlane(requestedCameraPosition - cameraPosition, Vector3.up);

            // 한 축이 가구에 막혀도 다른 축은 이동시켜 벽을 따라 자연스럽게 미끄러지게 한다.
            bool moved = false;
            Vector3 xStep = new(clampedDisplacement.x, 0f, 0f);
            if (xStep.sqrMagnitude > 0.000001f && CanOccupy(cameraPosition, cameraPosition + xStep))
            {
                ApplyTranslation(xStep);
                cameraPosition += xStep;
                moved = true;
            }

            Vector3 zStep = new(0f, 0f, clampedDisplacement.z);
            if (zStep.sqrMagnitude > 0.000001f && CanOccupy(cameraPosition, cameraPosition + zStep))
            {
                ApplyTranslation(zStep);
                moved = true;
            }
            return moved;
        }

        public void RotateViewAroundCamera(Transform cameraTransform, Vector2 lookInput, float degreesThisFrame)
        {
            float yawDelta = lookInput.x * degreesThisFrame;
            float targetPitch = Mathf.Clamp(
                artificialPitch - lookInput.y * degreesThisFrame,
                -ArtificialPitchLimit,
                ArtificialPitchLimit);
            float pitchDelta = targetPitch - artificialPitch;
            if (Mathf.Abs(yawDelta) < 0.0001f && Mathf.Abs(pitchDelta) < 0.0001f)
                return;

            Vector3 pivot = cameraTransform != null ? cameraTransform.position : lockedPosition;
            Vector3 viewRight = cameraTransform != null ? cameraTransform.right : lockedRotation * Vector3.right;
            Quaternion yawRotation = Quaternion.AngleAxis(yawDelta, Vector3.up);
            Vector3 pitchedAxis = yawRotation * viewRight;
            Quaternion pitchRotation = Quaternion.AngleAxis(pitchDelta, pitchedAxis);
            Quaternion viewRotation = pitchRotation * yawRotation;

            lockedPosition = pivot + viewRotation * (lockedPosition - pivot);
            lockedRotation = viewRotation * lockedRotation;
            artificialPitch = targetPitch;
            transform.SetPositionAndRotation(lockedPosition, lockedRotation);
        }

        private void ApplyTranslation(Vector3 displacement)
        {
            lockedPosition += displacement;
            transform.SetPositionAndRotation(lockedPosition, lockedRotation);
        }

        private bool CanOccupy(Vector3 currentCameraWorldPosition, Vector3 targetCameraWorldPosition)
        {
            Vector3 currentFlatPosition = currentCameraWorldPosition;
            currentFlatPosition.y = lockedPosition.y;
            Vector3 targetFlatPosition = targetCameraWorldPosition;
            targetFlatPosition.y = lockedPosition.y;
            float furniturePadding = PlayerRadius + 0.05f;
            if (MushLobbyFurnitureObstacle.IsBlocked(targetFlatPosition, furniturePadding))
            {
                bool escapingExistingObstacle =
                    MushLobbyFurnitureObstacle.TryGetEscapeDirection(
                        currentFlatPosition,
                        furniturePadding,
                        out Vector3 escapeDirection,
                        out _) &&
                    Vector3.Dot(targetFlatPosition - currentFlatPosition, escapeDirection) > 0f;
                if (!escapingExistingObstacle)
                    return false;
            }

            int currentCount = OverlapPlayerCapsule(currentCameraWorldPosition, currentCollisionBuffer);
            int count = OverlapPlayerCapsule(targetCameraWorldPosition, collisionBuffer);
            for (int index = 0; index < count; index++)
            {
                Collider candidate = collisionBuffer[index];
                if (candidate == null || candidate.transform.IsChildOf(transform) ||
                    ContainsCollider(currentCollisionBuffer, currentCount, candidate) ||
                    IsWalkableFloorCollider(candidate))
                    continue; // 고정 좌석과 이미 겹친 상태라면 빠져나오는 이동까지 막지 않는다.
                return false;
            }
            return true;
        }

        private int OverlapPlayerCapsule(Vector3 cameraWorldPosition, Collider[] results)
        {
            Vector3 lower = new(cameraWorldPosition.x, lockedFloorHeight + 0.31f, cameraWorldPosition.z);
            Vector3 upper = new(cameraWorldPosition.x, lockedFloorHeight + 1.55f, cameraWorldPosition.z);
            return Physics.OverlapCapsuleNonAlloc(
                lower,
                upper,
                PlayerRadius,
                results,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
        }

        private static bool ContainsCollider(Collider[] colliders, int count, Collider target)
        {
            for (int index = 0; index < count; index++)
            {
                if (colliders[index] == target)
                    return true;
            }
            return false;
        }

        private static bool IsWalkableFloorCollider(Collider collider)
        {
            Transform current = collider.transform;
            while (current != null)
            {
                string objectName = current.name;
                if (objectName == "Floor" || objectName == "Ground" ||
                    objectName.StartsWith("Carpet_") || objectName == "PROP_CenterRug" ||
                    objectName == "ENV_FloorBase" || objectName.StartsWith("ENV_FloorPlank_") ||
                    objectName == "Cabin Floor Base" || objectName.StartsWith("Cabin Floor Plank "))
                    return true;
                current = current.parent;
            }
            return false;
        }

        private void ResolveLobbyMovementBounds()
        {
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || renderer.gameObject.name != "Floor")
                    continue;

                Bounds floorBounds = renderer.bounds;
                float padding = PlayerRadius + 0.05f;
                if (floorBounds.size.x <= padding * 2f || floorBounds.size.z <= padding * 2f)
                    continue;

                lobbyXBounds = new Vector2(floorBounds.min.x + padding, floorBounds.max.x - padding);
                lobbyZBounds = new Vector2(floorBounds.min.z + padding, floorBounds.max.z - padding);
                return;
            }
        }

        private void LateUpdate()
        {
            transform.SetPositionAndRotation(lockedPosition, lockedRotation);
        }
    }
}
