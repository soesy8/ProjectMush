using System;
using Mush.Prototype;
using UnityEngine;

namespace Mush.Testing
{
    /// <summary>
    /// Track_v2-only experiment. The existing movement/route authority is moved
    /// to the front dogs; the rider follows a delayed heading at the authored
    /// spacing. Disable this component or its empty object to restore the rig.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    [AddComponentMenu("Mush/Test/Track Dog Led Sled Follow")]
    public sealed class TrackDogLedSledFollowTest : MonoBehaviour
    {
        private const string TrackScenePath = "Assets/Scenes/Track_v2.unity";

        [Header("Track_v2 주행 연결")]
        [SerializeField, InspectorName("주행 상태")] private MushMapRideBootstrap ride;
        [SerializeField, InspectorName("기존 주행 컨트롤러")] private MushSledKeyboardController controller;
        [SerializeField, InspectorName("조작 기준 강아지")]
        [Tooltip("앞줄 강아지들을 지정합니다. 비워 두면 가장 앞에 배치된 활성 강아지들을 자동으로 사용합니다.")]
        private MushRideDog[] leadDogs;

        [Header("추종 실험")]
        [SerializeField, Range(0f, 0.5f), InspectorName("회전 추종 딜레이 (초)")]
        [Tooltip("기본 0.2초. 플레이 중 슬라이더 또는 숫자 입력으로 조절합니다.\n강아지의 회전 방향을 이 시간만큼 늦춰 썰매와 플레이어에 반영합니다.\n직선 주행 간격은 원래 배치를 유지하며, 카메라에 추가 지연을 주지 않습니다.")]
        private float delaySeconds = 0.2f;

        [Header("노면 경사 추종")]
        [SerializeField, Range(1f, 20f), InspectorName("경사 추종 반응 (초당)")]
        [Tooltip("도로 면 경계의 기울기 변화를 부드럽게 반영합니다. 작을수록 부드럽고, 클수록 빠르게 반응합니다.\n조향 딜레이와 주행 간격에는 영향을 주지 않습니다.")]
        private float surfaceRotationResponse = 8f;

        [Header("회전 안쪽 강아지 연출")]
        [SerializeField, InspectorName("강아지 뒤처짐 연출")] private bool enableDogRetreat = true;
        [SerializeField, Range(0f, 0.8f), InspectorName("최대 뒤처짐 거리 (m)")]
        [Tooltip("조향을 끝까지 당겼을 때 회전 안쪽 강아지가 상대적으로 뒤로 이동하는 거리입니다. 실제 주행 속도와 판정에는 영향을 주지 않습니다.")]
        private float maximumDogRetreat = 0.3f;
        [SerializeField, Range(0.03f, 0.8f), InspectorName("뒤처짐 반응 시간 (초)")]
        private float dogRetreatSeconds = 0.18f;
        [SerializeField, Range(0.03f, 0.8f), InspectorName("대형 복귀 시간 (초)")]
        private float dogReturnSeconds = 0.28f;

        private readonly TrackFollowPoseHistory history = new TrackFollowPoseHistory();
        private TrackDogControlPivot pivot;
        private TrackDogReinRetreat dogRetreat;
        private Transform motionRoot;
        private Transform seat;
        private MushCurvedMapRuntime course;
        private Vector3 seatOffset;
        private Vector3 lastLeaderPosition;
        private Quaternion lastLeaderRotation;
        private float effectiveDelay;
        private Vector3 smoothedSurfaceNormal;
        private bool surfaceNormalInitialized;
        private bool initialized;
        private bool resetHistory;
        private bool warned;

        public bool ExperimentActive => initialized && isActiveAndEnabled;
        public Transform LeaderRoot => motionRoot;
        public Transform SledAnchor => seat;
        public float FollowDistance => motionRoot != null
            ? Vector3.ProjectOnPlane(Vector3.Scale(seatOffset, motionRoot.lossyScale), Vector3.up).magnitude : 0f;
        public float EffectiveDelaySeconds => effectiveDelay;
        public float DelaySeconds
        {
            get => delaySeconds;
            set => delaySeconds = float.IsFinite(value) ? Mathf.Clamp(value, 0f, 0.5f) : 0.2f;
        }

