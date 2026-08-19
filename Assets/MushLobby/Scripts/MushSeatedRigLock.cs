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
        private static readonly Vector2 LobbyXBounds = new(-3.48f, 3.48f);
        private static readonly Vector2 LobbyZBounds = new(-5.52f, 2.28f);
        private readonly Collider[] collisionBuffer = new Collider[32];
        private readonly Collider[] currentCollisionBuffer = new Collider[32];

        private Vector3 lockedPosition;
        private Quaternion lockedRotation;

        private void Awake()
        {
            lockedPosition = transform.position;
            lockedRotation = transform.rotation;
        }

        public void MoveSeat(Vector3 worldPosition, Quaternion worldRotation)
        {
            lockedPosition = worldPosition;
            lockedRotation = worldRotation;
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
            requestedCameraPosition.x = Mathf.Clamp(requestedCameraPosition.x, LobbyXBounds.x, LobbyXBounds.y);
            requestedCameraPosition.z = Mathf.Clamp(requestedCameraPosition.z, LobbyZBounds.x, LobbyZBounds.y);
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

        public void SnapTurnAroundCamera(Transform cameraTransform, float degrees)
        {
            Quaternion turn = Quaternion.AngleAxis(degrees, Vector3.up);
            if (cameraTransform == null)
            {
                lockedRotation = turn * lockedRotation;
                transform.SetPositionAndRotation(lockedPosition, lockedRotation);
                return;
            }

            // 헤드셋의 현재 월드 위치를 회전 중심으로 삼아 스냅 회전 때 좌석이 원을 그리며 밀리지 않게 한다.
            Vector3 pivot = cameraTransform.position;
            Vector3 fromPivot = lockedPosition - pivot;
            Vector3 rotatedPosition = pivot + turn * fromPivot;
            rotatedPosition.y = lockedPosition.y;
            lockedPosition = rotatedPosition;
            lockedRotation = turn * lockedRotation;
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
                    ContainsCollider(currentCollisionBuffer, currentCount, candidate))
                    continue; // 고정 좌석과 이미 겹친 상태라면 빠져나오는 이동까지 막지 않는다.
                return false;
            }
            return true;
        }

        private int OverlapPlayerCapsule(Vector3 cameraWorldPosition, Collider[] results)
        {
            Vector3 lower = new(cameraWorldPosition.x, lockedPosition.y + 0.31f, cameraWorldPosition.z);
            Vector3 upper = new(cameraWorldPosition.x, lockedPosition.y + 1.55f, cameraWorldPosition.z);
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

        private void LateUpdate()
        {
            transform.SetPositionAndRotation(lockedPosition, lockedRotation);
        }
    }
}
