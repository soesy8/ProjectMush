using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyDogInteraction : MonoBehaviour
    {
        [SerializeField] private MushLobbyDogRoamer roamer;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private MushLobbyDogExpression expression;
        [SerializeField] private float petRadius = 0.30f;
        [SerializeField] private float minimumStrokeSpeed = 0.22f;

        private XRSimpleInteractable interactable;
        private Vector3 previousLeftPosition;
        private Vector3 previousRightPosition;
        private float petCooldown;
        private int petCount;
        private bool positionsReady;
        private bool handWasNear;

        public void Configure(
            MushLobbyDogRoamer newRoamer,
            Transform newHead,
            Transform newLeftHand,
            Transform newRightHand)
        {
            roamer = newRoamer;
            head = newHead;
            leftHand = newLeftHand;
            rightHand = newRightHand;
            expression = GetComponent<MushLobbyDogExpression>();
        }

        public void ConfigureDogParts(
            MushLobbyDogRoamer newRoamer,
            Transform newHead,
            MushLobbyDogExpression newExpression)
        {
            roamer = newRoamer;
            head = newHead;
            expression = newExpression;
            CaptureHandPositions();
        }

        private void Awake()
        {
            petRadius = Mathf.Max(petRadius, 0.42f);
            minimumStrokeSpeed = Mathf.Min(minimumStrokeSpeed, 0.12f);
            if (expression == null)
                expression = GetComponent<MushLobbyDogExpression>();
            interactable = GetComponent<XRSimpleInteractable>();
            if (interactable != null)
                interactable.selectEntered.AddListener(OnSelected);
            CaptureHandPositions();
        }

        private void OnDestroy()
        {
            if (interactable != null)
                interactable.selectEntered.RemoveListener(OnSelected);
        }

        private void Update()
        {
            petCooldown -= Time.deltaTime;

            // Desktop preview hands follow the camera/mouse and are not physical
            // tracked hands. They must never trigger proximity petting.
            if (!XRSettings.isDeviceActive)
            {
                handWasNear = false;
                CaptureHandPositions();
                return;
            }

            if (head == null || roamer == null || roamer.IsMoving)
            {
                CaptureHandPositions();
                return;
            }

            bool near = CheckHand(leftHand, ref previousLeftPosition) |
                        CheckHand(rightHand, ref previousRightPosition);
            if (near && !handWasNear)
            {
                roamer.PlayHeadTilt();
                Pet();
            }
            handWasNear = near;
            positionsReady = true;
        }

        public void Pet()
        {
            if (petCooldown > 0f || roamer == null)
                return;

            petCooldown = 0.45f;
            roamer.MarkPetted();
            petCount++;
            if (petCount >= 3)
            {
                petCount = 0;
                roamer.Celebrate();
                expression?.ShowLoveCelebration();
            }
            else
            {
                roamer.PlayPet();
                expression?.ShowPetEnjoyment();
            }
        }

        private bool CheckHand(Transform hand, ref Vector3 previousPosition)
        {
            if (hand == null)
                return false;

            Vector3 current = hand.position;
            float speed = positionsReady && Time.deltaTime > 0f
                ? Vector3.Distance(current, previousPosition) / Time.deltaTime
                : 0f;
            previousPosition = current;
            bool near = Vector3.Distance(current, head.position) <= petRadius;
            if (near && speed >= minimumStrokeSpeed)
                Pet();
            return near;
        }

        private void CaptureHandPositions()
        {
            if (leftHand != null)
                previousLeftPosition = leftHand.position;
            if (rightHand != null)
                previousRightPosition = rightHand.position;
            positionsReady = true;
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            Pet();
        }
    }
}
