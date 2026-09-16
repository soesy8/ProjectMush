using UnityEngine;

namespace Mush.Customization
{
    /// <summary>
    /// Keeps an equipped item on an animated dog part without inheriting the
    /// mesh object's non-uniform scale. This is important for lobby head tilts.
    /// The dedicated MonoScript asset lets editor-baked accessories retain a
    /// stable script GUID when they are serialized into gameplay scenes.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class MushDogAccessoryFollower : MonoBehaviour
    {
        private Transform trackedPart;
        private Vector3 trackedLocalPosition;
        private Quaternion trackedLocalRotation;

        public void Configure(Transform part)
        {
            trackedPart = part;
            trackedLocalPosition = part.InverseTransformPoint(transform.position);
            trackedLocalRotation = Quaternion.Inverse(part.rotation) * transform.rotation;
        }

        private void LateUpdate()
        {
            if (trackedPart == null)
                return;

            transform.SetPositionAndRotation(
                trackedPart.TransformPoint(trackedLocalPosition),
                trackedPart.rotation * trackedLocalRotation);
        }
    }
}
