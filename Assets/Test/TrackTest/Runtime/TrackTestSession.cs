using Mush.Prototype;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mush.Testing.TrackTest
{
    /// <summary>Keeps this temporary scene out of normal completion/save flows.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class TrackTestSession : MonoBehaviour
    {
        [SerializeField] private MushMapRideBootstrap ride;
        [SerializeField] private MushSledKeyboardController controller;
        [SerializeField] private TrackDogLedSledFollowTest follower;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private bool captured;

        private void Update()
        {
            if (controller == null || ride == null || follower == null || !follower.ExperimentActive) return;
            if (!captured)
            {
                startPosition = controller.transform.position;
                startRotation = controller.transform.rotation;
                captured = true;
            }
            if (Keyboard.current?.f10Key.wasPressedThisFrame == true || ride.RouteProgress >= 0.96f)
                Restart();
        }

        public void Restart()
        {
            if (!captured) return;
            controller.ResetMotionForCourseRecovery();
            controller.transform.SetPositionAndRotation(startPosition, startRotation);
        }
    }
}
