using System;
using Mush.Prototype;
using UnityEngine;

namespace Mush.Testing.TrackTest
{
    /// <summary>
    /// Track_v2-only experiment. The existing movement/route authority is moved
    /// to the front dogs; the rider follows a delayed heading at the authored
    /// spacing, while road contact is judged at the sled after its pose updates.
    /// Disable this component or its empty object to restore the rig.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    [AddComponentMenu("Mush/Test/Track Dog Led Sled Follow")]
    public sealed class TrackDogLedSledFollowTest : MonoBehaviour
    {
        private const string TrackScenePath = "Assets/Test/TrackTest/Track_v2_TrackTest.unity";

        [Header("주행 연결")]
        [SerializeField, InspectorName("주행 관리")]
        [Tooltip("Track_v2 씬에서 주행 상태와 강아지 팀을 관리하는 오브젝트를 연결합니다.")]
        private MushMapRideBootstrap ride;
        [SerializeField, InspectorName("썰매 조작")]
        [Tooltip("Track_v2 씬에서 이동과 좌우 회전을 담당하는 오브젝트를 연결합니다.")]
        private MushSledKeyboardController controller;
        [SerializeField, InspectorName("앞줄 강아지")]
        [Tooltip("썰매를 앞에서 이끄는 강아지들을 지정합니다. 목록을 비워 두면 가장 앞에 있는 활성 강아지들을 자동으로 선택합니다.")]
        private MushRideDog[] leadDogs;

        [Header("썰매 회전 따라가기")]
        [SerializeField, Range(0f, 0.5f), InspectorName("회전 지연 시간 (초)")]
        [Tooltip("강아지가 먼저 회전한 뒤 썰매가 같은 방향을 따라가는 시간입니다. 0이면 즉시 따라가고, 클수록 늦게 따라갑니다. 기본값은 0.2초입니다.")]
        private float delaySeconds = 0.2f;

        [Header("도로 경사 따라가기")]
        [SerializeField, Range(1f, 20f), InspectorName("경사 반응 속도")]
        [Tooltip("도로의 기울기가 바뀔 때 썰매가 따라 기울어지는 빠르기입니다. 작을수록 천천히 부드럽게, 클수록 빠르게 반응합니다.")]
        private float surfaceRotationResponse = 8f;

        [Header("회전 연출 공통 설정")]
        [SerializeField, Range(5f, 90f), InspectorName("최대 연출 기준 (도/초)")]
        [Tooltip("실제 회전 속도가 이 값에 도달하면 강아지와 고삐 연출이 최대가 됩니다. 낮출수록 완만한 회전에서도 연출이 커집니다. 기본값 34는 1초에 34도 회전하는 속도입니다.")]
        private float turnRateForFullEffect = 34f;
        [SerializeField, Range(0.03f, 0.8f), InspectorName("회전 반응 시간 (초)")]
        [Tooltip("회전이 강해질 때 강아지의 위치와 몸 방향, 고삐 모양이 바뀌는 반응 시간입니다. 작을수록 빠르게, 클수록 천천히 변합니다.")]
        private float dogRetreatSeconds = 0.18f;
        [SerializeField, Range(0.03f, 0.8f), InspectorName("직진 복귀 시간 (초)")]
        [Tooltip("회전이 약해지거나 직진할 때 강아지와 고삐가 원래 모습으로 돌아오는 반응 시간입니다. 작을수록 빠르게, 클수록 천천히 돌아옵니다.")]
        private float dogReturnSeconds = 0.28f;

        [Header("안쪽 강아지 뒤로 이동")]
        [SerializeField, InspectorName("뒤로 이동 사용")]
        [Tooltip("회전 안쪽 강아지가 바깥쪽 강아지보다 살짝 뒤로 가는 모습을 켭니다. 왼쪽 회전에서는 왼쪽 강아지가, 오른쪽 회전에서는 오른쪽 강아지가 뒤로 이동합니다.")]
        private bool enableDogRetreat = true;
        [SerializeField, Range(0f, 0.8f), InspectorName("최대 뒤로 이동 (미터)")]
        [Tooltip("회전 연출이 가장 강할 때 안쪽 강아지가 뒤로 이동하는 최대 거리입니다. 클수록 두 강아지의 앞뒤 차이가 커집니다. 기본값 0.2미터는 20센티미터입니다.")]
        private float maximumDogRetreat = 0.2f;

