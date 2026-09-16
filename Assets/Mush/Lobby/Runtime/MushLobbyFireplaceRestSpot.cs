using System.Collections.Generic;
using UnityEngine;

namespace Mush.Lobby
{
    /// <summary>
    /// Provides two floor-rest reservations in front of the lobby fireplace.
    /// Dogs share the existing bed sleep transition, but do not overlap each other.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MushLobbyFireplaceRestSpot : MonoBehaviour
    {
        private const string FireplaceRootName = "PROP_FireplaceRoot";
        private const int SlotCount = 2;
        private static readonly List<MushLobbyFireplaceRestSpot> ActiveSpots = new();

        [SerializeField] private float sideOffset = 0.68f; // 의자가 없을 때도 좌석 정중앙은 비워 두는 최소 좌우 거리다.
        [SerializeField] private float distanceFromFireplace = 0.95f; // 화로 받침과 겹치지 않으면서 바로 앞 바닥에 눕는 거리다.
        [SerializeField] private float approachDistance = 0.72f;
        [SerializeField] private float approachSideOffset = 0.34f; // 개가 좌석 정중앙을 관통하지 않고 자기 쪽 가장자리로 드나가게 한다.
        [SerializeField] private float chairClearance = 0.38f; // 장착한 의자의 실제 폭 바깥에 개 몸통이 놓이도록 추가하는 여유다.

        private readonly MushLobbyDogRoamer[] reservedBy = new MushLobbyDogRoamer[SlotCount];
        private Transform lobbyRoot;
        private Transform fireplace;

        public float SurfaceY => fireplace != null ? fireplace.position.y : transform.position.y;

        public static MushLobbyFireplaceRestSpot Install(Transform newLobbyRoot)
        {
            if (newLobbyRoot == null)
                return null;

            Transform fireplaceTransform = FindDescendant(newLobbyRoot, FireplaceRootName);
            if (fireplaceTransform == null)
                return null;

            MushLobbyFireplaceRestSpot spot = newLobbyRoot.GetComponent<MushLobbyFireplaceRestSpot>();
            if (spot == null)
                spot = newLobbyRoot.gameObject.AddComponent<MushLobbyFireplaceRestSpot>();
            spot.lobbyRoot = newLobbyRoot;
            spot.fireplace = fireplaceTransform;
            spot.Register();
            return spot;
        }

        private void OnEnable()
        {
            Register();
        }

        private void OnDisable()
        {
            ActiveSpots.Remove(this);
            for (int index = 0; index < reservedBy.Length; index++)
                reservedBy[index] = null;
        }

        private void Register()
        {
            if (!ActiveSpots.Contains(this))
                ActiveSpots.Add(this);
        }

        public static bool TryReserveRandom(
            MushLobbyDogRoamer dog,
            out MushLobbyFireplaceRestSpot spot,
            out int slotIndex,
            out Vector3 approachWorld,
            out Vector3 sleepWorld,
            out Quaternion sleepRotation)
        {
            spot = null;
            slotIndex = -1;
            approachWorld = Vector3.zero;
            sleepWorld = Vector3.zero;
            sleepRotation = Quaternion.identity;
            if (dog == null)
                return false;

            float bestDistance = float.PositiveInfinity;
            MushLobbyFireplaceRestSpot bestSpot = null;
            int bestSlot = -1;
            for (int spotIndex = ActiveSpots.Count - 1; spotIndex >= 0; spotIndex--)
            {
                MushLobbyFireplaceRestSpot candidate = ActiveSpots[spotIndex];
                if (candidate == null)
                {
                    ActiveSpots.RemoveAt(spotIndex);
                    continue;
                }
                if (!candidate.isActiveAndEnabled || candidate.lobbyRoot == null || candidate.fireplace == null)
                    continue;

                int firstSlot = Random.Range(0, SlotCount);
                for (int offset = 0; offset < SlotCount; offset++)
                {
                    int candidateSlot = (firstSlot + offset) % SlotCount;
                    if (candidate.reservedBy[candidateSlot] != null)
                        continue;

                    candidate.GetPoints(candidateSlot, out Vector3 candidateApproach, out Vector3 candidateSleep, out _);
                    float distance = (candidateApproach - dog.transform.position).sqrMagnitude;
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    bestSpot = candidate;
                    bestSlot = candidateSlot;
                }
            }

            if (bestSpot == null || bestSlot < 0)
                return false;

            bestSpot.reservedBy[bestSlot] = dog;
            bestSpot.GetPoints(bestSlot, out approachWorld, out sleepWorld, out sleepRotation);
            spot = bestSpot;
            slotIndex = bestSlot;
            return true;
        }

        public bool IsReservedBy(MushLobbyDogRoamer dog, int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < reservedBy.Length && reservedBy[slotIndex] == dog;
        }

        public void Release(MushLobbyDogRoamer dog, int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < reservedBy.Length && reservedBy[slotIndex] == dog)
                reservedBy[slotIndex] = null;
        }

        private void GetPoints(int slotIndex, out Vector3 approachWorld, out Vector3 sleepWorld, out Quaternion sleepRotation)
        {
            Vector3 roomForward = Vector3.ProjectOnPlane(lobbyRoot.forward, Vector3.up).normalized;
            if (roomForward.sqrMagnitude < 0.0001f)
                roomForward = Vector3.forward;
            Vector3 roomRight = Vector3.Cross(Vector3.up, roomForward).normalized;
            Vector3 fireplaceFloor = new(fireplace.position.x, SurfaceY, fireplace.position.z);
            float resolvedSideOffset = ResolveSideOffset(roomRight, fireplaceFloor);
            float signedSideOffset = slotIndex == 0 ? -resolvedSideOffset : resolvedSideOffset;

            sleepWorld = fireplaceFloor + roomForward * distanceFromFireplace + roomRight * signedSideOffset;
            approachWorld = sleepWorld + roomForward * approachDistance +
                            roomRight * (slotIndex == 0 ? -approachSideOffset : approachSideOffset);
            sleepRotation = Quaternion.LookRotation(roomForward, Vector3.up);
        }

        private float ResolveSideOffset(Vector3 roomRight, Vector3 fireplaceFloor)
        {
            Transform chair = FindDescendant(lobbyRoot, "Placed Housing Chair");
            if (chair == null || !chair.gameObject.activeInHierarchy)
                return sideOffset;

            Renderer[] renderers = chair.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds chairBounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!hasBounds)
                {
                    chairBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    chairBounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
                return sideOffset;

            Vector3 absoluteRight = new(Mathf.Abs(roomRight.x), Mathf.Abs(roomRight.y), Mathf.Abs(roomRight.z));
            float chairHalfWidth = Vector3.Dot(chairBounds.extents, absoluteRight);
            float chairCenterOffset = Mathf.Abs(Vector3.Dot(chairBounds.center - fireplaceFloor, roomRight));
            return Mathf.Max(sideOffset, chairCenterOffset + chairHalfWidth + chairClearance);
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == objectName)
                    return candidate;
            }
            return null;
        }
    }
}