        private void OnEnable()
        {
            warned = false;
            if (Application.isPlaying) TryBegin();
        }

        private void Start() => TryBegin();
        private void Update()
        {
            if (!initialized) TryBegin();
            if (!initialized || ride == null || ride.IsPaused || Time.timeScale <= 0f) return;
            if (ride.HasFinished) dogRetreat?.Reset();
            else dogRetreat?.RemovePreviousOffsets();
        }

        private void TryBegin()
        {
            if (!Application.isPlaying || initialized || warned) return;
            if (gameObject.scene.path != TrackScenePath)
            {
                Warn("This temporary experiment only runs in Track_v2.");
                return;
            }
            if (ride == null || controller == null || ride.gameObject.scene != gameObject.scene ||
                controller.gameObject.scene != gameObject.scene)
            {
                Warn("Assign the Track_v2 ride and controller to the test object.");
                return;
            }
            try
            {
                if (!TrackDogControlPivot.TryCreate(ride, controller, out pivot)) return;
                if (!ride.TryClaimRideViewPose(this))
                    throw new InvalidOperationException("Another component already controls the ride view pose.");
                motionRoot = controller.transform;
                seat = ride.RideViewAnchor;
                course = ride.GetComponent<MushCurvedMapRuntime>();
                Vector3 dogOffset = FindLeaderOffset();
                seatOffset = pivot.Apply(dogOffset);
                dogRetreat = new TrackDogReinRetreat(motionRoot, course);
                controller.MotionReset += OnMotionReset;
                initialized = true;
                effectiveDelay = delaySeconds;
                ResetHistory();
            }
            catch (Exception exception)
            {
                controller.MotionReset -= OnMotionReset;
                dogRetreat?.Reset();
                dogRetreat = null;
                pivot?.Restore();
                pivot = null;
                ride.ReleaseRideViewPose(this);
                initialized = false;
                Warn(exception.Message);
            }
        }

        private Vector3 FindLeaderOffset()
        {
            MushRideDog[] candidates = leadDogs;
            bool autoSelect = candidates == null || candidates.Length == 0;
            if (autoSelect) candidates = motionRoot.GetComponentsInChildren<MushRideDog>(false);
            float front = float.NegativeInfinity;
            foreach (MushRideDog dog in candidates)
                if (ValidDog(dog)) front = Mathf.Max(front, motionRoot.InverseTransformPoint(dog.transform.position).z);
            Vector3 total = Vector3.zero;
            int count = 0;
            foreach (MushRideDog dog in candidates)
            {
                if (!ValidDog(dog)) continue;
                Vector3 local = motionRoot.InverseTransformPoint(dog.transform.position);
                if (autoSelect && front - local.z > 0.15f) continue;
                total += local;
                count++;
            }
            if (count == 0) throw new InvalidOperationException("Assign at least one active dog from the ride team.");
            Vector3 offset = total / count;
            offset.y = 0f;
            if (offset.z <= 0.1f) throw new InvalidOperationException("The control dogs must be ahead of the sled.");
            return offset;
        }

        private bool ValidDog(MushRideDog dog) => dog != null && dog.isActiveAndEnabled &&
            dog.transform.IsChildOf(motionRoot);