        [Header("강아지 방향과 위치")]
        [SerializeField, InspectorName("몸 돌리기와 옆 이동 사용")]
        [Tooltip("강아지 몸을 회전 방향으로 살짝 돌리고, 강아지들을 같은 방향으로 조금 옮기는 모습을 켭니다.")]
        private bool enableDogTurn = true;
        [SerializeField, Range(0f, 12f), InspectorName("최대 몸 회전 (도)")]
        [Tooltip("강아지 몸을 회전 방향으로 추가로 돌리는 최대 각도입니다. 클수록 몸을 더 많이 틀어 회전하는 느낌이 강해집니다. 기본값은 5도입니다.")]
        private float maximumDogYaw = 5f;
        [SerializeField, Range(0f, 0.3f), InspectorName("최대 옆 이동 (미터)")]
        [Tooltip("강아지들이 회전 방향으로 옆으로 이동하는 최대 거리입니다. 클수록 회전 안쪽으로 더 많이 이동합니다. 기본값 0.08미터는 8센티미터입니다.")]
        private float maximumDogLateralShift = 0.08f;

        [Header("고삐 모양")]
        [SerializeField, InspectorName("고삐 회전 연출 사용")]
        [Tooltip("회전할 때 고삐가 옆으로 살짝 휘고, 안쪽 줄은 팽팽하게, 바깥쪽 줄은 조금 느슨하게 보이도록 합니다.")]
        private bool enableReinTurn = true;
        [SerializeField, Range(0f, 0.3f), InspectorName("최대 옆 휘어짐 (미터)")]
        [Tooltip("회전할 때 고삐 중간이 회전 바깥쪽으로 휘는 최대 거리입니다. 클수록 곡선이 뚜렷해집니다. 기본값 0.08미터는 8센티미터입니다.")]
        private float maximumReinBend = 0.08f;
        [SerializeField, Range(0f, 0.2f), InspectorName("바깥쪽 추가 처짐 (미터)")]
        [Tooltip("회전 바깥쪽 고삐가 아래로 더 처지는 최대 거리입니다. 클수록 바깥쪽 줄이 느슨해 보이고 안쪽 줄과의 차이가 커집니다. 기본값 0.06미터는 6센티미터입니다.")]
        private float maximumReinSlack = 0.06f;

        private readonly TrackFollowPoseHistory history = new TrackFollowPoseHistory();
        private TrackDogControlPivot pivot;
        private TrackDogReinRetreat dogRetreat;
        private MushReinsVisual reinsVisual;
        private Transform motionRoot;
        private Transform seat;
        private MushCurvedMapRuntime course;
        private TrackTestAvoidance avoidance;
        private Vector3 seatOffset;
        private Vector3 lastLeaderPosition;
        private Quaternion lastLeaderRotation;
        private float effectiveDelay;
        private float turnPresentation;
        private float turnPresentationVelocity;
        private float observedTurnRate;
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
        public float TurnPresentationAmount => turnPresentation;
        public float TurnRateDegreesPerSecond => observedTurnRate;
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
            if (ride.HasFinished)
            {
                dogRetreat?.Reset();
                ResetTurnPresentation();
            }
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
                avoidance = motionRoot.GetComponent<TrackTestAvoidance>();
                seat = ride.RideViewAnchor;
                reinsVisual = seat.GetComponent<MushReinsVisual>();
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
                ResetTurnPresentation();
                reinsVisual = null;
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
            if (avoidance != null) targetPosition = avoidance.ConstrainFollowerPosition(seat.position, targetPosition);
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
            ride.RefreshRideCourseContact(this);
            UpdateTurnPresentation(leaderRotation, Time.deltaTime);
            if (enableDogRetreat || enableDogTurn)
                dogRetreat.Apply(turnPresentation, controller.RideStarted,
                    enableDogRetreat ? maximumDogRetreat : 0f,
                    enableDogTurn ? maximumDogLateralShift : 0f,
                    enableDogTurn ? maximumDogYaw : 0f,
                    dogRetreatSeconds, dogReturnSeconds, Time.deltaTime);
            else dogRetreat.Reset();
            UpdateReinTurnPresentation();
            pivot.UpdateTowLines();
            lastLeaderPosition = motionRoot.position;
            lastLeaderRotation = leaderRotation;
        }

