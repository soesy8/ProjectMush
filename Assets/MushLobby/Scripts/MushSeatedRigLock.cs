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

        private void LateUpdate()
        {
            transform.SetPositionAndRotation(lockedPosition, lockedRotation);
        }
    }
}