        private void LateUpdate()
        {
            if (!initialized || motionRoot == null || seat == null || ride == null ||
                ride.IsPaused || ride.HasFinished || Time.timeScale <= 0f) return;

            Quaternion leaderRotation = TrackDogControlPivot.Upright(motionRoot.rotation);
            float plausibleStep = Mathf.Max(3f, controller.CurrentSpeed * Time.deltaTime * 4f + 0.5f);
            if (resetHistory || !controller.RideStarted ||
                Vector3.Distance(lastLeaderPosition, motionRoot.position) > plausibleStep ||
                Quaternion.Angle(lastLeaderRotation, leaderRotation) > 60f)
                ResetHistory();

            history.Record(Time.timeAsDouble, motionRoot.position, leaderRotation);
            // Ease live slider changes without adding a second camera/follow lag.
            effectiveDelay = Mathf.MoveTowards(effectiveDelay, delaySeconds, Time.deltaTime);
            Pose delayed = history.Evaluate(Time.timeAsDouble - effectiveDelay);
            Vector3 targetPosition = motionRoot.position + delayed.rotation * Vector3.Scale(seatOffset, motionRoot.lossyScale);
            Quaternion targetRotation = delayed.rotation;
            Vector3 targetNormal = Vector3.up;
            if (course != null && course.TryGetCourseSurface(targetPosition, out Vector3 surface,
                    out Vector3 normal, out _, out _))
            {
                targetPosition.y = surface.y + controller.RideHeight + seatOffset.y * motionRoot.lossyScale.y;
                targetNormal = normal;
            }
            Vector3 surfaceNormal = SmoothSurfaceNormal(targetNormal, Time.deltaTime);
            // Smooth only the terrain tilt; keep the delayed steering heading intact.
            Vector3 slopeForward = Vector3.ProjectOnPlane(delayed.rotation * Vector3.forward, surfaceNormal);
            if (slopeForward.sqrMagnitude > 0.0001f)
                targetRotation = Quaternion.LookRotation(slopeForward.normalized, surfaceNormal);
            seat.SetPositionAndRotation(targetPosition, targetRotation);
            if (enableDogRetreat)
                dogRetreat.Apply(controller.CurrentSteering, controller.RideStarted,
                    maximumDogRetreat, dogRetreatSeconds, dogReturnSeconds, Time.deltaTime);
            else dogRetreat.Reset();
            pivot.UpdateTowLines();
            lastLeaderPosition = motionRoot.position;
            lastLeaderRotation = leaderRotation;
        }

        private Vector3 SmoothSurfaceNormal(Vector3 targetNormal, float deltaTime)
        {
            if (!surfaceNormalInitialized)
            {
                smoothedSurfaceNormal = targetNormal;
                surfaceNormalInitialized = true;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-surfaceRotationResponse * deltaTime);
                smoothedSurfaceNormal = Vector3.Slerp(smoothedSurfaceNormal, targetNormal, blend).normalized;
            }
            return smoothedSurfaceNormal;
        }

        private void ResetHistory()
        {
            Quaternion rotation = TrackDogControlPivot.Upright(motionRoot.rotation);
            history.Reset(Time.timeAsDouble, motionRoot.position, rotation);
            lastLeaderPosition = motionRoot.position;
            lastLeaderRotation = rotation;
            resetHistory = false;
            // Spawn, recovery and teleports start from their new ground pose.
            surfaceNormalInitialized = false;
            dogRetreat?.Reset();
        }

        private void OnMotionReset()
        {
            resetHistory = true;
            dogRetreat?.Reset();
        }

        private void OnDisable()
        {
            if (controller != null) controller.MotionReset -= OnMotionReset;
            dogRetreat?.Reset();
            dogRetreat = null;
            try
            {
                pivot?.Restore();
            }
            finally
            {
                if (ride != null) ride.ReleaseRideViewPose(this);
                pivot = null;
                initialized = false;
                surfaceNormalInitialized = false;
            }
        }

        private void OnValidate()
        {
            DelaySeconds = delaySeconds;
            surfaceRotationResponse = float.IsFinite(surfaceRotationResponse) ? Mathf.Clamp(surfaceRotationResponse, 1f, 20f) : 8f;
            maximumDogRetreat = float.IsFinite(maximumDogRetreat) ? Mathf.Clamp(maximumDogRetreat, 0f, 0.8f) : 0.3f;
            dogRetreatSeconds = float.IsFinite(dogRetreatSeconds) ? Mathf.Clamp(dogRetreatSeconds, 0.03f, 0.8f) : 0.18f;
            dogReturnSeconds = float.IsFinite(dogReturnSeconds) ? Mathf.Clamp(dogReturnSeconds, 0.03f, 0.8f) : 0.28f;
        }

        private void Warn(string message)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[TrackDogLedSledFollowTest] " + message, this);
        }
    }
}
