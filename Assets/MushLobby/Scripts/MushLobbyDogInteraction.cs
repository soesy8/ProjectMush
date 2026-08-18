using UnityEngine;
using UnityEngine.XR;
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
            petRadius = Mathf.Clamp(petRadius, 0.18f, 0.24f); // 손바닥이 실제 머리 가까이에 들어와야 하도록 기존 42cm 원거리 판정을 줄인다.
            minimumStrokeSpeed = Mathf.Clamp(minimumStrokeSpeed, 0.12f, 0.50f); // 추적 미세 떨림은 쓰다듬기로 세지 않고 의도적인 손 움직임만 받는다.
            if (expression == null)
                expression = GetComponent<MushLobbyDogExpression>();
            XRSimpleInteractable rayInteractable = GetComponent<XRSimpleInteractable>();
            if (rayInteractable != null)
                rayInteractable.enabled = false; // 개만 VR 레이 선택 대상에서 제외한다. 지도·상점·하우징 레이는 그대로 유지된다.
            CaptureHandPositions();
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

            if (head == null || roamer == null || roamer.IsMoving || roamer.IsFetching || roamer.IsFeeding)
            {
                CaptureHandPositions();
                return;
            }

            bool near = CheckHand(leftHand, ref previousLeftPosition) |
                        CheckHand(rightHand, ref previousRightPosition);
            if (near && !handWasNear && !roamer.IsRestingAtFireplace && !roamer.IsOnLap)
                roamer.PlayHeadTilt();
            handWasNear = near;
            positionsReady = true;
        }

        public void Pet()
        {
            if (petCooldown > 0f || roamer == null || roamer.IsFetching || roamer.IsFeeding)
                return;

            petCooldown = 0.45f;
            roamer.MarkPetted();
            bool calmPetPose = roamer.IsRestingAtFireplace || roamer.IsOnLap;
            petCount++;
            if (petCount >= 3)
            {
                petCount = 0;
                if (calmPetPose)
                    roamer.PlayRestingPet(); // 벽난로 바닥이나 무릎에서는 일어나 뛰지 않고 현재 자세로 하트만 보여 준다.
                else
                    roamer.Celebrate();
                expression?.ShowLoveCelebration();
            }
            else
            {
                if (calmPetPose)
                    roamer.PlayRestingPet(); // 벽난로 수면/무릎 앉기 자세를 깨지 않고 꼬리만 흔든다.
                else
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
    }
}