        private void UpdateTurnPresentation(Quaternion leaderRotation, float deltaTime)
        {
            bool moving = controller.RideStarted && controller.CurrentSpeed > 0.1f;
            observedTurnRate = moving && deltaTime > 0.00001f
                ? Vector3.SignedAngle(lastLeaderRotation * Vector3.forward,
                    leaderRotation * Vector3.forward, Vector3.up) / deltaTime : 0f;
            float target = Mathf.Clamp(observedTurnRate / turnRateForFullEffect, -1f, 1f);
            float response = Mathf.Abs(target) > Mathf.Abs(turnPresentation)
                ? dogRetreatSeconds : dogReturnSeconds;
            turnPresentation = Mathf.SmoothDamp(turnPresentation, target,
                ref turnPresentationVelocity, response, Mathf.Infinity, deltaTime);
            if (target == 0f && Mathf.Abs(turnPresentation) < 0.0005f &&
                Mathf.Abs(turnPresentationVelocity) < 0.005f)
                turnPresentation = turnPresentationVelocity = 0f;
        }

        private void UpdateReinTurnPresentation()
        {
            if (reinsVisual == null) return;
            float amount = enableReinTurn ? turnPresentation : 0f;
            float leftInside = Mathf.Clamp01(-amount);
            float rightInside = Mathf.Clamp01(amount);
            // A small outward bow suggests inertia; the pulled rein stays tighter.
            Vector3 bend = -motionRoot.right * (amount * maximumReinBend);
            float slack = Mathf.Abs(amount) * maximumReinSlack;
            reinsVisual.SetTurnPresentation(
                bend * Mathf.Lerp(1f, 0.45f, leftInside),
                bend * Mathf.Lerp(1f, 0.45f, rightInside),
                slack * Mathf.Lerp(1f, 0.15f, leftInside),
                slack * Mathf.Lerp(1f, 0.15f, rightInside));
        }

        private void ResetTurnPresentation()
        {
            turnPresentation = turnPresentationVelocity = observedTurnRate = 0f;
            reinsVisual?.ClearTurnPresentation();
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
            ResetTurnPresentation();
        }

        private void OnMotionReset()
        {
            resetHistory = true;
            dogRetreat?.Reset();
            ResetTurnPresentation();
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
                ResetTurnPresentation();
                reinsVisual = null;
                pivot = null;
                initialized = false;
                surfaceNormalInitialized = false;
            }
        }

        private void OnValidate()
        {
            DelaySeconds = delaySeconds;
            surfaceRotationResponse = float.IsFinite(surfaceRotationResponse) ? Mathf.Clamp(surfaceRotationResponse, 1f, 20f) : 8f;
            maximumDogRetreat = float.IsFinite(maximumDogRetreat) ? Mathf.Clamp(maximumDogRetreat, 0f, 0.8f) : 0.2f;
            dogRetreatSeconds = float.IsFinite(dogRetreatSeconds) ? Mathf.Clamp(dogRetreatSeconds, 0.03f, 0.8f) : 0.18f;
            dogReturnSeconds = float.IsFinite(dogReturnSeconds) ? Mathf.Clamp(dogReturnSeconds, 0.03f, 0.8f) : 0.28f;
            maximumDogYaw = float.IsFinite(maximumDogYaw) ? Mathf.Clamp(maximumDogYaw, 0f, 12f) : 5f;
            maximumDogLateralShift = float.IsFinite(maximumDogLateralShift) ? Mathf.Clamp(maximumDogLateralShift, 0f, 0.3f) : 0.08f;
            turnRateForFullEffect = float.IsFinite(turnRateForFullEffect) ? Mathf.Clamp(turnRateForFullEffect, 5f, 90f) : 34f;
            maximumReinBend = float.IsFinite(maximumReinBend) ? Mathf.Clamp(maximumReinBend, 0f, 0.3f) : 0.08f;
            maximumReinSlack = float.IsFinite(maximumReinSlack) ? Mathf.Clamp(maximumReinSlack, 0f, 0.2f) : 0.06f;
        }

        private void Warn(string message)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[TrackDogLedSledFollowTest] " + message, this);
        }
    }
}
